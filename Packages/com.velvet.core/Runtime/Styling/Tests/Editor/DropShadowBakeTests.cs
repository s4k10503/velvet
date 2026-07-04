using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Guards the baked drop-shadow silhouette (<see cref="DropShadowBaker"/>). The bake is the FULL soft
    /// silhouette: the interior is OPAQUE (alpha ~1 deep inside, ~0.5 at the box edge) and the soft halo bleeds
    /// beyond the box and fades to 0. The shadow is painted in the CASTER's own generateVisualContent FIRST, and
    /// the binding then repaints the caster's opaque fill over it, so the opaque interior is covered and only
    /// the outer halo remains — a behind-the-element drop shadow with no interior tint and no hard edge. Two
    /// presets whose radii round to the same whole pixel must bake the SAME geometry so a shared cache entry is
    /// correct for both. The bake + cache are private internals, driven here through the public bake API. Needs
    /// a graphics device for the pixel assertions (Graphics.Blit + ReadPixels).
    /// </summary>
    [TestFixture]
    internal sealed class DropShadowBakeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly MethodInfo ResetMethod =
            typeof(DropShadowBaker).GetMethod("ResetStaticCaches", Priv);

        [SetUp]
        public void SetUp() => ResetMethod?.Invoke(null, null);

        [TearDown]
        public void TearDown() => ResetMethod?.Invoke(null, null);

        // The interior must be OPAQUE: the bake is the full soft silhouette, and the binding repaints the
        // caster's opaque fill OVER it, so an opaque interior is exactly what the fill covers. A transparent
        // interior (the cut the old approach used) is the bug — it left a faint shadow over the top of the fill
        // and a hard band at the offset-down bottom edge. The center texel is the deep interior.
        [Test]
        public void Given_AShadowBake_When_Sampled_Then_TheInteriorTexelIsOpaque()
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange / Act — bake a typical card shadow and read its center (deep interior) texel.
            var tex = DropShadowBaker.GetOrBakeSilhouette(12f, 40f, 0f, 240f, 160f, 0f);
            Assume.That(tex, Is.Not.Null, "Precondition: the DropShadow shader resolved and produced a baked texture.");
            var centerAlpha = tex.GetPixel(tex.width / 2, tex.height / 2).a;

            // Assert — full-strength interior, covered at draw time by the repainted opaque fill.
            Assert.That(centerAlpha, Is.EqualTo(1f).Within(0.02f));
        }

        // The halo must be present OUTSIDE the box: a texel just beyond the box edge carries the soft shadow.
        // The silhouette fades over ~blur/2 px outside the edge (1 - smoothstep(-soft/2, soft/2, dist)), so the
        // sample is a few px outside the LEFT box edge — within that falloff band — on the vertical midline. The
        // box's left edge sits at x = pad (the quad is the box inset by pad per side).
        [Test]
        public void Given_AShadowBake_When_SampledInTheHaloBand_Then_TheExteriorTexelIsVisible()
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — a caster whose padding band (blur + ExtraPadding) is wide enough to sample inside.
            const float blur = 40f, w = 240f, h = 160f;
            var pad = blur + DropShadowBaker.ExtraPadding;

            // Act — bake and sample a texel a few px outside the left box edge, on the vertical midline.
            var tex = DropShadowBaker.GetOrBakeSilhouette(12f, blur, 0f, w, h, 0f);
            Assume.That(tex, Is.Not.Null, "Precondition: the DropShadow shader resolved and produced a baked texture.");
            var x = Mathf.RoundToInt(pad) - 4; // ~4 px outside the left box edge, inside the soft falloff
            var haloAlpha = tex.GetPixel(x, tex.height / 2).a;

            // Assert — the exterior halo carries shadow strength (the soft edge bleeds beyond the box).
            Assert.That(haloAlpha, Is.GreaterThan(0.05f));
        }

        // The cache keys by WHOLE-PIXEL radii (rounding is fine — bakes are pixel-resolution), so two presets
        // whose raw floats round to the same pixel share one cache entry. The bake must quantize the same way,
        // or the first-baked raw-float texture is shared by both and one gets a subpixel-off shadow. corner
        // 20.4 and 19.6 both round to 20px — they must bake to the same pixel size.
        [Test]
        public void Given_TwoCornersRoundingToSamePixel_When_Baked_Then_TheyBakeToTheSameSize()
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange / Act — bake two presets whose only difference rounds away to the same 20px corner.
            var a = DropShadowBaker.GetOrBakeSilhouette(20.4f, 30f, 0f, 200f, 120f, 0f);
            var b = DropShadowBaker.GetOrBakeSilhouette(19.6f, 30f, 0f, 200f, 120f, 0f);
            Assume.That(a != null && b != null, Is.True,
                "Precondition: the DropShadow shader resolved and baked both textures.");

            // Assert — identical bake geometry, so the shared cache entry is correct for both.
            Assert.That(a.width, Is.EqualTo(b.width));
        }
    }
}
