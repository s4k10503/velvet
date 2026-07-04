using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>ring-*</c> / <c>outline-*</c> className → reconciler contract. UI Toolkit (6000.3) has
    /// no CSS box-shadow / outline, so a ring class makes Velvet wrap the element in a layout wrapper
    /// (<see cref="FiberWrapperElementAppliers.RingWrapperClass"/>) holding a native-border OVERLAY sibling that paints
    /// the outset (or inset) band — no GPU shader, unlike the soft drop shadow. Width / color come from the
    /// utility scale; the corner radius follows the element's rounded-*. Ring is the lowest-precedence
    /// structural-WRAPPER layer (clip-path takes the wrapper first; the two are mutually exclusive). The drop
    /// shadow is a wrapper-less PAINT, so a ring composes with a shadow rather than competing for the wrapper.
    /// Removing the class (or unmounting) removes the wrapper with no residue. GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class RingWrapTests
    {
        private static VisualElement Wrapper(VisualElement root) => root[0];
        private static VisualElement Overlay(VisualElement root) => root[0][1]; // wrapper children: [inner, overlay]

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        [Test]
        public void Given_Ring2_When_Reconciled_Then_ElementIsWrappedInARingWrapper()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True);
        }

        [Test]
        public void Given_Ring2_When_Reconciled_Then_OverlayCarriesTheRingWidth()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(Overlay(scope.Root).style.borderTopWidth.value, Is.EqualTo(2f));
        }

        [Test]
        public void Given_BareRing_When_Reconciled_Then_OverlayUsesDefaultBlueRingColor()
        {
            using var scope = new ReconcilerScope();
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);

            Mount(scope, new VNode[] { V.Div(className: "ring", name: "card") });

            Assert.That(Overlay(scope.Root).style.borderTopColor.value, Is.EqualTo(blue));
        }

        [Test]
        public void Given_RingWithColor_When_Reconciled_Then_OverlayUsesThatColor()
        {
            using var scope = new ReconcilerScope();
            VelvetPalette.TryResolveColorToken("red-500", out var red);

            Mount(scope, new VNode[] { V.Div(className: "ring-2 ring-red-500", name: "card") });

            Assert.That(Overlay(scope.Root).style.borderRightColor.value, Is.EqualTo(red));
        }

        [Test]
        public void Given_Outline2_When_Reconciled_Then_ElementIsWrapped()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "outline-2", name: "card") });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True);
        }

        [Test]
        public void Given_ARingedElement_When_RingClassRemovedByPatch_Then_Unwrapped()
        {
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            Assume.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True,
                "Precondition: wrapped while ring-2 is present");

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "", name: "card") });

            // The inner card is restored at the wrapper's slot — no ring wrapper left.
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.False);
        }

        [Test]
        public void Given_ShadowAndRingTogether_When_Reconciled_Then_TheRingStillTakesTheWrapper()
        {
            // The shadow is a wrapper-less paint, so it no longer competes with the ring for the wrapper: a
            // shadow+ring element wears the ring wrapper AND carries a shadow paint on the inner.
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "shadow-lg ring-2", name: "card") });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True);
        }

        [Test]
        public void Given_ShadowAndRingTogether_When_Reconciled_Then_TheInnerCarriesTheShadowPaint()
        {
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Div(className: "shadow-lg ring-2", name: "card") });

            // Compose, not exclude: the ring-wrapped inner element carries the shadow paint binding.
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0][0]), Is.Not.Null);
        }

        [Test]
        public void Given_RingOnMotion_When_Reconciled_Then_NoRingWrapperIsCreated()
        {
            // A Motion never receives a structural wrapper (it would become the AnimatePresence enter/exit
            // anchor while the transition stays on the inner Motion, breaking it).
            using var scope = new ReconcilerScope();

            Mount(scope, new VNode[] { V.Motion("ring-2", key: "m") });

            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.False);
        }

        [Test]
        public void Given_APlainElement_When_RingClassAddedByPatch_Then_WrappedInPlace()
        {
            // The patch wrap-in-place path: a plain element gains ring-2 on a re-render and is wrapped.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.False,
                "Precondition: not wrapped while ring-less");

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "ring-2", name: "card") });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True);
        }

        [Test]
        public void Given_ARingedElement_When_ShadowClassAddedByPatch_Then_TheRingWrapperIsKept()
        {
            // ring + shadow now COMPOSE (shadow is a paint, not a competing wrapper): adding a shadow to a
            // ringed element keeps the ring wrapper in place and attaches a shadow paint to the inner.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            Assume.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True,
                "Precondition: ring-wrapped");

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "ring-2 shadow-lg", name: "card") });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True);
        }

        [Test]
        public void Given_ARingedElement_When_ShadowClassAddedByPatch_Then_TheShadowPaintIsCreatedOnTheInner()
        {
            // The discriminating half: the shadow paint actually attaches to the ring-wrapped inner element.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "ring-2 shadow-lg", name: "card") });

            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARingedElement_When_ClipClassAddedByPatch_Then_TheClipWrapperReplacesTheRing()
        {
            // ring → clip transition: clip outranks ring, so the clip wrapper must claim the element.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, before);
            Assume.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.RingWrapperClass), Is.True,
                "Precondition: ring-wrapped");

            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "ring-2 clip-path-[polygon(50%_0%,100%_100%,0%_100%)]", name: "card"),
            });

            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_ARingedElement_When_Unmounted_Then_NoRingBindingResidueRemains()
        {
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "ring-2", name: "card") };
            Mount(scope, tree);
            Assume.That(scope.Reconciler.Context.RingBindings.Count, Is.EqualTo(1), "Precondition: one ring binding");

            scope.Reconciler.Reconcile(scope.Root, tree, Array.Empty<VNode>());

            Assert.That(scope.Reconciler.Context.RingBindings.Count, Is.EqualTo(0));
        }
    }
}
