using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the shape/outline utility-className → reconciler wrapper contract, covering both the
    /// <c>clip-path-*</c> stencil-mask wrapper and the <c>ring-*</c> / <c>outline-*</c> overlay wrapper
    /// together, since the two structural wrappers are mutually exclusive and their precedence is part of
    /// the contract.
    /// <list type="bullet">
    /// <item><c>clip-path-*</c>: UI Toolkit (6000.3) has no USS <c>clip-path</c>; its supported
    /// arbitrary-shape mask is an overflow-hidden element with a vector background, so a
    /// <c>clip-path-[…]</c> class makes Velvet wrap the element in a masking wrapper
    /// (<see cref="FiberWrapperElementAppliers.ClipPathWrapperClass"/>) that carries the baked shape.</item>
    /// <item><c>ring-*</c> / <c>outline-*</c>: UI Toolkit (6000.3) has no CSS box-shadow / outline either,
    /// so a ring class makes Velvet wrap the element in a layout wrapper
    /// (<see cref="FiberWrapperElementAppliers.RingWrapperClass"/>) holding a native-border OVERLAY
    /// sibling that paints the outset (or inset) band — no GPU shader, unlike the soft drop shadow. Width /
    /// color come from the utility scale; the corner radius follows the element's rounded-*.</item>
    /// <item>Both reuse the same <c>WrapperToInnerMap</c> seam. Clip-path OUTRANKS ring for the structural
    /// wrapper slot (the two are mutually exclusive); the drop shadow is a wrapper-less PAINT, so it
    /// composes with either wrapper instead of competing for it — CSS clip-path clips the box-shadow too,
    /// so an active clip suppresses the shadow paint, while a ring does not.</item>
    /// <item>Removing the wrapping class (or unmounting) destroys any baked resource (the VectorImage for
    /// clip) and removes the wrapper with no residue.</item>
    /// </list>
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathWrapTests
    {
        private const string Triangle = "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        private static VisualElement Wrapper(VisualElement root) => root[0];
        private static VisualElement Overlay(VisualElement root) => root[0][1]; // wrapper children: [inner, overlay]

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        // Creation

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_ElementIsWrappedInAClipWrapper()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_WrapperHidesOverflow()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert: overflow:hidden is half of the stencil-mask combination (vector bg + hidden).
            Assert.That(Wrapper(scope.Root).style.overflow.value, Is.EqualTo(Overflow.Hidden));
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_TheInnerElementIsTheWrappersChild()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root)[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_AClipClass_When_Reconciled_Then_ABindingIsTracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_NoClipClass_When_Reconciled_Then_ElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "rounded-2xl", name: "plain") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_ClipPathNone_When_Reconciled_Then_ElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "clip-path-none", name: "plain") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_ClipOnMotion_When_Reconciled_Then_NoClipWrapperIsCreated()
        {
            // Arrange — a Motion carrying a clip-path utility. Motion does not auto-wrap: a structural
            // wrapper would become the AnimatePresence enter/exit anchor while the transition stays on the
            // inner Motion.
            using var scope = new ReconcilerScope();
            LogAssert.Expect(LogType.Warning, new Regex(@"clip-path-\* utility on a Motion is ignored"));

            // Act
            Mount(scope, new VNode[] { V.Motion(Triangle, key: "m") });

            // Assert
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        // Clip suppresses the shadow paint (CSS clip-path clips the box-shadow too)

        [Test]
        public void Given_ClipAndShadowClasses_When_Reconciled_Then_TheClipWrapperWins()
        {
            // Arrange: the clip is a structural wrapper; the shadow is a paint suppressed while clipped.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_ClipAndShadowClasses_When_Reconciled_Then_NoShadowPaintIsAttached()
        {
            // Arrange: CSS clip-path clips the box-shadow too, so the shadow paint self-suppresses on the clip.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AShadowedElement_When_ClipClassAddedByPatch_Then_TheShadowPaintIsDetached()
        {
            // Arrange: shadow paint first, then a patch adds the clip — the clip clips the box-shadow, so the
            // shadow patch (running after the clip patch, seeing clipActive) detaches the paint.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: $"shadow-lg {Triangle}", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AClippedElement_When_ClipReplacedByShadowOnPatch_Then_TheShadowPaintTakesOver()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act: the clip class goes away and a shadow class appears in the same render.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));
        }

        // Patch: spec change

        [Test]
        public void Given_AClippedElement_When_ShapeChangedByPatch_Then_TheBindingSpecFollows()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);

            // Act: triangle → circle on the same element (linear patch, no key change).
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "clip-path-[circle(50%)]", name: "card") });

            // Assert
            var inner = Wrapper(scope.Root)[0];
            Assert.That(scope.Reconciler.Context.ClipPathBindings[inner].Spec.Kind,
                Is.EqualTo(ClipPathKind.Circle));
        }

        // Patch: class addition / removal

        [Test]
        public void Given_APlainElement_When_ClipClassAddedByPatch_Then_ElementIsWrappedInPlace()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);
        }

        [Test]
        public void Given_AClippedElement_When_ClipClassRemovedByPatch_Then_WrapperIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(Wrapper(scope.Root).ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.True);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "", name: "card") });

            // Assert: the inner element took the wrapper's slot; no wrapper residue.
            Assert.That(scope.Root[0].ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass), Is.False);
        }

        [Test]
        public void Given_AClippedElement_When_ClipClassRemovedByPatch_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: "", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Patch: steady state

        [Test]
        public void Given_AClippedElement_When_RepatchedWithTheSameClass_Then_TheSpecInstanceIsReused()
        {
            // Arrange: the patch fast path compares the winning clip token against the live binding's
            // Spec.Source before parsing — an unchanged class list must not rebuild the spec.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            var inner = Wrapper(scope.Root)[0];
            var specBefore = scope.Reconciler.Context.ClipPathBindings[inner].Spec;

            // Act: a re-render carries the identical clip class.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Div(className: Triangle, name: "card") });

            // Assert
            Assert.That(ReferenceEquals(specBefore, scope.Reconciler.Context.ClipPathBindings[inner].Spec), Is.True);
        }

        // Reconciler disposal

        [Test]
        public void Given_AClippedElement_When_ReconcilerDisposed_Then_ClipBindingsAreReleased()
        {
            // Arrange — a still-mounted clipped element. Root disposal never routes live elements
            // through FiberElementCleaner, so Dispose itself must release the clip bindings (and
            // destroy any baked VectorImage), symmetric with its ShadowBindings teardown.
            var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Triangle, name: "card") });
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Dispose();

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Unmount

        [Test]
        public void Given_AClippedElement_When_Unmounted_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AClippedElement_When_Unmounted_Then_WrapperToInnerMapIsEmpty()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: Triangle, name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.WrapperToInnerMap.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.WrapperToInnerMap.Count, Is.EqualTo(0));
        }

        // User wrapElement opt-out

        [Test]
        public void Given_AUserWrapElementWithClipClass_When_Patched_Then_NotDoubleWrapped()
        {
            // Arrange: an element with BOTH a user wrapElement and a clip-path class. The create path
            // returns the user's wrapper and opts out of the className clip (no ClipPathBinding).
            using var scope = new ReconcilerScope();
            Func<VisualElement, VisualElement> wrap = el =>
            {
                var w = new VisualElement();
                w.Add(el);
                return w;
            };
            var before = new VNode[] { V.Button(className: Triangle, wrapElement: wrap, key: "b") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));

            // Act: a re-render patches the same element.
            scope.Reconciler.Reconcile(scope.Root, before,
                new VNode[] { V.Button(className: Triangle, wrapElement: wrap, key: "b") });

            // Assert: patch must honor the opt-out and NOT stack a clip wrapper on the user wrapper.
            Assert.That(scope.Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(0));
        }

        // Ring / outline wrapper

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
            // Tailwind's default ring color is blue-500 at 0.5 alpha (the palette token resolves opaque).
            VelvetPalette.TryResolveColorToken("blue-500", out var blue);
            blue.a = 0.5f;

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
            // The shadow is a wrapper-less paint, so it does not compete with the ring for the wrapper: a
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
