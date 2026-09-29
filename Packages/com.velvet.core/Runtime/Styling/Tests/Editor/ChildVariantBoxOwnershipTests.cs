using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a child keeps when a <c>[&amp;&gt;*]:</c> payload and a gap, grid or divide container
    /// write the same inline slot on it — the manipulators directly, the payload through a layer. The
    /// container's value wins while it applies, the precedence its polyfill documents; once it stops, the
    /// slot holds what CSS would leave there, the payload's value.
    /// </summary>
    /// <remarks>
    /// The transfer cases pose the same deferred re-apply <see cref="PerChildManipulatorOwnershipTests"/>
    /// poses, over the element graph a pooled re-rent leaves behind — the child under the second container
    /// while the first still tracks it — but across the two kinds of writer, whose claims sit in different
    /// tables and so never answer for each other.
    /// </remarks>
    [TestFixture]
    internal sealed class ChildVariantBoxOwnershipTests
    {
        // A gap suffix resolves through StyleArbitraryValueResolver.TryGetSpacingPx: 4 is 16px and 8 is
        // 32px. A divide-x-N is N px on its own scale (StyleDivideClass).
        private const string Gap4Row = "flex flex-row gap-x-4";
        private const string Gap8Row = "flex flex-row gap-x-8";
        private const string Grid4 = "grid grid-cols-2 gap-x-4";
        private const string Grid8 = "grid grid-cols-2 gap-x-8";
        private const string Divide4Row = "flex flex-row divide-x-4";
        private const string Divide8Row = "flex flex-row divide-x-8";
        private const string MarginPayloadRow = "flex flex-row [&>*]:ml-[2px]";
        private const string BorderPayloadRow = "flex flex-row [&>*]:border-l-[2px]";
        private const string WidthPayloadRow = "flex flex-row [&>*]:w-[8px]";

        // Null reads as a word rather than as the zero StyleLength.value carries for it, so a cleared slot
        // cannot be mistaken for a written zero.
        private static string Inline(StyleLength length)
            => length.keyword == StyleKeyword.Null
                ? "null"
                : length.value.value.ToString(CultureInfo.InvariantCulture);

        private static string Inline(StyleFloat value)
            => value.keyword == StyleKeyword.Null
                ? "null"
                : value.value.ToString(CultureInfo.InvariantCulture);

        private static string Inline(StyleColor color)
            => color.keyword == StyleKeyword.Null ? "null" : color.value.ToString();

        // Two rows, each carrying its own utility, with one child in the first that the second will take.
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

        private static string FourSides(VisualElement element)
            => string.Join(" ", Inline(element.style.marginLeft), Inline(element.style.marginTop),
                Inline(element.style.marginRight), Inline(element.style.marginBottom));

        private static VisualElement OneRow(ReconcilerScope scope, string className)
        {
            var tree = new VNode[]
            {
                V.Div(className: className, children: new VNode[] { V.Text("a"), V.Text("b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);
            return scope.Root[0];
        }

        [Test]
        public void Given_AChildAPayloadRowGaveToAGapRow_When_ThePayloadRowRunsAfterwards_Then_TheGapSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, MarginPayloadRow, Gap8Row);
            var fromPayload = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.GapManipulators[second].Apply());

            // Act
            ctx.ChildVariantManipulators[first].Apply();

            // Assert — the payload's own write rides along: with nothing applied there, the turn-off under
            // test never runs and the gap survives for the wrong reason.
            Assert.That((fromPayload, Inline(moving.style.marginLeft)), Is.EqualTo(("2", "32")));
        }

        [Test]
        public void Given_AChildAPayloadRowGaveToAGrid_When_ThePayloadRowRunsAfterwards_Then_TheColumnGapSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, MarginPayloadRow, Grid8);
            var fromPayload = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.GridManipulators[second].Apply());

            // Act
            ctx.ChildVariantManipulators[first].Apply();

            // Assert — as above, the payload's own write rides along.
            Assert.That((fromPayload, Inline(moving.style.marginLeft)), Is.EqualTo(("2", "32")));
        }

        [Test]
        public void Given_AChildAPayloadRowGaveToADivideRow_When_ThePayloadRowRunsAfterwards_Then_TheDividerSurvives()
        {
            // Arrange — a divider sits on every child but the last, on its right edge, so the moving child is
            // the first one and the payload writes that same edge.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, _) = TwoRows(scope, "flex flex-row [&>*]:border-r-[2px]", Divide8Row);
            var moving = first[0];
            var fromPayload = Inline(moving.style.borderRightWidth);
            first.Remove(moving);
            second.Insert(0, moving);
            ctx.DivideManipulators[second].Apply();

            // Act
            ctx.ChildVariantManipulators[first].Apply();

            // Assert — as above, the payload's own write rides along.
            Assert.That((fromPayload, Inline(moving.style.borderRightWidth)), Is.EqualTo(("2", "8")));
        }

        [Test]
        public void Given_AChildAGapRowGaveToAPayloadRow_When_TheGapRowRunsAfterwards_Then_ThePayloadSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, Gap4Row, MarginPayloadRow);
            var spacedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.GapManipulators[first].Apply();

            // Assert — the gap row's own spacing rides along: with nothing tracked there, the release under
            // test never runs and the payload survives for the wrong reason.
            Assert.That((spacedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "2")));
        }

        [Test]
        public void Given_AChildAGridGaveToAPayloadRow_When_TheGridRunsAfterwards_Then_ThePayloadSurvives()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, Grid4, MarginPayloadRow);
            var sizedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.GridManipulators[first].Apply();

            // Assert — as above, the grid's own column gap rides along.
            Assert.That((sizedByFirst, Inline(moving.style.marginLeft)), Is.EqualTo(("16", "2")));
        }

        [Test]
        public void Given_AChildAGridGaveToAWidthPayloadRow_When_TheGridRunsAfterwards_Then_ThePayloadWidthSurvives()
        {
            // Arrange — no layout runs here, so the grid never sizes the column and its width release is the
            // same hand-back its deferred pass makes; the column gap stands in for its having held the child.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, Grid4, WidthPayloadRow);
            var sizedByFirst = Inline(moving.style.marginLeft);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.GridManipulators[first].Apply();

            // Assert
            Assert.That((sizedByFirst, Inline(moving.style.width)), Is.EqualTo(("16", "8")));
        }

        [Test]
        public void Given_AChildAGridGaveToAFourSidedPayloadRow_When_TheGridRunsAfterwards_Then_EverySideTakesThePayload()
        {
            // Arrange — the grid holds all four margins, the ones it zeroes as well as its column gap.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, moving) = TwoRows(scope, Grid4, "flex flex-row [&>*]:m-[3px]");
            var sizedByFirst = FourSides(moving);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.GridManipulators[first].Apply();

            // Assert
            Assert.That((sizedByFirst, FourSides(moving)), Is.EqualTo(("16 0 0 0", "3 3 3 3")));
        }

        [Test]
        public void Given_AChildAColoredDivideRowGaveToAColorPayloadRow_When_TheDivideRowRunsAfterwards_Then_ThePayloadColorSurvives()
        {
            // Arrange — the first child carries the divider.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, _) = TwoRows(scope, "flex flex-row divide-x-4 divide-gray-200",
                "flex flex-row [&>*]:border-[#00FF00]");
            var moving = first[0];
            var coloredByFirst = Inline(moving.style.borderRightColor);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.DivideManipulators[first].Apply();

            // Assert — the divider's own color rides along, as it is the value a missed release would leave.
            Assert.That((coloredByFirst == "null", Inline(moving.style.borderRightColor)),
                Is.EqualTo((false, Color.green.ToString())));
        }

        [Test]
        public void Given_AChildADivideRowGaveToAPayloadRow_When_TheDivideRowRunsAfterwards_Then_ThePayloadSurvives()
        {
            // Arrange — the first child carries the divider, on the edge the payload writes.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var (first, second, _) = TwoRows(scope, Divide4Row, "flex flex-row [&>*]:border-r-[2px]");
            var moving = first[0];
            var dividedByFirst = Inline(moving.style.borderRightWidth);
            ReRent(first, second, moving, () => ctx.ChildVariantManipulators[second].Apply());

            // Act
            ctx.DivideManipulators[first].Apply();

            // Assert — as above, the divide row's own border rides along.
            Assert.That((dividedByFirst, Inline(moving.style.borderRightWidth)), Is.EqualTo(("4", "2")));
        }

        [Test]
        public void Given_ARowCarryingAGapAndAPayload_When_TheGapIsDropped_Then_ThePayloadMarginReturns()
        {
            // Arrange — `.row > * { margin-left: 2px }` outlives the row's gap in CSS.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "flex flex-row gap-x-4 [&>*]:ml-[2px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var second = scope.Root[0][1];
            var spaced = Inline(second.style.marginLeft);
            var after = new VNode[]
            {
                V.Div(className: "flex flex-row [&>*]:ml-[2px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the gap's own write rides along, since a gap that never landed leaves the same slot.
            Assert.That((spaced, Inline(second.style.marginLeft)), Is.EqualTo(("16", "2")));
        }

        [Test]
        public void Given_ARowCarryingAGapAndAPayload_When_ItsFirstChildIsRemoved_Then_TheNewFirstChildKeepsOnlyThePayloadMargin()
        {
            // Arrange — keyed, so the second Label is the element that becomes first rather than a patched copy.
            using var scope = new ReconcilerScope();
            const string row = "flex flex-row gap-x-4 [&>*]:ml-[2px]";
            var before = new VNode[]
            {
                V.Div(className: row,
                    children: new VNode[] { V.Label(key: "a", text: "a"), V.Label(key: "b", text: "b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var promoted = scope.Root[0][1];
            var spaced = Inline(promoted.style.marginLeft);
            var after = new VNode[]
            {
                V.Div(className: row, children: new VNode[] { V.Label(key: "b", text: "b") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the gap it carried as the second child rides along, since the stale value is the point.
            Assert.That((spaced, Inline(promoted.style.marginLeft)), Is.EqualTo(("16", "2")));
        }

        [Test]
        public void Given_ARowCarryingAGapAndAPayload_When_ItTurnsIntoAColumn_Then_TheAbandonedEdgeKeepsThePayloadMargin()
        {
            // Arrange — plain gap-4 follows the direction, so the column moves the gap from the left edge to
            // the top one and leaves the left edge to the payload.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "flex flex-row gap-4 [&>*]:ml-[2px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var second = scope.Root[0][1];
            var after = new VNode[]
            {
                V.Div(className: "flex flex-col gap-4 [&>*]:ml-[2px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the top edge reads the gap, so a row that never became a column cannot pass.
            Assert.That((Inline(second.style.marginLeft), Inline(second.style.marginTop)),
                Is.EqualTo(("2", "16")));
        }

        [Test]
        public void Given_ARowCarryingAGapAndAPayload_When_ItRenders_Then_TheFirstChildKeepsThePayloadMargin()
        {
            // Arrange — a gap spaces BETWEEN children, so the first child's leading edge is not the gap's and
            // keeps what the payload gives it.
            using var scope = new ReconcilerScope();

            // Act
            var row = OneRow(scope, "flex flex-row gap-x-4 [&>*]:ml-[2px]");

            // Assert — the second child reads the gap, so a row whose gap never applied cannot pass.
            Assert.That((Inline(row[0].style.marginLeft), Inline(row[1].style.marginLeft)),
                Is.EqualTo(("2", "16")));
        }

        [Test]
        public void Given_APayloadWithAShorthandAndItsLonghand_When_TheGapHandsTheEdgeBack_Then_TheLonghandWins()
        {
            // Arrange — the payload names margin-left twice: through m-[4px] and through ml-[2px]. Only the
            // first child's leading edge is handed back rather than held.
            using var scope = new ReconcilerScope();

            // Act
            var row = OneRow(scope, "flex flex-row gap-x-4 [&>*]:m-[4px] [&>*]:ml-[2px]");

            // Assert — the top edge is the shorthand's alone, so a payload that never landed cannot pass.
            Assert.That((Inline(row[0].style.marginLeft), Inline(row[0].style.marginTop)),
                Is.EqualTo(("2", "4")));
        }

        // GREEN_ON_BASE(characterization): the base never re-resolved an edge the gap does not write, which a
        // hand-back re-resolving the payload's whole shorthand would do.
        [Test]
        public void Given_AGapRowWithAShorthandPayloadAndAChildsOwnLonghand_When_ItRenders_Then_TheFirstChildKeepsThePayloadsOtherEdge()
        {
            // Arrange — the payload's layer outranks the child's own mt-[8px] on the top edge, which the gap
            // never writes; only the first child's leading edge is handed back.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row gap-x-4 [&>*]:m-[4px]",
                    children: new VNode[] { V.Div(className: "mt-[8px]"), V.Div(className: "mt-[8px]") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the second child, never handed back, is the row's own answer for the same edge.
            var row = scope.Root[0];
            Assert.That((Inline(row[0].style.marginTop), Inline(row[1].style.marginTop)), Is.EqualTo(("4", "4")));
        }

        [Test]
        public void Given_AGridWithASizePayloadAndAChildsOwnHeight_When_ItHandsTheWidthBack_Then_TheHeightKeepsThePayload()
        {
            // Arrange — no layout runs here, so the grid hands every child's width back; size-[40px] outranks
            // the child's own h-[20px] on the height, which the grid never writes.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "grid grid-cols-2 [&>*]:size-[40px]",
                    children: new VNode[] { V.Div(className: "h-[20px]"), V.Div(className: "h-[20px]") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the width is the handed-back slot, so a grid that handed nothing back cannot pass.
            var child = scope.Root[0][0];
            Assert.That((Inline(child.style.width), Inline(child.style.height)), Is.EqualTo(("40", "40")));
        }

        [Test]
        public void Given_APayloadWithAShorthandAndAnotherEdgesLonghand_When_TheGapHandsOneEdgeBack_Then_TheOtherEdgeKeepsItsLonghand()
        {
            // Arrange — re-resolving m-[4px] for the handed-back left edge rewrites the top edge as well,
            // where mt-[1px] is the narrower layer.
            using var scope = new ReconcilerScope();

            // Act
            var row = OneRow(scope, "flex flex-row gap-x-4 [&>*]:m-[4px] [&>*]:mt-[1px]");

            // Assert — the handed-back edge rides along, so a payload that never landed cannot pass.
            Assert.That((Inline(row[0].style.marginLeft), Inline(row[0].style.marginTop)),
                Is.EqualTo(("4", "1")));
        }

        [Test]
        public void Given_ASolidDivideRowWithNoColor_When_ADividedChildDeclaresABorderColor_Then_TheDividerDrawsInIt()
        {
            // Arrange — divide-x sets the divider's width only, so its color is the child's own border color.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x",
                    children: new VNode[] { V.Div(className: "border-[#FF0000]"), V.Div() }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the width reads the divider, so a row whose divider never landed cannot pass.
            var divided = scope.Root[0][0];
            Assert.That((Inline(divided.style.borderRightWidth), Inline(divided.style.borderRightColor)),
                Is.EqualTo(("1", Color.red.ToString())));
        }

        [Test]
        public void Given_AColoredDivideRow_When_ItsColorIsDropped_Then_TheDividerTakesTheChildsBorderColor()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-gray-200",
                    children: new VNode[] { V.Div(className: "border-[#FF0000]"), V.Div() }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var divided = scope.Root[0][0];
            var colored = Inline(divided.style.borderRightColor);
            var after = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x",
                    children: new VNode[] { V.Div(className: "border-[#FF0000]"), V.Div() }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the divide color rides along, since a divider that never took it leaves the child's.
            Assert.That((colored == "null" || colored == Color.red.ToString(), Inline(divided.style.borderRightColor)),
                Is.EqualTo((false, Color.red.ToString())));
        }

        [Test]
        public void Given_ADivideRowCarryingABorderPayload_When_ItRenders_Then_TheLastChildKeepsThePayloadBorder()
        {
            // Arrange — the divider stops before the last child, so the last child's edge is the payload's.
            using var scope = new ReconcilerScope();

            // Act
            var row = OneRow(scope, "flex flex-row divide-x-4 [&>*]:border-r-[2px]");

            // Assert — the first child reads the divider, so a row whose divider never applied cannot pass.
            Assert.That((Inline(row[1].style.borderRightWidth), Inline(row[0].style.borderRightWidth)),
                Is.EqualTo(("2", "4")));
        }

        [Test]
        public void Given_ADivideRowCarryingABorderPayload_When_ItsLastChildIsRemoved_Then_TheNewLastChildKeepsOnlyThePayloadBorder()
        {
            // Arrange — keyed, so the first Label is the element that becomes last rather than a patched copy.
            using var scope = new ReconcilerScope();
            const string row = "flex flex-row divide-x-4 [&>*]:border-r-[2px]";
            var before = new VNode[]
            {
                V.Div(className: row,
                    children: new VNode[] { V.Label(key: "a", text: "a"), V.Label(key: "b", text: "b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var promoted = scope.Root[0][0];
            var divided = Inline(promoted.style.borderRightWidth);
            var after = new VNode[]
            {
                V.Div(className: row, children: new VNode[] { V.Label(key: "a", text: "a") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the divider it carried as the first child rides along, since the stale value is the point.
            Assert.That((divided, Inline(promoted.style.borderRightWidth)), Is.EqualTo(("4", "2")));
        }

        [Test]
        public void Given_AColoredDivideRow_When_ItRenders_Then_TheLastChildKeepsItsOwnBorderColor()
        {
            // Arrange — the divider stops before the last child, so its color is not the last child's.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row divide-x divide-gray-200",
                    children: new VNode[] { V.Div(), V.Div(className: "border-[#FF0000]") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), tree);

            // Assert — the first child reads the divider's color, so a row whose color never landed cannot pass.
            var row = scope.Root[0];
            Assert.That((Inline(row[1].style.borderRightColor), Inline(row[0].style.borderRightColor) == "null"),
                Is.EqualTo((Color.red.ToString(), false)));
        }

        [Test]
        public void Given_AWrappingGapContainerWithItsOwnMargin_When_TheGapIsDropped_Then_ItsMarginReturns()
        {
            // Arrange — a wrapping gap writes -gap/2 onto the container's own four margins.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "flex flex-row flex-wrap gap-4 m-[4px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var container = scope.Root[0];
            var whileWrapping = Inline(container.style.marginLeft);
            var after = new VNode[]
            {
                V.Div(className: "flex flex-row flex-wrap m-[4px]",
                    children: new VNode[] { V.Text("a"), V.Text("b") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — the wrap's own write rides along, since a gap that never wrapped leaves the margin.
            Assert.That((whileWrapping, Inline(container.style.marginLeft)), Is.EqualTo(("-8", "4")));
        }

        // GREEN_ON_BASE(characterization): the base nulled a gap row's first-child edge unconditionally, which
        // a hand-back writing nothing where the element keeps no layers would stop.
        [Test]
        public void Given_AnElementTheReconcilerRemovedFromAGapRow_When_ItIsReparentedAsAnotherRowsFirstChild_Then_ItsStaleGapIsCleared()
        {
            // Arrange — removal drops the element's layer map but leaves its inline gap; code that kept a
            // reference then puts it first in another gap row.
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var before = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Div(key: "a"), V.Div(key: "b") }),
                V.Div(className: Gap8Row, children: new VNode[] { V.Div(key: "c") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var removed = scope.Root[0][1];
            var after = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Div(key: "a") }),
                V.Div(className: Gap8Row, children: new VNode[] { V.Div(key: "c") }),
            };
            scope.Reconciler.Reconcile(scope.Root, before, after);
            var stale = Inline(removed.style.marginLeft);
            var other = scope.Root[1];
            other.Insert(0, removed);

            // Act
            ctx.GapManipulators[other].Apply();

            // Assert — the stale gap rides along, since an element that kept none leaves the same slot.
            Assert.That((stale, Inline(removed.style.marginLeft)), Is.EqualTo(("16", "null")));
        }

        // GREEN_ON_BASE(characterization): a pooled Label keeps nothing of the gap row it left, which a
        // hold outliving the layer map it lives in would break.
        [Test]
        public void Given_ALabelPooledOutOfAGapRow_When_ARowWithoutAGapRentsItWithItsOwnMargin_Then_TheOldGapIsNotHeld()
        {
            // Arrange — the gap row's second Label goes back to the pool and comes out as a plain row's
            // Label carrying an arbitrary margin of its own, in the same pass.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Label(text: "a"), V.Label(text: "b") }),
                V.Div(className: "flex flex-row", children: new VNode[] { V.Label(text: "c") }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            var leaving = scope.Root[0][1];
            var after = new VNode[]
            {
                V.Div(className: Gap4Row, children: new VNode[] { V.Label(text: "a") }),
                V.Div(className: "flex flex-row",
                    children: new VNode[] { V.Label(text: "c"), V.Label(className: "ml-[2px]", text: "d") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, before, after);

            // Assert — that the pool handed THAT element over rides along, since a freshly built Label never
            // carried the gap row's hold.
            var arrived = scope.Root[1][1];
            Assert.That((ReferenceEquals(leaving, arrived), Inline(arrived.style.marginLeft)),
                Is.EqualTo((true, "2")));
        }
    }

    /// <summary>
    /// The one hand-back <see cref="ChildVariantBoxOwnershipTests"/> cannot reach without layout: a grid that
    /// sized its columns and then has no row width to size them from.
    /// </summary>
    [TestFixture]
    internal sealed class ChildVariantBoxOwnershipPanelTests : PanelTestBase
    {
        [Test]
        public void Given_AGridThatSizedItsColumns_When_ItsRowWidthGoesToZero_Then_AChildGetsItsOwnWidthBack()
        {
            // Arrange — the children declare a width of their own, which the column replaces while it sizes.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "grid", className: "grid grid-cols-2 gap-4 w-[300px]",
                    children: new VNode[] { V.Div(className: "w-[8px]"), V.Div(className: "w-[8px]") }));
            var container = _window.rootVisualElement.Q<VisualElement>("grid");
            ForcePanelUpdate(container.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }
            var sizedByColumn = container[0].style.width.value.value > 100f;
            container.style.width = 0f;
            ForcePanelUpdate(container.panel);

            // Act
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }

            // Assert — the column width rides along, since a grid that never sized leaves the child's own.
            Assert.That((sizedByColumn, container[0].style.width.value.value),
                Is.EqualTo((true, 8f)));
        }

        // GREEN_ON_BASE(characterization): a child that leaves a grid that sized it keeps no column width,
        // which a release handing back nothing would stop.
        [Test]
        public void Given_AGridThatSizedAChild_When_TheChildLeavesAndTheGridRunsAgain_Then_TheColumnWidthIsReleased()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "grid", className: "grid grid-cols-2 gap-4 w-[300px]",
                    children: new VNode[] { V.Div(), V.Div() }));
            var container = _window.rootVisualElement.Q<VisualElement>("grid");
            ForcePanelUpdate(container.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }
            var leaving = container[1];
            var sizedByColumn = leaving.style.width.value.value > 100f;
            new VisualElement().Add(leaving);

            // Act
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }

            // Assert — the column width rides along, since a child the grid never sized carries none.
            Assert.That((sizedByColumn, leaving.style.width.keyword), Is.EqualTo((true, StyleKeyword.Null)));
        }
    }
}
