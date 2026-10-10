using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that each utility taking a colour reads a CSS colour function through the shared grammar:
    /// <c>bg-</c> / <c>text-</c> / <c>border-[..]</c>, the opacity modifier, <c>ring-</c> / <c>outline-</c>,
    /// <c>divide-</c>, <c>shadow-</c> / <c>drop-shadow-</c> and the gradient stops.
    /// <see cref="CssColorFunctionTests"/> owns the values the grammar gives; here one value, <c>hsl(210 40% 50%)</c> (sRGB 0.3, 0.5, 0.7), stands for all of them.
    /// </summary>
    internal sealed class CssColorFunctionUtilityTests
    {
        private const float Tolerance = 1e-4f;
        private const string Hsl = "hsl(210_40%_50%)";
        private static readonly float[] Expected = { 0.3f, 0.5f, 0.7f, 1f };

        // What a case reads when its utility declined the class, so the comparison fails rather than reading a
        // default colour.
        private static readonly float[] Unresolved = { float.NaN, float.NaN, float.NaN, float.NaN };

        private static float[] Channels(Color c) => new[] { c.r, c.g, c.b, c.a };

        [TestCase("bg-", ArbitraryProperty.BackgroundColor)]
        [TestCase("text-", ArbitraryProperty.TextColor)]
        [TestCase("border-", ArbitraryProperty.BorderColor)]
        public void Given_AColorFunctionInAPrefixsBrackets_When_Parsed_Then_ThePrefixsColorTakesIt(string prefix, ArbitraryProperty property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(prefix + "[" + Hsl + "]", out var style);

            // Assert
            Assert.That(ok && style.Property == property ? Channels(style.Color) : Unresolved,
                Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_AnOpacityModifierOnAColorFunction_When_Parsed_Then_TheModifierSetsTheAlpha()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("bg-[" + Hsl + "]/50", out var style);

            // Assert
            Assert.That(ok ? Channels(style.Color) : Unresolved,
                Is.EqualTo(new[] { 0.3f, 0.5f, 0.7f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_ARingColorFunction_When_Extracted_Then_TheRingTakesIt()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring-2", "ring-[" + Hsl + "]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_ADivideColorFunction_When_Extracted_Then_TheDividerTakesIt()
        {
            // Act
            var ok = StyleDivideClass.TryExtract(new[] { "divide-x", "divide-[" + Hsl + "]" }, out var spec);

            // Assert
            Assert.That(ok && spec.HasColor ? Channels(spec.Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_AShadowColorFunctionWithSpaces_When_Extracted_Then_ItIsOneColourToken()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_4px_8px_hsl(210_40%_50%_/_50%)]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Color) : Unresolved,
                Is.EqualTo(new[] { 0.3f, 0.5f, 0.7f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_AShadowLegacyRgbWithSpacesAfterItsCommas_When_Extracted_Then_ItIsOneColourToken()
        {
            // Act — the comma syntax the grammar already read, spaced as pasted CSS spaces it.
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_4px_8px_rgb(51,_102,_153)]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Color) : Unresolved,
                Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_AFromStopColorFunction_When_Extracted_Then_TheFirstStopTakesIt()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[" + Hsl + "]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Stops[0].Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_AColorFunctionInAStopList_When_Extracted_Then_ItsSpacesDoNotSplitTheStop()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right," + Hsl + ",#0000ff]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Stops[0].Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_AnOutlineColorFunction_When_Extracted_Then_TheOutlineTakesIt()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "outline-2", "outline-[" + Hsl + "]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }

        [Test]
        public void Given_ADropShadowColorFunction_When_Extracted_Then_TheShadowTakesIt()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "drop-shadow-[0_4px_8px_" + Hsl + "]" }, out var spec);

            // Assert
            Assert.That(ok ? Channels(spec.Color) : Unresolved, Is.EqualTo(Expected).Within(Tolerance));
        }
    }
}
