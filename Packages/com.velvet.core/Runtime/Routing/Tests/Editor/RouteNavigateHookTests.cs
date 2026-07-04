// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the imperative routing hooks captured from a mounted component: <c>UseNavigate</c> /
    /// <c>UseMatch</c> / <c>UseSearchParams</c>.
    /// <list type="bullet">
    /// <item><c>UseNavigate</c> returns a function that navigates through the active router.</item>
    /// <item><c>UseMatch</c> returns a match (with captured params) when its location-relative pattern matches
    /// the current location, matching case-insensitively by default and independently of the route table, and
    /// returns null when the pattern does not match.</item>
    /// <item><c>UseSearchParams</c> parses the query string of the current location.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteNavigateHookTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static class Capture
        {
            public static Func<string, UniTask<NavigationResult>>? Navigate;
            public static RouteMatch? Match;
            public static ISearchParams? SearchParams;
            public static SearchParamsSetter? SetSearchParams;

            public static string MatchPattern = "users/:id";

            public static void Reset()
            {
                Navigate = null;
                Match = null;
                SearchParams = null;
                SetSearchParams = null;
                MatchPattern = "users/:id";
            }

            [Component]
            public static VNode Render()
            {
                Navigate = Hooks.UseNavigate();
                Match = Hooks.UseMatch(MatchPattern);
                (SearchParams, SetSearchParams) = Hooks.UseSearchParams();
                return V.Label(text: "capture");
            }
        }

        private MountedTree MountAt(Router router)
            => V.Mount(_root,
                V.Provider(RouterContext.Location, router.CurrentLocation,
                    children: new VNode[] { V.Component(Capture.Render, key: "cap") }));

        [Test]
        public void Given_CapturedNavigate_When_Invoked_Then_NavigatesThroughRouter()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("about", element: V.Component(StubB)),
            });
            router.NavigateSync("/home");
            using var mounted = MountAt(router);

            // Act
            Capture.Navigate!("/about").GetAwaiter().GetResult();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_PatternMatchingLocation_When_UseMatch_Then_CapturesParam()
        {
            // Arrange
            var router = new Router(new[] { Route("users/:id", element: V.Component(StubA)) });
            router.NavigateSync("/users/7");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.Match!.Params["id"], Is.EqualTo("7"));
        }

        [Test]
        public void Given_PatternCaseDiffersFromLocation_When_UseMatch_Then_StillMatchesAndCapturesParam()
        {
            // UseMatch matches a location-relative pattern case-insensitively by default, independently of the
            // route table's own case-sensitivity.
            // Arrange
            var router = new Router(new[] { Route("users/:id", element: V.Component(StubA)) });
            router.NavigateSync("/users/5");
            Capture.MatchPattern = "USERS/:id";

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.Match!.Params["id"], Is.EqualTo("5"));
        }

        [Test]
        public void Given_PatternNotMatchingLocation_When_UseMatch_Then_ReturnsNull()
        {
            // Arrange
            Capture.MatchPattern = "posts/:id";
            var router = new Router(new[] { Route("users/:id", element: V.Component(StubA)) });
            router.NavigateSync("/users/7");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.Match, Is.Null);
        }

        [Test]
        public void Given_LocationWithQueryString_When_UseSearchParams_Then_ParsesFirstParam()
        {
            // The route table matches by path only; the query string lives on CurrentLocation.Path.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?q=velvet&page=2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Get("q"), Is.EqualTo("velvet"));
        }

        [Test]
        public void Given_LocationWithQueryString_When_UseSearchParams_Then_ParsesSecondParam()
        {
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?q=velvet&page=2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Get("page"), Is.EqualTo("2"));
        }

        [Test]
        public void Given_LocationWithRepeatedKey_When_UseSearchParams_Then_GetReturnsFirstValue()
        {
            // Get yields the first value of a repeated key.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?a=1&a=2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Get("a"), Is.EqualTo("1"));
        }

        [Test]
        public void Given_LocationWithRepeatedKey_When_UseSearchParams_Then_GetAllReturnsEveryValue()
        {
            // GetAll preserves every value of a repeated key in order.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?a=1&a=2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.GetAll("a"), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void Given_PlusInValue_When_UseSearchParams_Then_DecodesPlusAsSpace()
        {
            // A literal '+' in the query denotes a space.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?q=hello+world");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Get("q"), Is.EqualTo("hello world"));
        }

        [Test]
        public void Given_EncodedPlusInValue_When_UseSearchParams_Then_DecodesToLiteralPlus()
        {
            // '%2B' is an escaped plus and must round-trip to '+', not collapse to a space.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?op=1%2B2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Get("op"), Is.EqualTo("1+2"));
        }

        [Test]
        public void Given_QueryString_When_UseSearchParams_Then_HasAndKeysReflectParams()
        {
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?q=velvet&page=2");

            // Act
            using var mounted = MountAt(router);

            // Assert
            Assert.That(Capture.SearchParams!.Has("q"), Is.True);
            Assert.That(Capture.SearchParams!.Has("missing"), Is.False);
            Assert.That(Capture.SearchParams!.Keys, Is.EqualTo(new[] { "q", "page" }));
        }

        [Test]
        public void Given_SetSearchParams_When_Invoked_Then_ReplacesLocationWithBuiltQuery()
        {
            // The setter rebuilds the query (multi-value + escaping) and replaces the current entry,
            // dropping any previously present key.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?old=1");
            using var mounted = MountAt(router);

            var next = new SearchParams();
            next.Append("name", "a b");   // space escapes to %20 and survives the round-trip
            next.Append("tag", "x");
            next.Append("tag", "y");      // repeated key is preserved

            // Act — explicit Replace mode.
            Capture.SetSearchParams!.Invoke(next, NavigationMode.Replace);

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/search?name=a%20b&tag=x&tag=y"));
        }

        [Test]
        public void Given_SetSearchParams_When_InvokedWithDefaultMode_Then_PushesSoBackReturnsToPreviousQuery()
        {
            // setSearchParams defaults to PUSH, so the previous query stays in history.
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?old=1");
            using var mounted = MountAt(router);
            var next = new SearchParams();
            next.Append("new", "2");

            // Act — default mode (Push).
            Capture.SetSearchParams!.Invoke(next);
            Assume.That(router.CurrentLocation.Path, Is.EqualTo("/search?new=2"), "Precondition: navigated to the new query");

            // Assert — Back returns to the previous query (a Replace default would have dropped it).
            router.GoBackSync();
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/search?old=1"),
                "Default setSearchParams pushes, so Back returns to the previous query");
        }

        [Test]
        public void Given_SetSearchParamsFunctional_When_Invoked_Then_UpdaterReceivesCurrentParamsAndResultIsApplied()
        {
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?keep=1");
            using var mounted = MountAt(router);

            // Act — functional form: edit the CURRENT params without rebuilding from scratch.
            Capture.SetSearchParams!.Invoke(prev =>
            {
                var n = new SearchParams();
                n.Append("keep", prev.Get("keep"));   // reads the current params
                n.Append("added", "2");
                return n;
            });

            // Assert — the updater saw keep=1 (current) and its result became the new query.
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/search?keep=1&added=2"),
                "The functional updater receives the current params and its result is applied");
        }
    }
}
