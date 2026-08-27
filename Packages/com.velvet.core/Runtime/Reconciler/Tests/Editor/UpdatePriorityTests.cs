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
    /// <item>Lane routing by tier: Urgent and Normal enroll on the immediate (next-frame) tier; Transition
    /// enrolls on the delayed tier. Each tier's drain flushes only its own lanes.</item>
    /// <item>An Urgent lane drains and clears the dirty flag; once the queue is empty a further flush is a no-op.</item>
    /// <item>Transition updates require a delayed flush and coalesce on the same fiber, and a starved Transition
    /// lane is promoted to Normal — and drained — by the flush that reaches the starvation threshold, however
    /// often the lane was re-scheduled while pending.</item>
    /// <item>A fiber's lane queue drains lowest-value-first, one lane per flush; an Urgent update added to a
    /// fiber already on the delayed tier also enrolls it on the immediate tier so a synchronous immediate flush
    /// can commit it.</item>
    /// <item>A render-phase setState re-runs Render() synchronously within the same commit, leaves no pending
    /// next-frame work, and is bounded by <see cref="FiberBeginWork.RenderPhaseUpdateLimit"/>; the render-phase
    /// counter resets even when the re-run exits via an exception, leaving the fiber able to settle later.</item>
    /// <item>A setState raised inside a discrete event handler takes the Urgent lane and flushes synchronously at
    /// the handler's end; a setState outside any discrete handler stays on the Normal lane and requires a flush.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The UIToolkit scheduler does not advance in EditMode, so the Urgent and Transition lanes are injected
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

        #region Delayed-tier routing

        // GREEN_ON_BASE(refactor): Transition already routed to the delayed tier and nowhere else.
        [Test]
        public void Given_TransitionLaneUpdate_When_Scheduled_Then_EnrollsOnDelayedTierOnly()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Assert
            Assert.AreEqual((1, 0),
                (Scheduler(s_simpleFiber).DelayedPendingCount, Scheduler(s_simpleFiber).ImmediatePendingCount),
                "The Transition lane routes to the delayed tier, preserving its deferral");
        }

        // GREEN_ON_BASE(refactor): the immediate drain already left a Transition enrolment alone.
        [Test]
        public void Given_TransitionLaneUpdate_When_ImmediateDrained_Then_LeavesItPendingUnflushed()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            Scheduler(s_simpleFiber).DrainImmediateForTest();

            // Assert — the enrolment travels in the assertion, or an unscheduled fiber satisfies the
            // render count on its own and the drain is asked nothing.
            Assert.AreEqual((1, 1),
                (s_simpleRenderCount, Scheduler(s_simpleFiber).DelayedPendingCount),
                "The immediate drain leaves the delayed-tier fiber unflushed and still enrolled");
        }

        // GREEN_ON_BASE(refactor): the delayed drain already flushed a Transition enrolment.
        [Test]
        public void Given_TransitionLaneUpdate_When_DelayedDrained_Then_Flushes()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            Scheduler(s_simpleFiber).DrainDelayedForTest();

            // Assert
            Assert.AreEqual(2, s_simpleRenderCount, "The delayed drain flushes the delayed-tier fiber");
        }

        // GREEN_ON_BASE(refactor): repeated enrolment on one lane already coalesced.
        [Test]
        public void Given_RepeatedTransitionLaneUpdates_When_Scheduled_Then_CoalesceToOneDelayedEntry()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Assert
            Assert.AreEqual(1, Scheduler(s_simpleFiber).DelayedPendingCount,
                "Repeated delayed-tier scheduling on the same fiber coalesces into one delayed entry");
        }

        // GREEN_ON_BASE(refactor): a coalesced delayed entry already rendered once.
        [Test]
        public void Given_RepeatedTransitionLaneUpdates_When_DelayedDrained_Then_RendersOnce()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            Scheduler(s_simpleFiber).DrainDelayedForTest();

            // Assert
            Assert.AreEqual(2, s_simpleRenderCount, "The coalesced delayed-tier entry renders once");
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
            Assert.AreEqual((3, "transition-update"), (s_simpleRenderCount, s_simpleLastValue),
                "The transition content render is followed by the render that observes isPending cleared");
        }

        [Test]
        public void Given_TransitionUpdate_When_StartedTwiceAndFlushed_Then_CoalescesContentAndClearsPending()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-1"));
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-2"));

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((3, "transition-2"), (s_simpleRenderCount, s_simpleLastValue),
                "Multiple Transition updates share one content render before the pending-clear render");
        }

        [Test]
        public void Given_StarvedTransition_When_NormalUpdatesReachThreshold_Then_TransitionIsPromotedAndDrainedInThatFlush()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(s_simpleRenderCount, Is.EqualTo(1), "Precondition: the mount rendered once");
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));
            const int threshold = 30;
            for (var i = 0; i < threshold - 1; i++)
            {
                s_simpleSetValue.Invoke($"normal-{i}");
                mounted.FlushStateForTest();
            }
            var renderCountBeforeFinal = s_simpleRenderCount;

            // Act — the flush that reaches the threshold promotes the starved lane to Normal and drains
            // it in the same pass; a mere hand-off to another still-outranked lane would keep losing to
            // the sustained Normal traffic.
            s_simpleSetValue.Invoke($"normal-{threshold - 1}");
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((renderCountBeforeFinal + 2, false), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "The promoted content coalesces with the preempting Normal render, then the pending-clear "
                + "render leaves no lane queued");
        }

        [Test]
        public void Given_SustainedTransitionRescheduling_When_NormalPreemptionReachesThreshold_Then_TransitionLaneIsPromoted()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(s_simpleRenderCount, Is.EqualTo(1), "Precondition: the mount rendered once");
            const int threshold = 30;

            // Act — the first pass genuinely enrols the Transition lane; every later pass re-signals
            // transition intent as a coalesced re-add onto the still-pending lane, while a fresh Normal
            // update preempts each flush, so the Transition lane never drains on its own. The starvation
            // clock measures continuous pendency, so the coalesced re-adds must not restart it: the
            // threshold flush must still promote and drain the starved lane.
            for (var i = 0; i < threshold; i++)
            {
                s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke($"transition-{i}"));
                s_simpleSetValue.Invoke($"normal-{i}");
                mounted.FlushStateForTest();
            }

            // Assert — the threshold flush promoted the starved lane to Normal and drained it in the
            // same pass, leaving nothing pending (a promote that merely re-queued it on a still-outranked
            // lane would keep starving under the sustained Normal traffic).
            Assert.AreEqual((false, false),
                (s_simpleFiber.LaneQueue.Contains(FiberUpdatePriority.Transition), s_simpleFiber.IsDirty),
                "Sustained Transition re-scheduling under Normal preemption must not defeat the "
                + "starvation promotion — the threshold flush drains the starved lane");
        }

        [Test]
        public void Given_StarvedTransitionPromotedBehindUrgent_When_ThresholdFlushDrainsUrgent_Then_IsPendingSurvives()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(s_simpleRenderCount, Is.EqualTo(1), "Precondition: the mount rendered once");
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));
            const int threshold = 30;
            for (var i = 0; i < threshold - 1; i++)
            {
                s_simpleSetValue.Invoke($"normal-{i}");
                mounted.FlushStateForTest();
            }

            // Act — the threshold flush finds an Urgent co-pending: promotion erases the Transition label
            // (relabelling the lane to Normal), the flush drains Urgent first, and the promoted work stays
            // queued one more pass.
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            mounted.FlushStateForTest();

            // Assert — the settle sweep must not read the erased Transition label as settled: isPending
            // survives until the commit that renders the promoted content.
            Assert.AreEqual((true, true),
                (s_simpleFiber.IsTransitionPending, s_simpleFiber.LaneQueue.Contains(FiberUpdatePriority.Normal)),
                "isPending survives the Urgent drain while the promoted lane is still queued");
        }

        [Test]
        public void Given_StarvedTransitionPromotedBehindUrgent_When_ThePromotedLaneDrains_Then_IsPendingClears()
        {
            // Arrange — same shape as the survival test above, driven one flush further.
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            Assume.That(s_simpleRenderCount, Is.EqualTo(1), "Precondition: the mount rendered once");
            s_simpleStartTransition.Invoke(() => s_simpleSetValue.Invoke("transition-update"));
            const int threshold = 30;
            for (var i = 0; i < threshold - 1; i++)
            {
                s_simpleSetValue.Invoke($"normal-{i}");
                mounted.FlushStateForTest();
            }
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            mounted.FlushStateForTest();
            Assume.That(s_simpleFiber.IsTransitionPending, Is.True,
                "Precondition: isPending survived the Urgent drain");

            // Act — drain the promoted lane: this is the commit that renders the promoted content.
            mounted.FlushStateForTest();

            // Assert — the promoted marker retires with its drain; the sweep must not skip forever.
            Assert.IsFalse(s_simpleFiber.IsTransitionPending,
                "isPending clears at the commit that drains the promoted lane");
        }

        #endregion

        #region Lane queue ordering

        // GREEN_ON_BASE(refactor): Urgent already outranked Transition in the same queue.
        [Test]
        public void Given_UrgentAddedToTransitionFiber_When_Flushed_Then_DrainsUrgentLaneFirst()
        {
            // Arrange — Transition (2) is queued first, then Urgent (0) joins the same fiber's queue
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((2, true), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "The first flush drains the higher-priority Urgent lane and leaves the Transition lane pending");
        }

        // GREEN_ON_BASE(refactor): the second flush already drained what the first left queued.
        [Test]
        public void Given_UrgentAddedToTransitionFiber_When_FlushedTwice_Then_DrainsRemainingTransitionLane()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            mounted.FlushStateForTest();

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual((3, false), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "The second flush drains the remaining Transition lane and clears the dirty flag");
        }

        // GREEN_ON_BASE(refactor): the escalation already enrolled both tiers for this pair.
        [Test]
        public void Given_UrgentAddedToTransitionFiber_When_Scheduled_Then_EnrollsOnBothTiers()
        {
            // An Urgent update on a fiber already on the delayed tier must also enroll it on the immediate tier,
            // otherwise the end-of-discrete-event FlushImmediate (immediate tier only) can not commit it
            // synchronously.
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));

            // Act
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Assert
            Assert.AreEqual((1, 1),
                (Scheduler(s_simpleFiber).ImmediatePendingCount, Scheduler(s_simpleFiber).DelayedPendingCount),
                "The Urgent lane enrolls the immediate tier while the original Transition lane stays on the delayed tier");
        }

        // GREEN_ON_BASE(refactor): FlushImmediate already left the delayed lane queued.
        [Test]
        public void Given_UrgentAddedToTransitionFiber_When_FlushImmediate_Then_DrainsUrgentLaneAndLeavesTransitionPending()
        {
            // Arrange
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            Scheduler(s_simpleFiber).FlushImmediate();

            // Assert
            Assert.AreEqual((2, true), (s_simpleRenderCount, s_simpleFiber.IsDirty),
                "FlushImmediate drains the Urgent lane and leaves the Transition lane pending");
        }

        // GREEN_ON_BASE(refactor): these three lanes already drained in this order.
        [Test]
        public void Given_ThreeLanesOnOneFiber_When_FlushedRepeatedly_Then_DrainsLowestValueFirst()
        {
            // Arrange — Urgent (0), Normal (1), Transition (2) pending on one fiber
            s_simpleInitial = "initial";
            using var mounted = V.Mount(_root, V.Component(SimpleRender, key: "simple"));
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_simpleFiber.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);

            // Act
            mounted.FlushStateForTest();
            var afterFirst = s_simpleRenderCount;
            mounted.FlushStateForTest();
            var afterSecond = s_simpleRenderCount;
            mounted.FlushStateForTest();

            // Assert — one lane per flush, lowest value first: Urgent, then Normal, then Transition
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
            Assume.That(s_btnFiber.LaneQueue.Count, Is.GreaterThan(0), "Precondition: an update is queued on the fiber");
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
                    // Min only when something is queued: FiberLaneSet.Min returns default==Urgent on an empty
                    // set, which would let a dropped enqueue pass the assertion falsely.
                    var queue = s_btnFiber.LaneQueue;
                    s_btnLaneInHandler = queue.Count > 0 ? queue.Min : (FiberUpdatePriority?)null;
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
                    // something is queued (FiberLaneSet.Min returns default==Urgent on an empty set).
                    var queue = s_btnFiber.LaneQueue;
                    s_btnLaneInHandler = queue.Count > 0 ? queue.Min : (FiberUpdatePriority?)null;
                },
                key: "tg");
        }

        #endregion
    }
}
