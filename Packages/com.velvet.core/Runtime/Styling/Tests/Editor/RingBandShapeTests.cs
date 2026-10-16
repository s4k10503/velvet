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
        public void Given_AnOutline_When_ItGrowsACorner_Then_ARoundedOneGrowsAndASquareOneStaysSquare()
        {
            var radii = RingBandShape.OutlineRadii(new Vector4(20f, 0f, 20f, 0f), 80f);
            Assert.That((radii.x, radii.y), Is.EqualTo((100f, 0f)));
        }
    }
}
