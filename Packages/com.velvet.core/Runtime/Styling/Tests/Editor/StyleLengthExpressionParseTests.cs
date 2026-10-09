using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which texts <see cref="StyleLengthExpression.TryParse"/> itself declines, asked directly: the
    /// resolver asks it only of a value that opens with one of the four functions it reads, so these shapes
    /// never reach it through a class.
    /// </summary>
    internal sealed class StyleLengthExpressionParseTests
    {
        [TestCase("_calc(1px)")]
        [TestCase("(1px)")]
        [TestCase("-(1px)")]
        [TestCase("-calc(1px)")]
        [TestCase("calc(1px")]
        [TestCase("calc(1px)px")]
        [TestCase("calc(1px)_")]
        [TestCase("calc(1px)(")]
        public void Given_TextThatIsNoSingleLength_When_Parsed_Then_ItIsDeclined(string text)
        {
            // Act
            var ok = StyleLengthExpression.TryParse(text, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ASingleDimension_When_Parsed_Then_ItIsRead()
        {
            // Act
            var ok = StyleLengthExpression.TryParse("1px", out _);

            // Assert
            Assert.That(ok, Is.True);
        }
    }
}
