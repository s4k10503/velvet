// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the anchor element a <see cref="V.VirtualList{T}"/> item mounts on takes part in the
    /// layout of the container the controller stacks its visible items in. A reconcile expands a
    /// <c>V.Component</c> and a <c>V.Provider</c> inline, so an item renderer returning one of those is the
    /// only way to reach that anchor.
    /// <list type="bullet">
    /// <item>Consecutive items start the list's item height apart rather than sharing one position, for a
    /// renderer returning a Component and for one returning a Provider.</item>
    /// <item>An item is as wide as the container it stacks in.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each case reads a resolved rect, which is why the fixture takes a real panel and acts through
    /// <see cref="PanelTestBase.ForcePanelUpdate"/>. The item bodies are sized with an arbitrary value, which
    /// resolves to an inline style and so needs no stylesheet attached.
    /// </remarks>
    [TestFixture]
    internal sealed class VirtualListItemMountTests : PanelTestBase
    {
        private const float ItemHeight = 40f;

        [Component]
        private static VNode ItemRender(string label) => V.Div(className: "h-[30px]", name: $"item-{label}");

        private static readonly ComponentContext<string> ItemContext = ComponentContext<string>.Create("default");

        [Test]
        public void Given_AVirtualListWhoseRendererReturnsAComponent_When_ItsItemsRender_Then_TheSecondStartsAnItemHeightBelowTheFirst()
        {
            // Arrange
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: ItemHeight,
                    renderer: item => V.Component(ItemRender, item, key: item),
                    name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("item-b").worldBound.yMin
                    - _window.rootVisualElement.Q<VisualElement>("item-a").worldBound.yMin,
                Is.EqualTo(ItemHeight).Within(0.01f));
        }

        [Test]
        public void Given_AVirtualListWhoseRendererReturnsAProvider_When_ItsItemsRender_Then_TheSecondStartsAnItemHeightBelowTheFirst()
        {
            // Arrange — the Provider is the other node kind the item path asks CreateElement for, and it
            // reaches a different factory arm than the Component above.
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: ItemHeight,
                    renderer: item => V.Provider(ItemContext, item,
                        children: new VNode[] { V.Div(className: "h-[30px]", name: $"item-{item}") }),
                    name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("item-b").worldBound.yMin
                    - _window.rootVisualElement.Q<VisualElement>("item-a").worldBound.yMin,
                Is.EqualTo(ItemHeight).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base already sizes an item to the container it stacks in.
        // It did that from insets of the anchor's own; the anchor has none now, so what holds the width
        // is that container's cross axis, and this case is what fails if it stops holding it.
        [Test]
        public void Given_AVirtualListWhoseRendererReturnsAComponent_When_ItsItemsRender_Then_AnItemIsAsWideAsTheContainerItStacksIn()
        {
            // Arrange
            var items = new List<string> { "a", "b", "c" };
            _mounted = V.Mount(_window.rootVisualElement,
                V.VirtualList(items, item => item, itemHeight: ItemHeight,
                    renderer: item => V.Component(ItemRender, item, key: item),
                    name: "vlist", className: "h-[200px]"));

            // Act
            ForcePanelUpdate(_window.rootVisualElement.panel);

            // Assert
            Assert.That(
                _window.rootVisualElement.Q<VisualElement>("item-a").worldBound.width,
                Is.EqualTo(_window.rootVisualElement.Q<ScrollView>("vlist").contentContainer.worldBound.width)
                    .Within(0.01f));
        }
    }
}
