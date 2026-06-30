using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the SDF gradient-silhouette bake (<c>GradientSilhouetteBaker</c> + the
    /// Velvet/GradientSilhouette shader) that fills a skewed element: a sheared, rounded, gradient-filled
    /// shape whose edge antialiasing is baked into the alpha. Orientation matches the upright gradient (the
    /// <c>from</c> stop at the box top for <c>bg-gradient-to-b</c>); the interior is opaque and the bleed
    /// margin around the shape is transparent. The bake needs a graphics device (Graphics.Blit), so those
    /// cases self-skip under -nographics; the bounding-box sizing is pure math and always runs. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientSilhouetteBakeTests
    {
        private static GradientSpec RedToBlueDown()
        {
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-b", "from-[#FF0000]", "to-[#0000FF]" }, out var spec);
            return spec;
        }

        [Test]
        public void Given_AToBottomGradient_When_BakedUnskewed_Then_TheTopIsFromRed()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — bake a 64x64 unskewed silhouette and sample the centre column near the TOP edge.
            var tex = GradientSilhouetteBaker.Bake(RedToBlueDown(), 64f, 64f, 0f, 0f, new Vector4(8f, 8f, 8f, 8f));
            Assume.That(tex, Is.Not.Null, "Precondition: the silhouette shader resolved and baked");
            var top = tex.GetPixel(tex.width / 2, tex.height - 5);
            Object.DestroyImmediate(tex);

            // Assert — the top is red (the `from` stop), matching the upright background orientation.
            Assert.That(top.r > top.b, Is.True);
        }

        [Test]
        public void Given_AToBottomGradient_When_BakedUnskewed_Then_TheBottomIsToBlue()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — sample the centre column near the BOTTOM edge.
            var tex = GradientSilhouetteBaker.Bake(RedToBlueDown(), 64f, 64f, 0f, 0f, new Vector4(8f, 8f, 8f, 8f));
            Assume.That(tex, Is.Not.Null, "Precondition: the silhouette shader resolved and baked");
            var bottom = tex.GetPixel(tex.width / 2, 5);
            Object.DestroyImmediate(tex);

            // Assert — the bottom is blue (the `to` stop).
            Assert.That(bottom.b > bottom.r, Is.True);
        }

        [Test]
        public void Given_ASilhouette_When_Baked_Then_TheInteriorIsOpaque()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — sample the centre (well inside the shape).
            var tex = GradientSilhouetteBaker.Bake(RedToBlueDown(), 64f, 64f, 0f, 0f, new Vector4(8f, 8f, 8f, 8f));
            Assume.That(tex, Is.Not.Null, "Precondition: the silhouette shader resolved and baked");
            var center = tex.GetPixel(tex.width / 2, tex.height / 2);
            Object.DestroyImmediate(tex);

            // Assert — the filled interior is fully opaque (the SDF mask is 1 inside).
            Assert.That(center.a, Is.GreaterThan(0.9f));
        }

        [Test]
        public void Given_ASilhouette_When_Baked_Then_TheBleedMarginCornerIsTransparent()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — sample the very corner of the texture: the bleed margin OUTSIDE the (unskewed,
            // rounded) shape.
            var tex = GradientSilhouetteBaker.Bake(RedToBlueDown(), 64f, 64f, 0f, 0f, new Vector4(8f, 8f, 8f, 8f));
            Assume.That(tex, Is.Not.Null, "Precondition: the silhouette shader resolved and baked");
            var corner = tex.GetPixel(0, 0);
            Object.DestroyImmediate(tex);

            // Assert — outside the shape the SDF mask is 0, so the corner is transparent (no hard rectangle).
            Assert.That(corner.a, Is.LessThan(0.1f));
        }

        [Test]
        public void Given_AFromStopAtHalf_When_BakedViaShader_Then_TheUpperRegionIsFlatFrom()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Arrange — to-b red→blue with from-50%: the SHADER must keep the upper region flat `from` (red),
            // verifying its position-based stops match the C# bake (a default-position bake would blend blue).
            StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-b", "from-[#FF0000]", "from-50%", "to-[#0000FF]" }, out var spec);

            // Act — sample the box upper region (t≈0.27, comfortably before the 50% start, inside the shape).
            var tex = GradientSilhouetteBaker.Bake(spec, 64f, 64f, 0f, 0f, new Vector4(8f, 8f, 8f, 8f));
            Assume.That(tex, Is.Not.Null, "Precondition: the silhouette shader resolved and baked");
            var upper = tex.GetPixel(tex.width / 2, Mathf.RoundToInt(tex.height * 0.72f));
            Object.DestroyImmediate(tex);

            // Assert — still flat red (no blue yet) because the gradient does not start until 50%.
            Assert.That(upper.b, Is.LessThan(0.1f));
        }

        [Test]
        public void Given_ASkewXAngle_When_SizingTheBake_Then_TheQuadWidensToHoldTheSlant()
        {
            // Arrange / Act — a skew-x shear shifts the top/bottom edges, widening the bounding box by
            // |tanX|·h. Pure sizing math, so this runs without a graphics device.
            GradientSilhouetteBaker.QuadSize(100f, 50f, 0.5f, 0f, out var quadW, out _);

            // Assert — the baked quad is wider than the element so the slant is not clipped.
            Assert.That(quadW, Is.GreaterThan(100));
        }
    }
}
