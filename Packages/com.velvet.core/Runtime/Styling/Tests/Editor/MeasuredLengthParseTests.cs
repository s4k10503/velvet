using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the families that keep a length the element measures, or a percentage added to a length or under
    /// a function of one, in a spec of their own rather than an inline length — a shadow, a clip-path and a
    /// gradient — read where no element is at hand.
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

        // GREEN_ON_BASE(characterization): the base read no shadow length in ems, so this one was declined with the rest.
        // Flooring a measured blur at zero for a literal as well reddens it.
        [Test]
        public void Given_AShadowBlurOfANegativeLengthInEms_When_Extracted_Then_TheShadowIsDeclined()
        {
            // Act
            var ok = StyleShadowClass.TryExtract(new[] { "shadow-[0_0_-1em_#000000]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
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

        [Test]
        public void Given_AClipPathInsetOfTheLargerOfAPercentageAndPixels_When_Bounded_Then_ItIsTheLarger()
        {
            // Arrange
            StyleClipPathClass.TryExtract(new[] { "clip-path-[inset(max(10%,30px))]" }, out var spec);

            // Act — 30px from each edge of the 100px box, a tenth of it being less.
            var bounded = spec != null && ClipPathVectorImageBaker.TryComputeBounds(spec, 100f, 100f, out var bounds)
                ? $"{bounds.x:F1} {bounds.width:F1}"
                : "declined";

            // Assert
            Assert.That(bounded, Is.EqualTo("30.0 40.0"));
        }

        [Test]
        public void Given_AGradientStopUnderAFunctionOfAPercentage_When_Placed_Then_ItIsReadAgainstTheLine()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right,#000000_0px,#ffffff_calc(max(50%,30px)_-_10px)]" }, out var spec);

            // Act — a horizontal line 200px long: 100px less 10px.
            var placed = spec.Stops != null ? GradientBackground.ResolveStops(spec, 200f, 100f).Positions[1] : float.NaN;

            // Assert
            Assert.That(placed, Is.EqualTo(0.45f).Within(1e-4f));
        }

        [Test]
        public void Given_AColourHintUnderAFunctionOfAPercentage_When_Placed_Then_ItIsReadAgainstTheLine()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,min(25%,100px),#ffffff]" }, out var spec);

            // Act — a horizontal line 800px long, a quarter of which is more than 100px.
            var hint = spec.Stops != null ? GradientBackground.ResolveStops(spec, 800f, 100f).Hints[0] : float.NaN;

            // Assert
            Assert.That(hint, Is.EqualTo(0.125f).Within(1e-4f));
        }

        [TestCase("max(10%,30px)_50%", "30.00 50.00")]
        [TestCase("calc(10%_+_30px)_50%", "40.00 50.00")]
        public void Given_AnEllipseRadiusOtherThanAPercentageOrPixels_When_Sized_Then_ItIsReadAgainstItsSide(
            string size, string expected)
        {
            // Arrange
            var ok = StyleGradientClass.TryExtract(new[] { "bg-radial-[" + size + ",#ff0000,#0000ff]" }, out var spec);

            // Act
            var radii = ok ? GradientBackground.RadialRadii(spec, 100f, 100f) : default;

            // Assert
            Assert.That(ok ? $"{radii.x:F2} {radii.y:F2}" : "declined", Is.EqualTo(expected));
        }

        [Test]
        public void Given_ACircleRadiusAMathFunctionTakesBelowZero_When_Extracted_Then_ItIsClampedRatherThanDeclined()
        {
            // Arrange
            var ok = StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_calc(0px_-_10px),#ff0000,#0000ff]" }, out var spec);

            // Act
            var radius = ok ? GradientBackground.RadialRadii(spec, 100f, 100f).x : float.NaN;

            // Assert
            Assert.That(radius, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void Given_ACentreOffsetFromTheRightUnderAFunctionOfAPercentage_When_Placed_Then_ItIsThatFarFromTheRight()
        {
            // Arrange
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_10px_at_right_max(10%,30px)_top_50%,#ff0000,#0000ff]" }, out var spec);

            // Act — 30px in from the right of a 100px box.
            var x = ok ? spec.CentreIn(100f, 100f).x : float.NaN;

            // Assert
            Assert.That(x, Is.EqualTo(0.7f).Within(1e-4f));
        }
    }
}
