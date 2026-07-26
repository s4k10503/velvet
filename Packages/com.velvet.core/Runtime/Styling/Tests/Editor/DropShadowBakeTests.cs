using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Guards the baked drop-shadow silhouette (<see cref="DropShadowBaker"/>): the bake is the FULL soft
    /// silhouette, where the interior is OPAQUE (alpha ~1 deep inside, ~0.5 at the box edge) and the soft halo
    /// bleeds beyond the box and fades to 0 — the shadow is painted in the CASTER's own generateVisualContent
    /// FIRST, and the binding then repaints the caster's opaque fill over it, so the opaque interior is covered
    /// and only the outer halo remains, a behind-the-element drop shadow with no interior tint and no hard edge;
    /// and that the baked-silhouette texture cache stays BOUNDED with its LRU bookkeeping in lockstep — a baked
    /// shadow is keyed by caster pixel size, so a size-varying shadow must not accumulate one full-quad
    /// <c>Texture2D</c> per distinct size, the cache evicts the least-recently-used bake past its cap, and the
    /// recency list / node map never desync from the cache (no duplicate or orphaned nodes). Two presets whose
    /// radii round to the same whole pixel must also bake the SAME geometry so a shared cache entry is correct
    /// for both. The bake and cache are private internals, driven here through the public bake API and reflection
    /// (no production test hooks). Needs a graphics device for the pixel assertions (Graphics.Blit + ReadPixels).
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DropShadowBakeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly System.Type T = typeof(DropShadowBaker);
        private static readonly FieldInfo CacheField = T.GetField("s_silhouetteCache", Priv);
        private static readonly FieldInfo LruField = T.GetField("s_silhouetteLru", Priv);
        private static readonly FieldInfo NodesField = T.GetField("s_silhouetteLruNodes", Priv);
        private static readonly FieldInfo CapField = T.GetField("MaxSilhouetteCacheEntries", Priv);
        private static readonly MethodInfo StoreMethod = T.GetMethod("StoreSilhouette", Priv);
        private static readonly MethodInfo ResetMethod = T.GetMethod("ResetStaticCaches", Priv);

        private static int CacheCount => ((ICollection)CacheField.GetValue(null)).Count;
        private static int LruCount => ((ICollection)LruField.GetValue(null)).Count;
        private static int NodesCount => ((ICollection)NodesField.GetValue(null)).Count;
        private static int Cap => (int)CapField.GetValue(null);
        private static void Store(int w, Texture2D tex) => StoreMethod.Invoke(null, new object[] { (0L, w, 0, 0), tex });

        [SetUp]
        public void SetUp() => ResetMethod?.Invoke(null, null);

        [TearDown]
        public void TearDown() => ResetMethod?.Invoke(null, null);

        // The interior must be OPAQUE: the bake is the full soft silhouette, and the binding repaints the
        // caster's opaque fill OVER it, so an opaque interior is exactly what the fill covers. A transparent
        // interior is the bug — cutting a hole there would leave a faint shadow visible over the top of the
        // fill and a hard band at the offset-down bottom edge. The center texel is the deep interior.
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

        [Test]
        public void Given_ManyDistinctSizesPlusAReStore_When_ExceedingTheCap_Then_CacheStaysBoundedAndLruInSync()
        {
            // Arrange — the cap to exceed.
            var cap = Cap;

            // Act — store (cap + 20) distinct-size silhouettes, then re-store an existing key (overwrite path).
            for (var i = 0; i < cap + 20; i++)
            {
                Store(i, new Texture2D(2, 2));
            }
            Store(cap + 19, new Texture2D(2, 2)); // re-store a still-present key: must not duplicate an LRU node

            // Assert — bounded AND the recency list / node map are exactly in lockstep with the cache.
            Assert.That((CacheCount <= cap, CacheCount == LruCount, LruCount == NodesCount),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_OverwritingAnExistingKey_When_Stored_Then_ThePreviousTextureIsDestroyed()
        {
            // Arrange — store a texture at a key, keeping the reference to check its lifetime.
            var previous = new Texture2D(2, 2);
            Store(0, previous);

            // Act — re-store the same key with a different texture (StoreSilhouette's overwrite path).
            Store(0, new Texture2D(2, 2));

            // Assert — the overwritten texture was destroyed (Unity's fake-null on a destroyed Object).
            Assert.That(previous == null, Is.True);
        }

        [Test]
        public void Given_TheLeastRecentlyUsedEntry_When_EvictedPastTheCap_Then_ItsTextureIsDestroyed()
        {
            // Arrange — the first-stored key becomes least-recently-used and is the first evicted past the cap.
            var cap = Cap;
            var oldest = new Texture2D(2, 2);
            Store(0, oldest);

            // Act — store past the cap so the oldest entry is evicted (StoreSilhouette's LRU-eviction path).
            for (var i = 1; i <= cap; i++)
            {
                Store(i, new Texture2D(2, 2));
            }

            // Assert — the evicted texture was destroyed (Unity's fake-null on a destroyed Object).
            Assert.That(oldest == null, Is.True);
        }
    }
}
