using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS-gap parity contract under <c>flex-wrap</c>, where native <c>gap</c> spaces
    /// BOTH axes (between items in a line and between wrapped lines). A single leading-edge margin can
    /// only space the main axis, so <see cref="StyleGapManipulator"/> switches to the classic
    /// wrap-compatible half-margin polyfill: <c>gap/2</c> on all four sides of every child and
    /// <c>-gap/2</c> on all four sides of the container, yielding <c>gap</c> between any two adjacent
    /// items in either axis while keeping content flush to the container edge.
    /// <list type="bullet">
    /// <item>A <c>flex flex-row flex-wrap gap-4</c> container gives every child a four-side margin of
    /// gap/2 and the container a four-side margin of -gap/2.</item>
    /// <item>Axis-specific <c>gap-x</c> / <c>gap-y</c> under wrap behave identically (the half-margin
    /// polyfill spaces both axes regardless of the requested axis — wrapping needs both).</item>
    /// <item>Non-wrap containers are untouched by this path (covered by <c>GapParityTests</c>).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The half-margin path writes layout-independent margins, so every assertion here reads inline
    /// <c>element.style.margin*</c> after a reconcile WITHOUT attaching to a panel or ticking layout —
    /// wrap is detected off-panel via the <c>flex-wrap</c> class marker.
    /// </remarks>
    [TestFixture]
    internal sealed class GapWrapParityTests
    {
        // --space-4 == 16px (see _tokens.uss); half-margin == 8px, container == -8px.
        private const float Space4 = 16f;
        private const float Half4 = 8f;

        private static VNode Row(string className, int childCount)
        {
            var children = new VNode[childCount];
            for (var i = 0; i < childCount; i++)
            {
                children[i] = V.Div(className: "child");
            }
            return V.Div(className: className, children: children);
        }

        private static VisualElement Container(VisualElement root) => root[0];

        private static void AssertFourSideMargin(VisualElement element, float value, string label)
        {
            Assert.That(element.style.marginLeft.value.value, Is.EqualTo(value), $"{label}: marginLeft");
            Assert.That(element.style.marginRight.value.value, Is.EqualTo(value), $"{label}: marginRight");
            Assert.That(element.style.marginTop.value.value, Is.EqualTo(value), $"{label}: marginTop");
            Assert.That(element.style.marginBottom.value.value, Is.EqualTo(value), $"{label}: marginBottom");
        }

        [Test]
        public void Given_FlexRowWrapGap4_When_Reconciled_Then_EveryChildHasHalfMarginAllSides()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap gap-4", 4) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: every child (incl. the first) carries gap/2 on all four sides — wrap needs both axes.
            Assert.That(container.childCount, Is.EqualTo(4));
            for (var i = 0; i < container.childCount; i++)
            {
                AssertFourSideMargin(container[i], Half4, $"child[{i}]");
            }
        }

        [Test]
        public void Given_FlexRowWrapGap4_When_Reconciled_Then_ContainerHasNegativeHalfMarginAllSides()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap gap-4", 4) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: the container cancels the children's outer-edge half-margins with -gap/2 on all sides.
            AssertFourSideMargin(container, -Half4, "container");
        }

        [Test]
        public void Given_GapXWrap_When_Reconciled_Then_StillSpacesBothAxesViaHalfMargin()
        {
            // Arrange — under wrap the axis hint is irrelevant: wrapping requires both axes spaced,
            // so gap-x-4 + flex-wrap uses the same four-side half-margin polyfill as plain gap.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            AssertFourSideMargin(container[0], Half4, "child[0]");
            AssertFourSideMargin(container[2], Half4, "child[2]");
            AssertFourSideMargin(container, -Half4, "container");
        }

        [Test]
        public void Given_WrapContainer_When_WrapClassPatchedAway_Then_HalfMarginsClearedToLeading()
        {
            // Arrange — wrap container applies the half-margin set (incl. container negative margin).
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row flex-wrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            Assume.That(container.style.marginRight.value.value, Is.EqualTo(-Half4),
                "Precondition: container carries the wrap negative margin");

            // Act — drop flex-wrap; the manipulator must flip to the non-wrap leading path and clear the
            // four-side half-margins AND the container's negative margins it wrote.
            var tree2 = new VNode[] { Row("flex flex-row gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert: container margins fully cleared; children back to leading-margin-only spacing.
            AssertFourSideMargin(container, 0f, "container after un-wrap");
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(0f), "first child no leading gap");
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "gap before 2nd child");
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(0f), "no residual right half-margin");
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(0f), "no residual top half-margin");
        }

        [Test]
        public void Given_WrapContainer_When_GapClassPatchedAway_Then_AllHalfMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row flex-wrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);

            // Act — remove the gap class entirely; the manipulator is removed and must leave no residue,
            // including the container's negative margins.
            var tree2 = new VNode[] { Row("flex flex-row flex-wrap", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            AssertFourSideMargin(container, 0f, "container after gap removed");
            AssertFourSideMargin(container[1], 0f, "child after gap removed");
        }

        [Test]
        public void Given_NonWrapGapX4_When_Reconciled_Then_CrossAxisEdgesLeftUntouchedSoChildMarginsCompose()
        {
            // The non-wrap leading path writes ONLY the gap edge (margin-left for a row); it must never
            // touch the cross-axis edges, so a child's own cross-axis margin (e.g. mt-2, which resolves
            // from USS on a panel) is free to compose. Off-panel we prove this by asserting the gap path
            // leaves the children's marginTop / marginBottom / marginRight as inline `Null` — i.e. it
            // sets no inline value there, leaving those edges for the cascade.

            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: gap is on the leading edge; every other edge is left at keyword Null (untouched),
            // so an explicit cross-axis child margin would compose rather than be clobbered.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "gap on the leading edge");
            for (var i = 0; i < container.childCount; i++)
            {
                Assert.That(container[i].style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null),
                    $"child[{i}] marginTop untouched (cross-axis composes)");
                Assert.That(container[i].style.marginBottom.keyword, Is.EqualTo(StyleKeyword.Null),
                    $"child[{i}] marginBottom untouched");
                Assert.That(container[i].style.marginRight.keyword, Is.EqualTo(StyleKeyword.Null),
                    $"child[{i}] marginRight untouched (no trailing gap)");
            }
        }
    }
}
