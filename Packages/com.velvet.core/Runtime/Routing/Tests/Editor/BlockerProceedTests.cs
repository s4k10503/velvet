using NUnit.Framework;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class BlockerProceedTests
    {
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
        public void Given_ABackStepAGuardRedirected_When_Proceed_Then_TheRedirectLandsOnTheBackTargetsSlot()
        {
            // Arrange — the Guard on "/b" lets the first arrival through and redirects the Back step, which
            // reaches the Blocker as a Replace to "/login". The step the user asked for is what says which
            // slot that Replace belongs in.
            var guardChecks = 0;
            var router = BuildRouter("/a", Route("a"),
                Route("b", guard: _ => ++guardChecks == 1 ? null : "/login"),
                Route("c"), Route("login"));
            router.NavigateSync("/b");
            router.NavigateSync("/c");
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.GoBackSync();

            // Act
            state.Proceed();

            // Assert — CanGoForward is what separates the Back target's slot from the slot the user was on:
            // both land "/login", and only the step's own slot leaves "/c" ahead of it.
            Assert.That(
                (blockedResult, router.HistoryIndex, router.CurrentLocation.Path, router.CanGoForward),
                Is.EqualTo((NavigationResult.Blocked, 1, "/login", true)));
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
        public void Given_ABlockedPush_When_ANavigationMatchesNoRoute_Then_ProceedReIssuesTheAttemptStillHeld()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");
            var unmatchedResult = router.NavigateSync("/nowhere");

            // Act
            state.Proceed();

            // Assert — the unmatched navigation's own outcome rides along, since a router that matched it
            // would have cleared the block and left Proceed() re-issuing that navigation instead.
            Assert.That(
                (blockedResult, unmatchedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, NavigationResult.NotFound, "/other")));
        }

        [Test]
        public void Given_ASecondNavigationBlockedOverTheFirst_When_Proceed_Then_TheSecondIsWhatLands()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"), Route("third"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            var firstResult = router.NavigateSync("/other");
            var secondResult = router.NavigateSync("/third");

            // Act
            state.Proceed();

            // Assert — the first block rides along, because a Blocker that had only ever seen the second
            // attempt would land "/third" too.
            Assert.That(
                (firstResult, secondResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, NavigationResult.Blocked, "/third")));
        }

        [Test]
        public void Given_AProceedingBlocker_When_AnUnrelatedNavigationIsChecked_Then_ItIsPassedOverForThatOneToo()
        {
            // Arrange — the second Blocker vetoes the re-issue, which is what keeps the first Proceeding
            // past its own navigation and into one it never held.
            var router = BuildRouter("/home", Route("home"), Route("other"), Route("third"));
            var proceededChecks = 0;
            var proceeded = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ =>
            {
                proceededChecks++;
                return true;
            }, proceeded);
            var holdingChecks = 0;
            router.RouteBlockerManager.Register(_ => ++holdingChecks == 2, new RouteBlockerState());
            var blockedResult = router.NavigateSync("/other");
            proceeded.Proceed();

            // Act
            var unrelatedResult = router.NavigateSync("/third");

            // Assert — the check count is what says the first Blocker was passed over rather than asked,
            // and the landing is what says being passed over let a navigation through that it vetoes.
            Assert.That(
                (blockedResult, proceededChecks, unrelatedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 1, NavigationResult.Success, "/third")));
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
        public void Given_ABlockedBlockerWhoseRegistrationDied_When_Proceed_Then_ItSettlesAfterTheResume()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            var registration = router.RouteBlockerManager.Register(_ => true, state);
            var blockedResult = router.NavigateSync("/other");
            var statusBeforeProceed = state.Status;
            registration.Dispose();

            // Act
            state.Proceed();

            // Assert
            Assert.That(
                (blockedResult, statusBeforeProceed, state.Status, state.Attempt, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, RouteBlockerStatus.Blocked, RouteBlockerStatus.Idle,
                    (NavigationAttempt)null, "/other")));
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
        public void Given_AProceedingBlockerWhoseRegistrationDiedBesideAHoldingBlocker_When_TheHolderProceeds_Then_BothSettle()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var released = new RouteBlockerState();
            var releasedRegistration = router.RouteBlockerManager.Register(_ => true, released);
            var holding = new RouteBlockerState();
            var holdingChecks = 0;
            router.RouteBlockerManager.Register(_ => ++holdingChecks == 2, holding);
            var blockedResult = router.NavigateSync("/other");
            released.Proceed();
            var statusesBeforeFinalProceed = (released.Status, holding.Status);
            releasedRegistration.Dispose();

            // Act
            holding.Proceed();

            // Assert
            Assert.That(
                (blockedResult, holdingChecks, statusesBeforeFinalProceed, released.Status, released.Attempt,
                    holding.Status, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 2,
                    (RouteBlockerStatus.Proceeding, RouteBlockerStatus.Blocked), RouteBlockerStatus.Idle,
                    (NavigationAttempt)null, RouteBlockerStatus.Idle, "/other")));
        }

        [Test]
        public void Given_AProceedingBlockerBesideAHoldingBlockerWhoseRegistrationDies_When_NavigatingAgain_Then_TheLiveBlockerIsRearmed()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"), Route("third"));
            var released = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, released);
            var holding = new RouteBlockerState();
            var holdingChecks = 0;
            var holdingRegistration = router.RouteBlockerManager.Register(_ => ++holdingChecks == 2, holding);
            var blockedResult = router.NavigateSync("/other");
            released.Proceed();
            var statusesBeforeDispose = (released.Status, holding.Status);
            holdingRegistration.Dispose();
            var statusesAfterDispose = (released.Status, holding.Status, holding.Attempt?.NextPath);

            // Act
            var nextResult = router.NavigateSync("/third");

            // Assert
            Assert.That(
                (blockedResult, holdingChecks, statusesBeforeDispose, statusesAfterDispose, nextResult,
                    router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, 2,
                    (RouteBlockerStatus.Proceeding, RouteBlockerStatus.Blocked),
                    (RouteBlockerStatus.Idle, RouteBlockerStatus.Blocked, "/other"),
                    NavigationResult.Blocked, "/home")));
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

        [Test]
        public void Given_TwoBlockersHoldingOneAttempt_When_OneResets_Then_TheOthersProceedResumesNothing()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var first = new RouteBlockerState();
            var second = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, first);
            router.RouteBlockerManager.Register(_ => true, second);
            var blockedResult = router.NavigateSync("/other");
            first.Reset();

            // Act
            second.Proceed();

            // Assert — the first Blocker's status is what says the declined attempt is gone: a resume would
            // put it back in front of the same dialog, at the same location either way.
            Assert.That(
                (blockedResult, first.Status, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, RouteBlockerStatus.Idle, "/home")));
        }

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
