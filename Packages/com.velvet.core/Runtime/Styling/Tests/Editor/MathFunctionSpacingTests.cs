using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a math function as Tailwind spaces it and CSS then reads it: an operator spaced on one side is
    /// spaced on both inside a function Tailwind names in lower case, and a function name in another case is read
    /// but left as written.
    /// </summary>
    internal sealed class MathFunctionSpacingTests
    {
        [TestCase("calc(1px_-2px)", -1f)]
        [TestCase("calc(1px-_2px)", -1f)]
        [TestCase("calc(1px_+2px)", 3f)]
        public void Given_AnOperatorSpacedOnOneSide_When_ParsedAsAnArbitraryTop_Then_TailwindSpacesItAsATerm(
            string length, float expected)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(expected).Within(1e-4f));
        }

        [TestCase("CALC(1px_+_2px)", 3f)]
        [TestCase("Min(20px,2rem)", 20f)]
        [TestCase("CLAMP(10px,50px,20px)", 20f)]
        [TestCase("calc(MIN(1px+2px,8px))", 3f)]
        public void Given_AMathFunctionNamedInAnotherCase_When_ParsedAsAnArbitraryTop_Then_ItIsRead(
            string length, float expected)
        {
            // Act
            StyleArbitraryValueResolver.TryParse("top-[" + length + "]", out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(expected).Within(1e-4f));
        }

        [TestCase("top-[_calc(1px)]")]
        [TestCase("top-[calc(1px)_]")]
        [TestCase("top-[_1px]")]
        [TestCase("w-[1px_]")]
        [TestCase("border-[_calc(1px)]")]
        public void Given_ASpaceAtEitherEndOfALength_When_Parsed_Then_TheBrowserTrimsIt(string utility)
        {
            // Act
            StyleArbitraryValueResolver.TryParse(utility, out var s);

            // Assert
            Assert.That(s.Value, Is.EqualTo(1f));
        }

        // GREEN_ON_BASE(characterization): the base declined a space at either end of any bracket, so this one too.
        [Test]
        public void Given_ASpaceAtTheEndOfALengthWhereTailwindInfersALength_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var declined = !StyleArbitraryValueResolver.TryParse("border-[2px_]", out _);
            var resolved = StyleArbitraryValueResolver.TryParse("border-[2px]", out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base read a function name in lower case only, so this was declined.
        [Test]
        public void Given_AnUpperCaseCalcWithAnUnspacedPlus_When_ParsedAsAnArbitraryTop_Then_TheClassIsDeclined()
        {
            // Act
            var declined = !StyleArbitraryValueResolver.TryParse("top-[CALC(1px+2px)]", out _);
            var resolved = StyleArbitraryValueResolver.TryParse("top-[calc(1px+2px)]", out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base read a function name in lower case only, so this was declined.
        [Test]
        public void Given_AnUpperCaseCalcWhereTailwindInfersALength_When_Parsed_Then_TheClassIsDeclined()
        {
            // Act
            var declined = !StyleArbitraryValueResolver.TryParse("border-[CALC(2px)]", out _);
            var resolved = StyleArbitraryValueResolver.TryParse("border-[calc(2px)]", out _);

            // Assert
            Assert.That((declined, resolved), Is.EqualTo((true, true)));
        }
    }
}
