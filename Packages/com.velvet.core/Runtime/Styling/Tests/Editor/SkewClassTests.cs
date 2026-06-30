using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>skew-x-*</c> / <c>skew-y-*</c> utility parser (StyleSkewClass): the
    /// numeric scale is degrees (<c>skew-x-6</c>), a leading dash negates (<c>-skew-x-6</c>), the
    /// arbitrary form requires the unit (<c>skew-x-[8.5deg]</c>), the cascade is last-wins per
    /// axis, and <c>skew-x-0</c> is a recognized reset. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SkewClassTests
    {
        [Test]
        public void Given_APresetSkewX_When_Extracted_Then_TheAngleIsInDegrees()
        {
            // Arrange
            var classes = new[] { "w-full", "skew-x-6" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(6f));
        }

        [Test]
        public void Given_ALeadingDash_When_Extracted_Then_TheAngleIsNegated()
        {
            // Arrange
            var classes = new[] { "-skew-x-6" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(-6f));
        }

        [Test]
        public void Given_AnArbitraryValue_When_Extracted_Then_TheFloatDegreesParse()
        {
            // Arrange
            var classes = new[] { "skew-x-[8.5deg]" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(8.5f));
        }

        [Test]
        public void Given_ANegativeArbitraryY_When_Extracted_Then_TheYAxisIsNegated()
        {
            // Arrange
            var classes = new[] { "-skew-y-[2deg]" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.YDeg, Is.EqualTo(-2f));
        }

        [Test]
        public void Given_TwoSkewXClasses_When_Extracted_Then_TheLaterWins()
        {
            // Arrange — CSS cascade: later classes win.
            var classes = new[] { "skew-x-6", "-skew-x-12" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(-12f));
        }

        [Test]
        public void Given_ASkewThenAZeroReset_When_Extracted_Then_NoSkewIsWanted()
        {
            // Arrange — skew-x-0 is a recognized reset that overrides the earlier skew.
            var classes = new[] { "skew-x-6", "skew-x-0" };

            // Act
            var want = StyleSkewClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.False);
        }

        [Test]
        public void Given_BothAxes_When_Extracted_Then_TheyResolveIndependently()
        {
            // Arrange
            var classes = new[] { "skew-x-6", "skew-y-3" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That((spec.XDeg, spec.YDeg), Is.EqualTo((6f, 3f)));
        }

        [Test]
        public void Given_AnArbitraryValueWithoutTheDegUnit_When_Extracted_Then_ItIsNotRecognized()
        {
            // Arrange — the arbitrary skew requires the unit; px is not an angle.
            var classes = new[] { "skew-x-[8px]" };

            // Act
            var want = StyleSkewClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.False);
        }

        [Test]
        public void Given_AParseableSkewThenATrailingJunkSkew_When_ProbedAndExtracted_Then_WinnerMatchesSource()
        {
            // Arrange — a parseable winner followed by an unparseable skew-x token. The patch fast path equates
            // the probe's winner with the spec's Source, so both must key on the same (last PARSEABLE) token.
            var classes = new[] { "skew-x-6", "skew-x-junk" };

            // Act
            StyleSkewClass.TryGetWinningSkewClasses(classes, out var winnerX, out _);
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert — equal, so ApplySkewOnPatch's steady-state guard fires (no per-patch re-parse thrash).
            Assert.That(winnerX, Is.EqualTo(spec.SourceX));
        }
    }
}
