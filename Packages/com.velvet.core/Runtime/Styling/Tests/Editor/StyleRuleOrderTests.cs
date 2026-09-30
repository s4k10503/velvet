using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies StyleRuleOrder.NaturalCompare, the port of Tailwind's utils/compare.ts: digit runs compare as
    /// numbers, and a candidate that is a prefix of another comes first.
    /// </summary>
    [TestFixture]
    internal sealed class StyleRuleOrderTests
    {
        [Test]
        public void Given_TwoCandidatesDifferingInANumber_When_Compared_Then_TheNumbersCompareAsNumbers()
        {
            // Act
            var order = StyleRuleOrder.NaturalCompare("w-[9px]", "w-[10px]");

            // Assert
            Assert.That(order < 0, Is.True);
        }

        [Test]
        public void Given_ACandidateAndItsExtension_When_Compared_Then_TheShorterComesFirst()
        {
            // Act
            var order = StyleRuleOrder.NaturalCompare("bg-red-500", "bg-red-500/70");

            // Assert
            Assert.That(order < 0, Is.True);
        }
    }
}
