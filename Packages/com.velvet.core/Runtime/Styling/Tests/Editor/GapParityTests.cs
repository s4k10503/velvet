using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

using Velvet;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS-gap parity contract for <c>gap-*</c> / <c>gap-x-*</c> / <c>gap-y-*</c>. Unity UI
    /// Toolkit (6000.3) has no native flex <c>gap</c> and no <c>:first-child</c> / <c>:last-child</c> USS
    /// selectors, so Velvet emulates gap at the framework level via a <see cref="StyleGapManipulator"/> that
    /// writes an inter-child <em>leading</em> margin (margin-left for a row, margin-top for a column) on every
    /// child EXCEPT the first — spacing BETWEEN children only, matching CSS <c>gap</c>, with the axis following
    /// the container's flex-direction for a plain <c>gap-4</c>. Under <c>flex-wrap</c>, native <c>gap</c> spaces
    /// BOTH axes (between items in a line and between wrapped lines); a single leading-edge margin cannot do
    /// that alone, so the manipulator switches to the classic wrap-compatible half-margin polyfill instead:
    /// <c>gap/2</c> on all four sides of every child and <c>-gap/2</c> on all four sides of the container. This
    /// also covers a handful of hardening regressions (gap on a contentContainer-redirecting element like
    /// ScrollView must land on the reconciled content, not the element itself; the off-panel Auto-axis default
    /// is row, not column; the wrap container's own negative margin must survive a later <c>DiffStyles</c> pass
    /// since gap is applied AFTER style diffing; a child moved out of a gap container must carry no residual
    /// gap margin; <see cref="StyleGapManipulator.Apply"/> must no-op when nothing relevant changed so the
    /// GeometryChanged feedback its own writes provoke does not re-churn), and the RUNTIME axis flip: an
    /// Auto-axis gap resolves its edge from the direction class marker off-panel, so a re-render that swaps
    /// <c>flex-row</c> ↔ <c>flex-col</c> must move the inter-child margin to the new edge AND clear the edge it
    /// abandoned. Also specifies the <c>space-x-*</c> / <c>space-y-*</c> alias onto the same gap machinery
    /// (<c>space-*</c> is CSS's own inter-child-margin selector, <c>&gt; * + *</c>, which UITK has no equivalent
    /// for) — the alias maps onto <see cref="GapAxis"/> and the <c>--space-*</c> scale exactly like <c>gap-x-*</c>
    /// / <c>gap-y-*</c>, and the cheap <see cref="StyleGapClass.HasGapClass"/> gate must recognize it too, or it
    /// never reaches the manipulator — plus the <c>gap-x-[…]</c> / <c>gap-[…]</c> JIT arbitrary-value form.
    /// </summary>
    /// <remarks>
    /// The manipulator writes INLINE margins (resolved to pixels from the same scale as <c>_tokens.uss</c>), so
    /// the produced spacing is observable via <c>element.style.margin*</c> without attaching to a panel or
    /// ticking layout — wrap is likewise detected off-panel via the <c>flex-wrap</c> class marker. A USS
    /// child-selector approach to gap would resolve only under a live panel and produce no inline margins at
    /// all, so these off-panel assertions are a meaningful discriminator against that class of implementation.
    /// </remarks>
    [TestFixture]
    internal sealed class GapParityTests
    {
        // --space-4 == 16px, --space-2 == 8px (see _tokens.uss); half-margin == 8px, container == -8px.
        private const float Space4 = 16f;
        private const float Space2 = 8f;
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

        private static VNode[] Children(int count, string keyPrefix = null)
        {
            var children = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                children[i] = V.Div(className: "child", key: keyPrefix == null ? null : keyPrefix + i);
            }
            return children;
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

        private readonly record struct ModeState(int Mode);

        private sealed class ModeStore : Store<ModeState>
        {
            public ModeStore() : base(new ModeState(0)) { }
            public void Set(int mode) => SetState(_ => new ModeState(mode));
            protected override void ResetCore() => SetState(_ => new ModeState(0));
        }

        private static ModeStore s_store;
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        // Mode 0: row direction; mode 1: column direction. Plain gap-4 = Auto axis (follows direction).
        [Component]
        private static VNode Host()
        {
            var mode = Hooks.UseStore(s_store, s => s.Mode);
            var dir = mode == 0 ? "flex-row" : "flex-col";
            return V.Div(name: "host", className: $"flex {dir} gap-4", children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            });
        }

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

        // Bug 1: ScrollView (contentContainer-redirecting). gap must operate on the SAME container the
        // children are reconciled into — scrollView.contentContainer — not the ScrollView element itself.
        // The ScrollView indexer redirects to contentContainer, so the per-child leading margins look the
        // same either way; the load-bearing difference is the WRAP path's CONTAINER negative margin, which
        // must land on the contentContainer (where the content lives), not the ScrollView's own margin —
        // landing there would shift the whole scroller instead of just the content. We assert both: content
        // is spaced, and the container margin is on the contentContainer with the ScrollView's own margin
        // left untouched.
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
        // (margin-left): flex containers default to row, so the Auto axis's off-panel fallback must match
        // that default rather than assuming column.
        [Test]
        public void Given_BareFlexGap4_When_ReconciledOffPanel_Then_ResolvesHorizontalEdge()
        {
            // Arrange — note: NO flex-row / flex-col class, so the Auto axis falls back to the off-panel
            // class default, which resolves to row (flex's own default direction), not column.
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

        [Test]
        public void Given_AnAutoGapRow_When_Mounted_Then_TheSecondChildHasAHorizontalLeadingMargin()
        {
            // Arrange/Act — a flex-row gap-4 container is mounted.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));

            // Assert — the inter-child gap lands on the horizontal (left) edge of the second child.
            Assert.AreNotEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginLeft.keyword);
        }

        [Test]
        public void Given_AnAutoGapRow_When_FlippedToColumn_Then_TheSecondChildGainsAVerticalLeadingMargin()
        {
            // Arrange — a row gap container, then flipped to column by a re-render.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("b").style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null),
                "Precondition: no vertical margin while in row direction");

            // Act — the direction flips to column.
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the gap moves to the vertical (top) edge of the second child.
            Assert.AreNotEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginTop.keyword);
        }

        [Test]
        public void Given_AnAutoGapRow_When_FlippedToColumn_Then_TheAbandonedHorizontalMarginIsCleared()
        {
            // Arrange — a row gap container, then flipped to column by a re-render.
            using var store = new ModeStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(Host, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("b").style.marginLeft.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: horizontal margin present while in row direction");

            // Act — the direction flips to column.
            store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the stale horizontal margin is cleared (no leftover from the abandoned axis).
            Assert.AreEqual(StyleKeyword.Null, _root.Q<Label>("b").style.marginLeft.keyword);
        }

        // --- space-x-*/space-y-* alias onto the gap machinery, plus the gap-x-[...] arbitrary-value form ---
        //
        // space-* is an inter-child margin (> * + *); UITK has no such selector, and StyleGapManipulator already
        // writes exactly that leading margin on every child but the first, so space-x-N maps to
        // GapAxis.Horizontal + the same --space-* scale and space-y-N to GapAxis.Vertical. The cheap
        // StyleGapClass.HasGapClass gate (the patcher's early-out) must also recognize the alias or it never
        // reaches the manipulator.

        [Test]
        public void Given_SpaceX4Class_When_Parsed_Then_MapsToHorizontalGapAxis()
        {
            // Act
            var ok = StyleGapClass.TryParse("space-x-4", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((Space4, GapAxis.Horizontal)));
        }

        [Test]
        public void Given_SpaceY2Class_When_Parsed_Then_MapsToVerticalGapAxis()
        {
            // Act
            var ok = StyleGapClass.TryParse("space-y-2", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((8f, GapAxis.Vertical)));
        }

        [Test]
        public void Given_SpaceXClass_When_HasGapClassProbed_Then_GateReturnsTrue()
        {
            // Act — the FiberNodePatcher early-out depends on this gate recognizing the alias.
            var has = StyleGapClass.HasGapClass(new[] { "space-x-4" });

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_UnknownSpaceSuffix_When_Parsed_Then_DeclinesToParse()
        {
            // Act — a suffix outside the --space-* scale is not a recognized gap utility.
            var ok = StyleGapClass.TryParse("space-x-999", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_Reconciled_Then_LeadingMarginBetweenChildren()
        {
            // Arrange — mirrors the gap-x-4 parity test: the alias must drive the manipulator end-to-end.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = scope.Root[0];

            // Assert — 2nd child carries the gap; the first has no leading margin.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_ClassRemovedByPatch_Then_StaleMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row space-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            Assume.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(Space4), "Precondition: gap applied");

            // Act — patch the same container without the space class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the manipulator's leading margin is cleared (no ghost via the shared gap path).
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(0f));
        }

        [Test]
        public void Given_SpaceXReverse_When_Parsed_Then_DeclinesToParse()
        {
            // Act — space-x-reverse has no gap analog; it is an intentional no-op (locked here).
            var ok = StyleGapClass.TryParse("space-x-reverse", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_GapXArbitraryPixel_When_Parsed_Then_ResolvesPixelGapOnHorizontalAxis()
        {
            // Act — JIT arbitrary value: gap-x-[12px].
            var ok = StyleGapClass.TryParse("gap-x-[12px]", out var gap, out var axis);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gap utility");
            Assert.That((gap, axis), Is.EqualTo((12f, GapAxis.Horizontal)));
        }

        [Test]
        public void Given_GapArbitraryPercent_When_Parsed_Then_DeclinesToParse()
        {
            // Act — gap is a pixel inter-child margin; a percentage is not meaningful.
            var ok = StyleGapClass.TryParse("gap-[50%]", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_FlexRowGapArbitrary_When_Reconciled_Then_AppliesPixelGap()
        {
            // Arrange — the arbitrary form must drive the manipulator end-to-end, like the presets.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-[12px]", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(12f));
        }
    }
}
