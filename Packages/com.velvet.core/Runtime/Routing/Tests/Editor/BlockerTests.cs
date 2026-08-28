using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class BlockerTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        #region RouteBlockerManager check

        [Test]
        public void Given_NoBlockers_When_CheckAsync_Then_ReportsNotBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_BlockingBlocker_When_CheckAsync_Then_ReportsBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => true, new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.True);
        }

        [Test]
        public void Given_BlockingBlocker_When_CheckAsync_Then_StateBecomesBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => true, state);

            // Act
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Blocked));
        }

        [Test]
        public void Given_AllowingBlocker_When_CheckAsync_Then_ReportsNotBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => false, new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_AllowingBlocker_When_CheckAsync_Then_StateStaysIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => false, state);

            // Act
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_AllowingAndBlockingBlockers_When_CheckAsync_Then_ReportsBlockedWithoutShortCircuit()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => false, new RouteBlockerState());
            manager.Register(_ => true, new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.True);
        }

        [Test]
        public void Given_RegisteredBlocker_When_RegistrationDisposed_Then_CheckAsyncNoLongerSeesIt()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var registration = manager.Register(_ => true, new RouteBlockerState());

            // Act
            registration.Dispose();
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_BlockedBlockers_When_ResetAllBlocked_Then_EveryStateReturnsToIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state1 = new RouteBlockerState();
            var state2 = new RouteBlockerState();
            manager.Register(_ => true, state1);
            manager.Register(_ => true, state2);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            Assume.That(state1.Status, Is.EqualTo(RouteBlockerStatus.Blocked), "Precondition: both blockers blocked");
            Assume.That(state2.Status, Is.EqualTo(RouteBlockerStatus.Blocked), "Precondition: both blockers blocked");

            // Act
            manager.ResetAllBlocked();

            // Assert
            Assert.That(
                (state1.Status, state2.Status),
                Is.EqualTo((RouteBlockerStatus.Idle, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_ABlockerThatProceeded_When_CheckAsyncRunsAgain_Then_ItIsNotConsulted()
        {
            // Arrange — the resume is a no-op, so the pass under test is the next CheckAsync rather than
            // whatever Proceed() would have re-issued through a Router.
            var checks = 0;
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ =>
            {
                checks++;
                return true;
            }, state);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            state.Proceed();

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert — the status is what the skip is keyed on, and the pass leaves it where it was.
            Assert.That(
                (checks, blocked, state.Status),
                Is.EqualTo((1, false, RouteBlockerStatus.Proceeding)));
        }

        [Test]
        public void Given_ABlockerThatProceeded_When_SettleProceeding_Then_ItBlocksAgain()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => true, state);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            state.Proceed();

            // Act
            manager.SettleProceeding();
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That((blocked, state.Status), Is.EqualTo((true, RouteBlockerStatus.Blocked)));
        }

        [Test]
        public void Given_ABlockedBlockerBesideAProceedingOne_When_SettleProceeding_Then_TheProceedingOneIsLeftAlone()
        {
            // Arrange — the second Blocker blocks only its second pass, so it is the one holding the attempt
            // the first released.
            var manager = new RouteBlockerManager();
            var proceeded = new RouteBlockerState();
            var holding = new RouteBlockerState();
            var checks = 0;
            manager.Register(_ => true, proceeded);
            manager.Register(_ => ++checks == 2, holding);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            proceeded.Proceed();
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Act
            manager.SettleProceeding();

            // Assert — the blocked one rides along because a pass that left it Idle would make the settle
            // trivially correct rather than deferred.
            Assert.That(
                (proceeded.Status, holding.Status),
                Is.EqualTo((RouteBlockerStatus.Proceeding, RouteBlockerStatus.Blocked)));
        }

        #endregion

        #region RouteBlockerManager async check

        [Test]
        public void Given_BlockingAsyncBlocker_When_CheckAsync_Then_ReportsBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register((_, __) => UniTask.FromResult(true), new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.True);
        }

        [Test]
        public void Given_BlockingAsyncBlocker_When_CheckAsync_Then_StateBecomesBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register((_, __) => UniTask.FromResult(true), state);

            // Act
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Blocked));
        }

        [Test]
        public void Given_AllowingAsyncBlocker_When_CheckAsync_Then_ReportsNotBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register((_, __) => UniTask.FromResult(false), new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_AllowingAsyncBlocker_When_CheckAsync_Then_StateStaysIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register((_, __) => UniTask.FromResult(false), state);

            // Act
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_MixedBlockingSyncAndAsyncBlockers_When_CheckAsync_Then_BothStatesBecomeBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var syncState = new RouteBlockerState();
            var asyncState = new RouteBlockerState();
            manager.Register(_ => true, syncState);
            manager.Register((_, __) => UniTask.FromResult(true), asyncState);

            // Act
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That(
                (syncState.Status, asyncState.Status),
                Is.EqualTo((RouteBlockerStatus.Blocked, RouteBlockerStatus.Blocked)),
                "CheckAsync evaluates both sync and async entries");
        }

        #endregion

        #region Router navigation with blocker

        [Test]
        public void Given_BlockingBlocker_When_Navigate_Then_ReturnsBlocked()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_BlockingBlocker_When_Navigate_Then_CurrentLocationIsUnchanged()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/home"));
        }

        [Test]
        public void Given_AllowingBlocker_When_Navigate_Then_ReturnsSuccess()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_AllowingBlocker_When_Navigate_Then_CommitsTargetLocation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/other"));
        }

        [Test]
        public void Given_PreviouslyBlockedNavigation_When_NavigatingAgain_Then_ResetsAndCommits()
        {
            // Arrange
            var blockCount = 0;
            var router = BuildRouter("/home", Route("home"), Route("a"), Route("b"));
            router.RouteBlockerManager.Register(_ => ++blockCount == 1, new RouteBlockerState());
            router.NavigateSync("/a");
            Assume.That(router.CurrentLocation.Path, Is.EqualTo("/home"), "Precondition: the first attempt was blocked");

            // Act
            var result = router.NavigateSync("/b");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Success));
        }

        [Test]
        public void Given_BlockingAsyncBlocker_When_Navigate_Then_ReturnsBlocked()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.RouteBlockerManager.Register((_, __) => UniTask.FromResult(true), new RouteBlockerState());

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        #endregion

        #region Back and Forward with blocker

        [Test]
        public void Given_BlockerRegisteredAfterArriving_When_GoBack_Then_ReturnsBlocked()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            var result = router.GoBackSync();

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_BlockerRegisteredAfterArriving_When_GoBackBlocked_Then_LocationIsUnchanged()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/other"));
        }

        [Test]
        public void Given_BlockerRegisteredAfterArriving_When_GoBackBlocked_Then_HistoryIndexIsUnchanged()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            Assume.That(router.HistoryIndex, Is.EqualTo(1), "Precondition: positioned on the second entry");
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            router.GoBackSync();

            // Assert
            Assert.That(router.HistoryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Given_BlockerRegisteredBeforeForward_When_GoForward_Then_ReturnsBlocked()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.GoBackSync();
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            var result = router.GoForwardSync();

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_BlockerRegisteredBeforeForward_When_GoForwardBlocked_Then_LocationIsUnchanged()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.GoBackSync();
            router.RouteBlockerManager.Register(_ => true, new RouteBlockerState());

            // Act
            router.GoForwardSync();

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/home"));
        }

        [Test]
        public void Given_AllowingBlocker_When_GoForward_Then_CommitsForwardLocation()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            router.NavigateSync("/other");
            router.GoBackSync();
            router.RouteBlockerManager.Register(_ => false, new RouteBlockerState());

            // Act
            var result = router.GoForwardSync();
            Assume.That(result, Is.EqualTo(NavigationResult.Success), "Precondition: the forward step was allowed");

            // Assert
            Assert.That(router.CurrentLocation.Path, Is.EqualTo("/other"));
        }

        #endregion

        #region UseBlocker hook render-phase survival

        private static int s_blockerRenderCount;
        private static Action<int> s_blockerSetPhase;
        private static string s_blockerObservedDep;

        private static void ResetBlockerComponent()
        {
            s_blockerRenderCount = 0;
            s_blockerSetPhase = null;
            s_blockerObservedDep = null;
        }

        // Render-phase setState normalizes an odd phase to the next even phase in one re-run, so the blocker
        // dep swings to "transient" on the discarded attempt and back to the committed "settled" on settle.
        [Component]
        private static VNode RenderPhaseBlockerRender()
        {
            s_blockerRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_blockerSetPhase = setPhase;
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var dep = phase % 2 == 1 ? "transient" : "settled";
            Hooks.UseBlocker(_ => { s_blockerObservedDep = dep; return true; }, dep);
            return V.Label(text: dep);
        }

        [Test]
        public void Given_MountedUseBlocker_When_Navigate_Then_CommittedPredicateBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), V.Component(RenderPhaseBlockerRender, key: "blk"));

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_RenderPhaseReRun_When_SettingOddPhase_Then_NormalizesToNextEvenInOneReRun()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), V.Component(RenderPhaseBlockerRender, key: "blk"));
            Assume.That(s_blockerRenderCount, Is.EqualTo(1), "Precondition: the initial mount rendered once");

            // Act
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_blockerRenderCount, Is.EqualTo(3));
        }

        [Test]
        public void Given_RenderPhaseReRun_When_NavigatingAfterSettle_Then_CommittedBlockerStaysRegistered()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), V.Component(RenderPhaseBlockerRender, key: "blk"));
            router.RouteBlockerManager.ResetAllBlocked();
            s_blockerObservedDep = null;
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        [Test]
        public void Given_RenderPhaseReRun_When_NavigatingAfterSettle_Then_SettledPredicateIsObserved()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), V.Component(RenderPhaseBlockerRender, key: "blk"));
            router.RouteBlockerManager.ResetAllBlocked();
            s_blockerObservedDep = null;
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(s_blockerObservedDep, Is.EqualTo("settled"));
        }

        #endregion

        #region UseBlocker with deps omitted

        private static StateUpdater<bool> s_omittedDepsSetDirty;

        [Component]
        private static VNode OmittedDepsBlockerRender()
        {
            var (isDirty, setDirty) = Hooks.UseState(false);
            s_omittedDepsSetDirty = setDirty;
            Hooks.UseBlocker(_ => isDirty);
            return V.Label(text: isDirty ? "dirty" : "clean");
        }

        [Test]
        public void Given_UseBlockerWithDepsOmitted_When_TheCapturedStateChanges_Then_TheNewAnswerBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            s_omittedDepsSetDirty = default;
            using var mounted = V.Mount(new VisualElement(), V.Component(OmittedDepsBlockerRender, key: "blk"));

            // Act — the first departure reads the mount render's false, the second the re-render's true.
            var beforeChange = router.NavigateSync("/other");
            s_omittedDepsSetDirty.Invoke(true);
            mounted.FlushStateForTest();
            var afterChange = router.NavigateSync("/home");

            // Assert
            Assert.That(
                (beforeChange, afterChange),
                Is.EqualTo((NavigationResult.Success, NavigationResult.Blocked)),
                "Omitting deps re-registers the predicate every render, so the blocker answers with the "
                + "state of the render that registered it rather than the mount render's");
        }

        private static StateUpdater<bool> s_omittedDepsAsyncSetDirty;

        [Component]
        private static VNode OmittedDepsAsyncBlockerRender()
        {
            var (isDirty, setDirty) = Hooks.UseState(false);
            s_omittedDepsAsyncSetDirty = setDirty;
            Hooks.UseBlocker((_, _) => UniTask.FromResult(isDirty));
            return V.Label(text: isDirty ? "dirty" : "clean");
        }

        [Test]
        public void Given_AsyncUseBlockerWithDepsOmitted_When_TheCapturedStateChanges_Then_TheNewAnswerBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            s_omittedDepsAsyncSetDirty = default;
            using var mounted = V.Mount(
                new VisualElement(), V.Component(OmittedDepsAsyncBlockerRender, key: "blk-async"));

            // Act — the first departure reads the mount render's false, the second the re-render's true.
            var beforeChange = router.NavigateSync("/other");
            s_omittedDepsAsyncSetDirty.Invoke(true);
            mounted.FlushStateForTest();
            var afterChange = router.NavigateSync("/home");

            // Assert
            Assert.That(
                (beforeChange, afterChange),
                Is.EqualTo((NavigationResult.Success, NavigationResult.Blocked)),
                "The async overload stages the same null deps as the synchronous one, so the two must not "
                + "drift apart under an edit to either");
        }

        #endregion

        #region UseBlocker re-registration beside a proceeding Blocker

        private static RouteBlockerState s_answeredFormBlocker;
        private static RouteBlockerState s_holdingFormBlocker;
        private static StateUpdater<int> s_holdingFormRevise;

        [Component]
        private static VNode AnsweredFormRender()
        {
            s_answeredFormBlocker = Hooks.UseBlocker(_ => true);
            return V.Label(text: "answered");
        }

        [Component]
        private static VNode HoldingFormRender()
        {
            var (revision, revise) = Hooks.UseState(0);
            s_holdingFormRevise = revise;
            s_holdingFormBlocker = Hooks.UseBlocker(_ => true);
            return V.Label(text: revision.ToString());
        }

        [Component]
        private static VNode TwoBlockingFormsRender() =>
            V.Div(
                "forms",
                V.Component(AnsweredFormRender, key: "answered"),
                V.Component(HoldingFormRender, key: "holding"));

        [Test]
        public void Given_ABlockerReRegisteringWhileAnotherProceeds_When_ItProceedsToo_Then_TheDepartureLands()
        {
            // Arrange — the second form re-renders while it is the one still holding the departure, and a
            // UseBlocker written without a deps argument swaps its registration on every render.
            var router = BuildRouter("/home", Route("home"), Route("other"));
            s_answeredFormBlocker = null;
            s_holdingFormBlocker = null;
            s_holdingFormRevise = default;
            using var mounted = V.Mount(new VisualElement(), V.Component(TwoBlockingFormsRender, key: "forms"));
            var blockedResult = router.NavigateSync("/other");
            s_answeredFormBlocker.Proceed();
            s_holdingFormRevise.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_holdingFormBlocker.Proceed();

            // Assert — the first result rides along because a page whose forms never blocked reaches "/other"
            // on the navigation itself, with neither Proceed() having anything to resume.
            Assert.That(
                (blockedResult, router.CurrentLocation.Path),
                Is.EqualTo((NavigationResult.Blocked, "/other")));
        }

        #endregion

        #region Registration bookkeeping

        private static int EntryCountOf(RouteBlockerManager manager) =>
            ((ICollection)typeof(RouteBlockerManager)
                .GetField("_blockers", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(manager)).Count;

        [Test]
        public void Given_AnIdleBlocker_When_ItsRegistrationIsDisposed_Then_ItsEntryLeavesTheList()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var registration = manager.Register(_ => false, new RouteBlockerState());
            var entriesBeforeDispose = EntryCountOf(manager);

            // Act
            registration.Dispose();

            // Assert — the count before rides along because a manager that never held the entry reads 0
            // afterwards too.
            Assert.That((entriesBeforeDispose, EntryCountOf(manager)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ABlockerDisposedWhileBlocked_When_ANavigationLiftsTheBlock_Then_ItsEntryLeavesTheList()
        {
            // Arrange — the disposal itself cannot drop this entry: the state is still Blocked, and a saved
            // dialog handler may still answer it.
            var manager = new RouteBlockerManager();
            var registration = manager.Register(_ => true, new RouteBlockerState());
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            registration.Dispose();
            var entriesBeforeTheNextAttempt = EntryCountOf(manager);

            // Act
            manager.ResetAllBlocked();

            // Assert
            Assert.That((entriesBeforeTheNextAttempt, EntryCountOf(manager)), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_ABlockerDisposedWhileProceeding_When_TheAttemptSettles_Then_ItsEntryLeavesTheList()
        {
            // Arrange — Proceeding is the one status a disposal leaves that no later navigation's own
            // release clears, so the settle is the only place this entry can go.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            var registration = manager.Register(_ => true, state);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            state.Proceed();
            registration.Dispose();
            var entriesBeforeTheSettle = EntryCountOf(manager);

            // Act
            manager.SettleProceeding();

            // Assert
            Assert.That((entriesBeforeTheSettle, EntryCountOf(manager)), Is.EqualTo((1, 0)));
        }

        #endregion

        #region Blocker liveness during CheckAsync mutation

        [Test]
        public void Given_ABlockerThatUnregistersItselfWhileBlocking_When_ThePassCompletes_Then_ItsStateStaysIdle()
        {
            // Arrange — the Blocker disposes its own registration before answering true.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            IDisposable registration = null;
            registration = manager.Register(_ =>
            {
                registration.Dispose();
                return true;
            }, state);

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert — a dead registration neither blocks the navigation nor strands its state.
            Assert.That((blocked, state.Status), Is.EqualTo((false, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_AnEarlierBlockerUnregistersALaterOne_When_ThePassContinues_Then_TheLaterStateStaysIdle()
        {
            // Arrange — the earlier Blocker removes the later one before the snapshot loop reaches it.
            var manager = new RouteBlockerManager();
            var laterState = new RouteBlockerState();
            IDisposable laterRegistration = null;
            using var earlier = manager.Register(_ =>
            {
                laterRegistration.Dispose();
                return false;
            }, new RouteBlockerState());
            laterRegistration = manager.Register(_ => true, laterState);

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert
            Assert.That((blocked, laterState.Status), Is.EqualTo((false, RouteBlockerStatus.Idle)));
        }

        // GREEN_ON_BASE(characterization): the snapshot is already taken, and this is what says what it
        // is for. Measured: two cases beside it also fail when the walk goes back to the live list — but
        // both assert a state that stays Idle, where this one asserts a later blocker ran.
        [Test]
        public void Given_AnEarlierBlockerUnregistersItself_When_ThePassContinues_Then_TheNextOneIsStillConsulted()
        {
            // Arrange — the pass walks a snapshot, so an entry removed mid-pass is still visited and the
            // entries behind it do not shift under the walk. Three blockers, because with two the removal
            // empties the list and the loop ends either way.
            var manager = new RouteBlockerManager();
            var secondSaw = false;
            IDisposable first = null;
            first = manager.Register(_ =>
            {
                first.Dispose();
                return false;
            }, new RouteBlockerState());
            using var second = manager.Register(_ =>
            {
                secondSaw = true;
                return true;
            }, new RouteBlockerState());
            using var third = manager.Register(_ => false, new RouteBlockerState());

            // Act
            var blocked = manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();

            // Assert — the decision rides along because a second that was consulted and a second that
            // was skipped both leave every state Idle.
            Assert.That((secondSaw, blocked), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the superseded read already happens after the check returns,
        // and this is what says so. Measured: hoisting it to the top of the loop body — where a reader
        // expects a cancellation check — fails this case and no other in the suite.
        [Test]
        public void Given_AnAttemptTheCheckItselfSupersedes_When_ItWouldBlock_Then_NoStateIsFlipped()
        {
            // Arrange — the cancellation is read after the check returns rather than at the top of
            // the loop body, so a token the check itself cancelled is still seen. Hoisting the read is
            // the ordinary refactor, and it leaves Blocked wired to an attempt the caller discards.
            //
            // The check completes synchronously: yielding first would leave the pass mid-await, and
            // the fixture drives it with GetResult rather than an await of its own.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            using var cts = new CancellationTokenSource();
            using var registration = manager.Register((attempt, token) =>
            {
                cts.Cancel();
                return UniTask.FromResult(true);
            }, state);

            // Act
            manager.CheckAsync(Attempt(), NoResume, cts.Token).GetAwaiter().GetResult();

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_AnAlreadySupersededAttempt_When_ABlockerWouldBlock_Then_NoStateIsFlipped()
        {
            // Arrange — the attempt's token is cancelled before the pass begins.
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            using var registration = manager.Register(_ => true, state);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            manager.CheckAsync(Attempt(), NoResume, cts.Token).GetAwaiter().GetResult();

            // Assert — the abandoned attempt leaves no Blocked state behind.
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_ABlockedBlockerWhoseRegistrationDied_When_Reset_Then_ItStillReturnsToIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            var registration = manager.Register(_ => true, state);
            manager.CheckAsync(Attempt(), NoResume).GetAwaiter().GetResult();
            var statusBeforeReset = state.Status;
            registration.Dispose();

            // Act
            state.Reset();

            // Assert — the status before the call rides along because a pass that never blocked leaves
            // Idle here too, which would make the release read as correct without it having run.
            Assert.That(
                (statusBeforeReset, state.Status),
                Is.EqualTo((RouteBlockerStatus.Blocked, RouteBlockerStatus.Idle)));
        }

        #endregion
    }
}
