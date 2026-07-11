using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins slot-bound resolution for an inline fiber that a keyed reorder displaced. The fiber
    /// sibling chain is creation order and a reorder does not resync it, so the chain-next sibling
    /// can sit visually BEFORE the fiber; bounding the fiber's own reconcile by that sibling's slot
    /// start produced slotLimit &lt; slotStart, every slot in the fiber's range looked missing, and an
    /// independent re-render (its own setState — not a parent-driven render) inserted a brand-new
    /// element while the stale one stayed: a permanent duplicate no future reconcile removes. The
    /// bound must come from the nearest co-located slot start beyond the fiber's own, regardless of
    /// chain position.
    /// </summary>
    [TestFixture]
    internal sealed class InlineSiblingReorderRerenderTests
    {
        private readonly record struct OrderState(string Order);

        private sealed class OrderStore : Store<OrderState>
        {
            public OrderStore() : base(new OrderState("abc")) { }
            public void Set(string order) => SetState(_ => new OrderState(order));
            protected override void ResetCore() => SetState(_ => new OrderState("abc"));
        }

        private static OrderStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode RowA()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-a", text: "a" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode RowB()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-b", text: "b" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode RowC()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-c", text: "c" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode ReorderList()
        {
            var order = Hooks.UseStore(s_store, s => s.Order);
            var rows = new List<VNode>();
            foreach (var id in order)
            {
                rows.Add(id switch
                {
                    'a' => V.Component(RowA, key: "a"),
                    'b' => V.Component(RowB, key: "b"),
                    _ => V.Component(RowC, key: "c"),
                });
            }
            return V.Div(name: "container", children: rows.ToArray());
        }

        [Test]
        public void Given_KeyedInlineComponentsWereReordered_When_TheDisplacedFiberRerendersOnItsOwn_Then_NoDuplicateElementAppears()
        {
            // Arrange — mount [a,b,c], then reorder to [b,c,a] so fiber a's creation-order successor
            // (b) now sits visually before it.
            using var store = new OrderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ReorderList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("bca");
            scheduler.DrainImmediateForTest();
            var container = _root.Q<VisualElement>("container");
            Assume.That(container.childCount, Is.EqualTo(3), "Precondition: the reorder itself commits cleanly");

            // Act — fiber a re-renders on its own via its click handler (a discrete-event flush,
            // bypassing the parent).
            _root.Q<Button>("btn-a").SimulateClick();

            // Assert — the displaced fiber patched its own slot; no ghost duplicate was inserted.
            Assert.AreEqual(3, container.childCount);
        }

        [Test]
        public void Given_KeyedInlineComponentsWereReordered_When_TheDisplacedFiberRerendersOnItsOwn_Then_ItsOwnRowIsUpdatedInPlace()
        {
            // Arrange
            using var store = new OrderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ReorderList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("bca");
            scheduler.DrainImmediateForTest();

            // Act
            _root.Q<Button>("btn-a").SimulateClick();

            // Assert — exactly one btn-a exists and it carries the bumped state.
            var buttons = _root.Query<Button>(name: "btn-a").ToList();
            Assert.That((buttons.Count, buttons[0].text), Is.EqualTo((1, "a1")));
        }
    }
}
