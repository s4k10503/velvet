using System;
using System.Collections.Generic;
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
    /// <item>Each produced node carries the key returned by the selector. The selector key is authoritative
    /// and overrides any key the renderer set on the node — the list-mapping site owns the identity.</item>
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
