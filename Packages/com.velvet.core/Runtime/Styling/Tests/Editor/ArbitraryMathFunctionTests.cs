using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the bracketed <c>calc()</c> / <c>min()</c> / <c>max()</c> / <c>clamp()</c> that
    /// <c>StyleArbitraryValueResolver.TryParseValue</c> reads when the function comes to one pixel length or one
    /// percentage, across the utility families that read a length through it.
    /// </summary>
    internal sealed class ArbitraryMathFunctionTests
    {
        [TestCase("calc(1rem+4px)")]
        [TestCase("calc(1rem_+_4px)")]
        [TestCase("calc((1px+4px)*4)")]
        [TestCase("calc(40px/2)")]
        [TestCase("calc(1in-76px)")]
        [TestCase("min(20px,2rem)")]
        [TestCase("max(10px,1.25rem)")]
        [TestCase("clamp(10px,50px,20px)")]
        [TestCase("clamp(20px,5px,30px)")]
        [TestCase("clamp(10px,20px,30px)")]
        [TestCase("calc((1+3)*5px)")]
        [TestCase("calc(4*5px)")]
        [TestCase("calc(min(4,8)*5px)")]
        [TestCase("calc(max(1,0.5)*20px)")]
        [TestCase("calc(-1*-20px)")]
        [TestCase("calc(30px-(2*5px))")]
        [TestCase("clamp(20px,5px,10px)")]
        [TestCase("calc(10px+2*5px)")]
        [TestCase("calc(30px-5px-5px)")]
        [TestCase("calc(80px/2/2)")]
        [TestCase("calc(1e1px+10px)")]
        [TestCase("calc(25px_-_5px)")]
        public void Given_AMathFunctionOfPixelLengths_When_ParsedAsAnArbitraryTop_Then_ItIsTwentyPixels(string length)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_FortyHalfPixelTerms_When_ParsedAsAnArbitraryTop_Then_ItIsTwentyPixels()
        {
            // Arrange
            var terms = string.Join("+", System.Linq.Enumerable.Repeat("0.5px", 40));

            // Act
            StyleArbitraryValueResolver.TryParse("top-[calc(" + terms + ")]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_SixtyThreeNestedCalcs_When_ParsedAsAnArbitraryTop_Then_ItIsOnePixel()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + Nested(63) + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(1f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): a math function nested 64 deep was never read, and still is not.
        [Test]
        public void Given_SixtyFourNestedCalcs_When_ParsedAsAnArbitraryTop_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("top-[" + Nested(64) + "]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        private static string Nested(int depth)
            => string.Concat(System.Linq.Enumerable.Repeat("calc(", depth)) + "1px" + new string(')', depth);

        [Test]
        public void Given_AMathFunctionOfPercentages_When_ParsedAsAnArbitraryWidth_Then_ItIsAPercentage()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("w-[calc(100%/4)]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Unit), Is.EqualTo((true, 25f, LengthUnit.Percent)));
        }

        [Test]
        public void Given_ANegatedMathFunction_When_Parsed_Then_ItIsNegativePixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("-mt-[calc(1rem+4px)]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(-20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionTranslate_When_Parsed_Then_ItIsTwentyPixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("translate-x-[calc(1rem+4px)]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionBlur_When_Parsed_Then_ItIsSixPixels()
        {
            // Act
            StyleArbitraryValueResolver.TryParse("blur-[calc(2px*3)]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionGap_When_Parsed_Then_ItIsTwentyPixels()
        {
            // Act
            StyleGapClass.TryParse("gap-[calc(1rem+4px)]", out var gap, out _);

            // Assert
            Assert.That(gap, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionShadowOffset_When_Extracted_Then_ItIsTwentyPixels()
        {
            // Act
            StyleShadowClass.TryExtract(new[] { "shadow-[calc(1rem+4px)_4px_8px_#101820]" }, out var spec);

            // Assert
            Assert.That(spec.OffsetX, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionClipPathInset_When_Extracted_Then_ItIsTwentyPixelsNotAPercentage()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(calc(1rem+4px))]" }, out var spec);

            // Assert
            Assert.That((spec?.InsetTop.Value, spec?.InsetTop.IsPercent), Is.EqualTo(((float?)20f, (bool?)false)));
        }

        [Test]
        public void Given_AMathFunctionLeading_When_ParsedAndApplied_Then_ProducesTheTwentyPixelTag()
        {
            // Act
            var effect = StyleTextEffectClass.Parse(new[] { "leading-[calc(1rem+4px)]" });
            var result = StyleTextEffectClass.Apply("hi", null, null, null, effect.Leading);

            // Assert
            Assert.That(result, Is.EqualTo("<line-height=20px>hi</line-height>"));
        }

        [Test]
        public void Given_AMathFunctionFromLength_When_Extracted_Then_ItIsTwentyPixels()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[calc(1rem+4px)]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].PositionPx : float.NaN, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionFromPercentage_When_Extracted_Then_ItIsAQuarterOfTheLine()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[calc(10%+15%)]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 0 ? spec.Stops[0].Position : float.NaN, Is.EqualTo(0.25f).Within(1e-3f));
        }

        [TestCase("calc(1in-76px)")]
        [TestCase("min(1in,20px)")]
        public void Given_AMathFunctionListPosition_When_Extracted_Then_ItIsTwentyPixels(string position)
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_" + position + "]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length > 1 ? spec.Stops[1].PositionPx : float.NaN, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_AMathFunctionRadius_When_Parsed_Then_ItIsTwentyPixels()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("rounded-[calc(1rem+4px)]", out var s);

            // Assert
            Assert.That((ok, s.Value, s.Unit), Is.EqualTo((true, 20f, LengthUnit.Pixel)));
        }

        // GREEN_ON_BASE(characterization): a math function this reader declines was never read, and still is not.
        [TestCase("calc(1px+2)")]
        [TestCase("calc(1px*2px)")]
        [TestCase("calc(1px/2px)")]
        [TestCase("calc(1px/0)")]
        [TestCase("calc(4)")]
        [TestCase("calc(1px")]
        [TestCase("calc(1px))")]
        [TestCase("calc(1px)px")]
        [TestCase("min()")]
        [TestCase("min(1px,2)")]
        [TestCase("clamp(1px,2px)")]
        [TestCase("var(--gap)")]
        [TestCase("calc(1foo)")]
        [TestCase("calc(2*1foo)")]
        [TestCase("calc(1foo*2)")]
        [TestCase("calc(1px,2px)")]
        [TestCase("calc(1px+)")]
        [TestCase("min(1px,2px,)")]
        [TestCase("(1px)")]
        [TestCase("calc(1px)_")]
        [TestCase("calc(1px)#)")]
        [TestCase("calc(1px_-2px)")]
        [TestCase("calc(1px-_2px)")]
        [TestCase("calc(1px_+2px)")]
        [TestCase("-(1px)")]
        [TestCase("-calc(1px)")]
        [TestCase("_calc(1px)")]
        [TestCase("calc(1px---1px)")]
        public void Given_AMalformedMathFunction_When_ParsedAsAnArbitraryTop_Then_TheClassIsDeclined(string length)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a gap mixing a percentage with pixels was never read, nor is it now.
        [Test]
        public void Given_AMixedPercentageGap_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleGapClass.TryParse("gap-[calc(50%-1rem)]", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a translate taking the less of a percentage and pixels was never read.
        [Test]
        public void Given_AMinOfAPercentageAndPixels_When_ParsedAsATranslate_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("translate-x-[min(10%,4px)]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a stop at a bare number or a malformed function was never read.
        [TestCase("20")]
        [TestCase("calc(1foo)")]
        public void Given_AListStopAtNoLength_When_Extracted_Then_TheListIsDeclined(string position)
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_" + position + "]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AnEllipseRadiusOfAFoldedPercentage_When_Extracted_Then_ItIsHalfTheBox()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_calc(25%*2)_1in,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.X, spec.Radial.XPercent), Is.EqualTo((0.5f, true)));
        }

        [Test]
        public void Given_AnEllipseRadiusOfAFoldedLength_When_Extracted_Then_ItIsThosePixels()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_calc(1rem+4px)_1in,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.X, spec.Radial.XPercent), Is.EqualTo((20f, false)));
        }

        [TestCase("border-[50%]")]
        [TestCase("border-t-[25%]")]
        [TestCase("border-l-[10%]")]
        public void Given_APercentageBorderWidth_When_Parsed_Then_TheClassIsDeclined(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a border width folded to a percentage was never read, nor is it now.
        [Test]
        public void Given_AFoldedPercentageBorderWidth_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("border-[calc(25%*2)]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }
}
