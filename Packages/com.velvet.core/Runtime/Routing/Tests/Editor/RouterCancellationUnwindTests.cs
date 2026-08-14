using System.Collections;
using System.Threading;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    // Bounded for the cases here that await a blocker stub's Entered signal;
    // RouteTestStubs.MakeOneShotBlocker states what an unbounded fixture costs.
    [Timeout(30000)]
    [TestFixture]
    internal sealed class RouterCancellationUnwindTests
    {
        private RouteDefinition[] _routes;

        [SetUp]
        public void SetUp()
        {
            _routes = new[]
            {
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("about"),
                }),
            };
        }

        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledDuringBackAwait_Then_HistoryIndexIsUnchanged()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.GoBack(callerCts.Token);
            await entered.Task.Bounded();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "Precondition: the Back is still parked in the blocker and has committed nothing");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1),
                "A cancelled Back leaves the history pointing at the entry the user never left");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_GuardRedirect_When_CancelledDuringRedirectBlockerAwait_Then_NothingIsRecorded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The redirect target's own navigation is what parks on the blocker, so the cancellation surfaces
            // inside the await that RunGuardChecks wraps around it, with the whole redirect pair still
            // uncommitted.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => "/target"),
                    Route("target"),
                }));
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.NavigateAsync("/guarded", NavigationMode.Push, callerCts.Token);
            await entered.Task.Bounded();

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That($"{RouterHistoryProbe.CountOf(router)}/{router.HistoryIndex}", Is.EqualTo("1/0"),
                "A cancelled redirect leaves neither an entry nor an index move behind; either one alone "
                + "still leaves the stack describing a navigation that never happened");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledWithNoFollowUpNavigation_Then_StatusReturnsToIdle()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A caller-supplied token rather than a second navigation taking over: the newer navigation
            // would commit and set Status itself, masking whether this one cleaned up after itself.
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.NavigateAsync("/about", NavigationMode.Push, callerCts.Token);
            await entered.Task.Bounded();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "Precondition: the cancelled navigation never commits a location");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Idle),
                "With no navigation left in flight, UseNavigation must not keep reporting one");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_SupersededBackUnwindsLate_When_NewerBackHasCommitted_Then_TheCommittedStateSurvives()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The superseded attempt was parked while the newer Back committed, so the Idle status it would
            // put back describes a router that no longer exists — one with nothing in flight, where the newer
            // navigation has just finished one.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("contact") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/contact");
            var (check, entered, resumeCancelled, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.GoBack();
            await entered.Task.Bounded();
            await router.GoBack();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "Precondition: the newer Back committed while the superseded one was still parked");

            // Act
            resumeCancelled();
            await superseded;

            // Assert
            Assert.That($"idx={router.HistoryIndex} at={router.CurrentLocation?.Path} status={router.Status}",
                Is.EqualTo("idx=1 at=/about status=Ready"),
                "A late unwind must leave the newer navigation's own position and status standing");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_SupersededRedirectUnwindsLate_When_NewerNavigationHasPushed_Then_ItsHistorySurvives()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The abandoned redirect wrote nothing, so the stack holds exactly what the newer navigation put
            // there: /home and the /x it pushed onto it.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => "/target"),
                    Route("target"),
                    Route("x"),
                }));
            var (check, entered, resumeCancelled, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.NavigateAsync("/guarded");
            await entered.Task.Bounded();
            await router.NavigateAsync("/x");
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/x"),
                "Precondition: the newer navigation committed while the redirect was still parked");

            // Act
            resumeCancelled();
            await superseded;

            // Assert
            Assert.That(
                $"count={RouterHistoryProbe.CountOf(router)} idx={router.HistoryIndex} status={router.Status}",
                Is.EqualTo("count=2 idx=1 status=Ready"),
                "A late unwind must leave the newer navigation's own entries and status exactly as it left them");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_SupersededBlockerReturnsLate_When_NewerBackHasCommitted_Then_IndexAndStatusSurvive()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A blocker that awaits without forwarding the token returns "not blocked" instead of throwing,
            // so the abandoned attempt reaches the blocker check's own rollback rather than the exception
            // handlers — a separate write that needs the same ownership test.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("settings") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/settings");
            var (check, entered, _, resumeUnblocked) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.GoBack();
            await entered.Task.Bounded();
            await router.GoBack();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "Precondition: the newer Back committed while the superseded one was still parked");

            // Act
            resumeUnblocked();
            await superseded;

            // Assert
            Assert.That($"idx={router.HistoryIndex} status={router.Status}", Is.EqualTo("idx=1 status=Ready"),
                "A blocker returning after it was superseded must roll back neither the index nor the Status "
                + "the newer navigation established");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_AnAttemptParkedInABlocker_When_APathMatchingNoRouteIsNavigatedTo_Then_ItIsNotDispossessed()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A navigation that matches no route ends above the claim, so it takes nothing from the parked
            // attempt — neither the claim, nor the status, nor the token the attempt is parked under. It
            // therefore resumes and commits; it used to resume into a cancelled token and hand its caller
            // Cancelled for a navigation the user had asked for. The unmatched result is folded in because
            // withholding the dispossession must not cost that caller its own outcome.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("contact") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/contact");
            var (check, entered, _, resumeUnblocked) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var parked = router.GoBack();
            await entered.Task.Bounded();
            var unmatched = await router.NavigateAsync("/no-such-route");

            // Act
            resumeUnblocked();
            var result = await parked;

            // Assert
            Assert.That(
                $"unmatched={unmatched} parked={result} path={router.CurrentLocation?.Path} "
                + $"status={router.Status}",
                Is.EqualTo("unmatched=NotFound parked=Success path=/about status=Ready"));
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_SupersededRedirectReturnsLate_When_NewerNavigationHasPushed_Then_ItsHistorySurvives()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The inner redirect returns Cancelled from its own blocker check instead of throwing, so it
            // reaches the returned-result exit rather than the exception one. That exit is the one that must
            // also refuse to commit the redirect target onto the stack the newer navigation now owns.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[]
                {
                    Route("home"),
                    Route("guarded", guard: _ => "/target"),
                    Route("target"),
                    Route("x"),
                }));
            var (check, entered, _, resumeUnblocked) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.NavigateAsync("/guarded");
            await entered.Task.Bounded();
            await router.NavigateAsync("/x");
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/x"),
                "Precondition: the newer navigation committed while the redirect was still parked");

            // Act
            resumeUnblocked();
            await superseded;

            // Assert
            Assert.That($"count={RouterHistoryProbe.CountOf(router)} idx={router.HistoryIndex}", Is.EqualTo("count=2 idx=1"),
                "A redirect that returns Cancelled after being superseded must commit nothing of its own");
        });

        // GREEN_ON_BASE(refactor): the wait this bounds is the same wait, and a run where the code
        // under test arrives cannot tell the two apart. What the bound changes is the run where it
        // does not: a hang becomes a failure naming the wait.
        [UnityTest]
        public IEnumerator Given_BlockerParkedAcrossDispose_When_DisposeCancelsIt_Then_TheDeadRouterIsNotWritten()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Dispose retires the claim before cancelling, the opposite order to a navigation taking one.
            // A blocker of this shape unwinds synchronously inside that Cancel, so with the navigation
            // ordering it would still hold the claim and write to a router that is being torn down.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about") }));
            await router.NavigateAsync("/about");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var statusEvents = 0;
            var parked = router.GoBack();
            await entered.Task.Bounded();
            router.OnStatusChanged += _ => statusEvents++;

            // Act
            router.Dispose();
            await parked;

            // Assert
            Assert.That(statusEvents, Is.EqualTo(0),
                "Teardown leaves nothing for a resuming blocker to restore, so it must raise no transition");
        });
    }
}
