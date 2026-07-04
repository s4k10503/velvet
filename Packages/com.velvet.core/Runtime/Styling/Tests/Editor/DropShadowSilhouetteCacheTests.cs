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
    }
}
