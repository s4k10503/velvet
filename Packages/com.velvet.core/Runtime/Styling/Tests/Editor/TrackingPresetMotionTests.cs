using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// What a play can drive of the tracking presets: <c>tracking-normal</c> is a pixel length of zero, as
    /// <c>tracking-[0px]</c> is, and the others are em lengths measured on the element, which no play carries.
    /// GWT, one assert.
    /// </summary>
    internal sealed class TrackingPresetMotionTests
    {
        [Test]
        public void Given_TrackingNormal_When_ParsedForAPlay_Then_ItIsAZeroPixelLetterSpacing()
        {
            // Act
            var ok = MotionPropertyClassParser.TryParse("tracking-normal", out var style);

            // Assert
            Assert.That((ok, style.Property, style.Value, style.Expression == null),
                Is.EqualTo((true, ArbitraryProperty.LetterSpacing, 0f, true)));
        }

        // GREEN_ON_BASE(characterization): the em presets were declined for a play on the base too; the case pins that the new zero-pixel tracking-normal does not pull them in.
        [TestCase("tracking-tighter")]
        [TestCase("tracking-tight")]
        [TestCase("tracking-wide")]
        [TestCase("tracking-wider")]
        [TestCase("tracking-widest")]
        public void Given_AnEmTrackingPreset_When_ParsedForAPlay_Then_ItIsLeftToTheElementToMeasure(string preset)
        {
            // Act
            var ok = MotionPropertyClassParser.TryParse(preset, out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }
}
