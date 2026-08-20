using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a frame budget is wired through the lane queue so a fiber's own reconcile can pause and
    /// resume. <see cref="TimeSlicedReconcilerTests"/> covers the pause/resume machinery in isolation; these
    /// cover the integration.
    /// <list type="bullet">
    /// <item><see cref="FiberLane.BudgetForLane"/> gives the Urgent and Normal lanes a zero budget (they run
    /// synchronously and are never interrupted) and hands the Transition and Deferred lanes the time-sliced
    /// budget its caller supplied, so a large flat-list diff can pause.</item>
    /// <item>A Transition flush threads the time-sliced budget onto the fiber so the resume continues at the same
    /// budget — including a budget a caller supplied in place of the default; a Normal flush runs synchronously
    /// with a zero budget and never parks.</item>
    /// <item>A Transition flush over a large flat list parks mid-commit with only part of the new list committed,
    /// and the resume drains the remainder to completion.</item>
    /// <item>An immediate-tier flush no-ops while a reconcile is on the stack (the resume reentrancy guard) and
    /// commits once no reconcile is active.</item>
    /// <item>A layout effect is deferred until the paused commit completes, so it observes its ref'd element as
    /// attached rather than null.</item>
    /// <item>When a preceding inline-mount sibling's slot count changes while a following sibling is parked, the
    /// parked sibling's captured slot start is re-based so its resume lands after the preceding sibling's new
    /// prefix — for a single grow, a single shrink, two parked siblings re-based by one delta, a preceding
    /// sibling that is itself time-sliced (its delta committed across slices), and a nested growth absorbed by a
    /// wrapper (which must NOT shift the following sibling, since propagation is scoped to the shared mount
    /// point).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The UIToolkit scheduler does not advance in EditMode, so a parked slice is resumed manually via
    /// <see cref="MountedTreeTestExtensions.DrainTimeSlicedReconcileForTest"/>, and a pause is made deterministic
    /// regardless of host speed by flushing through
    /// <see cref="MountedTreeTestExtensions.FlushStateWithTinyBudgetForTest"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class TimeSlicedFiberTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
            _root = new VisualElement();
            ResetFlatList();
            ResetProbe();
            ResetRefOrdering();
            ResetSiblingShift();
            ResetRotation();
            ResetTransitionList();
        }

        [TearDown]
        public void TearDown()
        {
            FiberWorkLoop.IsInDiscreteEvent = false;
        }

        #region BudgetForLane

        [Test]
        public void Given_UrgentAndNormalLanes_When_BudgetQueried_Then_AreSynchronous()
        {
            // Act + Assert — Urgent and Normal are user-input-driven and run synchronously whatever time-sliced
            // budget the flush carries
            Assert.That(
                (FiberLane.BudgetForLane(FiberUpdatePriority.Urgent, ProbeTimeSlicedBudgetMs),
                    FiberLane.BudgetForLane(FiberUpdatePriority.Normal, ProbeTimeSlicedBudgetMs)),
                Is.EqualTo((0.0, 0.0)),
                "The Urgent and Normal lanes run synchronously with a zero budget");
        }

        [Test]
        public void Given_TransitionAndDeferredLanes_When_BudgetQueried_Then_AreTimeSliced()
        {
            // Act + Assert — Transition and Deferred slice against the budget the flush carries, so a large diff
            // can pause
            Assert.That(
                (FiberLane.BudgetForLane(FiberUpdatePriority.Transition, ProbeTimeSlicedBudgetMs),
                    FiberLane.BudgetForLane(FiberUpdatePriority.Deferred, ProbeTimeSlicedBudgetMs)),
                Is.EqualTo((ProbeTimeSlicedBudgetMs, ProbeTimeSlicedBudgetMs)),
                "The Transition and Deferred lanes slice against the time-sliced budget the flush carries");
        }

        // Distinct from both the synchronous budget (0) and the production default, so a routing that ignored
        // its argument cannot coincide with the expected value.
        private const double ProbeTimeSlicedBudgetMs = 7;

        #endregion

        #region Budget threading

        [Test]
        public void Given_TransitionFlush_When_Flushed_Then_TimeSlicedBudgetIsStoredOnFiber()
        {
            // Arrange
            s_flatListCount = 1;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            var fiber = s_flatListFiber;

            // Act
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(fiber);

            // Assert
            Assert.That(fiber.PendingReconcileBudgetMs, Is.GreaterThan(0.0),
                "A Transition flush threads the time-sliced budget so the resume continues at the same budget");
        }

        [Test]
        public void Given_TransitionFlushUnderATinyBudget_When_Flushed_Then_ThreadsThatBudgetNotTheDefault()
        {
            // Arrange
            s_flatListCount = 1;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            var fiber = s_flatListFiber;

            // Act
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();

            // Assert — every fixture that forces a park depends on this; without it those fixtures would run at
            // the production budget and never park, which is why each of them compares its parked state inside
            // its own assertion rather than gating on it
            Assert.That(fiber.PendingReconcileBudgetMs,
                Is.EqualTo(MountedTreeTestExtensions.TinyTimeSlicedBudgetMs),
                "A flush carries the caller's own time-sliced budget through to the fiber's resume budget");
        }

        [Test]
        public void Given_NormalFlush_When_Flushed_Then_RunsWithZeroBudget()
        {
            // Arrange
            s_flatListCount = 1;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            var fiber = s_flatListFiber;

            // Act
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(fiber);

            // Assert
            Assert.That(fiber.PendingReconcileBudgetMs, Is.EqualTo(0.0), "A Normal flush runs synchronously (zero budget)");
        }

        [Test]
        public void Given_NormalFlush_When_Flushed_Then_NeverParks()
        {
            // Arrange
            s_flatListCount = 1;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            var fiber = s_flatListFiber;

            // Act
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(fiber);

            // Assert
            Assert.That(fiber.HasPendingReconcileWorkForTest(), Is.False, "A synchronous flush never parks");
        }

        [Test]
        public void Given_TransitionFlushOnLargeList_When_BudgetTiny_Then_ParksMidCommit()
        {
            // Arrange
            s_flatListCount = 3;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            Assume.That(_root.childCount, Is.EqualTo(3), "Precondition: the initial mount is synchronous (budget 0)");
            var fiber = s_flatListFiber;

            // Act — a tiny budget forces a pause after one node so the park is deterministic in EditMode
            s_flatListCount = 40;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();

            // Assert
            Assert.That((fiber.HasPendingReconcileWorkForTest(), _root.childCount < 40), Is.EqualTo((true, true)),
                "The tiny budget parks the fast-path diff mid-commit with only part of the new list committed");
        }

        [Test]
        public void Given_ParkedTransitionFlush_When_Resumed_Then_CommitsFullList()
        {
            // Arrange
            s_flatListCount = 3;
            using var mounted = V.Mount(_root, V.Component(FlatListRender, key: "list"));
            var fiber = s_flatListFiber;
            s_flatListCount = 40;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();
            var parkedMidCommit = fiber.HasPendingReconcileWorkForTest();

            // Act
            fiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((parkedMidCommit, fiber.HasPendingReconcileWorkForTest(), _root.childCount),
                Is.EqualTo((true, false, 40)),
                "The flush parks mid-commit and the resume drains the remaining work, committing the full new list");
        }

        [Test]
        public void Given_UseTransitionWorkParksMidCommit_When_TheTerminalSliceCompletes_Then_IsPendingSpansTheWholeCommit()
        {
            // Arrange
            s_transitionListCount = 3;
            using var mounted = V.Mount(_root, V.Component(TransitionListRender, key: "transition-list"));
            s_transitionListStart.Invoke(() => s_transitionListSetCount.Invoke(40));

            // Act
            s_transitionListFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_transitionListFiber.HasPendingReconcileWorkForTest();
            var pendingWhileParked = s_transitionListFiber.IsTransitionPending;
            s_transitionListFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(
                (parked, pendingWhileParked, s_transitionListFiber.IsTransitionPending, _root.childCount),
                Is.EqualTo((true, true, false, 40)),
                "isPending stays lit through every parked slice and clears only after the terminal commit");
        }

        #endregion

        #region Reentrancy guard (resume on stack)

        [Test]
        public void Given_ReconcileActiveOnStack_When_FlushImmediate_Then_NoOps()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProbeRender, key: "probe"));
            Assume.That(s_probeRenderCount, Is.EqualTo(1), "Precondition: the probe mounted once");
            var scheduler = s_probeFiber.Reconciler.Context.BatchScheduler;
            s_probeSetValue();
            Assume.That(s_probeRenderCount, Is.EqualTo(1), "Precondition: the update is queued, not yet flushed");

            // Act — stand in for a discrete event dispatched while a resume is on the stack
            scheduler.SetReconcileActiveProbe(() => true);
            scheduler.FlushImmediate();

            // Assert
            Assert.That(s_probeRenderCount, Is.EqualTo(1),
                "FlushImmediate no-ops while a reconcile is on the stack (resume reentrancy guard)");
        }

        [Test]
        public void Given_NoReconcileOnStack_When_FlushImmediate_Then_DrainsQueuedUpdate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ProbeRender, key: "probe"));
            var scheduler = s_probeFiber.Reconciler.Context.BatchScheduler;
            s_probeSetValue();
            scheduler.SetReconcileActiveProbe(() => true);
            scheduler.FlushImmediate();
            Assume.That(s_probeRenderCount, Is.EqualTo(1), "Precondition: the guard suppressed the flush");

            // Act
            scheduler.SetReconcileActiveProbe(() => false);
            scheduler.FlushImmediate();

            // Assert
            Assert.That(s_probeRenderCount, Is.EqualTo(2),
                "FlushImmediate drains the queued update once no reconcile is on the stack");
        }

        #endregion

        #region UseLayoutEffect ordering across a paused commit

        [Test]
        public void Given_PausedCommit_When_LayoutEffectScheduled_Then_DeferredUntilCommitCompletes()
        {
            // Arrange
            s_refOrderingCount = 1;
            using var mounted = V.Mount(_root, V.Component(RefOrderingRender, key: "ref-ordering"));
            var fiber = s_refOrderingFiber;
            s_refOrderingEffectRan = false;
            s_refOrderingRefWasNullAtEffect = false;

            // Act
            s_refOrderingCount = 40;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();

            // Assert — "deferred" is a claim about a commit that is still in flight, so the pause has to be part
            // of the comparison: an effect that never ran at all reads identically on its own
            Assert.That((fiber.HasPendingReconcileWorkForTest(), s_refOrderingEffectRan), Is.EqualTo((true, false)),
                "UseLayoutEffect is deferred while the commit is paused (it would otherwise read a not-yet-attached ref)");
        }

        [Test]
        public void Given_DeferredLayoutEffect_When_CommitCompletes_Then_ObservesAttachedRef()
        {
            // Arrange
            s_refOrderingCount = 1;
            using var mounted = V.Mount(_root, V.Component(RefOrderingRender, key: "ref-ordering"));
            var fiber = s_refOrderingFiber;
            s_refOrderingEffectRan = false;
            s_refOrderingRefWasNullAtEffect = false;
            s_refOrderingCount = 40;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();
            var pausedMidFlight = fiber.HasPendingReconcileWorkForTest();

            // Act
            fiber.DrainTimeSlicedReconcileForTest();

            // Assert — the ref rides the LAST leaf (created only after the resume), yet the effect sees it attached
            Assert.That((pausedMidFlight, s_refOrderingEffectRan, s_refOrderingRefWasNullAtEffect),
                Is.EqualTo((true, true, false)),
                "After the paused commit completes the deferred layout effect runs once and observes its ref'd element attached");
        }

        #endregion

        #region Sibling-shift re-bases a parked following sibling

        [Test]
        public void Given_FollowingSiblingParked_When_PrecedingSiblingGrows_Then_ResumesAtRebasedSlot()
        {
            // Arrange — two inline siblings share one host: A occupies [0, Na), B occupies [Na, Na+Nb). B parks
            // mid-commit at slotStart = A's slot count.
            s_siblingACount = 3;
            s_siblingBCount = 3;
            using var mounted = V.Mount(_root, V.Component(SiblingHostRender, key: "host"));
            var host = _root.Q(name: "sibling-host");
            Assume.That(host.childCount, Is.EqualTo(6), "Precondition: the initial mount is 3 (A) + 3 (B)");
            s_siblingBCount = 30;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParkedAtASlotCount = s_siblingBFiber.HasPendingReconcileWorkForTest();

            // Act — A grows by 2 synchronously (Normal lane), which must re-base parked B's captured slotStart
            s_siblingACount = 5;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_siblingAFiber);
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((bParkedAtASlotCount, SiblingLayout(host, aCount: 5, bCount: 30)), Is.EqualTo((true, true)),
                "B parks, and its resumed body lands after A's grown prefix, in order, without corrupting either slot");
        }

        [Test]
        public void Given_FollowingSiblingParked_When_PrecedingSiblingShrinks_Then_ResumesAtRebasedSlot()
        {
            // Arrange — negative-delta counterpart: A removes rows while B is parked.
            s_siblingACount = 5;
            s_siblingBCount = 3;
            using var mounted = V.Mount(_root, V.Component(SiblingHostRender, key: "host"));
            var host = _root.Q(name: "sibling-host");
            Assume.That(host.childCount, Is.EqualTo(8), "Precondition: the initial mount is 5 (A) + 3 (B)");
            s_siblingBCount = 30;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParkedAtSlotStartFive = s_siblingBFiber.HasPendingReconcileWorkForTest();

            // Act — A shrinks 5 -> 2 synchronously, so B's parked slotStart must re-base by -3
            s_siblingACount = 2;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_siblingAFiber);
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((bParkedAtSlotStartFive, SiblingLayout(host, aCount: 2, bCount: 30)), Is.EqualTo((true, true)),
                "B parks, and its resumed body lands after A's left-shifted prefix, in order");
        }

        [Test]
        public void Given_TwoFollowingSiblingsParked_When_PrecedingSiblingGrows_Then_ReBasesBoth()
        {
            // Arrange — three inline siblings A,B,C share one host. B and C each park on a same-count reorder, so
            // neither changes the other's slot count; only A's later growth shifts both.
            s_siblingACount = 2;
            s_siblingBCount = 20;
            s_siblingCCount = 20;
            using var mounted = V.Mount(_root, V.Component(ThreeSiblingHostRender, key: "host"));
            var host = _root.Q(name: "three-sibling-host");
            Assume.That(host.childCount, Is.EqualTo(42), "Precondition: the initial mount is 2 (A) + 20 (B) + 20 (C)");
            s_siblingBReversed = true;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParked = s_siblingBFiber.HasPendingReconcileWorkForTest();
            s_siblingCReversed = true;
            s_siblingCFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingCFiber.FlushStateWithTinyBudgetForTest();
            var cParked = s_siblingCFiber.HasPendingReconcileWorkForTest();

            // Act — A grows 2 -> 4 synchronously while both B and C are parked
            s_siblingACount = 4;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_siblingAFiber);
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();
            s_siblingCFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the shift loop walks every following sibling, so A's single delta re-bases both B and C
            Assert.That((bParked, cParked, ThreeSiblingReversedLayout(host, aCount: 4, bCount: 20, cCount: 20)),
                Is.EqualTo((true, true, true)),
                "Both B and C park, and A's single delta re-bases both; each resumes its reversed order at its rebased slot");
        }

        [Test]
        public void Given_MiddleIndexedSiblingParked_When_PrecedingSiblingGrows_Then_ResumesWithinRebasedLimit()
        {
            // Arrange — three inline siblings A,B,C share one host. B is the MIDDLE sibling, so its slot range is
            // upper-bounded by C's MountSlotStart (a real SlotLimit, unlike a last tenant's int.MaxValue). B grows
            // via an unkeyed count change (the indexed path, whose Common phase consults SlotLimit on every
            // resumed iteration) and parks mid-commit.
            s_siblingACount = 2;
            s_siblingBCount = 20;
            s_siblingCCount = 20;
            using var mounted = V.Mount(_root, V.Component(ThreeSiblingHostRender, key: "host"));
            var host = _root.Q(name: "three-sibling-host");
            Assume.That(host.childCount, Is.EqualTo(42), "Precondition: the initial mount is 2 (A) + 20 (B) + 20 (C)");
            s_siblingBCount = 30;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParkedMidCommit = s_siblingBFiber.HasPendingReconcileWorkForTest();

            // Act — A grows 2 -> 5 synchronously; B's parked SlotLimit (= C's MountSlotStart) must re-base by +3
            // too, or the resumed Common-phase seam check rejects B's own trailing rows as out-of-range and
            // re-creates them at the C boundary instead of patching them in place.
            s_siblingACount = 5;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_siblingAFiber);
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((bParkedMidCommit, ThreeSiblingAscendingLayout(host, aCount: 5, bCount: 30, cCount: 20)),
                Is.EqualTo((true, true)),
                "B parks and resumes within its rebased SlotLimit, patching its trailing rows in place instead of mis-creating them at the C seam");
        }

        [Test]
        public void Given_FollowingSiblingParked_When_PrecedingSiblingItselfTimeSliced_Then_StaysAlignedAcrossSlices()
        {
            // Arrange — the preceding sibling A is itself time-sliced, so its child-count delta commits
            // incrementally across multiple resume slices. Each of A's slices must propagate its partial delta to
            // the parked following sibling B, or B's captured slotStart goes stale.
            s_siblingACount = 2;
            s_siblingBCount = 3;
            using var mounted = V.Mount(_root, V.Component(SiblingHostRender, key: "host"));
            var host = _root.Q(name: "sibling-host");
            Assume.That(host.childCount, Is.EqualTo(5), "Precondition: the initial mount is 2 (A) + 3 (B)");
            s_siblingBCount = 30;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParkedAtAOriginalSlotCount = s_siblingBFiber.HasPendingReconcileWorkForTest();

            // Act — A grows 2 -> 25 on the Transition lane so A itself parks and commits its +23 delta across
            // several slices; driving A to completion exercises both the partial and incremental propagation paths
            s_siblingACount = 25;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingAFiber.FlushStateWithTinyBudgetForTest();
            var aParked = s_siblingAFiber.HasPendingReconcileWorkForTest();
            s_siblingAFiber.DrainTimeSlicedReconcileForTest();
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((bParkedAtAOriginalSlotCount, aParked, SiblingLayout(host, aCount: 25, bCount: 30)),
                Is.EqualTo((true, true, true)),
                "Both siblings park, and B's resumed body lands after A's incrementally-grown prefix, in order");
        }

        [Test]
        public void Given_ParkedInlineSibling_When_ItReRendersAndForceDrains_Then_FollowingSiblingStaysAligned()
        {
            // Arrange — A (2) + B (3) inline siblings share one host. A grows 2 -> 20 on the Transition lane under a
            // tiny budget so it PARKS mid-grow with rows still pending.
            s_siblingACount = 2;
            s_siblingBCount = 3;
            using var mounted = V.Mount(_root, V.Component(SiblingHostRender, key: "host"));
            var host = _root.Q(name: "sibling-host");
            Assume.That(host.childCount, Is.EqualTo(5), "Precondition: the initial mount is 2 (A) + 3 (B)");
            s_siblingACount = 20;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingAFiber.FlushStateWithTinyBudgetForTest();
            var aParkedMidGrow = s_siblingAFiber.HasPendingReconcileWorkForTest();

            // Act — A re-renders AGAIN (Normal lane) while still parked. RenderAndReconcile force-drains the parked
            // grow before the new render; the drain's child-count delta must propagate to B (else B's MountSlotStart
            // is stale and its rows land inside A's grown prefix).
            s_siblingACount = 25;
            s_siblingAFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_siblingAFiber);
            s_siblingAFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That((aParkedMidGrow, SiblingLayout(host, aCount: 25, bCount: 3)), Is.EqualTo((true, true)),
                "A parks mid-grow and the force-drained grow's delta propagates to B, so B's rows stay aligned after A's grown prefix");
        }

        [Test]
        public void Given_FollowingSiblingParked_When_NestedInlineGrowthAbsorbedByWrapper_Then_DoesNotShift()
        {
            // Arrange — the host's first inline child is a wrapper (Outer) whose body is a div holding a
            // leaf-producing inline child. The wrapper's div is a single host child regardless of inner count, so
            // a nested growth must NOT shift the parked following sibling B: propagation is scoped to the mount
            // point a fiber actually shares, not across nesting boundaries.
            s_nestedInnerCount = 2;
            s_siblingBCount = 3;
            using var mounted = V.Mount(_root, V.Component(NestedHostRender, key: "host"));
            var host = _root.Q(name: "nested-host");
            Assume.That(host.childCount, Is.EqualTo(4), "Precondition: Outer div (1) + B (3)");
            var innerDiv = host.Q(name: "inner-div");
            Assume.That(innerDiv.childCount, Is.EqualTo(2), "Precondition: the inner div holds 2 leaves");
            s_siblingBCount = 30;
            s_siblingBFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_siblingBFiber.FlushStateWithTinyBudgetForTest();
            var bParkedAtSlotStartOne = s_siblingBFiber.HasPendingReconcileWorkForTest();

            // Act — the inner grows synchronously; the host-level child count (the div itself) is unchanged
            s_nestedInnerCount = 6;
            s_nestedInnerFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_nestedInnerFiber);
            s_siblingBFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the wrapper absorbs the delta, so B's slot does not move
            Assert.That((bParkedAtSlotStartOne, NestedLayout(host, innerDiv, innerCount: 6, bCount: 30)),
                Is.EqualTo((true, true)),
                "B parks, the nested growth stays inside the wrapper, and B's body still lands after the single Outer div");
        }

        #endregion

        #region Keyed rotation (time-sliced reorder must be order-faithful)

        // A rotated keyed list (the second half mounted before the first half) reordered to sorted order is the
        // shape the OLD absolute-index reorder mis-slots: the sorted target's long increasing block (the first
        // half's keys) is an untouched LIS anchor run that physically sits at the WRONG absolute slots, so an
        // absolute parent.Insert(slotStart + i, e) drops a moved element among the anchors and transposes a
        // neighbouring pair. The synchronous paths were fixed to anchor on the neighbour element; this exercises
        // the SAME rotation through the time-sliced keyed Pass2Reorder (parked + resumed under a tiny budget).
        [Test]
        public void Given_RotatedKeyedList_When_ReorderedToSortedUnderTinyBudget_Then_OrderIsCorrectAfterResume()
        {
            // Arrange — mount the keyed list rotated right by two (24 leaves: r-22, r-23, r-0, r-1, … r-21).
            s_rotationCount = 24;
            s_rotationSorted = false;
            using var mounted = V.Mount(_root, V.Component(RotationListRender, key: "rot"));
            var fiber = s_rotationFiber;
            Assume.That(RotationRotatedLayout(_root, 24), Is.True,
                "Precondition: the initial mount is rotated right by two (r-22, r-23, r-0, r-1, …)");

            // Act — re-render to sorted order on the Transition lane under a tiny budget so the keyed reorder parks
            // mid-pass, then drain every parked slice to completion.
            s_rotationSorted = true;
            fiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            fiber.FlushStateWithTinyBudgetForTest();
            var parkedMidPass = fiber.HasPendingReconcileWorkForTest();
            fiber.DrainTimeSlicedReconcileForTest();

            // Assert — every leaf sits at its sorted slot; the time-sliced reorder must not transpose any pair.
            Assert.That((parkedMidPass, RotationSortedLayout(_root, 24)), Is.EqualTo((true, true)),
                "The keyed reorder parks mid-pass and, after resuming, the list is fully sorted r-0..r-23 with no transposed pair");
        }

        #endregion

        #region Layout assertions (shared one-line invariants)

        // True when host holds aCount "a-i" leaves followed by bCount "b-i" leaves, each in ascending order.
        private static bool SiblingLayout(VisualElement host, int aCount, int bCount)
        {
            if (host.childCount != aCount + bCount) return false;
            for (var i = 0; i < aCount; i++)
            {
                if (((Label)host.ElementAt(i)).text != $"a-{i}") return false;
            }
            for (var i = 0; i < bCount; i++)
            {
                if (((Label)host.ElementAt(aCount + i)).text != $"b-{i}") return false;
            }
            return true;
        }

        // True when host holds aCount "a-i" ascending, then bCount "b-i" and cCount "c-i" each in reversed order.
        private static bool ThreeSiblingReversedLayout(VisualElement host, int aCount, int bCount, int cCount)
        {
            if (host.childCount != aCount + bCount + cCount) return false;
            for (var i = 0; i < aCount; i++)
            {
                if (((Label)host.ElementAt(i)).text != $"a-{i}") return false;
            }
            for (var i = 0; i < bCount; i++)
            {
                if (((Label)host.ElementAt(aCount + i)).text != $"b-{bCount - 1 - i}") return false;
            }
            for (var i = 0; i < cCount; i++)
            {
                if (((Label)host.ElementAt(aCount + bCount + i)).text != $"c-{cCount - 1 - i}") return false;
            }
            return true;
        }

        // True when host holds aCount "a-i", then bCount "b-i", then cCount "c-i", all in ascending order.
        private static bool ThreeSiblingAscendingLayout(VisualElement host, int aCount, int bCount, int cCount)
        {
            if (host.childCount != aCount + bCount + cCount) return false;
            for (var i = 0; i < aCount; i++)
            {
                if (((Label)host.ElementAt(i)).text != $"a-{i}") return false;
            }
            for (var i = 0; i < bCount; i++)
            {
                if (((Label)host.ElementAt(aCount + i)).text != $"b-{i}") return false;
            }
            for (var i = 0; i < cCount; i++)
            {
                if (((Label)host.ElementAt(aCount + bCount + i)).text != $"c-{i}") return false;
            }
            return true;
        }

        // True when the inner div holds innerCount leaves, the host holds the single Outer div + bCount B leaves,
        // and B's leaves follow the div in ascending order.
        private static bool NestedLayout(VisualElement host, VisualElement innerDiv, int innerCount, int bCount)
        {
            if (innerDiv.childCount != innerCount) return false;
            if (host.childCount != 1 + bCount) return false;
            for (var i = 0; i < bCount; i++)
            {
                if (((Label)host.ElementAt(1 + i)).text != $"b-{i}") return false;
            }
            return true;
        }

        // True when host holds n keyed leaves rotated right by two: r-{n-2}, r-{n-1}, r-0, r-1, … r-{n-3}.
        private static bool RotationRotatedLayout(VisualElement host, int n)
        {
            if (host.childCount != n) return false;
            for (var i = 0; i < n; i++)
            {
                if (((Label)host.ElementAt(i)).text != $"r-{(i + n - 2) % n}") return false;
            }
            return true;
        }

        // True when host holds n keyed leaves in sorted order r-0..r-{n-1}.
        private static bool RotationSortedLayout(VisualElement host, int n)
        {
            if (host.childCount != n) return false;
            for (var i = 0; i < n; i++)
            {
                if (((Label)host.ElementAt(i)).text != $"r-{i}") return false;
            }
            return true;
        }

        #endregion

        #region Sibling-shift host components (inline-mount siblings sharing one VE)

        private static int s_siblingACount;
        private static int s_siblingBCount;
        private static int s_siblingCCount;
        private static int s_nestedInnerCount;
        private static bool s_siblingBReversed;
        private static bool s_siblingCReversed;
        private static ComponentFiber s_siblingAFiber;
        private static ComponentFiber s_siblingBFiber;
        private static ComponentFiber s_siblingCFiber;
        private static ComponentFiber s_nestedInnerFiber;

        private static void ResetSiblingShift()
        {
            s_siblingACount = 0;
            s_siblingBCount = 0;
            s_siblingCCount = 0;
            s_nestedInnerCount = 0;
            s_siblingBReversed = false;
            s_siblingCReversed = false;
            s_siblingAFiber = null;
            s_siblingBFiber = null;
            s_siblingCFiber = null;
            s_nestedInnerFiber = null;
        }

        [Component]
        private static VNode SiblingHostRender()
        {
            return V.Div(
                name: "sibling-host",
                children: new VNode[]
                {
                    V.Component(SiblingARender, key: "a"),
                    V.Component(SiblingBRender, key: "b"),
                });
        }

        [Component]
        private static VNode ThreeSiblingHostRender()
        {
            return V.Div(
                name: "three-sibling-host",
                children: new VNode[]
                {
                    V.Component(SiblingARender, key: "a"),
                    V.Component(SiblingBRender, key: "b"),
                    V.Component(SiblingCRender, key: "c"),
                });
        }

        [Component]
        private static VNode SiblingARender()
        {
            s_siblingAFiber = FiberAmbientStack.Current;
            return FlatLeafFragment("a", s_siblingACount);
        }

        [Component]
        private static VNode SiblingBRender()
        {
            s_siblingBFiber = FiberAmbientStack.Current;
            return FlatLeafFragment("b", s_siblingBCount, s_siblingBReversed);
        }

        [Component]
        private static VNode SiblingCRender()
        {
            s_siblingCFiber = FiberAmbientStack.Current;
            return FlatLeafFragment("c", s_siblingCCount, s_siblingCReversed);
        }

        // A top-level Fragment of pure leaves is unwrapped to a flat array by NormalizeToArray, so the
        // inline-mount fiber's own reconcile takes the time-sliceable fast path. When <paramref name="reversed"/>
        // is set the leaves carry keys in reversed order, forcing a same-count keyed reorder (which parks under a
        // tiny budget without changing the fiber's slot count).
        private static VNode FlatLeafFragment(string prefix, int count, bool reversed = false)
        {
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                if (reversed)
                {
                    var idx = count - 1 - i;
                    children[i] = V.Label(text: $"{prefix}-{idx}", key: $"{prefix}{idx}");
                }
                else
                {
                    children[i] = V.Label(text: $"{prefix}-{i}");
                }
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode NestedHostRender()
        {
            // The host's first inline child is a wrapper (Outer) whose body nests another inline child; B is a
            // flat sibling at the host level. Outer's div is a single host child whatever the nested leaf count is.
            return V.Div(
                name: "nested-host",
                children: new VNode[]
                {
                    V.Component(NestedOuterRender, key: "outer"),
                    V.Component(SiblingBRender, key: "b"),
                });
        }

        [Component]
        private static VNode NestedOuterRender()
        {
            return V.Div(
                name: "inner-div",
                children: new VNode[]
                {
                    V.Component(NestedInnerRender, key: "inner"),
                });
        }

        [Component]
        private static VNode NestedInnerRender()
        {
            s_nestedInnerFiber = FiberAmbientStack.Current;
            return FlatLeafFragment("inner", s_nestedInnerCount);
        }

        #endregion

        #region UseTransition list component

        private static int s_transitionListCount;
        private static ComponentFiber s_transitionListFiber;
        private static StateUpdater<int> s_transitionListSetCount;
        private static TransitionStarter s_transitionListStart;

        private static void ResetTransitionList()
        {
            s_transitionListCount = 0;
            s_transitionListFiber = null;
            s_transitionListSetCount = default;
            s_transitionListStart = default;
        }

        [Component]
        private static VNode TransitionListRender()
        {
            s_transitionListFiber = FiberAmbientStack.Current;
            var (count, setCount) = Hooks.UseState(s_transitionListCount);
            var (_, start) = Hooks.UseTransition();
            s_transitionListSetCount = setCount;
            s_transitionListStart = start;
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Label(text: $"transition-{i}");
            }
            return V.Fragment(children: children);
        }

        #endregion

        #region FlatList component (flat host-leaf array; fast-path eligible)

        private static int s_flatListCount;
        private static ComponentFiber s_flatListFiber;

        private static void ResetFlatList()
        {
            s_flatListCount = 0;
            s_flatListFiber = null;
        }

        [Component]
        private static VNode FlatListRender()
        {
            s_flatListFiber = FiberAmbientStack.Current;
            var children = new VNode[s_flatListCount];
            for (var i = 0; i < s_flatListCount; i++)
            {
                children[i] = V.Label(text: $"item-{i}");
            }
            // A top-level Fragment of pure leaves is unwrapped to the flat array by NormalizeToArray, so the
            // fiber's own reconcile takes the time-sliceable fast path (no Component / Provider / Fragment leaf).
            return V.Fragment(children: children);
        }

        #endregion

        #region Rotation component (flat KEYED list; rotated -> sorted reorder, fast-path eligible)

        private static int s_rotationCount;
        private static bool s_rotationSorted;
        private static ComponentFiber s_rotationFiber;

        private static void ResetRotation()
        {
            s_rotationCount = 0;
            s_rotationSorted = false;
            s_rotationFiber = null;
        }

        // A flat fragment of KEYED leaves. When not sorted, the values are rotated RIGHT by two (the last two
        // values lead, then 0,1,2,…): the sorted target's long increasing block (values 0..n-3) is therefore an
        // untouched LIS anchor run that physically sits at the BACK and must move to the FRONT — the exact
        // (unambiguous) rotation shape the absolute-index reorder mis-slots, transposing the trailing pair. This
        // mirrors ReconcilerKeyedTests' [4,5,0,1,2,3] case, scaled up so it time-slices. Keyed host leaves stay
        // on the time-sliceable fast path, so the reorder runs through the parked/resumed Pass2Reorder.
        [Component]
        private static VNode RotationListRender()
        {
            s_rotationFiber = FiberAmbientStack.Current;
            var n = s_rotationCount;
            var children = new VNode[n];
            for (var i = 0; i < n; i++)
            {
                var val = s_rotationSorted ? i : (i + n - 2) % n;
                children[i] = V.Label(text: $"r-{val}", key: $"k{val}");
            }
            return V.Fragment(children: children);
        }

        #endregion

        #region Probe component (Normal-lane setter)

        private static int s_probeRenderCount;
        private static ComponentFiber s_probeFiber;
        private static Action s_probeSetValue;

        private static void ResetProbe()
        {
            s_probeRenderCount = 0;
            s_probeFiber = null;
            s_probeSetValue = null;
        }

        [Component]
        private static VNode ProbeRender()
        {
            s_probeRenderCount++;
            s_probeFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState("a");
            s_probeSetValue = () => setValue.Invoke("b");
            return V.Label(text: value);
        }

        #endregion

        #region RefOrdering component (flat list with a ref on the last leaf + layout effect)

        private static int s_refOrderingCount;
        private static ComponentFiber s_refOrderingFiber;
        private static bool s_refOrderingEffectRan;
        private static bool s_refOrderingRefWasNullAtEffect;
        private static readonly Ref<Label> s_refOrderingLabelRef = new();

        private static void ResetRefOrdering()
        {
            s_refOrderingCount = 0;
            s_refOrderingFiber = null;
            s_refOrderingEffectRan = false;
            s_refOrderingRefWasNullAtEffect = false;
            s_refOrderingLabelRef.Set(null);
        }

        [Component]
        private static VNode RefOrderingRender()
        {
            s_refOrderingFiber = FiberAmbientStack.Current;
            Hooks.UseLayoutEffect(() =>
            {
                s_refOrderingEffectRan = true;
                s_refOrderingRefWasNullAtEffect = s_refOrderingLabelRef.Current == null;
                return (Action)null; // no cleanup; cast disambiguates the Func<Action> / Func<IDisposable> overloads
            }, new object[] { s_refOrderingCount });

            var children = new VNode[s_refOrderingCount];
            for (var i = 0; i < s_refOrderingCount; i++)
            {
                // The ref rides the LAST leaf so it is created only after the paused commit resumes — the
                // deferred layout effect must still observe it as attached (non-null).
                children[i] = i == s_refOrderingCount - 1
                    ? V.Label(text: $"item-{i}", refCallback: s_refOrderingLabelRef.SetElement)
                    : V.Label(text: $"item-{i}");
            }
            return V.Fragment(children: children);
        }

        #endregion
    }
}
