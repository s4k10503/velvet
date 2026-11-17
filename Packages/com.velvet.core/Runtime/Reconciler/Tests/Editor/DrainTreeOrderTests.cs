using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the order one drain of the immediate tier renders the components queued on it, as one
    /// <see cref="EventLog"/> string per case: of two components, neither holding the other and neither inside a third
    /// queued with them, the one earlier in the tree renders first, whichever setter ran first, as React 19 renders
    /// them; VirtualList rows queued with a render that moves them render in their new order; and a component the
    /// drain's walk cannot reach from the root renders after the ones it can.
    /// </summary>
    [TestFixture]
    internal sealed class DrainTreeOrderTests
    {
        private sealed record Named(string Name);

        private sealed record Logged(string Name, EventLog Log);

        private sealed record ListOf(string Name);

        [Component]
        private static VNode Leaf(Named props)
        {
            EventLog.UseState(props.Name, 0);
            EventLog.UseRender(props.Name);
            return V.Div();
        }

        [Component]
        private static VNode RendersNothing(Named props)
        {
            EventLog.UseState(props.Name, 0);
            EventLog.UseRender(props.Name);
            return V.Fragment(Array.Empty<VNode>());
        }

        // Reads its log off its props rather than off a Provider, so it renders with no ancestor above it.
        [Component(Compiler = false)]
        private static VNode LoggedLeaf(Logged props)
        {
            props.Log.Add("render:" + props.Name);
            return V.Div();
        }

        [Component]
        private static VNode Siblings()
            => V.Fragment(
                V.Component(Leaf, new Named("A"), key: "a"),
                V.Component(Leaf, new Named("B"), key: "b"));

        [Component]
        private static VNode Wrap() => V.Component(Leaf, new Named("A"), key: "a");

        [Component]
        private static VNode WrappedThenSibling()
            => V.Fragment(
                V.Component(Wrap, key: "wrap"),
                V.Component(Leaf, new Named("B"), key: "b"));

        [Component]
        private static VNode PortalThenSibling(VisualElement target)
            => V.Fragment(
                V.Portal(target, new VNode[] { V.Component(Leaf, new Named("PortalChild"), key: "child") }, key: "portal"),
                V.Component(Leaf, new Named("After"), key: "after"));

        [Component]
        private static VNode LaterMountIntoAnEarlierContainer()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Fragment(
                V.Div(children: new VNode?[] { shown ? V.Component(Leaf, new Named("N"), key: "n") : null }),
                V.Div(children: new VNode[] { V.Component(Leaf, new Named("B"), key: "b") }));
        }

        [Component]
        private static VNode NothingThenSibling()
            => V.Fragment(
                V.Component(RendersNothing, new Named("Title"), key: "title"),
                V.Component(Leaf, new Named("List"), key: "list"));

        [Component]
        private static VNode LaterMountIntoAContainerAheadOfNothing()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Fragment(
                V.Div(children: new VNode?[] { shown ? V.Component(Leaf, new Named("List"), key: "list") : null }),
                V.Component(RendersNothing, new Named("Title"), key: "title"));
        }

        [Component]
        private static VNode LaterNothingAheadOfAContainer()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Fragment(
                shown ? V.Component(RendersNothing, new Named("Shortcuts"), key: "shortcuts") : null,
                V.Div(children: new VNode[] { V.Component(Leaf, new Named("List"), key: "list") }));
        }

        [Component]
        private static VNode PortalOfNothingThenSiblingThenPortal(VisualElement target)
            => V.Fragment(
                V.Portal(target, new VNode[] { V.Component(RendersNothing, new Named("Modal"), key: "modal") }, key: "modal"),
                V.Component(Leaf, new Named("Footer"), key: "footer"),
                V.Portal(target, new VNode[] { V.Component(Leaf, new Named("Toast"), key: "toast") }, key: "toast"));

        [Component]
        private static VNode LoggedSiblings(EventLog log)
            => V.Fragment(
                V.Component(LoggedLeaf, new Logged("A", log), key: "a"),
                V.Component(LoggedLeaf, new Logged("B", log), key: "b"));

        // Each row is a component of its own, which the list mounts in an element of its own.
        [Component]
        private static VNode RowHost(ListOf list)
        {
            var (flipped, _) = EventLog.UseState(list.Name, false);
            EventLog.UseRender(list.Name);
            return V.VirtualList(
                items: flipped ? new[] { "b", "a" } : new[] { "a", "b" },
                keySelector: item => item,
                itemHeight: 50f,
                renderer: item => V.Component(Leaf, new Named(list.Name + item), key: item),
                overscan: 0);
        }

        [Component]
        private static VNode RowHostUnderAParent()
        {
            EventLog.UseState("G", 0);
            EventLog.UseRender("G");
            return V.Component(RowHost, new ListOf("H"), key: "h");
        }

        [Component]
        private static VNode TwoRowHosts()
            => V.Fragment(
                V.Component(RowHost, new ListOf("H1"), key: "h1"),
                V.Component(RowHost, new ListOf("H2"), key: "h2"));

        [Component(Memoize = true, Compiler = false)]
        private static VNode MemoizedRowHost(ListOf list) => V.Component(RowHost, list);

        [Component(Compiler = false)]
        private static VNode SwappableMemoizedRowHosts()
        {
            var (swapped, _) = EventLog.UseState("F", false);
            EventLog.UseRender("F");
            var first = V.Component(MemoizedRowHost, new ListOf("H1"), key: "h1");
            var second = V.Component(MemoizedRowHost, new ListOf("H2"), key: "h2");
            return swapped ? V.Fragment(second, first) : V.Fragment(first, second);
        }

        // A headless mount measures no viewport, so each list is handed the height a layout pass would leave it, which
        // shows both its rows, and renders them.
        private static void ShowRows(MountedTree mounted)
        {
            var lists = new List<FiberVirtualListController>(mounted.Root.Reconciler.Context.VirtualListControllers.Values);
            foreach (var list in lists)
            {
                typeof(FiberVirtualListController)
                    .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(list, 100f);
                list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);
            }
        }

        private static ComponentFiber LoggedFiber(ComponentFiber fiber, string name)
        {
            if (fiber.Props is Logged logged && logged.Name == name) return fiber;
            for (var child = fiber.Child; child != null; child = child.Sibling)
            {
                var found = LoggedFiber(child, name);
                if (found != null) return found;
            }
            return null;
        }

        [Test]
        public void Given_TwoSiblingsSetInReverseTreeOrder_When_OneDrainRendersThem_Then_TheEarlierSiblingRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(Siblings)));
            log.Clear();

            // Act
            log.Setter<int>("B").Invoke(1);
            log.Setter<int>("A").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:A render:B"));
        }

        // Set in tree order: A sits a level deeper than B, under a parent with no update of its own.
        [Test]
        public void Given_AComponentUnderACleanParentAndASiblingAfterThatParent_When_OneDrainRendersBoth_Then_TheDeeperComponentEarlierInTheTreeRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(WrappedThenSibling)));
            log.Clear();

            // Act
            log.Setter<int>("A").Invoke(1);
            log.Setter<int>("B").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:A render:B"));
        }

        // The Portal's child mounts once the walk that placed After has ended, so it joins their parent's children
        // behind After.
        [Test]
        public void Given_APortalChildAndASiblingAfterThePortalSetInReverseTreeOrder_When_OneDrainRendersThem_Then_ThePortalChildRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalThenSibling, new VisualElement())));
            log.Clear();

            // Act
            log.Setter<int>("After").Invoke(1);
            log.Setter<int>("PortalChild").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:PortalChild render:After"));
        }

        // N mounts after B, so it joins their parent's children behind B, in a container ahead of B's.
        [Test]
        public void Given_AComponentMountedLaterIntoAContainerAheadOfItsSiblings_When_BothAreSetInReverseTreeOrder_Then_TheComponentInTheEarlierContainerRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(LaterMountIntoAnEarlierContainer)));
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<int>("B").Invoke(1);
            log.Setter<int>("N").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:N render:B"));
        }

        // Title holds no row, so its output starts where List's first row does.
        [Test]
        public void Given_AComponentRenderingNothingAheadOfASibling_When_BothAreSetInReverseTreeOrder_Then_TheComponentRenderingNothingRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(NothingThenSibling)));
            log.Clear();

            // Act
            log.Setter<int>("List").Invoke(1);
            log.Setter<int>("Title").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Title render:List"));
        }

        // Title holds no row and sits behind the last row of the container that holds List's container, and List
        // mounts after it, so it joins their parent's children behind Title.
        [Test]
        public void Given_AComponentRenderingNothingAfterAContainerASiblingMountedLaterInto_When_BothAreSetInReverseTreeOrder_Then_TheSiblingInTheContainerRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(LaterMountIntoAContainerAheadOfNothing)));
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<int>("Title").Invoke(1);
            log.Setter<int>("List").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:List render:Title"));
        }

        // Shortcuts holds no row and its slot holds the element List's row is in; it mounts after List, so it joins
        // their parent's children behind List.
        [Test]
        public void Given_AComponentRenderingNothingAheadOfTheElementASiblingRendersInto_When_BothAreSetInReverseTreeOrder_Then_TheComponentRenderingNothingRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(LaterNothingAheadOfAContainer)));
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<int>("List").Invoke(1);
            log.Setter<int>("Shortcuts").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Shortcuts render:List"));
        }

        // Modal holds no row, and the rows after its Portal's range in the target are the second Portal's.
        [Test]
        public void Given_APortalChildRenderingNothingAheadOfASiblingAndAnotherPortalIntoTheSameTarget_When_BothAreSetInReverseTreeOrder_Then_ThePortalChildRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalOfNothingThenSiblingThenPortal, new VisualElement())));
            log.Clear();

            // Act
            log.Setter<int>("Footer").Invoke(1);
            log.Setter<int>("Modal").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Modal render:Footer"));
        }

        // H's render re-places its list's rows and renders neither of them.
        [Test]
        public void Given_TwoVirtualListRowsAndTheirHostReorderingThemInOneBatch_When_OneDrainRendersThem_Then_TheRowsRenderInTheHostsNewOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(RowHost, new ListOf("H"))));
            ShowRows(mounted);
            log.Clear();

            // Act
            log.Setter<int>("Ha").Invoke(1);
            log.Setter<int>("Hb").Invoke(1);
            log.Setter<bool>("H").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:H render:Hb render:Ha"));
        }

        // H renders inside G's render rather than by its own entry, and re-places its list's rows there.
        [Test]
        public void Given_VirtualListRowsAndAParentWhoseRenderRendersTheirHostReorderingThem_When_OneDrainRendersThem_Then_TheRowsRenderInTheHostsNewOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(RowHostUnderAParent)));
            ShowRows(mounted);
            log.Clear();

            // Act
            log.Setter<int>("Ha").Invoke(1);
            log.Setter<int>("Hb").Invoke(1);
            log.Setter<bool>("H").Invoke(true);
            log.Setter<int>("G").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:G render:H render:Hb render:Ha"));
        }

        // H1 re-places its rows while H2's rows still wait behind them, and H2 re-places its own later in the drain.
        [Test]
        public void Given_TwoVirtualListHostsEachReorderingItsRowsInOneBatch_When_OneDrainRendersThem_Then_EachHostsRowsRenderInItsNewOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(TwoRowHosts)));
            ShowRows(mounted);
            log.Clear();

            // Act
            log.Setter<int>("H1a").Invoke(1);
            log.Setter<int>("H1b").Invoke(1);
            log.Setter<int>("H2a").Invoke(1);
            log.Setter<int>("H2b").Invoke(1);
            log.Setter<bool>("H1").Invoke(true);
            log.Setter<bool>("H2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:H1 render:H1b render:H1a render:H2 render:H2b render:H2a"));
        }

        // F's render swaps the two lists without rendering either list's host.
        [Test]
        public void Given_VirtualListRowsAndAParentSwappingTheMemoizedComponentsHoldingTheirListsInOneBatch_When_OneDrainRendersThem_Then_TheRowsRenderInTheParentsNewOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(SwappableMemoizedRowHosts)));
            ShowRows(mounted);
            log.Clear();

            // Act
            log.Setter<int>("H1a").Invoke(1);
            log.Setter<int>("H1b").Invoke(1);
            log.Setter<int>("H2a").Invoke(1);
            log.Setter<int>("H2b").Invoke(1);
            log.Setter<bool>("F").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:F render:H2a render:H2b render:H1a render:H1b"));
        }

        // Taken out of its parent's children, A has no path from the root for the drain's walk to reach it by.
        [Test]
        public void Given_AQueuedComponentWithNoPathFromTheRootAndOneInTheTree_When_OneDrainRendersBoth_Then_TheOneInTheTreeRendersFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), V.Component(LoggedSiblings, log));
            var detached = LoggedFiber(mounted.Root, "A");
            var inTheTree = LoggedFiber(mounted.Root, "B");
            log.Clear();
            detached.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            inTheTree.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            detached.Detach();

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:B render:A"));
        }
    }
}
