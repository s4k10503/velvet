using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the navigation blocking contract of <see cref="RouteBlockerManager"/>, its integration with
    /// <see cref="Router"/>, and the <c>UseBlocker</c> hook.
    /// <list type="bullet">
    /// <item>With no blockers registered a check reports not-blocked; a registered predicate returning true
    /// blocks and transitions its <see cref="RouteBlockerState"/> to Blocked, while false leaves it Idle.</item>
    /// <item>Every registered blocker is evaluated with no short-circuit, so a single blocking predicate blocks
    /// regardless of the others.</item>
    /// <item>Registration returns a disposable that unregisters the blocker on dispose.</item>
    /// <item><c>ResetAllBlocked</c> returns every Blocked state to Idle.</item>
    /// <item><c>CheckAsync</c> evaluates every registered blocker (sync and async entries alike).</item>
    /// <item>A blocking blocker on a router navigation yields <see cref="NavigationResult.Blocked"/> and keeps
    /// the current location, and on a Back/Forward step the provisional history index is rolled back; an
    /// allowing blocker yields <see cref="NavigationResult.Success"/>.</item>
    /// <item>A re-attempt after being blocked resets the prior block and navigates.</item>
    /// <item><c>UseBlocker</c> registers the committed predicate at settle and survives a render-phase re-run
    /// without registering a discarded attempt's transient predicate.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class BlockerTests
    {
        // Router.Current is global singleton state; dispose between tests.
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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();
            Assume.That(state1.Status, Is.EqualTo(RouteBlockerStatus.Blocked), "Precondition: both blockers blocked");
            Assume.That(state2.Status, Is.EqualTo(RouteBlockerStatus.Blocked), "Precondition: both blockers blocked");

            // Act
            manager.ResetAllBlocked();

            // Assert
            Assert.That(
                (state1.Status, state2.Status),
                Is.EqualTo((RouteBlockerStatus.Idle, RouteBlockerStatus.Idle)));
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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            var blocked = manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            manager.CheckAsync(Attempt()).GetAwaiter().GetResult();

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
            // The blocker blocks only its first invocation, so a second navigation lifts the prior block.
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
        public void Given_BlockerRegisteredAfterArriving_When_GoBackBlocked_Then_HistoryIndexIsRolledBack()
        {
            // The provisional Back index decrement is undone when the blocker rejects the step.
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
            // The deferred registration is applied at settle, so the committed predicate blocks the navigation.
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
            // Setting phase to 1 triggers exactly one render-phase re-run that normalizes it to 2: a total of
            // 1 mount + 2 attempts.
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
            // The discarded attempt never registers a "transient" predicate (registration is deferred to
            // settle) and the settled dep equals the committed dep, so the committed blocker stays functional.
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
    }
}
