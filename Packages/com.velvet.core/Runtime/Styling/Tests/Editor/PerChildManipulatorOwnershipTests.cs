using System;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies who may take a per-child value back off a child, for the gap, divide and grid
    /// manipulators. Each tracks the children it wrote to by reference, and the element pool hands a child
    /// straight from one container to another, so the container a child left can still be tracking it
    /// after the container it joined has written to it — and would then reset that write. The same
    /// question <see cref="ChildVariantChildOwnershipTests"/> poses for the child-combinator walk.
    /// </summary>
    /// <remarks>
    /// The containers, their manipulators and their spacing all come from a real reconcile of real utility
    /// classes; what each case then poses by hand is the order the two containers re-apply in, over the
    /// element graph a pooled re-rent leaves behind — the child under the second container while the first
    /// still tracks it. The first case runs that hand-off through the reconciler instead and comes out
    /// spaced, which is why the rest pose the order rather than reconciling for it: a container re-applies
    /// right after its own children are reconciled, and what defers one past the other's write is the
    /// panel. <see cref="StyleGapManipulator"/> names the two re-apply sources that arrive on the panel's
    /// schedule rather than the reconciler's.
    /// <para>
    /// Gap and grid share one claim table, so the pair is asked across families as well: both write a
    /// child's margins, and a child moving between a gap container and a grid one is one owner's or the
    /// other's.
    /// </para>
    /// </remarks>
    [TestFixture]
    internal sealed class PerChildManipulatorOwnershipTests
    {
        // --space-4 == 16px, --space-8 == 32px (_tokens.uss); divide-x-N is N px (StyleDivideClass).
        private const string Gap4Row = "flex flex-row gap-x-4";
        private const string Gap8Row = "flex flex-row gap-x-8";
        private const string Divide4Row = "flex flex-row divide-x-4";
        private const string Divide8Row = "flex flex-row divide-x-8";
        private const string Grid4 = "grid grid-cols-2 gap-x-4";
        private const string Grid8 = "grid grid-cols-2 gap-x-8";

        // Null reads as a word rather than as the zero StyleLength.value carries for it, so a cleared
        // margin cannot be mistaken for a written zero.
        private static string Inline(StyleLength length)
            => length.keyword == StyleKeyword.Null
                ? "null"
                : length.value.value.ToString(CultureInfo.InvariantCulture);

        private static string Inline(StyleFloat value)
            => value.keyword == StyleKeyword.Null
                ? "null"
                : value.value.ToString(CultureInfo.InvariantCulture);

        private static ReconcilerContext Context(ReconcilerScope scope)
            => (ReconcilerContext)typeof(Reconciler)
                .GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(scope.Reconciler)!;

        // Two rows, each spacing its own children, with one Label in the first that the second will take.
        private static (VisualElement First, VisualElement Second, VisualElement Moving) TwoRows(
            ReconcilerScope scope, string firstClass, string secondClass)
        {
            var tree = new VNode[]
            {
                V.Div(className: firstClass, children: new VNode[] { V.Text("a"), V.Text("b") }),
                V.Div(className: secondClass, children: new VNode[] { V.Text("c") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            return (scope.Root[0], scope.Root[1], scope.Root[0][1]);
        }

        // The element graph a pooled re-rent produces: the child is a child of the second container, which
        // has written its own value to it, while the first container still tracks it.
        private static void ReRent(VisualElement first, VisualElement second, VisualElement moving,
            Action applySecond)
        {
            first.Remove(moving);
            second.Add(moving);
            applySecond();
        }

        // GREEN_ON_BASE(characterization): the pooled hand-off a reconcile pass already spaces correctly,
        // which is what makes the deferred re-apply the cases below pose the reachable order rather than
        // this one.
        [Test]
        public void Given_ARowTakingAPooledLabelFromASiblingRow_When_TheMoveIsOneReconcilePass_Then_ItIsSpacedByTheRowItJoined()
        {
            // Arrange — the first row gives up its second Label and the second row takes one, in a single
            // pass, which is where the element pool hands the same instance over.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Text("a"), V.Text("b") }),
                V.Div(className: Gap8Row, children: new VNode[] { V.Text("c") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var leaving = scope.Root[0][1];
            var after = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Text("a") }),
                V.Div(className: Gap8Row, children: new VNode[] { V.Text("c"), V.Text("d") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — that the pool handed THAT element over rides along, since a freshly built one would
            // be spaced correctly whatever the two rows' trackers held.
            var arrived = scope.Root[1][1];
            Assert.That((ReferenceEquals(leaving, arrived), Inline(arrived.style.marginLeft)),
                Is.EqualTo((true, "32")));
        }

        [Test]
        public void Given_AChildReRentedByAnotherGapRow_When_TheOldRowRunsAfterwards_Then_TheNewSpacingSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, second, moving) = TwoRows(scope, Gap4Row, Gap8Row);
            var spacedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.GapManipulators[second].Apply());

            // Act
            ctx.GapManipulators[first].Apply();

            // Assert — the first row's own spacing rides along: with nothing tracked there, the release
            // under test never runs and the margin the second wrote survives for the wrong reason.
            Assert.That((spacedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "32")));
        }

        [Test]
        public void Given_AChildReRentedByAnotherGapRow_When_TheOldRowIsTornDown_Then_TheNewSpacingSurvives()
        {
            // Arrange — the same ownership question reached through detach rather than through the walk.
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, second, moving) = TwoRows(scope, Gap4Row, Gap8Row);
            var spacedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.GapManipulators[second].Apply());

            // Act
            first.RemoveManipulator(ctx.GapManipulators[first]);

            // Assert — as above, the first row's own spacing rides along.
            Assert.That((spacedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "32")));
        }

        // GREEN_ON_BASE(characterization): the reparent sweep the ownership claim must leave standing, so
        // that a child claimed by nobody still loses the margin its old row wrote.
        [Test]
        public void Given_AChildThatLeftAndWasNotReRented_When_TheGapRowRunsAfterwards_Then_TheSpacingIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, _, moving) = TwoRows(scope, Gap4Row, Gap8Row);
            var spacedByFirst = Inline(moving.style.marginLeft);
            first.Remove(moving);
            new VisualElement().Add(moving);

            // Act
            ctx.GapManipulators[first].Apply();

            // Assert — the precondition rides along, since a margin that was never written and one the
            // sweep cleared leave the same inline slot.
            Assert.That((spacedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "null")));
        }

        // GREEN_ON_BASE(characterization): the residue-free dispose the base already leaves, which the claim
        // must not turn into a no-op — Reconciler.ReleaseManipulators empties every claim table, so that
        // must happen after the loops whose releases read them and not before.
        [Test]
        public void Given_AChildThatLeftAGapRow_When_TheReconcilerIsDisposed_Then_TheSpacingIsCleared()
        {
            // Arrange — the child is still tracked by the row it left, which is what a teardown release
            // has to answer for; a child still in the row is cleared by the abandoned-edge pass instead.
            var scope = new ReconcilerScope();
            var (first, _, moving) = TwoRows(scope, Gap4Row, Gap8Row);
            var spacedByFirst = Inline(moving.style.marginLeft);
            first.Remove(moving);
            new VisualElement().Add(moving);

            // Act
            scope.Reconciler.Dispose();

            // Assert — the precondition rides along, as above.
            Assert.That((spacedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "null")));
        }

        [Test]
        public void Given_AChildReRentedByAnotherDivideRow_When_TheOldRowRunsAfterwards_Then_TheNewDividerSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, second, moving) = TwoRows(scope, Divide4Row, Divide8Row);
            var dividedByFirst = Inline(moving.style.borderLeftWidth);
            ReRent(first, second, moving, () => ctx.DivideManipulators[second].Apply());

            // Act
            ctx.DivideManipulators[first].Apply();

            // Assert — the first row's own divider rides along.
            Assert.That((dividedByFirst, Inline(moving.style.borderLeftWidth)), Is.EqualTo(("4", "8")));
        }

        // GREEN_ON_BASE(characterization): the reparent sweep the ownership claim must leave standing for
        // the divider border too.
        [Test]
        public void Given_AChildThatLeftAndWasNotReRented_When_TheDivideRowRunsAfterwards_Then_TheDividerIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, _, moving) = TwoRows(scope, Divide4Row, Divide8Row);
            var dividedByFirst = Inline(moving.style.borderLeftWidth);
            first.Remove(moving);
            new VisualElement().Add(moving);

            // Act
            ctx.DivideManipulators[first].Apply();

            // Assert — the precondition rides along, as above.
            Assert.That((dividedByFirst, Inline(moving.style.borderLeftWidth)), Is.EqualTo(("4", "null")));
        }

        [Test]
        public void Given_AChildReRentedByAnotherGrid_When_TheOldGridRunsAfterwards_Then_TheNewColumnGapSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, second, moving) = TwoRows(scope, Grid4, Grid8);
            var sizedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.GridManipulators[second].Apply());

            // Act
            ctx.GridManipulators[first].Apply();

            // Assert — the first grid's own column gap rides along.
            Assert.That((sizedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "32")));
        }

        // GREEN_ON_BASE(characterization): the reparent sweep the ownership claim must leave standing for
        // the grid's own writes.
        [Test]
        public void Given_AChildThatLeftAndWasNotReRented_When_TheGridRunsAfterwards_Then_TheSizingIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (first, _, moving) = TwoRows(scope, Grid4, Grid8);
            var sizedByFirst = Inline(moving.style.marginLeft);
            first.Remove(moving);
            new VisualElement().Add(moving);

            // Act
            ctx.GridManipulators[first].Apply();

            // Assert — the precondition rides along, as above.
            Assert.That((sizedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "null")));
        }

        [Test]
        public void Given_AGridChildReRentedByAGapRow_When_TheGridRunsAfterwards_Then_TheGapSpacingSurvives()
        {
            // Arrange — the two families write the same margin slot, so the claim they share has to answer
            // across them and not only within one.
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (grid, row, moving) = TwoRows(scope, Grid4, Gap8Row);
            var sizedByGrid = Inline(moving.style.marginLeft);
            ReRent(grid, row, moving, () => ctx.GapManipulators[row].Apply());

            // Act
            ctx.GridManipulators[grid].Apply();

            // Assert — the grid's own column gap rides along.
            Assert.That((sizedByGrid, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "32")));
        }

        [Test]
        public void Given_AGapChildReRentedByAGrid_When_TheRowRunsAfterwards_Then_TheColumnGapSurvives()
        {
            // Arrange — the other direction across the shared claim.
            using var scope = new ReconcilerScope();
            var ctx = Context(scope);
            var (row, grid, moving) = TwoRows(scope, Gap4Row, Grid8);
            var spacedByRow = Inline(moving.style.marginLeft);
            ReRent(row, grid, moving, () => ctx.GridManipulators[grid].Apply());

            // Act
            ctx.GapManipulators[row].Apply();

            // Assert — the row's own spacing rides along.
            Assert.That((spacedByRow, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "32")));
        }
    }
}
