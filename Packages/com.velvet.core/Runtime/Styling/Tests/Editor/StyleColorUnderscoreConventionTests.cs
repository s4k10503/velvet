using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the underscore-for-space arbitrary-value convention for functional color notation.
    /// A className string splits on spaces, so a bracketed value embeds its spaces as underscores —
    /// shadow and clip-path arbitrary values already restore them before parsing. The rgb()/rgba()
    /// grammar must do the same: without the substitution, the underscore form of a copy-pasted
    /// "rgb(0, 128, 255)" fails byte parsing on its "_128"/"_255" channels, the class silently falls
    /// back to a no-op USS class, and the color is never applied.
    /// </summary>
    [TestFixture]
    internal sealed class StyleColorUnderscoreConventionTests
    {
        [Test]
        public void Given_RgbWithUnderscoreSpacing_When_Parsed_Then_ResolvesChannels()
        {
            // Act — the underscore form of "rgb(0, 128, 255)".
            var ok = StyleArbitraryValueResolver.TryParse("bg-[rgb(0,_128,_255)]", out var s);

            // Assert — recognized as an arbitrary background color with the spaced channels resolved.
            Assert.That((ok, s.Property, s.Color.g, s.Color.b),
                Is.EqualTo((true, ArbitraryProperty.BackgroundColor, 128f / 255f, 1f)));
        }

        [Test]
        public void Given_RgbaWithUnderscoreSpacing_When_Parsed_Then_ResolvesAlpha()
        {
            // Act — the underscore form of "rgba(255, 0, 0, 0.5)".
            var ok = StyleArbitraryValueResolver.TryParse("bg-[rgba(255,_0,_0,_0.5)]", out var s);

            // Assert — the alpha channel survives the underscore substitution.
            Assert.That((ok, s.Color.r, s.Color.a), Is.EqualTo((true, 1f, 0.5f)));
        }
    }
}
