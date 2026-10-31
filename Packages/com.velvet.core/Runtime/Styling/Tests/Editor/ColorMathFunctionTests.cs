using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a math function in a colour function's channel, hue or alpha, read as Chromium reads it: a number
    /// or a percentage of the channel's reference, an angle or a number of degrees for a hue, clamped as a written
    /// channel is, with a NaN as 0 and an infinity as the channel's largest.
    /// </summary>
    internal sealed class ColorMathFunctionTests
    {
        private static string Channels(string value)
            => StyleColorValueParser.TryParseColor(value, out var c)
                ? $"{c.r * 255:0.#} {c.g * 255:0.#} {c.b * 255:0.#} {c.a:0.##}"
                : "declined";

        [TestCase("rgb(calc(255/2)_0_0)", "127.5 0 0 1")]
        [TestCase("rgb(calc(50%)_0_0)", "127.5 0 0 1")]
        [TestCase("rgb(50%_calc(50%)_0)", "127.5 127.5 0 1")]
        [TestCase("rgb(calc(300)_0_0)", "255 0 0 1")]
        [TestCase("rgb(calc(-20)_0_0)", "0 0 0 1")]
        [TestCase("rgb(calc(255/0)_0_0)", "255 0 0 1")]
        [TestCase("rgb(calc(NaN)_0_0)", "0 0 0 1")]
        [TestCase("rgb(min(255,_300)_0_0)", "255 0 0 1")]
        [TestCase("rgb(255_0_0_/_calc(0.5))", "255 0 0 0.5")]
        [TestCase("rgb(255_0_0_/_calc(50%))", "255 0 0 0.5")]
        [TestCase("rgba(calc(255),0,0,calc(0.5))", "255 0 0 0.5")]
        [TestCase("hsl(calc(60deg*2)_100%_50%)", "0 255 0 1")]
        [TestCase("hsl(calc(120)_100%_50%)", "0 255 0 1")]
        [TestCase("hsl(calc(120deg),calc(50%*2),50%)", "0 255 0 1")]
        [TestCase("hwb(calc(0deg)_calc(10%)_0%)", "255 25.5 25.5 1")]
        public void Given_AMathFunctionInAColourFunction_When_Parsed_Then_ItIsWhatChromiumReads(string value, string expected)
        {
            // Act
            var channels = Channels(value);

            // Assert
            Assert.That(channels, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): a length is no colour channel, was declined, and still is.
        [Test]
        public void Given_AMathFunctionComingToALengthInAChannel_When_Parsed_Then_TheColourIsDeclined()
        {
            // Act
            var channels = Channels("rgb(calc(1px)_0_0)");

            // Assert
            Assert.That(channels, Is.EqualTo("declined"));
        }
    }
}
