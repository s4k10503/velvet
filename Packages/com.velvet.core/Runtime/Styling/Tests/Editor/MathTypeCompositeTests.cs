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
        [TestCase("translate-x-4 translate-y-[calc(45deg)]", "translate-x-[0] translate-y-[0]")]
        [TestCase("scale-x-[2] scale-y-[calc(1deg)]", "scale-x-[1] scale-y-[1]")]
        [TestCase("skew-x-6 skew-y-[calc(10px)]", "skew-x-[0] skew-y-[0]")]
        [TestCase("blur-sm brightness-[calc(1px)]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0]")]
        [TestCase("blur-[calc(50%)]", "blur-[0] brightness-[1] contrast-[1] grayscale-[0] hue-rotate-[0] invert-[0] saturate-[1] sepia-[0]")]
        public void Given_AMathFunctionOfTheWrongTypeInAFamily_When_Normalized_Then_TheFamilyIsItsIdentity(
            string classes, string kept)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(kept));
        }

        // GREEN_ON_BASE(characterization): a math function of the family's own type was kept, and still is.
        [TestCase("translate-x-[calc(50%+1px)]")]
        [TestCase("blur-[calc(2px*3)]")]
        public void Given_AMathFunctionOfTheFamilysType_When_Normalized_Then_EveryTokenIsKept(string classes)
        {
            // Act
            var tokens = StyleSpacingPrecedence.Normalize(classes.Split(' '));

            // Assert
            Assert.That(string.Join(" ", tokens), Is.EqualTo(classes));
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
            Assert.That((ok, s.Value), Is.EqualTo((true, expected)));
        }
    }
}
