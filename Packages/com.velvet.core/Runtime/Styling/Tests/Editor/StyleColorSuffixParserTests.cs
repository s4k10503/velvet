using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which colour suffixes of a utility are refused: the ones whose base names no colour and the
    /// ones whose opacity modifier does not parse.
    /// </summary>
    internal sealed class StyleColorSuffixParserTests
    {
        [TestCase("nonsense/50")]
        [TestCase("blue-500/abc")]
        public void Given_ASuffixWithAnUnknownBaseOrABadModifier_When_ItIsParsed_Then_ItIsRefused(string suffix)
        {
            // Arrange / Act
            var parsed = StyleColorValueParser.TryParseColorSuffix(suffix, out _);

            // Assert
            Assert.That(parsed, Is.False);
        }
    }
}
