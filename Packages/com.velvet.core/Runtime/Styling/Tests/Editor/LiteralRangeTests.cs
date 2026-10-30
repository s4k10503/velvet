using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a bracket literal outside the range its CSS property takes, as Chromium reads the declaration
    /// Tailwind writes: a negative length where the property takes none is declined, a number past a float's
    /// range is the largest float of its sign, and an opacity is clamped to 0..1.
    /// </summary>
    internal sealed class LiteralRangeTests
    {
        [TestCase("w-[-5px]")]
        [TestCase("p-[-5px]")]
        [TestCase("rounded-[-2px]")]
        [TestCase("border-[-2px]")]
        [TestCase("text-[-4px]")]
        [TestCase("basis-[-4px]")]
        [TestCase("max-w-[-4px]")]
        [TestCase("w-[-2em]")]
        public void Given_ANegativeLiteralWhereThePropertyTakesNone_When_Parsed_Then_TheClassIsDeclined(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        // GREEN_ON_BASE(characterization): a margin takes a negative literal, which was read and still is.
        [Test]
        public void Given_ANegativeLiteralMargin_When_Parsed_Then_ItIsRead()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("mt-[-5px]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, -5f)));
        }

        [TestCase("w-[1e39px]")]
        [TestCase("blur-[1e39px]")]
        [TestCase("rotate-[1e39deg]")]
        [TestCase("duration-[1e39s]")]
        [TestCase("scale-[1e39]")]
        public void Given_ALiteralPastAFloatsRange_When_Parsed_Then_ItIsTheLargestFloat(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, float.MaxValue)));
        }

        [Test]
        public void Given_ANegativeLiteralPastAFloatsRange_When_ParsedAsAMargin_Then_ItIsTheMostNegativeFloat()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("mt-[-1e39px]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, -float.MaxValue)));
        }

        // GREEN_ON_BASE(characterization): a spelled-out infinity is no CSS number, was declined, and still is.
        [TestCase("w-[Infinitypx]")]
        [TestCase("w-[NaNpx]")]
        public void Given_ASpelledOutNonNumber_When_Parsed_Then_TheClassIsDeclined(string cls)
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse(cls, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ANegativeOpacity_When_Parsed_Then_ItIsClampedToZero()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("opacity-[-1]", out var s);

            // Assert
            Assert.That((ok, s.Value), Is.EqualTo((true, 0f)));
        }
    }
}
