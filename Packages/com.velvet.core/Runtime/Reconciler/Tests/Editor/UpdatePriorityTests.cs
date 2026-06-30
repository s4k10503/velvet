using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <see cref="FiberUpdatePriority"/> lane-queue contract for a function component's re-renders.
    /// <list type="bullet">
    /// <item>A Normal-lane update requires a flush to render; multiple Normal updates coalesce into a single
    /// render that commits the last value, and a setter call with an equal value skips the render.</item>
    /// <item>Lane routing by tier: Urgent and Normal enroll on the immediate (next-frame) tier; Deferred and
    /// Transition enroll on the delayed tier. Each tier's drain flushes only its own lanes.</item>
    /// <item>An Urgent lane drains and clears the dirty flag; once the queue is empty a further flush is a no-op.</item>
    /// <item>Deferred updates require a delayed flush and coalesce on the same fiber; Transition updates require a
    /// flush and coalesce, and a starved Transition lane is eventually promoted and processed.</item>
    /// <item>A fiber's lane queue drains lowest-value-first, one lane per flush; an Urgent update added to an
    /// already-Deferred fiber also enrolls it on the immediate tier so a synchronous immediate flush can commit it.</item>
    /// <item>A render-phase setState re-runs Render() synchronously within the same commit, leaves no pending
    /// next-frame work, and is bounded by <see cref="FiberBeginWork.RenderPhaseUpdateLimit"/>; the render-phase
    /// counter resets even when the re-run exits via an exception, leaving the fiber able to settle later.</item>
    /// <item>A setState raised inside a discrete event handler takes the Urgent lane and flushes synchronously at
    /// the handler's end; a setState outside any discrete handler stays on the Normal lane and requires a flush.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The UIToolkit scheduler does not advance in EditMode, so the Urgent and Deferred lanes are injected
    /// directly via <see cref="MountedTreeTestExtensions.ScheduleRerenderForTest"/>, tier routing is asserted
    /// against the tree-wide <see cref="FiberBatchScheduler"/>, and lane drain ordering against the per-fiber lane
    /// queue, which pops one lane per <see cref="MountedTreeTestExtensions.FlushStateForTest"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UpdatePriorityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            // IsInDiscreteEvent is a process-global static. Production restores it via RunDiscrete's finally, but
            // reset it here too so a test's lane assertions never depend on another test's teardown order.
            FiberWorkLoop.IsInDiscreteEvent = false;
            _root = new VisualElement();
            ResetSimple();
        }

        private static FiberBatchScheduler Scheduler(ComponentFiber fiber)
            => fiber.Reconciler.Context.BatchScheduler;

        #region Normal priority

        [Test]
        public void Given_NormalUpdate_When_SetterCalledWithoutFlush_Then_DoesNotRender()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(s_simpleRenderCount, Is.EqualTo(1), "Precondition: the mount rendered once");

            // Act
            s_simpleSetValue.Invoke("normal-update");

            // Assert
            Assert.AreEqual(1, s_simpleRenderCount, "A Normal-lane update does not render before the flush");
        }

        [Test]
        public void Given_NormalUpdate_When_Flushed_Then_RendersTheNewValue()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleSetValue.Invoke("normal-update");

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, "normal-update"), (s_simpleRenderCount, s_simpleLastValue));
        }

        [Test]
        public void Given_MultipleNormalUpdates_When_Flushed_Then_CoalescesToSingleRenderWithLastValue()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleSetValue.Invoke("update-1");
            s_simpleSetValue.Invoke("update-2");

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, "update-2"), (s_simpleRenderCount, s_simpleLastValue),
                "Multiple Normal updates coalesce into a single flush that commits the last value");
        }

        [Test]
        public void Given_EqualValue_When_SetterCalledAndFlushed_Then_SkipsRender()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleSetValue.Invoke("initial");
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_simpleRenderCount, "Setting an equal value schedules no re-render");
        }

        #endregion

        #region Urgent priority

        [Test]
        public void Given_UrgentUpdate_When_Scheduled_Then_EnrollsOnImmediateTierOnly()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Assert
            Assert.AreEqual((1, 0),
                (Scheduler(s_simpleFiber).ImmediatePendingCount, Scheduler(s_simpleFiber).DelayedPendingCount),
                "The Urgent lane routes to the immediate tier, not the delayed tier");
        }

        [Test]
        public void Given_UrgentUpdate_When_ImmediateDrained_Then_FiberRenders()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            Scheduler(s_simpleFiber).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(2, s_simpleRenderCount, "The immediate drain renders the Urgent-lane fiber");
        }

        [Test]
        public void Given_UrgentUpdate_When_Scheduled_Then_FiberIsDirty()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Assert
            Assert.IsTrue(s_simpleFiber.IsDirty, "Scheduling marks the fiber dirty");
        }

        [Test]
        public void Given_UrgentUpdate_When_SoleLaneDrained_Then_DirtyFlagClears()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            Scheduler(s_simpleFiber).DrainImmediateForTest();

            // Assert
            Assert.IsFalse(s_simpleFiber.IsDirty, "Draining the sole Urgent lane clears the dirty flag");
        }

        [Test]
        public void Given_EmptyQueue_When_Flushed_Then_IsNoOp()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            Scheduler(s_simpleFiber).DrainImmediateForTest();
            var before = s_simpleRenderCount;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(before, s_simpleRenderCount, "A flush after the queue is empty is a no-op");
        }

        #endregion

        #region Deferred priority

        [Test]
        public void Given_DeferredUpdate_When_Scheduled_Then_EnrollsOnDelayedTierOnly()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);

            // Assert
            Assert.AreEqual((1, 0),
                (Scheduler(s_simpleFiber).DelayedPendingCount, Scheduler(s_simpleFiber).ImmediatePendingCount),
                "The Deferred lane routes to the delayed tier, preserving its deferral");
        }

        [Test]
        public void Given_DeferredUpdate_When_ImmediateDrained_Then_DoesNotFlush()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);

            // Act
            Scheduler(s_simpleFiber).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(1, s_simpleRenderCount, "The immediate drain does not flush a Deferred-lane fiber");
        }

        [Test]
        public void Given_DeferredUpdate_When_DelayedDrained_Then_Flushes()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);

            // Act
            Scheduler(s_simpleFiber).DrainDelayedForTest();

            // Assert
            Assert.AreEqual(2, s_simpleRenderCount, "The delayed drain flushes the Deferred-lane fiber");
        }

        [Test]
        public void Given_RepeatedDeferredUpdates_When_Scheduled_Then_CoalesceToOneDelayedEntry()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);

            // Assert
            Assert.AreEqual(1, Scheduler(s_simpleFiber).DelayedPendingCount,
                "Repeated Deferred scheduling on the same fiber coalesces into one delayed entry");
        }

        [Test]
        public void Given_RepeatedDeferredUpdates_When_DelayedDrained_Then_RendersOnce()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);

            // Act
            Scheduler(s_simpleFiber).DrainDelayedForTest();

            // Assert
            Assert.AreEqual(2, s_simpleRenderCount, "The coalesced Deferred entry renders once");
        }

        #endregion

        #region Transition priority

        [Test]
        public void Given_TransitionUpdate_When_StartedWithoutFlush_Then_DoesNotRender()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));

            // Assert
            Assert.AreEqual(1, s_simpleRenderCount, "A Transition update does not render before the flush");
        }

        [Test]
        public void Given_TransitionUpdate_When_Flushed_Then_RendersTheNewValue()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, "transition-update"), (s_simpleRenderCount, s_simpleLastValue));
        }

        [Test]
        public void Given_TransitionUpdate_When_StartedTwiceAndFlushed_Then_CoalescesToSingleRenderWithLastValue()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-1"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-2"));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, "transition-2"), (s_simpleRenderCount, s_simpleLastValue),
                "Multiple Transition updates coalesce into one render that commits the last value");
        }

        [Test]
        public void Given_StarvedTransition_When_NormalUpdatesExceedThreshold_Then_TransitionIsPromotedAndProcessed()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));
            const int threshold = 30;
            for (var i = 0; i < threshold - 1; i++)
            {
                s_simpleSetValue.Invoke($"normal-{i}");
                mounted.FlushStateForTest();
            }
            s_simpleSetValue.Invoke($"normal-{threshold - 1}");
            mounted.FlushStateForTest();
            var renderCountBeforeFinal = s_simpleRenderCount;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.Greater(s_simpleRenderCount, renderCountBeforeFinal,
                "After starvation promotion, the Transition lane is processed and Render runs");
        }

        #endregion

        #region Lane queue ordering

        [Test]
        public void Given_UrgentAddedToDeferredFiber_When_Flushed_Then_DrainsUrgentLaneFirst()
        {
            // Arrange — Deferred (2) is queued first, then Urgent (0) joins the same fiber's queue
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, true), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "The first flush drains the higher-priority Urgent lane and leaves the Deferred lane pending");
        }

        [Test]
        public void Given_UrgentAddedToDeferredFiber_When_FlushedTwice_Then_DrainsRemainingDeferredLane()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            mounted.FlushStateForTest();

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((3, false), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "The second flush drains the remaining Deferred lane and clears the dirty flag");
        }

        [Test]
        public void Given_UrgentAddedToDeferredFiber_When_Scheduled_Then_EnrollsOnBothTiers()
        {
            // An Urgent update on an already-Deferred fiber must also enroll it on the immediate tier, otherwise
            // the end-of-discrete-event FlushImmediate (immediate tier only) can not commit it synchronously.
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Assert
            Assert.AreEqual((1, 1),
                (Scheduler(s_simpleFiber).ImmediatePendingCount, Scheduler(s_simpleFiber).DelayedPendingCount),
                "The Urgent lane enrolls the immediate tier while the original Deferred lane stays on the delayed tier");
        }

        [Test]
        public void Given_UrgentAddedToDeferredFiber_When_FlushImmediate_Then_DrainsUrgentLaneAndLeavesDeferredPending()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            Scheduler(s_simpleFiber).FlushImmediate();

            // Assert
            Assert.AreEqual((2, true), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "FlushImmediate drains the Urgent lane and leaves the Deferred lane pending");
        }

        [Test]
        public void Given_ThreeLanesOnOneFiber_When_FlushedRepeatedly_Then_DrainsLowestValueFirst()
        {
            // Arrange — Normal (1), Deferred (2), Transition (3) pending on one fiber
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Deferred);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            mounted.FlushStateForTest();
            var afterFirst = s_simpleRenderCount;
            mounted.FlushStateForTest();
            var afterSecond = s_simpleRenderCount;
            mounted.FlushStateForTest();

            // Assert — one lane per flush, lowest value first: Normal, then Deferred, then Transition
            Assert.AreEqual((2, 3, 4, false), (afterFirst, afterSecond, s_simpleRenderCount, s_simpleFiber.IsDirty),
                "Each flush pops exactly one lane in lowest-value-first order until the queue is empty");
        }

        #endregion

        #region Render-phase setState

        [Test]
        public void Given_RenderPhaseSetState_When_Mounted_Then_ReRunsSynchronouslyWithinTheCommit()
        {
            // Arrange
            ResetDerived();
            s_derivedTarget = "normalized";

            // Act
            using var mounted = V.Mount(_root, V.Component(DerivedRender, key: "derived"));

            // Assert
            Assert.AreEqual((2, "normalized"), (s_derivedRenderCount, s_derivedLastValue),
                "A render-phase setState re-runs Render() synchronously and the commit reflects the update");
        }

        [Test]
        public void Given_RenderPhaseSetState_When_Settled_Then_LeavesNoPendingNextFrameWork()
        {
            // Arrange
            ResetDerived();
            s_derivedTarget = "normalized";
            using var mounted = V.Mount(_root, V.Component(DerivedRender, key: "derived"));
            var before = s_derivedRenderCount;

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(before, s_derivedRenderCount, "A settled render-phase setState leaves no next-frame work");
        }

        [Test]
        public void Given_UnconditionalRenderPhaseSetState_When_Mounted_Then_BoundedByRenderPhaseUpdateLimit()
        {
            // Arrange
            ResetRunaway();
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*Too many re-renders"));

            // Act
            using var mounted = V.Mount(_root, V.Component(RunawayRender, key: "runaway"));

            // Assert — LogAssert.Expect verifies the runaway loop logged the "Too many re-renders" exception
            Assert.LessOrEqual(s_runawayRenderCount, FiberBeginWork.RenderPhaseUpdateLimit,
                "The render loop is bounded by RenderPhaseUpdateLimit");
        }

        [Test]
        public void Given_RenderPhaseSetStateThenThrow_When_FiberSurvives_Then_CounterResets()
        {
            // A render-phase setState bumps the counter, then the re-run throws. The fiber is not unmounted
            // (root-path exception preserves the previous tree), so the counter must reset on the exception path.
            // Arrange
            ResetThrowAfterBump();
            s_throwAfterBumpTarget = "normalized";
            s_throwAfterBumpShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*ThrowAfterBump boom"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowAfterBumpRender, key: "throw-after-bump"));

            // Assert — LogAssert.Expect verifies the re-run threw
            Assert.AreEqual(0, mounted.Root.RenderPhaseSetStateCounter,
                "The render-phase counter resets even when the loop exits via an exception");
        }

        [Test]
        public void Given_RecoveredFiber_When_NextRenderNormalizes_Then_SettlesWithoutTrippingTheLimit()
        {
            // Arrange — drive the fiber through the throwing render, then stop throwing
            ResetThrowAfterBump();
            s_throwAfterBumpTarget = "normalized";
            s_throwAfterBumpShouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException.*ThrowAfterBump boom"));
            using var mounted = V.Mount(_root, V.Component(ThrowAfterBumpRender, key: "throw-after-bump"));
            Assume.That(mounted.Root.RenderPhaseSetStateCounter, Is.EqualTo(0), "Precondition: the counter reset after the throw");

            // Act — a fresh render that itself does a render-phase setState (normalizes once)
            s_throwAfterBumpShouldThrow = false;
            s_throwAfterBumpTarget = "renormalized";
            s_throwAfterBumpSetRaw.Invoke("dirty");
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(("renormalized", 0), (s_throwAfterBumpLastValue, mounted.Root.RenderPhaseSetStateCounter),
                "The recovery render settles its normalization and returns the counter to zero");
        }

        #endregion

        #region Discrete event priority

        [Test]
        public void Given_DiscreteClickHandler_When_SetStateCalled_Then_SchedulesUrgentLane()
        {
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ButtonRender, key: "btn-host"));
            var button = _root.Q<Button>();
            Assume.That(button, Is.Not.Null, "Precondition: the component renders a Button");

            // Act
            button.SimulateClick();

            // Assert
            Assert.AreEqual(FiberUpdatePriority.Urgent, s_btnLaneInHandler,
                "A setState inside a discrete click handler schedules the Urgent lane");
        }

        [Test]
        public void Given_DiscreteClickHandler_When_HandlerEnds_Then_UpdateFlushesSynchronously()
        {
            // No manual flush: the discrete event brackets the handler and drains the immediate batch when it
            // returns, so the update is already committed.
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ButtonRender, key: "btn-host"));
            var button = _root.Q<Button>();
            Assume.That(button, Is.Not.Null);

            // Act
            button.SimulateClick();

            // Assert
            Assert.AreEqual((2, "clicked"), (s_btnRenderCount, s_btnValue),
                "A discrete-originated update flushes synchronously at the end of the handler");
        }

        [Test]
        public void Given_NonDiscreteSetState_When_Called_Then_StaysOnNormalLaneAndDoesNotFlushSynchronously()
        {
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ButtonRender, key: "btn-host"));

            // Act — a setter invoked outside any discrete event handler
            s_btnSetValue.Invoke("direct");

            // Assert
            Assume.That(s_btnFiber.LaneQueue, Is.Not.Null, "Precondition: an update is queued on the fiber");
            Assert.AreEqual((1, FiberUpdatePriority.Normal), (s_btnRenderCount, s_btnFiber.LaneQueue.Min),
                "Outside a discrete event, a setState stays on the Normal lane and does not flush synchronously");
        }

        [Test]
        public void Given_NonDiscreteSetState_When_Flushed_Then_RendersTheNewValue()
        {
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ButtonRender, key: "btn-host"));
            s_btnSetValue.Invoke("direct");

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, "direct"), (s_btnRenderCount, s_btnValue));
        }

        [Test]
        public void Given_DiscreteChangeHandler_When_ValueChanged_Then_SchedulesUrgentLane()
        {
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ToggleRender, key: "tg-host"));
            var toggle = _root.Q<Toggle>();
            Assume.That(toggle, Is.Not.Null, "Precondition: the component renders a Toggle");

            // Act
            toggle.SimulateChange(true);

            // Assert
            Assert.AreEqual(FiberUpdatePriority.Urgent, s_btnLaneInHandler,
                "A setState inside a discrete change handler schedules the Urgent lane");
        }

        [Test]
        public void Given_DiscreteChangeHandler_When_HandlerEnds_Then_UpdateFlushesSynchronously()
        {
            // Arrange
            ResetButton();
            using var mounted = V.Mount(_root, V.Component(ToggleRender, key: "tg-host"));
            var toggle = _root.Q<Toggle>();
            Assume.That(toggle, Is.Not.Null);

            // Act
            toggle.SimulateChange(true);

            // Assert
            Assert.AreEqual(2, s_btnRenderCount,
                "A discrete change-event update flushes synchronously at the end of the handler");
        }

        #endregion

        #region Derived-state component (render-phase setState that settles)

        private static string s_derivedTarget;
        private static string s_derivedLastValue;
        private static int s_derivedRenderCount;

        private static void ResetDerived()
        {
            s_derivedTarget = null;
            s_derivedLastValue = null;
            s_derivedRenderCount = 0;
        }

        [Component]
        private static VNode DerivedRender()
        {
            s_derivedRenderCount++;
            var (value, setValue) = Hooks.UseState("initial");
            // Render-phase normalization: drive the state toward the target exactly once. The setter bails out
            // via the equality check once value == target, so the loop settles.
            if (value != s_derivedTarget)
            {
                setValue.Invoke(s_derivedTarget);
            }
            s_derivedLastValue = value;
            return V.Label(text: value);
        }

        #endregion

        #region Runaway component (unconditional render-phase setState)

        private static int s_runawayRenderCount;

        private static void ResetRunaway()
        {
            s_runawayRenderCount = 0;
        }

        [Component]
        private static VNode RunawayRender()
        {
            s_runawayRenderCount++;
            var (value, setValue) = Hooks.UseState(0);
            // Unconditional render-phase setState: never bails out, so the render loop hits the limit.
            setValue.Invoke(value + 1);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region ThrowAfterBump component (render-phase setState then throw on the re-run)

        private static string s_throwAfterBumpTarget;
        private static bool s_throwAfterBumpShouldThrow;
        private static string s_throwAfterBumpLastValue;
        private static Action<string> s_throwAfterBumpSetRaw;

        private static void ResetThrowAfterBump()
        {
            s_throwAfterBumpTarget = null;
            s_throwAfterBumpShouldThrow = false;
            s_throwAfterBumpLastValue = null;
            s_throwAfterBumpSetRaw = null;
        }

        [Component]
        private static VNode ThrowAfterBumpRender()
        {
            var (value, setValue) = Hooks.UseState("initial");
            s_throwAfterBumpSetRaw = setValue;
            if (value != s_throwAfterBumpTarget)
            {
                // First attempt: render-phase setState bumps the counter before the re-run.
                setValue.Invoke(s_throwAfterBumpTarget);
            }
            else if (s_throwAfterBumpShouldThrow)
            {
                // Re-run attempt (value already normalized): throw with a non-zero counter so the exception path
                // is exercised while the fiber stays mounted (root-path recovery).
                throw new InvalidOperationException("ThrowAfterBump boom");
            }
            s_throwAfterBumpLastValue = value;
            return V.Label(text: value);
        }

        #endregion

        #region Simple component (UseState + UseTransition; for priority-switching tests)

        private static string s_simpleInitial;
        private static string s_simpleLastValue;
        private static int s_simpleRenderCount;
        private static Action<string> s_simpleSetValue;
        private static Action<Action> s_simpleStartTransition;
        private static ComponentFiber s_simpleFiber;

        private static void ResetSimple()
        {
            s_simpleInitial = null;
            s_simpleLastValue = null;
            s_simpleRenderCount = 0;
            s_simpleSetValue = null;
            s_simpleStartTransition = null;
            s_simpleFiber = null;
        }

        [Component]
        private static VNode SimpleRender()
        {
            s_simpleRenderCount++;
            // FiberAmbientStack.Current is the fiber whose body is executing; capture it so lane-injection tests
            // can target this fiber's lane queue directly (internal accessor via InternalsVisibleTo).
            s_simpleFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState(s_simpleInitial);
            s_simpleSetValue = setValue;
            s_simpleLastValue = value;
            var (_, start) = Hooks.UseTransition();
            s_simpleStartTransition = start;
            return V.Label(text: value);
        }

        #endregion

        #region Discrete event components

        private static int s_btnRenderCount;
        private static string s_btnValue;
        private static Action<string> s_btnSetValue;
        private static ComponentFiber s_btnFiber;
        private static FiberUpdatePriority? s_btnLaneInHandler;

        private static void ResetButton()
        {
            s_btnRenderCount = 0;
            s_btnValue = null;
            s_btnSetValue = null;
            s_btnFiber = null;
            s_btnLaneInHandler = null;
        }

        [Component]
        private static VNode ButtonRender()
        {
            s_btnRenderCount++;
            s_btnFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState("initial");
            s_btnSetValue = setValue;
            s_btnValue = value;
            return V.Button(
                text: value,
                onClick: () =>
                {
                    setValue.Invoke("clicked");
                    // Capture the lane the handler scheduled, before the end-of-event sync flush drains it. Read
                    // Min only when something is queued: SortedSet.Min returns default==Urgent on an empty set,
                    // which would let a dropped enqueue pass the assertion falsely.
                    var queue = s_btnFiber.LaneQueue;
                    s_btnLaneInHandler = queue != null && queue.Count > 0 ? queue.Min : (FiberUpdatePriority?)null;
                },
                key: "btn");
        }

        [Component]
        private static VNode ToggleRender()
        {
            s_btnRenderCount++;
            s_btnFiber = FiberAmbientStack.Current;
            var (on, setOn) = Hooks.UseState(false);
            return V.Toggle(
                value: on,
                onValueChanged: next =>
                {
                    setOn.Invoke(next);
                    // Capture the scheduled lane before the end-of-event sync flush drains it. Read Min only when
                    // something is queued (SortedSet.Min returns default==Urgent on an empty set).
                    var queue = s_btnFiber.LaneQueue;
                    s_btnLaneInHandler = queue != null && queue.Count > 0 ? queue.Min : (FiberUpdatePriority?)null;
                },
                key: "tg");
        }

        #endregion
    }
}
