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
    // The five direction/display classes are consulted FIRST — even on a panel — in the SAME precedence USS
    // itself uses when more than one matches the element (equal specificity, so the LAST declared RULE wins):
    // _layout.uss declares .grid, .flex, .flex-col, .flex-col-reverse, .flex-row, .flex-row-reverse in that
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
        public static FlexDirection Resolve(VisualElement element)
        {
            if (element.ClassListContains("flex-row-reverse"))
            {
                return FlexDirection.RowReverse;
            }
            if (element.ClassListContains("flex-row"))
            {
                return FlexDirection.Row;
            }
            if (element.ClassListContains("flex-col-reverse"))
            {
                return FlexDirection.ColumnReverse;
            }
            if (element.ClassListContains("flex-col"))
            {
                return FlexDirection.Column;
            }
            if (element.ClassListContains("flex"))
            {
                return FlexDirection.Row;
            }
            if (element.panel != null)
            {
                return element.resolvedStyle.flexDirection;
            }
            // No direction/display class and no panel to resolve against: mirror the .flex=row default — the
            // one place this deliberately disagrees with the raw engine, whose own default is column (see
            // Documentation~/styling-flexbox-and-gap.md, "The engine's raw flex default is a column, not a
            // row").
            return FlexDirection.Row;
        }
    }
}
