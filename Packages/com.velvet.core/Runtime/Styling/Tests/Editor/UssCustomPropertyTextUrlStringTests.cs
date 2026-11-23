using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a token after a <c>url()</c> holding a string is still read: the string's closing quote must not carry
    /// the search for the function's <c>)</c> past it.
    /// </summary>
    internal sealed class UssCustomPropertyTextUrlStringTests
    {
        // GREEN_ON_BASE(characterization): the base already quotes a value whose unkept unit follows a url() holding a string.
        [Test]
        public void Given_AUrlHoldingAStringFollowedByAnUnkeptUnit_When_TheSheetIsRewritten_Then_ItsValueIsQuoted()
        {
            // Arrange
            const string uss = ":root { --x: url(\"a\") 0.25rem; }";

            // Act
            var rewritten = UssCustomPropertyTextTests.RewriteWithin(uss);

            // Assert
            Assert.That(rewritten, Is.EqualTo(":root { --x: \"url(\\\"a\\\") 0.25rem\"; }"));
        }
    }
}
