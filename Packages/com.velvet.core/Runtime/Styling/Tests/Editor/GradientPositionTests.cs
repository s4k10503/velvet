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

        // The farthest-side ellipse of the same box: its radii are the centre's distances to the farther side.
        private static string FarthestSides(string position)
        {
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[ellipse_farthest-side_at_" + position + ",#ff0000,#0000ff]" }, out var spec);
            var radii = ok ? GradientBackground.RadialRadii(spec, 100f, 100f) : default;
            return ok ? $"{radii.x:F2} {radii.y:F2}" : "declined";
        }

        // GREEN_ON_BASE(characterization): the base reads a horizontal edge keyword written first as the x.
        [Test]
        public void Given_AHorizontalEdgeKeywordThenALength_When_Extracted_Then_TheKeywordIsTheX()
        {
            // Act
            var sides = NearestSides("left_20px");

            // Assert — the x radius is the 1e-4 floor of a centre on the left edge.
            Assert.That(sides, Is.EqualTo("0.00 20.00"));
        }

        // GREEN_ON_BASE(characterization): the base places an offset from the far edges inside the box.
        [Test]
        public void Given_OffsetsFromTheFarEdges_When_Extracted_Then_TheCentreIsInsideTheBox()
        {
            // Act — a centre 10px inside the right edge is 90px from the left; one 10px outside it would be 110px.
            var sides = FarthestSides("right_10px_bottom_20px");

            // Assert
            Assert.That(sides, Is.EqualTo("90.00 80.00"));
        }

        // GREEN_ON_BASE(characterization): the base declines a four-value position whose first edge is center.
        [Test]
        public void Given_AFourValuePositionOpeningOnCenter_When_Extracted_Then_TheGradientIsDeclined()
        {
            // Act — CSS's four-value <position> takes an edge keyword before each offset, and center is none.
            var sides = NearestSides("center_10px_left_5px");

            // Assert
            Assert.That(sides, Is.EqualTo("declined"));
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
