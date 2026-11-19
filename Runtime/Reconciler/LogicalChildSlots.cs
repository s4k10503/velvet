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
    /// <see cref="Count"/>, <see cref="ToLogical"/>, <see cref="ToPhysical"/> and <see cref="TryGetPhysical"/> read
    /// the container's <c>ContainerRows</c> through <c>ChildListWrites</c>, which builds that model from the child list
    /// once and keeps it beside every write, so a call costs a few block counts and one block rather than the child
    /// list.
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
            => container == null ? 0 : ChildListWrites.RenderedCount(container);

        /// <summary>
        /// Whether a rendered child currently occupies <paramref name="logical"/>, and where.
        /// <paramref name="physical"/> is the append position when the slot is empty, so a caller that
        /// inserts on the false branch needs no second call.
        /// </summary>
        internal static bool TryGetPhysical(VisualElement container, int logical, out int physical)
        {
            if (container == null)
            {
                physical = 0;
                return false;
            }
            return ChildListWrites.TryGetPhysical(container, logical, out physical);
        }

        /// <summary>
        /// The physical index of the <paramref name="logical"/>-th rendered child.
        /// </summary>
        /// <remarks>
        /// For <paramref name="logical"/> equal to (or past) <see cref="Count"/> this returns the position
        /// after the last rendered child and the nodes hosted after it (a ring band, a face border) rather than the
        /// end of the child list, so an append lands before a trailing invisible run instead of after it — the
        /// placement the filter bounds spacer depends on. An element in the back z band is the exception: an append
        /// steps over it, so slot 0 of a parent holding nothing else resolves after it.
        /// Insert semantics follow from the same rule: inserting at a logical slot puts the new child
        /// immediately before the child currently holding that slot, and so after every invisible child between
        /// that child and the rendered one before it. One hosted after its rendered child (a ring band) stays
        /// beside it; one hosted before its rendered child (a backdrop layer) is left before the new child,
        /// which is why BackdropFilterLayer places its layer again at the reconcile boundary.
        /// </remarks>
        internal static int ToPhysical(VisualElement container, int logical)
            => container == null ? 0 : ChildListWrites.ToPhysical(container, logical);

        // Where an append lands is decided by the invisible child's KIND rather than its position. The kinds have
        // opposite contracts: an element in the back z band, a SceneView's camera feed, a Particles element's layers and
        // an inset ring band must stay ahead of every rendered child, while the front band and a filter bounds spacer
        // must stay last. So an append steps over a leading one of the first four — slot 0 of a parent holding nothing
        // else resolves AFTER it — and stops before any other invisible child that is not hosted after its row
        // (IsHostedAfterItsRow). Deciding this positionally instead ("anything invisible seen before the first rendered
        // child is leading") sent an append past a bounds spacer on a parent whose rendered children had all been
        // removed.
        internal static bool StaysFirst(VisualElement child)
            => FiberZLayerCoordinator.IsBackBandMember(child)
                || child.ClassListContains(SceneViewDriver.FeedMarkerClass)
                || child.ClassListContains(ParticlesDriver.LayerMarkerClass)
                || child.ClassListContains(RingOverlay.InsetMarkerClass);

        // The nodes a rendered child hosts after itself: the ring band beside it and the face border over its box. The
        // outline overlay a stacking root keeps after all its rows carries the ring marker class too and is not one.
        internal static bool IsHostedAfterItsRow(VisualElement child)
            => (child.ClassListContains(RingOverlay.MarkerClass) && !child.ClassListContains(RingOverlay.OutlineMarkerClass))
               || child.ClassListContains(FaceBorderOverlay.MarkerClass);

        /// <summary>
        /// The logical slot a physical child index occupies — the number of rendered children before it.
        /// For an invisible child this is the slot it sits in front of.
        /// </summary>
        internal static int ToLogical(VisualElement container, int physical)
            => container == null || physical <= 0 ? 0 : ChildListWrites.ToLogical(container, physical);
    }
}
