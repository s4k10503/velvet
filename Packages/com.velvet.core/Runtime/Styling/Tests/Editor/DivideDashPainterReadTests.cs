using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which children a dashed divider's painted colour is read off: only a child whose edge a dash is bound to.
    /// </summary>
    internal sealed class DivideDashPainterReadTests
    {
        // GREEN_ON_BASE(characterization): the base reads no painted colour for a child no divide dash is bound to.
        [Test]
        public void Given_AChildNoDashIsBoundTo_When_ItsPaintedColourIsRead_Then_NoneIsRead()
        {
            // Arrange
            var child = new VisualElement();

            // Act
            var read = DivideDashPainter.TryReadPainted(child, StyleLonghand.BorderTopColor, out _);

            // Assert
            Assert.That(read, Is.False);
        }
    }
}
