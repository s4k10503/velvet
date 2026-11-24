using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <c>has-[…]:</c> element against what a <see cref="V.VirtualList{T}"/> range rendered from a scroll
    /// or a resize, outside any pass, changes under it: a <c>has-[.class]:</c> element matches a row the range places
    /// carrying the class and stops matching the rows it takes away, and one above the target a row's Portal mounts its
    /// children in matches a child carrying the class the range mounts there; a <c>has-[:checked]:</c> element matches
    /// a toggle the range places checked. Each holds by the time the range update returns, and a row's own layout
    /// effect reads the match already.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListHasVariantTests
    {
        private const float ItemHeight = 10f;

        private VisualElement _root;
        private static string[] s_items;
        private static Func<string, VNode> s_selectedRow;
        private static VisualElement s_target;
        private static VisualElement s_parent;
        private static StateUpdater<int> s_render;
        private static bool? s_parentMatchedInRowLayoutEffect;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            (s_items, s_selectedRow, s_target, s_parent, s_render, s_parentMatchedInRowLayoutEffect) =
                (new[] { "a", "b", "c" }, item => V.Div(name: item, className: "selected"), null, null, default, null);
        }

        [Component(Compiler = false)]
        private static VNode ListHost()
        {
            var (_, setRender) = Hooks.UseState(0);
            s_render = setRender;
            return V.Div(children: new VNode[]
            {
                V.Div(name: "parent", className: "has-[.selected]:bg-mark has-[:checked]:bg-checked",
                    children: new VNode[]
                    {
                        V.VirtualList(
                            items: s_items,
                            keySelector: item => item,
                            itemHeight: ItemHeight,
                            renderer: item => item == "c" ? s_selectedRow(item) : V.Div(name: item),
                            overscan: 0),
                    }),
                V.Div(name: "holder", className: "has-[.selected]:bg-mark", children: new VNode[]
                {
                    V.Div(name: "target"),
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode SelectedRow()
        {
            Hooks.UseLayoutEffect(() =>
            {
                s_parentMatchedInRowLayoutEffect = s_parent.ClassListContains("bg-mark");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Div(className: "selected");
        }

        private VisualElement Parent => _root.Q<VisualElement>("parent");

        private VisualElement Holder => _root.Q<VisualElement>("holder");

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
        public void Given_AHasElementAboveAVirtualList_When_AScrollBringsARowCarryingTheClassIntoTheRange_Then_ItMatches()
        {
            // Arrange — the first range, rendered outside any pass, holds "a" and "b".
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Act — a scroll to "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 20f, viewportHeight: 10f);

            // Assert
            Assert.That(Parent.ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AHasCheckedElementAboveAVirtualList_When_AScrollBringsACheckedToggleIntoTheRange_Then_ItMatches()
        {
            // Arrange — the row for "c" is a toggle created checked.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            s_selectedRow = item => V.Toggle(name: item, value: true);
            var controller = Controller(mounted);

            // Act — a scroll to "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 20f, viewportHeight: 10f);

            // Assert
            Assert.That(Parent.ClassListContains("bg-checked"), Is.True);
        }

        [Test]
        public void Given_AHasElementAboveAVirtualListMatchingARow_When_AScrollTakesTheRowOutOfTheRange_Then_ItStopsMatching()
        {
            // Arrange — a render of the host places "a" to "c" inside its pass.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            SetViewport(controller, 200f);
            Render(mounted);
            var matchedBeforeTheScroll = Parent.ClassListContains("bg-mark");

            // Act — a scroll, outside any pass, to a range of "a" and "b".
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Assert — null where the element did not match the rows before the scroll.
            Assert.That(matchedBeforeTheScroll ? Parent.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AHasElementAboveAVirtualListMatchingARow_When_AResizeTakesTheRowsOfEmptiedItemsAway_Then_ItStopsMatching()
        {
            // Arrange — a render of the host places "a" to "c" inside its pass. A viewport collapsed to nothing then
            // keeps the render that empties the items from taking the rows away.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            SetViewport(controller, 200f);
            Render(mounted);
            SetViewport(controller, 0f);
            s_items = Array.Empty<string>();
            Render(mounted);
            var matchedBeforeTheResize = Parent.ClassListContains("bg-mark");

            // Act — the viewport comes back, and the range it asks for, outside any pass, holds no item.
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 200f);

            // Assert — null where the element did not match the rows before the resize.
            Assert.That(matchedBeforeTheResize ? Parent.ClassListContains("bg-mark") : (bool?)null, Is.False);
        }

        [Test]
        public void Given_AHasElementAboveAPortalTarget_When_AScrollBringsARowPortallingTheClassThereIntoTheRange_Then_ItMatches()
        {
            // Arrange — the row for "c" is a Portal into "target", which no ancestor of the list holds.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            s_target = _root.Q<VisualElement>("target");
            s_selectedRow = _ => V.Portal(s_target, new VNode[] { V.Div(className: "selected") });
            var controller = Controller(mounted);

            // Act — a scroll to "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 20f, viewportHeight: 10f);

            // Assert
            Assert.That(Holder.ClassListContains("bg-mark"), Is.True);
        }

        [Test]
        public void Given_AHasElementAboveAVirtualList_When_AScrollBringsInARowCarryingTheClass_Then_TheRowsLayoutEffectReadsItMatching()
        {
            // Arrange — the row for "c" is a component whose layout effect reads "parent".
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            s_parent = Parent;
            s_selectedRow = item => V.Component(SelectedRow, key: item);
            var controller = Controller(mounted);

            // Act — a scroll to "c", outside any pass.
            controller.UpdateVisibleRange(scrollY: 20f, viewportHeight: 10f);

            // Assert — null where the layout effect never ran.
            Assert.That(s_parentMatchedInRowLayoutEffect, Is.True);
        }
    }
}
