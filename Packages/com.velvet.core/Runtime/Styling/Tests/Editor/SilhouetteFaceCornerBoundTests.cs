using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the corner bound the Velvet-painted face takes on an inset outline — the path a border stroke
    /// follows, inset by half the border width — when the declared radius exceeds the inset box.
    /// </summary>
    /// <remarks>
    /// At inset 0 the outline's box is the element's, so only a positive inset separates a bound taken from
    /// the inset box from one taken from the element's box. Each case makes a different side the shorter one,
    /// since the bound follows whichever side that is.
    /// </remarks>
    internal sealed class SilhouetteFaceCornerBoundTests
    {
        private const float Inset = 4f;

        private static float TopLeftCornerRadius(float width, float height)
        {
            var points = new List<Vector2>();
            SilhouetteFace.BuildShearedRoundedRectPolyline(points, new ShearedRoundedRect
            {
                Width = width,
                Height = height,
                Inset = Inset,
                RadiusTopLeft = 9999f,
                RadiusTopRight = 9999f,
                RadiusBottomRight = 9999f,
                RadiusBottomLeft = 9999f,
            });

            // The outline opens where the top-left corner's arc meets the top edge, one radius in from the
            // inset box's left side.
            return points[0].x - Inset;
        }

        // GREEN_ON_BASE(characterization): the base already bounds the outline by the inset box.
        // The extraction of that bound must keep it; `x1 - x0` -> `x1 + x0` reddens this case alone.
        [Test]
        public void Given_ATallInsetOutlineWithAnOversizedRadius_When_Built_Then_ItsCornersTakeHalfTheInsetBoxsWidth()
        {
            // Arrange — a 40 x 70 box inset by 4 leaves a 32 x 62 box, whose shorter side is its width.
            const float width = 40f;
            const float height = 70f;

            // Act
            var radius = TopLeftCornerRadius(width, height);

            // Assert
            Assert.That(radius, Is.EqualTo(16f).Within(1e-4f));
        }

        // GREEN_ON_BASE(characterization): the base already bounds the outline by the inset box.
        // The extraction of that bound must keep it; `y1 - y0` -> `y1 + y0` reddens this case alone.
        [Test]
        public void Given_AWideInsetOutlineWithAnOversizedRadius_When_Built_Then_ItsCornersTakeHalfTheInsetBoxsHeight()
        {
            // Arrange — a 70 x 40 box inset by 4 leaves a 62 x 32 box, whose shorter side is its height.
            const float width = 70f;
            const float height = 40f;

            // Act
            var radius = TopLeftCornerRadius(width, height);

            // Assert
            Assert.That(radius, Is.EqualTo(16f).Within(1e-4f));
        }
    }
}
