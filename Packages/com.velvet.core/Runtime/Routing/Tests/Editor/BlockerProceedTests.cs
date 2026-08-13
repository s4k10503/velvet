using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="RouteBlockerState.Proceed"/> does with the navigation its Blocker held.
    /// <list type="bullet">
    /// <item>Proceed re-issues the blocked attempt without consulting that Blocker again — a Push or a
    /// Replace by its path and in its mode, a Back or Forward as the same history step.</item>
    /// <item>While the re-issued navigation runs, the Blocker still holds the attempt it released — the
    /// status it reports over that span is pinned by <see cref="BlockerTests"/>.</item>
    /// <item>The Blocker comes back to Idle when that navigation lands, and when it ends without landing,
    /// so the next navigation is blocked again either way. On the landing it is already Idle by the time
    /// <see cref="Router.OnLocationChanged"/> runs.</item>
    /// <item>A second Blocker that blocks the re-issued navigation holds it instead: the first stays out of
    /// the way until that one proceeds too, and comes back into it when that one resets.</item>
    /// <item><see cref="RouteBlockerState.Reset"/> releases the Blocker and re-issues nothing.</item>
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
        public void Given_ABlockedReplace_When_Proceed_Then_TheEntryIsReplacedRatherThanPushed()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateAsync("/other", NavigationMode.Replace)
                .GetAwaiter().GetResult();

            // Act
            state.Proceed();

            // Assert — the index is what separates the mode the attempt carried from a Push, which would
            // commit the same path one entry further on.
            Assert.That(
                (blockedResult, router.HistoryIndex, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 0, "/other")));
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
            // navigation's own check, which is before the commit that clears it.
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
        public void Given_AProceedingBlocker_When_TheResumedNavigationNotifies_Then_ItIsAlreadyIdle()
        {
            // Arrange — subscribed after the block, so the only notification the handler reads is the
            // resumed navigation's own.
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");
            string statusOnNotify = null;
            router.OnLocationChanged += _ => statusOnNotify = state.Status.ToString();

            // Act
            state.Proceed();

            // Assert — null is what a Proceed() that notified nothing leaves, so the status separates the
            // ordering under test from the absence of a resume.
            Assert.That(
                (blockedResult, statusOnNotify),
                Is.EqualTo((NavigationResult.Blocked, nameof(RouteBlockerStatus.Idle))));
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

        [Test]
        public void Given_AResumedNavigationThatCommitsNothing_When_NavigatingAgain_Then_TheBlockerBlocksIt()
        {
            // Arrange — the Guard lets the first attempt through to the Blocker and sends the resumed one at
            // a path no route matches, so the resume ends without a commit.
            var guardChecks = 0;
            var router = BuildRouter("/home", Route("home"),
                Route("other", guard: _ => ++guardChecks == 1 ? null : "/nowhere"),
                Route("third"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");
            state.Proceed();

            // Act
            var nextResult = router.NavigateSync("/third");

            // Assert — the Guard count rides along because a Proceed() that re-issued nothing also leaves
            // the next navigation blocked, and the second Guard reading is what says the resume happened.
            Assert.That(
                (blockedResult, guardChecks, nextResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 2, NavigationResult.Blocked, "/home")));
        }

        [Test]
        public void Given_ASecondBlockerHoldingTheResumedAttempt_When_ItProceedsToo_Then_TheAttemptCommits()
        {
            // Arrange — the second Blocker blocks its second pass only, which is the resumed navigation.
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var proceeded = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, proceeded);
            var holding = new RouteBlockerState();
            var checks = 0;
            router.RouteBlockerManager.Register(_ => ++checks == 2, holding);
            var blockedResult = router.NavigateSync("/other");
            proceeded.Proceed();

            // Act
            holding.Proceed();

            // Assert — the first block rides along because a router that blocked nothing would reach "/other"
            // without either Proceed().
            Assert.That(
                (blockedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, "/other")));
        }

        [Test]
        public void Given_ASecondBlockerHoldingTheResumedAttempt_When_ItResets_Then_TheFirstBlocksTheNextNavigation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"), Route("third"));
            var proceeded = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, proceeded);
            var holding = new RouteBlockerState();
            var checks = 0;
            router.RouteBlockerManager.Register(_ => ++checks == 2, holding);
            var blockedResult = router.NavigateSync("/other");
            proceeded.Proceed();
            holding.Reset();

            // Act
            var nextResult = router.NavigateSync("/third");

            // Assert — the check count rides along because a Proceed() that re-issued nothing also leaves the
            // next navigation blocked, and the third reading is what says the resume reached this Blocker.
            Assert.That(
                (blockedResult, checks, nextResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 3, NavigationResult.Blocked, "/home")));
        }

        // GREEN_ON_BASE(characterization): Reset() releases the Blocker and abandons the blocked attempt on
        // the base too. Wiring Proceed() puts a re-issue on the state that Reset() must not reach.
        [Test]
        public void Given_ABlockedPush_When_Reset_Then_TheBlockerIsReleasedAndNothingIsResumed()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");

            // Act
            state.Reset();

            // Assert — the status separates a Reset that did nothing from one that released the Blocker,
            // which the location cannot: both leave the router where it was.
            Assert.That(
                (blockedResult, state.Status, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, RouteBlockerStatus.Idle, "/home")));
        }
    }
}
