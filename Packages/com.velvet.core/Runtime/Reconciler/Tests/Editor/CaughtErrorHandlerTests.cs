using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="MountOptions.OnCaughtError"/>.
    /// <list type="bullet">
    /// <item>It receives the exception the boundary caught, the boundary's name and the component stack.</item>
    /// <item>It runs once per catch, whether the error came from a render on mount or on update, a layout or
    /// passive effect's setup, an effect cleanup, a ref setup, an element's creation callback, a faulted
    /// resource, or a Suspense retry.</item>
    /// <item>Where a boundary's own fallback content throws and an ancestor boundary catches that, it runs
    /// once, for the ancestor.</item>
    /// <item>An exception it throws is logged, and the mount that reported the catch still completes.</item>
    /// <item>It runs after the layout effects of the fallback the boundary shows and before its ancestors', with
    /// the fallback's layout effects run in that same commit when the catch came from a passive effect, a frame
    /// callback, or a <see cref="V.VirtualList{T}"/> range rendered outside a reconcile pass, and after the
    /// layout effects of an unrelated component the same drain re-rendered. A boundary that an outer boundary
    /// replaces before its fallback commits reports nothing, including one an earlier sibling's layout effect
    /// has replaced by the time its own place in the commit comes, and no report of such a boundary is kept.
    /// A report waits for its fallback's layout effects when a commit scoped to a new VirtualList item runs
    /// first, and a drain's commit that shows a fallback commits it before the drain returns.</item>
    /// <item>Boundaries reporting in one commit report in tree order, whatever order they caught in, each after
    /// the layout effects of its own fallback.</item>
    /// <item>A boundary a parked transition has mounted reports in the commit that completes the transition, and
    /// one the transition parked short of reports without waiting for it.</item>
    /// <item>Once a chain of follow-up commits reaches its bound, the next entry neither runs the chain again
    /// nor delivers the report it left, and a parked transition still runs the layout effects it held.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class CaughtErrorHandlerTests
    {
        private const string Reported = "boom | fallback";

        private readonly List<string> _calls = new();
        private VisualElement _root;
        private MountedTree _mounted;
        private HeadlessEditorPanelHost _host;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _calls.Clear();
            _mounted = null;
            _host = null;
            s_setTick = default;
            s_source = null;
            s_order.Clear();
            s_fallbackElement = null;
            s_listFiber = null;
            s_listTick = 0;
            s_setFirstTick = default;
            s_setSecondTick = default;
            s_setOther = default;
            s_fallbackMounts = 0;
            s_reportCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _host?.Dispose();
        }

        private static MountOptions RecordingOrder { get; } = new((_, _) => s_order.Add("caught"));

        private static MountOptions CountingReports { get; } = new((_, _) => s_reportCount++);

        private MountedTree Mount(Func<VNode> child)
        {
            s_child = child;
            return V.Mount(
                _root,
                V.Component(BoundaryRender, key: "boundary"),
                new MountOptions((exception, _) => _calls.Add(exception.Message)));
        }

        private string Outcome() => $"{string.Join(", ", _calls)} | {_root.FindFirstLabel()?.text}";

        [Test]
        public void Given_AnOnCaughtErrorHandler_When_ABoundaryCatches_Then_ItReceivesTheCaughtExceptionAndTheComponentStack()
        {
            // Arrange
            var thrown = new InvalidOperationException("boom");
            (Exception Exception, ErrorInfo Info) received = default;
            s_child = () => V.Component(ThrowOnRender, thrown, key: "child");

            // Act
            using var mounted = V.Mount(
                _root,
                V.Component(BoundaryRender, key: "boundary"),
                new MountOptions((exception, info) => received = (exception, info)));

            // Assert
            Assert.That(
                $"{ReferenceEquals(received.Exception, thrown)} | {received.Info?.ErrorBoundary}"
                + $" | {received.Info?.ComponentStack.Split('\n')[0]}",
                Is.EqualTo("True | CaughtErrorHandlerTests.BoundaryRender |     at CaughtErrorHandlerTests.ThrowOnRender"));
        }

        [Test]
        public void Given_AChildWhoseRenderThrowsOnMount_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseRenderThrowsOnUpdate_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowOnUpdateRender, key: "child"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseLayoutEffectThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInLayoutEffectRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhosePassiveEffectThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowInPassiveEffectRender, key: "child"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseEffectCleanupThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            using var mounted = Mount(() => V.Component(ThrowInCleanupRender, key: "child"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseRefSetupThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInRefRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildWhoseElementCreationCallbackThrows_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Act
            using var mounted = Mount(() => V.Component(ThrowInOnCreatedRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_AChildReadingAFaultedResource_When_TheBoundaryCatches_Then_TheHandlerRunsOnce()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            s_source.TrySetException(new InvalidOperationException("boom"));

            // Act
            using var mounted = Mount(() => V.Component(ReadResourceRender, key: "child"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_ASuspendedChildWhoseResourceFaults_When_TheRetryThrowsIntoTheBoundary_Then_TheHandlerRunsOnce()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            using var mounted = Mount(() => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ReadResourceRender, key: "child") }));

            // Act
            s_source.TrySetException(new InvalidOperationException("boom"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_ABoundaryWhoseFallbackContentThrows_When_AnAncestorBoundaryCatchesThat_Then_TheHandlerRunsOnceForTheAncestor()
        {
            // Act
            using var mounted = Mount(() => V.Component(InnerBoundaryRender, key: "inner"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo("fallback content boom | fallback"));
        }

        [Test]
        public void Given_AnOnCaughtErrorHandlerThatThrows_When_ABoundaryCatchesALayoutEffectsError_Then_TheMountCompletesWithTheFallback()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: handler boom");
            s_child = () => V.Component(ThrowInLayoutEffectRender, key: "child");

            // Act
            try
            {
                _mounted = V.Mount(
                    _root,
                    V.Component(BoundaryRender, key: "boundary"),
                    new MountOptions((_, _) => throw new InvalidOperationException("handler boom")));
            }
            catch (InvalidOperationException)
            {
            }

            // Assert
            Assert.That($"{_mounted != null} | {_root.FindFirstLabel()?.text}", Is.EqualTo("True | fallback"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffectBetweenASiblingAndAnAncestorWithOne_When_ABoundaryCatchesARenderErrorOnMount_Then_TheReportFollowsTheFallbacksLayoutEffectAndPrecedesTheAncestors()
        {
            // Arrange
            s_child = () => V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "child");

            // Act
            _mounted = V.Mount(_root, V.Component(AncestorWithLayoutEffectRender, key: "ancestor"), RecordingOrder);

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("sibling, fallback, caught, ancestor"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesALayoutEffectsErrorOnMount_Then_TheReportFollowsTheFallbacksLayoutEffect()
        {
            // Arrange
            s_child = () => V.Component(ThrowInLayoutEffectRender, key: "child");

            // Act
            _mounted = V.Mount(_root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesAPassiveEffectsError_Then_TheFallbacksLayoutEffectRunsAndTheReportFollowsIt()
        {
            // Arrange
            s_child = () => V.Component(ThrowInPassiveEffectRender, key: "child");
            _mounted = V.Mount(_root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);

            // Act
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesAFrameCallbacksError_Then_TheFallbacksLayoutEffectRunsAndTheReportFollowsIt()
        {
            // Arrange
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            s_child = () => V.Component(ThrowInFrameCallbackRender, key: "child");
            _mounted = V.Mount(_host.Root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel); // absorbs the zero-delta arm-time firing

            // Act
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_AnUnrelatedComponentReRenderedInTheSameDrain_When_ABoundaryCatchesARenderErrorThere_Then_TheReportFollowsItsLayoutEffectAndTheFallbacks()
        {
            // Arrange
            s_child = () => V.Component(ThrowOnUpdateRender, key: "child");
            _mounted = V.Mount(_root, V.Component(SiblingHostRender, key: "host"), RecordingOrder);
            s_order.Clear();
            s_setOther.Invoke(1);
            s_setTick.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("other, fallback, caught"));
        }

        [Test]
        public void Given_ABoundaryThatCaughtALayoutEffectsError_When_AnOuterBoundaryReplacesItInTheSameCommit_Then_OnlyTheOuterBoundaryReports()
        {
            // Act
            _mounted = V.Mount(
                _root,
                V.Component(OuterBoundaryRender, key: "outer"),
                new MountOptions((exception, _) => _calls.Add(exception.Message)));

            // Assert
            Assert.That(string.Join(", ", _calls), Is.EqualTo("two"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesAnElementCallbacksErrorInAResumedSlice_Then_TheFallbacksLayoutEffectRunsWithItsRefAndTheReportFollowsIt()
        {
            // Arrange — the boundary is the tail the transition appends, past the first slice's budget.
            s_child = () => V.Component(ThrowInRefRender, key: "child");
            _mounted = V.Mount(_root, V.Component(SlicedListRender, key: "list"), RecordingOrder);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var afterFirstSlice = _root.FindLabelByText("fallback") == null ? "no fallback yet" : "fallback already";

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That($"{afterFirstSlice}; {string.Join(", ", s_order)}", Is.EqualTo("no fallback yet; fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesALayoutEffectsErrorInATimeSlicedCommit_Then_TheFallbacksLayoutEffectRunsAndTheReportFollowsIt()
        {
            // Arrange
            s_child = () => V.Component(ThrowInLayoutEffectRender, key: "child");
            _mounted = V.Mount(_root, V.Component(SlicedListRender, key: "list"), RecordingOrder);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_SiblingBoundariesCatchingInReverseTreeOrderInOnePassiveDrain_When_TheirFallbacksCommit_Then_TheReportsFollowTreeOrderAndEachFallbacksLayoutEffects()
        {
            // Arrange — the second boundary's child throws from its effect's cleanup, which the drain runs
            // before the first child's throwing setup.
            _mounted = V.Mount(
                _root,
                V.Component(PassiveBoundaryPairRender, key: "pair"),
                new MountOptions((exception, _) => s_order.Add(exception.Message)));
            _mounted.FlushEffectsForTest();
            s_setFirstTick.Invoke(1);
            s_setSecondTick.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("one, fallback, two"));
        }

        [Test]
        public void Given_ABoundaryTheFirstSliceOfAParkedTransitionMounted_When_ItCatchesAPassiveEffectsErrorBeforeThePassCompletes_Then_ItReportsInTheCommitThatCompletesThePass()
        {
            // Arrange — the boundary's row is the first the transition builds, and its child's render outlasts
            // the slice's budget, so the batch drain's slice parks right after that row.
            s_child = () => V.Component(SlowThrowInPassiveEffectRender, key: "child");
            _mounted = V.Mount(_root, V.Component(BoundaryFirstListRender, key: "list"), RecordingOrder);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            _mounted.FlushEffectsForTest();
            var beforeCompletion = $"{s_listFiber.HasPendingReconcileWorkForTest()} | {string.Join(", ", s_order)}";

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(
                $"{beforeCompletion}; {string.Join(", ", s_order)}",
                Is.EqualTo("True | fallback; fallback, caught"));
        }

        [Test]
        public void Given_ABoundaryCommittedBeforeATransitionThatParksAheadOfIt_When_ItCatchesAPassiveEffectsErrorWhileThePassIsParked_Then_ItReportsAfterItsFallbacksLayoutEffectWithoutWaitingForThePass()
        {
            // Arrange — the transition puts a slow row ahead of the boundary's, so its slice parks before it
            // reaches the boundary.
            s_child = () => V.Component(ThrowInPassiveEffectRender, key: "child");
            _mounted = V.Mount(_root, V.Component(SlowRowAheadOfBoundaryListRender, key: "list"), RecordingOrder);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            var parked = s_listFiber.HasPendingReconcileWorkForTest();

            // Act
            _mounted.FlushEffectsForTest();

            // Assert — the park is read beside the order, because a pass that completed holds nothing back
            // and reaches the same order with nothing about the hold measured.
            Assert.That($"{parked} | {string.Join(", ", s_order)}", Is.EqualTo("True | fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesAnElementCallbacksErrorInAResumedSliceThatPatchesItsRow_Then_TheFallbacksLayoutEffectRunsWithItsRefAndTheReportFollowsIt()
        {
            // Arrange — the boundary's row is the last the transition patches, past the first slice's budget,
            // and the patch has its child create the element whose creation callback throws.
            _mounted = V.Mount(_root, V.Component(TickedBoundaryListRender, key: "list"), RecordingOrder);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_listFiber.FlushStateWithTinyBudgetForTest();
            var afterFirstSlice = _root.FindLabelByText("fallback") == null ? "no fallback yet" : "fallback already";

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That($"{afterFirstSlice}; {string.Join(", ", s_order)}", Is.EqualTo("no fallback yet; fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryAboveAVirtualListCatchesAnItemsRefErrorInARangeRenderedOutsideAPass_Then_TheFallbacksLayoutEffectRunsAndTheReportFollowsIt()
        {
            // Arrange — a headless mount measures no viewport, so the list renders no item until the range
            // update below.
            s_child = () => V.Component(RefThrowingListRender, key: "list-host");
            _mounted = V.Mount(_root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>("rows")];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryAboveAVirtualListCatchesARenderErrorInsideAnItemInARangeRenderedOutsideAPass_Then_TheFallbacksLayoutEffectRunsAndTheReportFollowsIt()
        {
            // Arrange — the throwing component sits in the element the item returns rather than being the item:
            // a component item mounts through FiberRenderer.Mount, whose own end commits what the catch left.
            s_child = () => V.Component(RenderThrowingItemListRender, key: "list-host");
            _mounted = V.Mount(_root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>("rows")];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_AFallbackWhoseLayoutEffectThrowsIntoItsOwnBoundaryOnEveryMount_When_TheFollowUpBoundStopsTheChain_Then_TheNextEntryNeitherRunsItAgainNorDeliversItsReport()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(RefallingHostRender, key: "host"), CountingReports);
            var bounded = false;
            try
            {
                _mounted.FlushEffectsForTest();
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("follow-up"))
            {
                bounded = true;
            }
            s_setOther.Invoke(1);
            var reportsBefore = s_reportCount;

            // Act
            var nextEntry = "nothing thrown";
            try
            {
                _mounted.FlushStateForTest();
            }
            catch (InvalidOperationException exception)
            {
                nextEntry = exception.Message;
            }

            // Assert — the bound is read beside the next entry, because a chain that never started leaves the
            // next entry nothing to run either.
            Assert.That(
                $"{bounded} | {nextEntry} | {s_reportCount - reportsBefore} reported",
                Is.EqualTo("True | nothing thrown | 0 reported"));
        }

        [Test]
        public void Given_AParkedTransitionHoldingARowsLayoutEffect_When_TheFollowUpBoundStopsAChainElsewhere_Then_TheRowsLayoutEffectRunsWhenThePassCompletes()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(RefallingBesideASlowListRender, key: "host"), CaughtErrors.Unlogged);
            s_listTick = 1;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            var parked = s_listFiber.HasPendingReconcileWorkForTest();
            var bounded = false;
            try
            {
                _mounted.FlushEffectsForTest();
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("follow-up"))
            {
                bounded = true;
            }

            // Act
            s_listFiber.DrainTimeSlicedReconcileForTest();

            // Assert — the park and the bound are read beside the order, because a pass that completed before
            // the chain, or a chain that never reached the bound, leaves nothing for the bound to drop.
            Assert.That($"{parked} | {bounded} | {string.Join(", ", s_order)}", Is.EqualTo("True | True | slow"));
        }

        [Test]
        public void Given_AFallbackWithALayoutEffect_When_ABoundaryCatchesALayoutEffectsErrorInADrainsCommit_Then_TheFallbackCommitsAndReportsBeforeTheDrainReturns()
        {
            // Arrange
            s_child = () => V.Component(ThrowInLayoutEffectOnUpdateRender, key: "child");
            _mounted = V.Mount(_root, V.Component(LayoutFallbackBoundaryRender, key: "boundary"), RecordingOrder);
            s_setTick.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        [Test]
        public void Given_ABoundaryHoldingAReportFromARender_When_AnEarlierSiblingsLayoutEffectThrowsIntoAnOuterBoundaryInTheSameCommit_Then_OnlyTheOuterBoundaryReports()
        {
            // Arrange — the inner boundary catches while the mount renders, so its report is due in the mount's
            // commit, and the sibling ahead of it in that commit throws first.
            s_child = () => V.Component(ThrowOnRender, new InvalidOperationException("one"), key: "child");

            // Act
            _mounted = V.Mount(
                _root,
                V.Component(OuterAroundSiblingAndInnerBoundaryRender, key: "outer"),
                new MountOptions((exception, _) => _calls.Add(exception.Message)));

            // Assert
            Assert.That(string.Join(", ", _calls), Is.EqualTo("two"));
        }

        [Test]
        public void Given_ABoundaryThatAnOuterBoundaryReplacedBeforeItsReportWasDelivered_When_TheCommitsEnd_Then_TheContextKeepsNoReportForIt()
        {
            // Act
            _mounted = V.Mount(_root, V.Component(OuterBoundaryRender, key: "outer"), CaughtErrors.Unlogged);

            // Assert — the catch count is read beside the kept count, because an inner boundary that never
            // caught leaves nothing to keep either.
            Assert.That(
                $"{CatchesSoFar()} caught | {ReportsKept()} kept",
                Is.EqualTo("2 caught | 0 kept"));
        }

        [Test]
        public void Given_AMemoizedBoundaryInAPatchedVirtualListItemCatchingAChildsRender_When_ANewItemCommitsItsOwnSubtree_Then_TheReportWaitsForTheFallbacksLayoutEffect()
        {
            // Arrange — the boundary is memoized, so the patch re-renders only its dirty child and leaves no
            // entry of its own waiting; the new item mounts through a mount of its own, whose commit is
            // scoped to that item.
            _mounted = V.Mount(_root, V.Component(PatchedRowListHostRender, key: "host"), RecordingOrder);
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>("rows")];
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, 100f);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);
            s_setTick.Invoke(1);
            s_setHostTick.Invoke(1);
            s_order.Clear();

            // Act
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback, caught"));
        }

        // -1 where the context holds no such field, so a tree without it disagrees with the assertion instead
        // of raising out of these helpers.
        private long CatchesSoFar()
        {
            var field = typeof(ReconcilerContext)
                .GetField("NextCaughtErrorSequence", BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? -1 : (long)field.GetValue(_mounted.Root.Reconciler.Context);
        }

        private int ReportsKept()
        {
            var field = typeof(ReconcilerContext)
                .GetField("PendingCaughtErrorReports", BindingFlags.NonPublic | BindingFlags.Instance);
            return field == null ? -1 : ((ICollection)field.GetValue(_mounted.Root.Reconciler.Context)).Count;
        }

        [Test]
        public void Given_SiblingBoundariesWhoseFallbacksBothHaveLayoutEffectsCatchingInReverseTreeOrder_When_TheirFallbacksCommit_Then_EachReportFollowsItsOwnFallbacksLayoutEffect()
        {
            // Arrange — the second boundary catches first, so its fallback is pushed ahead of the first's.
            _mounted = V.Mount(
                _root,
                V.Component(NamedFallbackBoundaryPairRender, key: "pair"),
                new MountOptions((exception, _) => s_order.Add(exception.Message)));
            _mounted.FlushEffectsForTest();
            s_setFirstTick.Invoke(1);
            s_setSecondTick.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Act
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(", ", s_order), Is.EqualTo("fallback one, one, fallback two, two"));
        }

        // GREEN_ON_BASE(characterization): the base reports both catches in catch order at the catch, and the
        // queue that now holds them must keep the second while it delivers the first.
        [Test]
        public void Given_TwoBoundariesCatchingLayoutEffectsErrorsInOneCommit_When_Mounted_Then_EachReportsOnceInCatchOrder()
        {
            // Act
            _mounted = V.Mount(
                _root,
                V.Component(BoundaryPairRender, key: "pair"),
                new MountOptions((exception, _) => _calls.Add(exception.Message)));

            // Assert
            Assert.That(string.Join(", ", _calls), Is.EqualTo("one, two"));
        }

        // GREEN_ON_BASE(characterization): the base reports at the catch; here the Suspense boundary the
        // resolution would reveal is gone with the rest of the error boundary's content, so the resolution's
        // own entry is what commits the report.
        [Test]
        public void Given_ASuspenseBoundaryInsideAnErrorBoundary_When_ItsChildsResourceFaults_Then_TheErrorBoundaryReportsWithoutAFurtherFlush()
        {
            // Arrange
            s_source = new VelvetTaskCompletionSource<string>();
            _mounted = Mount(() => V.Component(SuspenseHostRender, key: "suspense-host"));

            // Act
            s_source.TrySetException(new InvalidOperationException("boom"));

            // Assert
            Assert.That(Outcome(), Is.EqualTo(Reported));
        }

        [Test]
        public void Given_NullOptions_When_Mounted_Then_ItThrowsArgumentNullException()
        {
            // Act
            TestDelegate mount = () => V.Mount(_root, V.Label(text: "ok"), null);

            // Assert
            Assert.That(mount, Throws.ArgumentNullException);
        }

        private static readonly List<string> s_order = new();
        private static VisualElement s_fallbackElement;
        private static StateUpdater<int> s_setOther;
        private static int s_fallbackMounts;
        private static int s_reportCount;
        private static StateUpdater<int> s_setFirstTick;
        private static StateUpdater<int> s_setSecondTick;
        private static ComponentFiber s_listFiber;
        private static int s_listTick;
        private static Func<VNode> s_child;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setHostTick;
        private static VelvetTaskCompletionSource<string> s_source;

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return s_child();
        }

        [Component]
        private static VNode ThrowOnRender(Exception exception) => throw exception;

        [Component]
        private static VNode ThrowOnUpdateRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (tick > 0) throw new InvalidOperationException("boom");
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException("boom")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInPassiveEffectRender()
        {
            Hooks.UseEffect((Func<Action>)(() => throw new InvalidOperationException("boom")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInCleanupRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException("boom"), new object[] { tick });
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode ThrowInRefRender()
            => V.Label(text: "ok", refCallback: _ => throw new InvalidOperationException("boom"));

        [Component]
        private static VNode ThrowInOnCreatedRender()
            => V.ScrollView(onCreated: _ => throw new InvalidOperationException("boom"));

        [Component]
        private static VNode ReadResourceRender() => V.Label(text: Hooks.Use(() => s_source.Task));

        [Component]
        private static VNode AncestorWithLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add("ancestor");
                return null;
            }), Array.Empty<object>());
            return V.Fragment(children: new VNode[]
            {
                V.Component(SiblingWithLayoutEffectRender, key: "sibling"),
                V.Component(LayoutFallbackBoundaryRender, key: "boundary"),
            });
        }

        [Component]
        private static VNode SiblingWithLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add("sibling");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode SlicedListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var rows = new List<VNode>();
            for (var i = 0; i < 4; i++)
            {
                rows.Add(V.Div(key: "item" + i, children: new VNode[] { V.Label(text: "item-" + i) }));
            }
            if (s_listTick > 0)
            {
                rows.Add(V.Div(key: "boundary", children: new VNode[]
                {
                    V.Component(LayoutFallbackBoundaryRender, key: "boundary"),
                }));
            }
            return V.Fragment(children: rows.ToArray());
        }

        // Longer than FiberLane.TimeSlicedBudgetMs, so the transition slice that renders it goes over its budget.
        private const int SlowRenderMs = 25;

        // One keyed row before the transition, so the keyed diff builds the rows the transition adds from the
        // first of them, with no budget check ahead of it.
        [Component(Compiler = false)]
        private static VNode BoundaryFirstListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            if (s_listTick == 0) return V.Fragment(children: new VNode[] { V.Label(text: "empty", key: "empty") });
            var rows = new List<VNode>
            {
                V.Div(key: "boundary", children: new VNode[] { V.Component(LayoutFallbackBoundaryRender, key: "boundary") }),
            };
            for (var i = 0; i < 3; i++)
            {
                rows.Add(V.Div(key: "item" + i, children: new VNode[] { V.Label(text: "item-" + i) }));
            }
            return V.Fragment(children: rows.ToArray());
        }

        [Component]
        private static VNode SlowThrowInPassiveEffectRender()
        {
            System.Threading.Thread.Sleep(SlowRenderMs);
            Hooks.UseEffect((Func<Action>)(() => throw new InvalidOperationException("boom")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Component(Compiler = false)]
        private static VNode SlowRowAheadOfBoundaryListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var boundaryRow = V.Div(key: "boundary", children: new VNode[]
            {
                V.Component(LayoutFallbackBoundaryRender, key: "boundary"),
            });
            if (s_listTick == 0) return V.Fragment(children: new VNode[] { boundaryRow });
            return V.Fragment(children: new VNode[]
            {
                V.Div(key: "slow", children: new VNode[] { V.Component(SlowRender, key: "slow") }),
                boundaryRow,
            });
        }

        [Component]
        private static VNode SlowRender()
        {
            System.Threading.Thread.Sleep(SlowRenderMs);
            return V.Label(text: "slow");
        }

        [Component(Compiler = false)]
        private static VNode TickedBoundaryListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var rows = new List<VNode>();
            for (var i = 0; i < 4; i++)
            {
                rows.Add(V.Div(key: "item" + i, children: new VNode[] { V.Label(text: "item-" + i) }));
            }
            rows.Add(V.Div(key: "boundary", children: new VNode[]
            {
                V.Component(TickedBoundaryRender, s_listTick, key: "boundary"),
            }));
            return V.Fragment(children: rows.ToArray());
        }

        [Component(IsErrorBoundary = true)]
        private static VNode TickedBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Component(FallbackWithLayoutEffectRender, key: "fallback"));
            return V.Component(ThrowInOnCreatedOnceTickedRender, tick, key: "child");
        }

        [Component]
        private static VNode ThrowInOnCreatedOnceTickedRender(int tick)
            => tick > 0
                ? V.ScrollView(onCreated: _ => throw new InvalidOperationException("boom"))
                : V.Label(text: "ok");

        [Component]
        private static VNode RefThrowingListRender()
            => V.VirtualList(
                items: new[] { "a" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Label(text: item, key: item,
                    refCallback: _ => throw new InvalidOperationException("boom")),
                overscan: 0,
                name: "rows");

        [Component]
        private static VNode RenderThrowingItemListRender()
            => V.VirtualList(
                items: new[] { "a" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Div(key: item, children: new VNode[]
                {
                    V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "row"),
                }),
                overscan: 0,
                name: "rows");

        [Component]
        private static VNode RefallingBesideASlowListRender()
            => V.Div(children: new VNode[]
            {
                V.Component(RefallingBoundaryRender, key: "boundary"),
                V.Component(SlowLayoutListRender, key: "list"),
            });

        [Component(Compiler = false)]
        private static VNode SlowLayoutListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            if (s_listTick == 0) return V.Fragment(children: new VNode[] { V.Label(text: "empty", key: "empty") });
            return V.Fragment(children: new VNode[]
            {
                V.Div(key: "slow", children: new VNode[] { V.Component(SlowLayoutRender, key: "slow") }),
                V.Div(key: "tail", children: new VNode[] { V.Label(text: "tail") }),
            });
        }

        [Component]
        private static VNode SlowLayoutRender()
        {
            System.Threading.Thread.Sleep(SlowRenderMs);
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add("slow");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: "slow");
        }

        [Component]
        private static VNode RefallingHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(OtherRender, key: "other"),
                V.Component(RefallingBoundaryRender, key: "boundary"),
            });

        // Each fallback it shows is a fresh mount whose layout effect throws back into it, for a chain long enough
        // to reach the follow-up bound twice over and short enough to settle where no bound stops it.
        [Component(IsErrorBoundary = true)]
        private static VNode RefallingBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                var mount = s_fallbackMounts++;
                return V.Component(RefallingFallbackRender, mount, key: "fallback-" + mount);
            });
            return V.Component(ThrowInPassiveEffectRender, key: "child");
        }

        private const int RefallingChainLength = 300;

        [Component]
        private static VNode RefallingFallbackRender(int mount)
        {
            Hooks.UseLayoutEffect(
                (Func<Action>)(() => mount < RefallingChainLength ? throw new InvalidOperationException("boom") : (Action)null),
                new object[] { mount });
            return V.Label(text: "fallback");
        }

        [Component]
        private static VNode ThrowInLayoutEffectOnUpdateRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseLayoutEffect(
                (Func<Action>)(() => tick > 0 ? throw new InvalidOperationException("boom") : (Action)null),
                new object[] { tick });
            return V.Label(text: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode OuterAroundSiblingAndInnerBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "outer fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowInLayoutEffectWithRender, "two", key: "sibling"),
                V.Component(BoundaryRender, key: "inner"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PatchedRowListHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHostTick = setTick;
            return V.VirtualList(
                items: tick == 0 ? new[] { "a" } : new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => item == "a"
                    ? V.Div(key: item, children: new VNode[] { V.Component(MemoizedLayoutFallbackBoundaryRender, key: "boundary") })
                    : V.Component(ItemLabelRender, key: item),
                overscan: 0,
                name: "rows");
        }

        [Component]
        private static VNode ItemLabelRender() => V.Label(text: "item");

        [Component(IsErrorBoundary = true, Memoize = true)]
        private static VNode MemoizedLayoutFallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(FallbackWithLayoutEffectRender, key: "fallback"));
            return V.Component(ThrowOnUpdateRender, key: "child");
        }

        [Component]
        private static VNode SuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ReadResourceRender, key: "child") });

        [Component]
        private static VNode BoundaryPairRender()
            => V.Div(children: new VNode[]
            {
                V.Component(InnerLayoutBoundaryRender, key: "first"),
                V.Component(SecondLayoutBoundaryRender, key: "second"),
            });

        [Component(IsErrorBoundary = true)]
        private static VNode SecondLayoutBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "second fallback"));
            return V.Component(ThrowInLayoutEffectWithRender, "two", key: "second");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode LayoutFallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(FallbackWithLayoutEffectRender, key: "fallback"));
            return s_child();
        }

        [Component]
        private static VNode FallbackWithLayoutEffectRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add(s_fallbackElement != null ? "fallback" : "fallback before its ref");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: "fallback", refCallback: element =>
            {
                s_fallbackElement = element;
                return () => s_fallbackElement = null;
            });
        }

        [Component]
        private static VNode SiblingHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(OtherRender, key: "other"),
                V.Component(LayoutFallbackBoundaryRender, key: "boundary"),
            });

        [Component]
        private static VNode OtherRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOther = setTick;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add("other");
                return null;
            }), new object[] { tick });
            return V.Label(text: "other");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode OuterBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "outer fallback"));
            return V.Div(children: new VNode[]
            {
                V.Component(InnerLayoutBoundaryRender, key: "inner"),
                V.Component(ThrowInLayoutEffectWithRender, "two", key: "second"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode InnerLayoutBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "inner fallback"));
            return V.Component(ThrowInLayoutEffectWithRender, "one", key: "first");
        }

        [Component]
        private static VNode ThrowInLayoutEffectWithRender(string message)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException(message)), Array.Empty<object>());
            return V.Label(text: message);
        }

        [Component]
        private static VNode NamedFallbackBoundaryPairRender()
            => V.Div(children: new VNode[]
            {
                V.Component(FirstNamedFallbackBoundaryRender, key: "first"),
                V.Component(SecondNamedFallbackBoundaryRender, key: "second"),
            });

        [Component(IsErrorBoundary = true)]
        private static VNode FirstNamedFallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(NamedLayoutFallbackRender, "fallback one", key: "fallback"));
            return V.Component(ThrowInPassiveSetupOnUpdateRender, key: "first");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode SecondNamedFallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(NamedLayoutFallbackRender, "fallback two", key: "fallback"));
            return V.Component(ThrowInPassiveCleanupOnUpdateRender, key: "second");
        }

        [Component]
        private static VNode NamedLayoutFallbackRender(string name)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_order.Add(name);
                return null;
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode PassiveBoundaryPairRender()
            => V.Div(children: new VNode[]
            {
                V.Component(FirstPassiveBoundaryRender, key: "first"),
                V.Component(SecondPassiveBoundaryRender, key: "second"),
            });

        [Component(IsErrorBoundary = true)]
        private static VNode FirstPassiveBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "first fallback"));
            return V.Component(ThrowInPassiveSetupOnUpdateRender, key: "first");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode SecondPassiveBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(FallbackWithLayoutEffectRender, key: "fallback"));
            return V.Component(ThrowInPassiveCleanupOnUpdateRender, key: "second");
        }

        [Component]
        private static VNode ThrowInPassiveSetupOnUpdateRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setFirstTick = setTick;
            Hooks.UseEffect(
                (Func<Action>)(() => tick > 0 ? throw new InvalidOperationException("one") : (Action)null),
                new object[] { tick });
            return V.Label(text: "one");
        }

        [Component]
        private static VNode ThrowInPassiveCleanupOnUpdateRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setSecondTick = setTick;
            Hooks.UseEffect((Func<Action>)(() => () => throw new InvalidOperationException("two")), new object[] { tick });
            return V.Label(text: "two");
        }

        [Component]
        private static VNode ThrowInFrameCallbackRender()
        {
            Hooks.UseFrame(_ => throw new InvalidOperationException("boom"));
            return V.Label(text: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode InnerBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(ThrowOnRender, new InvalidOperationException("fallback content boom"),
                key: "inner-fallback"));
            return V.Component(ThrowOnRender, new InvalidOperationException("boom"), key: "child");
        }
    }
}
