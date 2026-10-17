using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Guards the baked box-shadow layer (<see cref="BoxShadowBaker"/>): its shape, its Gaussian falloff and the
    /// cut-out of the caster's own box, the rect it covers, CSS's spread rule for its corner radius, and that the
    /// baked texture cache stays BOUNDED with its LRU bookkeeping in lockstep — a baked shadow is keyed by caster
    /// pixel size, so a size-varying shadow must not accumulate one full-quad <c>Texture2D</c> per distinct size,
    /// the cache evicts the least-recently-used bake past its cap, and the recency list / node map never desync
    /// from the cache (no duplicate or orphaned nodes). The bake and cache are private internals, driven here
    /// through the public bake API and reflection (no production test hooks). Needs a graphics device for the
    /// pixel assertions (Graphics.Blit + ReadPixels). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class BoxShadowBakeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly System.Type T = typeof(BoxShadowBaker);
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
        private static void Store(int w, Texture2D tex)
            => StoreMethod.Invoke(null, new object[] { (0L, 0L, 0L, w, 0, 0, 0L), tex });

        private static ShadowSpec Layer(float blur, float offsetY = 0f, float spread = 0f, float offsetX = 0f,
            bool inset = false)
            => new(Color.black, blur, offsetY, spread, offsetX, inset);

        // A borderless caster box with one radius on every corner.
        private static ShadowBox Box(float w, float h, float radius = 0f)
            => new(w, h, new Vector4(radius, radius, radius, radius), Vector4.zero);

        // The baked alpha at a point of the caster's local space (y down), read from the texel covering it.
        private static float AlphaAt(Texture2D tex, Rect rect, float x, float y)
            => tex.GetPixel(Mathf.FloorToInt(x - rect.x), tex.height - 1 - Mathf.FloorToInt(y - rect.y)).a;

        [SetUp]
        public void SetUp() => ResetMethod?.Invoke(null, null);

        [TearDown]
        public void TearDown() => ResetMethod?.Invoke(null, null);

        [Test]
        public void Given_AShadowBake_When_SampledInsideTheBox_Then_TheBoxIsCutOut()
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — CSS draws an outer shadow outside the caster's border box only.
            var layer = Layer(40f);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(Box(240f, 160f, 12f), layer, Vector2.zero);

            // Assert
            Assert.That(tex != null ? tex.GetPixel(tex.width / 2, tex.height / 2).a : float.NaN,
                Is.EqualTo(0f).Within(0.01f));
        }

        // Past a straight edge of a box much larger than the blur, a Gaussian of standard deviation σ = blur / 2
        // leaves 0.5·erfc(d / (σ·√2)) at distance d outside it. σ = 10 here; the sample is the texel whose centre
        // lies d past the box's right edge on its middle row.
        [TestCase(5.5f, 0.29116f)]
        [TestCase(10.5f, 0.14686f)]
        [TestCase(20.5f, 0.02018f)]
        public void Given_AShadowBake_When_SampledPastAnEdge_Then_ItFallsOffAsAGaussianOfHalfTheBlur(
            float distance, float expected)
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange
            var layer = Layer(20f);
            var rect = BoxShadowBaker.ShadowRect(Box(200f, 200f), layer, Vector2.zero);
            var x = Mathf.FloorToInt(200f - rect.x + distance);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(Box(200f, 200f), layer, Vector2.zero);

            // Assert
            Assert.That(tex != null ? tex.GetPixel(x, tex.height / 2).a : float.NaN,
                Is.EqualTo(expected).Within(0.01f));
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
            var a = BoxShadowBaker.GetOrBakeSilhouette(Box(200f, 120f, 20.4f), Layer(30f), Vector2.zero);
            var b = BoxShadowBaker.GetOrBakeSilhouette(Box(200f, 120f, 19.6f), Layer(30f), Vector2.zero);
            Assume.That(a != null && b != null, Is.True,
                "Precondition: the BoxShadow shader resolved and baked both textures.");

            // Assert — identical bake geometry, so the shared cache entry is correct for both.
            Assert.That(a.width, Is.EqualTo(b.width));
        }

        [Test]
        public void Given_AnUprightShadow_When_ItsRectIsTaken_Then_ItIsTheSpreadShapePaddedByThreeSigmasAndMoved()
        {
            // Arrange — blur 4 is σ = 2, so three of them and ExtraPadding pad each side; spread 2 grows the box.
            var pad = 6f + BoxShadowBaker.ExtraPadding;

            // Act
            var rect = BoxShadowBaker.ShadowRect(Box(100f, 40f), Layer(4f, offsetY: 6f, spread: 2f, offsetX: 4f),
                Vector2.zero);

            // Assert
            Assert.That(new[] { rect.x, rect.y, rect.width, rect.height },
                Is.EqualTo(new[] { 4f - 2f - pad, 6f - 2f - pad, 104f + (2f * pad), 44f + (2f * pad) })
                    .Within(1e-4f));
        }

        [Test]
        public void Given_ANegativeSpread_When_ItsRectIsTaken_Then_TheShapeShrinks()
        {
            // Arrange — Tailwind's shadow-lg back layer, 0 4px 6px -4px: σ = 3 pads each side by 9 + ExtraPadding.
            var pad = 9f + BoxShadowBaker.ExtraPadding;

            // Act
            var rect = BoxShadowBaker.ShadowRect(Box(100f, 40f), Layer(6f, offsetY: 4f, spread: -4f), Vector2.zero);

            // Assert
            Assert.That(new[] { rect.width, rect.height },
                Is.EqualTo(new[] { 92f + (2f * pad), 32f + (2f * pad) }).Within(1e-4f));
        }

        [Test]
        public void Given_AShadowOnACasterSkewedOnY_When_ItsRectIsTaken_Then_ItGrowsByTheShearOverhang()
        {
            // Arrange — with no blur the 102x42 padded box under skewY(45deg) spans 0.5·102 + 0.5·42 either side
            // of its centre vertically, and keeps its width.
            var skew = new Vector2(0f, 45f);
            var pad = BoxShadowBaker.ExtraPadding;

            // Act
            var rect = BoxShadowBaker.ShadowRect(Box(100f, 40f), Layer(0f), skew);

            // Assert
            Assert.That(new[] { rect.width, rect.height },
                Is.EqualTo(new[] { 100f + (2f * pad), 100f + 40f + (4f * pad) }).Within(1e-3f));
        }

        [Test]
        public void Given_AnOffsetShadowOnACasterSkewedOnY_When_ItsRectIsTaken_Then_TheOffsetShearsWithTheBox()
        {
            // Arrange — skewY(45deg) carries an offset of (10, 0) to (10, 10), so the shadow centres on the box
            // centre (50, 20) moved by that.
            var skew = new Vector2(0f, 45f);

            // Act
            var rect = BoxShadowBaker.ShadowRect(Box(100f, 40f), Layer(0f, offsetX: 10f), skew);

            // Assert
            Assert.That(new[] { rect.center.x, rect.center.y }, Is.EqualTo(new[] { 60f, 30f }).Within(1e-3f));
        }

        [Test]
        public void Given_AShadowOnACasterSkewedOnY_When_Baked_Then_TheTextureCoversTheShearOverhang()
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — the rect the paint draws the bake into, which a texel each way has to cover.
            var skew = new Vector2(0f, 45f);
            var rect = BoxShadowBaker.ShadowRect(Box(100f, 40f), Layer(0f), skew);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(Box(100f, 40f), Layer(0f), skew);

            // Assert
            Assert.That(tex != null ? tex.height : -1, Is.GreaterThanOrEqualTo(rect.height));
        }

        [TestCase(-7.5f, -7.5f, 1f, TestName = "Given_AShadowOnABoxRoundedAtOneCorner_When_Baked_Then_TheSquareCornerOfTheSpreadShapeCoversThePoint")]
        [TestCase(207.5f, -7.5f, 0f, TestName = "Given_AShadowOnABoxRoundedAtOneCorner_When_Baked_Then_TheRoundCornerOfTheSpreadShapeMissesThePoint")]
        public void Given_AShadowOnABoxRoundedAtOneCorner_When_Baked_Then_OnlyThatCornerOfTheSpreadShapeIsRound(
            float x, float y, float expected)
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — only the top-right corner is rounded (40px), and a 10px spread with no blur grows the
            // shape: its top-left corner stays square and covers a point 7.5px out along that diagonal, while its
            // top-right corner, rounded to 50px, misses the same point there.
            var box = new ShadowBox(200f, 120f, new Vector4(0f, 40f, 0f, 0f), Vector4.zero);
            var layer = Layer(0f, spread: 10f);
            var rect = BoxShadowBaker.ShadowRect(box, layer, Vector2.zero);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(box, layer, Vector2.zero);

            // Assert
            Assert.That(tex != null ? AlphaAt(tex, rect, x, y) : float.NaN, Is.EqualTo(expected).Within(0.02f));
        }

        [TestCase(5.5f, 60.5f, 1f, TestName = "Given_AnInsetShadowWithASpread_When_Baked_Then_ItCoversABandInsideTheBox")]
        [TestCase(100.5f, 60.5f, 0f, TestName = "Given_AnInsetShadowWithASpread_When_Baked_Then_ItDoesNotCoverTheMiddle")]
        public void Given_AnInsetShadowWithASpread_When_Baked_Then_ItCoversOnlyABandInsideTheBox(
            float x, float y, float expected)
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — inset 0 0 0 10px: a hole 10px in from every side, drawn inside the padding box only.
            var box = Box(200f, 120f);
            var layer = Layer(0f, spread: 10f, inset: true);
            var rect = BoxShadowBaker.ShadowRect(box, layer, Vector2.zero);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(box, layer, Vector2.zero);

            // Assert
            Assert.That(tex != null ? AlphaAt(tex, rect, x, y) : float.NaN, Is.EqualTo(expected).Within(0.02f));
        }

        [TestCase(100.5f, 5.5f, 1f, TestName = "Given_AnInsetShadowOffsetDown_When_Baked_Then_ItCoversTheTopEdge")]
        [TestCase(100.5f, 114.5f, 0f, TestName = "Given_AnInsetShadowOffsetDown_When_Baked_Then_ItDoesNotCoverTheBottomEdge")]
        public void Given_AnInsetShadowOffsetDown_When_Baked_Then_ItCoversOnlyTheTopEdge(float x, float y, float expected)
        {
            TestGraphics.IgnoreIfHeadless("a GPU shadow bake (Graphics.Blit + ReadPixels)");

            // Arrange — inset 0 10px: the hole moves 10px down, uncovering a band along the top edge.
            var box = Box(200f, 120f);
            var layer = Layer(0f, offsetY: 10f, inset: true);
            var rect = BoxShadowBaker.ShadowRect(box, layer, Vector2.zero);

            // Act
            var tex = BoxShadowBaker.GetOrBakeSilhouette(box, layer, Vector2.zero);

            // Assert
            Assert.That(tex != null ? AlphaAt(tex, rect, x, y) : float.NaN, Is.EqualTo(expected).Within(0.02f));
        }

        [Test]
        public void Given_AnInsetShadowOnABorderedBox_When_ItsRectIsTaken_Then_ItIsThePaddingBox()
        {
            // Arrange — borders left 2, top 3, right 4, bottom 5.
            var box = new ShadowBox(100f, 40f, Vector4.zero, new Vector4(2f, 3f, 4f, 5f));

            // Act
            var rect = BoxShadowBaker.ShadowRect(box, Layer(4f, offsetY: 6f, inset: true), Vector2.zero);

            // Assert
            Assert.That(new[] { rect.x, rect.y, rect.width, rect.height },
                Is.EqualTo(new[] { 2f, 3f, 94f, 32f }).Within(1e-4f));
        }

        [Test]
        public void Given_ABorderedBox_When_ItsPaddingRadiiAreTaken_Then_EachCornerLosesItsWiderBorder()
        {
            // Arrange — a 10px radius on every corner; borders left 2, top 3, right 4, bottom 5.
            var box = new ShadowBox(100f, 40f, new Vector4(10f, 10f, 10f, 10f), new Vector4(2f, 3f, 4f, 5f));

            // Act
            var radii = BoxShadowBaker.PaddingRadii(box);

            // Assert
            Assert.That(new[] { radii.x, radii.y, radii.z, radii.w },
                Is.EqualTo(new[] { 7f, 6f, 5f, 5f }).Within(1e-4f));
        }

        // CSS Backgrounds 3's own example: a 10px radius under a 20px spread is 10 + 20 × (1 + (0.5 − 1)³).
        [TestCase(10f, 20f, 27.5f)]
        [TestCase(0f, 20f, 0f)]
        [TestCase(30f, 20f, 50f)]
        [TestCase(10f, -4f, 6f)]
        [TestCase(2f, -4f, 0f)]
        public void Given_ACornerRadiusAndASpread_When_TheShadowRadiusIsTaken_Then_ItFollowsTheSpreadRule(
            float radius, float spread, float expected)
        {
            // Act
            var shadowRadius = BoxShadowBaker.SpreadRadius(radius, spread);

            // Assert
            Assert.That(shadowRadius, Is.EqualTo(expected).Within(1e-4f));
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
