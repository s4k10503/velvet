using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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
    /// state unchanged: a synchronous callback's once the work it queued commits, and an async action's when
    /// it completes or faults. A settle while any async action is in flight, the declaring component of the
    /// action unmounted or not, holds the entry until none is left.</item>
    /// <item>The Transition-lane render that lands the last work a transition queued already leaves its
    /// entries out, whether the component holding them is the one drained or a descendant its pass
    /// renders.</item>
    /// <item>An entry whose transition is still pending replays over a pass-through state another action
    /// changed.</item>
    /// <item>An entry its transition settled before any render showed it is shown once, then discarded.</item>
    /// <item>An entry added outside every scope while an async action is in flight belongs to the actions in
    /// flight, and is discarded once none is left. The count follows each action's task, and the subsystem
    /// reset puts it back to none.</item>
    /// <item>An entry no transition owns is discarded by the component's next Transition-lane render, which a
    /// parent pass subsuming the component does not cancel. Added outside every scope it logs a warning;
    /// added in a callback run by a starter whose component has unmounted it does not.</item>
    /// <item>A remounted fiber folds none of the previous mount's entries, and their action's settle does not
    /// render it.</item>
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
            AsyncActionsInFlightTestAccess.ResetForTest();
            ResetOptimistic();
            ResetActionHost();
            ResetSubsumingParent();
        }

        // The count follows each action's task, so one this fixture left awaiting is still counted after its
        // mount is disposed, and it must not decide ownership in the fixture that runs next.
        [TearDown]
        public void TearDown() => AsyncActionsInFlightTestAccess.ResetForTest();

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
            LogAssert.Expect(LogType.Warning, OutsideEveryTransitionWarning);

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
            LogAssert.Expect(LogType.Warning, OutsideEveryTransitionWarning);
            LogAssert.Expect(LogType.Warning, OutsideEveryTransitionWarning);

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
        public void Given_TwoPendingActions_When_TheFirstSettlesHavingChangedThePassthrough_Then_BothEntriesReplayOverTheNewStateUntilTheSecondSettles()
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
                Is.EqualTo("base+a+b|committed+a+b|committed"),
                "Entangled actions hold every entry, folded over the pass-through state, until the last completes");
        }

        [Test]
        public void Given_TwoPendingActions_When_TheLaterOneSettlesFirst_Then_NeitherEntryIsDiscardedUntilTheFirstSettlesToo()
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
            Assert.That($"{bothPending}|{firstPending}|{s_hostObserved}", Is.EqualTo("base+a+b|base+a+b|base"),
                "An action settling ahead of another in flight leaves its entry in place until that one settles");
        }

        [Test]
        public void Given_AnAsyncActionInFlight_When_ASyncTransitionAddingAnEntryCommitsItsWork_Then_TheEntryStaysUntilTheActionCompletes()
        {
            // Arrange — the transition starts after the action, so the action's entanglement is what holds the entry
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () => await gate.Task);
            s_hostStartSecond.Invoke(() =>
            {
                s_hostAdd.Invoke("sent");
                s_hostSetTick.Invoke(1);
            });
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "A transition started while an async action is in flight holds its entry until that action completes");
        }

        [Test]
        public void Given_AnEntryOwnedByAPendingAction_When_TheComponentDeclaringItsTransitionUnmounts_Then_TheEntryStaysUntilTheActionCompletes()
        {
            // Arrange — the action is still awaiting when its declaring component goes
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_childStart.Invoke(async () =>
            {
                s_hostAdd.Invoke("sent");
                await gate.Task;
            });
            DrainBothTiers(mounted);
            s_hostSetShowStarter.Invoke(false);
            DrainBothTiers(mounted);
            var afterUnmount = s_hostObserved;

            // Act
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{afterUnmount}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "The action counts until its task completes, the component that started it mounted or not");
        }

        [Test]
        public void Given_AnEntryAddedOutsideAnyTransition_When_TheNextTransitionLaneRenderRuns_Then_ItIsDiscarded()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            LogAssert.Expect(LogType.Warning, OutsideEveryTransitionWarning);
            s_hostAdd.Invoke("pending");

            // Act
            scheduler.DrainImmediateForTest();
            var shown = s_hostObserved;
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shown}|{s_hostObserved}", Is.EqualTo("base+pending|base"),
                "An entry no transition owns shows at the next render and is discarded by the Transition-lane one");
        }

        [Test]
        public void Given_AnAsyncActionWhoseWriteBeforeSuspendingCommits_When_ThatTransitionLaneRenderRuns_Then_TheEntryStaysUntilTheActionCompletes()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () =>
            {
                s_hostAdd.Invoke("sent");
                s_hostSetTick.Invoke(1);
                await gate.Task;
            });

            // Act — the delayed drain renders the component on the Transition lane with the entry still owned
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "A Transition-lane render drops only the entries no transition owns");
        }

        [Test]
        public void Given_AnUnownedEntryAlreadyShown_When_AParentPassSubsumesTheComponent_Then_TheTransitionLaneRenderStillDiscardsIt()
        {
            // Arrange — the subsumed render keeps only the lanes it asks for again, which is what this pins
            using var mounted = V.Mount(_root, V.Component(SubsumingParentRender, key: "subsuming-parent"));
            var scheduler = mounted.GetSchedulerForTest();
            LogAssert.Expect(LogType.Warning, OutsideEveryTransitionWarning);
            s_childAdd.Invoke("pending");
            scheduler.DrainImmediateForTest();
            var shown = s_childObserved;

            // Act
            s_parentSetTick.Invoke(1);
            scheduler.DrainImmediateForTest();
            var subsumed = s_childObserved;
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shown}|{subsumed}|{s_childObserved}", Is.EqualTo("base+pending|base+pending|base"),
                "A subsumed render that leaves the entry standing asks for the Transition-lane render again");
        }

        [Test]
        public void Given_AnEntryAddedByASyncCallbackThatQueuesNothing_When_TheCallbackReturns_Then_TheEntryIsShownOnceBeforeItIsDiscarded()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();

            // Act — the transition settles as the callback returns, ahead of the render addOptimistic asked for
            s_hostStartFirst.Invoke(() => s_hostAdd.Invoke("sent"));
            scheduler.DrainImmediateForTest();
            var shown = s_hostObserved;
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That($"{shown}|{s_hostObserved}", Is.EqualTo("base+sent|base"),
                "An entry no render showed before its transition settled is shown once, then discarded");
        }

        [Test]
        public void Given_AnAsyncActionInFlight_When_AnEntryIsAddedOutsideEveryScope_Then_ItStaysUntilTheActionCompletes()
        {
            // Arrange — the add stands for an action's code past its first suspension
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () => await gate.Task);
            s_hostAdd.Invoke("late");
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert — the pending reading is the one an unowned entry fails, the delayed drain having run
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+late|base"),
                "An entry added while an async action is in flight belongs to the actions in flight");
        }

        [Test]
        public void Given_AnEntryOwnedByTheActionsInFlight_When_TheOnlyOnesDeclaringComponentUnmounts_Then_TheEntryStaysUntilTheActionCompletes()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_childStart.Invoke(async () => await gate.Task);
            s_hostAdd.Invoke("late");
            DrainBothTiers(mounted);
            s_hostSetShowStarter.Invoke(false);
            DrainBothTiers(mounted);
            var afterUnmount = s_hostObserved;

            // Act
            gate.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{afterUnmount}|{s_hostObserved}", Is.EqualTo("base+late|base"),
                "An unmount does not count the action out, so the entry waits for the task");
        }

        [Test]
        public void Given_ASyncTransitionsEntryAlreadyShown_When_TheDrainLandingItsWorkRenders_Then_ThatRenderLeavesItOut()
        {
            // Arrange — the write to the same component is the work whose commit settles the transition
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_hostStartFirst.Invoke(() =>
            {
                s_hostAdd.Invoke("sent");
                s_hostSetTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = s_hostReadings.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That(s_hostReadings.Skip(shownBefore).FirstOrDefault(), Is.EqualTo("base"),
                "The first render the drain makes already shows the pass-through state");
        }

        [Test]
        public void Given_ADescendantHoldingASyncTransitionsEntry_When_TheParentsDrainLandsTheWork_Then_TheDescendantsRenderLeavesItOut()
        {
            // Arrange — the work is the parent's write, and the entry sits on the component its pass renders
            using var mounted = V.Mount(_root, V.Component(SubsumingParentRender, key: "subsuming-parent"));
            var scheduler = mounted.GetSchedulerForTest();
            s_parentStart.Invoke(() =>
            {
                s_childAdd.Invoke("sent");
                s_parentSetTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = s_childReadings.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That(s_childReadings.Skip(shownBefore).FirstOrDefault(), Is.EqualTo("base"),
                "The descendant commits with the parent's drain, so its first render there already omits the entry");
        }

        // GREEN_ON_BASE(characterization): the base keeps an entry through every render until the pass-through
        // state changes, so it holds this one too. What the case pins is that the leave-out this change adds
        // stays off while an action is in flight.
        [Test]
        public void Given_ASyncTransitionsEntryAlreadyShown_When_AnAsyncActionIsInFlightAtTheDrain_Then_ThatRenderKeepsIt()
        {
            // Arrange — the entry waits for the action, so the drain's commit is not what takes it down
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var scheduler = mounted.GetSchedulerForTest();
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () => await gate.Task);
            s_hostStartSecond.Invoke(() =>
            {
                s_hostAdd.Invoke("sent");
                s_hostSetTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = s_hostReadings.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That(s_hostReadings.Skip(shownBefore).FirstOrDefault(), Is.EqualTo("base+sent"),
                "The leave-out applies only where the transition settles with the commit");
        }

        // GREEN_ON_BASE(characterization): the base logged no warning from addOptimistic anywhere. What this
        // pins is that the warning this change adds stays out of a callback run by a starter whose component
        // has unmounted, which is a transition with nothing left to own the entry.
        [Test]
        public void Given_AStarterWhoseComponentUnmounted_When_ItsCallbackAddsAnEntry_Then_NoWarningIsLogged()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var start = s_childStart;
            s_hostSetShowStarter.Invoke(false);
            DrainBothTiers(mounted);

            // Act
            start.Invoke(() => s_hostAdd.Invoke("late"));
            DrainBothTiers(mounted);

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AFiberUnmountedWithAnEntryOutstanding_When_ItIsMountedAgainAndTheEntrysActionSettles_Then_NeitherReachesTheRemount()
        {
            // Arrange — the Unmount then Mount pair reuses one fiber, and the action is declared on another root,
            // so its settle reaches the remounted fiber only through a slot the unmount left enrolled
            s_passthrough = "base";
            var starter = FiberRenderer.CreateRoot(StarterRender);
            FiberRenderer.Mount(starter, new VisualElement());
            var optimistic = FiberRenderer.CreateRoot(OptimisticRender);
            FiberRenderer.Mount(optimistic, _root);
            var gate = new VelvetTaskCompletionSource();
            s_childStart.Invoke(async () =>
            {
                s_addOptimistic.Invoke("a");
                await gate.Task;
            });
            FiberWorkLoop.FlushState(optimistic);
            FiberRenderer.Unmount(optimistic);
            FiberRenderer.Mount(optimistic, _root);
            var afterRemount = s_observed;
            var rendersBefore = s_renderCount;

            // Act
            gate.TrySetResult();
            FiberWorkLoop.FlushState(optimistic);
            var extraRenders = s_renderCount - rendersBefore;
            FiberRenderer.Dispose(optimistic);
            FiberRenderer.Dispose(starter);

            // Assert
            Assert.That($"{afterRemount}|{extraRenders}", Is.EqualTo("base|0"),
                "A remount folds none of the previous mount's entries, and their action's settle asks it for nothing");
        }

        private static readonly Regex OutsideEveryTransitionWarning = new("added outside every transition");

        [Test]
        public void Given_TheResetEveryCaseHereRunsFirst_When_ItLooksUpWhatItResets_Then_TheSubsystemResetIsThere()
        {
            // Act — the reset returns quietly where its method is missing, so this is what notices a rename
            var reset = AsyncActionsInFlightTestAccess.FindSubsystemReset();

            // Assert
            Assert.That(reset, Is.Not.Null,
                "The set-up reset reaches the subsystem reset, or the ownership cases are order-dependent");
        }

        [Test]
        public void Given_AnEntryOwnedByTheActionsInFlight_When_TheSubsystemResetRuns_Then_TheEntryIsDiscarded()
        {
            // Arrange — an action the previous play session never completed
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var gate = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () => await gate.Task);
            s_hostAdd.Invoke("late");
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            AsyncActionsInFlightTestAccess.ResetForTest();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+late|base"),
                "The reset retires what the stale count held, on a root that is still mounted");
        }

        [Test]
        public void Given_AnActionCountedBeforeTheSubsystemReset_When_ItCompletesAfterwards_Then_ALaterActionsEntryStaysUntilThatOneCompletes()
        {
            // Arrange — the first action is counted out of the new count by the reset, and completes after it
            using var mounted = V.Mount(_root, V.Component(ActionHostRender, key: "action-host"));
            var stale = new VelvetTaskCompletionSource();
            var later = new VelvetTaskCompletionSource();
            s_hostStartFirst.Invoke(async () => await stale.Task);
            AsyncActionsInFlightTestAccess.ResetForTest();
            s_hostStartSecond.Invoke(async () => await later.Task);
            stale.TrySetResult();
            s_hostAdd.Invoke("late");
            DrainBothTiers(mounted);
            var whilePending = s_hostObserved;

            // Act
            later.TrySetResult();
            DrainBothTiers(mounted);

            // Assert
            Assert.That($"{whilePending}|{s_hostObserved}", Is.EqualTo("base+late|base"),
                "A completion from before the reset does not count the later action out");
        }

        // GREEN_ON_BASE(characterization): the base never leaves an entry out of a render, so the holder keeps
        // it there too. What this pins is the branch's half: the commit-time leave-out applies only to the
        // drain that lands the entry's own transition, never to an unrelated drain.
        [Test]
        public void Given_ATransitionEnrolledOnAnotherComponent_When_AnUnrelatedDrainRendersTheHolderOfItsEntry_Then_ThatRenderKeepsIt()
        {
            // Arrange — the entry's transition ends on the sibling, and the parent drains under another slot.
            // The parent's write is made first so its drain is the first one the delayed tier runs.
            using var mounted = V.Mount(_root, V.Component(SubsumingParentRender, key: "subsuming-parent"));
            var scheduler = mounted.GetSchedulerForTest();
            s_parentStartOther.Invoke(() => s_parentSetTick.Invoke(1));
            s_parentStart.Invoke(() =>
            {
                s_childAdd.Invoke("sent");
                s_siblingSetTick.Invoke(1);
            });
            scheduler.DrainImmediateForTest();
            var shownBefore = s_childReadings.Count;

            // Act
            scheduler.DrainDelayedForTest();

            // Assert
            Assert.That(s_childReadings.Skip(shownBefore).FirstOrDefault(), Is.EqualTo("base+sent"),
                "A drain that does not land the transition's last work leaves its entry in the render");
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
        private static int s_renderCount;
        private static Action<string> s_addOptimistic;

        private static void ResetOptimistic()
        {
            s_passthrough = null;
            s_observed = null;
            s_renderCount = 0;
            s_addOptimistic = null;
        }

        // Unwoven: the render count has to count every render the scheduler makes.
        [Component(Compiler = false)]
        private static VNode OptimisticRender()
        {
            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                s_passthrough,
                (current, action) => current + "+" + action);
            s_observed = optimisticState;
            s_renderCount++;
            s_addOptimistic = addOptimistic;
            return V.Label(text: optimisticState ?? string.Empty);
        }

        #endregion

        #region Subsuming parent

        private static string s_childObserved;
        private static readonly List<string> s_childReadings = new();
        private static Action<string> s_childAdd;
        private static StateUpdater<int> s_parentSetTick;
        private static TransitionStarter s_parentStart;
        private static TransitionStarter s_parentStartOther;
        private static StateUpdater<int> s_siblingSetTick;

        private static void ResetSubsumingParent()
        {
            s_childObserved = null;
            s_childReadings.Clear();
            s_childAdd = null;
            s_parentSetTick = default;
            s_parentStart = default;
            s_parentStartOther = default;
            s_siblingSetTick = default;
        }

        // Unwoven: the case needs every write to the parent to render it.
        [Component(Compiler = false)]
        private static VNode SubsumingParentRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            var (_, startOther) = Hooks.UseTransition();
            s_parentSetTick = setTick;
            s_parentStart = start;
            s_parentStartOther = startOther;
            return V.Div(children: new VNode[]
            {
                V.Component(SubsumedChildRender, key: "subsumed-child"),
                V.Component(SiblingRender, key: "sibling"),
            });
        }

        [Component(Compiler = false)]
        private static VNode SiblingRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_siblingSetTick = setTick;
            return V.Label(text: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode SubsumedChildRender()
        {
            var (optimisticState, addOptimistic) = Hooks.UseOptimistic<string, string>(
                "base",
                (current, action) => current + "+" + action);
            s_childObserved = optimisticState;
            s_childReadings.Add(optimisticState);
            s_childAdd = addOptimistic;
            return V.Label(text: optimisticState);
        }

        #endregion

        #region Action host component

        private static string s_hostObserved;
        private static readonly List<string> s_hostReadings = new();
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
            s_hostReadings.Clear();
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
            s_hostReadings.Add(optimisticState);
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
