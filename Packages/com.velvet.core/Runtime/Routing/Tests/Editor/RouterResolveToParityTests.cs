using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies relative resolution as React Router's <c>resolveTo</c> over <c>getResolveToMatches</c> does it.
    /// <list type="bullet">
    /// <item>The route levels a <c>..</c> steps over are the matches up to the caller's that contribute a path,
    /// so a pathless layout is no level of its own.</item>
    /// <item>Only the target's leading <c>..</c> segments step up a route; one after a <c>.</c> drops a URL
    /// segment.</item>
    /// <item>A search on the target is no part of the segments read.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RouterResolveToParityTests
    {
        [TearDown]
        public void TearDown() => Router.Current?.Dispose();

        [Test]
        public void Given_APathlessLayoutCallingDotDot_When_Resolved_Then_ItStepsOverTheRouteAboveTheLayout()
        {
            // Arrange — the layout is level 2 of the chain, and contributes no path of its own.
            var router = BuildRouter("/accounts/x",
                Route("/", children: new[] { Route("accounts", children: new[] { Route(null, children: new[] { Route("x") }) }) }));

            // Act
            var resolved = router.ResolvePath("..", baseRouteIndex: 2);

            // Assert
            Assert.That(resolved, Is.EqualTo("/"));
        }

        [Test]
        public void Given_ADotBeforeADotDot_When_Resolved_Then_TheDotDotDropsOneUrlSegment()
        {
            // Arrange — a two-segment route, so dropping a URL segment and dropping the route differ.
            var router = BuildRouter("/team/9", Route("/", children: new[] { Route("team/:id") }));

            // Act
            var resolved = router.ResolvePath("./..");

            // Assert
            Assert.That(resolved, Is.EqualTo("/team"));
        }

        [Test]
        public void Given_ADotDotWithASearch_When_Resolved_Then_TheSearchFollowsTheResolvedPath()
        {
            // Arrange
            var router = BuildRouter("/users/profile",
                Route("/", children: new[] { Route("users", children: new[] { Route("profile") }) }));

            // Act
            var resolved = router.ResolvePath("..?q=1");

            // Assert
            Assert.That(resolved, Is.EqualTo("/users?q=1"));
        }
    }
}
