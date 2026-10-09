using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS absolute length units (in, cm, mm, pt, pc, Q) that <c>StyleArbitraryValueResolver.TryParseValue</c>
    /// converts to pixels at their fixed ratios, one case per utility family that reads a length through it.
    /// </summary>
    internal sealed class AbsoluteLengthUnitTests
    {
        [TestCase("1in")]
        [TestCase("2.54cm")]
        [TestCase("25.4mm")]
        [TestCase("72pt")]
        [TestCase("6pc")]
        [TestCase("101.6Q")]
        public void Given_AnAbsoluteLengthEqualToOneInch_When_ParsedAsAnArbitraryTop_Then_ItIsNinetySixPixels(string length)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInchTop_When_Parsed_Then_ItIsAPixelLength()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("top-[1in]", out var s);

            // Assert
            Assert.That((ok, s.Property, s.Unit), Is.EqualTo((true, ArbitraryProperty.Top, UnityEngine.UIElements.LengthUnit.Pixel)));
        }

        // GREEN_ON_BASE(characterization): a font-metric unit other than em was never read, and still is not.
        [Test]
        public void Given_AnUnknownUnitTop_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("top-[1ex]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AnInchTranslate_When_Parsed_Then_ItIsNinetySixPixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("translate-x-[1in]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInchBlur_When_Parsed_Then_ItIsNinetySixPixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("blur-[1in]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInchGap_When_Parsed_Then_ItIsNinetySixPixels()
        {
            // Act
            StyleGapClass.TryParse("gap-[1in]", out var gap, out _);

            // Assert
            Assert.That(gap, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInchShadowOffset_When_Extracted_Then_ItIsNinetySixPixels()
        {
            // Act
            StyleShadowClass.TryExtract(new[] { "shadow-[1in_4px_8px_#101820]" }, out var spec);

            // Assert
            Assert.That(spec.OffsetX, Is.EqualTo(96f).Within(1e-3f));
        }

        // GREEN_ON_BASE(refactor): the same reading, now failing by assertion where the class is declined.
        [Test]
        public void Given_AnInchClipPathInset_When_Extracted_Then_ItIsNinetySixPixelsNotAPercentage()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(1in)]" }, out var spec);

            // Assert
            Assert.That((spec != null, spec?.InsetTop.Value, spec?.InsetTop.IsPercent),
                Is.EqualTo((true, (float?)96f, (bool?)false)));
        }

        [Test]
        public void Given_AnInchLeading_When_ParsedAndApplied_Then_ProducesTheNinetySixPixelTag()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-[1in]" });
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=96px>hi</line-height>"));
        }

        [TestCase("from-[1in]")]
        [TestCase("from-[2.54cm]")]
        [TestCase("from-[25.4mm]")]
        [TestCase("from-[72pt]")]
        [TestCase("from-[6pc]")]
        [TestCase("from-[101.6Q]")]
        public void Given_AnAbsoluteLengthFromPosition_When_Extracted_Then_ItIsNinetySixPixels(string utility)
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", utility, "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops[0].PositionPx, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInchListPosition_When_Extracted_Then_ItIsNinetySixPixels()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_1in]" }, out var spec);

            // Assert
            Assert.That(spec.Stops[1].PositionPx, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_ACircleOfAnInchRadius_When_Extracted_Then_ItsRadiusIsNinetySixPixels()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_1in,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Radial.X, Is.EqualTo(96f).Within(1e-3f));
        }

        [Test]
        public void Given_AnEllipseOfRemAndInchRadii_When_Extracted_Then_BothAreInPixels()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_2rem_1in,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.X, spec.Radial.Y), Is.EqualTo((32f, 96f)));
        }

        // GREEN_ON_BASE(characterization): a negative radius was rejected before and still is.
        [Test]
        public void Given_ACircleOfANegativeInchRadius_When_Extracted_Then_TheClassIsInert()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_-1in,#ff0000,#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }
}
