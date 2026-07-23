#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // Internal-only carrier that shuttles a fully-built z-managed element through the EXISTING
    // ReconcilerContext.PendingPortalMounts queue so its container placement resolves from the same
    // post-pass safe context Portal mounts already use (ChildReconciler.DrainPendingPortalMounts). Never
    // appears in an actual VNode tree — synthesized in FiberNodeFactory.CreateElement / FiberNodePatcher and
    // immediately enqueued, so RequiresInlineExpansion / CanPatch / ExpandInlineRecursive never see it: the
    // element it carries is already an ordinary ElementNode as far as the rest of the reconciler is
    // concerned, and this node's only job is to reach ResolveQueuedMount with the placeholder it was queued
    // against.
    internal sealed class ZLayerMountNode : VNode
    {
        public VisualElement Real = null!;
        public int ResolvedZ;

        // Carries an already-assigned mount-order tiebreak forward across a deferred reposition (the target
        // container did not exist yet for a sign flip on an already-z-managed element); null for a genuinely
        // new entry (a fresh mount, or a none-to-z transition), which is assigned one on resolution.
        public ulong? Order;
    }

    // Owns the reconciler-side mechanics of the z-* stacking feature: classifying which absolute elements
    // are z-managed, the lazily-created per-stacking-parent front/back layer containers, sorted insertion
    // within a layer, and the four patch-time transitions (z stays z / z->none / none->z / z changes within
    // z) that must preserve the real element's identity across a physical relocation. Called from
    // FiberNodeFactory (mount), FiberNodePatcher (patch), FiberElementCleaner (unmount), and ChildReconciler
    // (the leading-offset gate + the deferred-mount drain arm). No state of its own — everything lives in
    // ReconcilerContext, mirroring PanelHostFactory/AnchoredDriver's own ctx-parameter style.
    //
    // Safety invariant threaded through every method below: mutating a LAYER CONTAINER's own children
    // (insert / remove / reorder a member) never touches the stacking parent's child list and is always
    // safe, from any call site, at any time. Mutating the stacking parent's OWN children — creating a
    // container for the first time, or removing one once empty — changes physical indices a live
    // Indexed/Keyed diff over that same parent may still be iterating by absolute position (worse for a
    // LEADING back container, which shifts every ordinary sibling after it), so that half is always
    // deferred to the same post-pass point Portal's own deferred mount already resolves from
    // (EnqueueMount/ResolveQueuedMount for creation, PendingZLayerTeardownChecks/DrainTeardowns for removal).
    internal static class FiberZLayerCoordinator
    {
        internal const string FrontMarkerClass = "velvet-z-layer-front";
        internal const string BackMarkerClass = "velvet-z-layer-back";

        // True when child is one of the two layer containers this stacking parent may carry. Consulted by
        // SilhouetteBoundsSpacer.IsSpacer, the single centralized predicate every "real child" count/index
        // site in the reconciler already goes through.
        internal static bool IsLayerContainer(VisualElement child)
            => child.ClassListContains(FrontMarkerClass) || child.ClassListContains(BackMarkerClass);

        // The z-* scope gate: an element is z-managed only when it carries an explicit z-* utility class AND
        // is ALSO out-of-flow — either the "absolute" utility class (StyleOutOfFlowChild's off-panel/class-
        // based half: z-* on an in-flow element is a documented no-op, decided here before the element has
        // any live position to read) or V.Anchored's Props.Anchored (which forces position:absolute via
        // AnchoredDriver.Attach as a plain inline style, never through the utility class, so the classNames
        // array alone would otherwise never see it). Cheap prefix-gated (HasZIndexClass short-circuits the
        // common case with no z class at all).
        internal static bool TryClassify(string[] classNames, FiberElementProps? props, out int resolvedZ)
        {
            resolvedZ = 0;
            if (!StyleZIndexClass.HasZIndexClass(classNames))
            {
                return false;
            }
            if (!StyleOutOfFlowChild.IsOutOfFlowClass(classNames) && props?.Anchored == null)
            {
                return false;
            }
            return StyleZIndexClass.TryExtract(classNames, out resolvedZ);
        }

        // How many of `parent`'s LEADING children are its own back-layer container (0 or 1). Folded into
        // wherever a fresh (non-resumed) slotStart originates — ChildReconciler.Reconcile's single entry
        // point — so every downstream ordinary-child index computation is already correct without touching
        // any of them individually. A resumed time-sliced pass never re-enters through that same point (its
        // saved slotStart already has this baked in from when it first ran), so this must never be applied
        // twice for the same logical pass.
        internal static int LeadingOffset(VisualElement parent)
            => parent.childCount > 0 && parent[0].ClassListContains(BackMarkerClass) ? 1 : 0;

        // A hidden, zero-footprint stand-in left at a z-managed element's logical slot while its real
        // element lives in a layer container. Carries focusable/tabIndex so it can act as a same-panel Tab
        // proxy (FiberFocusNavigator) — set unconditionally (not opt-in): keeping Tab order following the
        // logical position has no downside, unlike PanelFocusOrder's genuine cross-panel behavior change.
        internal static VisualElement CreatePlaceholder()
        {
            var placeholder = new VisualElement
            {
                style = { display = DisplayStyle.None },
                focusable = true,
                tabIndex = 0,
            };
            return placeholder;
        }

        // Mount-time entry (FiberNodeFactory.CreateElement): `real` is fully built but not yet attached
        // anywhere (CreateElement never knows its own parent), so placement must wait for the drain, where
        // placeholder.parent is finally resolvable. Returns the placeholder to install at the ordinary slot.
        internal static VisualElement EnqueueMount(ReconcilerContext ctx, VisualElement real, int resolvedZ)
        {
            var placeholder = CreatePlaceholder();
            ctx.PendingPortalMounts.Enqueue(
                (placeholder, new ZLayerMountNode { Real = real, ResolvedZ = resolvedZ }, null, null, null));
            return placeholder;
        }

        // Drain-time resolution of a queued mount (ChildReconciler.DrainPendingPortalMounts's new case arm).
        // By now placeholder.parent is set (the caller already inserted it at the ordinary slot like any
        // other created element), so the stacking parent — and therefore its container — is finally known.
        internal static void ResolveQueuedMount(ReconcilerContext ctx, VisualElement placeholder, ZLayerMountNode node)
        {
            var stackingParent = placeholder.parent;
            if (stackingParent == null)
            {
                // The placeholder's own subtree was rolled back (a Suspense/error-boundary abort) before
                // this drained — mirrors DrainPendingPortalMounts' own placeholder.parent == null skip.
                return;
            }
            var container = GetOrCreateContainer(ctx, stackingParent, node.ResolvedZ);
            Place(ctx, placeholder, container, stackingParent, node.Real, node.ResolvedZ, node.Order);
        }

        // Patch-time: an already z-managed element's resolved z (or its front/back sign) changed. A no-op
        // when the resolved value is unchanged — ordering is a mount/patch-time invariant maintained
        // incrementally, never a periodic resort. Safe to call synchronously when the destination layer
        // already exists (a pure container-membership move); defers to the drain only for the rarer case
        // where this parent's FIRST member of the opposite sign has just appeared.
        internal static void Reposition(ReconcilerContext ctx, VisualElement placeholder, VisualElement real, int newResolvedZ)
        {
            if (!ctx.ZLayerMembers.TryGetValue(real, out var member) || member.ResolvedZ == newResolvedZ)
            {
                return;
            }
            var stackingParent = member.StackingParent;
            var existing = TryGetExistingContainer(ctx, stackingParent, newResolvedZ);
            if (existing != null)
            {
                if (!ReferenceEquals(existing, member.Container))
                {
                    DetachFromContainer(ctx, member.Container, real);
                }
                Place(ctx, placeholder, existing, stackingParent, real, newResolvedZ, member.Order);
                return;
            }
            DetachFromContainer(ctx, member.Container, real);
            ctx.ZLayerMembers.Remove(real);
            ctx.PendingPortalMounts.Enqueue(
                (placeholder, new ZLayerMountNode { Real = real, ResolvedZ = newResolvedZ, Order = member.Order },
                    null, null, null));
        }

        // Patch-time none-to-z: `real` is still ordinary (physically at its declared slot under
        // `stackingParent`); relocates it out, leaving `placeholder` in its place at the SAME physical
        // index (an in-place swap, like a WrapElement type-flip — parent childCount/order is unchanged, so
        // the caller's post-patch re-fetch-at-the-same-index picks up the new occupant). Synchronous when
        // the destination layer already exists; defers only when it must be created for the first time.
        internal static VisualElement RelocateFromOrdinarySlot(ReconcilerContext ctx, VisualElement real, int resolvedZ)
        {
            var stackingParent = real.parent!;
            var idx = stackingParent.IndexOf(real);
            var placeholder = CreatePlaceholder();
            stackingParent.Insert(idx, placeholder);
            real.RemoveFromHierarchy();

            var existing = TryGetExistingContainer(ctx, stackingParent, resolvedZ);
            if (existing != null)
            {
                Place(ctx, placeholder, existing, stackingParent, real, resolvedZ);
            }
            else
            {
                ctx.PendingPortalMounts.Enqueue(
                    (placeholder, new ZLayerMountNode { Real = real, ResolvedZ = resolvedZ }, null, null, null));
            }
            return placeholder;
        }

        // Patch-time z-to-none: `real` (found via `placeholder`) leaves its layer and returns to the
        // ordinary slot `placeholder` occupies, replacing it (same in-place-swap contract as above).
        internal static void RelocateToOrdinarySlot(ReconcilerContext ctx, VisualElement placeholder, VisualElement real)
        {
            if (ctx.ZLayerMembers.TryGetValue(real, out var member))
            {
                DetachFromContainer(ctx, member.Container, real);
                ctx.ZLayerMembers.Remove(real);
            }
            ctx.ZLayerPlaceholders.Remove(placeholder);

            var stackingParent = placeholder.parent!;
            var idx = stackingParent.IndexOf(placeholder);
            stackingParent.Insert(idx, real);
            stackingParent.Remove(placeholder);
        }

        // Cleanup entry (FiberElementCleaner.CleanupZLayerPlaceholder, mirroring CleanupPortal): detaches and
        // forgets the real element owned by a departing placeholder, returning it so the caller performs the
        // ordinary resource-cleanup + pool-return sequence it already owns for any removed element. Returns
        // null when `placeholder` is not (or is no longer) a z-layer placeholder.
        internal static VisualElement? TakeReal(ReconcilerContext ctx, VisualElement placeholder)
        {
            if (!ctx.ZLayerPlaceholders.Remove(placeholder, out var real))
            {
                return null;
            }
            if (ctx.ZLayerMembers.TryGetValue(real, out var member))
            {
                DetachFromContainer(ctx, member.Container, real);
                ctx.ZLayerMembers.Remove(real);
            }
            return real;
        }

        // The (logical parent, logical index) a peer-/group- source search must walk from: a z-managed
        // element's PLACEHOLDER position (its declared position among its true siblings), not its current
        // physical parent (the layer container, whose "preceding siblings" are unrelated same-layer
        // members). Ordinary elements resolve to their own physical parent/index unchanged.
        internal static bool TryGetLogicalPosition(ReconcilerContext ctx, VisualElement element, out VisualElement? parent, out int index)
        {
            if (ctx.ZLayerMembers.TryGetValue(element, out var member))
            {
                parent = member.StackingParent;
                index = parent.IndexOf(member.Placeholder);
                return index >= 0;
            }
            parent = element.parent;
            index = parent?.IndexOf(element) ?? -1;
            return parent != null && index >= 0;
        }

        // Runs at the same post-pass safe point as DrainPendingPortalMounts: any container whose membership
        // changed this pass and is now empty is removed from its stacking parent and forgotten.
        internal static void DrainTeardowns(ReconcilerContext ctx)
        {
            if (ctx.PendingZLayerTeardownChecks.Count == 0)
            {
                return;
            }
            foreach (var container in ctx.PendingZLayerTeardownChecks)
            {
                if (container.childCount > 0 || container.parent == null)
                {
                    continue;
                }
                var stackingParent = container.parent;
                stackingParent.Remove(container);
                if (!ctx.ZLayerHosts.TryGetValue(stackingParent, out var record))
                {
                    continue;
                }
                if (ReferenceEquals(record.Front, container)) record.Front = null;
                if (ReferenceEquals(record.Back, container)) record.Back = null;
                if (record.Front == null && record.Back == null)
                {
                    ctx.ZLayerHosts.Remove(stackingParent);
                }
            }
            ctx.PendingZLayerTeardownChecks.Clear();
        }

        // Places (inserts sorted, records bookkeeping) `real` into `container`, assigning a fresh mount-order
        // tiebreak unless one is supplied (a reposition/relocation carries its element's existing order
        // forward — see ZLayerMember.Order's own "assigned once" contract).
        private static void Place(
            ReconcilerContext ctx, VisualElement placeholder, VisualElement container, VisualElement stackingParent,
            VisualElement real, int resolvedZ, ulong? existingOrder = null)
        {
            var order = existingOrder ?? ctx.NextZOrder++;
            InsertSorted(ctx, container, real, resolvedZ, order);
            ctx.ZLayerPlaceholders[placeholder] = real;
            ctx.ZLayerMembers[real] = new ZLayerMember(placeholder, container, stackingParent, resolvedZ, order);
        }

        // The already-existing container for `resolvedZ`'s sign under `stackingParent`, or null. A pure
        // dictionary read — never mutates `stackingParent`'s children, so it is always safe to call, from
        // anywhere, including synchronously mid-diff.
        private static VisualElement? TryGetExistingContainer(ReconcilerContext ctx, VisualElement stackingParent, int resolvedZ)
        {
            if (!ctx.ZLayerHosts.TryGetValue(stackingParent, out var record))
            {
                return null;
            }
            return resolvedZ < 0 ? record.Back : record.Front;
        }

        // The container for `resolvedZ`'s sign under `stackingParent`, creating (and physically attaching)
        // it on first use. Only ever called from a safe (post-pass) context — creating one changes
        // `stackingParent`'s own child list, which a live diff over that same parent may still be indexing
        // by absolute position.
        private static VisualElement GetOrCreateContainer(ReconcilerContext ctx, VisualElement stackingParent, int resolvedZ)
        {
            if (!ctx.ZLayerHosts.TryGetValue(stackingParent, out var record))
            {
                record = new ZLayerHostRecord();
                ctx.ZLayerHosts[stackingParent] = record;
            }
            if (resolvedZ < 0)
            {
                if (record.Back == null)
                {
                    record.Back = CreateContainer(BackMarkerClass);
                    // The back layer must physically PRECEDE the parent's ordinary children to paint behind
                    // them — the one structural asymmetry against the front layer (SilhouetteBoundsSpacer's
                    // own trailing-only spacer convention), which is what LeadingOffset exists to reconcile
                    // against every ordinary-child index computation.
                    stackingParent.Insert(0, record.Back);
                }
                return record.Back;
            }
            if (record.Front == null)
            {
                record.Front = CreateContainer(FrontMarkerClass);
                // Trailing, like SilhouetteBoundsSpacer's own spacer — Add (not Insert at NonSpacerChildCount)
                // is correct here specifically because IsLayerContainer now makes IsSpacer recognize this
                // container too, so a co-existing bounds-spacer's own "after all rendered children" invariant
                // tolerates either relative order between the two trailing spacers.
                stackingParent.Add(record.Front);
            }
            return record.Front;
        }

        private static VisualElement CreateContainer(string markerClass)
        {
            var container = new VisualElement { pickingMode = PickingMode.Ignore };
            container.AddToClassList(markerClass);
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            return container;
        }

        // Inserts (or repositions) `real` at its sorted position within `container`: resolved z ascending,
        // ties broken by mount order. Only ever mutates `container`'s own children — never `container`'s
        // parent — so this is safe from any call site, synchronous or deferred.
        private static void InsertSorted(ReconcilerContext ctx, VisualElement container, VisualElement real, int resolvedZ, ulong order)
        {
            if (real.parent != null)
            {
                real.RemoveFromHierarchy();
            }
            var insertAt = container.childCount;
            for (var i = 0; i < container.childCount; i++)
            {
                var sibling = container.ElementAt(i);
                if (!ctx.ZLayerMembers.TryGetValue(sibling, out var siblingInfo))
                {
                    continue;
                }
                if (siblingInfo.ResolvedZ > resolvedZ
                    || (siblingInfo.ResolvedZ == resolvedZ && siblingInfo.Order > order))
                {
                    insertAt = i;
                    break;
                }
            }
            container.Insert(insertAt, real);
        }

        // Detaches `real` from `container` (a no-op if it has already moved) and marks the container for an
        // emptiness re-check at the next safe drain point — never removed here: that would mutate the
        // STACKING PARENT's own children, unsafe mid-pass (see the type's own safety-invariant note).
        private static void DetachFromContainer(ReconcilerContext ctx, VisualElement container, VisualElement real)
        {
            if (ReferenceEquals(real.parent, container))
            {
                real.RemoveFromHierarchy();
            }
            ctx.PendingZLayerTeardownChecks.Add(container);
        }
    }
}
