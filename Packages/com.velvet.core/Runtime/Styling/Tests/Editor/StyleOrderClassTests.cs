using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>order-*</c> utility parser (StyleOrderClass) against Tailwind v4's order utility: the
    /// keywords, a bare non-negative integer, the bracket form, a leading "-" negating either, and the cascade an
    /// important token wins. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StyleOrderClassTests
    {
        [TestCase("order-3", 3)]
        [TestCase("-order-3", -3)]
        [TestCase("order-[-2]", -2)]
        [TestCase("-order-[2]", -2)]
        [TestCase("order-first", int.MinValue)]
        [TestCase("order-last", int.MaxValue)]
        [TestCase("order-none", 0)]
        public void Given_AnOrderUtility_When_Parsed_Then_ItResolvesToTheOrderTailwindEmits(string cls, int expected)
        {
            // Arrange / Act
            var parsed = StyleOrderClass.TryParse(cls, out var order) ? order : (int?)null;

            // Assert
            Assert.That(parsed, Is.EqualTo(expected));
        }

        [TestCase("order-03")]
        [TestCase("order-1.5")]
        [TestCase("-order-first")]
        [TestCase("order-")]
        public void Given_ASpellingTailwindGeneratesNothingFor_When_Parsed_Then_ItIsDeclined(string cls)
        {
            // Arrange / Act
            var parsed = StyleOrderClass.TryParse(cls, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }

        [Test]
        public void Given_AnImportantOrderBeforeAPlainOne_When_Extracted_Then_TheImportantOneWins()
        {
            // Arrange
            var classes = new[] { "!order-1", "order-5" };

            // Act
            var found = StyleOrderClass.TryExtract(classes, out var order, out _) ? order : (int?)null;

            // Assert
            Assert.That(found, Is.EqualTo(1));
        }
    }
}
