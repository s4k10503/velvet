using System;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="RouteDefinition.Index"/>, React Router's <c>index: true</c>.
    /// <list type="bullet">
    /// <item>An index route matches its parent's path and outscores a sibling of that path.</item>
    /// <item>It declares no children: a tree or <c>V.Route</c> given a child list for one refuses it.</item>
    /// <item>An index route is a route the 404 of a path no route matches can commit at, as React Router's
    /// <c>getShortCircuitMatches</c> picks one among several top-level routes.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteIndexTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        [Test]
        public void Given_AnIndexRouteDeclaredAfterAnEmptyPathSibling_When_MatchingTheParentPath_Then_TheIndexRouteEndsTheChain()
        {
            // Arrange — the two branches spell the same path, so the index score is all that separates them.
            var tree = new RouteTree(new[]
            {
                Route("room", children: new[]
                {
                    Route(""),
                    new RouteDefinition { Index = true, Element = V.Component(StubB) },
                }),
            });

            // Act
            var result = tree.Match("/room");

            // Assert
            Assert.That(result?[result.Count - 1].Route.Index, Is.True);
        }

        [Test]
        public void Given_AnIndexRouteWithAnEmptyChildList_When_BuildingTheTree_Then_ItThrows()
        {
            // Arrange
            var routes = new[]
            {
                Route("/", children: new[]
                {
                    new RouteDefinition { Index = true, Element = V.Component(StubA), Children = Array.Empty<RouteDefinition>() },
                }),
            };

            // Act
            TestDelegate build = () => _ = new RouteTree(routes);

            // Assert
            Assert.That(build, Throws.ArgumentException);
        }

        [Test]
        public void Given_AnIndexFlagAndAChildList_When_VRouteBuildsTheRoute_Then_ItThrows()
        {
            // Arrange
            var element = V.Component(StubA);

            // Act
            TestDelegate build = () => V.Route(element: element, index: true, children: Array.Empty<RouteDefinition>());

            // Assert
            Assert.That(build, Throws.ArgumentException);
        }

        [Test]
        public void Given_AnIndexFlag_When_VRouteBuildsTheRoute_Then_TheRouteIsAnIndexRoute()
        {
            // Arrange
            var element = V.Component(StubA);

            // Act
            var route = V.Route(element: element, index: true);

            // Assert
            Assert.That(route.Index, Is.True);
        }

        [Test]
        public void Given_AnIndexRouteWithAPathAmongTopLevelRoutes_When_NavigatingToAnUnmatchedPath_Then_The404CommitsAtIt()
        {
            // Arrange — the index route has a path of its own, so only its index flag makes it the 404's route.
            var router = BuildRouter("/a",
                Route("a"),
                new RouteDefinition { Index = true, Path = "home", Element = V.Component(StubA) });

            // Act
            router.NavigateSync("/nowhere");

            // Assert
            Assert.That(router.CurrentLocation?.Matches?[0].RouteId, Is.EqualTo("1"));
        }
    }
}
