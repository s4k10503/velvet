using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a <see cref="V.VirtualList{T}"/>'s component rows whose structural variant goes away and comes back,
    /// with a range placed between while the list's root holds no structural rule: once the rule is back, each row
    /// matches its place among the rows the range left, as CSS reads the position a row holds now.
    /// </summary>
    [TestFixture]
    internal sealed class VirtualListStructuralRuleToggleTests
    {
        private VisualElement _root;
        private static string s_rowClass;
        private static Dictionary<string, StateUpdater<int>> s_renders;

        [SetUp]
        public void SetUp()
        {
            (_root, s_rowClass, s_renders) = (new VisualElement(), "even:bg-alt", new Dictionary<string, StateUpdater<int>>());
        }

        [Component(Compiler = false)]
        private static VNode Row(string item)
        {
            var (_, setRender) = Hooks.UseState(0);
            s_renders[item] = setRender;
            return V.Div(name: item, className: s_rowClass);
        }

        // Nothing else in the root carries a structural variant.
        [Component(Compiler = false)]
        private static VNode ListHost()
            => V.VirtualList(
                items: new[] { "a", "b", "c" },
                keySelector: item => item,
                itemHeight: 10f,
                renderer: item => V.Component(Row, item, key: item),
                overscan: 0);

        private FiberVirtualListController Controller(MountedTree mounted)
            => mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];

        // Renders items again with rowClass as their class.
        private static void RenderRows(MountedTree mounted, string rowClass, params string[] items)
        {
            s_rowClass = rowClass;
            foreach (var item in items) s_renders[item].Invoke(previous => previous + 1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [Test]
        public void Given_RowsThatDroppedEven_When_AScrollTakesTheFirstRowAwayAndTheRowsTakeEvenAgain_Then_TheNewSecondMatchesEven()
        {
            // Arrange — a range of "a" to "c", outside any pass; the rows then drop even:, and a scroll to "b" and "c"
            // takes "a" away and creates no row.
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            RenderRows(mounted, "p-2", "a", "b", "c");
            controller.UpdateVisibleRange(scrollY: 10f, viewportHeight: 30f);

            // Act
            RenderRows(mounted, "even:bg-alt", "b", "c");

            // Assert
            Assert.That(_root.Q<VisualElement>("c").ClassListContains("bg-alt"), Is.True);
        }

        [Test]
        public void Given_RowsThatDroppedNthLast2_When_ARangeTakesTheLastRowAwayAndTheRowsTakeItAgain_Then_TheNewFirstMatchesNthLast2()
        {
            // Arrange — a range of "a" to "c", outside any pass; the rows then drop nth-last-2:, and a range of "a" and
            // "b" takes "c" away and creates no row.
            s_rowClass = "nth-last-2:bg-end";
            using var mounted = V.Mount(_root, V.Component(ListHost, key: "host"));
            var controller = Controller(mounted);
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 30f);
            RenderRows(mounted, "p-2", "a", "b", "c");
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 15f);

            // Act
            RenderRows(mounted, "nth-last-2:bg-end", "a", "b");

            // Assert
            Assert.That(_root.Q<VisualElement>("a").ClassListContains("bg-end"), Is.True);
        }
    }
}
