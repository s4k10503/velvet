using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a <see cref="V.VirtualList{T}"/> row that stacks by a <c>z-*</c> class sits among the rows the
    /// list places: FiberZLayerCoordinator relocates it within the list's row container as it relocates an element
    /// within its parent, and the list keeps it there across the ranges it renders.
    /// <list type="bullet">
    /// <item>A row a render gives a z, and a row created with one, paints in front of the rows after it, and keeps
    /// doing so when the list renders the range again. A row a scroll brings into the range paints where its z puts
    /// it, though no pass renders that range. A row a render takes the z away from paints in its slot again.</item>
    /// <item>No placeholder is kept for a row a render gave a z once it leaves the list.</item>
    /// <item>A relocated row takes the list's item length, and hands it back when the list turns to the other
    /// axis.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListZLayerTests
    {
        private const float ItemHeight = 10f;

        private VisualElement _root;
        private static string[] s_items;
        private static string s_bClasses;
        private static bool s_horizontal;
        private static StateUpdater<int> s_render;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            (s_items, s_bClasses, s_horizontal, s_render) = (new[] { "a", "b", "c" }, "", false, default);
        }

        [Component]
        private static VNode ListHost()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_render = setRender;
            return V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: s_items,
                    keySelector: item => item,
                    itemHeight: ItemHeight,
                    renderer: item => V.Div(name: item, className: item == "b" ? s_bClasses : null),
                    horizontal: s_horizontal,
                    overscan: 0,
                    key: "list"),
            });
        }

        // Mounts the host with the viewport height a geometry pass would have left, so that each render of the host
        // renders the list's range inside its pass.
        private MountedTree Mount()
        {
            var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()], 200f);
            return mounted;
        }

        private static void Render(MountedTree mounted)
        {
            s_render.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private VisualElement Rows => _root.Q<ScrollView>().contentContainer.ElementAt(1);

        [Test]
        public void Given_AVirtualListRowARenderGivesAZIndex_When_TheListRendersIt_Then_ItPaintsInFrontOfTheRowsAfterIt()
        {
            // Arrange
            using var mounted = Mount();
            Render(mounted);

            // Act
            s_bClasses = "relative z-10";
            Render(mounted);

            // Assert
            Assert.That(ZBandProbe.PaintOrder(Rows), Is.EqualTo("a,c,b"));
        }

        [Test]
        public void Given_AVirtualListRowCreatedWithAZIndex_When_TheListRendersTheRangeAgain_Then_ItStillPaintsInFrontOfTheRowsAfterIt()
        {
            // Arrange
            s_bClasses = "relative z-10";
            using var mounted = Mount();
            Render(mounted);

            // Act
            Render(mounted);

            // Assert
            Assert.That(ZBandProbe.PaintOrder(Rows), Is.EqualTo("a,c,b"));
        }

        [Test]
        public void Given_AVirtualListRowCreatedWithAZIndex_When_ARenderTakesTheZIndexAway_Then_ItPaintsInItsSlotAgain()
        {
            // Arrange
            s_bClasses = "relative z-10";
            using var mounted = Mount();
            Render(mounted);

            // Act
            s_bClasses = "";
            Render(mounted);

            // Assert
            Assert.That(ZBandProbe.PaintOrder(Rows), Is.EqualTo("a,b,c"));
        }

        [Test]
        public void Given_AVirtualListRowWithAZIndex_When_AScrollBringsItIntoTheRange_Then_ItPaintsInFrontOfTheRowsAfterIt()
        {
            // Arrange — no viewport height set, so the host's mount renders no range.
            s_bClasses = "relative z-10";
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

            // Act — a range rendered from a scroll, outside any pass.
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(ZBandProbe.PaintOrder(Rows), Is.EqualTo("a,c,b"));
        }

        [Test]
        public void Given_AVirtualListRowWithANegativeZIndex_When_AScrollBringsItIntoTheRange_Then_ItPaintsBehindTheOtherRows()
        {
            // Arrange — no viewport height set, so the host's mount renders no range.
            s_bClasses = "relative -z-10";
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

            // Act — a range rendered from a scroll, outside any pass.
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(ZBandProbe.PaintOrder(Rows), Is.EqualTo("b,a,c"));
        }

        [Test]
        public void Given_AVirtualListRowARenderGaveAZIndex_When_ItLeavesTheList_Then_NoPlaceholderIsKeptForIt()
        {
            // Arrange
            using var mounted = Mount();
            Render(mounted);
            s_bClasses = "relative z-10";
            Render(mounted);

            // Act
            s_items = new[] { "a", "c" };
            Render(mounted);

            // Assert
            Assert.That(mounted.Root.Reconciler.Context.ZLayerPlaceholders.Count, Is.EqualTo(0));
        }

        [Test]
        public void Given_AVirtualListRowCreatedWithAZIndex_When_TheListPlacesIt_Then_ItTakesTheItemHeight()
        {
            // Arrange
            s_bClasses = "relative z-10";
            using var mounted = Mount();

            // Act
            Render(mounted);

            // Assert
            Assert.That(_root.Q<VisualElement>("b").style.height.value.value, Is.EqualTo(ItemHeight));
        }

        // GREEN_ON_BASE(characterization): the base writes a relocated row's item height to its placeholder, not the row.
        // Written to the row since, the height has to be handed back from the row when the list turns horizontal.
        [Test]
        public void Given_AVirtualListRowCreatedWithAZIndex_When_TheListTurnsHorizontal_Then_ItsHeightIsHandedBack()
        {
            // Arrange
            s_bClasses = "relative z-10";
            using var mounted = Mount();
            Render(mounted);

            // Act
            s_horizontal = true;
            Render(mounted);

            // Assert
            Assert.That(_root.Q<VisualElement>("b").style.height.keyword, Is.EqualTo(StyleKeyword.Null));
        }
    }
}
