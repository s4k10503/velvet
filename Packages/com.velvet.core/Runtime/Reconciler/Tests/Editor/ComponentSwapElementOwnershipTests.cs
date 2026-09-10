// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a host element belongs to the component whose output emitted it, so a slot whose
    /// component changes takes the element with it — the DOM half of the remount
    /// <c>Documentation~/react-migration.md</c> states for hook state.
    /// <list type="bullet">
    /// <item>Two components at one slot, each rendering a plain container, do not share that container: the
    /// arriving one's body is all its container holds, including where the departing one's body held an
    /// <c>V.AnimatePresence</c> whose committed children the arriving one never declared.</item>
    /// <item>The same holds where the arriving side is written inline rather than as a component, which
    /// leaves the container's new children a flat list of host leaves.</item>
    /// <item>A reorder inside a <c>V.List</c> of components is such a slot too: a component at a scope-less
    /// position opens no key scope, so the leaves it emits reconcile by sibling index and a moved row lands
    /// on a leaf a different component emitted. Wrapping the list in a keyed <c>V.ListFragment</c>
    /// establishes that scope, and the row's element then moves with the item; both readings are here,
    /// because the second is the edit the CHANGELOG offers for the first.</item>
    /// </list>
    /// The other direction — a component re-rendering itself keeps the element it emitted — is why the
    /// reading here is a term about the component instance rather than about the slot.
    /// <see cref="ComponentContainerIdentityTests"/> owns the other direction of the same pairing — which
    /// instance a container holds — and <see cref="AnimatePresenceStateRetirementTests"/> owns when a
    /// presence's own bookkeeping is retired.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentSwapElementOwnershipTests
    {
        private VisualElement _root = null!;
        private static SwapStore s_swap;
        private static ItemsStore s_items;
        private static readonly string[] Rows = { "a", "b", "c" };
        private static readonly string[] Ordered = { "a", "b" };
        private static readonly string[] Reordered = { "b", "a" };

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            s_swap?.Dispose();
            s_swap = null;
            s_items?.Dispose();
            s_items = null;
            _root = null!;
        }

        private readonly record struct SwapState(bool First);

        private sealed class SwapStore : Store<SwapState>
        {
            public SwapStore() : base(new SwapState(true)) { }
            public void ShowSecond() => SetState(_ => new SwapState(false));
            protected override void ResetCore() => SetState(_ => new SwapState(true));
        }

        private readonly record struct ItemsState(string[] Items);

        private sealed class ItemsStore : Store<ItemsState>
        {
            public ItemsStore() : base(new ItemsState(Ordered)) { }
            public void Reorder() => SetState(_ => new ItemsState(Reordered));
            protected override void ResetCore() => SetState(_ => new ItemsState(Ordered));
        }

        [Test]
        public void Given_TwoComponentsAtOneSlot_When_TheDepartingOneHeldAPresence_Then_ItsChildrenLeaveWithIt()
        {
            // Arrange — the departing body's rows are the presence's committed children, which only its own
            // boundary can reproduce for a diff; the arriving body declares none of them.
            s_swap = new SwapStore();
            using var mounted = V.Mount(_root, V.Component(SwapHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var rowsBefore = host.ElementAt(0).childCount;

            // Act
            s_swap.ShowSecond();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the row count before rides along, since an arrangement that mounted no rows would
            // satisfy the arriving container's own count while saying nothing about a departure.
            Assert.That(
                (rowsBefore, host.ElementAt(0).name, host.ElementAt(0).childCount),
                Is.EqualTo((3, "second", 1)),
                "The arriving component's body is all its container holds");
        }

        [Test]
        public void Given_AComponentReplacedByAnInlineElement_When_TheNewChildrenAreFlat_Then_ItsChildrenLeaveWithIt()
        {
            // Arrange — the arriving side is a host leaf rather than a component, so the container's new
            // children need no inline expansion and the path selection is what has to route them.
            s_swap = new SwapStore();
            using var mounted = V.Mount(_root, V.Component(InlineArrivalHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var rowsBefore = host.ElementAt(0).childCount;

            // Act
            s_swap.ShowSecond();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(
                (rowsBefore, host.ElementAt(0).name, host.ElementAt(0).childCount),
                Is.EqualTo((3, "second", 1)),
                "The inline arrival's own children are all its container holds");
        }

        [Test]
        public void Given_AKeyedListOfComponentsInAContainer_When_TheItemsAreReordered_Then_EachMovedRowIsRebuiltAroundItsOwnInstance()
        {
            // Arrange — V.List stamps the item key on each ComponentNode, but the list is written straight
            // into the container, so nothing above the components establishes a key scope and the leaves
            // they emit reconcile by sibling index. The first row's state is marked so the reading can
            // separate an element rebuilt around the surviving instance from the whole row remounting.
            s_items = new ItemsStore();
            using var mounted = V.Mount(_root, V.Component(ListHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            _root.Q<Button>("mark-a").SimulateClick();
            var before = new[] { host.ElementAt(0), host.ElementAt(1) };

            // Act
            s_items.Reorder();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — one comparison over what the reorder costs and what it does not. The names ride
            // along because a pass that rebuilt both rows without reordering them satisfies the count on
            // its own.
            Assert.That(
                (host.ElementAt(0).name + "," + host.ElementAt(1).name,
                    _root.Q<Button>("mark-a").text,
                    new[] { host.ElementAt(0), host.ElementAt(1) }.Count(before.Contains)),
                Is.EqualTo(("row-b,row-a", "a!", 0)),
                "A moved row is rebuilt at its new slot rather than carried to it, around its own instance");
        }

        // GREEN_ON_BASE(characterization): a keyed Fragment already scoped its rows' effective keys.
        // Their elements already moved with the items; the case is here because it is what a caller
        // writes instead of the arrangement above, and nothing pinned that it still works.
        [Test]
        public void Given_AKeyedListFragmentOfComponents_When_TheItemsAreReordered_Then_EachRowsElementMovesWithIt()
        {
            // Arrange — the one difference from the case above: the keyed Fragment establishes the scope
            // its rows' leaves take an effective key from.
            s_items = new ItemsStore();
            using var mounted = V.Mount(_root, V.Component(ListFragmentHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var before = new[] { host.ElementAt(0), host.ElementAt(1) };

            // Act
            s_items.Reorder();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(
                (host.ElementAt(0).name + "," + host.ElementAt(1).name,
                    ReferenceEquals(host.ElementAt(0), before[1]),
                    ReferenceEquals(host.ElementAt(1), before[0])),
                Is.EqualTo(("row-b,row-a", true, true)),
                "Each row keeps its element and moves it to the item's new slot");
        }

        #region Render targets

        [Component]
        private static VNode SwapHostRender()
        {
            var first = Hooks.UseStore(s_swap, state => state.First);
            return V.Div(name: "host", children: new VNode[]
            {
                first ? V.Component(PresenceBodyRender) : V.Component(PlainBodyRender),
            });
        }

        [Component]
        private static VNode PresenceBodyRender()
            => V.Div(name: "first", children: new VNode[]
            {
                V.AnimatePresence(children: V.List(Rows, row => row, row => V.Motion(
                    name: "row-" + row, children: new VNode[] { V.Label(text: row) }))),
            });

        [Component]
        private static VNode InlineArrivalHostRender()
        {
            var first = Hooks.UseStore(s_swap, state => state.First);
            return V.Div(name: "host", children: first
                ? new VNode[] { V.Component(PresenceBodyRender) }
                : new VNode[] { V.Div(name: "second", children: new VNode[] { V.Label(text: "only") }) });
        }

        [Component]
        private static VNode PlainBodyRender()
            => V.Div(name: "second", children: new VNode[] { V.Label(text: "only", name: "only") });

        [Component]
        private static VNode ListHostRender()
        {
            var items = Hooks.UseStore(s_items, state => state.Items);
            return V.Div(name: "host", children: V.List(items, item => item, item => V.Component(RowRender, item)));
        }

        [Component]
        private static VNode ListFragmentHostRender()
        {
            var items = Hooks.UseStore(s_items, state => state.Items);
            return V.Div(name: "host", children: new VNode[]
            {
                V.ListFragment(items, item => item, item => V.Component(RowRender, item), key: "rows"),
            });
        }

        [Component]
        private static VNode RowRender(string item)
        {
            var (mark, setMark) = Hooks.UseState(item);
            return V.Div(name: "row-" + item, children: new VNode[]
            {
                V.Button(name: "mark-" + item, text: mark, onClick: () => setMark.Invoke(m => m + "!")),
            });
        }

        #endregion
    }
}
