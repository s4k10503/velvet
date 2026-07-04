using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.List{T}"/>, which projects a source collection into a keyed
    /// VNode array.
    /// <list type="bullet">
    /// <item>A null or empty source yields an empty array.</item>
    /// <item>A non-empty source yields one slot per item, in source order.</item>
    /// <item>Each produced node carries the key returned by the selector. The selector key is authoritative
    /// and overrides any key the renderer set on the node — the list-mapping site owns the identity.</item>
    /// <item>A null result from the renderer is preserved as a null slot rather than dropped, so downstream
    /// reconciliation owns the null-filtering decision.</item>
    /// <item>The indexed overload passes the zero-based position to both the selector and the renderer, in
    /// ascending order.</item>
    /// </list>
    /// This fixture is the sole owner of the V.List contract; VNodeBuilderTests intentionally carries no
    /// V.List cases.
    /// </summary>
    [TestFixture]
    internal sealed class VListTests
    {
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
    }
}
