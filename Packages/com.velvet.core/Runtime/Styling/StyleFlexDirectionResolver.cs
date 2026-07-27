using UnityEngine.UIElements;

namespace Velvet
{
    // The flex direction a container lays its children out in, for the manipulators that place something on
    // the boundary BETWEEN two visually adjacent children (StyleGapManipulator's inter-child margin,
    // StyleDivideManipulator's inter-child border). A reversed container paints its children in the opposite
    // order, so the physical edge sitting between a given pair is the trailing one rather than the leading
    // one; a manipulator that picked its edge from the axis alone would put its margin / border on an outer
    // edge of the container and leave the boundary between the visually adjacent pair unmarked.
    //
    // The element handed in is the CHILD CONTAINER — the one the spaced children are actually parented to,
    // which is FiberNodePatcher.GetChildContainer's answer. It is deliberately NOT the element a manipulator
    // is attached to. A composite widget (ScrollView, Foldout, TabView, Tab, TwoPaneSplitView) redirects its
    // children into an inner box, so a direction class written on the widget lays out the WIDGET's own box —
    // a ScrollView's viewport and scrollers, a Foldout's toggle above its content — while the children being
    // spaced sit one level down under the inner box's own direction. An edge picked from the attached
    // element would then move the margin / border to the trailing side of a pair that is not painted in
    // reverse order at all. For a plain element the two are the same element, so this is the same read.
    //
    // Consequence for every redirecting widget: a class string only ever reaches the element it is written
    // on, so none of the five direction classes can land on an inner box and its verdict always comes from
    // the resolvedStyle fallback (or, off-panel, the widgetOwned default) below. That is the honest answer —
    // what lays an inner box out is the widget's own built-in USS, which no Velvet utility reaches.
    //
    // The five direction/display classes are consulted FIRST — even on a panel — in the SAME precedence USS
    // itself uses when more than one matches the element (equal specificity, so the LAST declared RULE wins):
    // _layout.uss declares .flex, .grid, .flex-col, .flex-col-reverse, .flex-row, .flex-row-reverse in that
    // source order, so flex-row-reverse beats flex-row beats flex-col-reverse beats flex-col beats the bare
    // .flex row default, regardless of which classes ended up on the element or in what order — a
    // responsive/state variant routinely leaves TWO direction classes on the live list at once (e.g.
    // "flex flex-col md:flex-row" above the breakpoint), and only checking one family (row OR column) would
    // silently pick the wrong one. This scan is the layout's mirror, not an independent policy: _layout.uss
    // orders the column family before the row family so a variant can turn a column INTO a row (the
    // mobile-first idiom), and if this list stopped matching that order the spacing would point one way while
    // the element renders the other. Any change to the stylesheet block moves this list with it.
    //
    // The bare .flex is checked LAST rather than folded in with flex-row: it does set a row direction, but it
    // is declared before all four direction utilities, so it loses to every one of them — flex-col included.
    // Folding it into the row tier would make "flex flex-col" resolve as a row while it renders as a column.
    //
    // Classes are read BEFORE resolvedStyle.flexDirection because flex-row(-reverse) / flex-col(-reverse) are
    // USS-only rules with no C# inline flex-direction write: resolvedStyle only catches up after the panel's
    // NEXT style pass, and a flip between two same-size directions moves no rect, so no GeometryChangedEvent
    // ever arrives to trigger a re-derive — a toggle driven through resolvedStyle could converge once by luck
    // (an unrelated rect change) and never again. The live class list, by contrast, is already the FINAL one
    // by the time a class-driven patch reaches a manipulator (SyncClassDrivenStyling patches it during
    // PatchBaseElement, before the post-children passes the manipulators run in), so it is synchronously
    // correct exactly when this is needed.
    //
    // ONE mutually-exclusive verdict is returned, rather than an axis question and a reversed question
    // answered independently: a container patched straight from flex-row-reverse to flex-col-reverse (no
    // row-family class survives the patch) must forget RowReverse entirely and see ColumnReverse fresh, which
    // a same-family-only check cannot do since it never looks at the other family at all.
    //
    // resolvedStyle is the fallback for the one case no class can cover: flex-direction set some other way (a
    // custom stylesheet rule, an inline style) with NONE of the five classes on the element — a direction
    // class, when present, always outranks a custom stylesheet or inline flexDirection. That fallback needs a
    // live panel, and still cannot self-correct on a same-rect toggle with no intervening reconcile pass.
    //
    // .grid also sets flex-direction: row in _layout.uss, and is deliberately NOT part of the scan: it implies
    // Row, which is this resolver's own fallback default, so recognizing it could never produce a different
    // answer than omitting it does.
    internal static class StyleFlexDirectionResolver
    {
        // widgetOwned marks a child container that is a widget's own inner box rather than the element the
        // class string was written on — see the header. It selects the off-panel default only.
        public static FlexDirection Resolve(VisualElement childContainer, bool widgetOwned)
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
            if (childContainer.panel != null)
            {
                return childContainer.resolvedStyle.flexDirection;
            }
            // No direction/display class and no panel to resolve against. A widget's own inner box takes the
            // ENGINE's default, column: the row default below exists only to mirror what .flex means on an
            // element an author wrote a class string on, and no class string ever reaches here, so there is
            // no such intent to mirror — what will actually lay this box out is the widget's own USS over
            // Yoga's raw column default. Answering row instead would make the off-panel verdict disagree
            // with the on-panel one for the same tree, and each would then pin the other's bug. A widget
            // whose USS overrides that default (a horizontally scrolling ScrollView, a horizontal
            // TwoPaneSplitView) is exactly what the resolvedStyle branch above is for, and needs a panel.
            if (widgetOwned)
            {
                return FlexDirection.Column;
            }
            // Mirror the .flex=row default — the one place this deliberately disagrees with the raw engine,
            // whose own default is column (see Documentation~/styling-flexbox-and-gap.md, "The engine's raw
            // flex default is a column, not a row").
            return FlexDirection.Row;
        }
    }
}
