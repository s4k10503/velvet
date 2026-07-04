using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the tree-position keying contract that the inline-expansion walk and the context
    /// spine-rewalk must derive identically for the same committed node, so a registry lookup keyed
    /// by <c>(parentFiber, positionKey, identity)</c> never misses.
    /// <list type="bullet">
    /// <item>An unkeyed inline ComponentNode's position key is the n-th occurrence of its identity
    /// within one reconcile scope, counted independently per identity.</item>
    /// <item>Scope segments are joined by the NUL (U+0000) delimiter; a null parent scope means the
    /// outermost keyed boundary, so the contribution becomes the entire scope.</item>
    /// <item>A keyed Fragment / Provider / Component extends the enclosing scope with its own key; an
    /// unkeyed one contributes its positional index, but only once an enclosing keyed boundary has
    /// established a scope — otherwise it stays scope-less (null).</item>
    /// <item>A Memo opens an <c>"m"</c>-prefixed index scope so a nested Memo cannot collide with an
    /// unkeyed Component at the same node index, and its dep-cache key prefers an explicit key over
    /// that scope.</item>
    /// <item>A Suspense boundary key extends the enclosing scope by its key or index, and its
    /// committed subtree renders under that key extended by <c>"p"</c> (primary) or <c>"f"</c>
    /// (fallback), keeping the two subtrees in disjoint scopes.</item>
    /// <item>Index contributions are stringified with the invariant culture.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class FiberKeyingTests
    {
        private const string Nul = "\0";

        [Test]
        public void Given_NullParentScope_When_ComposeFragmentScope_Then_ReturnsContributionAsWholeScope()
        {
            // Act
            var scope = FiberKeying.ComposeFragmentScope(null, "a");

            // Assert
            Assert.That(scope, Is.EqualTo("a"));
        }

        [Test]
        public void Given_NonNullParentScope_When_ComposeFragmentScope_Then_JoinsSegmentsWithNul()
        {
            // Act
            var scope = FiberKeying.ComposeFragmentScope("a", "b");

            // Assert
            Assert.That(scope, Is.EqualTo("a" + Nul + "b"));
        }

        [Test]
        public void Given_RepeatedIdentity_When_ResolveInlinePositionKey_Then_CountsPerIdentityIndependently()
        {
            // Arrange
            var counters = new Dictionary<object, int>();
            var boxes = new Dictionary<(object identity, int index), object>();
            var idA = new object();
            var idB = new object();

            // Act
            var firstA = FiberKeying.ResolveInlinePositionKey(counters, idA, boxes);
            var secondA = FiberKeying.ResolveInlinePositionKey(counters, idA, boxes);
            var firstB = FiberKeying.ResolveInlinePositionKey(counters, idB, boxes);
            var thirdA = FiberKeying.ResolveInlinePositionKey(counters, idA, boxes);

            // Assert — counting advances per identity and is tracked independently for idA versus idB
            Assert.That(firstA, Is.EqualTo((idA, 0)));
            Assert.That(secondA, Is.EqualTo((idA, 1)));
            Assert.That(firstB, Is.EqualTo((idB, 0)));
            Assert.That(thirdA, Is.EqualTo((idA, 2)));
        }

        [Test]
        public void Given_SameIdentityAndIndexAcrossPasses_When_ResolveInlinePositionKey_Then_ReturnsInternedBox()
        {
            // Arrange — two independent reconcile passes (fresh per-pass counters) share one box cache
            var boxes = new Dictionary<(object identity, int index), object>();
            var identity = new object();
            var firstPassCounters = new Dictionary<object, int>();
            var secondPassCounters = new Dictionary<object, int>();
            var first = FiberKeying.ResolveInlinePositionKey(firstPassCounters, identity, boxes);

            // Act — a later pass resolves the same (identity, index) position
            var second = FiberKeying.ResolveInlinePositionKey(secondPassCounters, identity, boxes);

            // Assert
            Assert.That(second, Is.SameAs(first),
                "The boxed position key is interned per (identity, index), so a later pass reuses the same box");
        }

        [Test]
        public void Given_KeyedFragmentWithinScope_When_FragmentChildScope_Then_ExtendsScopeWithKey()
        {
            // Act
            var scope = FiberKeying.FragmentChildScope("p", "k", 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "k"));
        }

        [Test]
        public void Given_KeyedFragmentAtRoot_When_FragmentChildScope_Then_KeyBecomesScope()
        {
            // Act
            var scope = FiberKeying.FragmentChildScope(null, "k", 3);

            // Assert
            Assert.That(scope, Is.EqualTo("k"));
        }

        [Test]
        public void Given_UnkeyedFragmentScopeLess_When_FragmentChildScope_Then_StaysNull()
        {
            // Act
            var scope = FiberKeying.FragmentChildScope(null, null, 3);

            // Assert
            Assert.That(scope, Is.Null);
        }

        [Test]
        public void Given_UnkeyedFragmentWithinScope_When_FragmentChildScope_Then_ContributesIndex()
        {
            // Act
            var scope = FiberKeying.FragmentChildScope("p", null, 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "3"));
        }

        [Test]
        public void Given_ScopeLess_When_ProviderChildScope_Then_StaysNull()
        {
            // Act
            var scope = FiberKeying.ProviderChildScope(null, "k", 3);

            // Assert
            Assert.That(scope, Is.Null);
        }

        [Test]
        public void Given_KeyedProviderWithinScope_When_ProviderChildScope_Then_ExtendsWithKey()
        {
            // Act
            var scope = FiberKeying.ProviderChildScope("p", "k", 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "k"));
        }

        [Test]
        public void Given_UnkeyedProviderWithinScope_When_ProviderChildScope_Then_ContributesIndex()
        {
            // Act
            var scope = FiberKeying.ProviderChildScope("p", null, 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "3"));
        }

        [Test]
        public void Given_ScopeLess_When_ComponentChildScope_Then_StaysNull()
        {
            // Act
            var scope = FiberKeying.ComponentChildScope(null, "k", 3);

            // Assert
            Assert.That(scope, Is.Null);
        }

        [Test]
        public void Given_UnkeyedComponentWithinScope_When_ComponentChildScope_Then_ContributesIndex()
        {
            // Act
            var scope = FiberKeying.ComponentChildScope("p", null, 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "3"));
        }

        [Test]
        public void Given_RootMemo_When_MemoScope_Then_PrefixesIndexWithM()
        {
            // Act
            var scope = FiberKeying.MemoScope(null, 3);

            // Assert
            Assert.That(scope, Is.EqualTo("m3"));
        }

        [Test]
        public void Given_NestedMemo_When_MemoScope_Then_ComposesParentWithMPrefixedIndex()
        {
            // Act
            var scope = FiberKeying.MemoScope("p", 3);

            // Assert
            Assert.That(scope, Is.EqualTo("p" + Nul + "m3"));
        }

        [Test]
        public void Given_ExplicitMemoKey_When_MemoCacheKey_Then_PrefersExplicitKey()
        {
            // Act
            var cacheKey = FiberKeying.MemoCacheKey("k", "m3");

            // Assert
            Assert.That(cacheKey, Is.EqualTo("k"));
        }

        [Test]
        public void Given_NoMemoKey_When_MemoCacheKey_Then_FallsBackToScope()
        {
            // Act
            var cacheKey = FiberKeying.MemoCacheKey(null, "m3");

            // Assert
            Assert.That(cacheKey, Is.EqualTo("m3"));
        }

        [Test]
        public void Given_UnkeyedSuspenseWithinScope_When_SuspenseKey_Then_ContributesIndex()
        {
            // Act
            var key = FiberKeying.SuspenseKey("p", null, 3);

            // Assert
            Assert.That(key, Is.EqualTo("p" + Nul + "3"));
        }

        [Test]
        public void Given_KeyedSuspenseAtRoot_When_SuspenseKey_Then_KeyBecomesScope()
        {
            // Act
            var key = FiberKeying.SuspenseKey(null, "k", 3);

            // Assert
            Assert.That(key, Is.EqualTo("k"));
        }

        [Test]
        public void Given_PrimarySubtree_When_SuspenseSubtreeScope_Then_MarksWithP()
        {
            // Act
            var scope = FiberKeying.SuspenseSubtreeScope("s", isFallback: false);

            // Assert
            Assert.That(scope, Is.EqualTo("s" + Nul + "p"));
        }

        [Test]
        public void Given_FallbackSubtree_When_SuspenseSubtreeScope_Then_MarksWithF()
        {
            // Act
            var scope = FiberKeying.SuspenseSubtreeScope("s", isFallback: true);

            // Assert
            Assert.That(scope, Is.EqualTo("s" + Nul + "f"));
        }

        [Test]
        public void Given_NodeIndex_When_Index_Then_UsesInvariantCultureStringification()
        {
            // Act
            var text = FiberKeying.Index(42);

            // Assert
            Assert.That(text, Is.EqualTo("42"));
        }
    }
}
