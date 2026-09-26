using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the corner radii the Velvet-painted outline takes — the path a border stroke follows, inset by half
    /// the border width — when the declared radii do not fit the box.
    /// </summary>
    /// <remarks>
    /// The radii are fitted to the element's box and the inset is then taken off each, so only a positive inset
    /// shows that it is taken off after the fit. The oversized cases each make a different side the shorter
    /// one, since the fit follows whichever side that is.
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

        // GREEN_ON_BASE(characterization): the base already paints a uniform oversized radius this way.
        // Fitting it to a square of the height, `ScaleFactor(w, h, …)` -> `ScaleFactor(h, h, …)`, reddens this
        // case alone.
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

        // GREEN_ON_BASE(characterization): the base already paints a uniform oversized radius this way.
        // Fitting it to a square of the width, `ScaleFactor(w, h, …)` -> `ScaleFactor(w, w, …)`, reddens this
        // case alone.
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

        [Test]
        public void Given_AnInsetOutlineWithAnOversizedRadius_When_Built_Then_EveryCornerTakesTheInsetOffItsFittedRadius()
        {
            // Arrange — a 40 x 70 box fits a uniform oversized radius to 20, and the inset of 4 leaves 16.
            var points = new List<Vector2>();

            // Act
            SilhouetteFace.BuildShearedRoundedRectPolyline(points, new ShearedRoundedRect
            {
                Width = 40f,
                Height = 70f,
                Inset = Inset,
                RadiusTopLeft = 9999f,
                RadiusTopRight = 9999f,
                RadiusBottomRight = 9999f,
                RadiusBottomLeft = 9999f,
            }, Samples);

            // Assert — each straight run ends one radius short of the corner it leads into.
            var radii = new[]
            {
                points[0].x - Inset,
                40f - Inset - points[1].x,
                70f - Inset - points[2 + Samples].y,
                points[3 + (2 * Samples)].x - Inset,
            };
            Assert.That(radii, Is.EqualTo(new[] { 16f, 16f, 16f, 16f }).Within(1e-4f));
        }

        // Chords per corner. The outline is a move, then per corner a straight run and this many chords.
        private const int Samples = 8;

        [Test]
        public void Given_MixedRadiiOverlappingOnTheTopEdge_When_Built_Then_TheSmallerCornerIsScaledWithTheLarger()
        {
            // Arrange — 10 + 100 across a 100px top edge scales every radius by 100/110. Bounding each corner by
            // half the box on its own instead leaves the 10px corner untouched.
            var points = new List<Vector2>();

            // Act
            SilhouetteFace.BuildShearedRoundedRectPolyline(points, new ShearedRoundedRect
            {
                Width = 100f,
                Height = 100f,
                RadiusTopLeft = 10f,
                RadiusTopRight = 100f,
            });

            // Assert — the outline opens one top-left radius in from the left edge.
            Assert.That(points[0].x, Is.EqualTo(10f * 100f / 110f).Within(1e-4f));
        }
    }
}
