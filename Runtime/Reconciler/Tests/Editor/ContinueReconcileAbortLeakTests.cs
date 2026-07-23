using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="Reconciler.ContinueReconcile"/>'s own top-level completion point consumes
    /// <see cref="ReconcilerContext.IsAborted"/> exactly like <see cref="Reconciler.Reconcile"/>'s does —
    /// reading it into <see cref="Reconciler.LastTopLevelWasAborted"/> and resetting it (twice, around the
    /// Portal / z-layer drain) rather than leaving it untouched. Without this, an Error Boundary catching
    /// inside a Portal that a RESUMED time-sliced slice enqueued leaves the shared flag true after
    /// ContinueReconcile returns; the very next unrelated fiber's own top-level Reconcile — sharing the same
    /// ReconcilerContext — then hits ChildReconciler.Reconcile's entry guard (<c>if (_ctx.IsAborted) return;</c>)
    /// and silently no-ops its entire pass. The same boundary must also apply the REST of a pass's
    /// per-pass resets (scoped-key registrations, declaring-panel resolution misses, deferred old-tree
    /// pool returns) — a pass that happens to complete in a resumed slice is still that pass's genuine
    /// end, and anything skipped there leaks on the shared context until some unrelated fiber's own
    /// fresh top-level pass happens to clean it.
    /// </summary>
    [TestFixture]
    internal sealed class ContinueReconcileAbortLeakTests
    {
        private VisualElement _root;
        private static bool s_fallbackShown;
        private static int s_listCount;
        private static bool s_portalAdded;
        private static bool s_keyedFragmentAdded;
        private static ComponentFiber s_listFiber;
        private static string s_counterText;
        private static StateUpdater<string> s_setCounterText;

        [SetUp]
        public void SetUp()
        {
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            _root = new VisualElement();
            FiberPortalRegistry.Clear();
            s_fallbackShown = false;
            s_listCount = 3;
            s_portalAdded = false;
            s_keyedFragmentAdded = false;
            s_listFiber = null;
            s_counterText = "initial";
            s_setCounterText = default;
        }

        [TearDown]
        public void TearDown()
        {
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            FiberPortalRegistry.Clear();
        }

        [Test]
        public void Given_ATimeSlicedResumeDrainsAPortalWhoseErrorBoundaryCatches_When_AnUnrelatedFiberLaterReRenders_Then_ItsChangeStillCommits()
        {
            // Arrange — a target the Portal resolves at enqueue time, and a host with two INDEPENDENT
            // inline-mounted sibling fibers sharing one ReconcilerContext (mirrors TimeSlicedFiberTests' own
            // SiblingHostRender shape): "list" (a flat, keyed, time-sliceable array that grows a brand-new
            // trailing Portal — wrapping an error boundary around a throwing child — only below) and
            // "counter" (an ordinary, unrelated fiber whose own later re-render this test observes).
            var portalTarget = new VisualElement();
            FiberPortalRegistry.Register("continue-reconcile-abort-leak-target", portalTarget);
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            Assume.That(s_listFiber.HasPendingReconcileWorkForTest(), Is.False,
                "Precondition: the initial mount is synchronous (zero budget)");

            // Act (1) — add the trailing Portal under a tiny time-sliced budget: the pass parks on the
            // existing (unchanged) keyed prefix one item per tick, then a RESUMED tick creates the Portal's
            // placeholder (enqueuing it) as the pass's very last entry — so that SAME tick's own top-level
            // finally (Reconciler.ContinueReconcile) is what drains it: the boundary catches the Portal
            // child's throw and calls SetAborted() on the shared context.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_portalAdded = true;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_listFiber);
            Assume.That(s_listFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the grow mid-commit");
            s_listFiber.DrainTimeSlicedReconcileForTest();
            Assume.That(s_fallbackShown, Is.True,
                "Precondition: the Portal's error boundary actually caught the throw");

            // Act (2) — an unrelated fiber sharing the same mounted tree (and so the same ReconcilerContext)
            // re-renders normally, synchronously, well after the time-sliced pass above fully completed and
            // returned.
            FiberLane.TimeSlicedBudgetOverrideForTest = -1;
            s_setCounterText.Invoke("updated");
            mounted.FlushStateForTest();

            // Assert — RED without the fix: a stale IsAborted left over from the Portal drain makes
            // ChildReconciler.Reconcile's entry guard silently no-op this fiber's entire reconcile, leaving
            // the old text in place instead of committing the update.
            Assert.That(_root.Q<Label>("counter-label").text, Is.EqualTo("updated"));
        }

        [Test]
        public void Given_AResumedSliceExpandsAKeyedFragmentSubtree_When_ThePassCompletesThroughItsOwnContinuation_Then_TheSharedContextCarriesNoStaleScopedKeyEntries()
        {
            // Arrange — same two-fiber host; the growth below appends an item whose subtree contains a
            // KEYED Fragment, so the scoped-key registration the expansion performs lands during a RESUMED
            // slice (the initial mount's own top-level pass — which does clear the table at its end — is
            // long finished by then).
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var ctx = mounted.Root.Reconciler.Context;
            Assume.That(ctx.EffectiveKeys.Count, Is.EqualTo(0),
                "Precondition: the synchronous initial mount left no scoped-key entries behind");

            // Act — the pass parks per-item under the tiny budget, so the brand-new trailing item (and the
            // keyed Fragment inside it) is expanded by a continuation tick, and that SAME tick's own
            // top-level completion is the only boundary this pass ever gets.
            FiberLane.TimeSlicedBudgetOverrideForTest = 0.0001;
            s_keyedFragmentAdded = true;
            s_listFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            FiberWorkLoop.FlushState(s_listFiber);
            Assume.That(s_listFiber.HasPendingReconcileWorkForTest(), Is.True,
                "Precondition: the tiny budget parked the grow mid-commit");
            s_listFiber.DrainTimeSlicedReconcileForTest();
            Assume.That(_root.Q<Label>("keyed-fragment-leaf"), Is.Not.Null,
                "Precondition: the keyed Fragment's subtree really was expanded and committed");

            // Assert — RED without the continuation boundary clearing the table: the entries registered by
            // the resumed slice's expansion outlive the pass on the shared context (nothing else runs), so
            // the count stays nonzero here instead of resetting at the pass's genuine end.
            Assert.That(ctx.EffectiveKeys.Count, Is.EqualTo(0));
        }

        [Component]
        private static VNode Host() => V.Div(children: new VNode[]
        {
            V.Component(ListRender, key: "list"),
            V.Component(CounterRender, key: "counter"),
        });

        [Component]
        private static VNode ListRender()
        {
            s_listFiber = FiberAmbientStack.Current;
            var total = s_listCount + (s_portalAdded || s_keyedFragmentAdded ? 1 : 0);
            var children = new VNode[total];
            for (var i = 0; i < s_listCount; i++)
            {
                children[i] = V.Label(text: "item-" + i, key: "item" + i);
            }
            if (s_portalAdded)
            {
                children[s_listCount] = V.Portal("continue-reconcile-abort-leak-target", key: "portal",
                    children: new VNode[] { V.Component(BoundaryWrappingThrowerRender, key: "throwing-child") });
            }
            else if (s_keyedFragmentAdded)
            {
                children[s_listCount] = V.Div(key: "kf-wrap", children: new VNode[]
                {
                    V.Fragment(key: "kf", children: new VNode[]
                    {
                        V.Label(name: "keyed-fragment-leaf", text: "kf-leaf"),
                    }),
                });
            }
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode CounterRender()
        {
            var (text, setText) = Hooks.UseState(s_counterText);
            s_setCounterText = setText;
            return V.Label(name: "counter-label", text: text);
        }

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_fallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Component(ThrowingChildRender, key: "throwing-child-inner");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion
    }
}
