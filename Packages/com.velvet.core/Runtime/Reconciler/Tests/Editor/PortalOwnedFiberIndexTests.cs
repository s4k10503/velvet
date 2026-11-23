using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the registry's index of the inline fibers each Portal placeholder owns, which the hide of a
    /// Suspense, the cleanup of a Portal and the drain of a Portal's children read in place of a test of every
    /// registered fiber: it lists a fiber under the placeholder it is stamped with once mounted, under none once
    /// the Portal leaves, and under the new placeholder once a restamp moves it. Each read answers -1 where the
    /// registry holds no such index. A fiber leaves its placeholder's list by a node the registry holds for it rather
    /// than by a search, which the cases below that read the nodes specify.
    /// </summary>
    [TestFixture]
    internal sealed class PortalOwnedFiberIndexTests
    {
        private VisualElement _root;
        private static StateUpdater<int> s_setPortals;
        private static VisualElement s_target;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_target = new VisualElement();
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode Child() => V.Label(text: "c");

        [Component]
        private static VNode Host()
        {
            var (count, setCount) = Hooks.UseState(2);
            s_setPortals = setCount;
            var portals = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                portals[i] = V.Portal(s_target, children: new VNode[] { V.Component(Child, key: "c") }, key: "p" + i);
            }
            return V.Div(children: portals);
        }

        private static IDictionary IndexOf(ComponentRegistry registry)
            => (IDictionary)typeof(ComponentRegistry)
                .GetField("_inlineFibersOfPortal", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(registry);

        private static int PlaceholdersIndexed(MountedTree mounted)
            => IndexOf(mounted.Root.Reconciler.Context.ComponentRegistry)?.Count ?? -1;

        private static int FibersIndexed(MountedTree mounted)
        {
            var index = IndexOf(mounted.Root.Reconciler.Context.ComponentRegistry);
            if (index == null) return -1;
            var fibers = 0;
            foreach (DictionaryEntry entry in index) fibers += ((ICollection)entry.Value).Count;
            return fibers;
        }

        private static int FibersIndexedUnderAPlaceholderTheyAreNotStampedWith(MountedTree mounted)
        {
            var index = IndexOf(mounted.Root.Reconciler.Context.ComponentRegistry);
            if (index == null) return -1;
            var strays = 0;
            foreach (DictionaryEntry entry in index)
            {
                foreach (ComponentFiber fiber in (IEnumerable)entry.Value)
                {
                    if (!ReferenceEquals(fiber.OwningPortalPlaceholder, entry.Key)) strays++;
                }
            }
            return strays;
        }

        private static void RenderHost(MountedTree mounted, int portals)
        {
            s_setPortals.Invoke(portals);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_TwoPortalsEachRenderingAComponent_When_TheTreeMounts_Then_TwoPlaceholdersAreIndexed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            var indexed = PlaceholdersIndexed(mounted);

            // Assert
            Assert.That(indexed, Is.EqualTo(2));
        }

        [Test]
        public void Given_TwoPortalsEachRenderingAComponent_When_TheTreeMounts_Then_EachFiberIsIndexedOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            var indexed = FibersIndexed(mounted);

            // Assert
            Assert.That(indexed, Is.EqualTo(2));
        }

        [Test]
        public void Given_TwoPortalsEachRenderingAComponent_When_TheTreeMounts_Then_NoFiberIsIndexedUnderAPlaceholderItIsNotStampedWith()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            var strays = FibersIndexedUnderAPlaceholderTheyAreNotStampedWith(mounted);

            // Assert
            Assert.That(strays, Is.EqualTo(0));
        }

        [Test]
        public void Given_TwoMountedPortals_When_OneIsRenderedAway_Then_OnlyTheOthersPlaceholderIsIndexed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            RenderHost(mounted, 1);

            // Assert
            Assert.That(PlaceholdersIndexed(mounted), Is.EqualTo(1));
        }

        [Test]
        public void Given_TwoMountedPortals_When_OneIsRenderedAway_Then_OnlyItsFiberStaysIndexed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            RenderHost(mounted, 1);

            // Assert
            Assert.That(FibersIndexed(mounted), Is.EqualTo(1));
        }

        private static bool Restamp(ComponentRegistry registry, ComponentFiber fiber, VisualElement placeholder)
        {
            var restamp = typeof(ComponentRegistry).GetMethod("RestampOwningPortal", BindingFlags.Instance | BindingFlags.NonPublic);
            if (restamp == null) return false;
            restamp.Invoke(registry, new object[] { fiber, placeholder });
            return true;
        }

        // A fiber registered inline the way the registry's own table cases write it, stamped and indexed under first.
        private static (ComponentRegistry Registry, ComponentFiber Fiber) RegisteredUnder(VisualElement first)
        {
            var registry = new ComponentRegistry(new ReconcilerContext());
            var fiber = new ComponentFiber { OwningPortalPlaceholder = first };
            var key = (fiber.Parent, (VisualElement)null, new VisualElement(), new object(), new object());
            var table = (IDictionary)typeof(ComponentRegistry)
                .GetField("_inlineFiberToKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry);
            table[fiber] = key;
            typeof(ComponentRegistry).GetMethod("IndexUnderOwningPortal", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(registry, new object[] { fiber });
            return (registry, fiber);
        }

        private static int IndexedUnder(ComponentRegistry registry, VisualElement placeholder)
        {
            var index = IndexOf(registry);
            if (index == null) return -1;
            return index.Contains(placeholder) ? ((ICollection)index[placeholder]).Count : 0;
        }

        [Test]
        public void Given_AnIndexedFiber_When_ItIsRestampedWithAnotherPlaceholder_Then_TheOldPlaceholderHoldsNoFiber()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            var (registry, fiber) = RegisteredUnder(first);

            // Act
            Restamp(registry, fiber, second);

            // Assert
            Assert.That(IndexedUnder(registry, first), Is.EqualTo(0));
        }

        [Test]
        public void Given_AnIndexedFiber_When_ItIsRestampedWithAnotherPlaceholder_Then_TheNewPlaceholderHoldsIt()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            var (registry, fiber) = RegisteredUnder(first);

            // Act
            Restamp(registry, fiber, second);

            // Assert
            Assert.That(IndexedUnder(registry, second), Is.EqualTo(1));
        }

        private static ComponentFiber[] OwnedBy(ComponentRegistry registry, VisualElement placeholder)
        {
            var index = IndexOf(registry);
            if (index == null || !index.Contains(placeholder)) return null;
            return ((IEnumerable)index[placeholder]).Cast<ComponentFiber>().ToArray();
        }

        private static int NodesHeld(ComponentRegistry registry)
            => ((IDictionary)typeof(ComponentRegistry)
                .GetField("_portalIndexNodes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(registry))?.Count ?? -1;

        private static ComponentFiber AlsoRegisteredUnder(ComponentRegistry registry, VisualElement placeholder)
        {
            var fiber = new ComponentFiber { OwningPortalPlaceholder = placeholder };
            var key = (fiber.Parent, (VisualElement)null, new VisualElement(), new object(), new object());
            var table = (IDictionary)typeof(ComponentRegistry)
                .GetField("_inlineFiberToKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(registry);
            table[fiber] = key;
            typeof(ComponentRegistry).GetMethod("IndexUnderOwningPortal", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(registry, new object[] { fiber });
            return fiber;
        }

        [Test]
        public void Given_TwoPortalsEachRenderingAComponent_When_TheTreeMounts_Then_EachIndexedFiberHoldsANodeToLeaveBy()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            var nodes = NodesHeld(mounted.Root.Reconciler.Context.ComponentRegistry);

            // Assert
            Assert.That(nodes, Is.EqualTo(2));
        }

        [Test]
        public void Given_TwoMountedPortals_When_OneIsRenderedAway_Then_OnlyTheOthersFiberHoldsANode()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Act
            RenderHost(mounted, 1);

            // Assert
            Assert.That(NodesHeld(mounted.Root.Reconciler.Context.ComponentRegistry), Is.EqualTo(1));
        }

        [Test]
        public void Given_AnIndexedFiber_When_ItIsRestampedWithAnotherPlaceholder_Then_ItHoldsOneNode()
        {
            // Arrange
            var (registry, fiber) = RegisteredUnder(new VisualElement());

            // Act
            Restamp(registry, fiber, new VisualElement());

            // Assert
            Assert.That(NodesHeld(registry), Is.EqualTo(1));
        }

        [Test]
        public void Given_ThreeFibersIndexedUnderOnePlaceholder_When_TheFirstIsRestampedAway_Then_TheOtherTwoStayInStampOrder()
        {
            // Arrange
            var placeholder = new VisualElement();
            var (registry, first) = RegisteredUnder(placeholder);
            var second = AlsoRegisteredUnder(registry, placeholder);
            var third = AlsoRegisteredUnder(registry, placeholder);

            // Act
            Restamp(registry, first, new VisualElement());

            // Assert
            Assert.That(OwnedBy(registry, placeholder), Is.EqualTo(new[] { second, third }));
        }
    }
}
