using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the ancestor↔descendant flush-coalescing contract: when a parent and one
    /// of its inline children are both dirtied within a single batch — in any enqueue order — each component
    /// renders exactly once in the single drain pass, and a higher-priority lane queued on the child is
    /// honored rather than dropped.
    /// <list type="bullet">
    /// <item>A child dirtied BEFORE its parent must not render twice. The parent's re-expansion of the
    /// child subsumes the child's own pending flush; the child renders once, not once for its own slot and
    /// again when the parent re-expands it.</item>
    /// <item>When the parent's re-expansion subsumes a still-dirty child, the child is removed from the
    /// batch scheduler's pending queue and its lane queue is settled, so a higher-priority lane queued on the
    /// child (e.g. Transition on the delayed tier) is not stranded and later dropped by FlushState's
    /// not-dirty early-return.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class FlushCoalescingParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
            _root = new VisualElement();
            ResetAll();
        }

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        [Test]
        public void Given_ChildDirtiedBeforeParent_When_BatchDrained_Then_ChildRendersExactlyOnce()
        {
            // Arrange — child setState enqueued BEFORE the parent setState in one handler (repro).
            using var mounted = MountParentChild();
            Assume.That((s_renderCountParent, s_renderCountChild), Is.EqualTo((1, 1)),
                "Precondition: parent and child each rendered once on mount");
            s_setChild.Invoke("c-updated");
            s_setParent.Invoke("p-updated");

            // Act — single coalesced drain.
            Scheduler(mounted).DrainImmediateForTest();

            // Assert — exactly one re-render each (count == 2 = mount + one batch render). The bug renders
            // the child twice (its own isolated slot flush + the parent's re-expansion), giving 3.
            Assert.AreEqual((2, 2), (s_renderCountParent, s_renderCountChild),
                "Parent and child each render exactly once in the single coalesced pass, regardless of enqueue order");
        }

        [Test]
        public void Given_ChildDirtiedBeforeParent_When_BatchDrained_Then_PendingSetEmpty()
        {
            // Arrange
            using var mounted = MountParentChild();
            s_setChild.Invoke("c-updated");
            s_setParent.Invoke("p-updated");
            Assume.That(Scheduler(mounted).ImmediatePendingCount, Is.EqualTo(2),
                "Precondition: both fibers pending on the immediate tier");

            // Act
            Scheduler(mounted).DrainImmediateForTest();

            // Assert
            Assert.AreEqual(0, Scheduler(mounted).ImmediatePendingCount,
                "The whole batch is drained — no fiber lingers in the pending set after the coalesced pass");
        }

        [Test]
        public void Given_ChildWithTransitionLane_When_ParentNormalFlushSubsumesChild_Then_ChildRemovedFromDelayedTier()
        {
            // Arrange — child enrolled on the delayed (Transition) tier, parent on the immediate (Normal) tier.
            // The immediate drain flushes the parent, whose re-expansion renders the still-dirty child. The
            // child's Transition enrollment on the delayed tier and its lane queue must be settled by that
            // subsumption rather than stranded (repro).
            using var mounted = MountParentChild();
            s_childFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parentFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            var scheduler = Scheduler(mounted);
            Assume.That((scheduler.ImmediatePendingCount, scheduler.DelayedPendingCount), Is.EqualTo((1, 1)),
                "Precondition: parent on the immediate tier, child on the delayed tier");

            // Act
            scheduler.DrainImmediateForTest();

            // Assert — the parent's re-expansion subsumed the child; it must not still be pending on the
            // delayed tier with a stranded lane that the next delayed drain would silently drop.
            Assert.AreEqual(0, scheduler.DelayedPendingCount,
                "The subsumed child is removed from the delayed tier (its lane is honored by the re-expansion, not dropped)");
            Assert.IsFalse(s_childFiber.IsDirty,
                "The subsumed child's dirty flag is cleared by the re-expansion");
            Assert.IsTrue(s_childFiber.LaneQueue == null || s_childFiber.LaneQueue.Count == 0,
                "The subsumed child's lane queue is settled, not left holding a stranded Transition lane");
        }

        [Test]
        public void Given_ChildWithTransitionLane_When_ParentSubsumesChild_Then_ChildRendersExactlyOnce()
        {
            // Arrange
            using var mounted = MountParentChild();
            s_childFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parentFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act — immediate drain (parent) then delayed drain (would re-flush a stranded child).
            Scheduler(mounted).DrainImmediateForTest();
            Scheduler(mounted).DrainDelayedForTest();

            // Assert — exactly one re-render of the child across both drains.
            Assert.AreEqual(2, s_renderCountChild,
                "The child renders exactly once (via the parent's re-expansion); the delayed drain finds nothing stranded");
        }

        private MountedTree MountParentChild()
        {
            var mounted = V.Mount(_root, V.Component(RenderParent, key: "parent"));
            s_parentFiber = mounted.Root.Child;
            s_childFiber = s_parentFiber?.Child;
            return mounted;
        }

        private static int s_renderCountParent;
        private static int s_renderCountChild;
        private static Action<string> s_setParent;
        private static Action<string> s_setChild;
        private static ComponentFiber s_parentFiber;
        private static ComponentFiber s_childFiber;

        private static void ResetAll()
        {
            s_renderCountParent = 0;
            s_renderCountChild = 0;
            s_setParent = null;
            s_setChild = null;
            s_parentFiber = null;
            s_childFiber = null;
        }

        // Compiler = false: these fixtures exercise the batch scheduler's lane coalescing / subsumption path,
        // which requires the parent to re-expand (re-render its children) on every flush. Auto-memo would let a
        // force-dirtied-but-unchanged parent memo-hit and skip re-expansion, so the subsumption under test would
        // not run. Opting out keeps these scheduler tests independent of the memoization axis.
        [Component(Compiler = false)]
        private static VNode RenderParent()
        {
            s_renderCountParent++;
            var (value, setValue) = Hooks.UseState("p");
            s_setParent = setValue;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Label(text: value),
                V.Component(RenderChild, key: "child"),
            });
        }

        [Component(Compiler = false)]
        private static VNode RenderChild()
        {
            s_renderCountChild++;
            var (value, setValue) = Hooks.UseState("c");
            s_setChild = setValue;
            return V.Label(name: "child", text: value);
        }
    }
}
