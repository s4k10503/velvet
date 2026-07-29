using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Translates between a container's LOGICAL child slots — the positions the reconciler addresses, one
    /// per rendered VNode — and the PHYSICAL child indices those occupy, with reconciler-invisible children
    /// (<see cref="SilhouetteBoundsSpacer.IsSpacer"/>) allowed to sit anywhere among them.
    /// </summary>
    /// <remarks>
    /// The reconciler used to assume the invisible children were only ever a LEADING run (a z-index layer
    /// container) or a TRAILING one (a filter bounds spacer), which let it treat
    /// <c>physical = logical + LeadingOffset</c> as true across a whole child list: slot arithmetic was done
    /// on physical indices and clamped against a count that trimmed only those two runs. That assumption is
    /// what confined every invisible child to an end of the list, and a paint that must sit BESIDE the
    /// element it decorates — a ring band, which has to occlude its own element but not its later siblings —
    /// cannot honour it.
    /// <para>
    /// The replacement is a single rule: every slot index the reconciler computes or stores is LOGICAL, and
    /// it is converted exactly once, at the point the DOM is touched. <see cref="ToPhysical"/> is that
    /// conversion for a read, a removal or an insert; <see cref="Count"/> is the logical bound that replaces
    /// the old physical one; <see cref="ToLogical"/> is the inverse, for the few places that start from a
    /// physical index.
    /// </para>
    /// <para>
    /// Cost, and where it is NOT paid. <see cref="ToPhysical"/> stops at the requested slot; <c>Count</c>
    /// scans the whole child list, so it must not sit inside a per-child loop — the per-child sites ask
    /// <see cref="TryGetPhysical"/> instead, which answers "is the slot occupied" and "where" in one walk,
    /// and <c>AssertDomIndexInvariant</c> computes its count inside the <c>Debug.Assert</c> arguments so a
    /// player build compiles it out with the call. What remains is one bounded walk per DOM touch on paths
    /// that are already mutating the DOM. A container with no invisible child still pays it: the walk is
    /// proportional to the slot index, not to the whole list, so a full pass stays quadratic in the child
    /// count. If that shows up in a profile, the fix is a cursor advancing alongside the caller's own loop,
    /// not a cached count (which every insert and remove would have to invalidate).
    /// </para>
    /// </remarks>
    internal static class LogicalChildSlots
    {
        /// <summary>
        /// The number of rendered children — the exclusive upper bound of the logical slot range. Replaces
        /// the physical <c>NonSpacerChildCount</c> wherever that was being read as "how many slots are there".
        /// </summary>
        internal static int Count(VisualElement container)
        {
            if (container == null)
            {
                return 0;
            }
            var rendered = 0;
            var physicalCount = container.childCount;
            for (var i = 0; i < physicalCount; i++)
            {
                if (!SilhouetteBoundsSpacer.IsSpacer(container[i]))
                {
                    rendered++;
                }
            }
            return rendered;
        }

        /// <summary>
        /// Whether a rendered child currently occupies <paramref name="logical"/>, and where. One walk
        /// instead of a <see cref="Count"/> bound check followed by a <see cref="ToPhysical"/> — the pair
        /// the hot per-child loops used to do, where the bound check alone scanned the whole child list.
        /// <paramref name="physical"/> is the append position when the slot is empty, so a caller that
        /// inserts on the false branch needs no second call.
        /// </summary>
        internal static bool TryGetPhysical(VisualElement container, int logical, out int physical)
        {
            physical = ToPhysical(container, logical);
            return logical >= 0 && physical < container.childCount
                && !SilhouetteBoundsSpacer.IsSpacer(container[physical]);
        }

        /// <summary>
        /// The physical index of the <paramref name="logical"/>-th rendered child.
        /// </summary>
        /// <remarks>
        /// For <paramref name="logical"/> equal to (or past) <see cref="Count"/> this returns the position
        /// just AFTER the last rendered child rather than the end of the child list, so an append lands
        /// before a trailing invisible run instead of after it — the placement the filter bounds spacer
        /// depends on. This is NOT always what the old <c>Insert(NonSpacerChildCount(parent), …)</c> idiom
        /// answered: on a parent holding a back z-layer container and no rendered child that idiom said 0,
        /// which would place the append ahead of a container that has to stay first.
        /// Insert semantics follow from the same rule: inserting at a logical slot puts the new child
        /// immediately before the child currently holding that slot, so an invisible child sitting between
        /// two rendered ones stays attached to the rendered child it precedes.
        /// </remarks>
        internal static int ToPhysical(VisualElement container, int logical)
        {
            if (container == null)
            {
                return 0;
            }
            var physicalCount = container.childCount;
            if (logical < 0)
            {
                logical = 0;
            }
            var rendered = 0;
            // Where an append lands, decided by the invisible child's KIND rather than its position. The
            // three kinds have opposite contracts: a z-index BACK container must stay the parent's first
            // physical child, while a front container and a filter bounds spacer must stay last. So an
            // append steps over a leading back container — slot 0 of a parent holding nothing else resolves
            // AFTER it — and stops before everything else. Deciding this positionally instead ("anything
            // invisible seen before the first rendered child is leading") sent an append past a bounds
            // spacer on a parent whose rendered children had all been removed.
            var appendAt = 0;
            for (var i = 0; i < physicalCount; i++)
            {
                var child = container[i];
                if (SilhouetteBoundsSpacer.IsSpacer(child))
                {
                    if (FiberZLayerCoordinator.IsBackLayerContainer(child))
                    {
                        appendAt = i + 1;
                    }
                    continue;
                }
                if (rendered == logical)
                {
                    return i;
                }
                rendered++;
                appendAt = i + 1;
            }
            return appendAt;
        }

        /// <summary>
        /// The logical slot a physical child index occupies — the number of rendered children before it.
        /// For an invisible child this is the slot it sits in front of.
        /// </summary>
        internal static int ToLogical(VisualElement container, int physical)
        {
            if (container == null || physical <= 0)
            {
                return 0;
            }
            var limit = physical < container.childCount ? physical : container.childCount;
            var rendered = 0;
            for (var i = 0; i < limit; i++)
            {
                if (!SilhouetteBoundsSpacer.IsSpacer(container[i]))
                {
                    rendered++;
                }
            }
            return rendered;
        }

    }
}
