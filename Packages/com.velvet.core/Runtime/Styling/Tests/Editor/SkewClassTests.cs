using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two bare <c>TryExtract</c>-shaped className parsers, colocated because the border-style tests
    /// mirror the skew-class tests' shape (no SetUp, plain static parser calls, last-token-wins cascade).
    /// <list type="bullet">
    /// <item>The <c>skew-x-*</c> / <c>skew-y-*</c> / <c>skew-*</c> utility parser (<see cref="StyleSkewClass"/>):
    /// the numeric scale is degrees (<c>skew-x-6</c>), a leading dash negates (<c>-skew-x-6</c>), the arbitrary
    /// form requires the unit (<c>skew-x-[8.5deg]</c>), the extractor takes the last token per axis of a list Normalize has put in Tailwind's order, <c>skew-x-0</c> is a
    /// recognized reset, and <c>skew-6</c> writes both axes beneath any per-axis class.</item>
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
            // Arrange — the extractor takes the last token it is handed; Normalize is what puts them in Tailwind's order.
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

        // --- skew-<n>: both axes ---

        [Test]
        public void Given_APlainSkew_When_Extracted_Then_BothAxesTakeTheAngle()
        {
            // Arrange
            var classes = new[] { "skew-6" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That((spec.XDeg, spec.YDeg), Is.EqualTo((6f, 6f)));
        }

        [Test]
        public void Given_ANegatedPlainSkew_When_Extracted_Then_BothAxesAreNegated()
        {
            // Arrange
            var classes = new[] { "-skew-6" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That((spec.XDeg, spec.YDeg), Is.EqualTo((-6f, -6f)));
        }

        [Test]
        public void Given_APlainSkewBracket_When_Extracted_Then_BothAxesTakeTheAngleInItsUnit()
        {
            // Arrange
            var classes = new[] { "skew-[.25turn]" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(new[] { spec.XDeg, spec.YDeg }, Is.EqualTo(new[] { 90f, 90f }).Within(1e-3f));
        }

        [Test]
        public void Given_APerAxisSkewWrittenBeforeAPlainSkew_When_Extracted_Then_ThePerAxisOneHoldsItsAxis()
        {
            // Arrange — Tailwind emits skew-<n> ahead of skew-x-*, so skew-x-12 wins its axis wherever it is written.
            var classes = new[] { "skew-x-12", "skew-6" };

            // Act
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That((spec.XDeg, spec.YDeg), Is.EqualTo((12f, 6f)));
        }

        [Test]
        public void Given_APlainSkewThatIsNoWholeNumberOfDegrees_When_Extracted_Then_ItIsDeclined()
        {
            // Act — Tailwind takes a bare skew value only as a positive integer, and a bracket only as an angle or 0.
            var fraction = StyleSkewClass.TryExtract(new[] { "skew-1.5" }, out _);
            var bareBracket = StyleSkewClass.TryExtract(new[] { "skew-[8]" }, out _);
            var whole = StyleSkewClass.TryExtract(new[] { "skew-2" }, out _);

            // Assert
            Assert.That((fraction, bareBracket, whole), Is.EqualTo((false, false, true)));
        }

        [Test]
        public void Given_APlainSkewBesideAPerAxisSkew_When_ProbedAndExtracted_Then_EachAxisWinnerMatchesItsSource()
        {
            // Arrange — the patch fast path compares the probe's winners against the spec's sources per axis.
            var classes = new[] { "skew-6", "skew-x-12" };

            // Act
            StyleSkewClass.TryGetWinningSkewClasses(classes, out var winnerX, out var winnerY);
            StyleSkewClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That($"{winnerX}|{winnerY}|{spec.SourceX}|{spec.SourceY}",
                Is.EqualTo("skew-x-12|skew-6|skew-x-12|skew-6"));
        }

        [Test]
        public void Given_APlainSkewBesideARejectedPerAxisBracket_When_Normalized_Then_NoSkewIsWanted()
        {
            // Arrange — skew-<n> and skew-x-[…] write one transform declaration, which a bare 8 invalidates.
            var rejected = StyleSpacingPrecedence.Normalize(new[] { "skew-6", "skew-x-[8]" });
            var accepted = StyleSpacingPrecedence.Normalize(new[] { "skew-6", "skew-x-[0]" });

            // Act
            var wantRejected = StyleSkewClass.TryExtract(rejected, out _);
            var wantAccepted = StyleSkewClass.TryExtract(accepted, out _);

            // Assert
            Assert.That((wantRejected, wantAccepted), Is.EqualTo((false, true)));
        }

        // --- one skew family written twice: Tailwind's sheet order ---

        // The angle each skew of classes resolves to after Normalize, written in that order and then reversed.
        private static string BothOrders(string first, string second)
        {
            StyleSkewClass.TryExtract(StyleSpacingPrecedence.Normalize(new[] { first, second }), out var written);
            StyleSkewClass.TryExtract(StyleSpacingPrecedence.Normalize(new[] { second, first }), out var reversed);
            return $"{written.XDeg},{written.YDeg} | {reversed.XDeg},{reversed.YDeg}";
        }

        [Test]
        public void Given_TwoSkewXStepsInEitherOrder_When_Normalized_Then_TheLargerStepWins()
        {
            // Act — Tailwind orders the two by candidate name, comparing the digit runs as numbers.
            var angles = BothOrders("skew-x-12", "skew-x-6");

            // Assert
            Assert.That(angles, Is.EqualTo("12,0 | 12,0"));
        }

        [Test]
        public void Given_ASkewXBracketAndAStepInEitherOrder_When_Normalized_Then_TheBracketWins()
        {
            // Act — `[` follows every digit in character code, so Tailwind emits the bracket after the step.
            var angles = BothOrders("skew-x-[10deg]", "skew-x-6");

            // Assert
            Assert.That(angles, Is.EqualTo("10,0 | 10,0"));
        }

        [Test]
        public void Given_ASkewYAndItsNegationInEitherOrder_When_Normalized_Then_ThePositiveOneWins()
        {
            // Act — the negated candidate sorts first, so Tailwind emits the positive one after it.
            var angles = BothOrders("skew-y-6", "-skew-y-6");

            // Assert
            Assert.That(angles, Is.EqualTo("0,6 | 0,6"));
        }

        [Test]
        public void Given_APlainSkewAndItsNegationInEitherOrder_When_Normalized_Then_ThePositiveOneWins()
        {
            // Act
            var angles = BothOrders("skew-6", "-skew-6");

            // Assert
            Assert.That(angles, Is.EqualTo("6,6 | 6,6"));
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
