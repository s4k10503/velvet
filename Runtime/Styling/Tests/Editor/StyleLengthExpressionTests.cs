using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="StyleLengthExpression.Evaluate"/> measures a bracketed length to against an em of
    /// 10px, a percentage basis of 200px and an 800x600 viewport.
    /// </summary>
    internal sealed class StyleLengthExpressionTests
    {
        private static float Evaluate(string text)
        {
            StyleLengthExpression.TryParse(text, out var expression);
            return expression?.Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f)) ?? float.NaN;
        }

        [TestCase("50vw", 400f)]
        [TestCase("50vh", 300f)]
        [TestCase("50vmin", 300f)]
        [TestCase("50vmax", 400f)]
        [TestCase("50svw", 400f)]
        [TestCase("50lvh", 300f)]
        [TestCase("50dvmin", 300f)]
        [TestCase("50svmax", 400f)]
        public void Given_AViewportLength_When_Evaluated_Then_ItIsTakenOfThePanel(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("2em", 20f)]
        [TestCase("calc(50%+1em)", 110f)]
        [TestCase("calc(50%-1rem)", 84f)]
        [TestCase("calc(10vw-25%)", 30f)]
        public void Given_ALinearLength_When_Evaluated_Then_EachTermIsTakenOfItsOwnBasis(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [TestCase("min(50%,40px)", 40f)]
        [TestCase("max(50%,40px)", 100f)]
        [TestCase("clamp(50px,10%,80px)", 50f)]
        [TestCase("clamp(10px,10%,80px)", 20f)]
        [TestCase("clamp(10px,50%,80px)", 80f)]
        [TestCase("min(1em,1vw)", 8f)]
        [TestCase("calc(2*min(50%,40px))", 80f)]
        [TestCase("calc(1px-min(50%,40px))", -39f)]
        public void Given_AnExtremumOfMixedUnits_When_Evaluated_Then_ItPicksTheMeasuredExtreme(string text, float expected)
        {
            // Act
            var px = Evaluate(text);

            // Assert
            Assert.That(px, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_AnEmWidthClass_When_ItsLengthIsEvaluated_Then_ItIsTwoEms()
        {
            // Arrange
            StyleArbitraryValueResolver.TryParse("w-[2em]", out var style);

            // Act
            var px = style.Expression?.Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f)) ?? float.NaN;

            // Assert
            Assert.That(px, Is.EqualTo(20f).Within(1e-3f));
        }

        [Test]
        public void Given_ANegatedExtremum_When_Evaluated_Then_ItIsTheExtremesNegation()
        {
            // Arrange
            StyleLengthExpression.TryParse("min(50%,40px)", out var expression);

            // Act
            var px = expression!.Negated().Evaluate(new RelativeLengthBasis(10f, 200f, 800f, 600f));

            // Assert
            Assert.That(px, Is.EqualTo(-40f).Within(1e-3f));
        }
    }
}
