using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS-Grid parity contract for <c>grid grid-cols-N</c>. Unity UI Toolkit (6000.3) has no
    /// <c>display: grid</c> (its layout is a Flexbox subset), so Velvet realizes <c>grid</c> as a flex-wrap
    /// row and a <see cref="StyleGridManipulator"/> sizes the children into N equal columns: each child gets
    /// width = (rowWidth - (N-1)*columnGap) / N, the column gap as a leading <c>margin-left</c> on every child
    /// but the first of its row, and the row gap as a leading <c>margin-top</c> on every row but the first.
    /// The grid OWNS its <c>gap-*</c> (a single owner of the child margins), so a grid container registers a
    /// grid manipulator and NO gap manipulator.
    /// </summary>
    /// <remarks>
    /// The gap margins are inline pixel values written without a panel (only the column WIDTH needs a resolved
    /// row width and is asserted under a panel elsewhere), so the row/column placement is observable via
    /// <c>element.style.margin*</c> off-panel — the same harness as <see cref="GapParityTests"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class GridParityTests
    {
        // --space-4 == 16px (see _tokens.uss).
        private const float Space4 = 16f;

        private static VNode Grid(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            return V.Div(className: className, children: children);
        }

        private static VisualElement Container(VisualElement root) => root[0];

        [Test]
        public void Given_GridCols3_When_Reconciled_Then_RegistersOneGridManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler, "GridManipulators"), Is.EqualTo(1));
        }

        [Test]
        public void Given_GridCols3WithGap_When_Reconciled_Then_GapManipulatorIsSuppressed()
        {
            // Arrange — the grid owns its gap, so the gap manipulator must NOT also attach (single owner of
            // the child margins).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler, "GapManipulators"), Is.EqualTo(0));
        }

        [Test]
        public void Given_GridCols3_When_Reconciled_Then_SecondColumnGetsLeadingColumnGap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            Assume.That(container.childCount, Is.EqualTo(6), "Precondition: six children placed");

            // Assert — child 1 (column 1 of row 0) carries the column gap.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_GridCols3_When_Reconciled_Then_FirstColumnHasNoLeadingColumnGap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act — child 3 starts row 1 in column 0, so it must have no leading column gap.
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[3].style.marginLeft.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_GridCols3_When_Reconciled_Then_SecondRowGetsLeadingRowGap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act — child 3 is the first cell of row 1, so it carries the row gap as a leading margin-top.
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[3].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_GridCols3_When_Reconciled_Then_FirstRowHasNoLeadingRowGap()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — child 1 is in row 0, so no leading row gap.
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_SeparateColumnAndRowGaps_When_Reconciled_Then_RowGapUsesGapY()
        {
            // Arrange — gap-x-4 sets only the column gap, gap-y-2 only the row gap.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-x-4 gap-y-2", 6) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — row 1's leading margin-top is the gap-y value (8px), not the gap-x value.
            Assert.That(container[3].style.marginTop.value.value, Is.EqualTo(8f));
        }

        [Test]
        public void Given_GridManipulator_When_GridColsPatchedAway_Then_ManipulatorRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(GetManipulatorCount(scope.Reconciler, "GridManipulators"), Is.EqualTo(1),
                "Precondition: the grid-cols class registered a manipulator");

            // Act — patch the same container without a grid-cols class.
            var tree2 = new VNode[] { Grid("flex flex-row", 6) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler, "GridManipulators"), Is.EqualTo(0));
        }

        [Test]
        public void Given_GridManipulator_When_GridColsPatchedAway_Then_ChildMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(Container(scope.Root)[1].style.marginLeft.value.value, Is.EqualTo(Space4),
                "Precondition: the grid wrote a column gap");

            // Act
            var tree2 = new VNode[] { Grid("flex flex-row", 6) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the inline margins the grid wrote are cleared on removal.
            Assert.That(Container(scope.Root)[1].style.marginLeft.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AGridWithGap_When_OnlyTheGridClassIsPatchedAway_Then_TheGapTakesOverTheChildMargins()
        {
            // Arrange — the grid owns the child margins while it is present, so the gap manipulator is
            // suppressed. Dropping only the grid class hands that ownership over within a single pass, and
            // the departing grid manipulator clears every margin it wrote as it detaches.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(GetManipulatorCount(scope.Reconciler, "GapManipulators"), Is.EqualTo(0),
                "Precondition: the grid suppressed the gap manipulator");

            // Act — same gap class, no grid class.
            var tree2 = new VNode[] { Grid("flex flex-row gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the arriving gap manipulator's margins survive the departing grid's teardown.
            Assert.That(Container(scope.Root)[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_GridCols2_When_PatchedToGridCols3_Then_PlacementFollowsNewColumnCount()
        {
            // Arrange — 6 children as 2 columns: child 2 starts row 1 (column 0, no leading gap).
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Grid("grid grid-cols-2 gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(Container(scope.Root)[2].style.marginLeft.value.value, Is.EqualTo(0f),
                "Precondition: at 2 columns child 2 begins a new row");

            // Act — re-spec to 3 columns: child 2 is now column 2 of row 0 (carries the column gap).
            var tree2 = new VNode[] { Grid("grid grid-cols-3 gap-4", 6) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(Container(scope.Root)[2].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_BareGrid_When_Reconciled_Then_RegistersOneGridManipulator()
        {
            // Arrange — a bare `grid` (no grid-cols) is a single column, still driven by the manipulator.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetManipulatorCount(scope.Reconciler, "GridManipulators"), Is.EqualTo(1));
        }

        [Test]
        public void Given_BareGrid_When_Reconciled_Then_ChildrenStackVerticallyWithRowGap()
        {
            // Arrange — a single column stacks the children, so each after the first carries the row gap as a
            // leading margin-top (and no column gap).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            Assume.That(container[1].style.marginLeft.value.value, Is.EqualTo(0f),
                "Precondition: a single column has no column gap");

            // Assert
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_GridCols3WithFiveChildren_When_Reconciled_Then_ShortLastRowStartsANewRow()
        {
            // Arrange — 5 children at 3 columns: child 3 begins row 1 (column 0), so it carries the row gap.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 5) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            Assume.That(container.childCount, Is.EqualTo(5), "Precondition: five children placed");

            // Assert
            Assert.That(container[3].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_GridCols3WithFiveChildren_When_Reconciled_Then_ShortLastRowSecondCellGetsColumnGap()
        {
            // Arrange — child 4 is column 1 of the short last row, so it carries the column gap.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Grid("grid grid-cols-3 gap-4", 5) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[4].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        private static int GetManipulatorCount(Reconciler reconciler, string propertyName)
        {
            var ctxField = typeof(Reconciler).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(ctxField, Is.Not.Null, "_ctx field not found");
            var ctx = ctxField.GetValue(reconciler);
            var prop = ctx.GetType().GetProperty(propertyName);
            Assert.That(prop, Is.Not.Null, propertyName + " property not found");
            var dict = prop.GetValue(ctx) as System.Collections.IDictionary;
            return dict?.Count ?? 0;
        }

        [Test]
        public void Given_AnArbitraryBracketColumnCount_When_Parsed_Then_TheCountIsExtracted()
        {
            // Act
            var ok = StyleGridClass.TryParse("grid-cols-[7]", out var columns);

            // Assert
            Assert.That((ok, columns), Is.EqualTo((true, 7)));
        }

        [Test]
        public void Given_AnEmptyBracketColumnCount_When_Parsed_Then_ItIsRejected()
        {
            // Act
            var ok = StyleGridClass.TryParse("grid-cols-[]", out _);

            // Assert
            Assert.That(ok, Is.False);
        }
    }

    /// <summary>
    /// Panel-backed coverage for the column WIDTH that <see cref="StyleGridManipulator"/> computes — the one
    /// part of the grid contract that needs a resolved row width (the off-panel <see cref="GridParityTests"/>
    /// cover the gap-margin placement and wiring). A <c>grid grid-cols-3 gap-4</c> row of a known width must
    /// size each column to <c>(rowWidth - (N-1)*columnGap) / N</c>: the manipulator reads
    /// <c>contentRect.width</c> on a GeometryChangedEvent and writes the inline width.
    /// </summary>
    [TestFixture]
    internal sealed class GridSizingPanelTests : PanelTestBase
    {
        private const float Space4 = 16f;

        [Test]
        public void Given_GridCols3OnA300pxRow_When_LaidOut_Then_EachColumnFillsAThirdMinusTheGaps()
        {
            // Arrange — 3 children in a 300px-wide grid-cols-3 gap-4 row (all inline, so no stylesheet needed).
            var children = new VNode[3];
            for (var i = 0; i < 3; i++)
            {
                children[i] = V.Div(className: "child");
            }
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "grid", className: "grid grid-cols-3 gap-4 w-[300px]", children: children));
            var container = _window.rootVisualElement.Q<VisualElement>("grid");

            // Act — resolve the 300px row width, then deliver a GeometryChangedEvent so the manipulator reads
            // contentRect.width and sizes the columns.
            ForcePanelUpdate(container.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }
            Assume.That(container.contentRect.width, Is.EqualTo(300f).Within(1f),
                "Precondition: the row resolved to its 300px width");

            // Assert — each column ≈ (300 - 2*16) / 3 (minus the manipulator's sub-pixel wrap-safety shave).
            var expected = (300f - 2f * Space4) / 3f;
            Assert.That(container[0].style.width.value.value, Is.EqualTo(expected).Within(2f));
        }
    }
}
