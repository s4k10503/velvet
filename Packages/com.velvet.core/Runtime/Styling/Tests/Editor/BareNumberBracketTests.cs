using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a bracket holding a bare number, or a keyword, where a length is read: Tailwind writes it
    /// through or types it, and the browser keeps what CSS takes, which for a length is a bare 0 alone.
    /// </summary>
    internal sealed class BareNumberBracketTests
    {
        [TestCase("top-[8]")]
        [TestCase("p-[4]")]
        [TestCase("rounded-[8]")]
        [TestCase("translate-x-[10]")]
        [TestCase("blur-[2]")]
        [TestCase("tracking-[2]")]
        [TestCase("border-[2]")]
        [TestCase("text-[16]")]
        [TestCase("text-[0]")]
        public void Given_ABareNumberWhereALengthIsRead_When_Parsed_Then_TheClassIsDeclined(string utility)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(utility, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ABareNumberGap_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleGapClass.TryParse("gap-[8]", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AShadowOfBareNumberOffsets_When_Extracted_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_4_8_#101820]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("border-[thin]", 1f)]
        [TestCase("border-[medium]", 3f)]
        [TestCase("border-t-[thick]", 5f)]
        public void Given_ALineWidthKeyword_When_ParsedAsABorderWidth_Then_ItIsThatManyPixels(string utility, float px)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(px));
        }

        [TestCase("text-[xx-small]", 9f)]
        [TestCase("text-[medium]", 16f)]
        [TestCase("text-[x-large]", 24f)]
        [TestCase("text-[xxx-large]", 48f)]
        public void Given_AnAbsoluteFontSizeKeyword_When_Parsed_Then_ItIsThatManyPixels(string utility, float px)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(px));
        }

        [TestCase("text-[larger]", 24f)]
        [TestCase("text-[smaller]", 20f / 1.2f)]
        public void Given_ARelativeFontSizeKeyword_When_Evaluated_Then_ItScalesTheParentsSize(string utility, float px)
        {
            // Arrange
            var basis = new RelativeLengthBasis(20f, 0f, 800f, 600f);

            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Expression?.Evaluate(basis) ?? float.NaN, Is.EqualTo(px).Within(1e-3f));
        }

        [Test]
        public void Given_ABareNumberRingWidth_When_Extracted_Then_NoRingIsDrawn()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "ring-[2]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("outline-[2]")]
        [TestCase("outline-[50%]")]
        public void Given_AnOutlineWidthTheBrowserDrops_When_Extracted_Then_TheOutlineIsMedium(string utility)
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { utility }, out var spec);

            // Assert
            Assert.That(ok ? spec.Outline.Width : float.NaN, Is.EqualTo(3f));
        }

        [Test]
        public void Given_AnOutlineWidthTheBrowserDropsBesideOutline_When_Extracted_Then_OutlinesWidthHolds()
        {
            // Act
            var ok = StyleRingClass.TryExtract(new[] { "outline", "outline-[2]" }, out var spec);

            // Assert
            Assert.That(ok ? spec.Outline.Width : float.NaN, Is.EqualTo(1f));
        }

        [TestCase("border-[2]", 0f)]
        [TestCase("border-[thick]", 5f)]
        public void Given_ABorderBracket_When_InsetParsed_Then_ItReservesTheAppliedWidth(string utility, float px)
        {
            // Act
            SilhouetteBoundsSpacer.BorderInset(new[] { utility }, out var left, out _);

            // Assert
            Assert.That(left, Is.EqualTo(px));
        }
    }
}
