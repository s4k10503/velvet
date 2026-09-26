using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the coordinate a keyed inline component is registered under, and the lifetime of the
    /// interned boxes <see cref="ComponentRegistry"/> keeps for it.
    /// <list type="bullet">
    /// <item>A keyed component resolves to <c>(SlotPath, key)</c> and an unkeyed one to
    /// <c>(SlotPath, nodeIndex)</c>, each from its own cache.</item>
    /// <item>Resolving an explicit position inserts nothing; a box exists only while a registration at
    /// that position is active, and is returned to every later resolution while it does.</item>
    /// <item>Dispose clears both caches, whether or not any fiber is still registered.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class InlineExplicitPositionCacheTests
    {
        private MountedTree? _mounted;
        private static StateUpdater<int> s_setPhase;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        [Component]
        private static VNode Counter()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(name: "out", text: count.ToString());
        }

        [Component]
        private static VNode ExplicitPositionCacheHost()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            s_setPhase = setPhase;
            return V.Div(children: new VNode?[]
            {
                phase < 1
                    ? V.Div(name: "left", children: new VNode?[] { V.Component(Counter, key: "same") })
                    : null,
                phase < 2
                    ? V.Div(name: "right", children: new VNode?[] { V.Component(Counter, key: "same") })
                    : null,
            });
        }

        [Test]
        public void Given_MissingExplicitPosition_When_Resolved_Then_ActiveCacheStaysEmpty()
        {
            // Arrange
            var context = new ReconcilerContext();
            var registry = context.ComponentRegistry;

            // Act
            var first = FiberKeying.ResolveInlineRegistryPositionKey(
                FiberKeying.WalkRoot, "missing", 0,
                registry.InlinePositionKeyBoxes, registry.InlineExplicitPositionKeyBoxes);
            var second = FiberKeying.ResolveInlineRegistryPositionKey(
                FiberKeying.WalkRoot, "missing", 0,
                registry.InlinePositionKeyBoxes, registry.InlineExplicitPositionKeyBoxes);

            // Assert
            Assert.That(
                (registry.InlineExplicitPositionKeyBoxes.Count, object.ReferenceEquals(first, second)),
                Is.EqualTo((0, false)));
        }

        [Test]
        public void Given_ActiveExplicitPosition_When_Resolved_Then_ReturnsCanonicalBox()
        {
            // Arrange
            var context = new ReconcilerContext();
            var registry = context.ComponentRegistry;
            var cacheKey = (FiberKeying.WalkRoot.SlotPath, "active");
            object canonical = cacheKey;
            registry.InlineExplicitPositionKeyBoxes[cacheKey] = (canonical, 1);

            // Act
            var resolved = FiberKeying.ResolveInlineRegistryPositionKey(
                FiberKeying.WalkRoot, "active", 0,
                registry.InlinePositionKeyBoxes, registry.InlineExplicitPositionKeyBoxes);

            // Assert
            Assert.That(resolved, Is.SameAs(canonical));
        }

        [Test]
        public void Given_KeyedAndUnkeyedComponents_When_RegistryPositionsResolve_Then_EachUsesItsOwnCoordinateShape()
        {
            // Arrange
            var context = new ReconcilerContext();
            var registry = context.ComponentRegistry;

            // Act
            var keyed = FiberKeying.ResolveInlineRegistryPositionKey(
                FiberKeying.WalkRoot, "key", 7,
                registry.InlinePositionKeyBoxes, registry.InlineExplicitPositionKeyBoxes);
            var unkeyed = FiberKeying.ResolveInlineRegistryPositionKey(
                FiberKeying.WalkRoot, null, 7,
                registry.InlinePositionKeyBoxes, registry.InlineExplicitPositionKeyBoxes);

            // Assert
            Assert.That(
                (keyed is System.ValueTuple<long, string>, unkeyed is System.ValueTuple<long, int>,
                    registry.InlineExplicitPositionKeyBoxes.Count, registry.InlinePositionKeyBoxes.Count),
                Is.EqualTo((true, true, 0, 1)));
        }

        [Test]
        public void Given_EmptyRegistryWithCachedPositions_When_Disposed_Then_BothPositionCachesAreCleared()
        {
            // Arrange
            var context = new ReconcilerContext();
            var registry = context.ComponentRegistry;
            registry.InlinePositionKeyBoxes[(1, 2)] = new object();
            registry.InlineExplicitPositionKeyBoxes[(1, "key")] = (new object(), 0);

            // Act
            registry.Dispose();

            // Assert
            Assert.That(
                (registry.InlinePositionKeyBoxes.Count, registry.InlineExplicitPositionKeyBoxes.Count),
                Is.EqualTo((0, 0)));
        }

        [Test]
        public void Given_TwoRegistrationsAtOneExplicitPosition_When_EachUnmounts_Then_TheCacheReferenceCountTracksThem()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ExplicitPositionCacheHost, key: "host"));
            var registry = _mounted.Root.Reconciler.Context.ComponentRegistry;
            var initial = (registry.InlineExplicitPositionKeyBoxes.Count,
                registry.InlineExplicitPositionKeyBoxes.Values.Sum(entry => entry.registrations));

            // Act
            s_setPhase.Invoke(1);
            _mounted.FlushStateForTest();
            var afterFirst = (registry.InlineExplicitPositionKeyBoxes.Count,
                registry.InlineExplicitPositionKeyBoxes.Values.Sum(entry => entry.registrations));
            s_setPhase.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (initial.Item1, initial.Item2, afterFirst.Item1, afterFirst.Item2,
                    registry.InlineExplicitPositionKeyBoxes.Count),
                Is.EqualTo((2, 3, 2, 2, 1)));
        }

        [Test]
        public void Given_RegistryHoldingInlineFibersAndAnUnregisteredExplicitPosition_When_Disposed_Then_ThePositionCacheIsCleared()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ExplicitPositionCacheHost, key: "host"));
            var registry = _mounted.Root.Reconciler.Context.ComponentRegistry;
            registry.InlineExplicitPositionKeyBoxes[(long.MinValue, "orphan")] = (new object(), 0);

            // Act
            registry.Dispose();

            // Assert
            Assert.That(registry.InlineExplicitPositionKeyBoxes, Is.Empty);
        }
    }
}
