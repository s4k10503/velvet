using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.List{T}"/>, which projects a source collection into a keyed
    /// VNode array, and its inline-composition sibling <see cref="V.ListFragment{T}(IReadOnlyList{T}, System.Func{T, string}, System.Func{T, VNode}, string)"/>.
    /// <list type="bullet">
    /// <item>A null or empty source yields an empty array.</item>
    /// <item>A non-empty source yields one slot per item, in source order.</item>
    /// <item>Each produced node carries the key returned by the selector. A node the renderer has just built
    /// is keyed in place; a node a list has placed before takes a copy when a later item's key differs.</item>
    /// <item>A node a list has placed before keeps the key it was placed under, and the slot of an item
    /// whose key differs takes a copy carrying that key — so one node returned for several items, or held
    /// across renders, gives each item keyed apart a row of its own. A node the renderer has just built is
    /// the one its slot holds, and a copy refuses a key holding the scope delimiter as the key setter
    /// does.</item>
    /// <item>A null result from the renderer is preserved as a null slot rather than dropped, so downstream
    /// reconciliation owns the null-filtering decision.</item>
    /// <item>The indexed overload passes the zero-based position to both the selector and the renderer, in
    /// ascending order.</item>
    /// <item><c>V.ListFragment</c> returns a single VNode that expands inline, so a header, the mapped items,
    /// and a footer land under one parent in declared order without an extra wrapper element; <c>V.List</c>
    /// used as the sole children argument keeps materializing its items directly under the parent.</item>
    /// </list>
    /// This fixture is the sole owner of the V.List / V.ListFragment contract; VNodeBuilderTests intentionally
    /// carries no V.List cases.
    /// </summary>
    [TestFixture]
    internal sealed class VListTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        #region Single-argument renderer

        [Test]
        public void Given_NullItems_When_Listed_Then_ReturnsEmpty()
        {
            // Act
            var result = V.List<string>(null, s => s, s => V.Label(text: s));

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_EmptyItems_When_Listed_Then_ReturnsEmpty()
        {
            // Act
            var result = V.List(Array.Empty<string>(), s => s, s => V.Label(text: s));

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_Items_When_Listed_Then_ProducesOneSlotPerItem()
        {
            // Arrange
            var items = new List<string> { "A", "B", "C" };

            // Act
            var result = V.List(items, s => s, s => V.Label(text: s));

            // Assert
            Assert.That(result.Length, Is.EqualTo(3));
        }

        [Test]
        public void Given_Items_When_Listed_Then_EachSlotCarriesSelectorKey()
        {
            // Arrange
            var items = new List<string> { "alpha", "beta" };

            // Act
            var result = V.List(items, s => $"key-{s}", s => V.Label(text: s));

            // Assert
            CollectionAssert.AreEqual(new[] { "key-alpha", "key-beta" }, new[] { result[0].Key, result[1].Key });
        }

        [Test]
        public void Given_RendererSetKey_When_Listed_Then_SelectorKeyOverridesIt()
        {
            // Arrange
            var items = new List<string> { "item" };

            // Act
            var result = V.List(items, _ => "auto-key", s => V.Label(text: s, key: "inner-key"));

            // Assert
            Assert.That(result[0].Key, Is.EqualTo("auto-key"));
        }

        #endregion

        #region Indexed renderer

        [Test]
        public void Given_NullItems_When_ListedIndexed_Then_ReturnsEmpty()
        {
            // Act
            var result = V.List<string>(null, (_, i) => i.ToString(), (_, _) => V.Label());

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_EmptyItems_When_ListedIndexed_Then_ReturnsEmpty()
        {
            // Act
            var result = V.List(Array.Empty<string>(), (_, i) => i.ToString(), (_, _) => V.Label());

            // Assert
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_Items_When_ListedIndexed_Then_ProducesOneSlotPerItem()
        {
            // Arrange
            var items = new List<int> { 10, 20, 30, 40 };

            // Act
            var result = V.List(items, (_, i) => i.ToString(), (val, i) => V.Label(text: $"{i}:{val}"));

            // Assert
            Assert.That(result.Length, Is.EqualTo(4));
        }

        [Test]
        public void Given_Items_When_ListedIndexed_Then_RendererReceivesAscendingIndices()
        {
            // Arrange
            var capturedIndices = new List<int>();
            var items = new List<string> { "A", "B", "C" };

            // Act
            V.List(items, (_, i) => i.ToString(), (_, i) =>
            {
                capturedIndices.Add(i);
                return V.Label();
            });

            // Assert
            Assert.That(capturedIndices, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Given_Items_When_ListedIndexed_Then_SelectorReceivesIndexForKey()
        {
            // Arrange
            var items = new List<string> { "x", "y" };

            // Act
            var result = V.List(items, (s, i) => $"{s}-{i}", (_, _) => V.Label());

            // Assert
            CollectionAssert.AreEqual(new[] { "x-0", "y-1" }, new[] { result[0].Key, result[1].Key });
        }

        [Test]
        public void Given_RendererSetKey_When_ListedIndexed_Then_SelectorKeyOverridesIt()
        {
            // Arrange
            var items = new List<string> { "item" };

            // Act
            var result = V.List(items, (_, i) => $"auto-{i}", (_, _) => V.Label(key: "inner-key"));

            // Assert
            Assert.That(result[0].Key, Is.EqualTo("auto-0"));
        }

        [Test]
        public void Given_RendererReturnsNullForAnItem_When_ListedIndexed_Then_NullSlotIsPreserved()
        {
            // Arrange
            var items = new List<int> { 1, 2 };

            // Act
            var result = V.List(items, (_, i) => i.ToString(), (val, _) => val == 1 ? null : V.Label(text: "2"));

            // Assert
            Assert.That(result[0], Is.Null);
            Assume.That(result[1], Is.Not.Null, "Precondition: the non-null item still produced a node");
        }

        #endregion

        #region A node a list has placed before

        [Test]
        public void Given_ARendererReturningOneNodeForEveryItem_When_Listed_Then_EachSlotCarriesItsOwnItemsKey()
        {
            // Arrange
            var shared = V.Label(text: "shared");
            var items = new List<string> { "a", "b", "c" };

            // Act
            var result = V.List(items, s => "key-" + s, _ => shared);

            // Assert
            Assert.That(string.Join(",", result.Select(node => node.Key)), Is.EqualTo("key-a,key-b,key-c"));
        }

        [Test]
        public void Given_ARendererReturningOneNodeForEveryItem_When_ListedIndexed_Then_EachSlotCarriesItsOwnItemsKey()
        {
            // Arrange
            var shared = V.Label(text: "shared");
            var items = new List<string> { "a", "b", "c" };

            // Act
            var result = V.List(items, (s, i) => s + "-" + i, (_, _) => shared);

            // Assert
            Assert.That(string.Join(",", result.Select(node => node.Key)), Is.EqualTo("a-0,b-1,c-2"));
        }

        // GREEN_ON_BASE(characterization): the base keys in place a node the renderer has just built.
        // That holds for a key the renderer set on it too, and keeping the first key of a node a list placed
        // before must not change it.
        [Test]
        public void Given_ARendererBuildingANodePerItem_When_Listed_Then_EachSlotHoldsTheNodeItBuilt()
        {
            // Arrange — keyed by the renderer as well, so what the selector's key replaces is a key the node
            // arrived with.
            var built = new List<VNode>();
            var items = new List<string> { "a", "b" };

            // Act
            var result = V.List(items, s => "key-" + s, s =>
            {
                var node = V.Label(text: s, key: "inner-" + s);
                built.Add(node);
                return node;
            });

            // Assert
            Assert.That(result, Is.EqualTo(built));
        }

        // GREEN_ON_BASE(characterization): the base re-keys a node in place whatever key it held.
        // Keeping the first key of a node a list placed before must still hand that node back when its key
        // has not moved, which is what keeps a hoisted node the same node across renders.
        [Test]
        public void Given_ANodeAListPlacedBefore_When_ListedAgainUnderTheSameKey_Then_TheSlotHoldsThatNode()
        {
            // Arrange
            var held = V.Label(text: "held");
            var items = new List<string> { "a" };
            V.List(items, s => "key-" + s, _ => held);

            // Act
            var result = V.List(items, s => "key-" + s, _ => held);

            // Assert
            Assert.That(result[0], Is.SameAs(held));
        }

        // GREEN_ON_BASE(characterization): the base refuses the delimiter through the key setter.
        // Every placement reached that setter there; a copy under a new key is built without it, and this is
        // what fails when the copy stops refusing the delimiter.
        [Test]
        public void Given_ANodeAListPlacedBefore_When_ListedUnderAKeyHoldingTheDelimiter_Then_ItIsRefused()
        {
            // Arrange
            var held = V.Label(text: "held");
            var items = new List<string> { "a" };
            V.List(items, s => "key-" + s, _ => held);

            // Act + Assert
            Assert.Throws<ArgumentException>(() => V.List(items, _ => "a\0b", _ => held));
        }

        private static VNode s_heldNode;
        private static StateUpdater<int> s_setHeldItem;
        private static int s_rowMounts;
        private static int s_rowUnmounts;
        private static int s_refSetUps;
        private static int s_refCleanUps;

        private static void ResetHeldNodeRecords(VNode held)
        {
            s_heldNode = held;
            s_setHeldItem = default;
            s_rowMounts = 0;
            s_rowUnmounts = 0;
            s_refSetUps = 0;
            s_refCleanUps = 0;
        }

        private static Action CountRefSetUp(VisualElement _)
        {
            s_refSetUps++;
            return () => s_refCleanUps++;
        }

        [Component(Compiler = false)]
        private static VNode CountedRowRender()
        {
            Hooks.UseEffect(() =>
            {
                s_rowMounts++;
                return () => s_rowUnmounts++;
            }, Array.Empty<object>());
            return V.Label(text: "row");
        }

        [Component(Compiler = false)]
        private static VNode HeldNodeForEveryItemHostRender()
            => V.Div(name: "list", children: V.List(Items, s => "key-" + s, _ => s_heldNode));

        // One item, whose key the Act moves on.
        [Component(Compiler = false)]
        private static VNode HeldNodeForOneItemHostRender()
        {
            var (item, setItem) = Hooks.UseState(0);
            s_setHeldItem = setItem;
            return V.Div(name: "list", children: V.List(new[] { item }, i => "key-" + i, _ => s_heldNode));
        }

        [Test]
        public void Given_ARendererReturningOneComponentNodeForEveryItem_When_TheListMounts_Then_EachItemHasARowOfItsOwn()
        {
            // Arrange
            ResetHeldNodeRecords(V.Component(CountedRowRender));

            // Act
            using var mounted = V.Mount(_root, V.Component(HeldNodeForEveryItemHostRender, key: "host"));

            // Assert
            Assert.That(_root.Q("list").childCount, Is.EqualTo(Items.Count));
        }

        [Test]
        public void Given_AComponentNodeHeldAcrossRenders_When_ItsItemsKeyMovesOn_Then_TheRowItLeftIsUnmounted()
        {
            // Arrange
            ResetHeldNodeRecords(V.Component(CountedRowRender));
            using var mounted = V.Mount(_root, V.Component(HeldNodeForOneItemHostRender, key: "host"));
            mounted.FlushEffectsForTest();

            // Act
            s_setHeldItem.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the row count rides along: a row left in the list beside its successor is the row the
            // unmount count misses.
            Assert.That(
                _root.Q("list").childCount + " row(s), " + s_rowMounts + " mounted, " + s_rowUnmounts + " unmounted",
                Is.EqualTo("1 row(s), 2 mounted, 1 unmounted"));
        }

        [Test]
        public void Given_AnElementNodeHeldAcrossRenders_When_ItsItemsKeyMovesOn_Then_ItsRowIsBuiltAgain()
        {
            // Arrange — a key is identity, so an item whose key moves on is a different row, as it is in React.
            ResetHeldNodeRecords(V.Label(text: "held", refCallback: CountRefSetUp));
            using var mounted = V.Mount(_root, V.Component(HeldNodeForOneItemHostRender, key: "host"));

            // Act
            s_setHeldItem.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_refSetUps + " set up, " + s_refCleanUps + " cleaned up", Is.EqualTo("2 set up, 1 cleaned up"));
        }

        // GREEN_ON_BASE(characterization): the base re-keys the held node in place and copies nothing.
        // No second node shares its props bag there; a copy under the new key does, and this is what fails
        // when the tree still holding the held node retires and hands that bag back to the pool.
        [Test]
        public void Given_AnElementNodeHeldAcrossRenders_When_ItsItemsKeyMovesOnAndTheOldTreeRetires_Then_ItKeepsItsProps()
        {
            // Arrange — the props pool starts empty, so a bag handed back to it is cleared rather than dropped
            // for want of room.
            var held = V.Label(text: "held");
            ResetHeldNodeRecords(held);
            using var mounted = V.Mount(_root, V.Component(HeldNodeForOneItemHostRender, key: "host"));
            var propsPool = (Stack<FiberElementProps>)typeof(VNodePool)
                .GetField("s_propsPool", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            propsPool.Clear();

            // Act
            s_setHeldItem.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(held.Props.Text, Is.EqualTo("held"));
        }

        // GREEN_ON_BASE(characterization): the base re-keys the held node in place and copies nothing.
        // No second node shares its event array there; a copy under the new key does, and this is what
        // fails when the tree still holding the held node retires and hands that array back to the pool.
        [Test]
        public void Given_AnElementNodeHeldAcrossRenders_When_ItsItemsKeyMovesOnAndTheOldTreeRetires_Then_ItKeepsItsEvents()
        {
            // Arrange — the props case above with a click handler, whose single-event array the pool rents; the
            // pool starts empty for the reason that case gives.
            var held = V.Button(text: "held", onClick: () => { });
            var binding = held.Events[0];
            ResetHeldNodeRecords(held);
            using var mounted = V.Mount(_root, V.Component(HeldNodeForOneItemHostRender, key: "host"));
            var eventPool = (Stack<FiberEventBinding[]>)typeof(VNodePool)
                .GetField("s_singleEventPool", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            eventPool.Clear();

            // Act
            s_setHeldItem.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(held.Events[0], Is.SameAs(binding));
        }

        #endregion

        #region Inline composition (V.ListFragment among siblings)

        private static readonly List<string> Items = new() { "i0", "i1", "i2" };

        [Test]
        public void Given_ListFragmentAmongSiblings_When_Mounted_Then_HeaderItemsFooterInOrder()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SiblingListHostRender, key: "host"));
            mounted.FlushStateForTest();

            // Assert
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the host renders a single container");
            var container = _root.ElementAt(0);
            Assume.That(container.childCount, Is.EqualTo(Items.Count + 2),
                "Precondition: header + every item + footer land directly under the container, no extra wrapper");
            var texts = new List<string>();
            for (var i = 0; i < container.childCount; i++)
            {
                texts.Add(((Label)container.ElementAt(i)).text);
            }
            Assert.That(texts, Is.EqualTo(new[] { "Header", "i0", "i1", "i2", "Footer" }),
                "The list expands inline among its siblings in declared order");
        }

        [Test]
        public void Given_ListAsSoleChildrenArgument_When_Mounted_Then_ItemsMaterializeUnderParent()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(SoleListHostRender, key: "host"));
            mounted.FlushStateForTest();

            // Assert
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: the host renders a single container");
            var container = _root.ElementAt(0);
            Assert.That(container.childCount, Is.EqualTo(Items.Count),
                "V.List as the sole children argument still spreads each item under the container");
        }

        [Component]
        private static VNode SiblingListHostRender()
            => V.Div("c",
                V.Label(text: "Header"),
                V.ListFragment(Items, s => s, s => V.Label(text: s)),
                V.Label(text: "Footer"));

        [Component]
        private static VNode SoleListHostRender()
            => V.Div("c", V.List(Items, s => s, s => V.Label(text: s)));

        #endregion
    }
}
