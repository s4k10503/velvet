using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families that keep a length the element measures, or a percentage added to a length, in a
    /// spec of their own rather than an inline length — a shadow, a clip-path and a gradient — read where no
    /// element is at hand.
    /// </summary>
    internal sealed class MeasuredLengthParseTests
    {
        [Test]
        public void Given_AShadowOffsetInEms_When_Extracted_Then_TheShadowIsRead()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_1em_0_0_#000000]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        [Test]
        public void Given_AClipPathInsetInEms_When_Extracted_Then_TheShapeIsRead()
        {
            // Act
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(1em)]" }, out var spec);

            // Assert
            Assert.That(spec, Is.Not.Null);
        }

        [Test]
        public void Given_AClipPathInsetOfAPercentagePlusPixels_When_Bounded_Then_ItIsBoth()
        {
            // Arrange
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(calc(10%_+_5px))]" }, out var spec);

            // Act — a tenth of the 100px box and 5px from each edge.
            var bounded = spec != null && ClipPathVectorImageBaker.TryComputeBounds(spec, 100f, 100f, out var bounds)
                ? $"{bounds.x:F1} {bounds.width:F1}"
                : "declined";

            // Assert
            Assert.That(bounded, Is.EqualTo("15.0 70.0"));
        }

        [Test]
        public void Given_AGradientStopAtHalfTheLinePlusPixels_When_Placed_Then_ItIsBoth()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_calc(50%_+_10px)]" }, out var spec);

            // Act — a horizontal line 100px long.
            var placed = spec.Stops != null ? GradientBackground.ResolveStops(spec, 100f, 100f).Positions[1] : float.NaN;

            // Assert
            Assert.That(placed, Is.EqualTo(0.6f).Within(1e-4f));
        }
    }
}
