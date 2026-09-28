using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestMounts;
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
        public void Given_NoBlockers_When_Check_Then_ReportsNotBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();

            // Act
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_BlockingBlocker_When_Check_Then_ReportsBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => true, new RouteBlockerState());

            // Act
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(blocked, Is.True);
        }

        [Test]
        public void Given_BlockingBlocker_When_Check_Then_StateBecomesBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => true, state);

            // Act
            manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Blocked));
        }

        [Test]
        public void Given_AllowingBlocker_When_Check_Then_ReportsNotBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => false, new RouteBlockerState());

            // Act
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_AllowingBlocker_When_Check_Then_StateStaysIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => false, state);

            // Act
            manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(state.Status, Is.EqualTo(RouteBlockerStatus.Idle));
        }

        [Test]
        public void Given_AnAllowingBlockerRegisteredBeforeABlockingOne_When_Check_Then_ReportsBlocked()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => false, new RouteBlockerState());
            manager.Register(_ => true, new RouteBlockerState());

            // Act
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(blocked, Is.True);
        }

        [Test]
        public void Given_RegisteredBlocker_When_RegistrationDisposed_Then_CheckNoLongerSeesIt()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var registration = manager.Register(_ => true, new RouteBlockerState());

            // Act
            registration.Dispose();
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That(blocked, Is.False);
        }

        [Test]
        public void Given_ABlockedBlocker_When_ResetAll_Then_ItReturnsToIdle()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ => true, state);
            manager.Check(Attempt(), NoResume);
            var statusBeforeReset = state.Status;

            // Act
            manager.ResetAll();

            // Assert
            Assert.That((statusBeforeReset, state.Status),
                Is.EqualTo((RouteBlockerStatus.Blocked, RouteBlockerStatus.Idle)));
        }

        [Test]
        public void Given_ABlockerThatProceeded_When_CheckRunsAgain_Then_ItIsNotConsulted()
        {
            // Arrange — the resume is a no-op, so the pass under test is the next Check rather than
            // whatever Proceed() would have re-issued through a Router.
            var checks = 0;
            var manager = new RouteBlockerManager();
            var state = new RouteBlockerState();
            manager.Register(_ =>
            {
                checks++;
                return true;
            }, state);
            manager.Check(Attempt(), NoResume);
            state.Proceed();

            // Act
            var blocked = manager.Check(Attempt(), NoResume);

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
            manager.Check(Attempt(), NoResume);
            state.Proceed();

            // Act
            manager.SettleProceeding();
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert
            Assert.That((blocked, state.Status), Is.EqualTo((true, RouteBlockerStatus.Blocked)));
        }

        [Test]
        public void Given_ANullPredicate_When_Registered_Then_ThrowsArgumentNullException()
        {
            // Arrange
            var manager = new RouteBlockerManager();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => manager.Register(null, new RouteBlockerState()));
        }

        [Test]
        public void Given_ANullState_When_Registered_Then_ThrowsArgumentNullException()
        {
            // Arrange
            var manager = new RouteBlockerManager();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => manager.Register(_ => true, null));
        }

        [Test]
        public void Given_OneBlocker_When_Check_Then_NothingIsLogged()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            manager.Register(_ => false, new RouteBlockerState());

            // Act
            manager.Check(Attempt(), NoResume);

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_ARegistrationDisposedWhileTheBlockersStartOver_When_ResetAll_Then_TheWalkCompletes()
        {
            // Arrange — both Blockers hold a block, each blocking while it was the last registered, and the
            // later one's state change disposes both registrations before the walk reaches the earlier one.
            var manager = new RouteBlockerManager();
            var earlier = new RouteBlockerState();
            var later = new RouteBlockerState();
            var earlierRegistration = manager.Register(_ => true, earlier);
            manager.Check(Attempt(), NoResume);
            var laterRegistration = manager.Register(_ => true, later);
            manager.Check(Attempt(), NoResume);
            later.Changed = () =>
            {
                earlierRegistration.Dispose();
                laterRegistration.Dispose();
            };
            Exception caught = null;

            // Act
            try
            {
                manager.ResetAll();
            }
            catch (Exception exception)
            {
                caught = exception;
            }

            // Assert
            Assert.That((caught?.GetType().Name ?? "none", earlier.Status, later.Status),
                Is.EqualTo(("none", RouteBlockerStatus.Idle, RouteBlockerStatus.Idle)));
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

        #region What a Blocker is asked and what it exposes

        [Test]
        public void Given_ABlocker_When_ANavigationIsAttempted_Then_ItIsAskedWithBothLocationsAndTheHistoryAction()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            string seen = null;
            router.RouteBlockerManager.Register(args =>
            {
                seen = $"{args.CurrentLocation?.Path} {args.NextLocation.Path} {args.HistoryAction}";
                return false;
            }, new RouteBlockerState());

            // Act
            router.NavigateAsync("/other?tab=1", NavigationMode.Replace).GetAwaiter().GetResult();

            // Assert
            Assert.That(seen, Is.EqualTo("/home /other?tab=1 Replace"));
        }

        [Test]
        public void Given_ABlockedNavigation_When_TheBlockerIsRead_Then_ItsLocationIsTheDestination()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(state.Location?.Path, Is.EqualTo("/other"));
        }

        [Test]
        public void Given_AnIdleBlocker_When_Read_Then_ItOffersNeitherProceedNorReset()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => false, state);

            // Act
            router.NavigateSync("/other");

            // Assert — the committed path rides along, because a Blocker nothing ever consulted offers neither
            // either.
            Assert.That((router.CurrentLocation.Path, state.Proceed == null, state.Reset == null),
                Is.EqualTo(("/other", true, true)));
        }

        [Test]
        public void Given_AGuardRedirectingAnAttemptTheBlockerLetThrough_When_ItRedirects_Then_TheRedirectIsNotPutToTheBlocker()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("admin", guard: _ => "/login"), Route("login"));
            var seen = new List<string>();
            router.RouteBlockerManager.Register(args =>
            {
                seen.Add(args.NextLocation.Path);
                return false;
            }, new RouteBlockerState());

            // Act
            router.NavigateSync("/admin");

            // Assert
            Assert.That($"seen={string.Join(",", seen)} at={router.CurrentLocation.Path}",
                Is.EqualTo("seen=/admin at=/login"));
        }

        private static StateUpdater<bool> s_setShouldBlock;

        [Component]
        private static VNode BooleanBlockerRender()
        {
            var (shouldBlock, setShouldBlock) = Hooks.UseState(false);
            s_setShouldBlock = setShouldBlock;
            Hooks.UseBlocker(shouldBlock);
            return V.Label(text: shouldBlock ? "blocking" : "allowing");
        }

        [Test]
        public void Given_UseBlockerWithABoolean_When_ItTurnsTrue_Then_TheNextNavigationIsBlocked()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("a"), Route("b"));
            s_setShouldBlock = default;
            using var mounted = V.Mount(new VisualElement(),
                WithRouter(router, V.Component(BooleanBlockerRender, key: "blk")));
            var whileFalse = router.NavigateSync("/a");
            s_setShouldBlock.Invoke(true);
            mounted.FlushStateForTest();

            // Act
            var whileTrue = router.NavigateSync("/b");

            // Assert
            Assert.That((whileFalse, whileTrue), Is.EqualTo((NavigationResult.Success, NavigationResult.Blocked)));
        }

        [Test]
        public void Given_AProceedingBlocker_When_ItsReleasedNavigationLoads_Then_ItsLocationIsStillThatDestination()
        {
            // Arrange — the released navigation parks on its loader, which keeps the Blocker Proceeding.
            var loading = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home", Route("home"), Route("other", loader: (_, _) => loading.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/other");

            // Act
            state.Proceed();

            // Assert
            Assert.That((state.Status, state.Location?.Path),
                Is.EqualTo((RouteBlockerStatus.Proceeding, "/other")));
        }

        [Test]
        public void Given_AProceedingBlocker_When_Read_Then_ItOffersNeitherProceedNorReset()
        {
            // Arrange
            var loading = new VelvetTaskCompletionSource<object>();
            var router = BuildRouter("/home", Route("home"), Route("other", loader: (_, _) => loading.Task));
            var state = new RouteBlockerState();
            router.RouteBlockerManager.Register(_ => true, state);
            router.NavigateSync("/other");

            // Act
            state.Proceed();

            // Assert
            Assert.That((state.Status, state.Proceed == null, state.Reset == null),
                Is.EqualTo((RouteBlockerStatus.Proceeding, true, true)));
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

        // GREEN_ON_BASE(refactor): the mount now publishes the router the hook registers against, which it
        // used to find through Router.Current; what the case reads is unchanged.
        [Test]
        public void Given_MountedUseBlocker_When_Navigate_Then_CommittedPredicateBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), WithRouter(router, V.Component(RenderPhaseBlockerRender, key: "blk")));

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        // GREEN_ON_BASE(refactor): the mount now publishes the router the hook registers against, which it
        // used to find through Router.Current; what the case reads is unchanged.
        [Test]
        public void Given_RenderPhaseReRun_When_SettingOddPhase_Then_NormalizesToNextEvenInOneReRun()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), WithRouter(router, V.Component(RenderPhaseBlockerRender, key: "blk")));
            Assume.That(s_blockerRenderCount, Is.EqualTo(1), "Precondition: the initial mount rendered once");

            // Act
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_blockerRenderCount, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(refactor): the mount now publishes the router the hook registers against, which it
        // used to find through Router.Current; what the case reads is unchanged.
        [Test]
        public void Given_RenderPhaseReRun_When_NavigatingAfterSettle_Then_CommittedBlockerStaysRegistered()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), WithRouter(router, V.Component(RenderPhaseBlockerRender, key: "blk")));
            s_blockerObservedDep = null;
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            var result = router.NavigateSync("/other");

            // Assert
            Assert.That(result, Is.EqualTo(NavigationResult.Blocked));
        }

        // GREEN_ON_BASE(refactor): the mount now publishes the router the hook registers against, which it
        // used to find through Router.Current; what the case reads is unchanged.
        [Test]
        public void Given_RenderPhaseReRun_When_NavigatingAfterSettle_Then_SettledPredicateIsObserved()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            ResetBlockerComponent();
            using var mounted = V.Mount(new VisualElement(), WithRouter(router, V.Component(RenderPhaseBlockerRender, key: "blk")));
            s_blockerObservedDep = null;
            s_blockerSetPhase.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            router.NavigateSync("/other");

            // Assert
            Assert.That(s_blockerObservedDep, Is.EqualTo("settled"));
        }

        #endregion

        #region UseBlocker under a router that changes

        private static StateUpdater<Router> s_setProvidedRouter;
        private static Router s_initialRouter;

        [Component]
        private static VNode RouterSwitchingHost()
        {
            var (router, setRouter) = Hooks.UseState(s_initialRouter);
            s_setProvidedRouter = setRouter;
            return V.Provider(RouterContext.Router, router,
                new VNode[] { V.Component(StableDepsBlockerRender, key: "form") });
        }

        [Component]
        private static VNode StableDepsBlockerRender()
        {
            Hooks.UseBlocker(_ => true, "stable");
            return V.Label(text: "form");
        }

        [Test]
        public void Given_AUseBlockerWithStableDeps_When_TheRouterAboveItChanges_Then_ItBlocksTheNewRouterAndReleasesTheOld()
        {
            // Arrange
            var first = BuildRouter("/home", Route("home"), Route("other"));
            var second = BuildRouter("/home", Route("home"), Route("other"));
            s_initialRouter = first;
            s_setProvidedRouter = default;
            using var mounted = V.Mount(new VisualElement(), V.Component(RouterSwitchingHost, key: "host"));

            // Act
            s_setProvidedRouter.Invoke(second);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((first.NavigateSync("/other"), second.NavigateSync("/other")),
                Is.EqualTo((NavigationResult.Success, NavigationResult.Blocked)));
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

        // GREEN_ON_BASE(refactor): the mount now publishes the router the hook registers against, which it
        // used to find through Router.Current; what the case reads is unchanged.
        [Test]
        public void Given_UseBlockerWithDepsOmitted_When_TheCapturedStateChanges_Then_TheNewAnswerBlocks()
        {
            // Arrange
            var router = BuildRouter("/home", Route("home"), Route("other"));
            s_omittedDepsSetDirty = default;
            using var mounted = V.Mount(new VisualElement(), WithRouter(router, V.Component(OmittedDepsBlockerRender, key: "blk")));

            // Act — the first departure reads the mount render's false, the second the re-render's true.
            var beforeChange = router.NavigateSync("/other");
            s_omittedDepsSetDirty.Invoke(true);
            mounted.FlushStateForTest();
            var afterChange = router.NavigateSync("/home");

            // Assert
            Assert.That(
                (beforeChange, afterChange),
                Is.EqualTo((NavigationResult.Success, NavigationResult.Blocked)),
                "Omitting deps hands each committed render's predicate to the registration, so the blocker "
                + "answers with the state of the render that committed last rather than the mount render's");
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
        public void Given_ABlockedBlocker_When_ItsRegistrationIsDisposed_Then_ItsEntryLeavesTheList()
        {
            // Arrange
            var manager = new RouteBlockerManager();
            var registration = manager.Register(_ => true, new RouteBlockerState());
            manager.Check(Attempt(), NoResume);
            var entriesBeforeDispose = EntryCountOf(manager);

            // Act
            registration.Dispose();

            // Assert
            Assert.That((entriesBeforeDispose, EntryCountOf(manager)), Is.EqualTo((1, 0)));
        }

        #endregion

        #region Blocker liveness during Check

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
            var blocked = manager.Check(Attempt(), NoResume);

            // Assert — a dead registration neither blocks the navigation nor strands its state.
            Assert.That((blocked, state.Status), Is.EqualTo((false, RouteBlockerStatus.Idle)));
        }

        #endregion
    }
}
