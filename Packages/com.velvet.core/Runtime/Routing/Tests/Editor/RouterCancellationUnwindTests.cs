using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an abandoned navigation leaves behind. Its provisional history mutations happen before
    /// the Guard and Blocker awaits, so an attempt that abandons them has to undo those mutations whether it
    /// leaves by exception (a blocker honoring its token) or by return value (one awaiting without
    /// forwarding it).
    /// <list type="bullet">
    /// <item>The provisional Back/Forward index is restored, so the history keeps describing the entry the
    /// user is still on.</item>
    /// <item>The provisional Push entry a Guard redirect appended is removed again.</item>
    /// <item><see cref="RouterStatus"/> returns to Idle, so <c>UseNavigation</c> stops reporting a pending
    /// navigation that no longer exists.</item>
    /// <item>None of those restores happens once a newer navigation has taken over, since the state they
    /// would put back describes a router that navigation has already replaced.</item>
    /// </list>
    /// </summary>
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

        // Same isolation rule as RouterTests: Router.Current is a global that each new Router() overwrites.
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledDuringBackAwait_Then_HistoryIndexIsRestored()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var router = new Router(_routes);
            await router.NavigateAsync("/home");
            await router.NavigateAsync("/about");
            var (check, entered) = MakeOneShotBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            using var callerCts = new CancellationTokenSource();
            var nav = router.GoBack(callerCts.Token);
            await entered.Task;
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/about"),
                "Precondition: the Back is still parked in the blocker and has committed nothing");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1),
                "A cancelled Back leaves the history pointing at the entry the user never left");
        });

        [UnityTest]
        public IEnumerator Given_GuardRedirect_When_CancelledDuringRedirectBlockerAwait_Then_ProvisionalPushIsUndone()
            => UniTask.ToCoroutine(async () =>
        {
            // The redirect target's own navigation is what parks on the blocker, so the cancellation surfaces
            // inside the await that RunGuardChecks wraps around it — past the point where the originating Push
            // entry was appended for the redirect to overwrite.
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
            await entered.Task;

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That($"{HistoryCountOf(router)}/{router.HistoryIndex}", Is.EqualTo("1/0"),
                "A cancelled redirect restores the history list and the index together; restoring either alone "
                + "still leaves the stack describing a navigation that never happened");
        });

        [UnityTest]
        public IEnumerator Given_BlockerHonorsToken_When_CancelledWithNoFollowUpNavigation_Then_StatusReturnsToIdle()
            => UniTask.ToCoroutine(async () =>
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
            await entered.Task;
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "Precondition: the cancelled navigation never commits a location");

            // Act
            callerCts.Cancel();
            await nav;

            // Assert
            Assert.That(router.Status, Is.EqualTo(RouterStatus.Idle),
                "With no navigation left in flight, UseNavigation must not keep reporting one");
        });

        [UnityTest]
        public IEnumerator Given_SupersededBackUnwindsLate_When_NewerBackHasCommitted_Then_IndexMatchesTheCommittedLocation()
            => UniTask.ToCoroutine(async () =>
        {
            // The superseded attempt saved an index describing a router that the newer navigation has since
            // replaced, so putting that index back desyncs it from the location actually on screen.
            // The asserted location records today's behaviour, not the intended one: both Backs land on
            // /home because the parked attempt already moved the shared index and nothing resets it while it
            // waits, so the second Back reads its target from the moved index and /about is skipped. That is
            // a separate open defect; this case discriminates only the index/location desync.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("contact") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/contact");
            var (check, entered, resumeCancelled, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.GoBack();
            await entered.Task;
            await router.GoBack();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "Precondition: the newer Back committed while the superseded one was still parked");

            // Act
            resumeCancelled();
            await superseded;

            // Assert
            Assert.That($"idx={router.HistoryIndex} at={router.CurrentLocation?.Path}",
                Is.EqualTo("idx=0 at=/home"),
                "A late unwind must not move the index away from the entry the newer navigation committed");
        });

        [UnityTest]
        public IEnumerator Given_SupersededRedirectUnwindsLate_When_NewerNavigationHasPushed_Then_ItsHistorySurvives()
            => UniTask.ToCoroutine(async () =>
        {
            // The guard snapshot was taken before the newer navigation pushed, so replaying it would delete
            // entries that belong to the location now on screen.
            // The asserted count records today's behaviour, not the intended one: the count is 3 because the
            // middle entry is the provisional /guarded push, a path the user never arrived at, stranded
            // precisely because the skipped restore is the correct choice here — a Back from /x re-runs its
            // guard and lands on /target. Reclaiming just that entry is a separate open defect.
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
            await entered.Task;
            await router.NavigateAsync("/x");
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/x"),
                "Precondition: the newer navigation committed while the redirect was still parked");

            // Act
            resumeCancelled();
            await superseded;

            // Assert
            Assert.That($"count={HistoryCountOf(router)} idx={router.HistoryIndex}", Is.EqualTo("count=3 idx=2"),
                "A late unwind must not replay a snapshot that predates the newer navigation's own entries");
        });

        [UnityTest]
        public IEnumerator Given_SupersededBlockerReturnsLate_When_NewerBackHasCommitted_Then_IndexAndStatusSurvive()
            => UniTask.ToCoroutine(async () =>
        {
            // A blocker that awaits without forwarding the token returns "not blocked" instead of throwing,
            // so the abandoned attempt reaches the blocker check's own rollback rather than the exception
            // handlers — a separate pair of writes that needs the same ownership test.
            // The asserted location records today's behaviour on the same open defect as the case above:
            // both Backs land on /home because the parked attempt already moved the shared index.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("settings") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/settings");
            var (check, entered, _, resumeUnblocked) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.GoBack();
            await entered.Task;
            await router.GoBack();
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/home"),
                "Precondition: the newer Back committed while the superseded one was still parked");

            // Act
            resumeUnblocked();
            await superseded;

            // Assert
            Assert.That($"idx={router.HistoryIndex} status={router.Status}", Is.EqualTo("idx=0 status=Ready"),
                "A blocker returning after it was superseded must roll back neither the index nor the Status "
                + "the newer navigation established");
        });

        [UnityTest]
        public IEnumerator Given_SupersededByAnUnmatchedPath_When_ItResumes_Then_TheIndexIsStillRestored()
            => UniTask.ToCoroutine(async () =>
        {
            // A navigation that matches no route returns before the index is ever touched, so it takes no
            // claim on it. The parked attempt is then still the only holder and must put the index back —
            // leaving it moved would strand it away from the location the user never left.
            // Arrange
            var router = BuildRouter("/home",
                Route("/", children: new[] { Route("home"), Route("about"), Route("contact") }));
            await router.NavigateAsync("/about");
            await router.NavigateAsync("/contact");
            var (check, entered, resumeCancelled, _) = MakeDeferredBlocker();
            using var registration = router.RouteBlockerManager.Register(check, new RouteBlockerState());
            var superseded = router.GoBack();
            await entered.Task;
            var unmatched = await router.NavigateAsync("/no-such-route");
            Assume.That(unmatched, Is.EqualTo(NavigationResult.NotFound),
                "Precondition: the superseding navigation returned without reaching the history index");

            // Act
            resumeCancelled();
            await superseded;

            // Assert
            Assert.That($"idx={router.HistoryIndex} at={router.CurrentLocation?.Path}",
                Is.EqualTo("idx=2 at=/contact"),
                "Only an attempt that took the index may stop the parked one from restoring it");
        });

        [UnityTest]
        public IEnumerator Given_SupersededRedirectReturnsLate_When_NewerNavigationHasPushed_Then_ItsHistorySurvives()
            => UniTask.ToCoroutine(async () =>
        {
            // The inner redirect returns Cancelled from its own blocker check instead of throwing, so the
            // outer reaches the returned-result restore rather than the exception one. Same stale snapshot,
            // a different line has to refuse to replay it.
            // The asserted count records today's behaviour for the same reason as the case above.
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
            await entered.Task;
            await router.NavigateAsync("/x");
            Assume.That(router.CurrentLocation?.Path, Is.EqualTo("/x"),
                "Precondition: the newer navigation committed while the redirect was still parked");

            // Act
            resumeUnblocked();
            await superseded;

            // Assert
            Assert.That($"count={HistoryCountOf(router)} idx={router.HistoryIndex}", Is.EqualTo("count=3 idx=2"),
                "A redirect that returns Cancelled after being superseded must not replay its stale snapshot");
        });

        [UnityTest]
        public IEnumerator Given_BlockerParkedAcrossDispose_When_DisposeCancelsIt_Then_TheDeadRouterIsNotWritten()
            => UniTask.ToCoroutine(async () =>
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
            await entered.Task;
            router.OnStatusChanged += _ => statusEvents++;

            // Act
            router.Dispose();
            await parked;

            // Assert
            Assert.That($"statusEvents={statusEvents} idx={router.HistoryIndex}", Is.EqualTo("statusEvents=0 idx=0"),
                "Teardown leaves nothing for a resuming blocker to restore, so it must write neither field");
        });

        // The history list has no accessor of its own, and adding one would put a test-only member on a
        // production type.
        private static int HistoryCountOf(Router router)
        {
            var field = typeof(Router).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic);
            return ((ICollection)field.GetValue(router)).Count;
        }
    }
}
