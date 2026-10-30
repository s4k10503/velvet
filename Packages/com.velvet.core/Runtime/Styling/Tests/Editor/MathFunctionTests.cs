using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS Values 4 math functions a bracket reads beyond calc(), min(), max() and clamp(), each
    /// against what Chromium computes for the same declaration: round(), mod(), rem(), abs(), sign(), the
    /// trigonometric and exponential functions, the constants, typed arithmetic, and the top level's rules for a
    /// NaN, an infinity and a result outside its property's range. Lengths are measured against an em of 10px,
    /// a percentage basis of 200px and an 800x600 viewport.
    /// </summary>
    internal sealed class MathFunctionTests
    {
        private static float Evaluate(string text)
        {
            StyleLengthExpression.TryParse(text, out var expression);
            return expression?.Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f)) ?? float.NaN;
        }

        [TestCase("round(10.5px,1px)", 11f)]
        [TestCase("round(-10.5px,1px)", -10f)]
        [TestCase("round(up,10.2px,1px)", 11f)]
        [TestCase("round(down,10.8px,1px)", 10f)]
        [TestCase("round(to-zero,-10.8px,1px)", -10f)]
        [TestCase("round(nearest,15px,10px)", 20f)]
        [TestCase("round(TO-ZERO,10.7px,1px)", 10f)]
        [TestCase("calc(round(2.5)*1px)", 3f)]
        [TestCase("calc(round(-2.5)*1px)", -2f)]
        [TestCase("calc(round(down,5.5)*1px)", 5f)]
        [TestCase("calc(round(pi,1)*1px)", 3f)]
        [TestCase("calc(1px*round(5,infinity))", 0f)]
        [TestCase("round(33%,10px)", 70f)]
        public void Given_ARoundedLength_When_Evaluated_Then_ItIsWhatChromiumComputes(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("mod(7px,3px)", 1f)]
        [TestCase("mod(-7px,3px)", 2f)]
        [TestCase("mod(7px,-3px)", -2f)]
        [TestCase("rem(-7px,3px)", -1f)]
        [TestCase("calc(mod(7,3)*1px)", 1f)]
        [TestCase("calc(1px*mod(5,infinity))", 5f)]
        [TestCase("calc(1px*rem(5,infinity))", 5f)]
        [TestCase("mod(-30%,7px)", 3f)]
        public void Given_ARemainder_When_Evaluated_Then_ItIsWhatChromiumComputes(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("abs(-5px)", 5f)]
        [TestCase("abs(-20%)", 40f)]
        [TestCase("calc(sign(-3px)*10px)", -10f)]
        [TestCase("calc(sign(50%)*10px)", 10f)]
        [TestCase("calc(1px*sign(-2%))", -1f)]
        [TestCase("hypot(3px,4px)", 5f)]
        [TestCase("hypot(-3px)", 3f)]
        public void Given_ASignOrMagnitude_When_Evaluated_Then_ItIsWhatChromiumComputes(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("calc(sin(30deg)*100px)", 50f)]
        [TestCase("calc(cos(0)*10px)", 10f)]
        [TestCase("calc(tan(45deg)*10px)", 10f)]
        [TestCase("calc(10px*sin(1))", 8.41471f)]
        [TestCase("calc(1e10px*sin(180deg))", 0f)]
        [TestCase("calc(1e10px*cos(90deg))", 0f)]
        [TestCase("calc(asin(1)/1deg*1px)", 90f)]
        [TestCase("calc(atan2(1px,1px)/1deg*1px)", 45f)]
        [TestCase("calc(1px*atan2(1,1)/1rad)", 0.785398f)]
        [TestCase("calc(pow(2,3)*1px)", 8f)]
        [TestCase("calc(sqrt(16)*1px)", 4f)]
        [TestCase("calc(log(e)*10px)", 10f)]
        [TestCase("calc(log(8,2)*1px)", 3f)]
        [TestCase("calc(exp(0)*5px)", 5f)]
        [TestCase("calc(pi*1px)", 3.14159f)]
        [TestCase("calc(Pi*1px)", 3.14159f)]
        [TestCase("calc(-1*pi*1px)", -3.14159f)]
        [TestCase("calc(e*1px)", 2.71828f)]
        public void Given_ATrigonometricOrExponentialTerm_When_Evaluated_Then_ItIsWhatChromiumComputes(
            string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        // A product or a quotient carries the powers of its operands' units, and the result is read wherever it
        // comes back to a length.
        [TestCase("calc(100px/2px*1px)", 50f)]
        [TestCase("calc(1px*1px/1px)", 1f)]
        [TestCase("calc(1s/1ms*1px)", 1000f)]
        [TestCase("calc(1deg/1deg*3px)", 3f)]
        [TestCase("calc(1px*1%/1%)", 1f)]
        [TestCase("calc(1px*1%/1px)", 2f)]
        [TestCase("calc(10%*10%/1px)", 400f)]
        public void Given_TypedArithmeticComingToALength_When_Evaluated_Then_ItIsWhatChromiumComputes(
            string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("calc(1px/0)")]
        [TestCase("calc(infinity*1px)")]
        [TestCase("calc(INFINITY*1px)")]
        [TestCase("calc(1e39px)")]
        [TestCase("calc(1px*tan(90deg))")]
        [TestCase("calc(1px*tan(pi/2))")]
        [TestCase("calc(1px*round(up,5,infinity))")]
        public void Given_APositivelyInfiniteResult_When_Evaluated_Then_ItIsTheLargestFloat(string text)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(float.MaxValue));
        }

        [TestCase("calc(-1px/0)")]
        [TestCase("calc(-infinity*1px)")]
        [TestCase("calc(1px*tan(-90deg))")]
        [TestCase("calc(1px*tan(270deg))")]
        [TestCase("calc(1px*round(down,-5,infinity))")]
        public void Given_ANegativelyInfiniteResult_When_Evaluated_Then_ItIsTheMostNegativeFloat(string text)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(-float.MaxValue));
        }

        [TestCase("calc(0px/0)")]
        [TestCase("calc(NaN*1px)")]
        [TestCase("calc((1px/0)-(1px/0))")]
        [TestCase("calc(acos(2)/1deg*1px)")]
        [TestCase("round(10px,0px)")]
        [TestCase("rem(7px,0px)")]
        [TestCase("calc(1px*mod(-5,infinity))")]
        [TestCase("calc(1px*sqrt(-1))")]
        public void Given_ANaNResult_When_Evaluated_Then_ItIsZero(string text)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(0f));
        }

        // An infinity inside the calculation stays one until the top level, so an extremum can still pick a
        // finite argument over it.
        [TestCase("min(1px/0,5px)", 5f)]
        [TestCase("clamp(10px,1px/0,20px)", 20f)]
        public void Given_AnInfiniteArgument_When_AnExtremumPicksAnother_Then_ItIsThatArgument(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): each was declined before these functions were read, and still is.
        // CSS's type rules reject every one.
        [TestCase("round(10.5px)")]
        [TestCase("round(nearest,5px)")]
        [TestCase("calc(100px/2px)")]
        [TestCase("calc(1px*2px)")]
        [TestCase("calc(sin(1px))")]
        [TestCase("calc(sqrt(4px))")]
        [TestCase("hypot(3px,4)")]
        [TestCase("calc(1px+sign(1px))")]
        [TestCase("calc(-pi*1px)")]
        [TestCase("calc(infinity)")]
        [TestCase("mod(10px,3)")]
        public void Given_AMathFunctionCSSRejects_When_Parsed_Then_ItIsDeclined(string text)
        {
            // Act
            var ok = StyleLengthExpression.TryParse(text, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [TestCase("w-[calc(-5px)]")]
        [TestCase("p-[calc(-5px)]")]
        [TestCase("rounded-[calc(-2px)]")]
        [TestCase("border-[calc(-2px)]")]
        [TestCase("blur-[calc(-2px)]")]
        [TestCase("brightness-[calc(-1)]")]
        [TestCase("grow-[calc(-2)]")]
        [TestCase("opacity-[calc(-1)]")]
        public void Given_AMathFunctionBelowItsPropertysRange_When_Parsed_Then_ItIsClampedToZero(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_AnOpacityMathFunctionAboveOne_When_Parsed_Then_ItIsClampedToOne()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("opacity-[calc(2)]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, 1f)));
        }

        [Test]
        public void Given_AWidthAnElementMeasuresBelowZero_When_Evaluated_Then_ItIsZero()
        {
            // Arrange
            StyleArbitraryValueResolver.TryParse("w-[calc(1em-100px)]", out var s);

            // Act
            var px = s.Expression?.Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f)) ?? float.NaN;

            // Assert
            Assert.That(px, Is.EqualTo(0f));
        }

        // GREEN_ON_BASE(characterization): a margin takes a negative length, so one was never raised, nor is now.
        [Test]
        public void Given_AMarginAnElementMeasuresBelowZero_When_Evaluated_Then_ItStaysNegative()
        {
            // Arrange
            StyleArbitraryValueResolver.TryParse("mt-[calc(1em-100px)]", out var s);

            // Act
            var px = s.Expression?.Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f)) ?? float.NaN;

            // Assert
            Assert.That(px, Is.EqualTo(-90f).Within(1e-3f));
        }

        [TestCase("rotate-[calc(45deg*2)]", ArbitraryProperty.Rotate)]
        [TestCase("hue-rotate-[calc(1turn/4)]", ArbitraryProperty.FilterHueRotate)]
        public void Given_AMathFunctionComingToAnAngle_When_Parsed_Then_ItIsNinetyDegrees(string cls, ArbitraryProperty property)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That((ok, s.Property, s.Value), Is.EqualTo((true, property, 90f)));
        }

        [Test]
        public void Given_AMathFunctionComingToANumber_When_ParsedAsAScale_Then_ItIsThatFactor()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("scale-[calc(1.5*2)]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, 3f)));
        }

        [Test]
        public void Given_AMathFunctionComingToATime_When_ParsedAsADuration_Then_ItIsHalfASecond()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("duration-[calc(1s/2)]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, 0.5f)));
        }

        [Test]
        public void Given_ASpacedMathFunctionInASkew_When_Parsed_Then_ItIsFifteenDegrees()
        {
            // Act
            var ok = StyleSkewClass.TryExtract(new[] { "skew-x-[calc(10deg_+_5deg)]" }, out var spec);

            // Assert
            Assert.That((ok, spec.XDeg), Is.EqualTo((true, 15f)));
        }

        // A bracket list splits into words on `_` outside parentheses, so a math function written with spaces
        // stays one word.

        [Test]
        public void Given_ASpacedMathFunctionInAClipPathInset_When_Extracted_Then_ItIsTwentyPixels()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(calc(1rem_+_4px))]" }, out var spec);

            // Assert
            Assert.That(spec?.InsetTop.Value, Is.EqualTo((float?)20f));
        }

        [Test]
        public void Given_ASpacedMathFunctionInAClipPathRound_When_Extracted_Then_ItsCornerIsSixPixels()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(10px_round_calc(2px_*_3))]" }, out var spec);

            // Assert
            Assert.That(spec?.CornerRadii?[0].Value, Is.EqualTo((float?)6f));
        }

        [Test]
        public void Given_SpacedMathFunctionsInAClipPathCircle_When_Extracted_Then_RadiusAndCentreAreRead()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[circle(calc(10px_+_5px)_at_calc(10%_+_15%)_50%)]" }, out var spec);

            // Assert
            Assert.That((spec?.RadiusX.Value, spec?.CenterX.Value), Is.EqualTo(((float?)15f, (float?)25f)));
        }

        [Test]
        public void Given_SpacedMathFunctionsInARadialSize_When_Extracted_Then_BothRadiiAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_calc(25%_*_2)_calc(1px_+_1px),#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.X, spec.Radial.Y), Is.EqualTo((0.5f, 2f)));
        }

        [Test]
        public void Given_ASpacedMathAngleInALinearGradient_When_Extracted_Then_ItIsNinetyDegrees()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[calc(45deg_*_2),#000000,#ffffff]" }, out var spec);

            // Assert
            Assert.That((ok, spec.AngleDeg), Is.EqualTo((true, 90f)));
        }

        [Test]
        public void Given_ASpacedMathAngleInAConicStart_When_Extracted_Then_ItIsNinetyDegrees()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-conic-[from_calc(45deg_*_2)_at_50%_50%,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((ok, spec.AngleDeg), Is.EqualTo((true, 90f)));
        }

        [Test]
        public void Given_ASpacedMathPercentageInARadialCentre_When_Extracted_Then_ItIsAQuarter()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_at_calc(10%_+_15%)_50%,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.CenterX, Is.EqualTo(0.25f).Within(1e-4f));
        }
    }
}
