using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a horizontal <c>V.VirtualList</c> lays out on a real panel.
    /// <list type="bullet">
    /// <item>Its range covers the viewport's width.</item>
    /// <item>Consecutive items start an item's width apart along one row.</item>
    /// <item>Its horizontal scroller reaches as far as the items' total width less the viewport's.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The list is sized with arbitrary values, which resolve to inline styles and so need no stylesheet.
    /// </remarks>
    [TestFixture]
    internal sealed class VirtualListHorizontalPanelTests : PanelTestBase
    {
        private const float ItemWidth = 40f;
        private static readonly string[] Items = Enumerable.Range(0, 100).Select(i => "item-" + i).ToArray();
        private StateUpdater<bool> _setHorizontal;

        private static VirtualListNode List(bool horizontal)
            => V.VirtualList(Items, item => item, itemHeight: ItemWidth,
                renderer: item => V.Div(name: item), overscan: 0,
                name: "hlist", className: "w-[200px] h-[100px]", horizontal: horizontal);

        private VNode RenderAxisList()
        {
            var (horizontal, setHorizontal) = Hooks.UseState(false);
            _setHorizontal = setHorizontal;
            return List(horizontal);
        }

        private ScrollView MountAndLayOut(bool reRenderFromVertical = false)
        {
            var tree = reRenderFromVertical ? V.Component(RenderAxisList) : (VNode)List(horizontal: true);
            _mounted = V.Mount(_window.rootVisualElement, tree);
            var scrollView = _window.rootVisualElement.Q<ScrollView>("hlist");
            if (reRenderFromVertical)
            {
                scrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            }
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return scrollView;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AHorizontalList_When_ItIsLaidOut_Then_ItsRangeCoversTheViewportsWidth(bool reRenderFromVertical)
        {
            // Arrange
            var scrollView = MountAndLayOut(reRenderFromVertical);

            // Act
            if (reRenderFromVertical)
            {
                _setHorizontal.Invoke(true);
                // Read the immediate re-render before another geometry phase can repair an old-axis measurement.
                _mounted.FlushStateForTest();
            }

            // Assert — the viewport is read beside the count, since a viewport the theme sized otherwise
            // would change the count with nothing about the axis measured.
            Assert.That((scrollView.contentViewport.layout.width, scrollView.contentContainer.ElementAt(1).childCount),
                Is.EqualTo((200f, 5)));
        }

        [Test]
        public void Given_AHorizontalList_When_ItIsLaidOut_Then_TheSecondItemStartsAnItemWidthRightOfTheFirst()
        {
            // Act
            var scrollView = MountAndLayOut();

            // Assert
            var first = scrollView.Q<VisualElement>("item-0").worldBound;
            var second = scrollView.Q<VisualElement>("item-1").worldBound;
            Assert.That((second.xMin - first.xMin, second.yMin - first.yMin), Is.EqualTo((ItemWidth, 0f)));
        }

        [Test]
        public void Given_AHorizontalList_When_ItIsLaidOut_Then_ItsScrollerReachesTheLastItem()
        {
            // Act
            var scrollView = MountAndLayOut();

            // Assert
            Assert.That(scrollView.horizontalScroller.highValue, Is.EqualTo(100 * ItemWidth - 200f));
        }
    }
}
