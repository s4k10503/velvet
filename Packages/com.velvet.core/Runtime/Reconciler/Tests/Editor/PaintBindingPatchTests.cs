using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the wrapper-less paint-binding contract of <c>shadow-*</c> (drop-shadow).
    /// <para>
    /// UI Toolkit (6000.3) has no <c>box-shadow</c>; CSS <c>box-shadow</c> is a NON-structural paint (it does not
    /// change layout and it follows a transform on the element), so a <c>shadow-*</c> class attaches a
    /// wrapper-less paint binding (<see cref="BoxShadowSilhouette"/>) that draws the baked shadow texture
    /// behind the element's own content — no structural wrapper, keyed in <c>ShadowBindings</c> by the element
    /// itself. The shadow's preset (blur/color/spread) comes from the utility scale and its corner radius
    /// follows the element's <c>rounded-*</c>. Removing the class (or unmounting) must detach the paint with no
    /// residue.
    /// </para>
    /// These cases drive the reconciler handoff across mount and every add/remove transition and assert
    /// binding / tracking STATE only (no panel paint). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class PaintBindingPatchTests
    {

        private static BoxShadowBinding Binding(ReconcilerScope scope, VisualElement element)
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
        public void Given_AGatedShadow_When_TheReconcilerIsDisposed_Then_NoShadowBindingSurvives()
        {
            // Arrange — the theme is flipped AFTER the mount, and the order is the whole of what makes
            // this pose anything: the conditional manipulator's attach-time evaluation is inside its
            // panel check, so a theme already true at mount is never read, while the theme SIGNAL it
            // subscribes is not — which is what applies the payload off-panel here. Releasing the
            // manipulator at dispose then turns dark:shadow-sm off,
            // which moves a gate token and re-derives the element's passes — and shadow-lg is still on the
            // resolved list, so the pass re-attaches into the table ReleasePaintBindings emptied a few
            // lines earlier. Emptying the gate table first is what stops it.
            var scope = new ReconcilerScope();
            var context = scope.Reconciler.Context;
            var wasDark = VelvetTheme.IsDark;
            try
            {
                Mount(scope, new VNode[] { V.Div(className: "shadow-lg dark:shadow-sm", name: "card") });
                VelvetTheme.IsDark = true;
                var lit = scope.Root[0].ClassListContains("shadow-sm");
                var attached = context.ShadowBindings.Count;

                // Act
                scope.Dispose();

                // Assert — both preconditions ride along. The mounted count, since a payload that never
                // bound leaves the same empty table a correct teardown does; and the gated class, since
                // the plain shadow-lg binds either way, so an Arrange that flipped the theme first would
                // otherwise pass on the unfixed code with nothing to turn off.
                Assert.That((lit, attached, context.ShadowBindings.Count), Is.EqualTo((true, 1, 0)));
            }
            finally
            {
                VelvetTheme.IsDark = wasDark;
            }
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_ThePaintBindingIsKeyedByTheElement()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg", name: "card") });

            // Assert: the binding is reachable via the element's own side-channel (no queryable shadow child).
            Assert.That(BoxShadowSilhouette.TryGet(scope.Root[0]), Is.Not.Null);
        }

        [Test]
        public void Given_ShadowOnMotion_When_Reconciled_Then_APaintBindingIsCreated()
        {
            // Arrange — a Motion paints a shadow as any element does.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Motion("shadow-2xl", key: "m") });

            // Assert
            Assert.That(BoxShadowSilhouette.TryGet(scope.Root[0]), Is.Not.Null);
        }

        [Test]
        public void Given_AMotion_When_ShadowClassAddedByPatch_Then_APaintBindingIsCreated()
        {
            // Arrange — a Motion that gains shadow-lg on a re-render.
            using var scope = new ReconcilerScope();
            var before = new VNode[] { V.Motion("", key: "m") };
            Mount(scope, before);
            // Whether the create path bound nothing for a Motion without a shadow class.
            var unbound = BoxShadowSilhouette.TryGet(scope.Root[0]) == null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Motion("shadow-lg", key: "m") });

            // Assert — read as null where the create path had bound one already, so the patch is what binds it.
            Assert.That(unbound ? BoxShadowSilhouette.TryGet(scope.Root[0]) : null, Is.Not.Null);
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_BlurPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert: the lg preset's front layer blur.
            Assert.That(Binding(scope, scope.Root[0]).Layers[0].Blur, Is.EqualTo(15f));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_ColorAlphaPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert: the lg preset's front layer alpha.
            Assert.That(Binding(scope, scope.Root[0]).Layers[0].Color.a, Is.EqualTo(0.1f).Within(0.001f));
        }

        [Test]
        public void Given_ShadowLg_When_Reconciled_Then_SpreadPresetIsApplied()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg") });

            // Assert
            Assert.That(Binding(scope, scope.Root[0]).Layers[0].Spread, Is.EqualTo(-3f));
        }

        [Test]
        public void Given_Rounded2xlShadow_When_Reconciled_Then_CornerRadiusFollowsRoundedScale()
        {
            // Arrange: off-panel, so the rounded-* class scale (not resolvedStyle) supplies the radius.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-lg rounded-2xl") });

            // Assert: --radius-2xl == 16px (the rounded-2xl scale value).
            Assert.That(Binding(scope, scope.Root[0]).CornerRadii.x, Is.EqualTo(16f));
        }

        [Test]
        public void Given_NoShadowClass_When_Reconciled_Then_NoPaintBindingIsCreated()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "rounded-2xl", name: "plain") });

            // Assert
            Assert.That(BoxShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        [Test]
        public void Given_ShadowNone_When_Reconciled_Then_NoPaintBindingIsCreated()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: "shadow-none", name: "plain") });

            // Assert
            Assert.That(BoxShadowSilhouette.TryGet(scope.Root[0]), Is.Null);
        }

        // Parity win: a shadow is non-structural — it must not shift a sibling's layout.

        [Test]
        public void Given_TwoSiblings_When_OneGainsShadow_Then_TheNextSiblingKeepsItsSlotIndex()
        {
            // Arrange: two plain siblings in a column. A shadow paint adds nothing structural, so gaining a
            // shadow leaves the sibling order/indices unchanged.
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
            Assume.That(Binding(scope, scope.Root[0]).Layers[0].Blur, Is.EqualTo(15f));

            // Act: lg → md on the same element (linear patch, no key change).
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "shadow-md", name: "card") });

            // Assert
            Assert.That(Binding(scope, scope.Root[0]).Layers[0].Blur, Is.EqualTo(6f));
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
            Assume.That(BoxShadowSilhouette.TryGet(element), Is.Not.Null);

            // Act: the shadow class is removed.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[] { V.Div(className: "", name: "card") });

            // Assert: the paint is gone from the element's side-channel.
            Assert.That(BoxShadowSilhouette.TryGet(element), Is.Null);
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
            // the inner element, so it composes with the user wrapper without needing to opt out.
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
