using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the cross-fiber auto-batching contract: setState on several fibers within one event handler
    /// coalesces into a single frame-boundary flush.
    /// <list type="bullet">
    /// <item>N cross-fiber setState calls in one handler schedule a single frame-boundary drain callback and
    /// enqueue all N dirty fibers on the same next-frame batch.</item>
    /// <item>Draining the batch renders each dirty fiber exactly once with no intermediate render between the
    /// updates, commits each fiber's latest value, and empties the pending set.</item>
    /// <item>Repeated setState on one fiber enqueues it once; the last value wins on the single coalesced render.</item>
    /// <item>The Normal lane lands on the immediate (next-frame) tier and the Transition lane on the delayed
    /// tier; each tier's drain flushes only its own fibers.</item>
    /// <item>The drain callback is anchored on the tree-stable root, so a fiber unmounted before the boundary is
    /// dropped from the pending queue while still-mounted fibers flush.</item>
    /// <item>The drain preserves enqueue order, flushing an earlier-dirtied fiber before a later one.</item>
    /// <item>A synchronous immediate flush is a no-op while a drain is already in progress; the update it
    /// schedules is deferred to the next-frame drain rather than committed re-entrantly.</item>
    /// <item>setState calls performed after an await in a plain async handler stay on the immediate tier and
    /// coalesce into a single next-frame drain — async auto-batching.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The UIToolkit scheduler does not advance in EditMode, so these tests inspect the tree-wide
    /// <see cref="FiberBatchScheduler"/> directly: <see cref="FiberBatchScheduler.ScheduledCallbackCount"/>
    /// asserts coalescing, the pending-count probes assert tier routing, and the test-only drain entry points
    /// simulate one frame-boundary callback firing.
    /// </remarks>
    [TestFixture]
    internal sealed class AutoBatchingTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetAll();
        }

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        [Test]
        public void Given_SettersOnThreeFibers_When_OneEventHandler_Then_SchedulesSingleDrainCallback()
        {
            // Arrange
            using var mounted = MountThree();
            var scheduler = Scheduler(mounted);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act — one synchronous event handler touching three different fibers
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Assert
            Assert.AreEqual(callbacksBefore + 1, scheduler.ScheduledCallbackCount,
                "Three cross-fiber setState calls coalesce into a single frame-boundary drain callback");
        }

        [Test]
        public void Given_SettersOnThreeFibers_When_OneEventHandler_Then_AllThreeQueueOnTheSameBatch()
        {
            // Arrange
            using var mounted = MountThree();

            // Act
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Assert
            Assert.AreEqual(3, Scheduler(mounted).ImmediatePendingCount,
                "All three dirty fibers are queued on the same next-frame batch");
        }

        [Test]
        public void Given_SettersOnThreeFibers_When_NotYetDrained_Then_NoIntermediateRender()
        {
            // Arrange
            using var mounted = MountThree();
            Assume.That((s_renderCountA, s_renderCountB, s_renderCountC), Is.EqualTo((1, 1, 1)),
                "Precondition: each fiber rendered once on mount");

            // Act
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Assert
            Assert.AreEqual((1, 1, 1), (s_renderCountA, s_renderCountB, s_renderCountC),
                "No render runs before the frame-boundary drain");
        }

        [Test]
        public void Given_SettersOnThreeFibers_When_BatchDrained_Then_EachRendersExactlyOnce()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual((2, 2, 2), (s_renderCountA, s_renderCountB, s_renderCountC),
                "Each fiber renders exactly once in the single batch pass");
        }

        [Test]
        public void Given_SettersOnThreeFibers_When_BatchDrained_Then_EachCommitsItsLatestValue()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(("a-updated", "b-updated", "c-updated"), (s_lastA, s_lastB, s_lastC));
        }

        [Test]
        public void Given_SettersOnThreeFibers_When_BatchDrained_Then_PendingSetIsEmpty()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            s_setC.Invoke("c-updated");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(0, Scheduler(mounted).ImmediatePendingCount, "The batch set is empty after draining");
        }

        [Test]
        public void Given_SameFiberSetTwice_When_Batched_Then_EnqueuedOnce()
        {
            // Arrange
            using var mounted = MountThree();

            // Act
            s_setA.Invoke("a-1");
            s_setA.Invoke("a-2");

            // Assert
            Assert.AreEqual(1, Scheduler(mounted).ImmediatePendingCount,
                "Repeated setState on the same fiber enqueues it once");
        }

        [Test]
        public void Given_SameFiberSetTwice_When_Drained_Then_LastValueWinsInOneRender()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-1");
            s_setA.Invoke("a-2");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual((2, "a-2"), (s_renderCountA, s_lastA),
                "The coalesced render runs once and commits the last value");
        }

        [Test]
        public void Given_MixedNormalAndTransition_When_Batched_Then_EachLaneSitsOnItsOwnTier()
        {
            // Arrange — Fiber A on the Normal lane (immediate tier), Fiber B on the Transition lane (delayed tier)
            using var mounted = MountThree();

            // Act
            s_setA.Invoke("a-normal");
            s_startTransitionB.Invoke(() => s_setB.Invoke("b-transition"));

            // Assert
            var scheduler = Scheduler(mounted);
            Assert.AreEqual((1, 1), (scheduler.ImmediatePendingCount, scheduler.DelayedPendingCount),
                "The Normal-lane fiber sits on the immediate tier and the Transition-lane fiber on the delayed tier");
        }

        [Test]
        public void Given_MixedNormalAndTransition_When_ImmediateDrained_Then_OnlyNormalLaneFiberFlushes()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-normal");
            s_startTransitionB.Invoke(() => s_setB.Invoke("b-transition"));

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual((2, 1), (s_renderCountA, s_renderCountB),
                "The immediate drain flushes the Normal-lane fiber and leaves the Transition-lane fiber pending");
        }

        [Test]
        public void Given_MixedNormalAndTransition_When_DelayedDrained_Then_TransitionLaneFiberFlushes()
        {
            // Arrange
            using var mounted = MountThree();
            s_setA.Invoke("a-normal");
            s_startTransitionB.Invoke(() => s_setB.Invoke("b-transition"));
            Scheduler(mounted).DrainImmediateForTest();

            // Act
            Scheduler(mounted).DrainDelayedForTest();

            // Assert
            Assert.AreEqual((2, "b-transition"), (s_renderCountB, s_lastB),
                "The delayed drain flushes the Transition-lane fiber and commits its value");
        }

        [Test]
        public void Given_LeadFiberUnmountedBeforeDrain_When_Unmounted_Then_DroppedFromPendingBatch()
        {
            // Arrange
            using var mounted = MountThree();
            var fiberA = mounted.Root.Child;
            Assume.That(fiberA, Is.Not.Null, "Precondition: the lead fiber is mounted");
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            Assume.That(Scheduler(mounted).ImmediatePendingCount, Is.EqualTo(2), "Precondition: both fibers are pending");

            // Act
            FiberRenderer.Unmount(fiberA);

            // Assert
            Assert.AreEqual(1, Scheduler(mounted).ImmediatePendingCount,
                "The unmounted lead fiber is removed from the pending batch");
        }

        [Test]
        public void Given_LeadFiberUnmountedBeforeDrain_When_BatchDrained_Then_SurvivingFiberStillFlushes()
        {
            // The drain callback is anchored on the tree-stable root mount element, not on the lead fiber's
            // mount point. Unity stops a scheduled item when its target VE detaches, so anchoring on a descendant
            // would strand still-mounted fibers when the lead unmounts before the next frame.
            // Arrange
            using var mounted = MountThree();
            // Inline children are appended in render order: Child = A, Child.Sibling = B.
            var fiberA = mounted.Root.Child;
            var fiberB = fiberA?.Sibling;
            Assume.That(fiberB, Is.Not.Null, "Precondition: the surviving fiber is mounted");
            s_setA.Invoke("a-updated");
            s_setB.Invoke("b-updated");
            FiberRenderer.Unmount(fiberA);

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual((2, "b-updated"), (s_renderCountB, s_lastB),
                "The surviving fiber is flushed and commits its value even though the lead unmounted");
        }

        [Test]
        public void Given_NestedParentAndChildBothDirty_When_Drained_Then_FlushesInEnqueueOrder()
        {
            // Arrange — the parent is dirtied before the child
            using var mounted = MountParentChild();
            s_orderLog.Clear();
            s_setParent.Invoke("p-updated");
            s_setChild.Invoke("c-updated");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(new[] { "parent", "child" }, s_orderLog.ToArray(),
                "The drain preserves enqueue order — the parent (enqueued first) flushes before the child");
        }

        [Test]
        public void Given_ImmediateFlushDuringDrain_When_DrainInProgress_Then_DiscreteUpdateIsDeferredNotCommitted()
        {
            // A FlushImmediate raised while a drain is on the stack must no-op: re-entering would re-enter the
            // reconciler at depth > 0 (corrupting shared abort / context-snapshot state) and clobber the shared
            // drain buffer. The sibling update it schedules stays queued on the immediate tier.
            // Arrange
            using var mounted = MountReentrantPair();
            Assume.That((s_reentrantHostRenderCount, s_reentrantSiblingRenderCount), Is.EqualTo((1, 1)),
                "Precondition: both fibers rendered once on mount");
            // Arm the host's layout effect and enroll it on the delayed tier. When the delayed drain commits the
            // host, the effect schedules an immediate-tier sibling update and calls FlushImmediate mid-drain.
            s_reentrantArmed = true;
            s_reentrantHostFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            Scheduler(mounted).DrainDelayedForTest();

            // Assert
            Assert.AreEqual(1, s_reentrantSiblingRenderCount,
                "FlushImmediate no-ops during a drain; the discrete sibling update is deferred, not committed re-entrantly");
        }

        [Test]
        public void Given_ImmediateFlushDuringDrain_When_NextFrameDrains_Then_DeferredUpdateCommits()
        {
            // Arrange
            using var mounted = MountReentrantPair();
            s_reentrantArmed = true;
            s_reentrantHostFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            Scheduler(mounted).DrainDelayedForTest();
            Assume.That(Scheduler(mounted).ImmediatePendingCount, Is.EqualTo(1),
                "Precondition: the deferred sibling update stays enrolled on the immediate tier");

            // Act — the next-frame immediate drain (no reconcile on the stack) commits it
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(2, s_reentrantSiblingRenderCount,
                "The deferred update commits on the next-frame immediate drain");
        }

        #region Async batching

        [Test]
        public void Given_TwoSettersAfterTheSameAwait_When_ContinuationRuns_Then_SchedulesSingleDrainCallback()
        {
            // Arrange
            using var mounted = MountThree();
            var scheduler = Scheduler(mounted);
            var gate = new UniTaskCompletionSource();
            RunAfterAwait(gate, () => { s_setA.Invoke("a-async"); s_setB.Invoke("b-async"); });
            var callbacksBefore = scheduler.ScheduledCallbackCount;
            Assume.That(scheduler.ImmediatePendingCount, Is.EqualTo(0), "Precondition: nothing queued while awaiting");

            // Act — completing the gate runs the continuation synchronously (no scheduler tick in EditMode)
            gate.TrySetResult();

            // Assert
            Assert.AreEqual(callbacksBefore + 1, scheduler.ScheduledCallbackCount,
                "Two setStates after the same await coalesce into a single frame-boundary drain callback");
        }

        [Test]
        public void Given_TwoSettersAfterTheSameAwait_When_ContinuationRuns_Then_BothQueueOnTheImmediateTier()
        {
            // Arrange
            using var mounted = MountThree();
            var gate = new UniTaskCompletionSource();
            RunAfterAwait(gate, () => { s_setA.Invoke("a-async"); s_setB.Invoke("b-async"); });

            // Act
            gate.TrySetResult();

            // Assert — post-await updates take the Normal lane (immediate tier), not the delayed/Transition tier
            Assert.AreEqual((2, 0), (Scheduler(mounted).ImmediatePendingCount, Scheduler(mounted).DelayedPendingCount),
                "Post-await setStates enqueue on the immediate tier, not the delayed tier");
        }

        [Test]
        public void Given_TwoSettersAfterTheSameAwait_When_NotYetDrained_Then_NoIntermediateRender()
        {
            // Arrange
            using var mounted = MountThree();
            Assume.That((s_renderCountA, s_renderCountB), Is.EqualTo((1, 1)), "Precondition: each rendered once on mount");
            var gate = new UniTaskCompletionSource();
            RunAfterAwait(gate, () => { s_setA.Invoke("a-async"); s_setB.Invoke("b-async"); });

            // Act
            gate.TrySetResult();

            // Assert
            Assert.AreEqual((1, 1), (s_renderCountA, s_renderCountB), "No render runs before the frame-boundary drain");
        }

        [Test]
        public void Given_TwoSettersAfterTheSameAwait_When_BatchDrained_Then_EachRendersExactlyOnce()
        {
            // Arrange
            using var mounted = MountThree();
            var gate = new UniTaskCompletionSource();
            RunAfterAwait(gate, () => { s_setA.Invoke("a-async"); s_setB.Invoke("b-async"); });
            gate.TrySetResult();

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual((2, 2), (s_renderCountA, s_renderCountB),
                "Each fiber renders exactly once for setStates batched after an await");
        }

        [Test]
        public void Given_TwoSettersAfterTheSameAwait_When_BatchDrained_Then_EachCommitsItsLatestValue()
        {
            // Arrange
            using var mounted = MountThree();
            var gate = new UniTaskCompletionSource();
            RunAfterAwait(gate, () => { s_setA.Invoke("a-async"); s_setB.Invoke("b-async"); });
            gate.TrySetResult();

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(("a-async", "b-async"), (s_lastA, s_lastB));
        }

        #endregion

        private MountedTree MountThree()
        {
            var tree = V.Div(name: "host", children: new VNode[]
            {
                V.Component(RenderA, key: "a"),
                V.Component(RenderB, key: "b"),
                V.Component(RenderC, key: "c"),
            });
            return V.Mount(_root, tree);
        }

        private MountedTree MountParentChild()
        {
            return V.Mount(_root, V.Component(RenderParent, key: "parent"));
        }

        // Runs `body` as a fire-and-forget async handler whose continuation resumes when `gate` completes,
        // standing in for a setState performed after an await in a plain (non-transition) async handler.
        private static void RunAfterAwait(UniTaskCompletionSource gate, Action body)
        {
            RunAsync(gate, body).Forget();
        }

        private static async UniTask RunAsync(
            UniTaskCompletionSource gate, Action body)
        {
            await gate.Task;
            body();
        }

        private static string s_lastA;
        private static string s_lastB;
        private static string s_lastC;
        private static int s_renderCountA;
        private static int s_renderCountB;
        private static int s_renderCountC;
        private static Action<string> s_setA;
        private static Action<string> s_setB;
        private static Action<string> s_setC;
        private static Action<Action> s_startTransitionB;

        private static void ResetAll()
        {
            s_lastA = s_lastB = s_lastC = null;
            s_renderCountA = s_renderCountB = s_renderCountC = 0;
            s_setA = s_setB = s_setC = null;
            s_startTransitionB = null;
            s_orderLog.Clear();
            s_renderCountParent = 0;
            s_renderCountChild = 0;
            s_setParent = null;
            s_setChild = null;
            s_reentrantHostRenderCount = 0;
            s_reentrantSiblingRenderCount = 0;
            s_reentrantHostFiber = null;
            s_reentrantSiblingTouch = null;
            s_reentrantScheduler = null;
            s_reentrantArmed = false;
        }

        [Component]
        private static VNode RenderA()
        {
            s_renderCountA++;
            var (value, setValue) = Hooks.UseState("a");
            s_setA = setValue;
            s_lastA = value;
            return V.Label(name: "a", text: value);
        }

        [Component]
        private static VNode RenderB()
        {
            s_renderCountB++;
            var (value, setValue) = Hooks.UseState("b");
            s_setB = setValue;
            s_lastB = value;
            var (_, start) = Hooks.UseTransition();
            s_startTransitionB = start;
            return V.Label(name: "b", text: value);
        }

        [Component]
        private static VNode RenderC()
        {
            s_renderCountC++;
            var (value, setValue) = Hooks.UseState("c");
            s_setC = setValue;
            s_lastC = value;
            return V.Label(name: "c", text: value);
        }

        private static readonly List<string> s_orderLog = new();
        private static int s_renderCountParent;
        private static int s_renderCountChild;
        private static Action<string> s_setParent;
        private static Action<string> s_setChild;

        [Component]
        private static VNode RenderParent()
        {
            s_renderCountParent++;
            if (s_renderCountParent > 1) s_orderLog.Add("parent");
            var (value, setValue) = Hooks.UseState("p");
            s_setParent = setValue;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Label(text: value),
                V.Component(RenderChild, key: "child"),
            });
        }

        [Component]
        private static VNode RenderChild()
        {
            s_renderCountChild++;
            if (s_renderCountChild > 1) s_orderLog.Add("child");
            var (value, setValue) = Hooks.UseState("c");
            s_setChild = setValue;
            return V.Label(name: "child", text: value);
        }

        private static int s_reentrantHostRenderCount;
        private static int s_reentrantSiblingRenderCount;
        private static ComponentFiber s_reentrantHostFiber;
        private static Action s_reentrantSiblingTouch;
        private static FiberBatchScheduler s_reentrantScheduler;
        private static bool s_reentrantArmed;

        [Component]
        private static VNode ReentrantHost()
        {
            s_reentrantHostRenderCount++;
            s_reentrantHostFiber = FiberAmbientStack.Current;
            s_reentrantScheduler = FiberAmbientStack.Current.Reconciler.Context.BatchScheduler;
            var (value, _) = Hooks.UseState("x");
            // No deps: runs after every commit. When armed, it stands in for a discrete-event boundary reached
            // while THIS fiber is being committed by the delayed-tier drain — it schedules an immediate-tier
            // update on a sibling and flushes it the way a discrete handler does at its end.
            Hooks.UseLayoutEffect(() =>
            {
                if (s_reentrantArmed)
                {
                    s_reentrantArmed = false;
                    s_reentrantSiblingTouch();
                    s_reentrantScheduler.FlushImmediate();
                }
                return (Action)null; // no cleanup; cast disambiguates the Func<Action> / Func<IDisposable> overloads
            });
            return V.Label(name: "reentrant-host", text: value);
        }

        [Component]
        private static VNode ReentrantSibling()
        {
            s_reentrantSiblingRenderCount++;
            var (value, setValue) = Hooks.UseState("a");
            s_reentrantSiblingTouch = () => setValue.Invoke("b");
            return V.Label(name: "reentrant-sib", text: value);
        }

        private MountedTree MountReentrantPair()
        {
            var tree = V.Div(name: "reentrant-root", children: new VNode[]
            {
                V.Component(ReentrantHost, key: "reentrant-host"),
                V.Component(ReentrantSibling, key: "reentrant-sib"),
            });
            return V.Mount(_root, tree);
        }
    }
}
