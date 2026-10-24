using System;
using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a route's identity is in a matched chain, and which routes can end one.
    /// <list type="bullet">
    /// <item>A route's <see cref="RouteMatch.RouteId"/> is its <see cref="RouteDefinition.Id"/>, or else its
    /// position in the tree: the indexes from the top-level array down, joined by <c>-</c>. No two routes of
    /// one tree share it, and a tree that gives two the same id refuses to build.</item>
    /// <item>A pathless layout consumes no path, so a chain never ends on one: it is in the chain of a path
    /// a child matches, and matches no path by itself.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouteTreeIdentityTests
    {
        #region RouteId

        [Test]
        public void Given_SiblingPathlessLayouts_When_MatchingAChildOfEach_Then_TheLayoutsHaveDistinctRouteIds()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("", children: new[] { Route("x") }),
                Route("", children: new[] { Route("y") }),
            });

            // Act
            var first = tree.Match("/x")[0].RouteId;
            var second = tree.Match("/y")[0].RouteId;

            // Assert
            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void Given_AnIndexRouteUnderTheRoot_When_Matching_Then_RouteIdsArePositionsInTheTree()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/", children: new[] { Route("") }),
            });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That((result[0].RouteId, result[1].RouteId), Is.EqualTo(("0", "0-0")));
        }

        [Test]
        public void Given_NestedSiblings_When_MatchingTheSecond_Then_ItsRouteIdIsItsPositionPathFromTheTop()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("about"),
                Route("shop", children: new[] { Route("cart"), Route("checkout") }),
            });

            // Act
            var result = tree.Match("/shop/checkout");

            // Assert
            Assert.That((result[0].RouteId, result[1].RouteId), Is.EqualTo(("1", "1-1")));
        }

        [Test]
        public void Given_ARouteWithAnId_When_Matching_Then_ItsRouteIdIsThatId()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                new RouteDefinition { Path = "/", Id = "root", Children = new[] { new RouteDefinition { Path = "a" } } },
            });

            // Act
            var result = tree.Match("/a");

            // Assert
            Assert.That((result[0].RouteId, result[1].RouteId), Is.EqualTo(("root", "0-0")));
        }

        [Test]
        public void Given_TwoRoutesSharingAnId_When_BuildingTheTree_Then_ItThrows()
        {
            // Arrange
            var routes = new[]
            {
                new RouteDefinition { Path = "a", Id = "same" },
                new RouteDefinition { Path = "b", Id = "same" },
            };

            // Act
            TestDelegate build = () => _ = new RouteTree(routes);

            // Assert
            Assert.That(build, Throws.ArgumentException.With.Message.Contains("same"));
        }

        [Test]
        public void Given_AnExplicitIdEqualToAnotherRoutesPosition_When_BuildingTheTree_Then_ItThrows()
        {
            // Arrange
            var routes = new[]
            {
                new RouteDefinition { Path = "a" },
                new RouteDefinition { Path = "b", Id = "0" },
            };

            // Act
            TestDelegate build = () => _ = new RouteTree(routes);

            // Assert
            Assert.That(build, Throws.ArgumentException);
        }

        #endregion

        #region Pathless layouts

        [Test]
        public void Given_APathlessLayout_When_MatchingItsOwnPath_Then_NothingMatches()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("", children: new[] { Route("x") }),
            });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_APathlessLayoutUnderTheRoot_When_MatchingTheRootPath_Then_TheRootIsTheLeaf()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("/", children: new[] { Route("", children: new[] { Route("x") }) }),
            });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_APathlessLayoutWithAnIndexChild_When_MatchingItsPath_Then_TheIndexEndsTheChain()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("", children: new[] { Route("") }),
            });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result.Count, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base already puts a pathless layout in the chain of its child's path.
        [Test]
        public void Given_APathlessLayout_When_MatchingAChildPath_Then_TheLayoutIsInTheChain()
        {
            // Arrange
            var tree = new RouteTree(new[]
            {
                Route("", children: new[] { Route("x") }),
            });

            // Act
            var result = tree.Match("/x");

            // Assert
            Assert.That(new[] { result[0].Route.Path, result[1].Route.Path }, Is.EqualTo(new[] { "", "x" }));
        }

        #endregion
    }
}
