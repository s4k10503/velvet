// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the navigation to a path no route matches, as React Router's data router commits it.
    /// <list type="bullet">
    /// <item>The location is committed, with a history entry, and the chain it holds is the root route alone,
    /// carrying a 404 <see cref="ErrorResponse"/> as its error. No guard, action or loader runs, and the loader
    /// data of the location left is gone.</item>
    /// <item>The root route's <c>errorElement</c> renders it, or the default error element where there is
    /// none. The navigation reports <see cref="NavigationResult.NotFound"/>, including a redirect to a path
    /// no route matches.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterNotFoundTests
    {
        private VisualElement _root = null!;
        private static Exception? s_routeError;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_routeError = null;
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
            _root = null!;
        }

        [Component]
        private static VNode LayoutRender() => V.Div(children: new VNode[] { V.Label(text: "layout"), V.Outlet() });

        [Component]
        private static VNode HomeRender() => V.Label(text: "home");

        [Component]
        private static VNode NotFoundRender()
        {
            s_routeError = Hooks.UseRouteError();
            return V.Label(text: "not-found");
        }

        private static string Errors(Router router)
            => string.Join(",", router.CurrentLoaderErrors.Select(e => $"{e.Key}={e.Value.Message}"));

        #region What the router commits

        [Test]
        public void Given_ACommittedLocation_When_AnUnmatchedPathIsNavigatedTo_Then_ThatPathIsTheLocation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(router.CurrentLocation!.Path, Is.EqualTo("/nowhere"));
        }

        [Test]
        public void Given_ASingleTopLevelRoute_When_AnUnmatchedPathIsNavigatedTo_Then_ThatRouteCarriesTheNotFoundError()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(Errors(router), Is.EqualTo("0=404 Not Found"));
        }

        [Test]
        public void Given_SeveralTopLevelRoutesAndNoRoot_When_AnUnmatchedPathIsNavigatedTo_Then_ARouteOfNoElementCarriesTheError()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("about"));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(Errors(router), Is.EqualTo("__shim-error-route__=404 Not Found"));
        }

        [Test]
        public void Given_SeveralTopLevelRoutesIncludingTheRoot_When_AnUnmatchedPathIsNavigatedTo_Then_TheRootCarriesTheError()
        {
            // Arrange
            var router = BuildRouter("/home", Route("about"), Route("/", children: new[] { Route("home") }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(Errors(router), Is.EqualTo("1=404 Not Found"));
        }

        [Test]
        public void Given_ARootLayout_When_AnUnmatchedPathIsNavigatedTo_Then_TheCommittedChainIsTheRootAlone()
        {
            // Arrange
            var router = BuildRouter("/home", Route("/", children: new[] { Route("home") }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(router.CurrentLocation!.Matches!.Select(m => m.Route!.Path), Is.EqualTo(new[] { "/" }));
        }

        [Test]
        public void Given_ALoadedRoute_When_AnUnmatchedPathIsNavigatedTo_Then_TheLoaderDataIsGone()
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("home", loader: (_, _) => VelvetTask.FromResult<object>("home-data")));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(router.CurrentLoaderData, Is.Empty);
        }

        [Test]
        public void Given_AnUnmatchedPath_When_NavigatedTo_Then_ThePreviousEntryIsOneStepBack()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(router.CanGoBack, Is.True);
        }

        [Test]
        public void Given_ARedirectToAPathNoRouteMatches_When_NavigatingToTheRedirectingRoute_Then_TheNotFoundIsCommittedAtTheTarget()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("gated", guard: _ => "/nowhere"));

            // Act
            var result = router.NavigateSync("/gated");

            // Assert
            Assert.That($"{result} {router.CurrentLocation!.Path}", Is.EqualTo("NotFound /nowhere"));
        }

        // GREEN_ON_BASE(characterization): the base commits nothing for the unmatched path, so no loader runs
        // there either; this pins that the commit this change adds starts none.
        [Test]
        public void Given_ALoaderOnTheRoot_When_AnUnmatchedPathIsNavigatedTo_Then_NoLoaderRuns()
        {
            // Arrange
            var calls = 0;
            var router = BuildRouter("/home", Route("/", loader: (_, _) =>
            {
                calls++;
                return VelvetTask.FromResult<object>("root-data");
            }, children: new[] { Route("home") }));

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base never commits the error, so there is none to clear; this
        // pins that the commit of a matched path replaces the one this change adds.
        [Test]
        public void Given_ACommittedNotFound_When_AMatchedPathIsNavigatedTo_Then_TheErrorIsCleared()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"));
            router.NavigateSync("/nowhere");

            // Act
            router.NavigateSync("/home");

            // Assert
            Assert.That(router.CurrentLoaderErrors, Is.Empty);
        }

        #endregion

        #region What renders

        [Test]
        public void Given_ARootRouteWithAnErrorElement_When_AnUnmatchedPathIsMounted_Then_ItsErrorElementRenders()
        {
            // Arrange
            var router = new Router(V.Routes(
                V.Route("/", V.Component(LayoutRender), errorElement: V.Component(NotFoundRender), children: new[]
                {
                    V.Route("home", V.Component(HomeRender)),
                })));
            router.NavigateSync("/nowhere");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("not-found"), Is.Not.Null);
        }

        [Test]
        public void Given_ARootRouteWithAnErrorElement_When_AnUnmatchedPathIsMounted_Then_TheLayoutDoesNotRender()
        {
            // Arrange
            var router = new Router(V.Routes(
                V.Route("/", V.Component(LayoutRender), errorElement: V.Component(NotFoundRender), children: new[]
                {
                    V.Route("home", V.Component(HomeRender)),
                })));
            router.NavigateSync("/nowhere");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("layout"), Is.Null);
        }

        [Test]
        public void Given_TheErrorElementOfTheNotFoundRoute_When_ItReadsTheRouteError_Then_ItIsA404ErrorResponse()
        {
            // Arrange
            var router = new Router(V.Routes(
                V.Route("/", V.Component(LayoutRender), errorElement: V.Component(NotFoundRender), children: new[]
                {
                    V.Route("home", V.Component(HomeRender)),
                })));
            router.NavigateSync("/nowhere?q=1");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(
                s_routeError is ErrorResponse { Status: 404, StatusText: "Not Found", Detail: "No route matches URL \"/nowhere\"" },
                Is.True);
        }

        [Test]
        public void Given_NoRouteDeclaresAnErrorElement_When_AnUnmatchedPathIsMounted_Then_TheDefaultErrorElementShowsTheStatus()
        {
            // Arrange
            var router = new Router(V.Routes(
                V.Route("/", V.Component(LayoutRender), children: new[]
                {
                    V.Route("home", V.Component(HomeRender)),
                })));
            router.NavigateSync("/nowhere");

            // Act
            using var mounted = V.Mount(_root, V.RouterProvider(router));

            // Assert
            Assert.That(_root.FindLabelByText("404 Not Found"), Is.Not.Null);
        }

        #endregion
    }
}
