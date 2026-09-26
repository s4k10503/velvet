using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="CornerRadiusFit.ScaleFactor"/> against the CSS rule it implements: every radius is scaled
    /// by the smallest ratio of a side to the sum of the two radii on it.
    /// </summary>
    /// <remarks>
    /// Each case overlaps one side alone, so each is the only case whose factor that side's sum decides.
    /// </remarks>
    internal sealed class CornerRadiusScaleFactorTests
    {
        private const float Width = 200f;
        private const float Height = 100f;

        private static CornerRadii Radii(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft)
            => new() { TopLeft = topLeft, TopRight = topRight, BottomRight = bottomRight, BottomLeft = bottomLeft };

        [Test]
        public void Given_RadiiOverlappingOnlyOnTheTopEdge_When_Scaled_Then_TheFactorIsTheTopEdgesRatio()
        {
            // Arrange — 150 + 100 horizontally across a 200px top edge; every vertical sum fits in 100.
            var radii = Radii(new Vector2(150f, 10f), new Vector2(100f, 10f), Vector2.zero, Vector2.zero);

            // Act
            var factor = CornerRadiusFit.ScaleFactor(Width, Height, radii);

            // Assert
            Assert.That(factor, Is.EqualTo(200f / 250f).Within(1e-5f));
        }

        [Test]
        public void Given_RadiiOverlappingOnlyOnTheBottomEdge_When_Scaled_Then_TheFactorIsTheBottomEdgesRatio()
        {
            // Arrange
            var radii = Radii(Vector2.zero, Vector2.zero, new Vector2(150f, 10f), new Vector2(150f, 10f));

            // Act
            var factor = CornerRadiusFit.ScaleFactor(Width, Height, radii);

            // Assert
            Assert.That(factor, Is.EqualTo(200f / 300f).Within(1e-5f));
        }

        [Test]
        public void Given_RadiiOverlappingOnlyOnTheLeftEdge_When_Scaled_Then_TheFactorIsTheLeftEdgesRatio()
        {
            // Arrange — the vertical components are the ones a left edge sums.
            var radii = Radii(new Vector2(10f, 80f), Vector2.zero, Vector2.zero, new Vector2(10f, 45f));

            // Act
            var factor = CornerRadiusFit.ScaleFactor(Width, Height, radii);

            // Assert
            Assert.That(factor, Is.EqualTo(100f / 125f).Within(1e-5f));
        }

        [Test]
        public void Given_RadiiOverlappingOnlyOnTheRightEdge_When_Scaled_Then_TheFactorIsTheRightEdgesRatio()
        {
            // Arrange
            var radii = Radii(Vector2.zero, new Vector2(10f, 60f), new Vector2(10f, 90f), Vector2.zero);

            // Act
            var factor = CornerRadiusFit.ScaleFactor(Width, Height, radii);

            // Assert
            Assert.That(factor, Is.EqualTo(100f / 150f).Within(1e-5f));
        }
    }
}
