using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how <c>StyleColorValueParser.RejectsAsColour</c> reads a value holding no characters.
    /// </summary>
    internal sealed class StyleColorValueParserEmptyValueTests
    {
        // GREEN_ON_BASE(characterization): the base already leaves an empty value undecided rather than rejecting it.
        [Test]
        public void Given_AnEmptyValue_When_ReadAsAColour_Then_ItIsNotRejected()
        {
            // Act
            var rejected = StyleColorValueParser.RejectsAsColour(string.Empty);

            // Assert
            Assert.That(rejected, Is.False);
        }
    }
}
