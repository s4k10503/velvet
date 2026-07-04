using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS-gap parity contract for <c>gap-*</c> / <c>gap-x-*</c> / <c>gap-y-*</c>.
    /// Unity UI Toolkit (6000.3) has no native flex <c>gap</c> and no <c>:first-child</c> /
    /// <c>:last-child</c> USS selectors, so Velvet emulates gap at the framework level via a
    /// <see cref="StyleGapManipulator"/> that writes an inter-child <em>leading</em> margin (margin-left
    /// for a row, margin-top for a column) on every child EXCEPT the first. The result is spacing
    /// BETWEEN children only — no leading, trailing, or outer-edge margin — matching CSS <c>gap</c>.
    /// <list type="bullet">
    /// <item>A <c>flex flex-row gap-x-4</c> container with 3 children gives equal horizontal spacing
    /// between children and ZERO trailing margin on the last child.</item>
    /// <item>A <c>flex flex-col gap-4</c> container gives vertical spacing with no trailing margin.</item>
    /// <item>Plain <c>gap-4</c> follows the container's flex-direction (row → horizontal, col →
    /// vertical).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The manipulator writes INLINE margins (resolved to pixels from the same scale as
    /// <c>_tokens.uss</c>), so the produced spacing is observable via <c>element.style.margin*</c>
    /// without attaching to a panel or ticking layout. The OLD <c>_gap.uss</c> (a <c>.gap-* &gt; *</c>
    /// USS child-selector that only resolves under a panel and also margins the last child) produces
    /// no inline margins at all, so these assertions fail against it and pass against the manipulator.
    /// </remarks>
    [TestFixture]
    internal sealed class GapParityTests
    {
        // --space-4 == 16px, --space-2 == 8px (see _tokens.uss).
        private const float Space4 = 16f;
        private const float Space2 = 8f;

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

        [Test]
        public void Given_FlexRowGapX4_When_Reconciled_Then_EqualLeadingMarginBetweenChildrenAndNoTrailing()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: first child has no leading margin; children 2 and 3 each carry the gap;
            // no trailing margin anywhere (margin-right stays 0 on every child, incl. the last).
            Assert.That(container.childCount, Is.EqualTo(3));
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(0f), "first child has no leading gap");
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "gap before 2nd child");
            Assert.That(container[2].style.marginLeft.value.value, Is.EqualTo(Space4), "gap before 3rd child");
            Assert.That(container[2].style.marginRight.value.value, Is.EqualTo(0f), "no trailing margin on last child");
        }

        [Test]
        public void Given_FlexColGap4_When_Reconciled_Then_VerticalLeadingMarginBetweenChildrenAndNoTrailing()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: vertical spacing via margin-top on all but the first child, no trailing margin-bottom.
            Assert.That(container[0].style.marginTop.value.value, Is.EqualTo(0f), "first child has no leading gap");
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4), "gap before 2nd child");
            Assert.That(container[2].style.marginTop.value.value, Is.EqualTo(Space4), "gap before 3rd child");
            Assert.That(container[2].style.marginBottom.value.value, Is.EqualTo(0f), "no trailing margin on last child");
        }

        [Test]
        public void Given_PlainGap4OnRow_When_Reconciled_Then_FollowsFlexDirectionHorizontally()
        {
            // Arrange — plain gap-4 on a flex-row must produce HORIZONTAL spacing (margin-left),
            // not the old vertical-only behavior.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "row gap is horizontal");
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(0f), "first child has no leading gap");
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(0f), "row gap adds no vertical margin");
        }

        [Test]
        public void Given_PlainGap2OnColumn_When_Reconciled_Then_FollowsFlexDirectionVertically()
        {
            // Arrange — plain gap on a flex-col (and the engine default column) is vertical.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col gap-2", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space2), "column gap is vertical");
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(0f), "column gap adds no horizontal margin");
        }

        [Test]
        public void Given_GapContainer_When_Reconciled_Then_RegistersOneGapManipulator()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(GetGapManipulatorCount(scope.Reconciler), Is.EqualTo(1));
        }

        [Test]
        public void Given_GapManipulator_When_GapClassPatchedAway_Then_ManipulatorRemovedAndMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(GetGapManipulatorCount(scope.Reconciler), Is.EqualTo(1),
                "Precondition: the gap class registered a manipulator");

            // Act — patch the same container without a gap class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);
            var container = Container(scope.Root);

            // Assert: manipulator gone and the leading margins it wrote are cleared.
            Assert.That(GetGapManipulatorCount(scope.Reconciler), Is.EqualTo(0));
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(0f), "gap margin cleared on removal");
        }

        [Test]
        public void Given_GapContainer_When_ChildAdded_Then_NewChildGetsLeadingMarginAndLastHasNone()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-x-4", 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);

            // Act — grow to 3 children; the manipulator must re-apply so the 3rd child gets a gap.
            var tree2 = new VNode[] { Row("flex flex-row gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container.childCount, Is.EqualTo(3));
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(0f), "first child still has no leading gap");
            Assert.That(container[2].style.marginLeft.value.value, Is.EqualTo(Space4), "added child carries the gap");
        }

        private static int GetGapManipulatorCount(Reconciler reconciler)
        {
            var ctxField = typeof(Reconciler).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(ctxField, Is.Not.Null, "_ctx field not found");
            var ctx = ctxField.GetValue(reconciler);
            var prop = ctx.GetType().GetProperty("GapManipulators");
            Assert.That(prop, Is.Not.Null, "GapManipulators property not found");
            var dict = prop.GetValue(ctx) as System.Collections.IDictionary;
            return dict?.Count ?? 0;
        }
    }
}
