using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseOptimistic{TState,TAction}"/> in a function component.
    /// <list type="bullet">
    /// <item>With no optimistic update outstanding, the returned state equals the pass-through state.</item>
    /// <item>Invoking addOptimistic derives the optimistic state via the apply function and shows it immediately.</item>
    /// <item>Successive addOptimistic calls compose on top of the previous optimistic value.</item>
    /// <item>An entry added inside a transition is discarded when that transition settles, with the pass-through
    /// state unchanged: a synchronous callback's once the work it queued commits, an async action's when it
    /// completes or faults, and one whose declaring component unmounts at that unmount.</item>
    /// <item>An entry whose transition is still pending replays over a pass-through state another action
    /// changed, and one transition settling leaves another's entry in place.</item>
    /// <item>An entry added outside every transition is discarded by the component's next Transition-lane
    /// render.</item>
    /// <item>A null apply function raises an <see cref="ArgumentNullException"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region
    /// static fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c>. Each action case
    /// asserts the state its component rendered at every step as one joined reading.
    /// </remarks>
    [TestFixture]
    internal sealed class UseOptimisticTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetOptimistic();
            ResetActionHost();
        }

        [Test]
        public void Given_NoOptimisticUpdate_When_FirstRender_Then_ReturnsPassthroughState()
        {
            // Arrange
            s_passthrough = "base";

            // Act
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-init"));

            // Assert
            Assert.That(s_observed, Is.EqualTo("base"),
                "With no optimistic update outstanding, the pass-through state is returned");
        }

        [Test]
        public void Given_MountedComponent_When_AddOptimisticInvoked_Then_OptimisticStateIsShownImmediately()
        {
            // Arrange
            s_passthrough = "base";
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-add"));

            // Act
            s_addOptimistic.Invoke("pending");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("base+pending"),
                "addOptimistic derives the optimistic state via the apply function and shows it");
        }

        [Test]
        public void Given_OptimisticState_When_AddOptimisticInvokedAgain_Then_ComposesOnPreviousValue()
        {
            // Arrange
            s_passthrough = "base";
            using var mounted = V.Mount(_root, V.Component(OptimisticRender, key: "optimistic-compose"));

            // Act
            s_addOptimistic.Invoke("a");
            mounted.FlushStateForTest();
            s_addOptimistic.Invoke("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_observed, Is.EqualTo("base+a+b"),
                "Successive addOptimistic calls compose on top of the previous optimistic value");
        }

        [Test]
        public void Given_NullApplyFunction_When_UseOptimisticCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => Hooks.UseOptimistic<string, string>("x", null));
        }

        #region Action-scoped lifetime

        [Test]
        public void Given_AnEntryAddedInsideASyncTransition_When_TheTransitionSettlesWithThePassthroughUnchanged_Then_ThePassthroughStateIsShownAgain()
        {
            // Arrange — the unrelated write keeps the transition pending until the delayed tier commits it
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_hostStartFirst.Invoke(() =>
            {
                s_hostAdd.Invoke("sent");
                s_hostSetTick.Invoke(1);
            });

            // Act
            scheduler.DrainImmediateForTest();
            var whilePending = s_hostObserved;
            scheduler.DrainDelayedForTest();
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "The entry shows ahead of the transition's commit and is discarded once that transition settles");
        }

        [Test]
        public void Given_APendingTransitionsEntry_When_ItsStarterStartsAgainBeforeItSettles_Then_TheEntryStaysUntilThePendingStateClears()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_hostStartFirst.Invoke(() =>
            {
                s_hostAdd.Invoke("a");
                s_hostSetTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var firstPending = s_hostObserved;

            // Act — the second call lights a flag already lit, joining the lifecycle the first one opened
            s_hostStartFirst.Invoke(() => s_hostSetTick.Invoke(2));
            scheduler.DrainImmediateForTest();
            var joined = s_hostObserved;
            scheduler.DrainDelayedForTest();
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That($"{firstPending}|{joined}|{s_hostObserved}", Is.EqualTo("base+a|base+a|base"),
                "Only isPending clearing retires the entry, not a further start on the same starter");
        }

        [Test]
        public void Given_ATransitionThatRetiredAnEntry_When_ItsStarterRunsAgainAddingNothing_Then_TheOptimisticComponentIsNotRenderedAgain()
        {
            // Arrange — a callback that queues nothing settles as it returns
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            s_hostStartFirst.Invoke(() => s_hostAdd.Invoke("a"));
            DrainBothTiers(mounted);
            var afterFirst = s_hostObserved;
            var rendersBefore = s_hostRenderCount;

            // Act
            s_hostStartFirst.Invoke(() => { });
            DrainBothTiers(mounted);

            // Assert — the retired reading is folded in, since without it a hook that retires nothing renders
            // nothing either
            Assert.That($"{afterFirst}|{s_hostRenderCount - rendersBefore}", Is.EqualTo("base|0"),
                "A settled transition keeps no claim on the optimistic slot whose entries it already retired");
        }

        [Test]
        public void Given_AnEntryAddedByAnAsyncAction_When_TheActionCompletesWithThePassthroughUnchanged_Then_ThePassthroughStateIsShownAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () =>
            {
                s_hostAdd.Invoke("sent");
                await gate.Task;
            });
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "An action that completes without moving the pass-through state still discards its entry");
        }

        [Test]
        public void Given_AnEntryAddedByAnAsyncAction_When_TheActionFaults_Then_ThePassthroughStateIsShownAgain()
        {
            // Arrange — the starter sits under a boundary of its own, which the fault is thrown to
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"),
                new MountOptions((_, _) => { }));
            var gate = new VelvetTaskCompletionSource();
            s_childStart.Invoke(async () =>
            {
                s_hostAdd.Invoke("sent");
                await gate.Task;
            });
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            gate.TrySetException(new InvalidOperationException("rejected"));
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "A faulted action discards its entry as a completed one does");
        }

        [Test]
        public void Given_TwoPendingActions_When_TheFirstSettlesHavingChangedThePassthrough_Then_TheSecondsEntryReplaysOverTheNewState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () =>
            {
                s_hostAdd.Invoke("a");
                await first.Task;
                s_hostSetPassthrough.Invoke("committed");
            });
            s_hostStartSecond.Invoke(async () =>
            {
                s_hostAdd.Invoke("b");
                await second.Task;
            });
            DrainBothTiers(mounted);
            var bothPending = s_hostObserved;

            // Act
            first.TrySetResult();
            DrainBothTiers(mounted);
            var secondPending = s_hostObserved;
            second.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{bothPending}|{secondPending}|{s_hostObserved}",
                Is.EqualTo("base+a+b|committed+b|committed"),
                "The pending entry is folded over the pass-through state the settled action wrote");
        }

        [Test]
        public void Given_TwoPendingActions_When_TheLaterOneSettlesFirst_Then_OnlyItsEntryIsDiscarded()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var first = new VelvetTaskCompletionSource();
            var second = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () =>
            {
                s_hostAdd.Invoke("a");
                await first.Task;
            });
            s_hostStartSecond.Invoke(async () =>
            {
                s_hostAdd.Invoke("b");
                await second.Task;
            });
            DrainBothTiers(mounted);
            var bothPending = s_hostObserved;

            // Act
            second.TrySetResult();
            DrainBothTiers(mounted);
            var firstPending = s_hostObserved;
            first.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{bothPending}|{firstPending}|{s_hostObserved}", Is.EqualTo("base+a+b|base+a|base"),
                "One action settling discards its own entry and leaves the other pending action's in place");
        }

        [Test]
        public void Given_AnEntryOwnedByAPendingAction_When_TheComponentDeclaringItsTransitionUnmounts_Then_TheEntryIsDiscarded()
        {
            // Arrange — the action never completes, so the unmount is all that ends its transition
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_childStart.Invoke(async () =>
            {
                s_hostAdd.Invoke("sent");
                await gate.Task;
            });
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            s_hostSetShowStarter.Invoke(false);
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "Unmounting the component that declared the transition settles it, discarding the entry it owned");
        }

        [Test]
        public void Given_AnEntryAddedOutsideAnyTransition_When_TheNextTransitionLaneRenderRuns_Then_ItIsDiscarded()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_hostAdd.Invoke("pending");

            // Act
            scheduler.DrainImmediateForTest();
            var shown = s_hostObserved;
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shown}|{s_hostObserved}", Is.EqualTo("base+pending|base"),
                "An entry no transition owns shows at the next render and is discarded by the Transition-lane one");
        }

        private static void DrainBothTiers(MountedTree mounted)
        {
            var scheduler = mounted.GetSchedulerForTest();
            scheduler.DrainImmediateForTest();
            scheduler.DrainDelayedForTest();
            scheduler.DrainImmediateForTest();
        }

        #endregion

        #region Optimistic component

        private static string s_passthrough;
        private static string s_observed;
        private static Action<string> s_addOptimistic;

        private static void ResetOptimistic()
        {
            s_passthrough = null;
            s_observed = null;
            s_addOptimistic = null;
        }

        [Component]
        private static VNode OptimisticRender()
        {
            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                s_passthrough,
                (current, action) => current + "+" + action);
            s_observed = optimisticState;
            s_addOptimistic = addOptimistic;
            return V.Label(text: optimisticState ?? string.Empty);
        }

        #endregion

        #region Action host component

        private static string s_hostObserved;
        private static int s_hostRenderCount;
        private static Action<string> s_hostAdd;
        private static StateUpdater<string> s_hostSetPassthrough;
        private static StateUpdater<int> s_hostSetTick;
        private static StateUpdater<bool> s_hostSetShowStarter;
        private static TransitionStarter s_hostStartFirst;
        private static TransitionStarter s_hostStartSecond;
        private static TransitionStarter s_childStart;

        private static void ResetActionHost()
        {
            s_hostObserved = null;
            s_hostRenderCount = 0;
            s_hostAdd = null;
            s_hostSetPassthrough = default;
            s_hostSetTick = default;
            s_hostSetShowStarter = default;
            s_hostStartFirst = default;
            s_hostStartSecond = default;
            s_childStart = default;
        }

        // Unwoven: the render count has to count every render the scheduler makes.
        [Component(Compiler = false)]
        private static VNode ActionHostRender()
        {
            var (passthrough, setPassthrough) = Hooks.UseState("base");
            var (_, setTick) = Hooks.UseState(0);
            var (showStarter, setShowStarter) = Hooks.UseState(true);
            var (_, startFirst) = Hooks.UseTransition();
            var (_, startSecond) = Hooks.UseTransition();
            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                passthrough,
                (current, action) => current + "+" + action);
            s_hostObserved = optimisticState;
            s_hostRenderCount++;
            s_hostAdd = addOptimistic;
            s_hostSetPassthrough = setPassthrough;
            s_hostSetTick = setTick;
            s_hostSetShowStarter = setShowStarter;
            s_hostStartFirst = startFirst;
            s_hostStartSecond = startSecond;
            return V.Div(children: new VNode[]
            {
                V.Label(text: optimisticState),
                showStarter ? V.Component(StarterBoundaryRender, key: "starter-boundary") : null,
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode StarterBoundaryRender()
        {
            Hooks.UseFallback(ex => V.Label(text: ex.Message));
            return V.Component(StarterRender, key: "starter");
        }

        [Component]
        private static VNode StarterRender()
        {
            var (_, start) = Hooks.UseTransition();
            s_childStart = start;
            return V.Label(text: "starter");
        }

        #endregion
    }
}
