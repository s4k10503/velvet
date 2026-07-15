using System;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies time-sliced reconciliation: a <c>Reconcile</c> with a frame budget commits as much as fits the
    /// budget, parks the rest, and <c>ContinueReconcile</c> resumes a parked diff to completion.
    /// <list type="bullet">
    /// <item>A zero (or omitted) budget reconciles synchronously: nothing is left pending and the full child list
    /// is committed in one call.</item>
    /// <item>A budget that is exceeded parks mid-diff with at least one node already committed; repeated
    /// <c>ContinueReconcile</c> drains the remaining work and clears <c>HasPendingWork</c>.</item>
    /// <item>Resuming a parked diff with a sufficient budget completes in a single call.</item>
    /// <item>Committed order is preserved across park/resume for the add, remove, and patch phases, including a
    /// full removal to an empty list (the resume terminates without looping).</item>
    /// <item>The keyed path park/resumes correctly across every sub-phase — the linear scan, tail-only add and
    /// remove, the reorder over a reversed list, a mixed add/remove/reorder, and duplicate keys (which warn) —
    /// and produces the new list's exact order.</item>
    /// <item>An unkeyed-to-keyed or keyed-to-unkeyed transition replaces the whole list under time-slicing.</item>
    /// <item>Starting a fresh <c>Reconcile</c> while a diff is parked clears the stale pending state and the new
    /// diff completes.</item>
    /// <item>Disposing while parked returns the pending buffers without throwing, and a fresh reconciler still
    /// works.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Drives the reconciler directly with an extremely small budget (0.001 ms) so a yield after one node is
    /// deterministic regardless of host speed. <see cref="DrainPendingWork"/> resumes until the work clears,
    /// failing with a diagnostic if a fixed iteration cap is reached.
    /// </remarks>
    [TestFixture]
    internal sealed class TimeSlicedReconcilerTests
    {
        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler.Dispose();
        }

        #region Synchronous (zero / omitted budget)

        [Test]
        public void Given_ZeroBudget_When_Reconciled_Then_NothingIsPending()
        {
            // Arrange
            var newChildren = new VNode[]
            {
                V.Label(text: "a"),
                V.Label(text: "b"),
                V.Label(text: "c"),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0);

            // Assert
            Assert.That(_reconciler.HasPendingWork, Is.False, "A zero-budget reconcile parks nothing");
        }

        [Test]
        public void Given_ZeroBudget_When_Reconciled_Then_AllChildrenCommitted()
        {
            // Arrange
            var newChildren = new VNode[]
            {
                V.Label(text: "a"),
                V.Label(text: "b"),
                V.Label(text: "c"),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0);

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(3), "A zero-budget reconcile commits the whole list in one call");
        }

        [Test]
        public void Given_OmittedBudget_When_ListReplaced_Then_CommitsNewListInOrder()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Label(text: "a"),
                V.Label(text: "b"),
            };
            var newChildren = new VNode[]
            {
                V.Label(text: "x"),
                V.Label(text: "y"),
                V.Label(text: "z"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "x", "y", "z" }),
                "Omitting the budget reconciles synchronously to the new list in order");
        }

        #endregion

        #region Park and resume

        [Test]
        public void Given_ExceededBudget_When_Reconciled_Then_AtLeastOneNodeCommittedImmediately()
        {
            // Arrange
            var newChildren = BuildLabelArray(100);

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0.001);

            // Assert
            Assert.That(_root.childCount, Is.GreaterThan(0), "A parked diff commits at least one node before yielding");
        }

        [Test]
        public void Given_ParkedDiff_When_ContinuedRepeatedly_Then_CommitsAllNodes()
        {
            // Arrange
            var newChildren = BuildLabelArray(100);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0.001);

            // Act
            DrainPendingWork();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(100), "Repeated resume drains every node to completion");
        }

        [Test]
        public void Given_ParkedDiff_When_ContinuedRepeatedly_Then_ClearsPendingWork()
        {
            // Arrange
            var newChildren = BuildLabelArray(50);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0.001);

            // Act
            DrainPendingWork();

            // Assert
            Assert.That(_reconciler.HasPendingWork, Is.False, "Resume clears the pending flag once the diff completes");
        }

        [Test]
        public void Given_ParkedDiff_When_ResumedWithSufficientBudget_Then_CompletesInOneCall()
        {
            // Arrange
            var newChildren = BuildLabelArray(10);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0.001);
            Assume.That(_reconciler.HasPendingWork, Is.True, "Precondition: the tiny budget parked the diff");

            // Act
            _reconciler.ContinueReconcile(frameBudgetMs: 10000);

            // Assert
            Assert.That(_reconciler.HasPendingWork, Is.False, "A single sufficient-budget resume completes the diff");
        }

        #endregion

        #region Phase ordering across park / resume

        [Test]
        public void Given_AddPhaseParked_When_Resumed_Then_AddedNodesRetainOrder()
        {
            // Arrange
            var newChildren = new VNode[]
            {
                V.Label(text: "label-0"),
                V.Label(text: "label-1"),
                V.Label(text: "label-2"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), newChildren, frameBudgetMs: 0.001);

            // Act
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "label-0", "label-1", "label-2" }),
                "Nodes added across yield/resume keep their declared order");
        }

        [Test]
        public void Given_RemovePhaseParked_When_Resumed_Then_ListShrinksToNewCount()
        {
            // Arrange
            var initialChildren = BuildLabelArray(5);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), initialChildren);
            var reducedChildren = BuildLabelArray(2);

            // Act
            _reconciler.Reconcile(_root, initialChildren, reducedChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(2), "A parked remove resumes to the new, smaller count");
        }

        [Test]
        public void Given_FullRemovalParked_When_Resumed_Then_ListEmptiesWithoutLooping()
        {
            // Arrange — commonLength = 0 (full removal). The resume must terminate rather than loop.
            var initialChildren = BuildLabelArray(3);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), initialChildren);

            // Act
            _reconciler.Reconcile(_root, initialChildren, Array.Empty<VNode>(), frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 100);

            // Assert
            Assert.That((_reconciler.HasPendingWork, _root.childCount), Is.EqualTo((false, 0)),
                "A full removal drains to an empty list and clears pending work without looping");
        }

        [Test]
        public void Given_CommonPhaseParked_When_Resumed_Then_PatchesEveryNodeInPlace()
        {
            // Arrange
            var oldChildren = new VNode[]
            {
                V.Label(text: "old-0"),
                V.Label(text: "old-1"),
                V.Label(text: "old-2"),
            };
            var newChildren = new VNode[]
            {
                V.Label(text: "new-0"),
                V.Label(text: "new-1"),
                V.Label(text: "new-2"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "new-0", "new-1", "new-2" }),
                "A parked common-phase patch resumes to patch every node in place");
        }

        #endregion

        #region Stale pending state

        [Test]
        public void Given_DiffParked_When_NewReconcileStarts_Then_StalePendingStateIsCleared()
        {
            // Arrange — park a large unkeyed diff, then start a fresh one with a sufficient budget on a new root.
            var firstChildren = BuildLabelArray(50);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), firstChildren, frameBudgetMs: 0.001);
            var currentRoot = new VisualElement();
            var secondChildren = BuildLabelArray(3);

            // Act
            _reconciler.Reconcile(currentRoot, Array.Empty<VNode>(), secondChildren, frameBudgetMs: 10000);

            // Assert
            Assert.That((_reconciler.HasPendingWork, currentRoot.childCount), Is.EqualTo((false, 3)),
                "A fresh reconcile discards the parked diff's stale state and completes the new one");
        }

        [Test]
        public void Given_KeyedDiffParked_When_NewReconcileStarts_Then_StaleKeyedStateIsCleared()
        {
            // Arrange — park a reversed keyed diff, then start a fresh keyed one on a new root.
            var firstOld = BuildKeyedLabelArray(50, "a");
            var firstNew = ReversedKeyedLabelArray(50, "a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), firstOld);
            _reconciler.Reconcile(_root, firstOld, firstNew, frameBudgetMs: 0.001);
            var anotherRoot = new VisualElement();
            var secondChildren = BuildKeyedLabelArray(3, "new");

            // Act
            _reconciler.Reconcile(anotherRoot, Array.Empty<VNode>(), secondChildren, frameBudgetMs: 10000);

            // Assert
            Assert.That((_reconciler.HasPendingWork, anotherRoot.childCount), Is.EqualTo((false, 3)),
                "A fresh keyed reconcile discards the parked keyed state and completes the new one");
        }

        #endregion

        #region Keyed path park / resume

        [Test]
        public void Given_SmallKeyedList_When_TimeSliced_Then_CompletesEventually()
        {
            // Arrange
            var children = new VNode[]
            {
                V.Label(key: "a", text: "a"),
                V.Label(key: "b", text: "b"),
            };

            // Act
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), children, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That((_reconciler.HasPendingWork, _root.childCount), Is.EqualTo((false, 2)),
                "A small keyed list reconciles to completion regardless of the tiny budget");
        }

        [Test]
        public void Given_KeyedLinearScan_When_TimeSliced_Then_PatchesAllNodesInOrder()
        {
            // Arrange — same key order on both sides, no element-type change, so only the linear scan runs.
            var oldChildren = BuildKeyedLabelArray(50, "old");
            var newChildren = BuildKeyedLabelArray(50, "new");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That((_root.childCount, ((Label)_root.ElementAt(0)).text, ((Label)_root.ElementAt(49)).text),
                Is.EqualTo((50, "new-0", "new-49")),
                "The keyed linear scan park/resumes to patch the full list in order");
        }

        [Test]
        public void Given_KeyedLinearScanWithTypeFlip_When_TimeSliced_Then_ReplacesElementWithNewType()
        {
            // Arrange — three same-key entries, the middle one flipping from Label to Button, so the
            // time-sliced linear scan's CanPatch=false replace branch runs under park/resume; every
            // other linear-scan spec here only exercises the CanPatch=true patch branch.
            var oldChildren = new VNode[]
            {
                V.Label(text: "a", key: "k0"),
                V.Label(text: "b", key: "k1"),
                V.Label(text: "c", key: "k2"),
            };
            var newChildren = new VNode[]
            {
                V.Label(text: "a2", key: "k0"),
                V.Button(text: "b2", key: "k1"),
                V.Label(text: "c2", key: "k2"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            Assume.That(_root.ElementAt(1), Is.InstanceOf<Label>(), "Precondition: k1 holds a Label");

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(_root.ElementAt(1), Is.InstanceOf<Button>());
        }

        [Test]
        public void Given_KeyedTailAdd_When_TimeSliced_Then_AddsAllInOrder()
        {
            // Arrange — the linear scan consumes all old, so the tail-add phase appends the remainder.
            var oldChildren = BuildKeyedLabelArray(5, "base");
            var newChildren = BuildKeyedLabelArray(30, "base");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 200);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ExpectedTexts("base", 30)),
                "A keyed tail-add park/resumes to append every new node in order");
        }

        [Test]
        public void Given_KeyedTailRemove_When_TimeSliced_Then_RemovesFromEnd()
        {
            // Arrange — the linear scan consumes all new, so the tail-remove phase drops the remainder.
            var oldChildren = BuildKeyedLabelArray(30, "x");
            var newChildren = BuildKeyedLabelArray(5, "x");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 200);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ExpectedTexts("x", 5)),
                "A keyed tail-remove park/resumes to keep the head and drop the tail");
        }

        [Test]
        public void Given_ReversedKeyedList_When_TimeSliced_Then_ProducesReversedOrder()
        {
            // Arrange — reversing forces the reorder phase to move every element (anchor set of size 1).
            var oldChildren = BuildKeyedLabelArray(20, "item");
            var newChildren = ReversedKeyedLabelArray(20, "item");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ReversedExpectedTexts("item", 20)),
                "A reversed keyed list park/resumes across every reorder sub-phase to the reversed order");
        }

        [Test]
        public void Given_MixedAddRemoveReorder_When_TimeSliced_Then_ProducesNewOrder()
        {
            // Arrange — old=[A..H], new=[H,A,X,C,Y,E,Z,G]: remove B,D,F; add X,Y,Z; move H to head.
            var oldChildren = new VNode[]
            {
                V.Label(key: "a", text: "A"),
                V.Label(key: "b", text: "B"),
                V.Label(key: "c", text: "C"),
                V.Label(key: "d", text: "D"),
                V.Label(key: "e", text: "E"),
                V.Label(key: "f", text: "F"),
                V.Label(key: "g", text: "G"),
                V.Label(key: "h", text: "H"),
            };
            var newChildren = new VNode[]
            {
                V.Label(key: "h", text: "H"),
                V.Label(key: "a", text: "A"),
                V.Label(key: "x", text: "X"),
                V.Label(key: "c", text: "C"),
                V.Label(key: "y", text: "Y"),
                V.Label(key: "e", text: "E"),
                V.Label(key: "z", text: "Z"),
                V.Label(key: "g", text: "G"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 300);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "H", "A", "X", "C", "Y", "E", "Z", "G" }),
                "A mixed add/remove/reorder park/resumes to the exact new order");
        }

        [Test]
        public void Given_ReversedKeyedList_When_ResumedWithSufficientBudget_Then_CompletesInOneStep()
        {
            // Arrange — 100 reversed elements + a tiny budget guarantee a yield.
            var oldChildren = BuildKeyedLabelArray(100, "k");
            var newChildren = ReversedKeyedLabelArray(100, "k");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            Assume.That(_reconciler.HasPendingWork, Is.True, "Precondition: the reversed list parked");

            // Act
            _reconciler.ContinueReconcile(frameBudgetMs: 10000);

            // Assert
            Assert.That((_reconciler.HasPendingWork, _root.childCount), Is.EqualTo((false, 100)),
                "A sufficient-budget resume completes the parked keyed reorder in one step");
        }

        [Test]
        public void Given_DuplicateKeys_When_TimeSliced_Then_WarnsAndProducesNewOrder()
        {
            // Arrange — a duplicate key drives the orphaned-old-index path; reordering forces the reorder phase.
            var oldChildren = new VNode[]
            {
                V.Label(key: "a", text: "A"),
                V.Label(key: "dup", text: "Dup1"),
                V.Label(key: "b", text: "B"),
                V.Label(key: "dup", text: "Dup2"),
                V.Label(key: "c", text: "C"),
                V.Label(key: "d", text: "D"),
                V.Label(key: "e", text: "E"),
                V.Label(key: "f", text: "F"),
            };
            var newChildren = new VNode[]
            {
                V.Label(key: "f", text: "F"),
                V.Label(key: "dup", text: "DupNew"),
                V.Label(key: "a", text: "A"),
                V.Label(key: "e", text: "E"),
                V.Label(key: "c", text: "C"),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate key detected"));

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "F", "DupNew", "A", "E", "C" }),
                "A duplicate-key keyed diff warns and still park/resumes to the new order");
        }

        #endregion

        #region Large keyed reorder (hinted-search rewrite — correctness at scale)

        // These assert final ORDER, so they guard that the hinted-search rewrite (IndexOfNear + RemoveAt) stays
        // order-faithful at the scale where the per-move scan term mattered — a wrong hint index would surface as a
        // transposed pair. They do not (and cannot, without a flaky wall-clock assert) measure the O(1)-vs-O(N)
        // lookup cost the change is about; that win is verified by out-of-band measurement.

        [Test]
        public void Given_LargeReversedKeyedList_When_TimeSliced_Then_ProducesExactReversedOrder()
        {
            // Arrange — at scale, a full reverse drives almost every element through the reorder walk (LIS size 1).
            const int n = 300;
            var oldChildren = BuildKeyedLabelArray(n, "item");
            var newChildren = ReversedKeyedLabelArray(n, "item");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 5000);

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ReversedExpectedTexts("item", n)),
                "A large reversed keyed list reorders to the exact reversed order");
        }

        [Test]
        public void Given_LargeRotatedKeyedList_When_TimeSliced_Then_ProducesExactRotatedOrder()
        {
            // Arrange — a rotate-by-k (every key shifts by k, wrapping) places elements with a different locality
            // than a pure reverse, exercising the search hint across reorder shapes, not just the reversed one.
            const int n = 200;
            const int k = 73;
            var oldChildren = BuildKeyedLabelArray(n, "r");
            var newChildren = new VNode[n];
            for (var i = 0; i < n; i++)
            {
                var idx = (i + k) % n;
                newChildren[i] = V.Label(text: $"r-{idx}", key: $"k{idx}");
            }
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork(maxIterations: 5000);

            // Assert
            var expected = new string[n];
            for (var i = 0; i < n; i++) expected[i] = $"r-{(i + k) % n}";
            Assert.That(LabelTexts(), Is.EqualTo(expected),
                "A large rotated keyed list reorders to the exact rotated order");
        }

        #endregion

        #region Keyed / unkeyed transition

        [Test]
        public void Given_UnkeyedToKeyed_When_TimeSliced_Then_ReplacesWholeList()
        {
            // Arrange — the linear scan breaks immediately, so the keyed path disposes all old and creates all new.
            var oldChildren = BuildLabelArray(20);
            var newChildren = BuildKeyedLabelArray(20, "k");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ExpectedTexts("k", 20)),
                "An unkeyed-to-keyed transition replaces the whole list under time-slicing");
        }

        [Test]
        public void Given_KeyedToUnkeyed_When_TimeSliced_Then_ReplacesWholeList()
        {
            // Arrange
            var oldChildren = BuildKeyedLabelArray(20, "k");
            var newChildren = BuildLabelArray(20);
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);

            // Act
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(ExpectedTexts("label", 20)),
                "A keyed-to-unkeyed transition replaces the whole list under time-slicing");
        }

        #endregion

        #region Dispose while parked

        [Test]
        public void Given_KeyedDiffParked_When_Disposed_Then_DoesNotThrow()
        {
            // Arrange
            var oldChildren = BuildKeyedLabelArray(100, "a");
            var newChildren = ReversedKeyedLabelArray(100, "a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            Assume.That(_reconciler.HasPendingWork, Is.True, "Precondition: a yield must have occurred");

            // Act + Assert — Dispose returns the pending-state buffers without throwing
            Assert.DoesNotThrow(() => _reconciler.Dispose());
        }

        [Test]
        public void Given_KeyedDiffParked_When_Disposed_Then_PendingWorkIsCleared()
        {
            // Arrange
            var oldChildren = BuildKeyedLabelArray(100, "a");
            var newChildren = ReversedKeyedLabelArray(100, "a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            Assume.That(_reconciler.HasPendingWork, Is.True, "Precondition: a yield must have occurred");

            // Act
            _reconciler.Dispose();

            // Assert
            Assert.That(_reconciler.HasPendingWork, Is.False, "Dispose clears the parked diff's pending work");
        }

        [Test]
        public void Given_ReconcilerDisposedWhileParked_When_FreshReconcilerUsed_Then_ItWorks()
        {
            // Arrange — dispose a reconciler mid-park, then exercise a fresh one to confirm shared pool state is intact.
            var oldChildren = BuildKeyedLabelArray(100, "a");
            var newChildren = ReversedKeyedLabelArray(100, "a");
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldChildren);
            _reconciler.Reconcile(_root, oldChildren, newChildren, frameBudgetMs: 0.001);
            Assume.That(_reconciler.HasPendingWork, Is.True, "Precondition: a yield must have occurred");
            _reconciler.Dispose();

            // Act
            _reconciler = new Reconciler();
            var freshRoot = new VisualElement();
            _reconciler.Reconcile(freshRoot, Array.Empty<VNode>(), BuildKeyedLabelArray(3, "x"));

            // Assert
            Assert.That(freshRoot.childCount, Is.EqualTo(3),
                "A fresh reconciler works after a parked one is disposed (pool state has no destructive side effects)");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Resumes via <c>ContinueReconcile</c> until <c>HasPendingWork</c> is false, failing with diagnostic
        /// context if the iteration cap is reached.
        /// </summary>
        private void DrainPendingWork(int maxIterations = 500, double budget = 0.001)
        {
            var iterations = 0;
            while (_reconciler.HasPendingWork)
            {
                if (iterations++ >= maxIterations)
                {
                    Assert.Fail($"DrainPendingWork: {maxIterations} iterations exceeded without completion");
                }
                _reconciler.ContinueReconcile(frameBudgetMs: budget);
            }
        }

        private string[] LabelTexts()
        {
            var texts = new string[_root.childCount];
            for (var i = 0; i < _root.childCount; i++)
            {
                texts[i] = ((Label)_root.ElementAt(i)).text;
            }
            return texts;
        }

        private static string[] ExpectedTexts(string prefix, int count)
        {
            var texts = new string[count];
            for (var i = 0; i < count; i++)
            {
                texts[i] = $"{prefix}-{i}";
            }
            return texts;
        }

        private static string[] ReversedExpectedTexts(string prefix, int count)
        {
            var texts = new string[count];
            for (var i = 0; i < count; i++)
            {
                texts[i] = $"{prefix}-{count - 1 - i}";
            }
            return texts;
        }

        private static VNode[] BuildLabelArray(int count)
        {
            var nodes = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                nodes[i] = V.Label(text: $"label-{i}");
            }
            return nodes;
        }

        private static VNode[] BuildKeyedLabelArray(int count, string prefix)
        {
            var nodes = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                nodes[i] = V.Label(text: $"{prefix}-{i}", key: $"k{i}");
            }
            return nodes;
        }

        private static VNode[] ReversedKeyedLabelArray(int count, string prefix)
        {
            var nodes = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                var idx = count - 1 - i;
                nodes[i] = V.Label(text: $"{prefix}-{idx}", key: $"k{idx}");
            }
            return nodes;
        }

        #endregion
    }
}
