using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="RouteBlockerState.Proceed"/> does with the navigation its Blocker held.
    /// <list type="bullet">
    /// <item>Proceed re-issues the blocked attempt, which commits without consulting that Blocker again — a
    /// Push by its path, a Back or Forward as the same history step.</item>
    /// <item>While the re-issued navigation runs, the Blocker still holds the attempt it released — the
    /// status it reports over that span is pinned by <see cref="BlockerTests"/>.</item>
    /// <item>The commit returns it to Idle, so the next navigation is blocked again.</item>
    /// <item><see cref="RouteBlockerState.Reset"/> re-issues nothing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class BlockerProceedTests
    {
        // Router.Current is global singleton state; dispose between tests.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [Test]
        public void Given_ABlockedPush_When_Proceed_Then_TheBlockedLocationCommits()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");

            // Act
            state.Proceed();

            // Assert — the block rides along because a blocker that never blocked would leave the router on
            // "/other" for the ordinary reason, with Proceed() doing nothing at all.
            Assert.That(
                (blockedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, "/other")));
        }

        [Test]
        public void Given_APredicateThatAlwaysBlocks_When_Proceed_Then_ItIsNotConsultedByTheResumedNavigation()
        {
            // Arrange
            var checks = 0;
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ =>
            {
                checks++;
                return true;
            }, state);
            var blockedResult = router.NavigateSync("/other");

            // Act
            state.Proceed();

            // Assert — the check count alone is 1 whether or not anything was re-issued, so the committed
            // path is what separates a resumed navigation from a Proceed() that did nothing.
            Assert.That(
                (blockedResult, checks, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 1, "/other")));
        }

        [Test]
        public void Given_ABlockedBackStep_When_Proceed_Then_TheHistoryIndexStepsBack()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.GoBackSync();

            // Act
            state.Proceed();

            // Assert — an unblocked GoBack lands on index 0 by itself, so the block is what makes the index
            // evidence about the resume rather than about the step.
            Assert.That((blockedResult, router.HistoryIndex), Is.EqualTo((NavigationResult.Blocked, 0)));
        }

        [Test]
        public void Given_ABlockedForwardStep_When_Proceed_Then_TheHistoryIndexStepsForward()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.GoBackSync();
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.GoForwardSync();

            // Act
            state.Proceed();

            // Assert — the block is what separates the resume from the step, as on the Back case.
            Assert.That((blockedResult, router.HistoryIndex), Is.EqualTo((NavigationResult.Blocked, 1)));
        }

        [Test]
        public void Given_ABlockerThatProceeded_When_AnotherIsConsultedByTheResumedNavigation_Then_ItStillHoldsItsAttempt()
        {
            // Arrange — the second Blocker allows everything and reads the first one from inside the resumed
            // navigation's check, the only point at which that navigation is observable from a test.
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var proceeding = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, proceeding);
            var checks = 0;
            string attemptSeenOnResume = null;
            router.RouteBlockerManager.Register(_ =>
            {
                checks++;
                if (checks == 2)
                {
                    attemptSeenOnResume = proceeding.Attempt?.NextPath;
                }
                return false;
            }, new RouteBlockerState());
            var blockedResult = router.NavigateSync("/other");

            // Act
            proceeding.Proceed();

            // Assert
            Assert.That((blockedResult, attemptSeenOnResume), Is.EqualTo((NavigationResult.Blocked, "/other")));
        }

        [Test]
        public void Given_AProceedingBlocker_When_TheResumedNavigationCommits_Then_ItReturnsToIdle()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");

            // Act
            state.Proceed();

            // Assert — the committed path rides along because Idle is also what a Proceed() that re-issued
            // nothing leaves behind.
            Assert.That(
                (blockedResult, state.Status, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, RouteBlockerStatus.Idle, "/other")));
        }

        [Test]
        public void Given_AProceedingBlocker_When_TheResumedNavigationHasCommitted_Then_ItBlocksTheNextNavigation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"), Route("third"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");
            state.Proceed();

            // Act
            var nextResult = router.NavigateSync("/third");

            // Assert — the resumed location rides along because a Proceed() that re-issued nothing also
            // leaves the next navigation blocked.
            Assert.That(
                (blockedResult, nextResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, NavigationResult.Blocked, "/other")));
        }

        // GREEN_ON_BASE(characterization): Reset() abandons the blocked attempt on the base too. Wiring
        // Proceed() to re-issue it puts a delegate on the state that Reset() must not reach.
        [Test]
        public void Given_ABlockedPush_When_Reset_Then_NothingIsResumed()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");

            // Act
            state.Reset();

            // Assert
            Assert.That(
                (blockedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, "/home")));
        }
    }
}
