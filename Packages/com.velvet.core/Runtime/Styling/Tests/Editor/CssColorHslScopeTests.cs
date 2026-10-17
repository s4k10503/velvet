using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a colour's <c>hsl()</c> saturation is clamped, floored and read past float's range as
    /// Chromium reads it, beside <see cref="CssColorFunctionTests"/>'s grammar: which spellings of the function and
    /// which utilities Chromium reads on the fast path that clamps a saturation above 100%, the space syntax's floor
    /// on a lightness, and a hue past float's largest. Underscores stand for spaces, as a class string writes them.
    /// </summary>
    internal sealed class CssColorHslScopeTests
    {
        private const float Tolerance = 1e-4f;

        private static float[] Parse(string value)
            => StyleColorValueParser.TryParseColor(value.AsSpan(), out var c)
                ? new[] { c.r, c.g, c.b, c.a }
                : new[] { float.NaN, float.NaN, float.NaN, float.NaN };

        // The value as bg-[...] spells it: background-color is a colour longhand, so Chromium reads a plain hsl()
        // there on the fast path that clamps its saturation.
        private static float[] ParseLonghand(string value) => ColorOfClass("bg-[" + value + "]");

        [TestCase("hsl(0_150%_75%)", 1f, 0.5f, 0.5f, 1f)]
        [TestCase("hsla(0_150%_75%)", 1f, 0.5f, 0.5f, 1f)]
        [TestCase("hsl(0deg_150.5%_75%)", 1f, 0.5f, 0.5f, 1f)]
        [TestCase("hsl(0grad_150%_75%)", 1f, 0.5f, 0.5f, 1f)]
        [TestCase("hsl(0rad_150%_75%)", 1f, 0.5f, 0.5f, 1f)]
        [TestCase("hsl(.0turn_150%_75%_/_0.5)", 1f, 0.5f, 0.5f, 0.5f)]
        [TestCase("hsl(0_150%_75%_/_.5)", 1f, 0.5f, 0.5f, 0.5f)]
        [TestCase("hsl(-0_150%_75%)", 1f, 0.5f, 0.5f, 1f)]
        public void Given_AnHslSaturationAbove100_When_ParsedAsALonghand_Then_ItClampsWhereChromiumReadsItPlain(string value, float r, float g, float b, float a)
        {
            // Act
            var color = ParseLonghand(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { r, g, b, a }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the base leaves every saturation above 100% unclamped, and each of
        // these spellings must still, being one Chromium's fast path declines; a clamp keyed on the saturation
        // alone gives the 0.5 channels instead.
        [TestCase("hsl(0_150_75)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(none_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(0_150%_75%_/_none)", 1f, 0.375f, 0.375f, 0f)]
        [TestCase("hsl(0_150%_75%_/_50%)", 1f, 0.375f, 0.375f, 0.5f)]
        [TestCase("hsl(0_150%_75%_/_5e-1)", 1f, 0.375f, 0.375f, 0.5f)]
        [TestCase("hsl(0_150%_75%_/_0.5_)", 1f, 0.375f, 0.375f, 0.5f)]
        [TestCase("hsl(0e0_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(+0_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(0_1.5e2%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(0_150%_75e0%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hsl(0_150%_.5%)", 0.0125f, 0f, 0f, 1f)]
        [TestCase("HSL(0_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("Hsl(0_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        [TestCase("hSLa(0_150%_75%)", 1f, 0.375f, 0.375f, 1f)]
        public void Given_AnHslSaturationAbove100_When_ChromiumDeclinesItsFastPath_Then_ItStaysUnclamped(string value, float r, float g, float b, float a)
        {
            // Act
            var color = ParseLonghand(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { r, g, b, a }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the base reads a whiteness above 100% as it is, and the plain-hsl clamp
        // must stay out of hwb(): the gray is 150 / (150 + 10), where a whiteness clamped to 100 gives 100 / 110.
        [Test]
        public void Given_AnHwbWhitenessAbove100_When_ParsedAsALonghand_Then_ItIsNotClamped()
        {
            // Act
            var color = ParseLonghand("hwb(0_150%_10%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.9375f, 0.9375f, 0.9375f, 1f }).Within(Tolerance));
        }

        // The space syntax floors a saturation and a lightness at zero when it parses, as Chromium's general parser
        // does, so a negative lightness is black whatever the saturation beside it.
        [TestCase("hsl(0_150_-50)")]
        [TestCase("hsl(0_150%_-50%)")]
        public void Given_ANegativeHslLightnessInTheSpaceSyntax_When_Parsed_Then_ItIsBlack(string value)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [TestCase("hsl(0,150%,75%)")]
        [TestCase("hsl(0e0,150%,75%)")]
        [TestCase("hsl(0,150%,75%,1)")]
        public void Given_ALegacyHslSaturationAbove100_When_Parsed_Then_ItClamps(string value)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0.5f, 0.5f, 1f }).Within(Tolerance));
        }

        // A number past float's range reads as float's largest of its sign, so each of these hues is the hue of
        // plus or minus 3.4028235e38: 0deg as degrees or turns, 216deg as gradians and 240deg as radians, and
        // 144deg and 120deg for the negative gradians and radians.
        [TestCase("hsl(1e39_100%_50%)", "hsl(0_100%_50%)")]
        [TestCase("hsl(1e100_100%_50%)", "hsl(0_100%_50%)")]
        [TestCase("hsl(1e305_100%_50%)", "hsl(0_100%_50%)")]
        [TestCase("hsl(-1e305_100%_50%)", "hsl(0_100%_50%)")]
        [TestCase("hsl(1e300turn_100%_50%)", "hsl(0_100%_50%)")]
        [TestCase("hsl(1e300grad_100%_50%)", "hsl(216_100%_50%)")]
        [TestCase("hsl(4e38grad_100%_50%)", "hsl(216_100%_50%)")]
        [TestCase("hsl(-1e300grad_100%_50%)", "hsl(144_100%_50%)")]
        [TestCase("hsl(1e300rad_100%_50%)", "hsl(240_100%_50%)")]
        [TestCase("hsl(-1e300rad_100%_50%)", "hsl(120_100%_50%)")]
        public void Given_AHuePastFloatsRange_When_Parsed_Then_ItIsTheHueOfFloatsLargest(string value, string equivalent)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(Parse(equivalent)).Within(Tolerance));
        }

        // 150% saturation beside 75% lightness is 0.5 clamped and 0.375 unclamped in the green and blue channels.
        private static float[] ColorOfClass(string cls)
            => StyleArbitraryValueResolver.TryParse(cls, out var s)
                ? new[] { s.Color.r, s.Color.g, s.Color.b, s.Color.a }
                : new[] { float.NaN, float.NaN, float.NaN, float.NaN };

        [TestCase("bg-[hsl(0_150%_75%)]")]
        [TestCase("text-[hsl(0_150%_75%)]")]
        [TestCase("border-t-[hsl(0_150%_75%)]")]
        [TestCase("border-be-[hsl(0_150%_75%)]")]
        public void Given_AnHslSaturationAbove100_When_AColourLonghandUtilityReadsIt_Then_ItClamps(string cls)
        {
            // Act
            var color = ColorOfClass(cls);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0.5f, 0.5f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_AnHslSaturationAbove100_When_OutlineReadsIt_Then_ItClamps()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "outline-2", "outline-[hsl(0_150%_75%)]" }, out var spec);

            // Assert
            Assert.That(new[] { spec.Color.r, spec.Color.g, spec.Color.b }, Is.EqualTo(new[] { 1f, 0.5f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_AnHslSaturationAbove100_When_ACaretSuffixReadsIt_Then_ItClamps()
        {
            // Act
            StyleColorValueParser.TryParseColorSuffix("[hsl(0_150%_75%)]", out var color);

            // Assert
            Assert.That(new[] { color.r, color.g, color.b }, Is.EqualTo(new[] { 1f, 0.5f, 0.5f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the base never clamps a saturation, and these utilities must not start to,
        // each being a shorthand, a colour mixed with an opacity modifier or a box shadow, which Chromium reads
        // through its general parser.
        [TestCase("border-[hsl(0_150%_75%)]", 1f)]
        [TestCase("border-x-[hsl(0_150%_75%)]", 1f)]
        [TestCase("border-y-[hsl(0_150%_75%)]", 1f)]
        [TestCase("bg-[hsl(0_150%_75%)]/50", 0.5f)]
        [TestCase("text-[hsl(0_150%_75%)]/50", 0.5f)]
        [TestCase("border-t-[hsl(0_150%_75%)]/50", 0.5f)]
        public void Given_AnHslSaturationAbove100_When_AShorthandOrAMixedColourReadsIt_Then_ItStaysUnclamped(string cls, float alpha)
        {
            // Act
            var color = ColorOfClass(cls);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0.375f, 0.375f, alpha }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the base never clamps a saturation, and ring-, a gradient stop and a
        // suffix carrying an opacity modifier must not start to; none is a colour longhand.
        [Test]
        public void Given_AnHslSaturationAbove100_When_RingReadsIt_Then_ItStaysUnclamped()
        {
            // Act
            StyleRingClass.TryExtract(new[] { "ring-2", "ring-[hsl(0_150%_75%)]" }, out var spec);

            // Assert
            Assert.That(new[] { spec.Color.r, spec.Color.g, spec.Color.b }, Is.EqualTo(new[] { 1f, 0.375f, 0.375f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): see the ring case above.
        [Test]
        public void Given_AnHslSaturationAbove100_When_AGradientStopReadsIt_Then_ItStaysUnclamped()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r", "from-[hsl(0_150%_75%)]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(new[] { spec.Stops[0].Color.r, spec.Stops[0].Color.g, spec.Stops[0].Color.b },
                Is.EqualTo(new[] { 1f, 0.375f, 0.375f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): see the ring case above.
        [Test]
        public void Given_AnHslSaturationAbove100_When_ASuffixWithAnOpacityModifierReadsIt_Then_ItStaysUnclamped()
        {
            // Act
            StyleColorValueParser.TryParseColorSuffix("[hsl(0_150%_75%)]/50", out var color);

            // Assert
            Assert.That(new[] { color.r, color.g, color.b }, Is.EqualTo(new[] { 1f, 0.375f, 0.375f }).Within(Tolerance));
        }
    }
}
