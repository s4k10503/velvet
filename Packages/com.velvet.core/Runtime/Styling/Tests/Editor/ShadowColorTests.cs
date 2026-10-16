using System;
using NUnit.Framework;
using UnityEngine;

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
    }
}
