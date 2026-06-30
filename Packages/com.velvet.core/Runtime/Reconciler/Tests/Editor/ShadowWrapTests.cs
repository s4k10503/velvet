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
    /// Specifies the <c>shadow-*</c> className → drop-shadow reconciler contract. UI Toolkit (6000.3) has no
    /// <c>box-shadow</c>; CSS <c>box-shadow</c> is a NON-structural paint (it does not change layout and it
    /// follows a transform on the element), so a <c>shadow-*</c> class attaches a wrapper-less paint binding
    /// (<see cref="DropShadowSilhouette"/>) that draws the baked shadow texture behind the element's own
    /// content — no structural wrapper, keyed in <c>ShadowBindings</c> by the element itself. The shadow's
    /// preset (blur/color/spread) comes from the utility scale and its corner radius follows the element's
    /// <c>rounded-*</c>. Removing the class (or unmounting) must detach the paint with no residue.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ShadowWrapTests
    {
        private static DropShadowBinding Binding(ReconcilerScope scope, VisualElement element)
            => scope.Reconciler.Context.ShadowBindings.TryGetValue(element, out var b) ? b : null;

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

        // Creation — wrapper-less paint

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_TheElementSitsDirectlyInTheRootWithNoWrapper()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert: no wrapper interposed — the shadowed element IS the root's direct child (paint, not wrap).
            Assert.That(scope.Root[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_APaintBindingIsTracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert: exactly one shadow paint binding, keyed by the element itself.
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_ThePaintBindingIsKeyedByTheElement()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert: the binding is reachable via the element's own side-channel (no queryable shadow child).
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Not.Null);
        }

        [Test]
        public void Given_ShadowOnMotion_When_Reconciled_Then_NoPaintBindingIsCreated()
        {
            // Arrange — a Motion carrying a shadow-* utility. A shadow on the animating element itself cannot
            // show: the enter/exit fade hides shadow paints, so the shadow belongs on a wrapped Div.
            using var scope = new ReconcilerScope();
            LogAssert.Expect(LogType.Warning, new Regex(@"shadow-\* utility on a Motion is ignored"));

            // Act
            Mount(scope, new VNode[] { V.Motion("shadow-2xl", key: "m") });

            // Assert — the Motion element carries no shadow paint binding.
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        [Test]
        public void Given_AMotion_When_ShadowClassAddedByPatch_Then_NoPaintBindingIsCreated()
        {
            // Arrange — a Motion that gains shadow-lg on a re-render. The create path refuses the paint on a
            // Motion; the patch path must enforce the same rule (a Motion never starts a shadow paint).
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Motion("", key: "m") };
            Mount(scope, before);
            Assume.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Null);

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Motion("shadow-lg", key: "m") });

            // Assert — still no paint binding on the Motion element.
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_BlurPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert: lg preset blur.
            Assert.That(Binding(scope, scope.Root[0]).Spec.Blur, Is.EqualTo(34f));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_ColorAlphaPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert: lg preset alpha (--color-shadow base, stepped per preset).
            Assert.That(Binding(scope, scope.Root[0]).Spec.Color.a, Is.EqualTo(0.28f).Within(0.001f));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_SpreadPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert
            Assert.That(Binding(scope, scope.Root[0]).Spec.Spread, Is.EqualTo(0f));
        }

        [Test]
        public void Given_Rounded2xlShadow_When_Reconciled_Then_CornerRadiusFollowsRoundedScale()
        {
            // Arrange: off-panel, so the rounded-* class scale (not resolvedStyle) supplies the radius.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg rounded-2xl") });

            // Assert: --radius-2xl == 16px (the rounded-2xl scale value).
            Assert.That(Binding(scope, scope.Root[0]).CornerRadius, Is.EqualTo(16f));
        }

        [Test]
        public void Given_NoShadowClass_When_Reconciled_Then_NoPaintBindingIsCreated()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "rounded-2xl", name: "plain") });

            // Assert
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        [Test]
        public void Given_ShadowNone_When_Reconciled_Then_NoPaintBindingIsCreated()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-none", name: "plain") });

            // Assert
            Assert.That(DropShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        // Parity win: a shadow is non-structural — it must not shift a sibling's layout.

        [Test]
        public void Given_TwoSiblings_When_OneGainsShadow_Then_TheNextSiblingKeepsItsSlotIndex()
        {
            // Arrange: two plain siblings in a column. The wrapper era interposed a container around the
            // shadowed element; a paint adds nothing structural, so the sibling order/indices are unchanged.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "", name: "a"),
                V.Div(className: "", name: "b"),
            };
            Mount(scope, before);
            Assume.That(scope.Root[1].name, Is.EqualTo("b"));

            // Act: the first sibling gains a shadow.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "shadow-lg", name: "a"),
                V.Div(className: "", name: "b"),
            });

            // Assert: the second sibling still occupies index 1 — no wrapper was interposed around the first.
            Assert.That(scope.Root[1].name, Is.EqualTo("b"));
        }

        [Test]
        public void Given_AShadowedElement_When_Reconciled_Then_NoWrapperElementIsAddedAroundIt()
        {
            // Arrange / Act: a lone shadowed element.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert: the root has exactly one child (the card) — the shadow added no sibling/wrapper element.
            Assert.That(scope.Root.childCount, Is.EqualTo(1));
        }

        // Patch: preset change

        [Test]
        public void Given_AShadowedElement_When_PresetChangedByPatch_Then_BlurUpdatesInPlace()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            Assume.That(Binding(scope, scope.Root[0]).Spec.Blur, Is.EqualTo(34f));

            // Act: lg → md on the same element (linear patch, no key change).
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "shadow-md", name: "card") });

            // Assert
            Assert.That(Binding(scope, scope.Root[0]).Spec.Blur, Is.EqualTo(22f));
        }

        // Patch: class removal (detach)

        [Test]
        public void Given_AShadowedElement_When_ShadowClassRemovedByPatch_Then_PaintBindingIsDetached()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            var element = scope.Root[0];
            Assume.That(DropShadowSilhouette.TryGet(element), Is.Not.Null);

            // Act: the shadow class is removed.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "", name: "card") });

            // Assert: the paint is gone from the element's side-channel.
            Assert.That(DropShadowSilhouette.TryGet(element), Is.Null);
        }

        [Test]
        public void Given_AShadowedElement_When_ShadowClassRemovedByPatch_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        // Unmount

        [Test]
        public void Given_AShadowedElement_When_Unmounted_Then_BindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Div(className: "shadow-lg", name: "card") };
            Mount(scope, before);
            Assume.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(0));
        }

        // User wrapElement composition: a paint composes with a user wrapper (no opt-out needed).

        [Test]
        public void Given_AUserWrapElementWithShadowClass_When_Reconciled_Then_TheInnerCarriesTheShadowPaint()
        {
            // Arrange: an element with BOTH a user wrapElement and a shadow-* class. The shadow is a paint on
            // the inner element, so it composes with the user wrapper rather than opting out (the wrapper era
            // had to opt out to avoid a double structural wrapper).
            using var scope = new ReconcilerScope();
            Func<VisualElement, VisualElement> wrap = el =>
            {
                var w = new VisualElement();
                w.Add(el);
                return w;
            };

            // Act
            Mount(scope, new VNode[] { V.Button(className: "shadow-lg", wrapElement: wrap, key: "b") });

            // Assert: exactly one shadow paint binding exists (on the inner button) — composes, not double-wraps.
            Assert.That(scope.Reconciler.Context.ShadowBindings.Count, Is.EqualTo(1));
        }
    }
}
