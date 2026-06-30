using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Route-relative navigation: a
    /// <c>..</c> removes the calling route's <i>entire</i> URL contribution (which may span several
    /// segments for a multi-segment route pattern, or zero for an index route), not a single URL
    /// segment. <see cref="RouterRelativeNavigationTests"/> covers the single-segment cases where
    /// route-relative and URL-segment-relative coincide; this fixture covers where they diverge.
    /// </summary>
    [TestFixture]
    internal sealed class RouterRouteRelativeNavigationTests
    {
        // Router.Current is global singleton state; dispose between tests.
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        // A single route definition whose pattern spans THREE URL segments.
        private static Router BuildMultiSegmentTree()
            => new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("team/:id/settings"),
                }),
            });

        // A nested tree used to exercise depth-anchored (caller-route) resolution.
        private static Router BuildNestedTree()
            => new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users", children: new[]
                    {
                        Route("profile"),
                    }),
                }),
            });

        [Test]
        public void Given_MultiSegmentRoute_When_ResolvingDotDot_Then_DropsWholeRouteNotOneSegment()
        {
            // Arrange a URL produced by a single route whose pattern is "team/:id/settings".
            var router = BuildMultiSegmentTree();
            var nav = router.NavigateSync("/team/9/settings");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the multi-segment route");

            // Act resolving ".." from the leaf route.
            var resolved = router.ResolvePath("..");

            // Assert it drops the whole route (up to parent "/"), not just the trailing "settings" segment.
            Assert.That(resolved, Is.EqualTo("/"));
        }

        [Test]
        public void Given_MultiSegmentRoute_When_ResolvingDotDotSibling_Then_AppendsToParentRouteBase()
        {
            // Arrange the same multi-segment route location.
            var router = BuildMultiSegmentTree();
            var nav = router.NavigateSync("/team/9/settings");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the multi-segment route");

            // Act resolving "../about" (parent route + sibling).
            var resolved = router.ResolvePath("../about");

            // Assert the sibling appends to the parent route's base ("/"), giving "/about".
            Assert.That(resolved, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_NestedLocation_When_ResolvingDotDotAnchoredAtParentRoute_Then_DropsToGrandparent()
        {
            // Arrange /users/profile with matches [root, users, profile].
            var router = BuildNestedTree();
            var nav = router.NavigateSync("/users/profile");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the nested leaf");

            // Act — ".." is anchored at the "users" route (baseRouteIndex 1) — i.e. a UseNavigate called in
            // the parent route's component, not the leaf.
            var resolved = router.ResolvePath("..", baseRouteIndex: 1);

            // Assert it resolves relative to "users" (up one route -> root "/"), not relative to the leaf
            // (which would give "/users").
            Assert.That(resolved, Is.EqualTo("/"));
        }

        [Test]
        public void Given_NestedLocation_When_ResolvingDotDotAnchoredAtLeaf_Then_DropsToParentRoute()
        {
            // Arrange the same nested location.
            var router = BuildNestedTree();
            var nav = router.NavigateSync("/users/profile");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the nested leaf");

            // Act — ".." is anchored at the leaf route (baseRouteIndex 2).
            var resolved = router.ResolvePath("..", baseRouteIndex: 2);

            // Assert it resolves relative to the leaf -> parent route "/users".
            Assert.That(resolved, Is.EqualTo("/users"));
        }
    }
}
