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
    /// Specifies two wrapper-less paint-binding contracts that compose with each other: <c>shadow-*</c>
    /// (drop-shadow) and <c>skew-x-*</c> (sheared silhouette).
    /// <para>
    /// UI Toolkit (6000.3) has no <c>box-shadow</c>; CSS <c>box-shadow</c> is a NON-structural paint (it does not
    /// change layout and it follows a transform on the element), so a <c>shadow-*</c> class attaches a
    /// wrapper-less paint binding (<see cref="DropShadowSilhouette"/>) that draws the baked shadow texture
    /// behind the element's own content — no structural wrapper, keyed in <c>ShadowBindings</c> by the element
    /// itself. The shadow's preset (blur/color/spread) comes from the utility scale and its corner radius
    /// follows the element's <c>rounded-*</c>. Removing the class (or unmounting) must detach the paint with no
    /// residue.
    /// </para>
    /// <para>
    /// Skew is likewise wrapper-less: the element's own <c>generateVisualContent</c> paints the sheared face, so
    /// a skewed element keeps its DOM slot (no structural wrapper), gets a tracked <see cref="SkewBinding"/>,
    /// composes with the wrapper-less shadow paint (the shadow follows the shear:
    /// <see cref="DropShadowBinding.SkewXDeg"/>), and detaches with no residue when the class is removed or the
    /// tree unmounts. For <c>TextElement</c> types (Button/Label) the silhouette callback is prepended so the
    /// sheared background renders BEFORE the text rather than on top of it. It also composes with
    /// <c>bg-gradient-*</c>: a skewed element cannot show a gradient through the rectangular background-image
    /// path (it cannot follow the shear), so a skewed element OWNS its gradient instead — the spec is fed into
    /// the <see cref="SkewBinding"/> and painted on the sheared mesh (<c>SkewSilhouette</c>), while the non-skew
    /// gradient path (<c>GradientBackground</c>, tracked in <c>GradientBackgrounds</c>) stands down, so the
    /// gradient renders exactly once, never as a straight rectangle behind the slant.
    /// </para>
    /// These cases drive the reconciler handoff across mount and every add/remove transition and assert
    /// binding / tracking STATE only (no panel paint). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class PaintBindingPatchTests
    {
        private const string Skew = "-skew-x-6";
        private const string Gradient = "bg-gradient-to-b from-[#FF0000] to-[#0000FF]";

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

        // --- skew-x-* paint binding ---

        [Test]
        public void Given_ASkewClass_When_Reconciled_Then_ABindingIsTracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Skew, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.SkewBindings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ASkewClass_When_Reconciled_Then_TheElementIsNotWrapped()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Skew, name: "card") });

            // Assert — skew paints on the element itself; the DOM slot holds the element directly.
            Assert.That(scope.Root[0].name, Is.EqualTo("card"));
        }

        [Test]
        public void Given_ASkewBinding_When_Extracted_Then_ItCarriesTheParsedAngle()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew, name: "card") });

            // Act
            var binding = scope.Reconciler.Context.SkewBindings[scope.Root[0]];

            // Assert
            Assert.That(binding.Spec.XDeg, Is.EqualTo(-6f));
        }

        [Test]
        public void Given_ASkewedElement_When_TheClassIsRemovedByPatch_Then_TheBindingIsUntracked()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew, name: "card") };
            Mount(scope, oldTree);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Div(className: "w-full", name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.SkewBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ASkewedShadowedElement_When_Reconciled_Then_TheShadowFollowsTheShear()
        {
            // Arrange — skew composes with the wrapper-less shadow paint (both on the same element); the
            // shadow paint must shear with the caster so a skewed card's shadow follows the slant.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Div(className: Skew + " shadow-md", name: "card") });

            // Assert — the shadow paint is keyed by the element itself and carries the caster's skew angle.
            var element = scope.Root[0];
            Assert.That(scope.Reconciler.Context.ShadowBindings[element].SkewXDeg, Is.EqualTo(-6f));
        }

        [Test]
        public void Given_ASkewedElementWithAFaceColor_When_FirstMounted_Then_TheNativeRectIsSuppressedWithoutAPatch()
        {
            // Arrange — a skewed element whose face color is authored inline (the bg-[…] case). The silhouette
            // suppresses the native rectangular background/border and re-paints them sheared; if that
            // suppression only ran on a later patch, the un-sheared rectangle would paint THROUGH the slant as
            // a double image until the first click/state change.
            using var scope = new ReconcilerScope();

            // Act — initial mount ONLY, no patch.
            Mount(scope, new VNode[] { V.Div(className: Skew + " bg-[#FF0000]", name: "card") });

            // Assert — the binding has already suppressed the native chrome (no double image on first paint).
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].SuppressionApplied, Is.True);
        }

        [Test]
        public void Given_ASkewedInlineColoredElement_When_PatchedWithAnAddOnlyClass_Then_TheSuppressionSurvives()
        {
            // Arrange — a skewed element whose face color is an inline bg-[…] (suppressed at mount).
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew + " bg-[#FF0000]", name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].SuppressionApplied, Is.True,
                "Precondition: mount suppressed the native rect");

            // Act — an ADD-ONLY class change (no arbitrary class removed), so the resolver does NOT re-write the
            // inline bg; the sentinel stays in place. An inline-driven stash must not be released here.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew + " bg-[#FF0000] mt-[8px]", name: "card") });

            // Assert — suppression survives, so the native rectangle does not reappear as a double image.
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].SuppressionApplied, Is.True);
        }

        [Test]
        public void Given_ASkewedElementWithAnInlineBorder_When_PatchedToANewBorderColor_Then_TheNewBorderIsReCaptured()
        {
            // Arrange — a skewed element with inline bg + inline border (both suppressed at mount). bg stays the
            // sentinel across the patch, so only the inline BORDER slot signals "the resolver overwrote us".
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew + " bg-[#000000] border-[#FFFFFF]", name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].BorderColor,
                Is.EqualTo(new UnityEngine.Color(1f, 1f, 1f, 1f)), "Precondition: mount captured the white border");

            // Act — patch rewrites ONLY the inline border color.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew + " bg-[#000000] border-[#FF0000]", name: "card") });

            // Assert — the binding re-captured the new red border (the sheared outline updates).
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].BorderColor,
                Is.EqualTo(new UnityEngine.Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void Given_ASkewedElement_When_TheTreeUnmounts_Then_NoBindingRemains()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew, name: "card") };
            Mount(scope, oldTree);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, Array.Empty<VNode>());

            // Assert
            Assert.That(scope.Reconciler.Context.SkewBindings.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_ASkewedButtonWithText_When_Reconciled_Then_TheSilhouetteCallbackIsPrependedBeforeTextRendering()
        {
            // Arrange — V.Button with text sets element.text on a TextElement; UITK registers its internal
            // text-rendering callback at construction, before Attach runs. Without the prepend fix the
            // silhouette fill is appended AFTER the text callback and covers the label.
            using var scope = new ReconcilerScope();

            // Act
            Mount(scope, new VNode[] { V.Button(className: Skew + " bg-[#3A4B5E]", text: "Squad", name: "seg") });

            // Assert — the silhouette OnGenerate delegate is the FIRST entry in the invocation list,
            // so the sheared background renders before (i.e. behind) the text.
            var element = scope.Root[0];
            var binding = scope.Reconciler.Context.SkewBindings[element];
            var invocations = element.generateVisualContent?.GetInvocationList();
            Assume.That(invocations, Is.Not.Null.And.Length.GreaterThan(1),
                "Precondition: at least the silhouette callback and the text callback must be present");
            Assert.That(invocations[0], Is.EqualTo(binding.OnGenerate));
        }

        // Gradient composition

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_TheSkewBindingOwnsTheGradient()
        {
            // Arrange / Act — a single element carrying both skew and a gradient.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the skew binding carries the gradient (it paints the sheared mesh fill).
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.True);
        }

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_NoStraightGradientIsTracked()
        {
            // Arrange / Act
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the straight background-image path stood down (deferred to the skew binding), so a
            // second, un-sheared gradient rectangle is never tracked behind the slant.
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.False);
        }

        [Test]
        public void Given_SkewAndGradient_When_Mounted_Then_TheParsedDirectionReachesTheBinding()
        {
            // Arrange / Act — bg-gradient-to-b resolves to CSS 180°; verify the whole spec threads through,
            // not just the has-gradient flag.
            using var scope = new ReconcilerScope();
            Mount(scope, new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].Gradient.AngleDeg,
                Is.EqualTo(180f));
        }

        [Test]
        public void Given_AGradientedSkewedElement_When_TheGradientClassesAreRemovedByPatch_Then_TheBindingRevertsToSolid()
        {
            // Arrange — skew + gradient (the binding owns the gradient).
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.True,
                "Precondition: mount fed the gradient into the skew binding");

            // Act — patch keeps skew but drops the gradient classes.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew, name: "card") });

            // Assert — the binding no longer carries a gradient, so Draw paints the solid fill again.
            Assert.That(scope.Reconciler.Context.SkewBindings[scope.Root[0]].HasGradient, Is.False);
        }

        [Test]
        public void Given_AStraightGradientElement_When_SkewIsAddedByPatch_Then_TheStraightGradientIsDropped()
        {
            // Arrange — a gradient WITHOUT skew: it takes the straight background-image path (tracked).
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[] { V.Div(className: Gradient, name: "card") };
            Mount(scope, oldTree);
            Assume.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.True,
                "Precondition: a non-skewed gradient is tracked on the straight path");

            // Act — skew is added; the skew binding takes ownership of the gradient.
            scope.Reconciler.Reconcile(scope.Root, oldTree,
                new VNode[] { V.Div(className: Skew + " " + Gradient, name: "card") });

            // Assert — the straight gradient is dropped (else an un-sheared rectangle lingers behind the slant).
            Assert.That(scope.Reconciler.Context.GradientBackgrounds.ContainsKey(scope.Root[0]), Is.False);
        }
    }
}
