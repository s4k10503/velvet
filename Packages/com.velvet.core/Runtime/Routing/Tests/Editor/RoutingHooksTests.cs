// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the descendant routing hooks observed from a route Component rendered through an Outlet:
    /// <c>UseLocation</c> / <c>UseParams</c> / <c>UseLoaderData</c> / <c>UseRouteError</c> /
    /// <c>UseOutletContext</c>.
    /// <list type="bullet">
    /// <item><c>UseLocation</c> returns the router's current location.</item>
    /// <item><c>UseParams</c> returns the params captured for the matched route.</item>
    /// <item><c>UseLoaderData</c> returns the loader result of the current route.</item>
    /// <item><c>UseRouteError</c> returns null when the route loaded cleanly, and the loader's thrown exception
    /// when the loader failed.</item>
    /// <item><c>UseOutletContext</c> returns the value the enclosing Outlet supplies.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The hooks need the live router-root Provider chain (Location / LoaderData / Errors) above an Outlet, so
    /// each test navigates a real <see cref="Router"/> and mounts that chain via <c>MountWithRouter</c>. The
    /// captured values are exposed through the <c>Capture</c> static component, reset in <c>SetUp</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class RoutingHooksTests
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
            public static RouterLocation? Location;
            public static IReadOnlyDictionary<string, string>? Params;
            public static string? LoaderData;
            public static Exception? Error;
            public static object? OutletContext;

            public static void Reset()
            {
                Location = null;
                Params = null;
                LoaderData = null;
                Error = null;
                OutletContext = null;
            }

            [Component]
            public static VNode Render()
            {
                Location = Hooks.UseLocation();
                Params = Hooks.UseParams();
                LoaderData = Hooks.UseLoaderData<string>();
                Error = Hooks.UseRouteError();
                OutletContext = Hooks.UseOutletContext<object>();
                return V.Label(text: "capture");
            }
        }

        /// <summary>
        /// Mounts the router-root provider chain (Location / LoaderData / Errors) above an Outlet, exactly as
        /// the application's router root does, driving rendering of the matched route at depth 1.
        /// </summary>
        private MountedTree MountWithRouter(Router router, object? outletContext = null)
        {
            var location = router.CurrentLocation;
            var loaderData = router.CurrentLoaderData;
            var errors = router.CurrentLoaderErrors;

            return V.Mount(_root,
                V.Provider(RouterContext.Location, location,
                    children: new VNode[]
                    {
                        V.Provider(RouterContext.LoaderData, loaderData,
                            children: new VNode[]
                            {
                                V.Provider(RouterContext.Errors, errors,
                                    children: new VNode[] { V.Outlet(context: outletContext) }),
                            }),
                    }));
        }

        [Test]
        public void Given_NavigatedRoute_When_UseLocation_Then_ReturnsCurrentLocationPath()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("dash", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/dash");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Location!.Path, Is.EqualTo("/dash"));
        }

        [Test]
        public void Given_ParamRoute_When_UseParams_Then_ReturnsCapturedParam()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("user/:id", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/user/abc");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Params!["id"], Is.EqualTo("abc"));
        }

        [Test]
        public void Given_LoadedRoute_When_UseLoaderData_Then_ReturnsLoaderResult()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("data",
                    element: V.Component(Capture.Render, key: "cap"),
                    loader: (ctx, ct) => VelvetTask.FromResult((object)"hello")),
            });
            router.NavigateSync("/data");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.LoaderData, Is.EqualTo("hello"));
        }

        [Test]
        public void Given_CleanlyLoadedRoute_When_UseRouteError_Then_ReturnsNull()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("ok", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/ok");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Error, Is.Null);
        }

        [Test]
        public void Given_ThrowingLoaderRoute_When_UseRouteError_Then_ReturnsThrownException()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("boom",
                    element: V.Component(Capture.Render, key: "cap"),
                    errorElement: V.Component(Capture.Render, key: "cap-error"),
                    loader: (ctx, ct) => throw new InvalidOperationException("loader-boom")),
            });
            router.NavigateSync("/boom");

            // Act
            using var mounted = MountWithRouter(router);

            // Assert
            Assert.That(Capture.Error!.Message, Does.Contain("loader-boom"));
        }

        [Test]
        public void Given_OutletSuppliesContext_When_UseOutletContext_Then_ReturnsSuppliedValue()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("ctx", element: V.Component(Capture.Render, key: "cap")),
            });
            router.NavigateSync("/ctx");

            // Act
            using var mounted = MountWithRouter(router, outletContext: "from-outlet");

            // Assert
            Assert.That(Capture.OutletContext, Is.EqualTo("from-outlet"));
        }
    }

    /// <summary>
    /// Specifies the imperative routing hooks captured from a mounted component: <c>UseNavigate</c> /
    /// <c>UseMatch</c> / <c>UseSearchParams</c>.
    /// <list type="bullet">
    /// <item><c>UseNavigate</c> returns a function that navigates through the active router.</item>
    /// <item><c>UseMatch</c> returns a match (with captured params) when its location-relative pattern matches
    /// the current location, matching case-insensitively by default and independently of the route table, and
    /// returns null when the pattern does not match.</item>
    /// <item><c>UseSearchParams</c> returns the query parsed off the current location, rebuilt every
    /// render, beside one shared setter that replaces the query string and navigates.</item>
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
            public static Func<string, VelvetTask<NavigationResult>>? Navigate;
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
        public void Given_SearchParamsInterface_When_ReturnAnnotationsRead_Then_GetIsNullableAndGetAllIsNot()
        {
            // Arrange
            var get = typeof(ISearchParams).GetMethod(nameof(ISearchParams.Get))!;
            var getAll = typeof(ISearchParams).GetMethod(nameof(ISearchParams.GetAll))!;

            // Act
            var annotations = (
                get: NullableAnnotationProbe.ReturnAnnotation(get),
                getAll: NullableAnnotationProbe.ReturnAnnotation(getAll));

            // Assert
            Assert.That(
                annotations,
                Is.EqualTo((
                    NullableAnnotationProbe.Annotation.Nullable,
                    NullableAnnotationProbe.Annotation.NotNullable)),
                "The hook hands back the interface, so the interface is the declaration a consumer reads: "
                + "Get answers an absent key with null, while GetAll substitutes an empty list and is the "
                + "control showing the probe separates the two states");
        }

        [Test]
        public void Given_SetSearchParams_When_Invoked_Then_ReplacesLocationWithBuiltQuery()
        {
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?old=1");
            using var mounted = MountAt(router);

            var next = new SearchParams();
            next.Append("name", "a b");
            next.Append("tag", "x");
            next.Append("tag", "y");

            // Act
            Capture.SetSearchParams!.Invoke(next, NavigationMode.Replace);

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/search?name=a%20b&tag=x&tag=y"));
        }

        [Test]
        public void Given_SetSearchParams_When_InvokedWithDefaultMode_Then_PushesSoBackReturnsToPreviousQuery()
        {
            // Arrange
            var router = new Router(new[] { Route("search", element: V.Component(StubA)) });
            router.NavigateSync("/search?old=1");
            using var mounted = MountAt(router);
            var next = new SearchParams();
            next.Append("new", "2");

            // Act
            Capture.SetSearchParams!.Invoke(next);
            Assume.That(router.CurrentLocation.Path, Is.EqualTo("/search?new=2"), "Precondition: navigated to the new query");

            // Assert
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

            // Act
            Capture.SetSearchParams!.Invoke(prev =>
            {
                var n = new SearchParams();
                n.Append("keep", prev.Get("keep"));
                n.Append("added", "2");
                return n;
            });

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/search?keep=1&added=2"),
                "The functional updater receives the current params and its result is applied");
        }
    }

    [TestFixture]
    internal sealed class RouteNavigationStateTests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Capture.Reset();
            NavCapture.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        private static class Capture
        {
            public static NavigationState State;
            public static int RenderCount;

            public static void Reset()
            {
                State = default;
                RenderCount = 0;
            }

            [Component]
            public static VNode Render()
            {
                State = Hooks.UseNavigation();
                RenderCount++;
                return V.Label(text: "capture");
            }
        }

        private static class NavCapture
        {
            public static System.Action<string> SetTarget;

            public static void Reset() => SetTarget = null;

            [Component]
            public static VNode Render()
            {
                var (target, set) = Hooks.UseState("/a");
                SetTarget = set;
                return V.Navigate(target, key: "nav");
            }
        }

        private MountedTree MountWith(Router router, VNode child)
            => V.Mount(_root,
                V.Provider(RouterContext.Location, router.CurrentLocation,
                    children: new[] { child }));

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_StateIsIdle()
        {
            // Arrange
            var router = new Router(new[] { Route("home", element: V.Component(StubA)) });
            router.NavigateSync("/home");

            // Act
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));

            // Assert
            Assert.That(Capture.State.State, Is.EqualTo(NavigationLifecycle.Idle));
        }

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_LocationIsCurrent()
        {
            // Arrange
            var router = new Router(new[] { Route("home", element: V.Component(StubA)) });
            router.NavigateSync("/home");

            // Act
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));

            // Assert
            Assert.That(Capture.State.Location!.Path, Is.EqualTo("/home"));
        }

        [Test]
        public void Given_MountedNavigationHook_When_NavigationOccurs_Then_ComponentReRendersAndStaysIdleAtRest()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("about", element: V.Component(StubB)),
            });
            router.NavigateSync("/home");
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));
            // The subscription must be committed before the navigation under test.
            mounted.FlushEffectsForTest();
            var rendersBefore = Capture.RenderCount;

            // Act
            router.NavigateSync("/about");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.RenderCount, Is.GreaterThan(rendersBefore));
            Assert.That(Capture.State.State, Is.EqualTo(NavigationLifecycle.Idle));
            Assert.That(Capture.State.Location!.Path, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_NavigateElement_When_Mounted_Then_RedirectsToTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("dest", element: V.Component(StubB)),
            });
            router.NavigateSync("/start");

            // Act
            using var mounted = MountWith(router, V.Navigate("/dest", key: "nav"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/dest"));
        }

        [Test]
        public void Given_NavigateElementWithReplace_When_Mounted_Then_ReplacesHistoryEntry()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("dest", element: V.Component(StubB)),
            });
            router.NavigateSync("/start");

            // Act
            using var mounted = MountWith(router, V.Navigate("/dest", replace: true, key: "nav"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/dest"));
            Assert.That(router.CanGoBack, Is.False);
        }

        [Test]
        public void Given_SettledNavigation_When_UseNavigation_Then_ReRendersExactlyOnce()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("home", element: V.Component(StubA)),
                Route("about", element: V.Component(StubB)),
            });
            router.NavigateSync("/home");
            using var mounted = MountWith(router, V.Component(Capture.Render, key: "cap"));
            mounted.FlushEffectsForTest();
            var rendersBefore = Capture.RenderCount;

            // Act
            router.NavigateSync("/about");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Capture.RenderCount, Is.EqualTo(rendersBefore + 1));
        }

        [Test]
        public void Given_NavigateElement_When_ToPropChanges_Then_RedirectsToNewTarget()
        {
            // Arrange
            var router = new Router(new[]
            {
                Route("start", element: V.Component(StubA)),
                Route("a", element: V.Component(StubB)),
                Route("b", element: V.Component(StubA)),
            });
            router.NavigateSync("/start");
            using var mounted = MountWith(router, V.Component(NavCapture.Render, key: "wrap"));
            mounted.FlushEffectsForTest();
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/a"));

            // Act
            NavCapture.SetTarget!("/b");
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/b"));
        }
    }
}
