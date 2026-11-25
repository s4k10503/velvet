using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies structural variants (<c>first:</c>, <c>last:</c>, <c>even:</c>) against the rows a
    /// <see cref="V.VirtualList{T}"/> range places, which no walk reconciles as the children of the list's container: a
    /// row matches its place among the rows a range leaves standing, the elements a component row renders as well, which
    /// stand in an anchor of its own, there after the component renders them again, and the rows behind it once it
    /// renders a different number of them; a child of the target a row's Portal mounts into matches its place again once
    /// a range rendered outside any pass takes that row away.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListStructuralVariantTests
    {
        private const float ItemHeight = 10f;

        private VisualElement _root;
        private static string[] s_items;
        private static Func<string, VNode> s_row;
        private static VisualElement s_target;
        private static StateUpdater<int> s_render;
        private static StateUpdater<int> s_renderRowB;
        private static StateUpdater<bool> s_setGrown;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            (s_items, s_row, s_target, s_render, s_renderRowB, s_setGrown) =
                (new[] { "a", "b", "c" }, item => V.Div(name: item, className: "first:bg-mark"), null, default, default,
                    default);
        }

        [Component(Compiler = false)]
        private static VNode ComponentRow(string item)
        {
            var (_, setRender) = Hooks.UseState(0);
            if (item == "b") s_renderRowB = setRender;
            return V.Div(name: item, className: "first:bg-mark even:bg-alt");
        }

        // Two elements, keyed by how often it rendered, so each render replaces both.
        [Component(Compiler = false)]
        private static VNode PairRow(string item)
        {
            var (renders, setRenders) = Hooks.UseState(0);
            if (item == "b") s_renderRowB = setRenders;
            return V.Fragment(
                V.Div(key: $"{item}-{renders}", name: item, className: "last:bg-end"),
                V.Div(key: $"{item}-more-{renders}", name: item + "-more", className: "last:bg-end"));
        }

        // One element, or two once s_setGrown says so.
        [Component(Compiler = false)]
        private static VNode GrowingRow(string item)
        {
            var (grown, setGrown) = Hooks.UseState(false);
            s_setGrown = setGrown;
            return grown ? V.Fragment(V.Div(name: item), V.Div(name: item + "-more")) : V.Div(name: item);
        }

        [Component(Compiler = false)]
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
                    renderer: item => s_row(item),
                    overscan: 0),
                V.Div(name: "target", children: new VNode[] { V.Div(name: "own", className: "last:bg-mark") }),
            });
        }

        private FiberVirtualListController Controller(MountedTree mounted)
            => mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

        // The viewport height the list's last geometry change measured.
        private static void SetViewport(FiberVirtualListController controller, float height)
            => typeof(FiberVirtualListController)
                .GetField("_viewportHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(controller, height);

        private static void Render(MountedTree mounted)
        {
            s_render.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_RowsCarryingFirst_When_AScrollMakesARowTheFirstOfTheRange_Then_ItMatches()
        {
            // Arrange — the first range, rendered outside any pass, holds "a" and "b".
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Act — a scroll to a range of "b" and "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 10f, viewportHeight: 15f);

            // Assert
            Assert.That(_root.Q<VisualElement>("b").ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AnElementRowAndAComponentRowCarryingEven_When_ARangeHoldsBoth_Then_TheComponentRowMatchesEven()
        {
            // Arrange — "a" is an element row, "b" a component row.
            s_row = item => item == "a"
                ? V.Div(name: item, className: "first:bg-mark even:bg-alt")
                : V.Component(ComponentRow, item, key: item);
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);

            // Act — a range of "a" and "b", outside any pass.
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Assert
            Assert.That(_root.Q<VisualElement>("b").ClassListContains("bg-alt"), Is.True);
        }

        [Test]
        public void Given_AComponentRowThatIsNotFirst_When_ItRendersAgain_Then_ItStillDoesNotMatchFirst()
        {
            // Arrange — a range of "a" and "b", outside any pass.
            s_row = item => V.Component(ComponentRow, item, key: item);
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);
            var rowB = _root.Q<VisualElement>("b");
            var notFirstBefore = !rowB.ClassListContains("bg-mark");

            // Act — "b"'s own component renders again, into the anchor it stands in.
            s_renderRowB.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where "b" matched first: before its render.
            Assert.That(notFirstBefore ? rowB.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_ComponentRowsCarryingFirst_When_AScrollMakesOneTheFirstOfTheRange_Then_ItMatches()
        {
            // Arrange — the first range, rendered outside any pass, holds "a" and "b".
            s_row = item => V.Component(ComponentRow, item, key: item);
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Act — a scroll to a range of "b" and "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 10f, viewportHeight: 15f);

            // Assert
            Assert.That(_root.Q<VisualElement>("b").ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_TwoComponentRowsRenderingTwoElementsEach_When_TheLastRendersBothAnew_Then_ItsSecondMatchesLast()
        {
            // Arrange — a range of "a" and "b", outside any pass.
            s_row = item => V.Component(PairRow, item, key: item);
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Act — "b"'s own component renders again, replacing both of its elements in the anchor it stands in.
            s_renderRowB.Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<VisualElement>("b-more").ClassListContains("bg-end"), Is.True);
        }

        [Test]
        public void Given_AnElementRowMatchingEvenBehindAComponentRow_When_TheComponentRowRendersASecondElement_Then_ItStopsMatchingEven()
        {
            // Arrange — "a" is a component row rendering one element, "b" an element row; a range of both, outside any
            // pass.
            s_row = item => item == "a"
                ? V.Component(GrowingRow, item, key: item)
                : V.Div(name: item, className: "even:bg-alt");
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            Controller(mounted).UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);
            var rowB = _root.Q<VisualElement>("b");
            var evenBefore = rowB.ClassListContains("bg-alt");

            // Act — "a"'s own component renders a second element ahead of "b".
            s_setGrown.Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — null where "b" did not match even: before "a" grew.
            Assert.That(evenBefore ? rowB.ClassListContains("bg-alt") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_ATargetChildCarryingLastBehindWhichAPortalRowMountsAChild_When_AResizeTakesTheRowsOfEmptiedItemsAway_Then_ItMatchesAgain()
        {
            // Arrange — the row for "c" is a Portal appending a child into "target" behind "own", and a render of the
            // host places "a" to "c" inside its pass. A viewport collapsed to nothing then keeps the render that
            // empties the items from taking the rows away.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            s_target = _root.Q<VisualElement>("target");
            s_row = item => item == "c" ? V.Portal(s_target, new VNode[] { V.Div(name: "portalled") }) : V.Div(name: item);
            var controller = Controller(mounted);
            SetViewport(controller, 200f);
            Render(mounted);
            SetViewport(controller, 0f);
            s_items = Array.Empty<string>();
            Render(mounted);
            var own = _root.Q<VisualElement>("own");
            var notLastBeforeTheResize = !own.ClassListContains("bg-mark");

            // Act — the viewport comes back, and the range it asks for, outside any pass, holds no item.
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — null where "own" matched last: while the Portal's child stood behind it.
            Assert.That(notLastBeforeTheResize ? own.ClassListContains("bg-mark") : (bool?)null, Is.True);
        }
    }
}
