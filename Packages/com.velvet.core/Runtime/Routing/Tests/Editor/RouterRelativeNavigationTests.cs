using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RouterRelativeNavigationTests
    {
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

        private static Router BuildMultiSegmentTree()
            => new Router(new[]
            {
                Route("/", children: new[]
                {
                    Route("team/:id/settings"),
                }),
            });

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
            // Arrange
            var router = BuildMultiSegmentTree();
            var nav = router.NavigateSync("/team/9/settings");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the multi-segment route");

            // Act
            var resolved = router.ResolvePath("..");

            // Assert
            Assert.That(resolved, Is.EqualTo("/"));
        }

        [Test]
        public void Given_MultiSegmentRoute_When_ResolvingDotDotSibling_Then_AppendsToParentRouteBase()
        {
            // Arrange
            var router = BuildMultiSegmentTree();
            var nav = router.NavigateSync("/team/9/settings");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the multi-segment route");

            // Act
            var resolved = router.ResolvePath("../about");

            // Assert
            Assert.That(resolved, Is.EqualTo("/about"));
        }

        [Test]
        public void Given_NestedLocation_When_ResolvingDotDotAnchoredAtParentRoute_Then_DropsToGrandparent()
        {
            // Arrange
            var router = BuildNestedTree();
            var nav = router.NavigateSync("/users/profile");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the nested leaf");

            // Act
            var resolved = router.ResolvePath("..", baseRouteIndex: 1);

            // Assert
            Assert.That(resolved, Is.EqualTo("/"));
        }

        [Test]
        public void Given_NestedLocation_When_ResolvingDotDotAnchoredAtLeaf_Then_DropsToParentRoute()
        {
            // Arrange
            var router = BuildNestedTree();
            var nav = router.NavigateSync("/users/profile");
            Assume.That(nav, Is.EqualTo(NavigationResult.Success), "Precondition: navigated to the nested leaf");

            // Act
            var resolved = router.ResolvePath("..", baseRouteIndex: 2);

            // Assert
            Assert.That(resolved, Is.EqualTo("/users"));
        }
    }
}
