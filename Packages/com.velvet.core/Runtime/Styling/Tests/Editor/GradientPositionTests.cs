using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a radial gradient's at_ position as CSS's &lt;position&gt; writes one, lengths and edge offsets
    /// included, read through the closest-side ellipse it gives in a 100x100 box: its radii are the centre's
    /// distances to the nearer side along each axis.
    /// </summary>
    internal sealed class GradientPositionTests
    {
        private static string NearestSides(string position)
        {
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[ellipse_closest-side_at_" + position + ",#ff0000,#0000ff]" }, out var spec);
            var radii = ok ? GradientBackground.RadialRadii(spec, 100f, 100f) : default;
            return ok ? $"{radii.x:F2} {radii.y:F2}" : "declined";
        }

        [TestCase("20px_30px", "20.00 30.00")]
        [TestCase("right_10px_bottom_20px", "10.00 20.00")]
        [TestCase("top_10px_left_5px", "5.00 10.00")]
        [TestCase("10px_top", "10.00 0.00")]
        [TestCase("calc(50%_+_30px)_40%", "20.00 40.00")]
        [TestCase("left_25%_bottom_5px", "25.00 5.00")]
        [TestCase("max(25%,30px)_50%", "30.00 50.00")]
        public void Given_APositionWithALength_When_Extracted_Then_TheCentreIsThere(string position, string expected)
        {
            // Act
            var sides = NearestSides(position);

            // Assert
            Assert.That(sides, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): each is no CSS position, was declined, and still is.
        [TestCase("top_10px")]
        [TestCase("right_10px_bottom")]
        [TestCase("left_5px_right_5px")]
        public void Given_APositionCSSRejects_When_Extracted_Then_TheGradientIsDeclined(string position)
        {
            // Act
            var sides = NearestSides(position);

            // Assert
            Assert.That(sides, Is.EqualTo("declined"));
        }
    }
}
