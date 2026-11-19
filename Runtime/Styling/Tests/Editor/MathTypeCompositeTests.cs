using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a math function in a family Tailwind composes through one declaration, typed as the browser types
    /// it there, and a number-or-percentage utility (scale, opacity, a filter amount) reading a percentage literal
    /// or a math function coming to one — each against Chromium.
    /// </summary>
    internal sealed class MathTypeCompositeTests
    {
        [TestCase("translate-y-[calc(45deg)]")]
        [TestCase("translate-x-[calc(1s)]")]
        [TestCase("scale-y-[calc(1deg)]")]
        [TestCase("brightness-[calc(1px)]")]
        [TestCase("blur-[calc(50%)]")]
        [TestCase("hue-rotate-[calc(10px)]")]
        public void Given_AMathFunctionOfTheWrongTypeInAFamily_When_Read_Then_TheClassIsRejected(string cls)
        {
            // Act
            var rejected = StyleCompositeRejection.TryReadRejected(cls, out _);

            // Assert
            Assert.That(rejected, Is.True);
        }

        [TestCase("skew-y-[calc(10px)]")]
        [TestCase("skew-x-[calc(2)]")]
        public void Given_AMathFunctionOfTheWrongTypeInASkew_When_Read_Then_TheSkewIsRejected(string cls)
        {
            // Act
            var rejected = StyleCompositeRejection.RejectsSkew(cls, out _);

            // Assert
            Assert.That(rejected, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base took any parenthesis, so a math function of the family's own
        // type was kept, and still is. Rejecting every math function in the family reddens it.
        [TestCase("translate-x-[calc(50%+1px)]")]
        [TestCase("blur-[calc(2px*3)]")]
        [TestCase("brightness-[calc(150%)]")]
        [TestCase("scale-x-[calc(50%*3)]")]
        [TestCase("hue-rotate-[calc(10deg*2)]")]
        public void Given_AMathFunctionOfTheFamilysType_When_Read_Then_TheClassIsKept(string cls)
        {
            // Act
            var rejected = StyleCompositeRejection.TryReadRejected(cls, out _);

            // Assert
            Assert.That(rejected, Is.False);
        }

        [TestCase("scale-[150%]", 1.5f)]
        [TestCase("scale-[calc(150%)]", 1.5f)]
        [TestCase("scale-x-[calc(50%*3)]", 1.5f)]
        [TestCase("opacity-[50%]", 0.5f)]
        [TestCase("opacity-[calc(25%*2)]", 0.5f)]
        [TestCase("brightness-[calc(150%)]", 1.5f)]
        [TestCase("grayscale-[calc(150%)]", 1f)]
        public void Given_APercentageOrAMathFunctionComingToOne_When_Parsed_Then_ItIsThatFraction(string cls, float expected)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That(ok ? s.Value : float.NaN, Is.EqualTo(expected));
        }
    }
}
