using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins React DOM parity for panel focus across keyed reorders — a parity the ENGINE itself
    /// provides and Velvet must not break: the placement walk moves a non-anchor element with
    /// RemoveAt + Insert inside one flush, and UI Toolkit's focus bookkeeping validates the focused
    /// element lazily, so an element re-inserted within the same frame keeps panel focus (and
    /// receives no Blur), exactly like a DOM node moved with <c>insertBefore</c>. These specs exist
    /// because the opposite was plausible enough to almost ship a re-focus workaround: any future
    /// placement change that detaches across a frame boundary (or an engine behavior change) must
    /// surface here, not as gamepad users silently losing their place in reordered lists.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedReorderFocusTests
    {
        private sealed class KeysStore : Store<string>
        {
            public KeysStore() : base("abc") { }
            public void Set(string keys) => SetState(_ => keys);
            protected override void ResetCore() => SetState(_ => "abc");
        }

        private static KeysStore s_store;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        [Component]
        private static VNode ReorderList()
        {
            var keys = Hooks.UseStore(s_store, x => x);
            var children = new System.Collections.Generic.List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Button(name: "item-" + key, key: key.ToString(), text: key.ToString()));
            }
            return V.Div(name: "list", children: children.ToArray());
        }

        // "abc" -> "cba" leaves a single-element longest increasing subsequence, so the focused
        // middle item is NOT an anchor and the walk physically moves it (RemoveAt + Insert).

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_ItKeepsPanelFocus()
        {
            // Arrange — real panel focus on the middle item.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            var item = _host.Root.Q<VisualElement>("item-b");
            item.Focus();
            _mounted.FlushStateForTest();
            Assume.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "Precondition: the middle item holds panel focus");

            // Act — reverse the list; the focused item is moved by the placement walk.
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert — the same element instance still holds panel focus after its move.
            Assert.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "A keyed reorder that moves the focused element must not drop panel focus");
        }

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_NoBlurEdgeReachesIt()
        {
            // Arrange — hook consumers (focus rings, whileFocus styling) are edge-driven, so a
            // same-frame move must ALSO stay silent on the event channel: a spurious Blur would
            // flicker every focus-derived state even though panel focus survives.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            var item = _host.Root.Q<VisualElement>("item-b");
            var blurCount = 0;
            item.RegisterCallback<BlurEvent>(_ => blurCount++);
            item.Focus();
            _mounted.FlushStateForTest();
            Assume.That(_host.Panel.focusController.focusedElement, Is.SameAs(item),
                "Precondition: the middle item holds panel focus");

            // Act
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(blurCount, Is.EqualTo(0),
                "A same-frame move must not deliver a Blur to the moved element");
        }

        [Test]
        public void Given_AFocusedKeyedItem_When_AReorderMovesIt_Then_ItsDomPositionFollowsTheNewOrder()
        {
            // Arrange — focus survival must not come at the cost of the reorder's outcome itself.
            using var store = new KeysStore();
            s_store = store;
            _mounted = V.Mount(_host.Root, V.Component(ReorderList, key: "list"));
            _host.Root.Q<VisualElement>("item-b").Focus();
            _mounted.FlushStateForTest();

            // Act
            store.Set("cba");
            _mounted.FlushStateForTest();

            // Assert — the list committed the reversed order.
            var list = _host.Root.Q<VisualElement>("list");
            Assert.That(
                (list.ElementAt(0).name, list.ElementAt(1).name, list.ElementAt(2).name),
                Is.EqualTo(("item-c", "item-b", "item-a")),
                "The reorder commits the new order with the focused element moved in place");
        }
    }
}
