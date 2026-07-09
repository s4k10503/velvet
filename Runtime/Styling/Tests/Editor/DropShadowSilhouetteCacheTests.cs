using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the baked-silhouette texture cache (every shadow now bakes a full-size silhouette) stays
    /// BOUNDED and that its LRU bookkeeping stays in lockstep with the cache: a baked shadow is keyed by caster
    /// pixel size, so a size-varying shadow must not accumulate one full-quad <c>Texture2D</c> per distinct
    /// size — the cache evicts the least-recently-used bake past its cap, and the recency list / node map never
    /// desync from the cache (no duplicate or orphaned nodes). The cache and its store/cap are private internals
    /// of <see cref="DropShadowBaker"/>, so this test drives them by reflection (no production test hooks). GWT.
    /// </summary>
    [TestFixture]
    internal sealed class DropShadowSilhouetteCacheTests
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
        private static void ResetCaches() => ResetMethod.Invoke(null, null);
        private static void Store(int w, Texture2D tex) => StoreMethod.Invoke(null, new object[] { (0L, w, 0, 0), tex });

        [SetUp]
        public void SetUp() => ResetCaches();

        [TearDown]
        public void TearDown() => ResetCaches();

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
