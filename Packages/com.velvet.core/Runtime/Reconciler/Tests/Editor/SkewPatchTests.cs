using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>skew-x-*</c> className → sheared-silhouette reconciler contract. Skew is
    /// wrapper-less: the element's own <c>generateVisualContent</c> paints the sheared face, so a
    /// skewed element keeps its DOM slot (no structural wrapper), gets a tracked
    /// <see cref="SkewBinding"/>, composes with the wrapper-less shadow paint (the shadow follows the
    /// shear: <see cref="DropShadowBinding.SkewXDeg"/>), and detaches with no residue when the class is
    /// removed or the tree unmounts. For <c>TextElement</c> types (Button/Label) the silhouette
    /// callback is prepended so the sheared background renders BEFORE the text rather than on top of
    /// it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SkewPatchTests
    {
        private const string Skew = "-skew-x-6";

        private static void Mount(ReconcilerScope scope, VNode[] tree)
            => scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

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
    }
}
