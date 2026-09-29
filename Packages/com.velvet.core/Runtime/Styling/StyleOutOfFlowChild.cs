using UnityEngine.UIElements;

namespace Velvet
{
    // Shared out-of-flow test for the index-driven child manipulators (StyleGapManipulator,
    // StyleGridManipulator, StyleDivideManipulator, StyleChildVariantManipulator). Each of them walks a
    // container's children by raw DOM index to decide "is this the first child" / "which column or row does
    // this child fall into", and writes margins / borders / widths on that basis. A child pulled out of layout
    // flow via position: absolute — a PopLayout ghost pinned by GeneralPathReconciler.PinExitingChildOutOfFlow, or an
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
        // StyleFlexDirectionResolver / StyleGapManipulator.IsWrap already use for flex-direction / flex-wrap.
        internal static bool IsOutOfFlow(VisualElement child)
        {
            // The filter bounds-spacer and the ring overlay are always out of flow (position:absolute) and must
            // never occupy a gap / grid / divide slot; recognize them by their markers so they do not need the
            // "absolute" utility class (which would leak into a user's has-[.absolute]: selector).
            if (IsInserted(child))
            {
                return true;
            }
            if (child.panel != null)
            {
                return child.resolvedStyle.position == Position.Absolute;
            }
            return child.ClassListContains("absolute");
        }

        // The off-panel (class-only) half of IsOutOfFlow's test, exposed standalone for callers that only have
        // a VNode's declared classNames — not a live element — at reconcile time (the z-* scope gate: z-* takes
        // effect only on an element that is ALSO position:absolute, decided before the element's class list has
        // ever been attached to anything). Recognizes only the "absolute" utility class, the same limitation
        // IsOutOfFlow's own off-panel fallback already has (an inline Styles.Position override is invisible
        // here either way).
        internal static bool IsOutOfFlowClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (cls == "absolute")
                {
                    return true;
                }
            }
            return false;
        }

        // Folds container's child sequence into the given running hash: each child's identity plus its
        // in-flow / out-of-flow state, in child order. Shared by the index-driven child manipulators'
        // signature hashes (gap, grid, child-variant) so a child's out-of-flow transition (a PopLayout pin
        // or its cancel) flips the signature even when neither its identity nor the container's total
        // child count changed.
        internal static int HashChildSequence(int hash, VisualElement container)
        {
            unchecked
            {
                var count = container.childCount;
                for (var i = 0; i < count; i++)
                {
                    var child = container[i];
                    hash = hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(child);
                    hash = hash * 31 + (IsOutOfFlow(child) ? 1 : 0);
                }
                return hash;
            }
        }

        // The index of container's last child that is the author's, -1 when there is none — the child the
        // manipulators following Tailwind's `> :not(:last-child)` exempt. `:last-child` counts an absolutely
        // positioned child, so only the children Velvet inserts itself (the filter bounds-spacer and the ring
        // overlay) are passed over.
        internal static int LastSpacedIndex(VisualElement container)
        {
            var last = -1;
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                if (!IsInserted(container[i]))
                {
                    last = i;
                }
            }
            return last;
        }

        // A child Velvet inserts itself rather than one the author wrote: the filter bounds-spacer or a ring
        // overlay. Both are absolutely positioned, which IsOutOfFlow's off-panel class check cannot see.
        private static bool IsInserted(VisualElement child)
            => SilhouetteBoundsSpacer.IsSpacer(child) || child.ClassListContains(RingOverlay.MarkerClass);

        // Whether child is display:none — through the hidden utility or an inline display — and so has no box
        // for CSS gap to space or to count as the first.
        internal static bool HasNoBox(VisualElement child)
            => child.ClassListContains("hidden") || child.style.display == DisplayStyle.None;
    }
}
