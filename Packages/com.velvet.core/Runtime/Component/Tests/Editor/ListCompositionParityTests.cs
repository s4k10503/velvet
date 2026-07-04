using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a mapped list can be composed inline among sibling nodes in a single children list,
    /// so a header, the mapped items, and a footer sit together under one parent without a wrapper.
    /// <list type="bullet">
    /// <item><see cref="V.ListFragment{T}(IReadOnlyList{T}, System.Func{T, string}, System.Func{T, VNode}, string)"/>
    /// returns a single VNode that expands inline, so header, item0..itemN, footer land under one parent in
    /// declared order without an extra wrapper element.</item>
    /// <item>The existing <see cref="V.List{T}(IReadOnlyList{T}, System.Func{T, string}, System.Func{T, VNode})"/>
    /// usage as the sole children argument keeps materializing the items directly under the parent.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ListCompositionParityTests
    {
        private static readonly List<string> Items = new() { "i0", "i1", "i2" };

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

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
    }
}
