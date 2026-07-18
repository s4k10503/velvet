using UnityEngine.UIElements;

namespace Velvet
{
    // Shared out-of-flow test for the index-driven child manipulators (StyleGapManipulator,
    // StyleGridManipulator, StyleDivideManipulator). Each of them walks a container's children by raw
    // DOM index to decide "is this the first child" / "which column or row does this child fall into", and
    // writes margins / borders / widths on that basis. A child pulled out of layout flow via
    // position: absolute — a PopLayout ghost pinned by GeneralPathReconciler.PinExitingChildOutOfFlow, or an
    // app-authored .absolute utility child — holds no slot in the flex line, so counting it would both waste
    // spacing on a sibling that no longer occupies one and, for a pinned ghost, disturb the compensated
    // inline position PinExitingChildOutOfFlow computed for it. CSS itself excludes out-of-flow children from
    // gap / grid placement the same way, so this is a correctness fix for the ordinary (non-PopLayout) case
    // too, not a PopLayout-only carve-out.
    internal static class StyleOutOfFlowChild
    {
        // On a panel this reads the resolved position (reflects both an inline override — the way a PopLayout
        // pin sets it — and a class-driven one). Off-panel (EditMode, pre-attach) resolvedStyle is not yet
        // meaningful, so this falls back to the .absolute utility class marker, mirroring the off-panel idiom
        // StyleGapManipulator.IsRow/IsWrap already use for flex-direction / flex-wrap.
        internal static bool IsOutOfFlow(VisualElement child)
        {
            // The filter bounds-spacer is always out of flow (position:absolute) and must never occupy a
            // gap / grid / divide slot; recognize it by its marker so it does not need the "absolute" utility
            // class (which would leak into a user's has-[.absolute]: selector).
            if (SilhouetteBoundsSpacer.IsSpacer(child))
            {
                return true;
            }
            if (child.panel != null)
            {
                return child.resolvedStyle.position == Position.Absolute;
            }
            return child.ClassListContains("absolute");
        }
    }
}
