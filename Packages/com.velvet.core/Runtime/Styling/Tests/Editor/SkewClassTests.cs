using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two bare <c>TryExtract</c>-shaped className parsers, colocated because the border-style tests
    /// mirror the skew-class tests' shape (no SetUp, plain static parser calls, last-token-wins cascade).
    /// <list type="bullet">
    /// <item>The <c>skew-x-*</c> / <c>skew-y-*</c> utility parser (<see cref="StyleSkewClass"/>): the numeric
    /// scale is degrees (<c>skew-x-6</c>), a leading dash negates (<c>-skew-x-6</c>), the arbitrary form requires
    /// the unit (<c>skew-x-[8.5deg]</c>), the cascade is last-wins per axis, and <c>skew-x-0</c> is a recognized
    /// reset.</item>
    /// <item>The <c>border-dashed</c> / <c>border-dotted</c> / <c>border-solid</c> utility parser
    /// (<see cref="StyleBorderStyleClass"/>): dashed / dotted are ACTIVE (a painted outline), border-solid is a
    /// recognized RESET (it overrides an earlier dashed / dotted, keeping the native solid border), and the
    /// cascade is last-token-wins.</item>
    /// </list>
    /// GWT, one assert per case.
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

        // --- border-dashed / border-dotted / border-solid utility parser ---

        [Test]
        public void Given_BorderDashed_When_Extracted_Then_TheStyleIsDashed()
        {
            // Act
            StyleBorderStyleClass.TryExtract(new[] { "border-2", "border-dashed" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [Test]
        public void Given_BorderDotted_When_Extracted_Then_TheStyleIsDotted()
        {
            // Act
            StyleBorderStyleClass.TryExtract(new[] { "border-dotted" }, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dotted));
        }

        [Test]
        public void Given_BorderSolid_When_Extracted_Then_ItIsNotActive()
        {
            // Act — border-solid keeps the native (solid) border, so there is no painted outline to attach.
            var active = StyleBorderStyleClass.TryExtract(new[] { "border-solid" }, out _);

            // Assert
            Assert.That(active, Is.False);
        }

        [Test]
        public void Given_DashedThenSolid_When_Extracted_Then_SolidResets()
        {
            // Arrange — CSS cascade: border-solid later in the list overrides the earlier border-dashed.
            var classes = new[] { "border-dashed", "border-solid" };

            // Act
            var active = StyleBorderStyleClass.TryExtract(classes, out _);

            // Assert
            Assert.That(active, Is.False);
        }

        [Test]
        public void Given_SolidThenDashed_When_Extracted_Then_DashedWins()
        {
            // Arrange — last recognized token wins, so dashed overrides an earlier reset.
            var classes = new[] { "border-solid", "border-dashed" };

            // Act
            StyleBorderStyleClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(spec.Style, Is.EqualTo(BorderLineStyle.Dashed));
        }

        [Test]
        public void Given_NoBorderStyleClass_When_GateProbed_Then_ItReturnsFalse()
        {
            // Act — the create / patch fast path depends on this gate ignoring plain width classes.
            var has = StyleBorderStyleClass.HasBorderStyleClass(new[] { "border-2", "w-full" });

            // Assert
            Assert.That(has, Is.False);
        }
    }
}
