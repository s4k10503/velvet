using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the inline-fiber teardown leak class: an inline-mounted component nested INSIDE a
    /// host element anchors on its parent FIBER (not the host VE), so <c>ComponentRegistry.Remove(VE)</c> — which
    /// only knows wrapper-mounted fibers — cannot dispose it, and the host's owning reconcile emits the host as an
    /// opaque leaf, so the orphan sweep never collects it either. When such a host is torn down OUT OF BAND — a
    /// keyed type-swap, a Portal unmount, or a VirtualList recycle — neither disposal path runs and the inline
    /// child leaks: its store subscription stays live (and a same-key re-entry would re-pair it as a zombie whose
    /// local state updates never re-render). The fix disposes by subtree containment in
    /// <c>FiberElementCleaner.CleanupElement</c>, mirroring the AnimatePresence(Wait) exit-anchor disposal.
    ///
    /// The observable is the live store-subscription count: <c>UseStore</c> subscribes synchronously at render and
    /// releases on fiber disposal (no effect flush needed), so a leaked inline child shows up as a subscription
    /// that was never released. Each case is RED before the fix (a stale subscription survives) and GREEN after.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class InlineFiberTeardownLeakTests
    {
        private readonly record struct CountState(int N);

        private sealed class CountStore : Store<CountState>
        {
            public CountStore() : base(new CountState(0)) { }
            protected override void ResetCore() => SetState(_ => new CountState(0));
        }

        private static CountStore s_store;

        // Inline-mounted (no wrapper): a component placed directly in a host element's children. Subscribes to the
        // shared store so its liveness is observable through the store's listener count.
        [Component]
        private static VNode InnerSub()
        {
            var n = Hooks.UseStore(s_store, s => s.N);
            return V.Label(text: n.ToString());
        }

        // #1 — keyed host whose TYPE flips (Div -> Button) at the same key, forcing a CanPatch=false type-swap
        // that removes the old Div. The inline child lives ONLY in the Div branch, so the swap genuinely orphans
        // it (the new Button does not re-pair it) — the exact "inline fiber inside a torn-down host leaf" leak.
        [Component]
        private static VNode TypeSwapHost()
        {
            var (asButton, setAsButton) = Hooks.UseState(false);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "swap", key: "swap", onClick: () => setAsButton.Invoke(b => !b)),
                asButton
                    ? V.Button(key: "host", text: "swapped")
                    : V.Div(key: "host", children: new VNode[] { V.Component(InnerSub, key: "inner") }),
            });
        }

        [Test]
        public void Given_AnInlineChildInsideAKeyedHost_When_TheHostTypeSwapsAway_Then_TheChildsSubscriptionIsReleased()
        {
            // Arrange — a mounted host (a Div) with one inline child subscribed to the store.
            using var store = new CountStore();
            s_store = store;
            var root = new VisualElement();
            var baseline = StoreSubscriberCount(store);
            using var mounted = V.Mount(root, V.Component(TypeSwapHost, key: "ts"));
            Assume.That(StoreSubscriberCount(store), Is.EqualTo(baseline + 1),
                "Precondition: the inline child subscribed exactly once");

            // Act — the host type flips to a childless Button, removing the Div (and orphaning its inline child).
            root.Q<Button>("swap").SimulateClick();

            // Assert — no live subscription remains: the orphaned child was disposed. A leak would leave one behind.
            Assert.That(StoreSubscriberCount(store), Is.EqualTo(baseline),
                "The type-swapped-away host's inline child was disposed, releasing its subscription (no leak).");
        }

        // #2 — a Portal whose content nests the inline child inside a host element. Unmounting the Portal removes
        // the host from the Portal target via CleanupPortal, which (like every element removal) must dispose the
        // inline child anchored inside it.
        [Component]
        private static VNode PortalLeakHost()
        {
            var (show, setShow) = Hooks.UseState(true);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show
                    ? V.Portal("leak-target", children: new VNode[]
                        {
                            V.Div(children: new VNode[] { V.Component(InnerSub, key: "pinner") }),
                        })
                    : V.Fragment(Array.Empty<VNode>()),
            });
        }

        [Test]
        public void Given_AnInlineChildInsidePortalContent_When_ThePortalUnmounts_Then_ItsSubscriptionIsReleased()
        {
            // Arrange — a Portal whose content's inline child subscribed to the store.
            using var store = new CountStore();
            s_store = store;
            var target = new VisualElement();
            FiberPortalRegistry.Register("leak-target", target);
            try
            {
                var root = new VisualElement();
                var baseline = StoreSubscriberCount(store);
                using var mounted = V.Mount(root, V.Component(PortalLeakHost, key: "portal-host"));
                Assume.That(StoreSubscriberCount(store), Is.EqualTo(baseline + 1),
                    "Precondition: the portal content's inline child subscribed exactly once");

                // Act — the Portal is unmounted, tearing down its target content out of band.
                root.Q<Button>("toggle").SimulateClick();

                // Assert — the inline child under the removed Portal content was disposed (subscription released).
                Assert.That(StoreSubscriberCount(store), Is.EqualTo(baseline),
                    "Unmounting the Portal disposed its content's inline child (no leak).");
            }
            finally
            {
                FiberPortalRegistry.Clear();
            }
        }

        // #3 — a VirtualList whose item renderer nests the inline child inside a host Div. Scrolling an item out of
        // the visible window recycles its element via the controller (outside any reconcile pass), which must
        // dispose the inline child mounted under it.
        private readonly record struct Row(string Id);

        [Test]
        public void Given_InlineChildrenInVirtualListItems_When_ScrolledToADisjointWindow_Then_RecycledChildrenAreReleased()
        {
            // Arrange — a VirtualList showing its first window of items, each item nesting an inline subscriber.
            using var store = new CountStore();
            s_store = store;
            using var scope = new ReconcilerScope();
            var baseline = StoreSubscriberCount(store);
            var rows = new Row[100];
            for (var i = 0; i < rows.Length; i++) rows[i] = new Row("r" + i);
            var node = V.VirtualList(
                items: rows,
                keySelector: r => r.Id,
                itemHeight: 50f,
                renderer: r => V.Div(children: new VNode[] { V.Component(InnerSub, key: "body-" + r.Id) }),
                overscan: 0);
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            using var controller = new FiberVirtualListController(scrollView, node, scope.Reconciler);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            var firstWindow = StoreSubscriberCount(store);
            Assume.That(firstWindow, Is.GreaterThan(baseline), "Precondition: the first window's items subscribed");

            // Act — the viewport jumps to a fully disjoint same-size window (items 80..84), recycling 0..4 out.
            controller.UpdateVisibleRange(scrollY: 4000f, viewportHeight: 200f);

            // Assert — the recycled items' inline children were disposed, so the live count is one window, not two.
            Assert.That(StoreSubscriberCount(store), Is.EqualTo(firstWindow),
                "Recycled VirtualList items disposed their inline children (no accumulation across scroll).");
        }

        /// <summary>
        /// Reads the live listener count of a <see cref="Store{TState}"/> by reflecting its private
        /// <c>StoreStateNotifier</c> and that notifier's <c>_listeners</c> list — the only way to observe a leaked
        /// (never-released) UseStore subscription, since the count is otherwise encapsulated.
        /// </summary>
        private static int StoreSubscriberCount<TState>(Store<TState> store)
        {
            var stateField = typeof(Store<TState>).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find Store<T>._state. The internal layout may have changed.");
            var notifier = stateField.GetValue(store)
                ?? throw new InvalidOperationException("Store<T>._state was null.");
            var listenersField = notifier.GetType().GetField("_listeners", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Could not find StoreStateNotifier._listeners. The internal layout may have changed.");
            return ((System.Collections.ICollection)listenersField.GetValue(notifier)).Count;
        }
    }
}
