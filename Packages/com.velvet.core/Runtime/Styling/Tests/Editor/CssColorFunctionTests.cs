using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS Color 4 functions the shared colour grammar (<see cref="StyleColorValueParser.TryParseColor"/>)
    /// reads, each against the sRGB value the specification's conversion gives: <c>rgb()</c> / <c>rgba()</c> and
    /// <c>hsl()</c> / <c>hsla()</c> in the comma and the space syntax, <c>hwb()</c>, the <c>/</c> alpha, percentages,
    /// <c>none</c>, hue units, the parse-time clamps, and <c>transparent</c>. Underscores stand for spaces, as a
    /// class string writes them.
    /// </summary>
    internal sealed class CssColorFunctionTests
    {
        private const float Tolerance = 1e-4f;

        // A value the grammar declines reads as NaN in every channel, so a case expecting a colour fails on it
        // rather than comparing against the default colour.
        private static float[] Parse(string value)
            => StyleColorValueParser.TryParseColor(value.AsSpan(), out var c)
                ? new[] { c.r, c.g, c.b, c.a }
                : new[] { float.NaN, float.NaN, float.NaN, float.NaN };

        private static bool Parses(string value) => StyleColorValueParser.TryParseColor(value.AsSpan(), out _);

        [Test]
        public void Given_SpaceSyntaxRgbWithSlashAlpha_When_Parsed_Then_ChannelsAndAlphaResolve()
        {
            // Act
            var color = Parse("rgb(255_0_0_/_50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_RgbPercentages_When_Parsed_Then_HundredPercentIsTheFullChannel()
        {
            // Act
            var color = Parse("rgb(100%_50%_0%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0.5f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ANoneRgbComponent_When_Parsed_Then_ItReadsAsZero()
        {
            // Act
            var color = Parse("rgb(none_255_0)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 1f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ASlashWithoutSpaces_When_Parsed_Then_TheAlphaStillResolves()
        {
            // Act
            var color = Parse("rgb(0_0_255/0.25)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 1f, 0.25f }).Within(Tolerance));
        }

        [Test]
        public void Given_ANoneAlpha_When_Parsed_Then_ItReadsAsZero()
        {
            // Act
            var color = Parse("rgb(0_0_255_/_none)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 1f, 0f }).Within(Tolerance));
        }

        [Test]
        public void Given_AnAlphaAboveOne_When_Parsed_Then_ItIsClampedToOne()
        {
            // Act
            var color = Parse("rgb(0_0_255_/_1.5)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 1f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_SpaceSyntaxChannelsOutsideTheRange_When_Parsed_Then_TheyAreClamped()
        {
            // Act
            var color = Parse("rgb(300_-20_0)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyRgbWithAFourthArgument_When_Parsed_Then_ItIsTheAlpha()
        {
            // Act
            var color = Parse("rgb(255,0,0,0.25)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 0.25f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyRgbaWithThreeArguments_When_Parsed_Then_ItIsOpaque()
        {
            // Act
            var color = Parse("rgba(0,0,255)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 1f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyChannelsOutsideTheRange_When_Parsed_Then_TheyAreClamped()
        {
            // Act
            var color = Parse("rgb(300,-20,0,2)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyPercentages_When_Parsed_Then_HundredPercentIsTheFullChannel()
        {
            // Act
            var color = Parse("rgb(100%,_0%,_50%,_50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0.5f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_AFractionalChannel_When_Parsed_Then_ItIsKept()
        {
            // Act
            var color = Parse("rgb(127.5,0,0)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.5f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ASignedExponentNumber_When_Parsed_Then_ItIsANumber()
        {
            // Act
            var color = Parse("rgb(+2.55e2_0_0)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_AnUppercaseFunctionName_When_Parsed_Then_ItIsRead()
        {
            // Act
            var color = Parse("RGB(255_0_0)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }).Within(Tolerance));
        }

        // Each declined value sits beside the nearest value the grammar reads, so a grammar declining
        // everything fails here too.
        [TestCase("rgb(100%,0,0)", "rgb(100%,0%,0%)")]
        [TestCase("rgb(0,0%,0%)", "rgb(0%,0%,0%)")]
        [TestCase("rgb(none,0,0)", "rgb(none_0_0)")]
        [TestCase("rgb(0,0,0,none)", "rgb(0,0,0,0)")]
        [TestCase("rgb(0,0,0,0,0)", "rgb(0,0,0,0)")]
        [TestCase("rgb(0,0)", "rgba(0,0,0)")]
        [TestCase("rgb(0_0_0_/)", "rgb(0_0_0_/_1)")]
        [TestCase("rgb(0_0)", "rgb(0_0_0)")]
        [TestCase("rgb(0_0_0_0)", "rgb(0_0_0_/_0)")]
        [TestCase("rgb(0_0_/_0_1)", "rgb(0_0_0_/_1)")]
        [TestCase("rgb(0_0_0_/_1_1)", "rgb(0_0_0_/_1)")]
        [TestCase("rgb(0_0_0_/_/_1)", "rgb(0_0_0_/_1)")]
        [TestCase("rgb(0_0_x)", "rgb(0_0_0)")]
        [TestCase("rgb(0_0_infinity)", "rgb(0_0_1e1)")]
        [TestCase("rgb(0_0_nan)", "rgb(0_0_1e1)")]
        [TestCase("rgb(0_0_1.)", "rgb(0_0_1.0)")]
        [TestCase("rgb(0_0_1e)", "rgb(0_0_1e0)")]
        [TestCase("rgb(0_0_1e+)", "rgb(0_0_1e+0)")]
        [TestCase("rgb(0_0_.)", "rgb(0_0_.5)")]
        [TestCase("rgb(0_0_-)", "rgb(0_0_-0)")]
        [TestCase("rgb(0_0_1x)", "rgb(0_0_1)")]
        [TestCase("rgb(0_0_1e1x)", "rgb(0_0_1e1)")]
        [TestCase("rgb(0_0_1x5)", "rgb(0_0_1e5)")]
        [TestCase("rgb(0_0_x%)", "rgb(0_0_0%)")]
        [TestCase("rgb(0_0_0", "rgb(0_0_0)")]
        [TestCase("rgbx(0_0_0)", "rgb(0_0_0)")]
        [TestCase("hsl(120,100,25%)", "hsl(120,100%,25%)")]
        [TestCase("hsl(120,100%,25)", "hsl(120,100%,25%)")]
        [TestCase("hsl(none,100%,50%)", "hsl(none_100%_50%)")]
        [TestCase("hsl(1px_100%_50%)", "hsl(1deg_100%_50%)")]
        [TestCase("hsl(120,100%,25%,0.5,1)", "hsl(120,100%,25%,0.5)")]
        [TestCase("hsl(120,100%)", "hsl(120,100%,25%)")]
        [TestCase("hsl(120,100%,25%,x)", "hsl(120,100%,25%,1)")]
        [TestCase("hsl(120_100%_25%_/_x)", "hsl(120_100%_25%_/_1)")]
        [TestCase("hwb(150,20%,10%)", "hwb(150_20%_10%)")]
        [TestCase("notacolor", "transparent")]
        public void Given_AValueTheGrammarDeclines_When_Parsed_Then_OnlyItsReadableNeighbourParses(string declined, string read)
        {
            // Act
            var parsed = (Parses(declined), Parses(read));

            // Assert
            Assert.That(parsed, Is.EqualTo((false, true)));
        }

        // Each literal is past double's range, and reads as Chromium reads it: a channel, a percentage or an alpha
        // clamps to its end, and a hue reads as 0deg.
        [TestCase("rgb(1e999_0_0)", 1f, 0f, 0f, 1f)]
        [TestCase("rgb(+1e999,0,0)", 1f, 0f, 0f, 1f)]
        [TestCase("rgb(1e999%_0_0)", 1f, 0f, 0f, 1f)]
        [TestCase("rgb(-1e999_0_0)", 0f, 0f, 0f, 1f)]
        [TestCase("rgb(255_0_0_/_-1e999)", 1f, 0f, 0f, 0f)]
        [TestCase("hsl(0_1e999%_50%)", 1f, 0f, 0f, 1f)]
        [TestCase("hsl(90_1e999%_50%)", 0.5f, 1f, 0f, 1f)]
        [TestCase("hsl(1e999_100%_50%)", 1f, 0f, 0f, 1f)]
        [TestCase("hsl(-1e999deg_100%_50%)", 1f, 0f, 0f, 1f)]
        [TestCase("hsl(1e307turn_100%_50%)", 1f, 0f, 0f, 1f)]
        public void Given_ALiteralPastDoublesRange_When_Parsed_Then_ItReadsAsChromiumReadsIt(string value, float r, float g, float b, float a)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { r, g, b, a }).Within(Tolerance));
        }

        [Test]
        public void Given_SpaceSyntaxHsl_When_Parsed_Then_ItIsTheSpecificationsSrgb()
        {
            // Act
            var color = Parse("hsl(210_40%_50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.3f, 0.5f, 0.7f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_HslSaturationAndLightnessAsNumbers_When_Parsed_Then_TheyReadAsPercentages()
        {
            // Act
            var color = Parse("hsl(210_40_50)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.3f, 0.5f, 0.7f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyHslaWithAlpha_When_Parsed_Then_ChannelsAndAlphaResolve()
        {
            // Act
            var color = Parse("hsla(120,100%,25%,0.5)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0.5f, 0f, 0.5f }).Within(Tolerance));
        }

        [Test]
        public void Given_LegacyHslWithAnAngleUnit_When_Parsed_Then_TheHueReadsInThatUnit()
        {
            // Act
            var color = Parse("hsl(0.5turn,100%,50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 1f, 1f, 1f }).Within(Tolerance));
        }

        [TestCase("hsl(180deg_100%_50%)")]
        [TestCase("hsl(0.5turn_100%_50%)")]
        [TestCase("hsl(200grad_100%_50%)")]
        [TestCase("hsl(3.14159265rad_100%_50%)")]
        [TestCase("hsl(-180_100%_50%)")]
        [TestCase("hsl(540_100%_50%)")]
        public void Given_AHueOfHalfATurn_When_Parsed_Then_ItIsCyan(string value)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 1f, 1f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ANegativeHue_When_Parsed_Then_ItWrapsAroundTheCircle()
        {
            // Act — -120deg is 240deg, sRGB blue.
            var color = Parse("hsl(-120_100%_50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 1f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ANoneHue_When_Parsed_Then_ItReadsAsZeroDegrees()
        {
            // Act
            var color = Parse("hsl(none_100%_50%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0f, 0f, 1f }).Within(Tolerance));
        }

        [TestCase("hsl(0_-50%_50%)")]
        [TestCase("hsl(0,-50%,50%)")]
        public void Given_ANegativeSaturation_When_Parsed_Then_ItIsClampedToGray(string value)
        {
            // Act
            var color = Parse(value);

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.5f, 0.5f, 0.5f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_Hwb_When_Parsed_Then_ItIsTheSpecificationsSrgb()
        {
            // Act — the specification's own example: hwb(150 20% 10%) is rgb(20% 90% 55%).
            var color = Parse("hwb(150_20%_10%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.2f, 0.9f, 0.55f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_HwbWhoseWhiteAndBlackExceedTheWhole_When_Parsed_Then_ItIsTheirRatioOfGray()
        {
            // Act — white 40 and black 80 sum past 100, so the gray is 40 / (40 + 80).
            var color = Parse("hwb(45_40%_80%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f / 3f, 1f / 3f, 1f / 3f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_AHslLighterThanHalf_When_Parsed_Then_ItMixesTowardWhite()
        {
            // Act
            var color = Parse("hsl(120_100%_75%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.5f, 1f, 0.5f, 1f }).Within(Tolerance));
        }

        [Test]
        public void Given_ANegativeHwbWhiteness_When_Parsed_Then_ItIsNotClampedAtParse()
        {
            // Act — the specification's sample conversion gives rgb(1, 0.4, -0.2) for a whiteness of -20%.
            var color = Parse("hwb(30_-20%_0%)");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 1f, 0.4f, 0f, 1f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): a hex colour read through ColorUtility before and still does.
        [Test]
        public void Given_AHexColour_When_Parsed_Then_ItStillReadsThroughColorUtility()
        {
            // Act
            var color = Parse("#336699");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f, 1f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): ColorUtility reads the keyword as transparent black before the function
        // grammar sits in front of it, and must still once it does.
        [Test]
        public void Given_Transparent_When_Parsed_Then_ItIsTransparentBlack()
        {
            // Act
            var color = Parse("transparent");

            // Assert
            Assert.That(color, Is.EqualTo(new[] { 0f, 0f, 0f, 0f }).Within(Tolerance));
        }
    }
}
