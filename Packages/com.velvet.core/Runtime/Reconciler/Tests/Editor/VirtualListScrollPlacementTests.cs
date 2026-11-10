using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> range rendered from a scroll, outside any reconcile pass,
    /// places before the scroll returns: the children of each row that is a <c>V.Portal</c> mount in its target, in
    /// the rows' order, a Component row between them included.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListScrollPlacementTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode ComponentRow(string item) => V.Div(name: item);

        [Test]
        public void Given_VirtualListRowsThatArePortalsAroundAComponentRow_When_AScrollBringsThemIntoTheRange_Then_TheirChildrenMountInTheTarget()
        {
            // Arrange — the list renders no range until a scroll asks for one. "c" is a Component, whose render
            // outside a pass is a top-level pass of its own.
            var target = new VisualElement();
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c", "d" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item switch
                    {
                        "b" or "d" => V.Portal(target, new VNode[] { V.Label(text: item, name: item) }),
                        "c" => V.Component(ComponentRow, item, key: item),
                        _ => V.Div(name: item),
                    },
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", target.Children().Select(child => child.name)), Is.EqualTo("b,d"));
        }
    }

    /// <summary>
    /// Specifies where the ring band of a <see cref="V.VirtualList{T}"/> row stands once a range rendered from a scroll
    /// has placed the rows again: directly after its row, for a row in its slot and for one a <c>z-*</c> class
    /// relocated.
    /// </summary>
    /// <remarks>
    /// A row put back in the list has its band put beside it by its attach callback, which only a panel raises, so the
    /// fixture takes a real one. <c>ring-2</c> is realised from C#, so no stylesheet is attached.
    /// </remarks>
    [TestFixture]
    internal sealed class VirtualListRowBandPanelTests : PanelTestBase
    {
        private VisualElement MountRows(string bClasses)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Div(name: item, className: item == "b" ? $"ring-2 {bClasses}" : "ring-2"),
                overscan: 0,
                name: "vlist",
                className: "h-[200px]"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return _window.rootVisualElement.Q<ScrollView>("vlist").contentContainer.ElementAt(1);
        }

        private void ScrollToTheFirstTwoRows()
        {
            var scrollView = _window.rootVisualElement.Q<ScrollView>("vlist");
            _mounted.Root.Reconciler.Context.VirtualListControllers[scrollView].UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        // Physical index of the row's ring band less the row's own: 1 where the band stands directly after it.
        private int BandOffset(VisualElement rows, string name)
        {
            var row = _window.rootVisualElement.Q<VisualElement>(name);
            return row == null || !_mounted.Root.Reconciler.Context.RingBindings.TryGetValue(row, out var ring)
                ? int.MinValue
                : rows.IndexOf(ring.Overlay) - rows.IndexOf(row);
        }

        // GREEN_ON_BASE(characterization): the base empties the row container and adds the rows back at its end.
        // Each band follows its row there, since the row's attach puts the band directly after it; inserting each
        // row back at its logical slot instead puts it ahead of the band the row before it left there.
        [Test]
        public void Given_RingedVirtualListRows_When_AScrollRendersTheRangeAgain_Then_TheFirstRowsBandStandsDirectlyAfterIt()
        {
            // Arrange
            var rows = MountRows("");

            // Act
            ScrollToTheFirstTwoRows();

            // Assert
            Assert.That(BandOffset(rows, "a"), Is.EqualTo(1));
        }

        [Test]
        public void Given_ARingedVirtualListRowWithAZIndex_When_AScrollRendersTheRangeAgain_Then_ItsBandStandsDirectlyAfterIt()
        {
            // Arrange
            var rows = MountRows("relative z-10");

            // Act
            ScrollToTheFirstTwoRows();

            // Assert
            Assert.That(BandOffset(rows, "b"), Is.EqualTo(1));
        }
    }
}
