using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a row updating its own state in a keyed list charges the same GC.Alloc blocks at 2,000
    /// rows as at 250, once the list has been updated before. The context cursor an isolated re-render rebuilds
    /// walks the parent's output up to the row.
    /// </summary>
    [TestFixture]
    internal sealed class ContextSpineRowUpdateAllocationTests
    {
        private sealed record RowProps(int Index, string Key);

        private static RowProps[] s_rows;
        private static StateUpdater<int>[] s_setters;

        [Component(Compiler = false)]
        private static VNode ListRender()
        {
            var (rows, _) = Hooks.UseState(s_rows);
            return V.Div("flex flex-col", V.List(rows, row => row.Key, row => V.Component(RowRender, row)));
        }

        [Component(Compiler = false, Memoize = true)]
        private static VNode RowRender(RowProps props)
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setters[props.Index] = setCount;
            return V.Label(text: "r" + props.Index + " " + count);
        }

        // The first update after a mount charges more than the ones after it, so each list is updated once
        // before the window, and the window takes the middle row, which walks half the list.
        private static int BlocksOfOneRowUpdate(int rows)
        {
            s_rows = new RowProps[rows];
            for (var i = 0; i < rows; i++) s_rows[i] = new RowProps(i, "row-" + i);
            s_setters = new StateUpdater<int>[rows];
            using var mounted = V.Mount(new VisualElement(), V.Component(ListRender));
            mounted.DrainForTest();
            var row = rows / 2;
            var next = 0;
            s_setters[row].Invoke(++next);
            mounted.DrainForTest();
            return GCAllocationProbe.MedianBlocksDuring(
                () => s_setters[row].Invoke(++next),
                () => mounted.DrainForTest());
        }

        // GREEN_ON_BASE(characterization): the base's per-row storage is already kept between updates, so it charges the same at both sizes.
        [Test]
        public void Given_AKeyedList_When_TheMiddleRowUpdatesItsState_Then_TheBlocksAt2000RowsEqualThoseAt250()
        {
            // Arrange
            BlocksOfOneRowUpdate(2000);
            var at250 = BlocksOfOneRowUpdate(250);

            // Act
            var at2000 = BlocksOfOneRowUpdate(2000);

            // Assert
            Assert.That(at2000, Is.EqualTo(at250));
        }
    }
}
