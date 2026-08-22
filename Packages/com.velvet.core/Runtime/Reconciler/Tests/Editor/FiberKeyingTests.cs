using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the tree-position keying contract that the inline-expansion walk and the context
    /// spine-rewalk must derive identically for the same committed node, so a registry lookup never
    /// misses.
    /// <list type="bullet">
    /// <item>An unkeyed inline ComponentNode's position key is the slot it occupies in the child array it
    /// sits in, under that array's own SlotPath — so a sibling that renders null costs its slot rather
    /// than shifting the ones after it, and a ComponentNode boundary restarts SlotPath.</item>
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
        public void Given_OneNodeIndexUnderTwoChildArrays_When_ResolveInlinePositionKey_Then_TheKeysDiffer()
        {
            // Arrange — index 0 of an unkeyed fragment's children, and index 0 of the array holding it.
            var boxes = new Dictionary<(long slotPath, int nodeIndex), object>();
            var fragmentChildren = FiberKeying.FragmentChild(FiberKeying.WalkRoot, null, 0);

            // Act
            var outer = FiberKeying.ResolveInlinePositionKey(FiberKeying.WalkRoot, 0, boxes);
            var inner = FiberKeying.ResolveInlinePositionKey(fragmentChildren, 0, boxes);

            // Assert — which array a slot belongs to is part of its key, so nesting does not collapse.
            Assert.That(inner, Is.Not.EqualTo(outer));
        }

        [Test]
        public void Given_ANestedPosition_When_ComponentChild_Then_SlotPathRestartsAtTheWalkRoots()
        {
            // Arrange — a Component found one level down an outer walk, so the position it sits at has
            // already moved off the walk root.
            var nested = FiberKeying.FragmentChild(FiberKeying.WalkRoot, null, 2);

            // Act
            var body = FiberKeying.ComponentChild(nested, null, 4);

            // Assert — the body output keys as though the walk had started there, which is what that
            // component's own isolated re-render does; the position it was found at does not.
            Assert.That(
                (body.SlotPath == FiberKeying.WalkRoot.SlotPath,
                    nested.SlotPath == FiberKeying.WalkRoot.SlotPath),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_SamePositionAcrossPasses_When_ResolveInlinePositionKey_Then_ReturnsInternedBox()
        {
            // Arrange — two independent reconcile passes share one box cache
            var boxes = new Dictionary<(long slotPath, int nodeIndex), object>();
            var first = FiberKeying.ResolveInlinePositionKey(FiberKeying.WalkRoot, 1, boxes);

            // Act — a later pass resolves the same position
            var second = FiberKeying.ResolveInlinePositionKey(FiberKeying.WalkRoot, 1, boxes);

            // Assert
            Assert.That(second, Is.SameAs(first),
                "The boxed position key is interned per position, so a later pass reuses the same box");
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

    /// <summary>
    /// Specifies the contract of <see cref="FiberTreeTraversal.NotifyContextChanged"/>, which walks a fiber
    /// subtree on a context value change and schedules the consumers that read the changed key.
    /// <list type="bullet">
    /// <item>Notification only SCHEDULES dependent consumers; it distributes no per-fiber value snapshot. Each
    /// scheduled consumer reads the new value live from the context cursor on its own re-render.</item>
    /// <item>A null root is tolerated and notifies nothing; a null key means "no specific context changed" and
    /// schedules nothing even for fibers that registered a dependency.</item>
    /// <item>Only fibers that registered a dependency on the changed key are scheduled; non-dependent fibers —
    /// including ancestors on the path to a deep dependent consumer — are not.</item>
    /// <item>Every dependent consumer in the walked subtree is reached, regardless of depth or sibling position.</item>
    /// <item>Within one propagation generation, a fiber is scheduled at most once even if it depends on several
    /// keys changed in that generation; across distinct generations it is scheduled once per generation.</item>
    /// <item>The default generation sentinel (<see cref="int.MinValue"/>) disables dedup, so repeated
    /// notifications of the same fiber each schedule it.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Re-render scheduling is observed through the <see cref="ComponentFiber.RequestRenderForContextHandler"/>
    /// delegate slot, counted per fiber.
    /// </remarks>
    [TestFixture]
    internal sealed class FiberTreeTraversalTests
    {
        [Test]
        public void Given_NullRoot_When_Notified_Then_DoesNotThrow()
        {
            // Act + Assert
            Assert.DoesNotThrow(() => FiberTreeTraversal.NotifyContextChanged(null, new object()));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedWithNullKey_Then_NotScheduled()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(new object());

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, null);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedWithDependedKey_Then_ScheduledOnce()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            var ctx = new object();
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_NonDependentFiber_When_Notified_Then_NotScheduled()
        {
            // Arrange
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, new object());

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_OnlyDescendantDependsOnContext_When_NotifiedFromParent_Then_DescendantIsScheduled()
        {
            // Arrange
            var parent = new ComponentFiber();
            var child = new ComponentFiber();
            var childScheduledCount = 0;
            child.RequestRenderForContextHandler = _ => childScheduledCount++;
            parent.AppendChild(child);
            var ctx = new object();
            child.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(parent, ctx);

            // Assert
            Assert.That(childScheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_OnlyDescendantDependsOnContext_When_NotifiedFromParent_Then_NonDependentParentIsNotScheduled()
        {
            // Arrange
            var parent = new ComponentFiber();
            var child = new ComponentFiber();
            var parentScheduledCount = 0;
            parent.RequestRenderForContextHandler = _ => parentScheduledCount++;
            parent.AppendChild(child);
            var ctx = new object();
            child.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(parent, ctx);

            // Assert
            Assert.That(parentScheduledCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_TwoDependentSiblings_When_NotifiedFromRoot_Then_BothAreScheduled()
        {
            // Arrange
            var root = new ComponentFiber();
            var childA = new ComponentFiber();
            var childB = new ComponentFiber();
            var childAScheduledCount = 0;
            var childBScheduledCount = 0;
            childA.RequestRenderForContextHandler = _ => childAScheduledCount++;
            childB.RequestRenderForContextHandler = _ => childBScheduledCount++;
            root.AppendChild(childA);
            root.AppendChild(childB);
            var ctx = new object();
            childA.RegisterContextDependency(ctx);
            childB.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That((childAScheduledCount, childBScheduledCount), Is.EqualTo((1, 1)));
        }

        [Test]
        public void Given_DeepDependentConsumer_When_NotifiedFromRoot_Then_DeepConsumerIsScheduled()
        {
            // Arrange — a consumer three levels below the change root depends on the key
            var ctx = new object();
            var root = new ComponentFiber();
            var directChild = new ComponentFiber();
            var grandchild = new ComponentFiber();
            var greatGrandchild = new ComponentFiber();
            root.AppendChild(directChild);
            directChild.AppendChild(grandchild);
            grandchild.AppendChild(greatGrandchild);
            var greatScheduledCount = 0;
            greatGrandchild.RequestRenderForContextHandler = _ => greatScheduledCount++;
            greatGrandchild.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That(greatScheduledCount, Is.EqualTo(1), "A deep dependent consumer is reached.");
        }

        [Test]
        public void Given_DeepDependentConsumer_When_NotifiedFromRoot_Then_NonDependentAncestorOnPathIsNotScheduled()
        {
            // Arrange
            var ctx = new object();
            var root = new ComponentFiber();
            var directChild = new ComponentFiber();
            var grandchild = new ComponentFiber();
            var greatGrandchild = new ComponentFiber();
            root.AppendChild(directChild);
            directChild.AppendChild(grandchild);
            grandchild.AppendChild(greatGrandchild);
            var directScheduledCount = 0;
            directChild.RequestRenderForContextHandler = _ => directScheduledCount++;
            greatGrandchild.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(root, ctx);

            // Assert
            Assert.That(directScheduledCount, Is.EqualTo(0), "A non-dependent ancestor on the path is not scheduled.");
        }

        [Test]
        public void Given_FiberDependingOnTwoKeys_When_BothChangeInSameGeneration_Then_ScheduledOnce()
        {
            // Arrange — one fiber depends on two keys that both change within the same propagation generation
            var ctxA = new object();
            var ctxB = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctxA);
            fiber.RegisterContextDependency(ctxB);
            const int generation = 7;

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctxA, generation);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctxB, generation);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedTwiceWithDefaultGeneration_Then_ScheduledEachTime()
        {
            // Arrange — the default generation sentinel disables dedup
            var ctx = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_DependentFiber_When_NotifiedInTwoDistinctGenerations_Then_ScheduledOncePerGeneration()
        {
            // Arrange
            var ctx = new object();
            var fiber = new ComponentFiber();
            var scheduledCount = 0;
            fiber.RequestRenderForContextHandler = _ => scheduledCount++;
            fiber.RegisterContextDependency(ctx);

            // Act
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx, 1);
            FiberTreeTraversal.NotifyContextChanged(fiber, ctx, 2);

            // Assert
            Assert.That(scheduledCount, Is.EqualTo(2));
        }
    }
}
