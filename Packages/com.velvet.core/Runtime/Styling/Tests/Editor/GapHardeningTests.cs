using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression tests for the framework-level CSS-<c>gap</c> polyfill hardening (code-review findings):
    /// <list type="number">
    /// <item>gap on a contentContainer-redirecting element (ScrollView) lands on the reconciled content,
    /// not the element's internal children;</item>
    /// <item>off-panel (EditMode) plain <c>gap</c> resolves the ROW edge by default (mirrors <c>.flex</c>),
    /// not column;</item>
    /// <item>the wrap container's own negative margin survives a subsequent <c>DiffStyles</c> pass (gap is
    /// applied AFTER style diffing);</item>
    /// <item>a child moved / removed out of a gap container carries no residual gap margin;</item>
    /// <item><see cref="StyleGapManipulator.Apply"/> is a no-op when nothing relevant changed (the
    /// GeometryChanged feedback its own writes provoke does not re-churn).</item>
    /// </list>
    /// All assertions read inline <c>element.style.margin*</c> after a reconcile, off-panel.
    /// </summary>
    [TestFixture]
    internal sealed class GapHardeningTests
    {
        private const float Space2 = 8f;  // --space-2
        private const float Space4 = 16f; // --space-4

        private static VNode[] Children(int count, string keyPrefix = null)
        {
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Div(className: "child", key: keyPrefix == null ? null : keyPrefix + i);
            }
            return children;
        }

        private static VisualElement Container(VisualElement root) => root[0];

        private static StyleGapManipulator GetGapManipulator(Reconciler reconciler, VisualElement element)
        {
            var ctxField = typeof(Reconciler).GetField("_ctx", BindingFlags.NonPublic | BindingFlags.Instance);
            var ctx = ctxField.GetValue(reconciler);
            var prop = ctx.GetType().GetProperty("GapManipulators");
            var dict = (IDictionary)prop.GetValue(ctx);
            return dict.Contains(element) ? (StyleGapManipulator)dict[element] : null;
        }

        private static void InvokeApply(StyleGapManipulator manipulator)
        {
            typeof(StyleGapManipulator)
                .GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(manipulator, null);
        }

        // Bug 1: ScrollView (contentContainer-redirecting). gap must operate on the SAME container the
        // children are reconciled into — scrollView.contentContainer — not the ScrollView element itself.
        // The ScrollView indexer redirects to contentContainer, so the per-child leading margins look the
        // same either way; the load-bearing difference is the WRAP path's CONTAINER negative margin, which
        // pre-fix lands on the ScrollView's OWN margin (wrong: shifts the whole scroller) and post-fix
        // lands on the contentContainer. We assert both: content is spaced, and the container margin is on
        // the contentContainer with the ScrollView's own margin left untouched.
        [Test]
        public void Given_ScrollViewWrapGap4_When_Reconciled_Then_ContainerMarginOnContentNotScrollView()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.ScrollView("flex flex-row flex-wrap gap-4", Children(3)),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var scrollView = (ScrollView)Container(scope.Root);
            var content = scrollView.contentContainer;

            // Assert: content children carry the four-side half-margin (wrap polyfill, both axes).
            Assert.That(content.childCount, Is.EqualTo(3), "children reconcile into the contentContainer");
            for (var i = 0; i < content.childCount; i++)
            {
                Assert.That(content[i].style.marginTop.value.value, Is.EqualTo(Space4 / 2f),
                    $"content child[{i}] carries the half-margin");
            }

            // The wrap container negative margin lands on the contentContainer (where the content lives),
            // NOT on the ScrollView itself — the ScrollView's own margin stays untouched.
            Assert.That(content.style.marginTop.value.value, Is.EqualTo(-Space4 / 2f),
                "container negative margin on the contentContainer");
            Assert.That(content.style.marginRight.value.value, Is.EqualTo(-Space4 / 2f),
                "container negative margin on all sides of the contentContainer");
            Assert.That(scrollView.style.marginTop.value.value, Is.Not.EqualTo(-Space4 / 2f),
                "the ScrollView's OWN margin must NOT carry the wrap negative margin");
        }

        // Bug 1 (non-wrap): gap-y on a ScrollView spaces the reconciled content children vertically.
        [Test]
        public void Given_ScrollViewGapY2_When_Reconciled_Then_ContentChildrenSpacedVertically()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.ScrollView("gap-y-2", Children(3)),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var scrollView = (ScrollView)Container(scope.Root);
            var content = scrollView.contentContainer;

            // Assert: the reconciled content children carry the vertical leading gap.
            Assert.That(content.childCount, Is.EqualTo(3), "children reconcile into the contentContainer");
            Assert.That(content[0].style.marginTop.value.value, Is.EqualTo(0f), "first content child no leading gap");
            Assert.That(content[1].style.marginTop.value.value, Is.EqualTo(Space2), "gap before 2nd content child");
            Assert.That(content[2].style.marginTop.value.value, Is.EqualTo(Space2), "gap before 3rd content child");
        }

        // Bug 2: off-panel, bare `flex gap-4` (Auto axis, no flex-row/flex-col) must resolve the ROW edge
        // (margin-left), mirroring the new .flex=row default — NOT the old column fallback (margin-top).
        [Test]
        public void Given_BareFlexGap4_When_ReconciledOffPanel_Then_ResolvesHorizontalEdge()
        {
            // Arrange — note: NO flex-row / flex-col class, so the Auto axis falls back to the off-panel
            // class default. Pre-fix this defaulted to column; post-fix it defaults to row.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "flex gap-4", children: Children(3)) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert: horizontal (margin-left) spacing, not vertical.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "bare flex gap is horizontal (row default)");
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(0f), "first child no leading gap");
            Assert.That(container[1].style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null), "no vertical margin on a row gap");
        }

        // Bug 2 corollary: flex-col still forces the column (margin-top) edge off-panel.
        [Test]
        public void Given_FlexColGap4_When_ReconciledOffPanel_Then_StillResolvesVerticalEdge()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "flex flex-col gap-4", children: Children(3)) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4), "flex-col forces column gap");
            Assert.That(container[1].style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Null), "no horizontal margin on a column gap");
        }

        // Bug 3: the wrap container's own negative margin must survive a Color (DiffStyles) change on the
        // same element. Gap runs AFTER DiffStyles, so the manipulator's container margin write is last.
        [Test]
        public void Given_WrapGapContainer_When_StyleDiffedOnPatch_Then_ContainerNegativeMarginSurvives()
        {
            // Arrange — a wrapping gap container that also carries an inline style override (Color), so a
            // patch runs DiffStyles on the same element.
            using var scope = new ReconcilerScope();
            var styles1 = new StyleOverrides { Color = UnityEngine.Color.red };
            var tree1 = new VNode[]
            {
                V.Div(className: "flex flex-row flex-wrap gap-4", styles: styles1, children: Children(3)),
            };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            Assume.That(container.style.marginRight.value.value, Is.EqualTo(-Space4 / 2f),
                "Precondition: wrap container carries the negative half-margin");

            // Act — patch with a CHANGED style so DiffStyles writes on this element on the patch pass.
            var styles2 = new StyleOverrides { Color = UnityEngine.Color.blue };
            var tree2 = new VNode[]
            {
                V.Div(className: "flex flex-row flex-wrap gap-4", styles: styles2, children: Children(3)),
            };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert: the container's negative half-margin is intact (gap re-applied AFTER DiffStyles).
            Assert.That(container.style.marginRight.value.value, Is.EqualTo(-Space4 / 2f),
                "wrap container negative margin survives DiffStyles");
            Assert.That(container.style.marginTop.value.value, Is.EqualTo(-Space4 / 2f),
                "wrap container negative margin survives on every side");
        }

        // Bug 4: with no gap class and no existing manipulator, ApplyGapManipulator does no work and
        // registers nothing (the cheap early-out path). Proven by the absence of any inline gap margin
        // and no manipulator registration.
        [Test]
        public void Given_NoGapClass_When_Reconciled_Then_NoManipulatorRegisteredAndNoMargins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "flex flex-row", children: Children(3)) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(GetGapManipulator(scope.Reconciler, container), Is.Null, "no gap class → no manipulator");
            Assert.That(container[1].style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Null), "no gap margin written");
        }

        // Bug 5: a child removed from a gap container must not keep its inline gap margin. We capture the
        // gap manipulator and a real child, detach the child from the container, re-Apply, and assert the
        // detached (still-alive, non-pooled) child's gap margin was reset.
        [Test]
        public void Given_ChildMovedOutOfGapContainer_When_Reapplied_Then_NoResidualGapMargin()
        {
            // Arrange — a gap-x row; capture the 3rd child (which carries a leading gap margin).
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "flex flex-row gap-x-4", children: Children(3)) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            var manipulator = GetGapManipulator(scope.Reconciler, container);
            var movedChild = container[2];
            Assume.That(movedChild.style.marginLeft.value.value, Is.EqualTo(Space4),
                "Precondition: the child carries a gap margin while in the container");

            // Act — move the child out of the gap container (a sibling reparent / removal), then re-apply.
            container.Remove(movedChild);
            var sink = new VisualElement();
            sink.Add(movedChild);
            InvokeApply(manipulator);

            // Assert: the manipulator reset the gap margin on the element that left its container.
            Assert.That(movedChild.style.marginLeft.keyword, Is.EqualTo(StyleKeyword.Null),
                "reparented child carries no residual gap margin");
            // And the children still in the container keep correct spacing (2 children now).
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4), "remaining child still spaced");
        }

        // Bug 6: Apply() is a no-op when nothing relevant changed. After the initial application, a second
        // Apply() (simulating a redundant GeometryChanged tick) must not re-churn — we prove correctness
        // by mutating the inline margin and asserting a no-op Apply does NOT overwrite it (signature
        // unchanged → early return), while a real child-set change DOES re-apply.
        [Test]
        public void Given_NoRelevantChange_When_ApplyCalledAgain_Then_EarlyReturnsWithoutRewriting()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.Div(className: "flex flex-row gap-x-4", children: Children(3)) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            var manipulator = GetGapManipulator(scope.Reconciler, container);

            // Poke a sentinel value onto a margined child; a no-op Apply (no child-set / gap / edge change)
            // must NOT rewrite it back to the gap value.
            container[1].style.marginLeft = 999f;

            // Act
            InvokeApply(manipulator);

            // Assert: untouched → Apply early-returned (dirty-check held).
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(999f),
                "no relevant change → Apply is a no-op");

            // But a real child-set change must re-apply (correctness preserved).
            container.Add(new VisualElement());
            InvokeApply(manipulator);
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4),
                "a child-set change re-applies the gap (overwriting the sentinel)");
            Assert.That(container[3].style.marginLeft.value.value, Is.EqualTo(Space4),
                "the newly added child gets the gap");
        }
    }
}
