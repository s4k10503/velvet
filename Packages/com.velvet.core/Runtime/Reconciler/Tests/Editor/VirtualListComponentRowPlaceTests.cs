using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> component row's own render costs the list's structural variants:
    /// where the row renders as many elements as before, they are matched where the list's last pass placed them, and
    /// no pass over the list runs again to place its other rows, whichever row renders.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListComponentRowPlaceTests
    {
        private VisualElement _root;
        private static StateUpdater<int> s_renderRowA;
        private static StateUpdater<int> s_renderRowB;

        [SetUp]
        public void SetUp()
        {
            (_root, s_renderRowA, s_renderRowB) = (new VisualElement(), default, default);
        }

        [Component(Compiler = false)]
        private static VNode ComponentRow(string item)
        {
            var (_, setRender) = Hooks.UseState(0);
            if (item == "a") s_renderRowA = setRender;
            if (item == "b") s_renderRowB = setRender;
            return V.Div(name: item, className: "first:bg-mark");
        }

        [Component(Compiler = false)]
        private static VNode ListHost()
            => V.VirtualList(
                items: new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Component(ComponentRow, item, key: item),
                overscan: 0);

        // Renders a range of "a" and "b" outside any pass, and returns the place it gave item's anchor.
        private ItemMountPlace PlaceAfterARange(MountedTree mounted, string item)
        {
            mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);
            return (ItemMountPlace)_root.Q<VisualElement>(item).parent.userData;
        }

        [Test]
        public void Given_ComponentRowsARangePlaced_When_TheFirstRendersAgainAsManyElements_Then_NoPassPlacesTheSecondAgain()
        {
            // Arrange — "b"'s anchor is given a total no pass over the list writes.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var placeOfB = PlaceAfterARange(mounted, "b");
            placeOfB.Total = -1;

            // Act — "a"'s own component renders again, into the anchor it stands in.
            s_renderRowA.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(placeOfB.Total, Is.EqualTo(-1));
        }

        [Test]
        public void Given_ComponentRowsARangePlaced_When_TheSecondRendersAgainAsManyElements_Then_NoPassPlacesTheFirstAgain()
        {
            // Arrange — "a"'s anchor is given a total no pass over the list writes.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var placeOfA = PlaceAfterARange(mounted, "a");
            placeOfA.Total = -1;

            // Act — "b"'s own component renders again, into the anchor it stands in.
            s_renderRowB.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(placeOfA.Total, Is.EqualTo(-1));
        }
    }
}
