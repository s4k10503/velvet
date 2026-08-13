using System;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies Suspense-boundary behavior for function components and its interaction with error boundaries.
    /// A boundary wraps its children with <c>V.Suspense(fallback, children)</c>; a descendant suspends by reading
    /// a pending resource via <c>Hooks.Use</c>.
    /// <list type="bullet">
    /// <item>A child that resolves synchronously renders its content; a child that suspends renders the boundary's
    /// fallback, and once the resource resolves the fallback is swapped for the children.</item>
    /// <item>While a boundary is suspended its visible fallback subtree still renders normally, so a stateful
    /// fallback can flush its own state update; only the offscreen primary is deferred.</item>
    /// <item>A nested boundary catches its own descendant's suspension, so an outer boundary does not over-suspend
    /// when only the inner boundary's primary is pending.</item>
    /// <item>Sibling Suspense boundaries are tracked independently: resolving one boundary's child leaves the
    /// other sibling's fallback unchanged. With the suspended one expanded first, it keeps its own hidden
    /// descendants deferred without deferring the resolved sibling's; with the resolved one expanded first,
    /// the later suspension does not reach back over the children already expanded.</item>
    /// <item>A nested boundary owns its own descendants' visibility: an enclosing boundary that resolves leaves
    /// the inner boundary's primary subtree deferred, while an enclosing boundary that suspends defers the
    /// inner boundary's visible fallback subtree too and releases it again on resolve. Where one component
    /// fiber owns both Suspense nodes, a suspended enclosing one also defers a resolved inner one's
    /// children.</item>
    /// <item>A <c>Hooks.Use</c> resource that faults — synchronously or asynchronously — routes the exception to
    /// the nearest enclosing error boundary rather than the fallback.</item>
    /// <item>An unrelated parent re-render does not remove a still-pending child's fallback.</item>
    /// <item>The resource factory runs once per deps across a suspend/resume; the retained subtree reuses the
    /// existing resource on resume.</item>
    /// <item>With two suspending reads, the boundary stays in fallback until both resolve and then reveals the
    /// combined content; one resolving while the other is pending raises no unhandled exception.</item>
    /// <item>A suspending read with no enclosing boundary logs a warning.</item>
    /// <item>The fallback-to-children reveal is deferred through the lane queue, not applied synchronously when the
    /// resource resolves.</item>
    /// <item>The boundary emits no host node: its fallback and resolved content sit at the same VisualElement depth
    /// as the equivalent content without a boundary, and a boundary placed directly inside AnimatePresence renders
    /// wrapper-less inside that container.</item>
    /// <item>A primary that suspends rolls back cleanly: a childless poolable leaf it created is reclaimed into the
    /// pool, a child-bearing container orphan is left for GC (not pooled), and a container orphan's deferred child
    /// layout effect never runs against the dead orphan; on resume the primary subtree is rebuilt and its effect
    /// runs exactly once. A <c>V.Custom&lt;T&gt;</c> orphan is a container for this purpose whatever <c>T</c> is,
    /// so its child fiber is disposed too.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/>. A suspending child is driven by a
    /// <see cref="UniTaskCompletionSource{T}"/> whose completion is triggered explicitly so suspend/resume timing
    /// is deterministic in EditMode.
    /// </remarks>
    [TestFixture]
    internal sealed class SuspenseBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetAsyncChild();
            ResetTwoUses();
            ResetSiblingBoundaries();
            ResetOffscreenSibling();
            ResetNestedOffscreen();
            ResetSuspenseHost();
            ResetParentRerender();
            ResetRootless();
            ResetErrorBoundary();
        }

        #region Suspend / resolve reveal

        [Test]
        public void Given_SuspenseBoundary_When_ChildResolvesSynchronously_Then_RendersChildren()
        {
            // Arrange
            s_asyncChildFactory = _ => UniTask.FromResult("data");

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("data"),
                "A synchronously-resolved child renders its content with no fallback");
        }

        [Test]
        public void Given_SuspenseBoundary_When_ChildSuspends_Then_RendersFallback()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."),
                "A suspending child renders the boundary's fallback");
        }

        [Test]
        public void Given_SuspendedBoundary_When_ResourceResolves_Then_SwapsFallbackForChildren()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: the fallback is shown while pending");

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("ready"),
                "Once the resource resolves the fallback is swapped for the resolved children");
        }

        [Test]
        public void Given_StatefulFallback_When_FallbackStateUpdatesWhileSuspended_Then_FallbackRerenders()
        {
            // Arrange — the visible fallback subtree renders normally, so it can flush its own state update
            s_fallbackTickSetter = null;
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(StatefulFallbackHostRender, key: "host"));
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("fallback-0"), "Precondition: the stateful fallback is shown");

            // Act
            s_fallbackTickSetter?.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("fallback-1"),
                "A stateful fallback updates while the boundary is still suspended");
        }

        [Test]
        public void Given_NestedBoundaries_When_InnerSuspends_Then_InnerFallbackIsShown()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(NestedSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.FindLabelByText("inner-loading"), Is.Not.Null,
                "A nested boundary catches its own descendant's suspension and shows its inner fallback");
        }

        [Test]
        public void Given_NestedBoundaries_When_InnerSuspends_Then_OuterDoesNotOverSuspend()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(NestedSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.FindLabelByText("outer-loading"), Is.Null,
                "The outer boundary commits the inner boundary's output and does not show its own fallback");
        }

        #endregion

        #region Faulted resource routes to the error boundary

        [Test]
        public void Given_ErrorBoundaryAroundSuspense_When_ResourceFaultsSynchronously_Then_ShowsErrorFallback()
        {
            // Arrange
            var failure = new InvalidOperationException("boom");
            s_asyncChildFactory = _ => UniTask.FromException<string>(failure);
            s_errorBoundaryFallbackText = "error!";

            // Act
            using var mounted = V.Mount(_root, V.Component(ErrorBoundaryWithSuspenseRender, key: "host"));

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("error!"),
                "A synchronously-faulted resource routes to the error boundary, not the suspense fallback");
        }

        [Test]
        public void Given_ErrorBoundaryAroundSuspense_When_ResourceFaultsAsynchronously_Then_ShowsErrorFallback()
        {
            // Arrange — suspends first (shows the loading fallback), then faults
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_errorBoundarySuspenseFallback = "loading...";
            s_errorBoundaryFallbackText = "error!";
            using var mounted = V.Mount(_root, V.Component(ErrorBoundaryWithSuspenseRender, key: "host"));
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: the suspense fallback is shown while pending");

            // Act
            source.TrySetException(new InvalidOperationException("nope"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("error!"),
                "An asynchronously-faulted resource routes to the error boundary once it settles");
        }

        #endregion

        #region Retention across unrelated re-render and suspend / resume

        [Test]
        public void Given_PendingChild_When_ParentRerendersUnrelated_Then_FallbackIsPreserved()
        {
            // Arrange — the child resource is still pending; an unrelated parent state change triggers a re-render
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_parentRerenderFallbackText = "loading...";
            s_parentRerenderTick = 0;
            using var mounted = V.Mount(_root, V.Component(ParentRerenderHostRender, key: "host"));
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: the fallback is shown on the initial mount");

            // Act
            s_parentRerenderTickSetter.Invoke(s_parentRerenderTick + 1);
            mounted.FlushStateForTest();

            // Assert — each primary-child fiber's own pending async slot keeps the wrapper-less boundary's fallback
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."),
                "An unrelated parent re-render does not remove the still-pending child's fallback");
        }

        [Test]
        public void Given_PendingChild_When_ResourceResolvesAfterUnrelatedRerender_Then_RevealsChildren()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_parentRerenderFallbackText = "loading...";
            s_parentRerenderTick = 0;
            using var mounted = V.Mount(_root, V.Component(ParentRerenderHostRender, key: "host"));
            s_parentRerenderTickSetter.Invoke(s_parentRerenderTick + 1);
            mounted.FlushStateForTest();
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: the fallback survived the unrelated re-render");

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("ready"),
                "After the resource resolves the boundary switches to the children");
        }

        [Test]
        public void Given_SuspendThenResume_When_BoundaryResolves_Then_FactoryRunsOncePerDeps()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            Assume.That(s_asyncChildFactoryCallCount, Is.EqualTo(1), "Precondition: the factory ran once on suspend");

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — the retained subtree reuses the existing resource on resume
            Assert.That(s_asyncChildFactoryCallCount, Is.EqualTo(1),
                "The resource factory runs once per deps across a suspend/resume; resume does not re-fetch");
        }

        #endregion

        #region Two suspending reads

        [Test]
        public void Given_TwoSuspendingReads_When_OneResolves_Then_FallbackPersists()
        {
            // Arrange
            var source1 = new UniTaskCompletionSource<string>();
            var source2 = new UniTaskCompletionSource<string>();
            s_twoUsesFactory1 = _ => source1.Task;
            s_twoUsesFactory2 = _ => source2.Task;
            using var mounted = V.Mount(_root, V.Component(TwoUsesHostRender, key: "host"));
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: the fallback is shown while both are pending");

            // Act — resolve only the first; the second remains pending
            source1.TrySetResult("A");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."),
                "While one read is still pending the boundary keeps its fallback and raises no unhandled exception");
        }

        [Test]
        public void Given_TwoSuspendingReads_When_BothResolve_Then_RevealsCombinedContent()
        {
            // Arrange
            var source1 = new UniTaskCompletionSource<string>();
            var source2 = new UniTaskCompletionSource<string>();
            s_twoUsesFactory1 = _ => source1.Task;
            s_twoUsesFactory2 = _ => source2.Task;
            using var mounted = V.Mount(_root, V.Component(TwoUsesHostRender, key: "host"));
            source1.TrySetResult("A");
            mounted.FlushStateForTest();
            Assume.That(_root.FindFirstLabel()?.text, Is.EqualTo("loading..."), "Precondition: still pending on the second read");

            // Act
            source2.TrySetResult("B");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindFirstLabel()?.text, Is.EqualTo("A/B"),
                "Once both reads resolve the boundary reveals the combined content");
        }

        #endregion

        #region Independent sibling boundaries

        [Test]
        public void Given_TwoSiblingBoundaries_When_OneChildResolves_Then_OnlyThatBoundaryReveals()
        {
            // Arrange — two sibling Suspense boundaries, each with its own pending child
            var sourceA = new UniTaskCompletionSource<string>();
            var sourceB = new UniTaskCompletionSource<string>();
            s_siblingFactoryA = _ => sourceA.Task;
            s_siblingFactoryB = _ => sourceB.Task;
            using var mounted = V.Mount(_root, V.Component(SiblingBoundariesHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("loading-A"), Is.Not.Null, "Precondition: boundary A starts in fallback");
            Assume.That(_root.FindLabelByText("loading-B"), Is.Not.Null, "Precondition: boundary B starts in fallback");

            // Act — resolve only boundary A's child
            sourceA.TrySetResult("A-content");
            mounted.FlushStateForTest();

            // Assert — boundary B is unaffected and keeps its own fallback
            Assert.That(_root.FindLabelByText("loading-B"), Is.Not.Null,
                "Resolving one sibling boundary does not pull the other out of (or push it further into) its fallback");
        }

        [Test]
        public void Given_SuspendedBoundaryFollowedByResolvedSibling_When_OffscreenDescendantUpdates_Then_FallbackSurvives()
        {
            // Arrange — the suspended Suspense is expanded BEFORE the resolved one. That order is what used
            // to lose the boundary's suspended mark to the resolved sibling's expansion.
            var source = new UniTaskCompletionSource<string>();
            s_offscreenFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(OffscreenSiblingHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("sibling-0"), Is.Not.Null,
                "Precondition: the second Suspense renders its children, not its fallback");

            // Act — a state update on a fiber inside the suspended Suspense's offscreen primary subtree
            s_offscreenSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("loading-A"), Is.Not.Null,
                "An offscreen primary descendant's flush is deferred, so the committed fallback stays in its slot");
        }

        // GREEN_ON_BASE(characterization): the base reaches this outcome by never deferring at all —
        // the boundary-wide mark this branch replaces had already been cleared by this sibling's own
        // expansion, which is the defect the case above pins. The replacement does report a fallback up
        // for this boundary, so these children now reach the offscreen walk and what keeps them flushing
        // is its per-fiber check: widen that marking to the boundary and this case goes red.
        [Test]
        public void Given_SuspendedBoundaryFollowedByResolvedSibling_When_ResolvedSiblingsDescendantUpdates_Then_ItRerenders()
        {
            // Arrange — the same boundary fiber owns both Suspense nodes, so keeping the suspended one's
            // mark up must not reach the resolved one's children
            var source = new UniTaskCompletionSource<string>();
            s_offscreenFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(OffscreenSiblingHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("loading-A"), Is.Not.Null,
                "Precondition: the first Suspense is showing its fallback");

            // Act
            s_resolvedSiblingSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("sibling-1"), Is.Not.Null,
                "A descendant of the resolved sibling Suspense is not offscreen, so its own flush still commits");
        }

        // GREEN_ON_BASE(characterization): the snapshot exclusion is the only thing separating these two
        // in this order, since the resolved one expands first and claims nothing.
        [Test]
        public void Given_ResolvedBoundaryFollowedBySuspendedSibling_When_TheResolvedOnesDescendantUpdates_Then_ItRerenders()
        {
            // Arrange — the mirror order: the resolved Suspense expands first, so the suspended one's
            // marking pass runs with the resolved one's children already in the walk's fiber set
            var source = new UniTaskCompletionSource<string>();
            s_offscreenFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(ResolvedFirstSiblingHostRender, key: "host"));

            // Act
            s_resolvedSiblingSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback term is folded in rather than assumed: with no boundary showing a
            // fallback the offscreen walk never runs, and the update would commit whatever the marking did
            Assert.That(
                (_root.FindLabelByText("loading-D") != null, _root.FindLabelByText("sibling-1") != null),
                Is.EqualTo((true, true)),
                "While the later sibling holds its fallback up, the earlier resolved sibling's descendant is not offscreen and its own flush still commits");
        }

        #endregion

        #region Nested boundary ownership of offscreen primaries

        [Test]
        public void Given_NestedBoundaries_When_InnerPrimaryDescendantUpdatesWhileOuterResolved_Then_InnerFallbackKeepsItsSlot()
        {
            // Arrange — only the inner boundary suspends, so the outer commits the inner's fallback as its
            // own resolved output and its expansion covers the inner's offscreen primary subtree
            var source = new UniTaskCompletionSource<string>();
            s_nestedInnerFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(NestedPrimaryHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("inner-loading"), Is.Not.Null,
                "Precondition: the inner boundary is showing its fallback");

            // Act — a state update on a fiber inside the inner boundary's offscreen primary subtree
            s_nestedPrimarySetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("inner-loading"), Is.Not.Null,
                "An enclosing boundary resolving leaves the inner boundary's primary offscreen, so that primary's flush does not patch over the inner fallback");
        }

        // GREEN_ON_BASE(characterization): an enclosing boundary that suspends still hides an inner
        // boundary's visible fallback subtree, which the nested-ownership skip must not reach.
        [Test]
        public void Given_NestedBoundariesBothSuspended_When_InnerFallbackDescendantUpdates_Then_OuterFallbackKeepsItsSlot()
        {
            // Arrange — a second suspending child suspends the OUTER boundary as well, so the inner
            // boundary's fallback sits inside the rolled-back primary whose slot the outer fallback holds
            var innerSource = new UniTaskCompletionSource<string>();
            var outerSource = new UniTaskCompletionSource<string>();
            s_nestedInnerFactory = _ => innerSource.Task;
            s_nestedOuterFactory = _ => outerSource.Task;
            using var mounted = V.Mount(_root, V.Component(NestedBothSuspendHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("outer-loading"), Is.Not.Null,
                "Precondition: the outer boundary is showing its fallback");

            // Act — a state update on a fiber inside the inner boundary's own fallback subtree
            s_nestedFallbackSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("outer-loading"), Is.Not.Null,
                "The inner boundary's fallback is offscreen under the suspended outer boundary, so its flush does not patch over the outer fallback");
        }

        // GREEN_ON_BASE(characterization): the mark an enclosing suspend wrote on an inner boundary's
        // fallback subtree is released when that enclosing boundary resolves.
        [Test]
        public void Given_NestedBoundariesBothSuspended_When_OuterResolvesAndInnerFallbackDescendantUpdates_Then_ItRerenders()
        {
            // Arrange — the outer resolves while the inner is still pending, which puts the inner
            // boundary's fallback back on screen
            var innerSource = new UniTaskCompletionSource<string>();
            var outerSource = new UniTaskCompletionSource<string>();
            s_nestedInnerFactory = _ => innerSource.Task;
            s_nestedOuterFactory = _ => outerSource.Task;
            using var mounted = V.Mount(_root, V.Component(NestedBothSuspendHostRender, key: "host"));
            outerSource.TrySetResult("outer-ready");
            mounted.FlushStateForTest();
            Assume.That(_root.FindLabelByText("inner-fallback-0"), Is.Not.Null,
                "Precondition: the outer revealed its children and the inner boundary is still showing its fallback");

            // Act
            s_nestedFallbackSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("inner-fallback-1"), Is.Not.Null,
                "A visible inner fallback flushes its own state update once the enclosing boundary has resolved");
        }

        // GREEN_ON_BASE(characterization): the enclosing expansion's mark over a resolved inner
        // Suspense's children is the only thing deferring them here.
        [Test]
        public void Given_TwoSuspenseNodesOnOneBoundaryFiber_When_TheOuterSuspendsAndAnInnerChildUpdates_Then_OuterFallbackKeepsItsSlot()
        {
            // Arrange — the host's two Suspense nodes share one boundary fiber, for the reason
            // SameFiberNestedHostRender states, so FindNearestSuspenseBoundary answers with that fiber for
            // the inner one's children. The inner Suspense resolves, so it claims nothing and marks its
            // children visible; the outer then suspends on its own second child and marks them over
            var source = new UniTaskCompletionSource<string>();
            s_sameFiberOuterFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(SameFiberNestedHostRender, key: "host"));

            // Act
            s_sameFiberInnerSetter.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("outer-loading"), Is.Not.Null,
                "Where one boundary fiber owns both Suspense nodes, a resolved inner one's children are offscreen under the suspended enclosing one, so their flush does not patch over its fallback");
        }

        #endregion

        #region Rootless suspend

        [Test]
        public void Given_NoEnclosingBoundary_When_ChildSuspends_Then_LogsWarning()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_rootlessFactory = _ => source.Task;
            LogAssert.Expect(LogType.Warning, new Regex("Suspense"));

            // Act + Assert — LogAssert.Expect verifies a suspending read with no boundary warns
            using var mounted = V.Mount(_root, V.Component(RootlessSuspenseUseRender, key: "host"));
        }

        #endregion

        #region Deferred reveal through the lane queue

        [Test]
        public void Given_SuspendedBoundary_When_ResourceResolvesBeforeFlush_Then_FallbackPersistsUntilFlush()
        {
            // Arrange — the boundary swap goes through the lane queue, so it has not run before FlushStateForTest
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("loading..."), Is.Not.Null, "Precondition: the fallback is shown while pending");

            // Act
            source.TrySetResult("ready");

            // Assert — the reveal is deferred, so the fallback Label still stands before the flush drains the lane
            Assert.That(_root.FindLabelByText("loading..."), Is.Not.Null,
                "The fallback-to-children reveal is deferred via the lane queue, not applied synchronously on resolve");
        }

        [Test]
        public void Given_SuspendedBoundaryResolved_When_Flushed_Then_FallbackIsRemoved()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            source.TrySetResult("ready");
            Assume.That(_root.FindLabelByText("loading..."), Is.Not.Null, "Precondition: the deferred reveal has not run yet");

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("loading..."), Is.Null,
                "Flushing the boundary's Normal lane drains the deferred reveal and removes the fallback");
        }

        #endregion

        #region Wrapper-less boundary depth

        [Test]
        public void Given_ResolvedBoundary_When_Mounted_Then_AddsNoVisualElementDepthVsBaseline()
        {
            // Arrange — the boundary must add no VisualElement depth vs the same content without a boundary
            s_asyncChildFactory = _ => UniTask.FromResult("data");
            var baselineRoot = new VisualElement();
            using var baseline = V.Mount(baselineRoot, V.Component(SuspenseAsyncChildRender, key: "child"));
            var baselineLabel = baselineRoot.FindFirstLabel();
            Assume.That(baselineLabel?.text, Is.EqualTo("data"), "Precondition: the baseline rendered the content");
            var baselineDepth = IntermediateElementCount(baselineRoot, baselineLabel);

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            var label = _root.FindFirstLabel();

            // Assert
            Assert.That(IntermediateElementCount(_root, label), Is.EqualTo(baselineDepth),
                "A resolved boundary emits no host node: its content sits at the baseline depth");
        }

        [Test]
        public void Given_FallbackBoundary_When_Mounted_Then_AddsNoVisualElementDepthVsBaseline()
        {
            // Arrange
            var baselineRoot = new VisualElement();
            using var baseline = V.Mount(baselineRoot, V.Component(LoadingLabelRender, key: "loading"));
            var baselineLabel = baselineRoot.FindLabelByText("loading...");
            Assume.That(baselineLabel, Is.Not.Null, "Precondition: the baseline rendered the loading Label");
            var baselineDepth = IntermediateElementCount(baselineRoot, baselineLabel);
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"));
            var label = _root.FindLabelByText("loading...");

            // Assert
            Assert.That(IntermediateElementCount(_root, label), Is.EqualTo(baselineDepth),
                "A boundary showing its fallback emits no host node: the fallback sits at the baseline depth");
        }

        [Test]
        public void Given_BoundaryInsideAnimatePresence_When_Suspended_Then_FallbackSitsDirectlyInContainer()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";

            // Act
            using var mounted = V.Mount(_root, V.Component(AnimatePresenceSuspenseHostRender, key: "host"));

            // Assert
            var fallback = _root.FindLabelByText("loading...");
            Assert.That(fallback?.parent, Is.EqualTo(_root),
                "AnimatePresence and Suspense are both DOM-less, so the fallback sits directly in the parent");
        }

        [Test]
        public void Given_BoundaryInsideAnimatePresence_When_Resolved_Then_ContentSitsDirectlyInContainer()
        {
            // Arrange
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            s_suspenseHostFallbackText = "loading...";
            using var mounted = V.Mount(_root, V.Component(AnimatePresenceSuspenseHostRender, key: "host"));

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            var content = _root.FindLabelByText("ready");
            Assert.That(content?.parent, Is.EqualTo(_root),
                "DOM-less AnimatePresence + Suspense: resolved content sits directly in the parent (no wrapper)");
        }

        #endregion

        #region Suspended-primary rollback

        [Test]
        public void Given_SuspendedPrimaryWithPoolableLeaf_When_RolledBack_Then_LeafIsReclaimedToPool()
        {
            // Arrange — the primary created a childless poolable Label before the sibling async child suspended;
            // the non-Label fallback (a Button) does not rent it back, so it stays in the pool and is observable.
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(OrphanLeafSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(VNodePoolTestAccess.LabelPoolCountForTest, Is.GreaterThanOrEqualTo(1),
                "A poolable leaf created by a suspended primary is reclaimed into the pool on rollback");
        }

        [Test]
        public void Given_SuspendedPrimaryWithPoolableLeaf_When_Resolved_Then_PrimarySiblingLeafIsRecommitted()
        {
            // Arrange
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(OrphanLeafSuspenseHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("primary-leaf"), Is.Null, "Precondition: the suspended primary's sibling leaf is not visible while in fallback");

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — resuming re-commits the retained primary subtree's sibling leaf
            Assert.That(_root.FindLabelByText("primary-leaf"), Is.Not.Null,
                "Resolving re-commits the primary subtree's sibling leaf");
        }

        [Test]
        public void Given_SuspendedPrimaryWithPoolableLeaf_When_Resolved_Then_AsyncChildContentIsRendered()
        {
            // Arrange
            VNodePoolTestAccess.ClearLabelPoolForTest();
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(OrphanLeafSuspenseHostRender, key: "host"));
            Assume.That(_root.FindLabelByText("ready"), Is.Null, "Precondition: the async child's resolved content is not yet rendered while pending");

            // Act
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.FindLabelByText("ready")?.text, Is.EqualTo("ready"),
                "Resolving renders the async child's resolved content");
        }

        [Test]
        public void Given_SuspendedPrimaryWithChildBearingButton_When_RolledBack_Then_ButtonIsNotPooled()
        {
            // Arrange — the primary created a Button declared with children; CreateElement inline-expands those
            // children into the Button, so the orphan may host fibers reused on resolve. Pooling such a container
            // would resurface its stale child subtree on the next rent, so only childless leaves are reclaimed.
            VNodePoolTestAccess.ClearButtonPoolForTest();
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(ChildBearingButtonSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(VNodePoolTestAccess.ButtonPoolCountForTest, Is.EqualTo(0),
                "A child-bearing Button orphan is left for GC, not returned to the pool");
        }

        [Test]
        public void Given_SuspendedPrimaryWithContainerFiber_When_RolledBack_Then_DeferredChildEffectDoesNotRun()
        {
            // Arrange — the primary committed a container Div whose inline-expanded child schedules a layout
            // effect. The container is dropped on rollback; the rollback must dispose the child fiber so its setup
            // never runs against the dead orphan after the boundary has swapped to the fallback.
            s_containerOrphanEffectMountCount = 0;
            s_containerOrphanEffectCleanupCount = 0;
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(ContainerOrphanFiberSuspenseHostRender, key: "host"));

            // Assert
            Assert.That((s_containerOrphanEffectMountCount, s_containerOrphanEffectCleanupCount), Is.EqualTo((0, 0)),
                "A suspended primary's container-orphan child layout effect never runs, so no cleanup pairs against it");
        }

        [Test]
        public void Given_SuspendedPrimaryWithCustomLeafSubclassFiber_When_RolledBack_Then_DeferredChildEffectDoesNotRun()
        {
            // Arrange — V.Custom<T> declares children for any T, so a subclass of a poolable primitive holds an
            // inline-expanded child fiber exactly as a Div does, and the rollback owes it the same disposal.
            s_customLeafOrphanEffectMountCount = 0;
            s_customLeafOrphanEffectCleanupCount = 0;
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;

            // Act
            using var mounted = V.Mount(_root, V.Component(CustomLeafOrphanFiberSuspenseHostRender, key: "host"));

            // Assert
            Assert.That((s_customLeafOrphanEffectMountCount, s_customLeafOrphanEffectCleanupCount), Is.EqualTo((0, 0)),
                "A suspended primary's custom-leaf-orphan child layout effect never runs, so no cleanup pairs against it");
        }

        [Test]
        public void Given_SuspendedPrimaryWithContainerFiber_When_Resolved_Then_FreshChildEffectRunsOnce()
        {
            // Arrange
            s_containerOrphanEffectMountCount = 0;
            s_containerOrphanEffectCleanupCount = 0;
            var source = new UniTaskCompletionSource<string>();
            s_asyncChildFactory = _ => source.Task;
            using var mounted = V.Mount(_root, V.Component(ContainerOrphanFiberSuspenseHostRender, key: "host"));
            Assume.That(s_containerOrphanEffectMountCount, Is.EqualTo(0), "Precondition: the suspended primary's effect did not run");

            // Act — the resume rebuilds the primary with a fresh fiber, not the disposed one
            source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_containerOrphanEffectMountCount, Is.EqualTo(1),
                "After resolve the freshly mounted child fiber's layout effect setup runs exactly once");
        }

        #endregion

        #region Host components and helpers

        [Component]
        private static VNode InnerSuspenseMiddleRender()
            => V.Suspense(
                fallback: V.Label(text: "inner-loading"),
                children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") });

        [Component]
        private static VNode NestedSuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[] { V.Component(InnerSuspenseMiddleRender, key: "middle") });

        private static System.Action<int> s_fallbackTickSetter;

        [Component]
        private static VNode StatefulFallbackRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_fallbackTickSetter = setTick;
            return V.Label(text: $"fallback-{tick}");
        }

        [Component]
        private static VNode StatefulFallbackHostRender()
            => V.Suspense(
                fallback: V.Component(StatefulFallbackRender, key: "fb"),
                children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") });

        [Component]
        private static VNode OrphanLeafSuspenseHostRender()
            => V.Suspense(
                fallback: V.Button(text: "loading"),
                children: new VNode[]
                {
                    V.Label(text: "primary-leaf"),
                    V.Component(SuspenseAsyncChildRender, key: "child"),
                });

        [Component]
        private static VNode ChildBearingButtonSuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Button(children: new VNode[] { V.Label(text: "btn-child") }),
                    V.Component(SuspenseAsyncChildRender, key: "child"),
                });

        private static int s_containerOrphanEffectMountCount;
        private static int s_containerOrphanEffectCleanupCount;

        [Component]
        private static VNode LeadingEffectChildRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_containerOrphanEffectMountCount++;
                return () => s_containerOrphanEffectCleanupCount++;
            }, Array.Empty<object>());
            return V.Label(text: "leading-effect");
        }

        [Component]
        private static VNode ContainerOrphanFiberSuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(children: new VNode[]
                    {
                        V.Component(LeadingEffectChildRender, key: "leading"),
                    }),
                    V.Component(SuspenseAsyncChildRender, key: "child"),
                });

        private sealed class ProbeLabel : Label
        {
        }

        private static int s_customLeafOrphanEffectMountCount;
        private static int s_customLeafOrphanEffectCleanupCount;

        [Component]
        private static VNode CustomLeafOrphanEffectChildRender()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_customLeafOrphanEffectMountCount++;
                return () => s_customLeafOrphanEffectCleanupCount++;
            }, Array.Empty<object>());
            return V.Label(text: "custom-leaf-effect");
        }

        [Component]
        private static VNode CustomLeafOrphanFiberSuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Custom<ProbeLabel>(children: new VNode[]
                    {
                        V.Component(CustomLeafOrphanEffectChildRender, key: "leading"),
                    }),
                    V.Component(SuspenseAsyncChildRender, key: "child"),
                });

        // Number of VisualElements strictly between root and descendant (both excluded).
        private static int IntermediateElementCount(VisualElement root, VisualElement descendant)
        {
            var count = 0;
            for (var cur = descendant.parent; cur != null && cur != root; cur = cur.parent)
            {
                count++;
            }
            return count;
        }

        [Component]
        private static VNode LoadingLabelRender() => V.Label(text: "loading...");

        #endregion

        #region SuspenseAsyncChild component (Hooks.Use + factory call count)

        private static Func<CancellationToken, UniTask<string>> s_asyncChildFactory;
        private static int s_asyncChildFactoryCallCount;

        private static void ResetAsyncChild()
        {
            s_asyncChildFactory = null;
            s_asyncChildFactoryCallCount = 0;
        }

        [Component]
        private static VNode SuspenseAsyncChildRender()
        {
            // The factory lambda captures only static fields, so it is cached as a single static delegate whose
            // identity is stable across renders — the resource is reused across suspend -> resume without a
            // re-fetch (resource keying by factory identity).
            var data = Hooks.Use(ct =>
            {
                s_asyncChildFactoryCallCount++;
                return s_asyncChildFactory(ct);
            });
            return V.Label(text: data);
        }

        #endregion

        #region TwoUsesAsyncChild component (Hooks.Use x 2)

        private static Func<CancellationToken, UniTask<string>> s_twoUsesFactory1;
        private static Func<CancellationToken, UniTask<string>> s_twoUsesFactory2;

        private static void ResetTwoUses()
        {
            s_twoUsesFactory1 = null;
            s_twoUsesFactory2 = null;
        }

        [Component]
        private static VNode TwoUsesAsyncChildRender()
        {
            var a = Hooks.Use(s_twoUsesFactory1, "a");
            var b = Hooks.Use(s_twoUsesFactory2, "b");
            return V.Label(text: a + "/" + b);
        }

        #endregion

        #region SiblingBoundaries components (two sibling V.Suspense in one Render)

        private static Func<CancellationToken, UniTask<string>> s_siblingFactoryA;
        private static Func<CancellationToken, UniTask<string>> s_siblingFactoryB;

        private static void ResetSiblingBoundaries()
        {
            s_siblingFactoryA = null;
            s_siblingFactoryB = null;
        }

        [Component]
        private static VNode SiblingChildARender()
        {
            var a = Hooks.Use(s_siblingFactoryA, "a");
            return V.Label(text: a);
        }

        [Component]
        private static VNode SiblingChildBRender()
        {
            var b = Hooks.Use(s_siblingFactoryB, "b");
            return V.Label(text: b);
        }

        [Component]
        private static VNode SiblingBoundariesHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading-A"),
                    children: new VNode[] { V.Component(SiblingChildARender, key: "childA") },
                    key: "a"),
                V.Suspense(
                    fallback: V.Label(text: "loading-B"),
                    children: new VNode[] { V.Component(SiblingChildBRender, key: "childB") },
                    key: "b"),
            });

        #endregion

        #region OffscreenSibling components (a suspending V.Suspense followed by a resolving one)

        private static Func<CancellationToken, UniTask<string>> s_offscreenFactory;
        private static Action<int> s_offscreenSetter;
        private static Action<int> s_resolvedSiblingSetter;

        private static void ResetOffscreenSibling()
        {
            s_offscreenFactory = null;
            s_offscreenSetter = null;
            s_resolvedSiblingSetter = null;
        }

        // Renders before its suspending sibling so its fiber is created — and its setter captured — while
        // the boundary's primary expansion is still in progress.
        [Component]
        private static VNode OffscreenStatefulChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_offscreenSetter = setTick;
            return V.Label(text: $"primary-{tick}");
        }

        [Component]
        private static VNode ResolvedSiblingStatefulChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_resolvedSiblingSetter = setTick;
            return V.Label(text: $"sibling-{tick}");
        }

        [Component]
        private static VNode OffscreenSuspendingChildRender()
        {
            var value = Hooks.Use(s_offscreenFactory, "offscreen");
            return V.Label(text: value);
        }

        [Component]
        private static VNode OffscreenSiblingHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading-A"),
                    children: new VNode[]
                    {
                        V.Component(OffscreenStatefulChildRender, key: "stateful"),
                        V.Component(OffscreenSuspendingChildRender, key: "suspending"),
                    },
                    key: "a"),
                V.Suspense(
                    fallback: V.Label(text: "loading-B"),
                    children: new VNode[] { V.Component(ResolvedSiblingStatefulChildRender, key: "sibling") },
                    key: "b"),
            });

        // The same two boundaries in the opposite order, so the suspended one's marking pass is the one
        // that has to leave the other's children alone.
        [Component]
        private static VNode ResolvedFirstSiblingHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading-C"),
                    children: new VNode[] { V.Component(ResolvedSiblingStatefulChildRender, key: "sibling") },
                    key: "c"),
                V.Suspense(
                    fallback: V.Label(text: "loading-D"),
                    children: new VNode[] { V.Component(OffscreenSuspendingChildRender, key: "suspending") },
                    key: "d"),
            });

        #endregion

        #region NestedOffscreen components (a V.Suspense inside another V.Suspense's children)

        private static Func<CancellationToken, UniTask<string>> s_nestedInnerFactory;
        private static Func<CancellationToken, UniTask<string>> s_nestedOuterFactory;
        private static Func<CancellationToken, UniTask<string>> s_sameFiberOuterFactory;
        private static Action<int> s_nestedPrimarySetter;
        private static Action<int> s_nestedFallbackSetter;
        private static Action<int> s_sameFiberInnerSetter;

        private static void ResetNestedOffscreen()
        {
            s_nestedInnerFactory = null;
            s_nestedOuterFactory = null;
            s_sameFiberOuterFactory = null;
            s_nestedPrimarySetter = null;
            s_nestedFallbackSetter = null;
            s_sameFiberInnerSetter = null;
        }

        // Renders before its suspending sibling so its fiber is created — and its setter captured — while
        // the inner boundary's primary expansion is still in progress.
        [Component]
        private static VNode NestedPrimaryStatefulChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_nestedPrimarySetter = setTick;
            return V.Label(text: $"inner-primary-{tick}");
        }

        [Component]
        private static VNode NestedInnerSuspendingChildRender()
        {
            var value = Hooks.Use(s_nestedInnerFactory, "nested-inner");
            return V.Label(text: value);
        }

        [Component]
        private static VNode NestedOuterSuspendingChildRender()
        {
            var value = Hooks.Use(s_nestedOuterFactory, "nested-outer");
            return V.Label(text: value);
        }

        [Component]
        private static VNode NestedStatefulFallbackRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_nestedFallbackSetter = setTick;
            return V.Label(text: $"inner-fallback-{tick}");
        }

        [Component]
        private static VNode NestedPrimaryMiddleRender()
            => V.Suspense(
                fallback: V.Label(text: "inner-loading"),
                children: new VNode[]
                {
                    V.Component(NestedPrimaryStatefulChildRender, key: "stateful"),
                    V.Component(NestedInnerSuspendingChildRender, key: "suspending"),
                });

        // Nothing under the outer boundary suspends except through the inner one, which catches it — so the
        // outer commits the inner's fallback as its own resolved output.
        [Component]
        private static VNode NestedPrimaryHostRender()
            => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[] { V.Component(NestedPrimaryMiddleRender, key: "middle") });

        [Component]
        private static VNode NestedStatefulFallbackMiddleRender()
            => V.Suspense(
                fallback: V.Component(NestedStatefulFallbackRender, key: "fb"),
                children: new VNode[] { V.Component(NestedInnerSuspendingChildRender, key: "suspending") });

        [Component]
        private static VNode SameFiberInnerStatefulChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_sameFiberInnerSetter = setTick;
            return V.Label(text: $"inner-child-{tick}");
        }

        [Component]
        private static VNode SameFiberOuterSuspendingChildRender()
        {
            var value = Hooks.Use(s_sameFiberOuterFactory, "same-fiber-outer");
            return V.Label(text: value);
        }

        // Both V.Suspense nodes expand under the same FiberStack.Current — no component is written between
        // them to push another — so one boundary fiber holds both records.
        [Component]
        private static VNode SameFiberNestedHostRender()
            => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[]
                {
                    V.Suspense(
                        fallback: V.Label(text: "inner-loading"),
                        children: new VNode[] { V.Component(SameFiberInnerStatefulChildRender, key: "inner-child") },
                        key: "inner"),
                    V.Component(SameFiberOuterSuspendingChildRender, key: "outer-suspending"),
                },
                key: "outer");

        // The second child suspends outside the inner boundary, so the outer boundary suspends as well.
        [Component]
        private static VNode NestedBothSuspendHostRender()
            => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[]
                {
                    V.Component(NestedStatefulFallbackMiddleRender, key: "middle"),
                    V.Component(NestedOuterSuspendingChildRender, key: "outer-suspending"),
                });

        #endregion

        #region SuspenseHost component (V.Suspense wrapper)

        private static string s_suspenseHostFallbackText;

        private static void ResetSuspenseHost()
        {
            s_suspenseHostFallbackText = "loading...";
        }

        [Component]
        private static VNode SuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(text: s_suspenseHostFallbackText),
                children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") });

        [Component]
        private static VNode AnimatePresenceSuspenseHostRender()
            => V.AnimatePresence(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: s_suspenseHostFallbackText),
                    children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") },
                    key: "s"),
            });

        [Component]
        private static VNode TwoUsesHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading..."),
                children: new VNode[] { V.Component(TwoUsesAsyncChildRender, key: "child") });

        private static string s_parentRerenderFallbackText;
        private static int s_parentRerenderTick;
        private static System.Action<int> s_parentRerenderTickSetter;

        private static void ResetParentRerender()
        {
            s_parentRerenderFallbackText = "loading...";
            s_parentRerenderTick = 0;
            s_parentRerenderTickSetter = null;
        }

        [Component]
        private static VNode ParentRerenderHostRender()
        {
            var (tick, setTick) = Hooks.UseState(s_parentRerenderTick);
            s_parentRerenderTick = tick;
            s_parentRerenderTickSetter = setTick;
            return V.Suspense(
                fallback: V.Label(text: s_parentRerenderFallbackText),
                children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") });
        }

        #endregion

        #region RootlessSuspenseUse component (Hooks.Use without a boundary)

        private static Func<CancellationToken, UniTask<string>> s_rootlessFactory;

        private static void ResetRootless()
        {
            s_rootlessFactory = null;
        }

        [Component]
        private static VNode RootlessSuspenseUseRender()
        {
            var data = Hooks.Use(s_rootlessFactory);
            return V.Label(text: data);
        }

        #endregion

        #region ErrorBoundaryWithSuspense component (error boundary + V.Suspense)

        private static string s_errorBoundarySuspenseFallback;
        private static string s_errorBoundaryFallbackText;

        private static void ResetErrorBoundary()
        {
            s_errorBoundarySuspenseFallback = "loading...";
            s_errorBoundaryFallbackText = "error!";
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ErrorBoundaryWithSuspenseRender()
        {
            Hooks.UseFallback(_ => V.Label(text: s_errorBoundaryFallbackText));
            return V.Suspense(
                fallback: V.Label(text: s_errorBoundarySuspenseFallback),
                children: new VNode[] { V.Component(SuspenseAsyncChildRender, key: "child") });
        }

        #endregion
    }
}
