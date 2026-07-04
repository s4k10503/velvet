using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.VirtualList{T}"/>, which renders a large fixed-height collection
    /// into a ScrollView while keeping only the visible range in the DOM.
    /// <list type="bullet">
    /// <item>Reconciling a VirtualList produces a single ScrollView host.</item>
    /// <item>The host's contentContainer holds exactly two children: a total-height spacer sized to
    /// itemHeight times the item count, and a visible-items container.</item>
    /// <item>Changing the item count rescales the spacer to the new itemHeight times count.</item>
    /// <item>Reconciling the VirtualList away removes the host and disposes its controller.</item>
    /// <item>The visible range is firstVisible..lastVisible derived from scroll offset, viewport height, and
    /// itemHeight, widened by overscan on both sides and clamped to the collection bounds.</item>
    /// <item>An empty collection renders no visible items and tolerates range updates without throwing.</item>
    /// <item>The DSL rejects a null items / keySelector / renderer with <see cref="ArgumentNullException"/>, and a
    /// non-positive itemHeight with <see cref="ArgumentOutOfRangeException"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListTests : ReconcilerTestFixture
    {
        #region Host structure

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_HostIsScrollView()
        {
            // Arrange
            var tree = Tree(CreateItems(10), itemHeight: 30f);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.ElementAt(0), Is.InstanceOf<ScrollView>());
        }

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_ContentContainerHoldsSpacerAndVisibleContainer()
        {
            // Arrange
            var tree = Tree(CreateItems(50), itemHeight: 25f);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var scrollView = (ScrollView)Root.ElementAt(0);
            Assert.That(scrollView.contentContainer.childCount, Is.EqualTo(2),
                "contentContainer holds the total-height spacer and the visible-items container");
        }

        [Test]
        public void Given_VirtualList_When_Reconciled_Then_SpacerHeightIsItemHeightTimesCount()
        {
            // Arrange
            const float itemHeight = 40f;
            const int itemCount = 100;
            var tree = Tree(CreateItems(itemCount), itemHeight);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assert.That(spacer.style.height.value.value, Is.EqualTo(itemHeight * itemCount).Within(0.1f));
        }

        [Test]
        public void Given_MountedVirtualList_When_ItemCountChanges_Then_SpacerHeightRescales()
        {
            // Arrange
            var tree1 = Tree(CreateItems(10), itemHeight: 30f);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var spacer = ((ScrollView)Root.ElementAt(0)).contentContainer.ElementAt(0);
            Assume.That(spacer.style.height.value.value, Is.EqualTo(300f).Within(0.1f),
                "Precondition: the spacer initially reflects 10 items");

            // Act
            var tree2 = Tree(CreateItems(20), itemHeight: 30f);
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(spacer.style.height.value.value, Is.EqualTo(600f).Within(0.1f));
        }

        [Test]
        public void Given_MountedVirtualList_When_ReconciledAway_Then_HostIsRemoved()
        {
            // Arrange
            var tree = Tree(CreateItems(10), itemHeight: 30f);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            Assume.That(Root.childCount, Is.EqualTo(1), "Precondition: the host mounted");

            // Act
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(0));
        }

        #endregion

        #region Visible range

        [Test]
        public void Given_ViewportAtTop_When_RangeUpdated_Then_RendersVisibleWindowPlusOverscan()
        {
            // Arrange — viewport 200 / itemHeight 50 spans items 0..4; overscan 2 extends the tail to item 6.
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            Assert.That(visibleContainer.childCount, Is.EqualTo(7),
                "Items 0..4 are visible and overscan 2 adds items 5..6, for 7 rendered items");
        }

        [Test]
        public void Given_ScrolledDown_When_RangeUpdated_Then_StillRendersANonEmptyWindow()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            Assume.That(visibleContainer.childCount, Is.GreaterThan(0), "Precondition: the top window rendered items");

            // Act
            controller.UpdateVisibleRange(scrollY: 500f, viewportHeight: 200f);

            // Assert
            Assert.That(visibleContainer.childCount, Is.GreaterThan(0),
                "A scrolled-down window still renders the items now in view");
        }

        [Test]
        public void Given_EmptyCollection_When_RangeUpdated_Then_DoesNotThrow()
        {
            // Arrange
            var node = V.VirtualList(
                items: CreateItems(0),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Label(text: item.Name, key: item.Id));
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);

            // Act + Assert
            Assert.DoesNotThrow(() => controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f));
        }

        [Test]
        public void Given_ClipPathItems_When_OverlappingRangeRepatchesThem_Then_ItemsAreNotDoubleWrapped()
        {
            // Arrange: item roots carrying a clip-path utility, so CreateElement returns the clip
            // WRAPPER and the controller stores it. The reuse path must patch the resolved INNER
            // and re-store the slot's current top-level element — patching the wrapper itself used
            // to double-wrap it and leak one extra ClipPathBinding per reused item.
            var node = V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Div(className: "clip-path-[polygon(50%_0%,100%_100%,0%_100%)]"),
                overscan: 2);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, Reconciler);
            var visibleContainer = scrollView.contentContainer.ElementAt(1);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            Assume.That(visibleContainer.childCount, Is.GreaterThan(0), "Precondition: the top window rendered items");

            // Act: an overlapping window re-renders, routing the shared keys through the patch path.
            controller.UpdateVisibleRange(scrollY: 150f, viewportHeight: 200f);

            // Assert: exactly one clip binding per rendered item — no double-wrap residue.
            Assert.That(Reconciler.Context.ClipPathBindings.Count, Is.EqualTo(visibleContainer.childCount));
        }

        #endregion

        #region Context inheritance

        private static readonly ComponentContext<string> ThemeContext = ComponentContext<string>.Create("default");
        private static string s_lastSeen;
        private static StateUpdater<int> s_itemSetCount;

        [Component]
        private static VNode ItemConsumerRender()
        {
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: s_lastSeen);
        }

        [Component]
        private static VNode RerenderItemConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_itemSetCount = setCount;
            s_lastSeen = Hooks.UseContext(ThemeContext);
            return V.Label(text: $"{s_lastSeen}{count}");
        }

        private static VNode VirtualListHost(Func<TestItem, VNode> renderer)
            => V.Provider(ThemeContext, "provided", new VNode[]
            {
                V.VirtualList(
                    items: CreateItems(100),
                    keySelector: item => item.Id,
                    itemHeight: 50f,
                    renderer: renderer,
                    overscan: 0),
            });

        [Component]
        private static VNode ItemHostRender() => VirtualListHost(item => V.Component(ItemConsumerRender, key: item.Id));

        [Component]
        private static VNode RerenderItemHostRender()
            => VirtualListHost(item => V.Component(RerenderItemConsumerRender, key: item.Id));

        [Test]
        public void Given_ProviderAboveVirtualList_When_ItemRenders_Then_ReadsProvidedValue()
        {
            // Arrange: a VirtualList enclosed by a Provider, whose item renderer reads that context. Items mount
            // through the controller (outside the reconcile pass); the controller restores the context the
            // Provider established at the list's tree position by parenting items under the host fiber.
            s_lastSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(ItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[scrollView];

            // Act: the visible range renders items (no live panel; drive the range directly).
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert: the item body read the Provider enclosing the VirtualList, not the context default.
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "A VirtualList item inherits the context enclosing the list's tree position");
        }

        [Test]
        public void Given_VirtualListItem_When_ItemReRendersInIsolation_Then_StillReadsProvidedValue()
        {
            // Arrange: a VirtualList item that has mounted under an enclosing Provider.
            s_lastSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(RerenderItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_lastSeen, Is.EqualTo("provided"), "Precondition: the item mounted under the provided context");

            // Act: an item re-renders on its own setState (no host / controller re-render).
            s_lastSeen = null;
            s_itemSetCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the spine rebuilds the enclosing Provider for the isolated re-render.
            Assert.That(s_lastSeen, Is.EqualTo("provided"),
                "An isolated re-render of a VirtualList item reconstructs the context enclosing the list");
        }

        [Test]
        public void Given_VirtualListItemsMounted_When_HostUnmounts_Then_DoesNotThrow()
        {
            // Arrange: items mounted under the host (item fibers parented under the host for context sharing).
            s_lastSeen = null;
            var root = new VisualElement();
            var mounted = V.Mount(root, V.Component(ItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_lastSeen, Is.EqualTo("provided"), "Precondition: items mounted under the host context");

            // Act + Assert: tearing down disposes items via both the VE-anchored sweep and the controller
            // cleanup; FiberRenderer.Dispose is idempotent, so the double path must not throw.
            Assert.DoesNotThrow(() => mounted.Dispose());
        }

        #endregion

        #region Provider placed INSIDE the item renderer (above the consumer)

        private static readonly ComponentContext<string> InnerContext = ComponentContext<string>.Create("inner-default");
        private static string s_innerSeen;
        private static StateUpdater<int> s_innerSetCount;

        [Component]
        private static VNode InnerProviderConsumerRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_innerSetCount = setCount;
            s_innerSeen = Hooks.UseContext(InnerContext);
            return V.Label(text: $"{s_innerSeen}{count}");
        }

        // The renderer's OWN top-level node is a Provider that encloses the consumer — mirroring Portal's drained
        // subtree, which IS reconstructed via DetachedMountContext.DescendantNodes.
        [Component]
        private static VNode InnerProviderItemHostRender()
            => V.VirtualList(
                items: CreateItems(100),
                keySelector: item => item.Id,
                itemHeight: 50f,
                renderer: item => V.Provider(InnerContext, "inner-provided", new VNode[]
                {
                    V.Component(InnerProviderConsumerRender, key: item.Id),
                }),
                overscan: 0);

        [Test]
        public void Given_AVirtualListItemRendererWithItsOwnProvider_When_TheConsumerReRendersInIsolation_Then_ItStillReadsThatProvider()
        {
            // Arrange: an item whose renderer wraps the consumer in its own Provider; on mount the consumer reads it.
            s_innerSeen = null;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(InnerProviderItemHostRender, key: "host"));
            var scrollView = root.Q<ScrollView>();
            mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(0f, 200f);
            Assume.That(s_innerSeen, Is.EqualTo("inner-provided"), "Precondition: the item read its own Provider on mount");

            // Act: the consumer re-renders on its own setState (no host / controller re-render).
            s_innerSeen = null;
            s_innerSetCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert: the spine reconstructs the Provider the renderer placed above the consumer (not the default).
            Assert.That(s_innerSeen, Is.EqualTo("inner-provided"),
                "An isolated re-render reconstructs a Provider the item renderer itself placed above the consumer");
        }

        #endregion

        #region DSL argument validation

        [Test]
        public void Given_NullItems_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList<TestItem>(
                    items: null,
                    keySelector: item => item.Id,
                    itemHeight: 30f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NullKeySelector_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: (Func<TestItem, string>)null,
                    itemHeight: 30f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NullRenderer_When_VirtualListBuilt_Then_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: 30f,
                    renderer: (Func<TestItem, VNode>)null));
        }

        [Test]
        public void Given_ZeroItemHeight_When_VirtualListBuilt_Then_ThrowsArgumentOutOfRangeException()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: 0f,
                    renderer: item => V.Label(text: item.Name)));
        }

        [Test]
        public void Given_NegativeItemHeight_When_VirtualListBuilt_Then_ThrowsArgumentOutOfRangeException()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                V.VirtualList(
                    items: CreateItems(5),
                    keySelector: item => item.Id,
                    itemHeight: -10f,
                    renderer: item => V.Label(text: item.Name)));
        }

        #endregion

        #region Helpers

        private static VNode[] Tree(IReadOnlyList<TestItem> items, float itemHeight) => new VNode[]
        {
            V.VirtualList(
                items: items,
                keySelector: item => item.Id,
                itemHeight: itemHeight,
                renderer: item => V.Label(text: item.Name, key: item.Id)),
        };

        private sealed class TestItem
        {
            public string Id { get; init; }
            public string Name { get; init; }
        }

        private static IReadOnlyList<TestItem> CreateItems(int count)
        {
            var items = new TestItem[count];
            for (var i = 0; i < count; i++)
            {
                items[i] = new TestItem { Id = $"item-{i}", Name = $"Item {i}" };
            }
            return items;
        }

        #endregion
    }
}
