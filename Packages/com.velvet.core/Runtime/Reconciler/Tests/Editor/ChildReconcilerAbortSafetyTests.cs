using System;
using NUnit.Framework;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two properties of a same-key/same-index type flip whose replacement element
    /// construction triggers an error-boundary abort (<see cref="ReconcilerContext.IsAborted"/>),
    /// across every <c>ChildReconciler.PatchOrReplaceAtSlot</c> call site (the Common-phase indexed
    /// loop, and both keyed Pass-1 linear-scan implementations — sync and time-sliced):
    /// (1) the replacement element is still inserted at the slot — <see cref="ReconcilerContext.IsAborted"/>
    /// only ever becomes true via a boundary's SUCCESSFUL fallback render (FiberErrorBoundary.TryCatch
    /// calls SetAborted only after TryShowFallback's own Reconcile call returns without throwing), so by
    /// the time the abort is observed the newly built element already holds the boundary's fully
    /// rendered fallback content, not a half-built one — discarding it would strand the slot with
    /// nothing where the fallback should be, which React's render/commit split never does (an error
    /// boundary's fallback only ever replaces committed DOM, never leaves it empty mid-render); and
    /// (2) the same abort stops the scan from processing any LATER sibling — it is left untouched
    /// instead of being patched while the reconcile is aborted.
    /// </summary>
    [TestFixture]
    internal sealed class ChildReconcilerAbortSafetyTests : ReconcilerTestFixture
    {
        private static bool s_fallbackShown;
        private static StateUpdater<bool> s_setFlag;

        public override void SetUp()
        {
            base.SetUp();
            s_fallbackShown = false;
            s_setFlag = default;
        }

        [Component]
        private static VNode IndexedListHost()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.Div(children: flipped
                ? new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(BoundaryWrappingThrowerRender) }),
                    V.Label(text: "b-updated"),
                }
                : new VNode[]
                {
                    V.Label(text: "a"),
                    V.Label(text: "b"),
                });
        }

        [Test]
        public void Given_ErrorBoundaryAbortsDuringIndexedReplace_When_Reconciled_Then_TheFallbackIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — unkeyed siblings select the Common-phase indexed diff. The first sibling flips
            // from a Label to a Div wrapping an error boundary whose child throws; the flip's CanPatch
            // decision is false, so building the replacement recurses into the boundary before the
            // abort is observed. The boundary catches successfully, so the built Div (containing the
            // "caught" fallback Label) is inserted at slot 0 despite the abort; the abort then stops
            // the scan before the second sibling is reached, so its ORIGINAL text survives instead of
            // being patched to "b-updated".
            using var mounted = V.Mount(Root, V.Component(IndexedListHost, key: "host"));
            var container = Root.ElementAt(0);

            // Act
            s_setFlag.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            var replacement = (VisualElement)container.ElementAt(0);
            Assert.That(
                (s_fallbackShown, container.childCount, ((Label)replacement.ElementAt(0)).text, ((Label)container.ElementAt(1)).text),
                Is.EqualTo((true, 2, "caught", "b")));
        }

        [Component]
        private static VNode KeyedSyncListHost()
        {
            var (flipped, setFlipped) = Hooks.UseState(false);
            s_setFlag = setFlipped;
            return V.Div(children: flipped
                ? new VNode[]
                {
                    V.Div(key: "k0", children: new VNode[] { V.Component(BoundaryWrappingThrowerRender) }),
                    V.Label(text: "b-updated", key: "k1"),
                }
                : new VNode[]
                {
                    V.Label(text: "a", key: "k0"),
                    V.Label(text: "b", key: "k1"),
                });
        }

        [Test]
        public void Given_ErrorBoundaryAbortsDuringKeyedSyncReplace_When_Reconciled_Then_TheFallbackIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — keyed siblings with both keys present on both sides select the fully
            // synchronous keyed Pass-1 linear scan (the default V.Mount re-render path runs
            // frameBudgetMs: 0). Same type-flip-triggers-abort shape as the indexed case above.
            using var mounted = V.Mount(Root, V.Component(KeyedSyncListHost, key: "host"));
            var container = Root.ElementAt(0);

            // Act
            s_setFlag.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            var replacement = (VisualElement)container.ElementAt(0);
            Assert.That(
                (s_fallbackShown, container.childCount, ((Label)replacement.ElementAt(0)).text, ((Label)container.ElementAt(1)).text),
                Is.EqualTo((true, 2, "caught", "b")));
        }

        [Test]
        public void Given_AbortObservedDuringTimeSlicedKeyedReplace_When_Reconciled_Then_TheReplacementIsInsertedAndLaterSiblingIsUntouched()
        {
            // Arrange — an extremely small frame budget forces the time-sliced keyed Pass-1 linear
            // scan (Pass1Linear) instead of ReconcileKeyedSync, exercising the same helper call
            // site's checkAbortAfterCreate: true path under the state-machine (park/resume)
            // implementation. This path can only be driven by calling Reconciler.Reconcile directly
            // (V.Mount's re-render path always uses frameBudgetMs: 0), and a real error-boundary
            // component mounted this way would bootstrap its OWN isolated ReconcilerContext (its
            // fiber has no parent fiber to inherit the shared one from — see SetupMount), so
            // SetAborted() would never reach the context this test observes. A refCallback fired
            // during CreateElement stands in for the abort a real boundary would raise, exercising
            // the same _ctx.IsAborted contract without depending on component-fiber parentage.
            var oldTree = new VNode[] { V.Label(text: "a", key: "k0"), V.Label(text: "b", key: "k1") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var ctx = Reconciler.Context;

            var newTree = new VNode[]
            {
                V.Div(key: "k0", refCallback: _ =>
                {
                    ctx.IsAborted = true;
                    return null;
                }),
                V.Label(text: "b-updated", key: "k1"),
            };

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree, frameBudgetMs: 0.001);
            DrainPendingWork();

            // Assert — the rebuilt k0 element is inserted despite the abort (refCallback fires after
            // the element is fully built), and the abort stops the scan before k1 is reached, so its
            // original text survives instead of being patched to "b-updated".
            Assert.That((Root.childCount, ((Label)Root.ElementAt(1)).text), Is.EqualTo((2, "b")));
        }

        private void DrainPendingWork(int maxIterations = 500, double budget = 0.001)
        {
            var iterations = 0;
            while (Reconciler!.HasPendingWork)
            {
                if (iterations++ >= maxIterations)
                {
                    Assert.Fail($"DrainPendingWork: {maxIterations} iterations exceeded without completion");
                }
                Reconciler.ContinueReconcile(frameBudgetMs: budget);
            }
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
            return V.Component(ThrowingChildRender, key: "throwing-child");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion
    }
}
