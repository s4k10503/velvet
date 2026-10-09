using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements.TestFramework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

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
    /// ScrollView must land on the reconciled content, not the element itself; the Auto-axis default with no
    /// direction class is row, not column; the wrap container's own negative margin must survive a later
    /// <c>DiffStyles</c> pass since gap is applied AFTER style diffing; a child moved out of a gap container
    /// must carry no residual gap margin; <see cref="StyleGapManipulator.Apply"/> must no-op when nothing
    /// relevant changed so the GeometryChanged feedback its own writes provoke does not re-churn), and the
    /// RUNTIME axis flip: an Auto-axis gap resolves its edge from the direction class marker — the class list
    /// is consulted before <c>resolvedStyle</c> even on a panel, see <see cref="StyleFlexDirectionResolver"/>
    /// — so a re-render that swaps <c>flex-row</c> ↔ <c>flex-col</c> must move the inter-child margin to the new
    /// edge AND clear the edge it abandoned. Also specifies <c>space-x-*</c> / <c>space-y-*</c> on the same
    /// manipulator — Tailwind v4's <c>:where(&amp; &gt; :not(:last-child))</c> margin, which UITK has no
    /// selector for — on <see cref="GapAxis"/> and the <c>--space-*</c> scale like <c>gap-x-*</c> /
    /// <c>gap-y-*</c>, with the cheap <see cref="StyleGapClass.HasGapClass"/> gate recognizing it too, or it
    /// never reaches the manipulator — plus the <c>gap-x-[…]</c> / <c>gap-[…]</c> JIT arbitrary-value form.
    /// </summary>
    /// <remarks>
    /// The manipulator writes INLINE margins (resolved to pixels from the same scale as <c>_tokens.uss</c>), so
    /// the produced spacing is observable via <c>element.style.margin*</c> without attaching to a panel or
    /// ticking layout — off-panel, both direction and wrap fall back from the class markers (the only source
    /// besides an inline value) to the same defaults their on-panel <c>resolvedStyle</c> fallback would produce. That
    /// agreement extends to a composite widget's inner box, whose off-panel direction default is the
    /// engine's column rather than <c>.flex</c>'s row so that the two answers match. It does NOT extend to a
    /// widget whose built-in USS lays that box out as a row — a horizontally scrolling <c>ScrollView</c>, a
    /// <c>TwoPaneSplitView</c>, a <c>ToggleButtonGroup</c> — which only a live panel reports; that case is
    /// covered in <see cref="CompositeWidgetSpacingDirectionPanelTests"/>. A USS
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
        public void Given_GapXWrap_When_Reconciled_Then_OnlyTheColumnAxisTakesHalfMargins()
        {
            // Arrange — gap-x-4 is CSS column-gap alone: under wrap it spaces the items along each line, and
            // with no row-gap nothing separates the wrapped lines.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — the child's and the container's horizontal halves, and neither one's top edge.
            var child = container[0].style;
            Assert.That(
                (child.marginLeft.value.value, child.marginRight.value.value, child.marginTop.keyword,
                    container.style.marginLeft.value.value, container.style.marginTop.keyword),
                Is.EqualTo((Half4, Half4, StyleKeyword.Null, -Half4, StyleKeyword.Null)));
        }

        [TestCase("flex flex-row flex-wrap gap-x-4 gap-y-2")]
        [TestCase("flex flex-row flex-wrap !gap-x-4 gap-x-8 gap-y-2")]
        [TestCase("flex flex-row flex-wrap gap-x-4 gap-y-2! gap-y-8")]
        [TestCase("flex flex-row flex-wrap !gap-2 gap-x-4!")]
        public void Given_AWrappingRowWithDifferentColumnAndRowGaps_When_Reconciled_Then_EachAxisTakesItsOwnHalf(string className)
        {
            // Arrange — column-gap and row-gap choose their winning declarations independently.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            var child = container[0].style;
            Assert.That((child.marginLeft.value.value, child.marginTop.value.value), Is.EqualTo((Half4, Space2 / 2f)));
        }

        [Test]
        public void Given_ARowWithAColumnAndARowGap_When_Reconciled_Then_OnlyTheColumnGapSpacesTheLine()
        {
            // Arrange — a single-line row is spaced by column-gap; row-gap only separates wrapped lines.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4 gap-y-2", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[1].style.marginLeft.value.value, container[1].style.marginTop.keyword),
                Is.EqualTo((Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ARowWhoseFirstChildIsHidden_When_Reconciled_Then_TheFirstShownChildTakesNoGap()
        {
            // Arrange — a display:none child has no box, so CSS gap neither spaces it nor counts it as the first.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row gap-4", children: new VNode[]
                {
                    V.Div(className: "hidden"),
                    V.Div(className: "child"),
                    V.Div(className: "child"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — the next child still takes the gap, so the gap did run.
            Assert.That((container[1].style.marginLeft.keyword, container[2].style.marginLeft.value.value),
                Is.EqualTo((StyleKeyword.Null, Space4)));
        }

        [Test]
        public void Given_ARowWhoseFirstChildIsHiddenInline_When_TheRowReapplies_Then_TheFirstShownChildTakesNoGap()
        {
            // Arrange — the same box rule as the hidden utility, for an inline display:none set outside the
            // class list; the second reconcile is what re-applies the gap after it.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container[0].style.display = DisplayStyle.None;

            // Act
            var tree2 = new VNode[] { Row("flex flex-row gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the next child still takes the gap, so the gap did run.
            Assert.That((container[1].style.marginLeft.keyword, container[2].style.marginLeft.value.value),
                Is.EqualTo((StyleKeyword.Null, Space4)));
        }

        [Test]
        public void Given_AGappedRowWhoseFirstChildGoesDisplayNoneWithNoChildListChange_When_TheGapReapplies_Then_TheFirstShownChildTakesNoGap()
        {
            // Arrange — the child list stays as it was, so only the display change can tell this apply from the last
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            container[0].style.display = DisplayStyle.None;

            // Act
            InvokeApply(GetGapManipulator(scope.Reconciler, container));

            // Assert — the next child still takes the gap, so the gap did run.
            Assert.That((container[1].style.marginLeft.keyword, container[2].style.marginLeft.value.value),
                Is.EqualTo((StyleKeyword.Null, Space4)));
        }

        [Test]
        public void Given_AWrappingRowWithOnlyARowGap_When_Reconciled_Then_EachChildTakesHalfOfItAboveAndBelow()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap gap-y-4", 2) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — with no column gap the horizontal edges stay unwritten.
            var style = Container(scope.Root)[0].style;
            Assert.That((style.marginTop.value.value, style.marginBottom.value.value, style.marginLeft.keyword,
                    style.marginRight.keyword),
                Is.EqualTo((Half4, Half4, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AHiddenChildAfterAGappedOne_When_ASpaceMarginLandsOnTheGapEdge_Then_ItGivesWayToTheChildsOwnMargin()
        {
            // Arrange — space-x-reverse puts the space margin on the edge the gap uses, and a hidden child takes
            // no gap, so its margin there is the space's alone and yields to its own ml-[5px].
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row gap-x-4 space-x-2 space-x-reverse", children: new VNode[]
                {
                    V.Div(className: "child"),
                    V.Div(className: "child"),
                    V.Div(className: "child hidden ml-[5px]"),
                    V.Div(className: "child"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert — the gapped child's margin rides along: the space margin landing on the gap edge and adding
            // to the gap there is the situation the name sets up.
            var container = Container(scope.Root);
            Assert.That((container[1].style.marginLeft.value.value, container[2].style.marginLeft.value.value),
                Is.EqualTo((Space4 + Space2, 5f)));
        }

        [Test]
        public void Given_AWrappingRowWithBothGaps_When_ItsRowGapLeaves_Then_TheContainerHandsBackItsVerticalMargins()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row flex-wrap gap-4", 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var held = Container(scope.Root).style.marginTop.value.value;

            // Act
            var tree2 = new VNode[] { Row("flex flex-row flex-wrap gap-x-4", 2) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the margin before the patch rides along, since a container never held reads Null too.
            var style = Container(scope.Root).style;
            Assert.That((held, style.marginTop.keyword, style.marginBottom.keyword),
                Is.EqualTo((-Half4, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpaceRowWhoseLastChildCarriesARing_When_Reconciled_Then_ItsOverlayIsNotTheLastChild()
        {
            // Arrange — the ring overlay Velvet inserts after its element is not the author's, so the ringed
            // child stays the last one.
            using var scope = new ReconcilerScope();
            VNode RowOf() => V.Div(className: "flex flex-row space-x-4", children: new VNode[]
            {
                V.Div(className: "child"),
                V.Div(className: "child"),
                V.Div(className: "child ring-2"),
            });
            var tree1 = new VNode[] { RowOf() };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);

            // Act — re-apply the row now that the overlay is in place.
            scope.Reconciler.Context.GapManipulators[container].Apply();

            // Assert — the overlay's presence and the second child's margin ride along, so a row whose ring
            // never landed or whose space never ran cannot pass.
            Assert.That((container.childCount, container[1].style.marginRight.value.value,
                    container[2].style.marginRight.keyword),
                Is.EqualTo((4, Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpaceRowWhoseLastChildIsAbsolute_When_Reconciled_Then_TheChildBeforeItTakesTheMargin()
        {
            // Arrange — Tailwind's `:not(:last-child)` counts an absolutely positioned last child.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row space-x-2", children: new VNode[]
                {
                    V.Div(className: "child"),
                    V.Div(className: "child"),
                    V.Div(className: "absolute"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space2));
        }

        [Test]
        public void Given_ASpaceYColumnWithAZeroBottomMarginPayload_When_Reconciled_Then_ThePayloadWins()
        {
            // Arrange — `[&>*]:mb-0` outranks Tailwind's zero-specificity space rule on the edge both set.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col space-y-4 [&>*]:mb-0", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — nothing inline, so the payload's class applies; the second child's top edge rides along,
            // since Tailwind's space-y never writes it.
            Assert.That((container[0].style.marginBottom.keyword, container[1].style.marginTop.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base already lets flex-nowrap outrank flex-wrap, the order
        // _layout.uss declares them in. What reddens it is the flex-nowrap marker answering "wrap" in
        // StyleGapManipulator.WrapMarker, which puts the row on the half-margin path.
        [Test]
        public void Given_FlexWrapBesideFlexNowrap_When_ReconciledOffPanel_Then_TheLeadingMarginPathApplies()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap flex-nowrap gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexWrapReverseGap4_When_ReconciledOffPanel_Then_StillUsesTheHalfMarginPolyfill()
        {
            // Arrange — flex-wrap-reverse is a DIFFERENT string than flex-wrap, so the off-panel class-marker
            // fallback must recognize it too (the same shape of miss the flex-col / flex-col-reverse fix
            // addressed for direction) — otherwise a flex-wrap-reverse container would wrongly resolve to
            // non-wrap off-panel and use the single leading-edge margin instead of the half-margin polyfill.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap-reverse gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            AssertFourSideMargin(container[0], Half4, "child[0] under flex-wrap-reverse");
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
        // The wrap class sits on the contentContainer because that is where the wrap verdict is read from —
        // a flex-wrap in the ScrollView's own class string wraps only the ScrollView's box.
        [Test]
        public void Given_ScrollViewWrapGap4_When_Reconciled_Then_ContainerMarginOnContentNotScrollView()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.ScrollView(
                    className: "flex flex-row gap-4",
                    onCreated: el => ((ScrollView)el).contentContainer.AddToClassList("flex-wrap"),
                    children: Children(3)),
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

        // No class marker can reach the content container, so off-panel the axis comes from the inner-box
        // default, the engine's column. The on-panel twin in CompositeWidgetSpacingDirectionPanelTests
        // resolves the same container and must land on the same edge — a default disagreeing with the
        // resolved answer would let one of the two pin what the other refutes.
        [Test]
        public void Given_ScrollViewRowGap4_When_ReconciledOffPanel_Then_TheAxisFollowsTheContentContainer()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.ScrollView("flex flex-row gap-4", Children(3)),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var content = ((ScrollView)Container(scope.Root)).contentContainer;

            // Assert: vertical spacing, matching the column the content container resolves to on a panel.
            Assert.That(content[1].style.marginTop.value.value, Is.EqualTo(Space4));
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

        // The signature-widening regression guard: ComputeSignature must encode all four edges distinctly.
        // A class-driven direction toggle reaches the manipulator through UpdateGap, which unconditionally
        // forces a re-apply (bypassing the cached signature) — so the ONLY place a collapsed edge encoding
        // can matter is a re-application that does NOT go through UpdateGap, i.e. exactly what a
        // GeometryChangedEvent-triggered Apply() call is. This drives that path directly: change the class
        // list on the live element (not through the reconciler, so UpdateGap never runs), then call Apply()
        // via reflection the same way OnGeometryChanged would — a stale cached signature that collapsed Top
        // and Bottom into one bucket would make this a no-op and leave the abandoned margin in place.
        [Test]
        public void Given_AClassListChangeWithNoConfigChange_When_ApplyReruns_Then_TheMarginMovesToTheNewEdge()
        {
            // Arrange — establish the Top edge off-panel via a real reconcile.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            var manipulator = GetGapManipulator(scope.Reconciler, container);
            Assume.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4),
                "Precondition: the column gap settled on the leading (top) edge");

            // Act — flip the class list DIRECTLY (never through UpdateGap, so gap/axis/markers are
            // unchanged and the cached signature is the only thing standing between this call and the new
            // edge), then re-run Apply() the way a GeometryChangedEvent would.
            container.RemoveFromClassList("flex-col");
            container.AddToClassList("flex-col-reverse");
            InvokeApply(manipulator);

            // Assert — the stale leading (top) margin is cleared once Apply() re-derives the new edge.
            Assert.That(container[1].style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null));
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

        // --- space-x-*/space-y-* on the gap manipulator, plus the gap-x-[...] arbitrary-value form ---
        //
        // space-* is Tailwind v4's `:where(& > :not(:last-child))` margin; UITK has no such selector, so
        // StyleGapManipulator writes it. space-x-N maps to GapAxis.Horizontal + the same --space-* scale and
        // space-y-N to GapAxis.Vertical. The cheap StyleGapClass.HasGapClass gate (the patcher's early-out)
        // must also recognize the family or it never reaches the manipulator.

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
        public void Given_AChildVariantRow_When_TheReconcilerRemovesAChild_Then_TheRemovedElementCarriesNoPayload()
        {
            // Arrange — the [&>*]: twin of the gap case: a plain Div is discarded rather than pooled, and the
            // reference stands in for user code that kept it.
            using var scope = new ReconcilerScope();
            VNode RowOf(params string[] keys)
            {
                var children = new VNode[keys.Length];
                for (var i = 0; i < keys.Length; i++)
                {
                    children[i] = V.Div(className: "child", key: keys[i]);
                }
                return V.Div(className: "flex flex-row [&>*]:ml-[2px]", children: children);
            }
            var tree1 = new VNode[] { RowOf("a", "b", "c") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var removed = Container(scope.Root)[1];
            var fromPayload = removed.style.marginLeft.value.value;

            // Act
            var tree2 = new VNode[] { RowOf("a", "c") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the payload it carried rides along, since an element that never took one reads Null too.
            Assert.That((fromPayload, removed.style.marginLeft.keyword), Is.EqualTo((2f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AFlexRowGapContainer_When_AnInlineReverseDirectionIsSet_Then_TheInlineDirectionOutranksTheClass()
        {
            // Arrange — the flex-row class alone would put the gap on the leading edge.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexDirection = FlexDirection.RowReverse;

            // Act — reconciling the container again applies the spacing after its children.
            var tree2 = new VNode[] { Row("flex flex-row gap-x-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — a reversed row puts the gap on the trailing edge.
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        // GREEN_ON_BASE(characterization): the base never reads an inline direction, so the class row holds
        // there throughout. What this pins is the branch's half: clearing the inline value hands the
        // direction back to the class rather than leaving the reversed edge in place.
        [Test]
        public void Given_AFlexRowGapContainerWithAnInlineDirection_When_TheInlineDirectionIsCleared_Then_TheClassDirectionReturns()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexDirection = FlexDirection.RowReverse;
            var tree2 = new VNode[] { Row("flex flex-row gap-x-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);
            container.style.flexDirection = StyleKeyword.Null;

            // Act
            var tree3 = new VNode[] { Row("flex flex-row gap-x-4", 5) };
            scope.Reconciler.Reconcile(scope.Root, tree2, tree3);

            // Assert — a row puts the gap on the leading edge.
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AWrappingGapContainer_When_AnInlineNoWrapIsSet_Then_NoHalfMarginsAreWritten()
        {
            // Arrange — the flex-wrap class alone would select the half-margin path.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-wrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexWrap = Wrap.NoWrap;

            // Act — reconciling the container again applies the spacing after its children.
            var tree2 = new VNode[] { Row("flex flex-wrap gap-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the non-wrap path leaves the first child's margins unset, where the half-margin path writes four.
            Assert.That(container[0].style.marginRight.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ANoWrapGapContainer_When_AnInlineWrapIsSet_Then_TheHalfMarginsAreWritten()
        {
            // Arrange — the flex-nowrap class alone would select the leading-margin path.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-nowrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexWrap = Wrap.Wrap;

            // Act — reconciling the container again applies the spacing after its children.
            var tree2 = new VNode[] { Row("flex flex-nowrap gap-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the half-margin path writes the first child's right half.
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Half4));
        }

        [Test]
        public void Given_ANoWrapGapContainer_When_AnInlineWrapReverseIsSet_Then_TheHalfMarginsAreWritten()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-nowrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexWrap = Wrap.WrapReverse;

            // Act
            var tree2 = new VNode[] { Row("flex flex-nowrap gap-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Half4));
        }

        // GREEN_ON_BASE(characterization): the base never reads an inline wrap, so the class wrap holds there
        // throughout. What this pins is the branch's half: clearing the inline value hands the wrap back
        // to the class rather than leaving the row on the no-wrap path.
        [Test]
        public void Given_AWrappingGapContainerWithAnInlineNoWrap_When_TheInlineWrapIsCleared_Then_TheClassWrapReturns()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-wrap gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexWrap = Wrap.NoWrap;
            var tree2 = new VNode[] { Row("flex flex-wrap gap-4", 4) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);
            container.style.flexWrap = StyleKeyword.Null;

            // Act
            var tree3 = new VNode[] { Row("flex flex-wrap gap-4", 5) };
            scope.Reconciler.Reconcile(scope.Root, tree2, tree3);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Half4));
        }

        [Test]
        public void Given_AGapRowWithAnInlineDirection_When_EveryDirectionClassIsRemoved_Then_TheInlineDirectionIsRead()
        {
            // Arrange — flex-direction set outside the class list, which the classes no longer cover once they have left.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            container.style.flexDirection = FlexDirection.RowReverse;

            // Act
            var tree2 = new VNode[] { Row("gap-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — a reversed row puts the gap on the trailing edge.
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        // GREEN_ON_BASE(characterization): the base answers Column once every direction class has left too. What
        // this pins is the branch's half: the call that finds the classes gone sets the window itself, so a
        // manipulator that read them present only once still does not take the off-panel fallback, which is Row.
        [Test]
        public void Given_AGapRowReadOnceWithItsDirectionClass_When_EveryDirectionClassIsRemoved_Then_TheGapMovesToTheColumnEdge()
        {
            // Arrange — the manipulator's creation is the only read that finds a direction class.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);

            // Act
            var tree2 = new VNode[] { Row("gap-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — an element carrying no direction class lays out as a column.
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_Reconciled_Then_TheFirstChildCarriesTheEndMargin()
        {
            // Arrange — Tailwind v4's `:where(& > :not(:last-child))` margin-inline-end: every child but the last.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowSpaceX4_When_ClassRemovedByPatch_Then_StaleMarginsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row space-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var spaced = scope.Root[0][0].style.marginRight.value.value;

            // Act — patch the same container without the space class.
            var tree2 = new VNode[] { Row("flex flex-row", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the margin before the patch rides along, since a child never spaced reads Null too.
            Assert.That((spaced, scope.Root[0][0].style.marginRight.keyword), Is.EqualTo((Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_SpaceXReverse_When_Parsed_Then_DeclinesToParseAsAGapValue()
        {
            // Act — space-x-reverse carries no pixel gap value of its own (TryParse is the pixel-value
            // parser); it is now recognized separately as a reverse marker (see the tests below), not
            // dropped as an unsupported no-op.
            var ok = StyleGapClass.TryParse("space-x-reverse", out _, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_SpaceXReverse_When_ReverseMarkersExtracted_Then_TheHorizontalMarkerIsRecognized()
        {
            // Act — space-x-reverse is no longer an unsupported no-op: StyleGapManipulator reads it through
            // this extractor instead of the pixel-value TryParse.
            StyleGapClass.ExtractReverseMarkers(new[] { "space-x-reverse" }, out var xReverse, out var yReverse);

            // Assert
            Assert.That((xReverse, yReverse), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_SpaceYReverse_When_ReverseMarkersExtracted_Then_TheVerticalMarkerIsRecognized()
        {
            // Act
            StyleGapClass.ExtractReverseMarkers(new[] { "space-y-reverse" }, out var xReverse, out var yReverse);

            // Assert
            Assert.That((xReverse, yReverse), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_NoReverseMarkerClass_When_ReverseMarkersExtracted_Then_BothMarkersAreFalse()
        {
            // Act
            StyleGapClass.ExtractReverseMarkers(new[] { "flex", "flex-row", "gap-4" }, out var xReverse, out var yReverse);

            // Assert
            Assert.That((xReverse, yReverse), Is.EqualTo((false, false)));
        }

        // --- End-to-end: the reverse marker / row-reverse / col-reverse edge flip, driven through the
        // reconciler exactly like the rest of this file's off-panel oracle (inline margins, no panel needed
        // — StyleGapManipulator's off-panel fallback reads the flex-row-reverse / flex-col-reverse / space-*-
        // reverse class markers directly, matching the on-panel resolvedStyle.flexDirection reads exactly). ---

        [Test]
        public void Given_FlexRowSpaceXReverse_When_Reconciled_Then_TheFirstChildCarriesTheStartMargin()
        {
            // Arrange — space-x-reverse moves Tailwind's margin to margin-inline-start (margin-left), still on every child
            // but the last.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-x-4 space-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowSpaceXReverse_When_Reconciled_Then_TheLastChildCarriesNoMargin()
        {
            // Arrange — the last child takes no space margin on either horizontal edge.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-x-4 space-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[2].style.marginLeft.keyword, container[2].style.marginRight.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AnEndMarginRow_When_SpaceXReverseIsAddedByPatch_Then_TheStaleEndMarginIsCleared()
        {
            // Arrange — first reconcile establishes the END (margin-right) edge with no reverse marker.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row space-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            var spaced = container[1].style.marginRight.value.value;

            // Act — patch in space-x-reverse: the edge flips from Right to Left, so the manipulator must
            // clear the now-abandoned end margin, not just add the new start one.
            var tree2 = new VNode[] { Row("flex flex-row space-x-4 space-x-reverse", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the margin before the patch rides along, since a child never spaced reads Null too.
            Assert.That((spaced, container[1].style.marginRight.keyword), Is.EqualTo((Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_FlexRowReverseGap4_When_ReconciledOffPanel_Then_ResolvesTheTrailingHorizontalEdge()
        {
            // Arrange — plain gap-4 (Auto axis) on a flex-row-reverse container: the row-reverse direction
            // alone (no space-*-reverse marker) must move the margin to the trailing edge (margin-right).
            // This is the parity bug independent of the issue as filed: plain gap-4 was already wrong on a
            // row-reverse container.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexColReverseGap4_When_ReconciledOffPanel_Then_ResolvesTheTrailingVerticalEdge()
        {
            // Arrange — plain gap-4 on a flex-col-reverse container. Off-panel, this exercises TWO bugs at
            // once: the axis-detection fallback used to check only "flex-col" (a different string than
            // "flex-col-reverse"), resolving the wrong axis entirely, and the edge itself must land on the
            // trailing margin-bottom, not margin-top.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginBottom.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowReverseWithSpaceXReverse_When_Reconciled_Then_TheFirstChildCarriesTheStartMargin()
        {
            // Arrange — the idiom Tailwind documents for a reversed row: space-x-reverse moves the margin to margin-left.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse space-x-4 space-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexColGapY4WithSpaceXReverse_When_Reconciled_Then_TheHorizontalMarkerDoesNotAffectTheVerticalAxis()
        {
            // Arrange — a vertical gap-y-4 on a non-reversed column stays on the leading (margin-top) edge with
            // a space-x-reverse marker present.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col gap-y-4 space-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexColReverseGapX4_When_Reconciled_Then_TheColumnGapSpacesNothing()
        {
            // Arrange — gap-x-4 is column-gap alone, which a single-line column never uses: the items stack on
            // the row axis.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse gap-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[1].style.marginLeft.keyword, container[1].style.marginBottom.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_FlexRowReverseGapY4_When_Reconciled_Then_TheRowGapSpacesNothing()
        {
            // Arrange — the symmetric case: gap-y-4 is row-gap alone, which a single-line row never uses.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse gap-y-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[1].style.marginTop.keyword, container[1].style.marginRight.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_FlexColSpaceYReverse_When_Reconciled_Then_TheFirstChildCarriesTheTopMargin()
        {
            // Arrange — the vertical twin of space-x-reverse: margin-block-start (margin-top) on every child but the last.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col space-y-4 space-y-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AGapRow_When_TheReconcilerRemovesASpacedChild_Then_TheRemovedElementCarriesNoGap()
        {
            // Arrange — a plain Div is discarded rather than pooled, so no pool reset scrubs it on the way out;
            // the reference stands in for user code that kept it to re-parent later.
            using var scope = new ReconcilerScope();
            VNode RowOf(params string[] keys)
            {
                var children = new VNode[keys.Length];
                for (var i = 0; i < keys.Length; i++)
                {
                    children[i] = V.Div(className: "child", key: keys[i]);
                }
                return V.Div(className: "flex flex-row gap-x-4", children: children);
            }
            var tree1 = new VNode[] { RowOf("a", "b", "c") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var removed = Container(scope.Root)[1];
            var spaced = removed.style.marginLeft.value.value;

            // Act
            var tree2 = new VNode[] { RowOf("a", "c") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the gap it carried rides along, since an element never spaced reads Null too.
            Assert.That((spaced, removed.style.marginLeft.keyword), Is.EqualTo((Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_FlexRowReverseSpaceX4_When_Reconciled_Then_TheFirstChildCarriesTheEndMargin()
        {
            // Arrange — Tailwind's space-x never reads flex-direction: without space-x-reverse, a reversed row keeps
            // margin-right on every child but the last.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexColReverseSpaceY4_When_Reconciled_Then_TheFirstChildCarriesTheBottomMargin()
        {
            // Arrange — the vertical twin: without space-y-reverse, a reversed column keeps margin-bottom.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col-reverse space-y-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginBottom.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexColGap4WithSpaceYReverse_When_Reconciled_Then_TheGapStaysOnTheLeadingEdge()
        {
            // Arrange — CSS gap has no reverse marker, so space-y-reverse on the axis a plain gap-4 resolves
            // to leaves the gap where the direction puts it.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-col gap-4 space-y-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginTop.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexRowGapX4WithSpaceXReverse_When_Reconciled_Then_TheGapStaysOnTheLeadingEdge()
        {
            // Arrange — the fixed-axis form: space-x-reverse beside gap-x-4 on a row that is not reversed.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4 space-x-reverse", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_FlexWrapSpaceX4_When_Reconciled_Then_TheFirstChildCarriesTheWholeEndMargin()
        {
            // Arrange — Tailwind's space-x writes one margin edge per child whether or not the row wraps, so the
            // half-margin path CSS gap takes under flex-wrap is not space-x's.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row flex-wrap space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AGapXAndASpaceXOnOneRow_When_Reconciled_Then_BothApply()
        {
            // Arrange — Tailwind composes the two: the gap between the children and the space margin on them.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row gap-x-4 space-x-2", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[1].style.marginLeft.value.value, container[1].style.marginRight.value.value),
                Is.EqualTo((Space4, Space2)));
        }

        [Test]
        public void Given_AGapXAndASpaceXOnOneEdge_When_Reconciled_Then_TheMarginsAdd()
        {
            // Arrange — on a reversed row the gap moves to margin-right, the edge the space margin already has, and the
            // two add up the way a gap and a margin add up in the browser.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse gap-x-4 space-x-2", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space4 + Space2));
        }

        [Test]
        public void Given_ANegativeSpaceX_When_Reconciled_Then_TheEndMarginIsNegative()
        {
            // Arrange — Tailwind's -space-x-4.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row -space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(-Space4));
        }

        [Test]
        public void Given_ASpaceYWithoutASpaceX_When_Reconciled_Then_TheEndMarginIsBottom()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row space-y-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginBottom.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_ASpaceXRowWhoseChildrenCarryALeftMargin_When_Reconciled_Then_TheirOwnMarginIsKept()
        {
            // Arrange — space-x writes margin-inline-end only; the start edge stays the child's own.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row space-x-4", children: new VNode[]
                {
                    V.Div(className: "ml-[5px]"),
                    V.Div(className: "ml-[5px]"),
                    V.Div(className: "ml-[5px]"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginLeft.value.value, Is.EqualTo(5f));
        }

        [Test]
        public void Given_ASpaceXRowWhoseChildrenCarryARightMarginClass_When_Reconciled_Then_TheClassWins()
        {
            // Arrange — Tailwind writes space at zero specificity, so the child's own mr-2 wins on the edge both set,
            // and nothing is left inline for the class to lose to. The second child's left edge rides along:
            // Tailwind's space never writes it.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row space-x-4", children: new VNode[]
                {
                    V.Div(className: "mr-2"),
                    V.Div(className: "mr-2"),
                    V.Div(className: "mr-2"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[0].style.marginRight.keyword, container[1].style.marginLeft.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpaceXRowWhoseChildrenCarryAnArbitraryRightMargin_When_Reconciled_Then_TheirOwnValueWins()
        {
            // Arrange — the arbitrary-value twin of the case above, with the same second term.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row space-x-4", children: new VNode[]
                {
                    V.Div(className: "mr-[5px]"),
                    V.Div(className: "mr-[5px]"),
                    V.Div(className: "mr-[5px]"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That((container[0].style.marginRight.value.value, container[1].style.marginLeft.keyword),
                Is.EqualTo((5f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_ASpaceXRowWhoseChildrenCarryAnUnrelatedClass_When_Reconciled_Then_TheSpaceStillApplies()
        {
            // Arrange — a utility that sets no margin-right leaves the space margin in place.
            using var scope = new ReconcilerScope();
            var tree = new VNode[]
            {
                V.Div(className: "flex flex-row space-x-4", children: new VNode[]
                {
                    V.Div(className: "p-2"),
                    V.Div(className: "p-2"),
                    V.Div(className: "p-2"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginRight.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_ASpaceMarginYieldingToAChildsOwnValue_When_AGapJoinsItOnThatEdge_Then_TheSumIsHeld()
        {
            // Arrange — a reversed row puts a gap-x on margin-right too, where CSS adds it to the margin; a gap
            // does not give way to the child's own value the way a space margin does.
            using var scope = new ReconcilerScope();
            VNode RowOf(string className) => V.Div(className: className, children: new VNode[]
            {
                V.Div(className: "mr-[5px]"),
                V.Div(className: "mr-[5px]"),
                V.Div(className: "mr-[5px]"),
            });
            var tree1 = new VNode[] { RowOf("flex flex-row-reverse space-x-2") };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            var yielded = container[1].style.marginRight.value.value;

            // Act
            var tree2 = new VNode[] { RowOf("flex flex-row-reverse gap-x-4 space-x-2") };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the yielded value before the gap joined rides along.
            Assert.That((yielded, container[1].style.marginRight.value.value), Is.EqualTo((5f, Space4 + Space2)));
        }

        [Test]
        public void Given_AGridWithASpaceX_When_Reconciled_Then_TheSpaceIsAMarginRatherThanAColumnGap()
        {
            // Arrange — Tailwind's space-x on a grid is still its margin rule; the grid's column gap is gap-x's.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("grid grid-cols-2 space-x-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert — child 1 opens the second column, so a column gap would sit on its left.
            Assert.That((container[1].style.marginLeft.value.value, container[0].style.marginRight.value.value),
                Is.EqualTo((0f, Space4)));
        }

        [Test]
        public void Given_AGridWithASpaceY_When_Reconciled_Then_TheFirstChildCarriesTheBottomMargin()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("grid grid-cols-2 space-y-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[0].style.marginBottom.value.value, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_AStartMarginRow_When_SpaceXReverseIsRemovedByPatch_Then_TheStaleStartMarginIsCleared()
        {
            // Arrange — the mirror image of the earlier Right→Left clear test: establish the START
            // (margin-left) edge first, then patch the marker away so the edge flips back to the end.
            using var scope = new ReconcilerScope();
            var tree1 = new VNode[] { Row("flex flex-row space-x-4 space-x-reverse", 3) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree1);
            var container = Container(scope.Root);
            var spaced = container[1].style.marginLeft.value.value;

            // Act
            var tree2 = new VNode[] { Row("flex flex-row space-x-4", 3) };
            scope.Reconciler.Reconcile(scope.Root, tree1, tree2);

            // Assert — the margin before the patch rides along, since a child never spaced reads Null too.
            Assert.That((spaced, container[1].style.marginLeft.keyword), Is.EqualTo((Space4, StyleKeyword.Null)));
        }

        [Test]
        public void Given_FlexRowReverseWrapGap4_When_Reconciled_Then_StillUsesTheSymmetricHalfMarginPolyfill()
        {
            // Arrange — flex-wrap always wins over direction: a reversed container that also wraps must
            // still use the four-side half-margin polyfill (symmetric on every side), never ResolveEdge's
            // leading/trailing split, since wrapping needs both axes spaced regardless of direction.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex flex-row-reverse flex-wrap gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            AssertFourSideMargin(container[0], Half4, "child[0] in a reversed wrapping container");
        }

        [Test]
        public void Given_AnElementCarryingBothFlexColAndFlexRowReverse_When_Reconciled_Then_TheLaterDeclaredClassWins()
        {
            // Arrange — two direction classes on one element is routine once responsive/state variants are
            // involved (a base flex-col plus a variant-applied md:flex-row-reverse both land in the SAME
            // live class list once the breakpoint is met). _layout.uss declares .flex-col before
            // .flex-row-reverse, so at equal specificity the LATER rule wins the USS cascade regardless of
            // which class was written first or which one a naive "row family vs column family" check
            // happens to look at — the gap must resolve exactly as real USS would: horizontal, trailing.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row("flex-col flex-row-reverse gap-4", 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);

            // Assert
            Assert.That(container[1].style.marginRight.value.value, Is.EqualTo(Space4));
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

        [TestCase("flex flex-row gap-x-[12px]")]
        [TestCase("flex flex-row !gap-x-[12px]")]
        [TestCase("flex flex-row gap-x-[12px]!")]
        [TestCase("flex flex-row !gap-x-[12px] gap-x-8")]
        [TestCase("flex flex-row gap-x-[12px] !gap-x-[32px]!")]
        public void Given_FlexRowGapArbitrary_When_Reconciled_Then_AppliesPixelGap(string className)
        {
            // Arrange — the arbitrary form must drive the manipulator end-to-end, like the presets.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 3) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(scope.Root[0][1].style.marginLeft.value.value, Is.EqualTo(12f));
        }

        [TestCase("flex flex-row gap-2 hover:gap-4", Space4, false)]
        [TestCase("flex flex-row !gap-2 hover:!gap-4", Space4, false)]
        [TestCase("flex flex-row gap-2! hover:gap-4", Space2, false)]
        [TestCase("flex flex-row space-x-2 hover:!space-x-4", Space4, true)]
        [TestCase("flex flex-row space-x-2 hover:space-x-4!", Space4, true)]
        public void Given_AVariantSpacing_When_HoverStartsAndEnds_Then_TheWinningSpacingAppliesAndRestores(
            string className, float hoveredSpacing, bool space)
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { Row(className, 2) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var container = Container(scope.Root);
            var child = container[space ? 0 : 1];
            float ReadMargin() => space ? child.style.marginRight.value.value : child.style.marginLeft.value.value;
            var atRest = ReadMargin();

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                container.SimulateEvent(over);
            }
            var whileHover = ReadMargin();
            using (var leave = PointerOutEvent.GetPooled())
            {
                container.SimulateEvent(leave);
            }
            var afterLeave = ReadMargin();

            // Assert
            Assert.That((atRest, whileHover, afterLeave), Is.EqualTo((Space2, hoveredSpacing, Space2)));
        }
    }

    /// <summary>
    /// On-panel coverage for the row-reverse / col-reverse gap edge flip, using a real
    /// <see cref="UnityEditor.EditorWindow"/> panel (<see cref="PanelTestBase"/>) rather than the simulated
    /// one <see cref="GapReverseRuntimeFlipTests"/> below uses. <c>flex-row-reverse</c> /
    /// <c>flex-col-reverse</c> are USS-only rules (<c>_layout.uss</c>) with no C# parse path of their own —
    /// <c>resolvedStyle.flexDirection</c> never actually reports <c>RowReverse</c> without the bundled
    /// <c>StyleUtilities.uss</c> attached to a real panel. Since <see cref="StyleFlexDirectionResolver"/>
    /// reads the class markers before resolvedStyle even on a panel, the first test below is class-marker
    /// coverage on a panel, not resolvedStyle coverage — the second test is the one that actually proves the
    /// resolvedStyle FALLBACK, by withholding every direction/display class entirely.
    /// </summary>
    [TestFixture]
    internal sealed class GapReversePanelTests : PanelTestBase
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";
        private const float Space4 = 16f;

        protected override void LoadStyleSheets()
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _window.rootVisualElement.styleSheets.Add(sheet);
        }

        [Test]
        public void Given_AFlexRowReverseGapContainer_When_PanelResolves_Then_TheSecondChildCarriesTheTrailingMargin()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "flex flex-row-reverse gap-x-4", children: new VNode[]
                {
                    V.Label(name: "a", text: "a"),
                    V.Label(name: "b", text: "b"),
                }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");
            ForcePanelUpdate(row.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            row.SimulateEvent(evt);
            Assume.That(row.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved flex-row-reverse from the bundled USS");

            // Assert
            var b = _window.rootVisualElement.Q<Label>("b");
            Assert.That(b.resolvedStyle.marginRight, Is.EqualTo(Space4));
        }

        [Test]
        public void Given_ADirectionSetOnlyThroughResolvedStyle_When_PanelResolves_Then_TheGapStillFollowsIt()
        {
            // Arrange — flex-direction set via an inline style (standing in for a custom stylesheet rule),
            // with NONE of the five direction/display classes (flex / flex-row(-reverse) /
            // flex-col(-reverse)) anywhere on the element — the one case the direction resolver's class scan
            // cannot answer, so it must fall through to resolvedStyle. A VisualElement is a flex container
            // by Yoga's own default regardless of a "flex" class, so the inline flex-direction alone is
            // enough to produce a real RowReverse layout.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "row", className: "gap-x-4",
                    refCallback: el => { el.style.flexDirection = FlexDirection.RowReverse; return null; },
                    children: new VNode[]
                    {
                        V.Label(name: "a", text: "a"),
                        V.Label(name: "b", text: "b"),
                    }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");
            ForcePanelUpdate(row.panel);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            row.SimulateEvent(evt);
            Assume.That(row.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.RowReverse),
                "Precondition: the panel resolved the inline flex-direction with no direction class present");

            // Assert
            var b = _window.rootVisualElement.Q<Label>("b");
            Assert.That(b.resolvedStyle.marginRight, Is.EqualTo(Space4));
        }
    }

    /// <summary>
    /// The on-panel convergence regression guard for a class-driven direction toggle. <c>flex-row-reverse</c>
    /// / <c>flex-col-reverse</c> are USS-only rules with no C# inline <c>flex-direction</c> write, so
    /// <c>resolvedStyle.flexDirection</c> only catches up after the panel's NEXT style pass — and a
    /// same-rect direction toggle (children reorder, the container itself never resizes) fires no
    /// <c>GeometryChangedEvent</c> to trigger a re-derive. A manipulator that consulted resolvedStyle for
    /// this would converge on the FIRST toggle (the initial layout pass happens to change the rect) but
    /// never on a LATER toggle back — the margin would stay wrong indefinitely, with nothing left to
    /// correct it. <see cref="StyleGapManipulator"/> avoids this by preferring the flex-row(-reverse) /
    /// flex-col(-reverse) class markers over resolvedStyle (they are the FINAL class list by the time
    /// <c>UpdateGap</c> runs, unlike resolvedStyle), so <c>UpdateGap</c>'s own unconditional forced re-apply
    /// converges immediately, without needing any geometry event at all. This test proves exactly that: it
    /// ticks the panel after the toggle but deliberately does NOT synthesize a <c>GeometryChangedEvent</c> —
    /// a version that relied on one (or on resolvedStyle) would never converge here.
    /// </summary>
    [TestFixture]
    internal sealed class GapReverseRuntimeFlipTests
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        private const string UserSheetPath = "Assets/VelvetGapWrapsByRule.uss";

        private readonly record struct DirectionState(bool Reversed);

        private sealed class DirectionStore : Store<DirectionState>
        {
            public DirectionStore() : base(new DirectionState(false)) { }
            public void Set(bool reversed) => SetState(_ => new DirectionState(reversed));
            protected override void ResetCore() => SetState(_ => new DirectionState(false));
        }

        private static DirectionStore s_store;

        // A raw class-string store for the scenarios below that need more than a two-state boolean:
        // the bare-flex round trip and the axis-and-edge-both-flip case both toggle between class
        // strings that are not simple mirror images of each other.
        private readonly record struct ClassNameState(string ClassName);

        private sealed class ClassNameStore : Store<ClassNameState>
        {
            public ClassNameStore(string initial) : base(new ClassNameState(initial)) { }
            public void Set(string className) => SetState(_ => new ClassNameState(className));
            protected override void ResetCore() => SetState(_ => new ClassNameState(string.Empty));
        }

        private static ClassNameStore s_classNameStore;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(200, 200) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            _sim.rootVisualElement.styleSheets.Add(sheet);
            s_store = null;
            s_classNameStore = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
            AssetDatabase.DeleteAsset(UserSheetPath);
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        [Component]
        private static VNode ColumnGapRow()
        {
            var reversed = Hooks.UseStore(s_store, s => s.Reversed);
            var dir = reversed ? "flex-col-reverse" : "flex-col";
            return V.Div(name: "col", className: $"flex {dir} gap-4", children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            });
        }

        [Test]
        public void Given_AColumnGapContainer_When_FlippedToColumnReverse_Then_TheStaleTopMarginIsClearedWithNoSynthesizedEvent()
        {
            // Arrange — establish the leading (margin-top) edge on a settled column container.
            using var store = new DirectionStore();
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(ColumnGapRow, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.marginTop.value.value, Is.EqualTo(16f),
                "Precondition: the column gap settled on the leading (top) edge");

            // Act — flip to flex-col-reverse and just tick the panel; deliberately NO synthesized
            // GeometryChangedEvent. Production must converge through UpdateGap's own forced re-apply
            // (which now reads the class marker, already final at patch time) rather than depending on an
            // event that a same-rect direction toggle never actually fires.
            store.Set(true);
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the abandoned leading margin is cleared without any manufactured event.
            Assert.That(b.style.marginTop.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AColumnReverseGapContainer_When_FlippedBackToColumn_Then_TheStaleBottomMarginIsClearedWithNoSynthesizedEvent()
        {
            // Arrange — the round trip: start REVERSED (so the very first layout pass, which happens to
            // change the rect from nothing to something, cannot be the thing that makes this converge) and
            // settle on the trailing (margin-bottom) edge.
            using var store = new DirectionStore();
            s_store = store;
            store.Set(true);
            using var mounted = V.Mount(Root, V.Component(ColumnGapRow, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.marginBottom.value.value, Is.EqualTo(16f),
                "Precondition: the column-reverse gap settled on the trailing (bottom) edge");

            // Act — flip BACK to flex-col (no longer reversed) and just tick; no synthesized event. This is
            // the exact toggle direction the reviewed regression left unconverged: a resolvedStyle-only
            // read has no rect change to piggyback a natural GeometryChangedEvent on here.
            store.Set(false);
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the abandoned trailing margin is cleared without any manufactured event.
            Assert.That(b.style.marginBottom.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Component]
        private static VNode ClassDrivenGapRow()
        {
            var className = Hooks.UseStore(s_classNameStore, s => s.ClassName);
            return V.Div(name: "row", className: className, children: new VNode[]
            {
                V.Label(name: "a", text: "a"),
                V.Label(name: "b", text: "b"),
            });
        }

        // Bare `flex` is itself a direction-bearing class (_layout.uss: `.flex { flex-direction: row; }`,
        // the stylesheet's own recommended spelling for a row), so it must be part of the class scan, not
        // just flex-row(-reverse) / flex-col(-reverse) — otherwise the idiomatic
        // `reversed ? "flex flex-row-reverse gap-4" : "flex gap-4"` toggle has no marker at all in its OFF
        // state and falls through to a possibly-stale resolvedStyle.

        [Test]
        public void Given_ABareFlexGapRow_When_ToggledToRowReverse_Then_TheMarginMovesToTheTrailingEdge()
        {
            // Arrange — plain "flex gap-4": no flex-row class, just the direction-bearing bare flex.
            using var store = new ClassNameStore("flex gap-4");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.marginLeft.value.value, Is.EqualTo(16f),
                "Precondition: the bare-flex row settled on the leading (left) edge");

            // Act — toggle to flex-row-reverse; no synthesized event.
            store.Set("flex flex-row-reverse gap-4");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the margin moved to the trailing edge.
            Assert.That(b.style.marginRight.value.value, Is.EqualTo(16f));
        }

        [Test]
        public void Given_ABareFlexRowReverseGapRow_When_ToggledBackToPlainFlex_Then_TheStaleTrailingMarginIsCleared()
        {
            // Arrange — the failing direction: start reversed (a marker present in both the axis and the
            // reversed sense), then toggle to bare "flex", which carries NO reversed marker at all — the
            // OFF state of the reviewed idiom.
            using var store = new ClassNameStore("flex flex-row-reverse gap-4");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.marginRight.value.value, Is.EqualTo(16f),
                "Precondition: the reversed row settled on the trailing (right) edge");

            // Act — toggle to bare flex (no synthesized event). Without the class-marker-first fix, this
            // falls through to resolvedStyle, which is still stale RowReverse at UpdateGap time and never
            // gets a geometry event to correct it (a same-rect direction toggle moves no rect).
            store.Set("flex gap-4");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the stale trailing margin is cleared.
            Assert.That(b.style.marginRight.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AColumnReverseGapRow_When_ToggledToBareFlex_Then_TheAxisAndEdgeBothConvergeToLeft()
        {
            // Arrange — the "worse" variant: flex-col-reverse carries NEITHER a row-family class, so
            // toggling to bare "flex" must flip BOTH the axis (column -> row) and the reversed bit
            // (reversed -> not) — a single margin-left assertion proves both at once, since getting EITHER
            // one wrong leaves the left margin at 0 or Null instead of the gap value.
            using var store = new ClassNameStore("flex-col-reverse gap-4");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            Assume.That(b.style.marginBottom.value.value, Is.EqualTo(16f),
                "Precondition: the column-reverse row settled on the trailing (bottom) edge");

            // Act — toggle to bare flex (no synthesized event).
            store.Set("flex gap-4");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — both axis and edge converged to the plain row's leading (left) edge.
            Assert.That(b.style.marginLeft.value.value, Is.EqualTo(16f));
        }

        [Test]
        public void Given_AGapXRowReverseContainer_When_ToggledToColumnReverse_Then_TheColumnGapLeavesTheItems()
        {
            // Arrange — the cross-axis hole: a gap-x-4 container starts flex-row-reverse (trailing =
            // margin-right), then patches straight to flex-col-reverse — no row-family class survives the patch
            // at all, so a same-family-only check would keep the stale Right edge. In a single-line column the
            // column gap spaces nothing.
            using var store = new ClassNameStore("flex-row-reverse gap-x-4");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            var spaced = b.style.marginRight.value.value;

            // Act — patch straight to flex-col-reverse (no synthesized event).
            store.Set("flex-col-reverse gap-x-4");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the row's gap before the patch rides along.
            Assert.That((spaced, b.style.marginRight.keyword, b.style.marginLeft.keyword),
                Is.EqualTo((16f, StyleKeyword.Null, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AFixedSizeWrappingGapRow_When_FlexWrapIsRemoved_Then_TheContainerMarginIsHandedBackWithNoSynthesizedEvent()
        {
            // Arrange — a fixed size keeps the container's rect, so no geometry event follows the style pass
            // that drops the wrap.
            using var store = new ClassNameStore("flex flex-row flex-wrap gap-4 w-[150px] h-[40px]");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var row = Root.Q<VisualElement>("row");
            var wrapped = row.style.marginLeft.value.value;

            // Act — drop flex-wrap without spelling flex-nowrap; no synthesized event.
            store.Set("flex flex-row gap-4 w-[150px] h-[40px]");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the wrap path's negative half-margin rides along, since a container never wrapped
            // reads Null too.
            Assert.That((wrapped, row.style.marginLeft.keyword), Is.EqualTo((-8f, StyleKeyword.Null)));
        }

        [Test]
        public void Given_AFixedSizeGapRow_When_EveryDirectionClassIsRemoved_Then_TheGapMovesToTheColumnEdgeWithNoSynthesizedEvent()
        {
            // Arrange — with neither flex nor a direction utility left, the container lays out in the engine's
            // column; a fixed size keeps its rect, so no geometry event follows the style pass that says so.
            using var store = new ClassNameStore("flex flex-row gap-4 w-[150px] h-[60px]");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            var spaced = b.style.marginLeft.value.value;

            // Act — drop every direction/display class; no synthesized event.
            store.Set("gap-4 w-[150px] h-[60px]");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();

            // Assert — the row's gap before the change rides along, since a child never spaced reads 0 too.
            Assert.That((spaced, b.style.marginTop.value.value), Is.EqualTo((16f, 16f)));
        }

        [Component]
        private static VNode ClassDrivenLabel()
        {
            var className = Hooks.UseStore(s_classNameStore, s => s.ClassName);
            return V.Label(name: "a", className: className, text: "a");
        }

        [Component]
        private static VNode SpaceColumnWithAClassDrivenChild()
            => V.Div(name: "col", className: "flex flex-col space-y-4", children: new VNode[]
            {
                V.Component(ClassDrivenLabel, key: "a"),
                V.Label(name: "b", text: "b"),
            });

        [Test]
        public void Given_ASpaceColumn_When_AChildRerendersAloneWithABottomMarginClass_Then_TheClassWins()
        {
            // Arrange — only the child's own component re-renders, so the column's patch never runs; the class
            // change on the child is what has to bring the space margin's yield back into question.
            using var store = new ClassNameStore("child");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(SpaceColumnWithAClassDrivenChild, key: "col"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            var a = Root.Q<Label>("a");
            var spaced = a.style.marginBottom.value.value;

            // Act
            store.Set("child mb-8");
            scheduler.DrainImmediateForTest();

            // Assert — the space margin before the child's own class arrived rides along.
            Assert.That((spaced, a.style.marginBottom.keyword), Is.EqualTo((16f, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base reads a stylesheet's wrap through resolvedStyle once a
        // layout pass has run. What reddens it is OnGeometryChanged leaving the wrap class-left window open,
        // which keeps answering from the inline flex-wrap, unset here, instead of from resolvedStyle.
        [Test]
        public void Given_ARowThatAStylesheetRuleWraps_When_ItsWrapClassLeaves_Then_TheRuleIsReadAfterTheNextLayout()
        {
            // Arrange — the rule wraps the row too, so dropping the class marker must end back on the wrap path
            // once resolvedStyle has caught up.
            File.WriteAllText(UserSheetPath, ".test-wraps-by-rule { flex-wrap: wrap; }\n");
            AssetDatabase.ImportAsset(UserSheetPath, ImportAssetOptions.ForceSynchronousImport);
            Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(UserSheetPath));
            using var store = new ClassNameStore("flex flex-row flex-wrap test-wraps-by-rule gap-4 w-[150px] h-[60px]");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");

            // Act
            store.Set("flex flex-row test-wraps-by-rule gap-4 w-[150px] h-[60px]");
            scheduler.DrainImmediateForTest();
            Tick();
            Tick();
            Tick();

            // Assert — the half-margin: the leading path writes no margin-top on a row.
            Assert.That(b.style.marginTop.value.value, Is.EqualTo(8f));
        }

        // GREEN_ON_BASE(characterization): the base answers from the inline flex-wrap, unset here, once a wrap
        // class has left too. What this pins is the branch's half: the call that finds the marker gone sets the
        // window itself, so a manipulator that read the marker present only once does not take resolvedStyle,
        // which the rule keeps at wrap.
        [Test]
        public void Given_ARowThatAStylesheetRuleWrapsAndTheWrapClassIsReadOnce_When_ItIsRemoved_Then_TheRowLeavesTheWrapPathBeforeAnyLayout()
        {
            // Arrange — the gap and the wrap class arrive together, after the panel has resolved the rule's wrap,
            // so adding the manipulator is the only read that finds the marker and no layout pass follows it.
            File.WriteAllText(UserSheetPath, ".test-wraps-by-rule { flex-wrap: wrap; }\n");
            AssetDatabase.ImportAsset(UserSheetPath, ImportAssetOptions.ForceSynchronousImport);
            Root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(UserSheetPath));
            using var store = new ClassNameStore("flex flex-row test-wraps-by-rule w-[150px] h-[60px]");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(ClassDrivenGapRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            store.Set("flex flex-row flex-wrap test-wraps-by-rule gap-4 w-[150px] h-[60px]");
            scheduler.DrainImmediateForTest();

            // Act — drop the marker; no panel pass.
            store.Set("flex flex-row test-wraps-by-rule gap-4 w-[150px] h-[60px]");
            scheduler.DrainImmediateForTest();

            // Assert — the leading path writes no margin-top on a row.
            Assert.That(b.style.marginTop.value.value, Is.EqualTo(0f));
        }

        [Component]
        private static VNode InlineWrapClassRow()
        {
            var className = Hooks.UseStore(s_classNameStore, s => s.ClassName);
            return V.Div(name: "row", className: className,
                refCallback: el => { el.style.flexWrap = Wrap.Wrap; return null; },
                children: new VNode[]
                {
                    V.Label(name: "a", text: "a"),
                    V.Label(name: "b", text: "b"),
                });
        }

        // GREEN_ON_BASE(characterization): the base reads the inline wrap through resolvedStyle at once. What
        // reddens it is IsWrap's class-left window answering without the inline flex-wrap, which drops the row
        // to the leading path until the next geometry event.
        [Test]
        public void Given_AnInlineWrapBesideAFlexWrapClass_When_TheClassIsRemoved_Then_TheInlineWrapStillHolds()
        {
            // Arrange — the inline style still wraps once the class goes, so the gap must stay on the wrap path.
            using var store = new ClassNameStore("flex flex-row flex-wrap gap-4");
            s_classNameStore = store;
            using var mounted = V.Mount(Root, V.Component(InlineWrapClassRow, key: "row"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Tick();
            Tick();
            var b = Root.Q<Label>("b");
            var wrapped = b.style.marginTop.value.value;

            // Act — the patch alone, before any panel pass could correct it.
            store.Set("flex flex-row gap-4");
            scheduler.DrainImmediateForTest();

            // Assert — the half-margin before and after: the leading path writes no margin-top on a row.
            Assert.That((wrapped, b.style.marginTop.value.value), Is.EqualTo((8f, 8f)));
        }

        [Component]
        private static VNode InlineWrapRow()
        {
            // "flex flex-row" is present (the realistic, idiomatic shape — nearly every real container
            // carries a direction class) and NO flex-wrap class at all: the inline flex-wrap set below is
            // the only source that can possibly answer, exactly the "wrap set some other way" case a
            // direction class must NOT paper over.
            var children = new VNode[3];
            for (var i = 0; i < 3; i++)
            {
                children[i] = V.Div(name: "child-" + i, className: "w-[40px] h-[20px]");
            }
            return V.Div(name: "row", className: "flex flex-row w-[60px] gap-4",
                refCallback: el => { el.style.flexWrap = Wrap.Wrap; return null; },
                children: children);
        }

        [Test]
        public void Given_AnInlineFlexWrapWithNoWrapClass_When_ThePanelSettles_Then_TheHalfMarginPolyfillAppliesWithoutASynthesizedEvent()
        {
            // Arrange/Act — a 60px-wide row with three 40px children set to wrap only via an inline style
            // (no flex-wrap class at all): wrapping genuinely changes the container's own measured height
            // (one line of content vs. three stacked lines), unlike a same-size direction toggle, so the
            // resulting GeometryChangedEvent is a REAL one this test deliberately never synthesizes.
            using var mounted = V.Mount(Root, V.Component(InlineWrapRow, key: "row"));
            Tick();
            Tick();
            Tick();

            // Assert — the container carries the wrap path's own negative half-margin, proving the
            // resolvedStyle fallback (not a class) correctly detected wrapping and self-corrected.
            var row = Root.Q<VisualElement>("row");
            Assert.That(row.style.marginRight.value.value, Is.EqualTo(-8f));
        }
    }
}
