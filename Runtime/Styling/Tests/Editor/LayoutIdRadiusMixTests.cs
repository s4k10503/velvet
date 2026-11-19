using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins how a layoutId projection mixes two corner radii (<see cref="LayoutIdLook.Mix"/>) where one of them is
    /// none.
    /// </summary>
    internal sealed class LayoutIdRadiusMixTests
    {
        private static Length[] Corners(float value, LengthUnit unit)
            => new[] { new Length(value, unit), new Length(value, unit), new Length(value, unit), new Length(value, unit) };

        // GREEN_ON_BASE(characterization): the base mixes a percentage radius toward none in percent.
        [Test]
        public void Given_APercentageRadiusMixedTowardNone_When_HalfWay_Then_TheMixIsInPercent()
        {
            // Arrange — a radius of none is written in pixels, and takes the other radius's unit.
            var from = Corners(10f, LengthUnit.Percent);
            var to = Corners(0f, LengthUnit.Pixel);

            // Act
            var mixed = LayoutIdLook.Mix(from, to, 0.5f, new Length[4]);

            // Assert
            Assert.That(mixed[0].unit, Is.EqualTo(LengthUnit.Percent));
        }
    }
}
