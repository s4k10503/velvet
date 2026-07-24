using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a time-sliced (Transition-lane) reconcile which enqueues a <see cref="V.Portal"/> mount
    /// and then parks (frame budget exhausted) survives its own top-level finally draining that Portal in the
    /// SAME pass. The drain's Portal-target resolution re-enters <c>ChildReconciler.Reconcile</c> on the SAME
    /// instance, whose entry unconditionally discards the just-parked state as though it were stale leftovers
    /// from a finished pass; without pinning it across that nested call, the wipe destroys the continuation
    /// before the caller ever observes <see cref="Reconciler.HasPendingWork"/> — the remaining rows are then
    /// silently never created (a truncated commit, no error).
    /// <list type="bullet">
    /// <item>An unkeyed (Indexed-path) grow that creates a Portal as its very first new row, then parks under a
    /// tiny budget with that Portal still queued, still resumes and commits every trailing row once drained —
    /// and the Portal's own content mounts exactly once.</item>
    /// <item>The same shape on the keyed path (every row, including the Portal, carries a key) exercises
    /// <c>PendingKeyedState</c> instead of <c>PendingIndexedState</c> — the entry-clear this bug is about
    /// discards both unconditionally, regardless of which one is actually parked.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Registry-form <c>V.Portal(targetId:)</c> is used throughout: its target resolves at CREATE time, and its
    /// drain-time resolution falls through to the exact same nested-Reconcile call as the layer
    /// (<c>V.Portal(layer:)</c>) and <c>V.WorldSpace</c> arms once the target is known, so this one form
    /// exercises the shared fix for all three. The tiny <see cref="FiberLane.TimeSlicedBudgetOverrideForTest"/>
    /// forces a deterministic pause after one node (the UIToolkit scheduler does not advance in EditMode), and a
    /// parked slice is driven to completion manually via
    /// <see cref="MountedTreeTestExtensions.DrainTimeSlicedReconcileForTest"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class TimeSlicedPortalDrainParkTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            _root = new VisualElement();
            FiberPortalRegistry.Clear();
            ResetIndexedList();
            ResetKeyedList();
        }

        [TearDown]
        public void TearDown()
        {
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            FiberWorkLoop.IsInDiscreteEvent = false;
            FiberPortalRegistry.Clear();
        }

        [Test]
        public void Given_UnkeyedGrowEnqueuesPortalThenParks_When_DrainedAfterSamePassPortalDrain_Then_ResumesAndCommitsFullList()
        {
            // Arrange — initial mount is empty (no Portal, no rows), so the grow below creates the Portal fresh
            // (not a patch) as the reconcile's very first Add-phase iteration.
            var target = new VisualElement();
            FiberPortalRegistry.Register("indexed-park-target", target);
            using var mounted = V.Mount(_root, V.Component(IndexedListRender, key: "list"));
            var fiber = s_indexedListFiber;
            Assume.That(_root.childCount, Is.EqualTo(0), "Precondition: the initial mount renders no rows");

            // Act — grow to a Portal row + 40 trailing rows under a tiny budget: the very first Add iteration
            // (the Portal) both enqueues the deferred mount AND exhausts the budget, so this SAME FlushState call
            // parks with the Portal still queued — its own top-level finally (Reconciler.Reconcile) drains that
            // queue before FlushState returns, re-entering ChildReconciler.Reconcile on this fiber's own instance.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_indexedTotalCount = 41;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(fiber);
            fiber.DrainTimeSlicedReconcileForTest();

            // Assert — RED without the fix: the same-pass drain's nested Reconcile call wipes PendingIndexedState
            // the instant it is set, so HasPendingWork already reads false when FlushState returns; the drain
            // above is then a no-op and the list stays truncated at 1 (only the Portal placeholder) forever.
            Assert.That((_root.childCount, target.childCount), Is.EqualTo((41, 1)),
                "The parked pass resumes through the same-pass Portal drain and commits every trailing row, with the Portal's content mounted exactly once");
        }

        [Test]
        public void Given_KeyedGrowEnqueuesPortalThenParks_When_DrainedAfterSamePassPortalDrain_Then_ResumesAndCommitsFullList()
        {
            // Arrange — same shape, but every row (including the Portal) carries a key, so the reconcile takes
            // the Keyed path and parks into PendingKeyedState instead of PendingIndexedState.
            var target = new VisualElement();
            FiberPortalRegistry.Register("keyed-park-target", target);
            using var mounted = V.Mount(_root, V.Component(KeyedListRender, key: "list"));
            var fiber = s_keyedListFiber;
            Assume.That(_root.childCount, Is.EqualTo(0), "Precondition: the initial mount renders no rows");

            // Act — same tiny-budget grow as the unkeyed case, on the keyed path.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_keyedTotalCount = 41;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(fiber);
            fiber.DrainTimeSlicedReconcileForTest();

            // Assert — RED without the fix: DiscardPendingKeyedState (invoked by the same entry-clear) also
            // returns PendingKeyedState's pooled buffers to the shared pool, so the wipe is destructive twice
            // over here; the list stays truncated at 1 (only the Portal placeholder) forever.
            Assert.That((_root.childCount, target.childCount), Is.EqualTo((41, 1)),
                "The parked keyed pass resumes through the same-pass Portal drain and commits every trailing row, with the Portal's content mounted exactly once");
        }

        #region Indexed list component (unkeyed; Portal occupies index 0 once the count is nonzero)

        private static int s_indexedTotalCount;
        private static ComponentFiber s_indexedListFiber;

        private static void ResetIndexedList()
        {
            s_indexedTotalCount = 0;
            s_indexedListFiber = null;
        }

        [Component]
        private static VNode IndexedListRender()
        {
            s_indexedListFiber = FiberAmbientStack.Current;
            var total = s_indexedTotalCount;
            var children = new VNode[total];
            if (total > 0)
            {
                children[0] = V.Portal("indexed-park-target", children: new VNode[] { V.Label(text: "portal-content") });
                for (var i = 1; i < total; i++)
                {
                    children[i] = V.Label(text: $"row-{i}");
                }
            }
            return V.Fragment(children: children);
        }

        #endregion

        #region Keyed list component (every row, including the Portal, carries a key)

        private static int s_keyedTotalCount;
        private static ComponentFiber s_keyedListFiber;

        private static void ResetKeyedList()
        {
            s_keyedTotalCount = 0;
            s_keyedListFiber = null;
        }

        [Component]
        private static VNode KeyedListRender()
        {
            s_keyedListFiber = FiberAmbientStack.Current;
            var total = s_keyedTotalCount;
            var children = new VNode[total];
            if (total > 0)
            {
                children[0] = V.Portal("keyed-park-target", key: "portal",
                    children: new VNode[] { V.Label(text: "portal-content") });
                for (var i = 1; i < total; i++)
                {
                    children[i] = V.Label(text: $"row-{i}", key: $"row{i}");
                }
            }
            return V.Fragment(children: children);
        }

        #endregion
    }
}
