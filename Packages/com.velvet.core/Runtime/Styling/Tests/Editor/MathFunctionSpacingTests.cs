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
            var parsed = StyleArbitraryValueResolver.TryParse("border-[2px_]", out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base read a function name in lower case only, so this was declined.
        [TestCase("top-[CALC(1px+2px)]")]
        [TestCase("border-[CALC(2px)]")]
        public void Given_AnUpperCaseCalcLeftUnspacedOrNotTakenForALength_When_Parsed_Then_TheClassIsDeclined(string utility)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(utility, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base read a lower-case function name, the control for the declining case above.
        [TestCase("top-[calc(1px+2px)]")]
        [TestCase("border-[calc(2px)]")]
        public void Given_ALowerCaseCalc_When_Parsed_Then_TheClassResolves(string utility)
        {
            // Act
            var parsed = StyleArbitraryValueResolver.TryParse(utility, out _);

            // Assert
            Assert.That(parsed, Is.True);
        }
    }
}
