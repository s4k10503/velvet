using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Router"/> resolves relative navigation targets against the current location,
    /// both for the single-segment case (where route-relative and URL-segment-relative coincide) and for a
    /// caller route whose own pattern spans several URL segments or none (where they diverge).
    /// <list type="bullet">
    /// <item>An absolute path (leading <c>/</c>) passes through unchanged.</item>
    /// <item><c>.</c> resolves to the current location's path.</item>
    /// <item><c>..</c> drops the last segment of the current path; <c>../sibling</c> resolves against the
    /// parent.</item>
    /// <item>A bare segment is appended to the current path.</item>
    /// <item>A relative target navigates to its resolved absolute path.</item>
    /// <item><c>..</c> removes the calling route's entire URL contribution — which may span several segments
    /// for a multi-segment route pattern — not a single URL segment, and a sibling resolves against that
    /// route's base.</item>
    /// <item>A depth-anchored <c>..</c> (an explicit <c>baseRouteIndex</c>, as a parent route's own
    /// <c>UseNavigate</c> call would supply) resolves relative to that route in the match chain rather than
    /// always the leaf.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterRelativeNavigationTests
    {
        // Router.Current is global singleton state; dispose between tests.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        private static Router BuildTree()
            => new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("users", children: new[]
                    {
                        Route("profile"),
                        Route("settings"),
                    }),
                    Route("about"),
                }),
            });

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
        public void Given_CurrentLocation_When_ResolvingAbsolutePath_Then_PassesThroughUnchanged()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users/profile");

            // Act
            var resolved = router.ResolvePath("/about");

            // Assert
            Assert.That(resolved, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_CurrentLocation_When_ResolvingDot_Then_ResolvesToCurrentPath()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users/profile");

            // Act
            var resolved = router.ResolvePath(".");

            // Assert
            Assert.That(resolved, Is.EqualTo("/users/profile"));
        }

        [Test]
        public void Given_CurrentLocation_When_ResolvingDotDot_Then_DropsLastSegment()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users/profile");

            // Act
            var resolved = router.ResolvePath("..");

            // Assert
            Assert.That(resolved, Is.EqualTo("/users"));
        }

        [Test]
        public void Given_CurrentLocation_When_ResolvingDotDotSibling_Then_ResolvesAgainstParent()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users/profile");

            // Act
            var resolved = router.ResolvePath("../settings");

            // Assert
            Assert.That(resolved, Is.EqualTo("/users/settings"));
        }

        [Test]
        public void Given_CurrentLocation_When_ResolvingBareSegment_Then_AppendsToCurrentPath()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users");

            // Act
            var resolved = router.ResolvePath("profile");

            // Assert
            Assert.That(resolved, Is.EqualTo("/users/profile"));
        }

        [Test]
        public void Given_CurrentLocation_When_NavigatingToRelativeSibling_Then_CommitsResolvedPath()
        {
            // Arrange
            var router = BuildTree();
            router.NavigateSync("/users/profile");

            // Act
            var result = router.NavigateSync("../settings");
            Assume.That(result, Is.EqualTo(NavigationResult.Success), "Precondition: the relative navigation succeeded");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/users/settings"));
        }

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
