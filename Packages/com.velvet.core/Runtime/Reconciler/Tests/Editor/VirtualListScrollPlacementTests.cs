using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a <see cref="V.VirtualList{T}"/> range rendered from a scroll, outside any reconcile pass,
    /// places before the scroll returns: the children of each row that is a <c>V.Portal</c> mount in its target, in
    /// the rows' order, a Component row between them included, and in the order a pass mounts them where a Component
    /// row renders a Portal of its own. A Portal a range that failed had built mounts nothing when the range renders
    /// again. A child of such a Portal that suspends with no Suspense above the list leaves the list's host waiting on
    /// the read rather than leaving the scroll.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListScrollPlacementTests
    {
        private VisualElement _root;
        private static VisualElement s_target;
        private static int s_refusals;
        private static VelvetTaskCompletionSource<string> s_resource;
        private static ComponentFiber s_listHost;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            (s_target, s_refusals, s_resource, s_listHost) =
                (new VisualElement(), 0, new VelvetTaskCompletionSource<string>(), null);
        }

        [Component]
        private static VNode ComponentRow(string item) => V.Div(name: item);

        [Component(Compiler = false)]
        private static VNode PortalComponentRow(string item) => V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) });

        [Component(Compiler = false)]
        private static VNode PendingRead() => V.Label(name: "pending", text: Hooks.Use(_ => s_resource.Task));

        [Component(Compiler = false)]
        private static VNode SuspendingPortalListHost()
        {
            s_listHost = FiberAmbientStack.Current;
            return V.VirtualList(
                items: new[] { "a" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: _ => V.Portal(s_target, new VNode[] { V.Component(PendingRead) }),
                overscan: 0);
        }

        private ScrollView List => _root.Q<ScrollView>();

        [Test]
        public void Given_APortalRowAheadOfAComponentRowThatRendersAPortalToTheSameTarget_When_AScrollBringsThemIntoTheRange_Then_TheirChildrenMountInTheRowsOrder()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "b", "c" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item == "c"
                        ? V.Component(PortalComponentRow, item, key: item)
                        : V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) }),
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", s_target.Children().Select(child => child.name)), Is.EqualTo("b,c"));
        }

        [Test]
        public void Given_ARangeThatFailedAfterBuildingARowHoldingAPortal_When_TheRangeIsRenderedAgain_Then_ThePortalsChildrenMountOnce()
        {
            // Arrange — the renderer refuses "b" once, after "a" has been built around a Portal.
            s_refusals = 1;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item == "b" && s_refusals-- > 0
                        ? throw new InvalidOperationException("row refused")
                        : item == "a"
                            ? V.Div(name: item, children: new VNode[] { V.Portal(s_target, new VNode[] { V.Label(text: item) }) })
                            : V.Div(name: item),
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];
            try
            {
                controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_target.childCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_AVirtualListRowThatIsAPortalWhoseChildSuspendsWithNoSuspenseAbove_When_AScrollBringsItIntoTheRange_Then_TheListsHostWaitsOnTheRead()
        {
            // Arrange — the list renders no range until a scroll asks for one.
            using var mounted = V.Mount(_root, V.Component(SuspendingPortalListHost, key: "host"));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(s_listHost.SuspendedOn, Is.Not.Null);
        }

        [Test]
        public void Given_VirtualListRowsThatArePortalsAroundAComponentRow_When_AScrollBringsThemIntoTheRange_Then_TheirChildrenMountInTheTarget()
        {
            // Arrange — the list renders no range until a scroll asks for one. "c" is a Component, whose render
            // outside a pass is a top-level pass of its own.
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.VirtualList(
                    items: new[] { "a", "b", "c", "d" },
                    keySelector: item => item,
                    itemHeight: 10f,
                    renderer: item => item switch
                    {
                        "b" or "d" => V.Portal(s_target, new VNode[] { V.Label(text: item, name: item) }),
                        "c" => V.Component(ComponentRow, item, key: item),
                        _ => V.Div(name: item),
                    },
                    overscan: 0),
            }));
            var controller = mounted.Root.Reconciler.Context.VirtualListControllers[List];

            // Act
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert
            Assert.That(string.Join(",", s_target.Children().Select(child => child.name)), Is.EqualTo("b,d"));
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
