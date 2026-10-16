using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// The corner radii a ring's spread and an outline's growth give a band (<see cref="RingBandShape"/>), against
    /// CSS: a box-shadow's corner follows CSS Backgrounds 3's spread formula, so a square corner stays square, and an
    /// outline's corner grows with it where the border edge is rounded and stays square where it is not. Read off
    /// Chromium's paint of `box-shadow: 0 0 0 80px` and `outline: 80px solid` on a box rounded 20px, whose outer
    /// corners measured near 70 and 100 at the screenshot's resolution, where a plain r + s gives 100 for both.
    /// </summary>
    [TestFixture]
    internal sealed class RingBandShapeTests
    {
        [Test]
        public void Given_ASquareCorner_When_ARingSpreadsIt_Then_ItStaysSquare()
        {
            Assert.That(RingBandShape.SpreadRadius(0f, 8f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_ACornerAtLeastTheSpread_When_ARingSpreadsIt_Then_ItGrowsByTheSpread()
        {
            Assert.That(RingBandShape.SpreadRadius(8f, 2f), Is.EqualTo(10f));
        }

        [Test]
        public void Given_ACornerUnderTheSpread_When_ARingSpreadsIt_Then_ItFollowsTheCubic()
        {
            // 20 + 80 * (1 + (20 / 80 - 1)^3) = 66.25.
            Assert.That(RingBandShape.SpreadRadius(20f, 80f), Is.EqualTo(66.25f).Within(1e-3f));
        }

        [Test]
        public void Given_AnInsetSpread_When_ItShrinksACorner_Then_ItStopsAtZero()
        {
            Assert.That((RingBandShape.SpreadRadius(30f, -20f), RingBandShape.SpreadRadius(10f, -20f)), Is.EqualTo((10f, 0f)));
        }

        [Test]
        public void Given_UnequalBorders_When_ThePaddingCornerIsTaken_Then_EachAxisLosesItsOwnBorder()
        {
            // CSS Backgrounds 3: the inner edge's horizontal radius is the outer less the side border, the vertical
            // the outer less the top border, so a 20px corner over a 10px left and 4px top border is 10 by 16.
            var radii = RingBandShape.PaddingRadii(new Vector4(20f, 20f, 20f, 20f), left: 10f, top: 4f, right: 0f, bottom: 0f);
            Assert.That((radii.TopLeft, radii.TopRight), Is.EqualTo((new Vector2(10f, 16f), new Vector2(20f, 16f))));
        }

        [Test]
        public void Given_AnEllipticalCorner_When_AnInsetSpreadShrinksIt_Then_EachAxisStopsAtZero()
        {
            var radii = RingBandShape.Shrink(new CornerRadii { TopLeft = new Vector2(10f, 16f) }, 12f);
            Assert.That(radii.TopLeft, Is.EqualTo(new Vector2(0f, 4f)));
        }

        [Test]
        public void Given_AnEllipticalCorner_When_ItsDistanceIsTaken_Then_ItsEdgeIsZero()
        {
            // A point on the bottom-right corner's 10 by 16 ellipse, in a 40 by 40 box centred on 0.
            var radii = new CornerRadii { BottomRight = new Vector2(10f, 16f) };
            var centre = new Vector2(20f - 10f, 20f - 16f);
            var onEdge = centre + new Vector2(10f * Mathf.Cos(0.7f), 16f * Mathf.Sin(0.7f));
            Assert.That(InsetShadowBaker.RoundedBoxDistance(onEdge, new Vector2(20f, 20f), radii), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Given_AnOutline_When_ItGrowsACorner_Then_ARoundedOneGrowsAndASquareOneStaysSquare()
        {
            var radii = RingBandShape.OutlineRadii(new Vector4(20f, 0f, 20f, 0f), 80f);
            Assert.That((radii.x, radii.y), Is.EqualTo((100f, 0f)));
        }
    }
}
