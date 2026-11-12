using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies classes that open as a box-shadow family and that the box-shadow read takes no part from. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowReadTests
    {
        // GREEN_ON_BASE(characterization): the base declares no box-shadow for a bare inset-shadow, the root with no size.
        [Test]
        public void Given_ABareInsetShadow_When_TheBoxShadowIsRead_Then_NoBoxShadowIsDeclared()
        {
            // Act
            var reading = StyleRingClass.ReadBoxShadow(new[] { "inset-shadow" });

            // Assert
            Assert.That(reading.Declared, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base declares no box-shadow for a shadow bracket holding nothing at all.
        [Test]
        public void Given_AnEmptyShadowBracket_When_TheBoxShadowIsRead_Then_NoBoxShadowIsDeclared()
        {
            // Act
            var reading = StyleRingClass.ReadBoxShadow(new[] { "shadow-[]" });

            // Assert
            Assert.That(reading.Declared, Is.False);
        }
    }
}
