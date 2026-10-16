using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The shadow colour utilities, <c>shadow-&lt;colour&gt;</c> and <c>shadow-[&lt;colour&gt;]</c>: Tailwind writes them to
    /// <c>--tw-shadow-color</c>, which every colour of a box-shadow size reads before its own, so the colour
    /// replaces the size's whichever class comes first and draws nothing alone. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ShadowColorTests
    {
        private static Color Parsed(string css)
        {
            StyleColorValueParser.TryParseColor(css.AsSpan(), out var color);
            return color;
        }

        [Test]
        public void Given_AShadowSizeAndABracketedColour_When_Extracted_Then_TheShadowTakesTheColour()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-lg", "shadow-[#ff0000]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color : Color.clear, Is.EqualTo(Parsed("#ff0000")));
        }

        [Test]
        public void Given_APaletteColourBeforeTheSize_When_Extracted_Then_TheShadowTakesTheColour()
        {
            // Arrange
            VelvetPalette.TryResolveColorToken("red-500", out var red);

            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-red-500", "shadow-lg" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color : Color.clear, Is.EqualTo(red));
        }

        [Test]
        public void Given_AShadowColourWithAnOpacityModifier_When_Extracted_Then_ItsAlphaIsScaled()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-lg", "shadow-red-500/50" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color.a : float.NaN, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ABracketedShadowWithItsOwnColour_When_AShadowColourIsAdded_Then_TheShadowColourWins()
        {
            // Tailwind writes the bracket's colour as var(--tw-shadow-color, #000).
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_4px_8px_#000]", "shadow-[#ff0000]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color : Color.clear, Is.EqualTo(Parsed("#ff0000")));
        }

        [Test]
        public void Given_AShadowColourAlone_When_Extracted_Then_NoShadowIsDrawn()
        {
            // Act — the control is the same colour beside a size, which it colours.
            var alone = StyleShadowClass.TryExtract(new[] { "shadow-[#ff0000]" }, out _);
            StyleShadowClass.TryExtract(new[] { "shadow-[#ff0000]", "shadow-lg" }, out var sized);

            // Assert
            Assert.That((alone, sized.Color), Is.EqualTo((false, Parsed("#ff0000"))));
        }

        [Test]
        public void Given_ADropShadowBesideAShadowColour_When_Extracted_Then_TheDropShadowKeepsItsColour()
        {
            // Act — a drop-shadow reads its own colour property; the control is a box-shadow size, which reads
            // --tw-shadow-color.
            StyleShadowClass.TryExtract(new[] { "drop-shadow-lg" }, out var plain);
            StyleShadowClass.TryExtract(new[] { "drop-shadow-lg", "shadow-[#ff0000]" }, out var dropped);
            StyleShadowClass.TryExtract(new[] { "shadow-lg", "shadow-[#ff0000]" }, out var boxed);

            // Assert
            Assert.That((dropped.Color == plain.Color, boxed.Color == Parsed("#ff0000")), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARejectedShadowFollowedByAShadowColour_When_Extracted_Then_TheBoxShadowStaysDiscarded()
        {
            // Act — a shadow colour writes no box-shadow, so it cannot replace the one the browser rejected; the
            // control is the ring without that rejected shadow.
            var discarded = StyleRingClass.TryExtract(new[] { "ring-2", "shadow-[0_4_8_#000]", "shadow-[#ff0000]" }, out _);
            var kept = StyleRingClass.TryExtract(new[] { "ring-2", "shadow-[#ff0000]" }, out _);

            // Assert
            Assert.That((discarded, kept), Is.EqualTo((false, true)));
        }

        // A size's opacity modifier, currentcolor, and an inherited shadow colour, as Tailwind v4 writes them.

        [Test]
        public void Given_ABracketedShadowNamingNoColour_When_Extracted_Then_ItTakesTheTextColour()
        {
            // Act — Tailwind writes var(--tw-shadow-color, currentcolor).
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_4px_8px]" }, out var spec);

            // Assert
            Assert.That((ok, spec.CurrentColor, spec.Color.a), Is.EqualTo((true, true, 1f)));
        }

        [Test]
        public void Given_AColourlessBracketedShadowWithAModifier_When_Extracted_Then_TheTextColourIsScaled()
        {
            // Act — color-mix(in oklab, currentcolor 50%, transparent).
            StyleShadowClass.TryExtract(new[] { "shadow-[0_4px_8px]/50" }, out var spec);

            // Assert
            Assert.That((spec.CurrentColor, spec.Color.a), Is.EqualTo((true, 0.5f)));
        }

        [TestCase("shadow-lg/20")]
        [TestCase("shadow/20")]
        [TestCase("shadow-[0_4px_8px_#000]/20")]
        public void Given_ASizeWithAModifier_When_Extracted_Then_ItsOwnColourTakesThatAlpha(string utility)
        {
            // Act — oklab(from <colour> l a b / 20%) replaces the size colour's alpha.
            var ok = StyleShadowClass.TryExtract(new[] { utility }, out var spec);

            // Assert
            Assert.That(ok ? spec.Color.a : float.NaN, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Given_ASizeModifierBesideAShadowColour_When_Extracted_Then_TheColourIsScaledByIt()
        {
            // Act — the modifier is --tw-shadow-alpha, which the colour is mixed with.
            StyleShadowClass.TryExtract(new[] { "shadow-lg/20", "shadow-[#ff0000]" }, out var spec);

            // Assert
            Assert.That(spec.Color.a, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Given_ShadowCurrent_When_Extracted_Then_TheShadowTakesTheTextColourAtItsModifier()
        {
            // Act
            StyleShadowClass.TryExtract(new[] { "shadow-lg", "shadow-current/50" }, out var spec);

            // Assert
            Assert.That((spec.CurrentColor, spec.Color.a), Is.EqualTo((true, 0.5f)));
        }

        [Test]
        public void Given_AShadowSizeWithAModifier_When_GateChecked_Then_ItIsAShadowClass()
        {
            Assert.That((StyleShadowClass.IsShadowClass("shadow/20"), StyleShadowClass.IsShadowClass("shadowy")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ShadowInheritUnderAColouredParent_When_Reconciled_Then_TheShadowTakesTheParentsColour()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "shadow-[#ff0000]", name: "parent", children: new VNode[]
                {
                    V.Div(className: "shadow-lg shadow-inherit", name: "child"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var child = scope.Root.Q<VisualElement>("child");

            // Assert
            Assert.That(DropShadowSilhouette.TryGet(child)?.Spec.TintFor(child), Is.EqualTo((Color?)Parsed("#ff0000")));
        }

        [Test]
        public void Given_ShadowInheritUnderAParentNamingNoShadowColour_When_Reconciled_Then_TheSizeKeepsItsOwn()
        {
            // Arrange — --tw-shadow-color is unset on the parent, so var() falls back to the size's own colour; the
            // control is the same size with no inherit.
            using var scope = new ReconcilerScope();
            StyleShadowClass.TryExtract(new[] { "shadow-lg" }, out var plain);
            var tree = new VNode[]
            {
                V.Div(className: "p-1", name: "parent", children: new VNode[]
                {
                    V.Div(className: "shadow-lg shadow-inherit", name: "child"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            var child = scope.Root.Q<VisualElement>("child");

            // Assert
            Assert.That(DropShadowSilhouette.TryGet(child)?.Spec.TintFor(child), Is.EqualTo((Color?)plain.Color));
        }
    }
}
