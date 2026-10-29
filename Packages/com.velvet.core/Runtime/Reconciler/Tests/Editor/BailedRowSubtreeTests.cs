using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a parent re-render does to the rows of a list whose memoized component rendered nothing.
    /// <list type="bullet">
    /// <item>The row's element is left as it is: the tree the row committed is not patched onto it again, which
    /// would reconcile every descendant against itself. The count is the reads of each row element's
    /// <c>contentContainer</c>, which a patch makes to reach the element's children.</item>
    /// <item>A row whose props changed is patched, so the count above is a count of patches and not of nothing.</item>
    /// <item>A component below a row that rendered nothing still shows a state update made in the same batch.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class BailedRowSubtreeTests
    {
        private sealed class CountingRow : VisualElement
        {
            public int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        private const int Rows = 20;

        private static int s_offset;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setCell;
        private static int s_siblingRenders;

        [Component(Compiler = false)]
        private static VNode List()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var children = new VNode[Rows];
            for (var i = 0; i < Rows; i++) children[i] = V.Component(Row, i + s_offset, key: "k" + i);
            return V.Custom<VisualElement>(name: "box", children: children);
        }

        [Component(Memoize = true, Compiler = false)]
        private static VNode Row(int id) => V.Custom<CountingRow>(name: "r" + id, children: new VNode[] { V.Label(name: "l" + id) });

        [Component(Memoize = true, Compiler = false)]
        private static VNode CellRow(int id) => V.Custom<CountingRow>(name: "r" + id, children: new VNode[] { V.Component(Cell) });

        [Component(Compiler = false)]
        private static VNode Cell()
        {
            var (value, setCell) = Hooks.UseState(0);
            s_setCell = setCell;
            return V.Label(name: "cell", text: value.ToString());
        }

        [Component(Compiler = false)]
        private static VNode CellList()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Custom<VisualElement>(name: "box", children: new VNode[] { V.Component(CellRow, 0, key: "k0") });
        }

        [Component(Memoize = true, Compiler = false)]
        private static VNode PairRow(int id) => V.Custom<CountingRow>(
            name: "r" + id, children: new VNode[] { V.Component(Cell), V.Component(Sibling) });

        [Component(Compiler = false)]
        private static VNode Sibling()
        {
            s_siblingRenders++;
            return V.Label(name: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode PairList()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Custom<VisualElement>(name: "box", children: new VNode[] { V.Component(PairRow, 0, key: "k0") });
        }

        [Component(Memoize = true, Compiler = false)]
        private static VNode RootChildRow(int id) => V.Component(Sibling);

        [Component(Compiler = false)]
        private static VNode RootChildList()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Custom<VisualElement>(name: "box", children: new VNode[] { V.Component(RootChildRow, 0, key: "k0") });
        }

        private static int RowReadsDuringParentRerender(int offsetAfter)
        {
            s_offset = 0;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            var rows = root.Query<CountingRow>().ToList();
            foreach (var row in rows) row.Reads = 0;
            s_offset = offsetAfter;
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();
            return rows.Sum(row => row.Reads);
        }

        [Test]
        public void Given_RowsWhosePropsAreUnchanged_When_TheirParentReRenders_Then_NoRowElementIsPatched()
        {
            // Act
            var reads = RowReadsDuringParentRerender(offsetAfter: 0);

            // Assert
            Assert.That(reads, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the instrument for the case above: a patch of the base's own reaches the row's contentContainer.
        [Test]
        public void Given_RowsWhosePropsChange_When_TheirParentReRenders_Then_RowElementsArePatched()
        {
            // Act
            var reads = RowReadsDuringParentRerender(offsetAfter: 1000);

            // Assert
            Assert.That(reads, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): a state update below an unchanged row reaches the screen on the base, and has to keep doing so.
        [Test]
        public void Given_ARowWhosePropsAreUnchanged_When_AComponentBelowItAndItsParentUpdateTogether_Then_TheComponentShowsItsState()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(CellList));

            // Act
            s_setCell.Invoke(1);
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(root.Q<Label>("cell").text, Is.EqualTo("1"));
        }

        [Test]
        public void Given_ARowWhosePropsAreUnchanged_When_OneComponentBelowItAndItsParentUpdateTogether_Then_ItsSiblingBelowTheRowIsNotRenderedAgain()
        {
            // Arrange
            s_siblingRenders = 0;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(PairList));

            // Act
            s_setCell.Invoke(1);
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_siblingRenders, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the order the component below the row is flushed before its parent in, the outcome the case above has to match.
        [Test]
        public void Given_ARowWhosePropsAreUnchanged_When_OneComponentBelowItUpdatesAndIsFlushedBeforeItsParentUpdates_Then_ItsSiblingBelowTheRowIsNotRenderedAgain()
        {
            // Arrange
            s_siblingRenders = 0;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(PairList));
            s_setCell.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_siblingRenders, Is.EqualTo(1));
        }

        [Test]
        public void Given_ARowWhosePropsAreUnchangedAndWhoseOutputIsAComponent_When_TheirParentReRenders_Then_ThatComponentIsNotRenderedAgain()
        {
            // Arrange
            s_siblingRenders = 0;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(RootChildList));

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_siblingRenders, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the component with the update pending shows it beside a sibling that has none.
        [Test]
        public void Given_ARowWhosePropsAreUnchanged_When_OneComponentBelowItAndItsParentUpdateTogether_Then_ThatComponentShowsItsState()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(PairList));

            // Act
            s_setCell.Invoke(1);
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(root.Q<Label>("cell").text, Is.EqualTo("1"));
        }
    }
}
