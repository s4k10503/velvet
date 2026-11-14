using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Translates between a container's LOGICAL child slots — the positions the reconciler addresses, one
    /// per rendered VNode — and the PHYSICAL child indices those occupy, with reconciler-invisible children
    /// (<see cref="IsInvisible"/>) allowed to sit anywhere among them.
    /// </summary>
    /// <remarks>
    /// The reconciler used to assume the invisible children were only ever a LEADING run (a z-index layer
    /// container) or a TRAILING one (a filter bounds spacer), which let it treat a physical index as a
    /// logical one shifted by a single leading offset across a whole child list: slot arithmetic was done
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
    /// scans the whole child list, so it must not sit inside a per-child loop. <see cref="TryGetPhysical"/>
    /// answers "is the slot occupied" and "where" in one walk. That walk is proportional to the slot index,
    /// and every child it passes is asked <see cref="IsInvisible"/>, so a loop calling these once per child is
    /// quadratic in the child count, and whatever <see cref="IsInvisible"/> costs is paid that many times. A
    /// diff reads its old rows into <see cref="OldSlotOccupants"/> rather than once per child, and removes them
    /// by element, which <c>HostCommitQueue</c> searches for from where that read found it. What it still asks
    /// by slot, a <see cref="LogicalSlotCursor"/> answers, walking on or back from the last slot it resolved, not
    /// a cached count, which every insert and remove would have to invalidate: the general path's leaf commit,
    /// <c>ChildReconciler</c>'s check of each slot the indexed diff patches and of each old row keyed Pass 2 takes
    /// (<c>AssertDomIndexInvariant</c>), and the inserts of its loops (<see cref="LogicalSlotCursor.Insert"/>).
    /// <c>PatchOrReplaceAtSlot</c>'s insert takes the caller's cursor too, but in the indexed diff that cursor stood
    /// on the row the replacement has just removed, so the insert there still walks from the first child.
    /// </para>
    /// </remarks>
    internal static class LogicalChildSlots
    {
        /// <summary>
        /// Whether <paramref name="child"/> holds no logical slot: a Velvet-internal child
        /// (<see cref="SilhouetteBoundsSpacer.IsSpacer"/>) or an element <see cref="FiberZLayerCoordinator"/>
        /// relocated, whose placeholder holds its slot.
        /// </summary>
        internal static bool IsInvisible(VisualElement child)
            => SilhouetteBoundsSpacer.HasMarkerClass(child) || FiberZLayerCoordinator.HoldsNoLogicalSlot(child);

        /// <summary>
        /// The number of rendered children — the exclusive upper bound of the logical slot range. Counts
        /// what the reconciler addresses by slot, not the physical children.
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
                if (!IsInvisible(container[i]))
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
                && !IsInvisible(container[physical]);
        }

        /// <summary>
        /// The physical index of the <paramref name="logical"/>-th rendered child.
        /// </summary>
        /// <remarks>
        /// For <paramref name="logical"/> equal to (or past) <see cref="Count"/> this returns the position
        /// just AFTER the last rendered child rather than the end of the child list, so an append lands
        /// before a trailing invisible run instead of after it — the placement the filter bounds spacer
        /// depends on. An element in the back z band is the exception: an append steps over it, so slot 0 of a
        /// parent holding nothing else resolves after it.
        /// Insert semantics follow from the same rule: inserting at a logical slot puts the new child
        /// immediately before the child currently holding that slot, and so after every invisible child between
        /// that child and the rendered one before it. One hosted after its rendered child (a ring band) stays
        /// beside it; one hosted before its rendered child (a backdrop layer) is left before the new child,
        /// which is why BackdropFilterLayer places its layer again at the reconcile boundary.
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
            // kinds have opposite contracts: an element in the back z band, a SceneView's camera feed, a
            // Particles element's layers and an inset ring band must stay ahead of every rendered child, while
            // the front band and a filter bounds spacer must stay last. So an append steps over a leading one of
            // the first four — slot 0 of a parent holding nothing else resolves AFTER it — and stops before
            // everything else.
            // Deciding this positionally instead ("anything invisible seen before the first rendered child is
            // leading") sent an append past a bounds spacer on a parent whose rendered children had all been
            // removed.
            var appendAt = 0;
            for (var i = 0; i < physicalCount; i++)
            {
                var child = container[i];
                if (IsInvisible(child))
                {
                    if (StaysFirst(child))
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

        internal static bool StaysFirst(VisualElement child)
            => FiberZLayerCoordinator.IsBackBandMember(child)
                || child.ClassListContains(SceneViewDriver.FeedMarkerClass)
                || child.ClassListContains(ParticlesDriver.LayerMarkerClass)
                || child.ClassListContains(RingOverlay.InsetMarkerClass);

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
                if (!IsInvisible(container[i]))
                {
                    rendered++;
                }
            }
            return rendered;
        }

    }

    /// <summary>
    /// <see cref="LogicalChildSlots.TryGetPhysical"/> for one container, resolved from the last slot this
    /// cursor found occupied rather than from the start of the child list, walking on to a slot above it and
    /// back to one below it, so a caller pays the distance between consecutive slots rather than each slot's
    /// index.
    /// </summary>
    /// <remarks>
    /// The remembered slot is trusted only while its element still sits at the remembered physical index;
    /// otherwise the call walks from the start. A single insert or removal before that element moves it,
    /// which the check sees, and one after it is counted by the walk on. The check misses an insert and a
    /// removal before it that cancel out where exactly one of them is a rendered child, so hold a cursor only across
    /// a loop in which the container's rendered children keep their slots.
    /// </remarks>
    internal struct LogicalSlotCursor
    {
        private VisualElement _container;
        private VisualElement _occupant;
        private int _logical;
        private int _physical;

        /// <summary>
        /// Same answer as <see cref="LogicalChildSlots.TryGetPhysical"/>, including its append position on
        /// the false branch.
        /// </summary>
        internal bool TryGetPhysical(VisualElement container, int logical, out int physical)
        {
            if (IsCurrentFor(container))
            {
                // MUTANT_SURVIVES(equivalent, boundary): no caller asks again for the slot the cursor remembers, and for
                // that slot the walk back gives up, leaving the walk from the start to give the answer the walk on gives.
                if (logical >= _logical ? WalkForward(container, logical, out physical) : WalkBack(container, logical, out physical))
                {
                    return true;
                }
            }
            if (!LogicalChildSlots.TryGetPhysical(container, logical, out physical))
            {
                return false;
            }
            Remember(container, logical, physical);
            return true;
        }

        /// <summary>
        /// Inserts <paramref name="element"/>, a rendered child, through <paramref name="host"/> where
        /// <see cref="LogicalChildSlots.ToPhysical"/> puts a child at <paramref name="logical"/>, and remembers it as
        /// the slot's occupant. A caller inserting at ascending slots pays the distance from the slot it last inserted
        /// at rather than the slot's index; an append, past the last rendered child, is the common case.
        /// </summary>
        internal void Insert(HostCommitQueue host, VisualElement container, int logical, VisualElement element)
        {
            var physical = IsCurrentFor(container) && logical > _logical
                ? InsertIndexFrom(container, logical)
                : LogicalChildSlots.ToPhysical(container, logical);
            host.Insert(container, physical, element);
            // An insert into a container on a panel can put a sibling ahead of the element before this returns (a
            // layer hosted before its element), which moves it past where it was put; the slot is remembered only
            // while the element still stands there, and the next call walks from the start otherwise.
            if (physical < container.childCount && ReferenceEquals(container[physical], element))
            {
                Remember(container, logical, physical);
            }
            else
            {
                _container = null;
            }
        }

        // ToPhysical's answer for a slot above the remembered one, which holds its own rendered occupant: the walk
        // starts after it, with the append position ToPhysical would have reached at it.
        private int InsertIndexFrom(VisualElement container, int logical)
        {
            var rendered = _logical;
            var appendAt = _physical + 1;
            var physicalCount = container.childCount;
            for (var i = _physical + 1; i < physicalCount; i++)
            {
                var child = container[i];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    if (LogicalChildSlots.StaysFirst(child))
                    {
                        appendAt = i + 1;
                    }
                    continue;
                }
                rendered++;
                if (rendered == logical)
                {
                    return i;
                }
                appendAt = i + 1;
            }
            return appendAt;
        }

        private bool IsCurrentFor(VisualElement container)
            => ReferenceEquals(_container, container)
                && _physical < container.childCount
                && ReferenceEquals(container[_physical], _occupant);

        private bool WalkForward(VisualElement container, int logical, out int physical)
        {
            var rendered = _logical;
            var physicalCount = container.childCount;
            for (physical = _physical; physical < physicalCount; physical++)
            {
                if (LogicalChildSlots.IsInvisible(container[physical]))
                {
                    continue;
                }
                if (rendered == logical)
                {
                    Remember(container, logical, physical);
                    return true;
                }
                rendered++;
            }
            return false;
        }

        private bool WalkBack(VisualElement container, int logical, out int physical)
        {
            var remaining = _logical - logical;
            for (physical = _physical - 1; physical != -1; physical--)
            {
                if (LogicalChildSlots.IsInvisible(container[physical]))
                {
                    continue;
                }
                if (--remaining == 0)
                {
                    Remember(container, logical, physical);
                    return true;
                }
            }
            return false;
        }

        private void Remember(VisualElement container, int logical, int physical)
        {
            _container = container;
            _occupant = container[physical];
            _logical = logical;
            _physical = physical;
        }
    }
}
