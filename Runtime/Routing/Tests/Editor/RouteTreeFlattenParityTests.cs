using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which routes can end a matched chain and in what order the chains are tried, as React Router's
    /// <c>flattenRoutes</c>, <c>rankRouteBranches</c> and <c>computeScore</c> decide it.
    /// <list type="bullet">
    /// <item>A route with a null path is never a chain's end unless it is an index route; one with an empty path
    /// is, children or none, and takes no index score.</item>
    /// <item>A path's optional segments make one branch per way of keeping or dropping them, scored as the
    /// plain path each spells.</item>
    /// <item>A branch scores one per segment of its joined path besides each segment's own value, and equal
    /// scores are ordered by React Router's comparator under V8's sort, over branches flattened each child's
    /// ahead of its parent's.</item>
    /// <item>An absolute child path is matched after its parents' joined path, which it has to start with.</item>
    /// </list>
    /// Every case here builds with the base's own surface, so the base answers each of them.
    /// </summary>
    [TestFixture]
    internal sealed class RouteTreeFlattenParityTests
    {
        [Test]
        public void Given_ANullPathRouteWithoutChildren_When_MatchingTheRoot_Then_NothingMatches()
        {
            // Arrange
            var tree = new RouteTree(new[] { new RouteDefinition { Element = V.Component(StubA) } });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Given_AnEmptyPathRouteWithChildren_When_MatchingItsOwnPath_Then_ItEndsTheChain()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("", children: new[] { Route("x") }) });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result?.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnEmptyPathRouteDeclaredAfterASlashSibling_When_MatchingTheRoot_Then_TheSlashRouteWins()
        {
            // Arrange — both score alike once the empty path takes no index score, and the earlier sibling is
            // tried first.
            var tree = new RouteTree(new[] { Route("/"), Route("") });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result?[0].Route.Path, Is.EqualTo("/"));
        }

        // GREEN_ON_BASE(characterization): the base already collapses the run; spelling RouteTree.JoinPaths' relative-path test `<= 0` reddens it.
        [Test]
        public void Given_ARootPathOpeningOnARunOfSlashesDeclaredAfterItsPlainSpelling_When_BothMatch_Then_TheEarlierDeclarationWins()
        {
            // Arrange — React Router's joinPaths makes the run one slash, so `//a` joins to `/a` and scores what
            // its sibling does.
            var tree = new RouteTree(new[] { Route("a"), Route("//a") });

            // Act
            var result = tree.Match("/a");

            // Assert
            Assert.That(result?[0].Route.Path, Is.EqualTo("a"));
        }

        [Test]
        public void Given_ARequiredPairDeclaredBeforeAnOptionalPair_When_BothMatch_Then_TheEarlierDeclarationWins()
        {
            // Arrange — the optional pair's branch keeping both segments spells `/:a/:b`, which scores what
            // `/:c/:d` does.
            var tree = new RouteTree(new[] { Route(":c/:d"), Route(":a?/:b?") });

            // Act
            var result = tree.Match("/1/2");

            // Assert
            Assert.That(result?[0].Route.Path, Is.EqualTo(":c/:d"));
        }

        [Test]
        public void Given_ALiteralSplatBeforeALongerDynamicPathOfEqualSegmentValue_When_BothMatch_Then_TheLongerPathWins()
        {
            // Arrange — the segments' values with the splat's penalty tie the two at 19; the count of segments
            // in each joined path, four against seven, is what puts the longer one ahead.
            var tree = new RouteTree(new[] { Route("x/y/*"), Route(":a/:b/:c/:d/:e/:f") });

            // Act
            var result = tree.Match("/x/y/1/2/3/4");

            // Assert
            Assert.That(result?[0].Route.Path, Is.EqualTo(":a/:b/:c/:d/:e/:f"));
        }

        // GREEN_ON_BASE(characterization): the base reaches this leaf by backtracking over its optional segment; a stable sort by score alone would rank `/:p/:s` first here and end the chain at `:s`.
        [Test]
        public void Given_EqualScoringBranchesOfTwoVariantsOfAnOptionalParent_When_BothMatch_Then_TheEarlierSiblingWins()
        {
            // Arrange — `/:q/:r` (the parent without `:p`) and `/:p/:s` (with it) both score 10; React Router's
            // comparator calls them siblings, by their positions under the parent, and puts `:q/:r` first,
            // where the order they were flattened in puts `/:p/:s` first.
            var tree = new RouteTree(new[] { Route(":p?", children: new[] { Route(":q/:r"), Route(":s") }) });

            // Act
            var result = tree.Match("/1/2");

            // Assert
            Assert.That(result?[result.Count - 1].Route.Path, Is.EqualTo(":q/:r"));
        }

        [Test]
        public void Given_AChildWhosePathEndsWhereItsParentsDoes_When_MatchingThatPath_Then_TheChildEndsTheChain()
        {
            // Arrange — the child's absolute `/` adds nothing to its parent's path, so the two branches score
            // alike.
            var tree = new RouteTree(new[] { Route("/", children: new[] { Route("/") }) });

            // Act
            var result = tree.Match("/");

            // Assert
            Assert.That(result?.Count, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnAbsoluteChildPathUnderItsParents_When_MatchingIt_Then_OnlyItsRestFollowsTheParent()
        {
            // Arrange
            var tree = new RouteTree(new[] { Route("app", children: new[] { Route("/app/x") }) });

            // Act
            var result = tree.Match("/app/x");

            // Assert
            Assert.That(result?.Count, Is.EqualTo(2));
        }

        [Test]
        public void Given_AnAbsoluteChildPathOutsideItsParents_When_BuildingTheTree_Then_ItThrows()
        {
            // Arrange
            var routes = new[] { Route("app", children: new[] { Route("/other") }) };

            // Act
            TestDelegate build = () => _ = new RouteTree(routes);

            // Assert
            Assert.That(build, Throws.ArgumentException);
        }

        [Test]
        public void Given_ANullPath_When_VRouteBuildsTheRoute_Then_ItAcceptsIt()
        {
            // Arrange
            var element = V.Component(StubA);

            // Act
            TestDelegate build = () => V.Route(null, element);

            // Assert
            Assert.That(build, Throws.Nothing);
        }
    }
}
