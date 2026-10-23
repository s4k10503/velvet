using UnityEngine.UIElements;

namespace Velvet
{
    // The flex direction a container lays its children out in, for StyleGapManipulator's gap-* margin, which
    // sits on the boundary BETWEEN two visually adjacent children. A reversed container paints its children
    // in the opposite order, so the physical edge sitting between a given pair is the trailing one rather
    // than the leading one; picking the edge from the axis alone would put the margin on an outer edge of
    // the container and leave the boundary between the visually adjacent pair unmarked.
    //
    // The element handed in is the CHILD CONTAINER (FiberNodePatcher.GetChildContainer), NOT the element a
    // manipulator is attached to. A composite widget redirects its children into an inner box, which no class
    // of the author's reaches: a direction class on the widget reaches it as an inline value
    // (StyleCompositeFlowManipulator), and without one its verdict comes from the resolvedStyle fallback
    // (ResolveWithoutClasses) below. For a plain element the two are the same element.
    //
    // After an inline flex-direction, the five direction/display classes are consulted — before
    // resolvedStyle, on a panel too outside a geometry event — in the SAME precedence USS
    // itself uses when more than one matches the element (equal specificity, so the LAST declared RULE wins):
    // _layout.uss declares .flex, .grid, .flex-col, .flex-col-reverse, .flex-row, .flex-row-reverse in that
    // source order, so flex-row-reverse beats flex-row beats flex-col-reverse beats flex-col beats the bare
    // .flex row default, regardless of which classes ended up on the element or in what order. More than one
    // still reaches the live list: StyleClassProjection drops a losing class only when a higher-priority one
    // claims every property it writes, which leaves the bare .flex beside any direction utility (it also sets
    // display) and leaves two direction utilities written at the same priority tying with each other. Checking
    // one family (row OR column) would silently pick the wrong one. This scan is the layout's mirror, not an
    // independent policy: _layout.uss
    // orders the column family before the row family so a variant can turn a column INTO a row (the
    // mobile-first idiom), and if this list stopped matching that order the spacing would point one way while
    // the element renders the other. Any change to the stylesheet block moves this list with it.
    //
    // The bare .flex is checked LAST rather than folded in with flex-row: it does set a row direction, but it
    // is declared before all four direction utilities, so it loses to every one of them — flex-col included.
    // Folding it into the row tier would make "flex flex-col" resolve as a row while it renders as a column.
    //
    // Classes are read BEFORE resolvedStyle.flexDirection outside a geometry event because
    // flex-row(-reverse) / flex-col(-reverse) are USS-only rules with no C# inline flex-direction write:
    // resolvedStyle only catches up after the panel's NEXT style pass. The live class list, by contrast, is
    // already the FINAL one by the time a class-driven patch reaches a manipulator (SyncClassDrivenStyling
    // patches it during PatchBaseElement, before the post-children passes the manipulators run in), so it is
    // synchronously correct exactly when this is needed.
    //
    // ONE mutually-exclusive verdict is returned, rather than an axis question and a reversed question
    // answered independently: a container patched straight from flex-row-reverse to flex-col-reverse (no
    // row-family class survives the patch) must forget RowReverse entirely and see ColumnReverse fresh, which
    // a same-family-only check cannot do since it never looks at the other family at all.
    //
    // StyleGapManipulator.DirectionOf reads an inline flex-direction before asking this resolver for the
    // classes' verdict.
    //
    // resolvedStyle is the fallback for flex-direction set by a custom stylesheet rule with NONE of the five
    // classes on the element. The geometry event after the next layout pass reads resolvedStyle first
    // (StyleGapManipulator.DirectionOf), which settles a rule that outranks a direction class as well.
    //
    // .grid also sets flex-direction: row in _layout.uss, and is deliberately NOT part of the scan: a grid class
    // suppresses StyleGapManipulator (FiberNodePatcher.ApplyResolvedLayoutManipulators), so no gap reads it.
    internal static class StyleFlexDirectionResolver
    {
        // The class a horizontal TwoPaneSplitView puts on its content container.
        private const string HorizontalSplitContentClass = "unity-two-pane-split-view--horizontal";

        // The verdict of the five direction/display classes; null when none is on the element.
        public static FlexDirection? FromClasses(VisualElement childContainer)
        {
            if (childContainer.ClassListContains("flex-row-reverse"))
            {
                return FlexDirection.RowReverse;
            }
            if (childContainer.ClassListContains("flex-row"))
            {
                return FlexDirection.Row;
            }
            if (childContainer.ClassListContains("flex-col-reverse"))
            {
                return FlexDirection.ColumnReverse;
            }
            if (childContainer.ClassListContains("flex-col"))
            {
                return FlexDirection.Column;
            }
            if (childContainer.ClassListContains("flex"))
            {
                return FlexDirection.Row;
            }
            return null;
        }

        // The verdict of the flex-wrap / flex-nowrap / flex-wrap-reverse markers; null when none is on the
        // element. _layout.uss declares them nowrap, wrap, wrap-reverse, so the later one wins as there.
        public static Wrap? WrapFromClasses(VisualElement element)
        {
            if (element.ClassListContains("flex-wrap-reverse"))
            {
                return Wrap.WrapReverse;
            }
            if (element.ClassListContains("flex-wrap"))
            {
                return Wrap.Wrap;
            }
            if (element.ClassListContains("flex-nowrap"))
            {
                return Wrap.NoWrap;
            }
            return null;
        }

        // The verdict when no direction/display class is on the element.
        public static FlexDirection ResolveWithoutClasses(VisualElement childContainer)
        {
            if (childContainer.panel != null)
            {
                return childContainer.resolvedStyle.flexDirection;
            }
            // Off a panel no stylesheet has resolved, so the element is the engine's column, but for the row a
            // widget's stylesheet gives an inner box the engine marks with a class of the box's own: a
            // horizontally scrolling ScrollView's and a horizontal TwoPaneSplitView's, which
            // CompositeWidgetSpacingDirectionPanelTests holds against a panel.
            return childContainer.ClassListContains(ScrollView.horizontalVariantContentUssClassName)
                || childContainer.ClassListContains(HorizontalSplitContentClass)
                ? FlexDirection.Row
                : FlexDirection.Column;
        }
    }
}
