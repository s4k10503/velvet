using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the values a Motion play draws the paint layers at: shadow per layer, skew per axis, gradient per stop,
    /// with a pair that has no continuous path stepping at one half.
    /// </summary>
    [TestFixture]
    internal sealed class MotionPaintInterpolationTests
    {
        private static readonly ShadowSpec Large = new(new Color(0f, 0f, 0f, 0.4f), 10f, 4f, 2f);

        private static GradientSpec TwoStops(Color first, Color second) => new()
        {
            Type = GradientType.Linear,
            Stops = new[] { new GradientStop(first, 0f), new GradientStop(second, 1f) },
        };

        [Test]
        public void Given_ALayerBothSidesHave_When_HalfwayThere_Then_ItsBlurIsTheMean()
        {
            // Arrange
            var from = new[] { new ShadowSpec(Color.black, 2f, 1f, 0f) };

            // Act
            var layers = MotionPaintInterpolation.Shadow(from, new[] { Large }, 0.5f);

            // Assert
            Assert.That(layers[0].Blur, Is.EqualTo(6f).Within(1e-4f));
        }

        [Test]
        public void Given_ALayerOnlyTheEndHas_When_HalfwayThere_Then_ItsAlphaIsHalfTheEndsFromTransparent()
        {
            // Arrange
            var none = System.Array.Empty<ShadowSpec>();

            // Act
            var layers = MotionPaintInterpolation.Shadow(none, new[] { Large }, 0.5f);

            // Assert
            Assert.That(layers[0].Color.a, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Given_ALayerOnlyTheEndHas_When_HalfwayThere_Then_ItsOffsetIsHalfTheEndsFromZero()
        {
            // Arrange
            var none = System.Array.Empty<ShadowSpec>();

            // Act
            var layers = MotionPaintInterpolation.Shadow(none, new[] { Large }, 0.5f);

            // Assert
            Assert.That(layers[0].OffsetY, Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void Given_AnInsetLayerAgainstAnOuterOne_When_JustBeforeHalf_Then_TheOuterOneShows()
        {
            // Arrange
            var inset = new ShadowSpec(Color.black, 4f, 2f, 0f, 0f, inset: true);

            // Act
            var layers = MotionPaintInterpolation.Shadow(new[] { Large }, new[] { inset }, 0.49f);

            // Assert
            Assert.That(layers[0].Inset, Is.False);
        }

        [Test]
        public void Given_ASkewOnOneAxisOnly_When_HalfwayThere_Then_EachAxisIsItsOwnMean()
        {
            // Arrange
            var from = new SkewSpec(6f, 0f, null, null);
            var to = new SkewSpec(0f, 12f, null, null);

            // Act
            var skew = MotionPaintInterpolation.Skew(from, to, 0.5f);

            // Assert
            Assert.That((skew.XDeg, skew.YDeg), Is.EqualTo((3f, 6f)));
        }

        [Test]
        public void Given_TwoGradientsOfOneShape_When_HalfwayThere_Then_EachStopIsItsOwnMean()
        {
            // Arrange
            var from = TwoStops(Color.black, Color.black);
            var to = TwoStops(Color.white, Color.white);

            // Act
            var stops = MotionPaintInterpolation.Gradient(from, to, 0.5f).Stops;

            // Assert
            Assert.That(stops[1].Color.r, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_GradientsOfDifferentStopCounts_When_JustBeforeHalf_Then_TheStartShows()
        {
            // Arrange
            var from = TwoStops(Color.black, Color.white);
            var to = new GradientSpec
            {
                Type = GradientType.Linear,
                Stops = new[] { new GradientStop(Color.red, 0f), new GradientStop(Color.green, 0.5f), new GradientStop(Color.blue, 1f) },
            };

            // Act
            var stops = MotionPaintInterpolation.Gradient(from, to, 0.49f).Stops;

            // Assert
            Assert.That(stops.Length, Is.EqualTo(2));
        }

        [Test]
        public void Given_GradientsOfDifferentStopCounts_When_AtHalf_Then_TheEndShows()
        {
            // Arrange
            var from = TwoStops(Color.black, Color.white);
            var to = new GradientSpec
            {
                Type = GradientType.Linear,
                Stops = new[] { new GradientStop(Color.red, 0f), new GradientStop(Color.green, 0.5f), new GradientStop(Color.blue, 1f) },
            };

            // Act
            var stops = MotionPaintInterpolation.Gradient(from, to, 0.5f).Stops;

            // Assert
            Assert.That(stops.Length, Is.EqualTo(3));
        }
    }
}
