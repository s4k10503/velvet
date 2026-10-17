using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies two bare <c>TryExtract</c>-shaped className parsers, colocated because the border-style tests
    /// mirror the skew-class tests' shape (no SetUp, plain static parser calls, last-token-wins cascade).
    /// <list type="bullet">
    /// <item>The <c>skew-x-*</c> / <c>skew-y-*</c> / <c>skew-*</c> utility parser (<see cref="StyleSkewClass"/>):
    /// the numeric scale is degrees (<c>skew-x-6</c>), a leading dash negates (<c>-skew-x-6</c>), the arbitrary
    /// form requires the unit (<c>skew-x-[8.5deg]</c>), the cascade is last-wins per axis among the classes of the
    /// highest importance, <c>skew-x-0</c> is a recognized reset, and <c>skew-6</c> writes both axes.</item>
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

        // --- skew-<n>: both axes ---

        // A class list as the reconciler hands it to the extractor: Normalize has put the base classes in Tailwind's
        // order, which is what makes the last class of an axis the one Tailwind emits last.
        private static SkewSpec Resolved(string classes)
        {
            StyleSkewClass.TryExtract(StyleSpacingPrecedence.Normalize(classes.Split(' ')), out var spec);
            return spec;
        }

        // The same, with payload appended after the base classes as the variant gate appends the class a variant adds.
        private static SkewSpec ResolvedWithVariant(string classes, string payload)
        {
            var composed = new System.Collections.Generic.List<string>(StyleSpacingPrecedence.Normalize(classes.Split(' ')))
            {
                payload,
            };
            StyleSkewClass.TryExtract(composed.ToArray(), out var spec);
            return spec;
        }

        [TestCase("skew-6", 6f)]
        [TestCase("-skew-6", -6f)]
        [TestCase("skew-[10deg]", 10f)]
        [TestCase("skew-6 -skew-6", 6f)]
        [TestCase("-skew-6 skew-6", 6f)]
        [TestCase("!skew-6 skew-x-12", 6f)]
        [TestCase("skew-6! skew-x-12", 6f)]
        [TestCase("!skew-6 !skew-x-12", 12f)]
        [TestCase("!skew-x-12 skew-x-6!", 6f)]
        [TestCase("skew-x-6! !skew-x-12", 6f)]
        [TestCase("-skew-x-6! skew-x-12", -6f)]
        [TestCase("!-skew-x-6 skew-x-12", -6f)]
        [TestCase("!skew-x-6 skew-x-[8]", 6f)]
        [TestCase("skew-x-[8] !skew-x-6", 6f)]
        public void Given_AClassListOfSkews_When_Resolved_Then_TheXAngleIsThatOfTheClassTailwindEmitsLast(
            string classes, float expected)
        {
            // Act
            var spec = Resolved(classes);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base reads no skew-<n>, so the per-axis class holds x there as well.
        // Swap the two lanes of StyleFamilyOrder.SkewLane and every row reddens; the candidate name alone, without
        // the lane, reddens the negative one.
        [TestCase("skew-x-12 skew-6", 12f)]
        [TestCase("skew-6 skew-x-12", 12f)]
        [TestCase("-skew-x-12 skew-6", -12f)]
        public void Given_APerAxisSkewBesideAPlainOne_When_Resolved_Then_ThePerAxisClassHoldsX(
            string classes, float expected)
        {
            // Act
            var spec = Resolved(classes);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(expected));
        }

        [TestCase("skew-6", 6f)]
        [TestCase("-skew-6", -6f)]
        [TestCase("skew-x-12 skew-6", 6f)]
        [TestCase("!skew-6 skew-y-3", 6f)]
        [TestCase("!skew-6 !skew-y-3", 3f)]
        public void Given_AClassListOfSkews_When_Resolved_Then_TheYAngleIsThatOfTheClassTailwindEmitsLast(
            string classes, float expected)
        {
            // Act
            var spec = Resolved(classes);

            // Assert
            Assert.That(spec.YDeg, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base reads no skew-<n>, so the per-axis class holds y there as well.
        // Swap the two lanes of StyleFamilyOrder.SkewLane and every row reddens; the candidate name alone, without
        // the lane, reddens the negative one.
        [TestCase("skew-y-3 skew-6", 3f)]
        [TestCase("skew-6 skew-y-3", 3f)]
        [TestCase("-skew-y-3 skew-6", -3f)]
        public void Given_APerAxisSkewBesideAPlainOne_When_Resolved_Then_ThePerAxisClassHoldsY(
            string classes, float expected)
        {
            // Act
            var spec = Resolved(classes);

            // Assert
            Assert.That(spec.YDeg, Is.EqualTo(expected));
        }

        [Test]
        public void Given_APlainSkewBracket_When_Extracted_Then_BothAxesTakeTheAngleInItsUnit()
        {
            // Act
            var spec = Resolved("skew-[.25turn]");

            // Assert
            Assert.That(spec.YDeg, Is.EqualTo(90f).Within(1e-3f));
        }

        [TestCase("skew-x-12", "skew-6", 6f)]
        [TestCase("skew-x-12", "!skew-6", 6f)]
        [TestCase("!skew-x-12", "!skew-x-6", 6f)]
        [TestCase("!skew-x-12", "skew-x-6", 12f)]
        public void Given_ABaseSkewAndAVariantPayload_When_Resolved_Then_TheXAngleIsThatOfTheOneThatWins(
            string classes, string payload, float expected)
        {
            // Act
            var spec = ResolvedWithVariant(classes, payload);

            // Assert
            Assert.That(spec.XDeg, Is.EqualTo(expected));
        }

        [TestCase("skew-x-12", "skew-6", 6f)]
        [TestCase("skew-y-3", "skew-6", 6f)]
        public void Given_ABaseSkewAndAVariantPayload_When_Resolved_Then_TheYAngleIsThatOfTheOneThatWins(
            string classes, string payload, float expected)
        {
            // Act
            var spec = ResolvedWithVariant(classes, payload);

            // Assert
            Assert.That(spec.YDeg, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base reads no skew-<n>, and declines each of these with it.
        // Parse the bare value with `float.TryParse`, or take the bracket without its unit, and its row reddens.
        [TestCase("skew-1.5")]
        [TestCase("skew-[8]")]
        public void Given_APlainSkewThatIsNoWholeNumberOfDegrees_When_Extracted_Then_ItIsDeclined(string utility)
        {
            // Act
            var want = StyleSkewClass.TryExtract(new[] { utility }, out _);

            // Assert — Tailwind takes a bare skew value only as a whole number, and a bracket only as an angle or 0.
            Assert.That(want, Is.False);
        }

        [Test]
        public void Given_APlainSkewOfAWholeNumberOfDegrees_When_Extracted_Then_ItIsWanted()
        {
            // Act
            var want = StyleSkewClass.TryExtract(new[] { "skew-2" }, out _);

            // Assert
            Assert.That(want, Is.True);
        }

        [Test]
        public void Given_APlainSkewBesideAPerAxisSkew_When_Probed_Then_TheYWinnerIsThePlainClass()
        {
            // Arrange — the patch fast path compares the probe's winners against the spec's sources per axis.
            var classes = StyleSpacingPrecedence.Normalize(new[] { "skew-6", "skew-x-12" });

            // Act
            StyleSkewClass.TryGetWinningSkewClasses(classes, out _, out var winnerY);

            // Assert
            Assert.That(winnerY, Is.EqualTo("skew-6"));
        }

        // GREEN_ON_BASE(characterization): the base reads no skew-6, and draws nothing for the rejected bracket.
        // Let TryExtract ignore a rejected pick and this reddens, skew-6 then holding y.
        [Test]
        public void Given_APlainSkewBesideARejectedPerAxisBracket_When_Normalized_Then_NoSkewIsWanted()
        {
            // Arrange — skew-<n> and skew-x-[…] write one transform declaration, which a bare 8 invalidates.
            var normalized = StyleSpacingPrecedence.Normalize(new[] { "skew-6", "skew-x-[8]" });

            // Act
            var want = StyleSkewClass.TryExtract(normalized, out _);

            // Assert
            Assert.That(want, Is.False);
        }

        [Test]
        public void Given_APlainSkewBesideAZeroPerAxisBracket_When_Normalized_Then_ASkewIsWanted()
        {
            // Arrange
            var normalized = StyleSpacingPrecedence.Normalize(new[] { "skew-6", "skew-x-[0]" });

            // Act
            var want = StyleSkewClass.TryExtract(normalized, out _);

            // Assert
            Assert.That(want, Is.True);
        }

        [TestCase("skew-[5px]", "skew-y-3")]
        [TestCase("skew-x-6", "!skew-y-[10]")]
        public void Given_ASkewHeldByAnAngleTheBrowserRejects_When_Extracted_Then_NoSkewIsWanted(
            string rejected, string other)
        {
            // Act
            var want = StyleSkewClass.TryExtract(new[] { rejected, other }, out _);

            // Assert
            Assert.That(want, Is.False);
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
