using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the padding edge a border band is filled to (<see cref="BorderBandPainter"/>).
    /// </summary>
    internal sealed class BorderBandPainterTests
    {
        // GREEN_ON_BASE(characterization): the base takes each border's width off the corner radius across it.
        [Test]
        public void Given_ARoundedTopLeftCornerUnderBordersOfTwoWidths_When_ItsInnerRadiusIsTaken_Then_EachAxisLosesTheBorderAcrossIt()
        {
            // Arrange — a 10px corner under a 2px left border and a 3px top border.
            var outer = new CornerRadii { TopLeft = new Vector2(10f, 10f) };
            var sides = new BorderSides { Left = 2f, Top = 3f };

            // Act
            var inner = BorderBandPainter.InnerRadii(outer, sides);

            // Assert
            Assert.That(new[] { inner.TopLeft.x, inner.TopLeft.y }, Is.EqualTo(new[] { 8f, 7f }));
        }
    }
}
