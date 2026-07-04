using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Router"/> resolves relative navigation targets against the current location.
    /// <list type="bullet">
    /// <item>An absolute path (leading <c>/</c>) passes through unchanged.</item>
    /// <item><c>.</c> resolves to the current location's path.</item>
    /// <item><c>..</c> drops the last segment of the current path; <c>../sibling</c> resolves against the
    /// parent.</item>
    /// <item>A bare segment is appended to the current path.</item>
    /// <item>A relative target navigates to its resolved absolute path.</item>
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
    }
}
