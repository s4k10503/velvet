#nullable enable
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Internal-only carrier that shuttles a fully-built relocated element through the EXISTING
    // ReconcilerContext.PendingPortalMounts queue, so it joins its parent from the same post-pass point a Portal
    // mount resolves from (ChildReconciler.DrainPendingPortalMounts). Never appears in an actual VNode tree:
    // FiberNodeFactory synthesizes and enqueues it, so RequiresInlineExpansion / CanPatch / ExpandInlineRecursive
    // never see it.
    internal sealed class ZLayerMountNode : VNode
    {
        public VisualElement Real = null!;
    }

    // Brings CSS's paint order among siblings — z-index, positioned before unpositioned, flex order — to UI Toolkit,
    // which paints, picks and lays out one child list in one order. A child that has to paint somewhere other than
    // its declared slot is RELOCATED within its own parent: a zero-size placeholder keeps the declared slot for the
    // reconciler, structural variants, Tab order and peer searches, and the real element is sorted to where it
    // paints (Hierarchy.Sort, which reorders without detaching, so nothing it runs is cancelled). Where the element
    // takes part in flex layout, its layout node is put back at the slot it lays out at (ChildLayoutOrder), so only
    // its paint moves.
    //
    // A parent's children are arranged as [back band] [declared slots] [front band]: the back band holds negative z,
    // sorted by z; the slots hold the unpositioned children in flex order; the front band holds positioned children
    // with z auto or 0 in flex order (when an unpositioned sibling would otherwise paint over one of them) and then
    // positive z, sorted by z. Equal keys keep tree order.
    //
    // Safety invariant: a slot occupant (an ordinary child or a placeholder) changes only where the reconciler expects
    // it to — when an element is created or patched — or at the end of a pass for a parent no parked diff holds.
    // Everything else this moves is invisible to the reconciler's slot arithmetic (LogicalChildSlots.IsInvisible).
    internal static class FiberZLayerCoordinator
    {
        // The z a pinned PopLayout ghost stacks at: in front of every ordinary sibling, as a positioned element paints
        // above the in-flow ones, and beneath any element given a positive z of its own.
        internal const int PinnedGhostZ = 0;

        private enum Role
        {
            Slot,
            BackReal,
            MiddleReal,
            FrontReal,
            Anchor,
            Dead,
        }

        // Partner: for a Slot, the real element it stands for; for a real element, the element its layout node is
        // exchanged with (its placeholder or its anchor), null when none is; for an Anchor, its real element.
        // Slot: for a real element, its placeholder.
        private sealed class Held
        {
            public Role Role;
            public VisualElement? Partner;
            public VisualElement? Slot;
        }

        private static readonly ConditionalWeakTable<VisualElement, Held> s_held = new();

        // The elements whose resolved z is not auto: each makes a stacking context of what it holds, as a z-index does,
        // whether or not it has to be relocated to paint at that z.
        private static readonly ConditionalWeakTable<VisualElement, object> s_zStacked = new();

        internal static bool CreatesStackingContext(VisualElement element) => s_zStacked.TryGetValue(element, out _);

        private static void NoteZStacking(VisualElement real, bool stacks)
        {
            if (stacks)
            {
                s_zStacked.AddOrUpdate(real, s_candidate);
            }
            else
            {
                s_zStacked.Remove(real);
            }
        }

        // A relocated real element: painted outside its declared slot, which its placeholder holds.
        internal static bool IsRelocatedReal(VisualElement child)
            => child != null && s_held.TryGetValue(child, out var held)
                && held.Role is Role.BackReal or Role.MiddleReal or Role.FrontReal;

        // IsRelocatedReal or IsInternal in one lookup: every role but Slot, whose placeholder holds the slot.
        internal static bool HoldsNoLogicalSlot(VisualElement child)
        {
            if (child == null) return false;
            if (!s_held.TryGetValue(child, out var held)) return false;
            return held.Role != Role.Slot;
        }

        // An anchor (the box a relocated element lays out at, when that is not its placeholder's) or a retired
        // placeholder or anchor waiting for removal: internal, and invisible to everything that walks children.
        internal static bool IsInternal(VisualElement child)
            => child != null && s_held.TryGetValue(child, out var held) && held.Role is Role.Anchor or Role.Dead;

        // A relocated element sitting in the back band. LogicalChildSlots needs it apart from the other invisible
        // children, which an append must stop before rather than step over.
        internal static bool IsBackBandMember(VisualElement child)
            => child != null && s_held.TryGetValue(child, out var held) && held.Role == Role.BackReal;

        // The children of container in the order flex lays them out: a relocated element stands where its layout
        // node is, and the element whose node it took (its placeholder or anchor) is left out.
        internal static void FlowChildren(VisualElement container, List<VisualElement> into)
        {
            into.Clear();
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                if (s_held.TryGetValue(child, out var held))
                {
                    switch (held.Role)
                    {
                        case Role.Dead:
                            continue;
                        case Role.BackReal or Role.FrontReal or Role.MiddleReal when held.Partner != null:
                            continue;
                        case Role.Anchor:
                            into.Add(held.Partner!);
                            continue;
                        case Role.Slot when held.Partner != null && s_held.TryGetValue(held.Partner, out var real)
                            && ReferenceEquals(real.Partner, child):
                            into.Add(held.Partner);
                            continue;
                    }
                }
                into.Add(child);
            }
        }

        // The element in container's last declared slot, read through a placeholder to its real element — the child
        // a `:last-child` selector matches. Null when container holds no declared child.
        internal static VisualElement? LastDeclaredChild(VisualElement container)
        {
            for (var i = container.childCount - 1; i >= 0; i--)
            {
                var child = container[i];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    continue;
                }
                return s_held.TryGetValue(child, out var held) && held.Role == Role.Slot && held.Partner != null
                    ? held.Partner
                    : child;
            }
            return null;
        }

        // Whether the classes give a z other than auto, and which. Kept for the callers that only ask whether an
        // element stacks at all (FiberNodeFactory.FindFirstMotionDescendant).
        internal static bool TryClassify(string[] classNames, out int resolvedZ)
        {
            resolvedZ = 0;
            return StyleZIndexClass.HasZIndexClass(classNames) && StyleZIndexClass.TryExtract(classNames, out resolvedZ);
        }

        private static readonly object s_candidate = new();

        // Whether an element whose class list classified it (TryClassify, passed as classified) is stacked, and at
        // which z. A winning z-* class reading a custom property stacks at the integer that resolves to, and
        // leaves the element unstacked, as z-auto does, while it resolves to none. classElement carries the
        // classes, which a clip-path wrapper does not.
        internal static bool Stacks(ReconcilerContext ctx, VisualElement classElement, bool classified, ref int resolvedZ)
        {
            ctx.ZLayerCandidates.Remove(classElement);
            if (!classified)
            {
                return false;
            }
            ctx.ZLayerCandidates.Add(classElement, s_candidate);
            if (!StyleCustomProperties.TryVariableZ(classElement, out var variable))
            {
                return true;
            }
            if (variable is not { } z)
            {
                return false;
            }
            resolvedZ = z;
            return true;
        }

        // The element occupying classElement's slot: its clip-path wrapper where one holds it.
        internal static VisualElement SlotOf(ReconcilerContext ctx, VisualElement classElement)
            => ctx.ClipPathBindings.TryGetValue(classElement, out var binding) ? binding.Wrapper : classElement;

        // SlotOf's inverse.
        internal static VisualElement ClassElementOf(ReconcilerContext ctx, VisualElement slot)
        {
            if (slot.ClassListContains(FiberWrapperElementAppliers.ClipPathWrapperClass))
            {
                foreach (var binding in ctx.ClipPathBindings.Values)
                {
                    if (ReferenceEquals(binding.Wrapper, slot))
                    {
                        return binding.Inner;
                    }
                }
            }
            return slot;
        }

        // Whether declared — the class array as written, variants included — names a z, a flex order or a position
        // anywhere, so the element's stacking has to be tracked. Only the utility after a token's last variant is
        // read, so `md:absolute` counts and `[&>*]:z-10`'s bracketed selector does not.
        internal static bool DeclaresStacking(string[] declared)
        {
            if (declared == null)
            {
                return false;
            }
            foreach (var cls in declared)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var core = StyleArbitraryValueResolver.StripImportant(UtilityOf(cls), out _);
                if (core == "absolute" || core == "relative" || StyleOrderClass.IsOrderToken(core)
                    || core.StartsWith("z-", System.StringComparison.Ordinal)
                    || core.StartsWith("-z-", System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // The utility after cls's last variant separator outside brackets and parentheses.
        private static string UtilityOf(string cls)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < cls.Length; i++)
            {
                switch (cls[i])
                {
                    case '[' or '(':
                        depth++;
                        break;
                    case ']' or ')':
                        depth--;
                        break;
                    case ':' when depth == 0:
                        start = i + 1;
                        break;
                }
            }
            return start == 0 ? cls : cls.Substring(start);
        }

        // Records the z and flex order resolved — the class source the element's passes read, with whatever variant
        // payload is lit — gives the element whose slot real occupies, or forgets them where neither declared nor
        // an anchoring gives a stacking to track. Returns what is recorded.
        internal static StackingFacts? Record(ReconcilerContext ctx, VisualElement real, VisualElement classElement,
            string[] declared, string[] resolved, bool anchored)
        {
            if (!anchored && !DeclaresStacking(declared) && !DeclaresStacking(resolved))
            {
                // A candidate is only ever recorded beside facts.
                if (ctx.StackingFacts.Remove(real))
                {
                    ctx.ZLayerCandidates.Remove(classElement);
                }
                NoteZStacking(real, false);
                return null;
            }
            var classified = TryClassify(resolved, out var z);
            int? stackedZ = Stacks(ctx, classElement, classified, ref z) ? z : null;
            NoteZStacking(real, stackedZ != null);
            var order = 0;
            if (StyleOrderClass.HasOrderClass(resolved) && StyleOrderClass.TryExtract(resolved, out var declaredOrder, out _))
            {
                order = StyleCustomProperties.TryVariableOrder(classElement, out var variable) ? variable ?? 0 : declaredOrder;
            }
            var facts = new StackingFacts(stackedZ, order);
            ctx.StackingFacts[real] = facts;
            return facts;
        }

        // Whether facts alone, whatever the siblings, move an element out of its declared slot: a z other than auto
        // and 0, or a flex order an in-flow element lays out by.
        private static bool IsExplicit(ReconcilerContext ctx, VisualElement real, StackingFacts? facts, VisualElement? parent)
            => facts is { } f
                && ((f.Z is { } z && z != 0 && (ChildLayoutOrder.Available || IsAbsolute(ctx, real)))
                    || (f.Order != 0 && !IsAbsolute(ctx, real) && (parent == null || IsFlexOrGrid(parent))));

        // A zero-footprint stand-in left at a relocated element's declared slot. Carries focusable/tabIndex so it
        // can act as a same-panel Tab proxy (FiberFocusNavigator). display:None is NOT used: it excludes an element
        // from VisualElementFocusRing's ring, which would leave every focus redirect onto it unreachable. Absolute
        // and zero-size instead, so it takes no part in flex flow or in the gap/grid/divide/child-variant walks.
        internal static VisualElement CreatePlaceholder()
        {
            var placeholder = new VisualElement
            {
                focusable = true,
                tabIndex = 0,
            };
            placeholder.style.position = Position.Absolute;
            placeholder.style.width = 0f;
            placeholder.style.height = 0f;
            return placeholder;
        }

        // The box a relocated element lays out at when its flex order puts that somewhere other than its declared
        // slot: zero-size, absolute and unfocusable, since only its layout node takes part, and that one is the
        // relocated element's.
        private static VisualElement CreateAnchor()
        {
            var anchor = new VisualElement { focusable = false, pickingMode = PickingMode.Ignore };
            anchor.style.position = Position.Absolute;
            anchor.style.width = 0f;
            anchor.style.height = 0f;
            return anchor;
        }

        // Create-time entry (FiberNodeFactory): returns the element to install at the slot — a placeholder when the
        // element's own facts relocate it (it joins its parent when the pass drains, ResolveQueuedMount), or outer
        // itself, whose siblings decide at the end of the pass whether it moves. The placeholder -> real pair is
        // registered here, before the drain: AnimatePresence's enter dispatch resolves it during this same pass.
        internal static VisualElement OnCreated(ReconcilerContext ctx, VisualElement outer, VisualElement classElement,
            string[] declared, string[] resolved, bool anchored)
        {
            var facts = Record(ctx, outer, classElement, declared, resolved, anchored);
            if (!IsExplicit(ctx, outer, facts, null))
            {
                return outer;
            }
            var placeholder = CreatePlaceholder();
            Hold(placeholder, Role.Slot, outer);
            Hold(outer, RoleFor(facts!.Value.Z), null, placeholder);
            ctx.ZLayerPlaceholders[placeholder] = outer;
            ctx.PendingPortalMounts.Enqueue((placeholder, new ZLayerMountNode { Real = outer }, null, null, null));
            return placeholder;
        }

        // Drain-time resolution of a queued mount. By now placeholder.parent is set, so the parent is known. An
        // element whose facts changed before the drain (a custom property that stopped resolving) takes its slot.
        internal static void ResolveQueuedMount(ReconcilerContext ctx, VisualElement placeholder, ZLayerMountNode node)
        {
            var parent = placeholder.parent;
            // The parent alone cannot say the pair was torn down: a placeholder inside an element whose creation
            // failed is still parented by that element when this drains. The mapping can, since TakeReal removes it
            // before the real element is pooled.
            if (parent == null || !ctx.ZLayerPlaceholders.ContainsKey(placeholder))
            {
                return;
            }
            var real = node.Real;
            Realign(ctx, parent);
            if (!IsExplicit(ctx, real, ctx.StackingFacts.TryGetValue(real, out var facts) ? facts : null, parent))
            {
                parent.hierarchy.Insert(parent.hierarchy.IndexOf(placeholder), real);
                ctx.ZLayerPlaceholders.Remove(placeholder);
                s_held.Remove(placeholder);
                s_held.Remove(real);
                parent.hierarchy.Remove(placeholder);
                MarkPending(ctx, parent);
                return;
            }
            Hold(real, RoleFor(facts.Z), null, placeholder);
            ctx.ZLayerMembers[real] = new ZLayerMember(placeholder, parent, facts.Z, facts.Order);
            parent.hierarchy.Add(real);
            MarkPending(ctx, parent);
        }

        // A patch put newReal in the slot oldReal held, a clip-path wrapper added around it or removed from it. The
        // placeholder, the relocation record and the facts follow, so newReal is what the bands arrange.
        internal static void Rehome(ReconcilerContext ctx, VisualElement oldReal, VisualElement newReal)
        {
            if (!s_held.TryGetValue(oldReal, out var held))
            {
                return;
            }
            var anchor = AnchorOf(oldReal);
            Hold(newReal, held.Role, held.Partner, held.Slot);
            s_held.Remove(oldReal);
            if (held.Slot != null)
            {
                Hold(held.Slot, Role.Slot, newReal);
                ctx.ZLayerPlaceholders[held.Slot] = newReal;
            }
            if (anchor != null)
            {
                Hold(anchor, Role.Anchor, newReal);
            }
            if (ctx.ZLayerMembers.Remove(oldReal, out var member))
            {
                ctx.ZLayerMembers[newReal] = member;
            }
            if (ctx.StackingFacts.Remove(oldReal, out var facts))
            {
                ctx.StackingFacts[newReal] = facts;
            }
        }

        // Patch-time entry: real (the slot's element, read through its placeholder) was patched to declared /
        // resolved. Explicit facts relocate it at once and their loss returns it to its slot at once, so the slot
        // occupant changes only where the patching diff expects it to; a relocation its siblings decide waits for
        // the end of the pass (Arrange).
        internal static void OnPatched(ReconcilerContext ctx, VisualElement real, VisualElement classElement,
            string[] declared, string[] resolved, bool anchored)
        {
            var facts = Record(ctx, real, classElement, declared, resolved, anchored);
            Settle(ctx, real, facts);
        }

        // The z a custom property supplies classElement changed with no patch of it; null is no integer, which
        // unstacks it. Outside a pass no drain follows, so the parent is arranged at once.
        internal static void Restack(ReconcilerContext ctx, VisualElement classElement, int? resolvedZ)
        {
            var real = SlotOf(ctx, classElement);
            if (!ctx.StackingFacts.TryGetValue(real, out var facts))
            {
                return;
            }
            facts = facts with { Z = resolvedZ };
            ctx.StackingFacts[real] = facts;
            Settle(ctx, real, facts);
            if (ctx.SharedReconcileDepth == 0)
            {
                DrainTeardowns(ctx);
                ResortLayers(ctx);
            }
        }

        // A flex order a custom property supplies classElement changed with no patch of it.
        internal static void Reorder(ReconcilerContext ctx, VisualElement classElement, int order)
        {
            var real = SlotOf(ctx, classElement);
            if (!ctx.StackingFacts.TryGetValue(real, out var facts))
            {
                return;
            }
            facts = facts with { Order = order };
            ctx.StackingFacts[real] = facts;
            Settle(ctx, real, facts);
            if (ctx.SharedReconcileDepth == 0)
            {
                DrainTeardowns(ctx);
                ResortLayers(ctx);
            }
        }

        // A variant re-derived real's class source, which outside a pass nothing follows: the parent is arranged at
        // once there.
        internal static void OnResolved(ReconcilerContext ctx, VisualElement real, VisualElement classElement,
            string[] declared, string[] resolved, bool anchored)
        {
            var facts = Record(ctx, real, classElement, declared, resolved, anchored);
            Settle(ctx, real, facts);
            if (ctx.SharedReconcileDepth == 0)
            {
                DrainTeardowns(ctx);
                ResortLayers(ctx);
            }
        }

        // A variant moved slot's position utility (`md:absolute`) on or off: its parent's arrangement may change.
        internal static void OnPositionToggled(ReconcilerContext ctx, VisualElement classElement)
        {
            var real = SlotOf(ctx, classElement);
            var parent = ParentOf(ctx, real);
            if (parent == null)
            {
                return;
            }
            MarkPending(ctx, parent);
            if (ctx.SharedReconcileDepth == 0)
            {
                DrainTeardowns(ctx);
                ResortLayers(ctx);
            }
        }

        // A variant moved control's flex or grid utility on or off: whether its children take a flex order changed.
        internal static void OnContainerToggled(ReconcilerContext ctx, VisualElement control)
        {
            MarkPending(ctx, control);
            if (ctx.SharedReconcileDepth == 0)
            {
                DrainTeardowns(ctx);
                ResortLayers(ctx);
            }
        }

        private static void Settle(ReconcilerContext ctx, VisualElement real, StackingFacts? facts)
        {
            if (facts == null && !s_held.TryGetValue(real, out _))
            {
                return;
            }
            var placeholder = PlaceholderOf(ctx, real);
            // Still queued: the drain reads the facts again.
            if (placeholder != null && real.parent == null)
            {
                return;
            }
            var parent = placeholder?.parent ?? real.parent;
            if (parent == null)
            {
                return;
            }
            var explicitly = IsExplicit(ctx, real, facts, parent);
            if (placeholder == null)
            {
                if (explicitly)
                {
                    RelocateFromOrdinarySlot(ctx, real, facts!.Value);
                }
            }
            else if (!explicitly && !StaysLifted(ctx, real, parent, facts))
            {
                RelocateToOrdinarySlot(ctx, placeholder, real);
            }
            MarkPending(ctx, parent);
        }

        // Whether real, no longer relocated by its own facts, still paints in the front band its parent's last
        // arrangement lifted the positioned children into. Arrange revisits it at the end of the pass.
        private static bool StaysLifted(ReconcilerContext ctx, VisualElement real, VisualElement parent, StackingFacts? facts)
            => facts is { } f && ctx.ZLayerHosts.TryGetValue(parent, out var record) && record.Lifted
                && (f.Z == 0 || (f.Z == null && IsPositioned(ctx, real)));

        // Marks ghost, a child a PopLayout presence has just pinned out of flow, to paint as a positioned element
        // does while it exits (PinnedGhostZ). A pin is read when its parent is arranged, so it stands for as long as
        // the entry does whatever the ghost's classes say.
        internal static void PinGhost(ReconcilerContext ctx, VisualElement ghost)
        {
            ctx.PinnedGhosts.Remove(ghost);
            ctx.PinnedGhosts.Add(ghost, s_candidate);
            MarkGhostParent(ctx, ghost);
        }

        // A pinned ghost whose exit was cancelled goes back to painting by its own classes.
        internal static void UnpinGhost(ReconcilerContext ctx, VisualElement ghost)
        {
            if (ctx.PinnedGhosts.Remove(ghost))
            {
                MarkGhostParent(ctx, ghost);
            }
        }

        private static void MarkGhostParent(ReconcilerContext ctx, VisualElement ghost)
        {
            var parent = PlaceholderOf(ctx, ghost)?.parent ?? ghost.parent;
            if (parent != null)
            {
                MarkPending(ctx, parent);
            }
        }

        // The placeholder standing at real's declared slot while it is relocated, placed or still queued; null while
        // real is in that slot itself.
        internal static VisualElement? PlaceholderOf(ReconcilerContext ctx, VisualElement real)
            => s_held.TryGetValue(real, out var held) && held.Slot is { } slot && ctx.ZLayerPlaceholders.ContainsKey(slot)
                ? slot
                : null;

        private static VisualElement? ParentOf(ReconcilerContext ctx, VisualElement real)
            => ctx.ZLayerMembers.TryGetValue(real, out var member) ? member.StackingParent : real.parent;

        // Relocates real, still at its declared slot, out of it: a fresh placeholder takes the slot at the same
        // physical index (real becomes invisible to the slot arithmetic in the same step, so the caller's re-fetch
        // at that index reads the placeholder). Nothing leaves the panel; Arrange sorts real into its band.
        private static void RelocateFromOrdinarySlot(ReconcilerContext ctx, VisualElement real, StackingFacts facts)
        {
            var parent = real.parent!;
            var placeholder = CreatePlaceholder();
            Hold(placeholder, Role.Slot, real);
            Hold(real, RoleFor(facts.Z), null, placeholder);
            ctx.ZLayerPlaceholders[placeholder] = real;
            ctx.ZLayerMembers[real] = new ZLayerMember(placeholder, parent, facts.Z, facts.Order);
            parent.hierarchy.Insert(parent.hierarchy.IndexOf(real), placeholder);
            MarkPending(ctx, parent);
        }

        // Returns real to the slot its placeholder holds: the two exchange places by a sort, so real never leaves
        // the panel and every other child keeps its index; the placeholder, now invisible, is removed once the pass
        // is over.
        private static void RelocateToOrdinarySlot(ReconcilerContext ctx, VisualElement placeholder, VisualElement real)
        {
            var parent = placeholder.parent!;
            Exchange(parent, placeholder, real);
            Retire(ctx, placeholder);
            RetireAnchorOf(ctx, real);
            s_held.Remove(real);
            ctx.ZLayerPlaceholders.Remove(placeholder);
            ctx.ZLayerMembers.Remove(real);
            if (ctx.ZLayerHosts.TryGetValue(parent, out var record))
            {
                // The sort laid the list out in physical order again.
                record.Swapped = false;
            }
            MarkPending(ctx, parent);
        }

        // Cleanup entry (FiberElementCleaner.CleanupZLayerPlaceholder): forgets the real element a departing
        // placeholder stood for and returns it, with stillPlaced true when it still stands in the parent. Such an
        // element is retired rather than removed — removing it would shift the index of every child after it, which
        // the caller is about to remove by — and FiberElementCleaner releases it at the end of the pass.
        internal static VisualElement? TakeReal(ReconcilerContext ctx, VisualElement placeholder, out bool stillPlaced)
        {
            stillPlaced = false;
            if (!ctx.ZLayerPlaceholders.Remove(placeholder, out var real))
            {
                return null;
            }
            ctx.PinnedGhosts.Remove(real);
            s_held.Remove(placeholder);
            ctx.ZLayerMembers.Remove(real);
            var parent = real.parent;
            if (parent == null)
            {
                s_held.Remove(real);
                return real;
            }
            // The caller removes the placeholder by its index, which takes whatever layout node sits there.
            Realign(ctx, parent);
            RetireAnchorOf(ctx, real);
            Hold(real, Role.Dead, null);
            stillPlaced = true;
            return real;
        }

        // Forgets a retired real element FiberElementCleaner has taken back out of its parent.
        internal static void ForgetRetired(VisualElement real) => s_held.Remove(real);

        // The (logical parent, logical index) a peer-/group- source search must walk from: a relocated element's
        // PLACEHOLDER position (its declared position among its siblings), not its physical one.
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

        // Removes the retired placeholders and anchors. Runs at the post-pass point DrainPendingPortalMounts runs
        // from, where no diff holds a physical index.
        internal static void DrainTeardowns(ReconcilerContext ctx)
        {
            if (ctx.PendingZLayerRemovals.Count == 0)
            {
                return;
            }
            foreach (var retired in ctx.PendingZLayerRemovals)
            {
                if (retired.parent is { } parent)
                {
                    Realign(ctx, parent);
                    retired.RemoveFromHierarchy();
                    MarkPending(ctx, parent);
                }
                s_held.Remove(retired);
            }
            ctx.PendingZLayerRemovals.Clear();
        }

        // Called where a diff starts over parent's children: the diff inserts and removes by index, which UI
        // Toolkit applies to the layout list at the same index, so the list goes back to physical order first.
        internal static void NoteChildrenReconciled(ReconcilerContext ctx, VisualElement? parent)
        {
            if (parent == null)
            {
                return;
            }
            ctx.PendingZLayerResorts.Add(parent);
            Realign(ctx, parent);
        }

        // Arranges every parent pending since the last call. A parent a diff is parked over keeps its slot occupants
        // (Arrange), and is arranged again when that diff resumes, which notes its children reconciled.
        internal static void ResortLayers(ReconcilerContext ctx)
        {
            if (ctx.PendingZLayerResorts.Count == 0)
            {
                return;
            }
            var queue = ctx.ZLayerArrangeQueue;
            var start = queue.Count;
            queue.AddRange(ctx.PendingZLayerResorts);
            ctx.PendingZLayerResorts.Clear();
            try
            {
                for (var i = start; i < queue.Count; i++)
                {
                    Arrange(ctx, queue[i], ctx.IsParkedParent(queue[i]));
                }
            }
            finally
            {
                queue.RemoveRange(start, queue.Count - start);
            }
        }

        private static void MarkPending(ReconcilerContext ctx, VisualElement parent) => ctx.PendingZLayerResorts.Add(parent);

        private static void Hold(VisualElement element, Role role, VisualElement? partner, VisualElement? slot = null)
        {
            if (s_held.TryGetValue(element, out var held))
            {
                held.Role = role;
                held.Partner = partner;
                held.Slot = slot;
                return;
            }
            s_held.Add(element, new Held { Role = role, Partner = partner, Slot = slot });
        }

        private static Role RoleFor(int? z) => z switch
        {
            < 0 => Role.BackReal,
            > 0 => Role.FrontReal,
            _ => Role.MiddleReal,
        };

        private static void Retire(ReconcilerContext ctx, VisualElement element)
        {
            Hold(element, Role.Dead, null);
            ctx.PendingZLayerRemovals.Add(element);
        }

        private static void RetireAnchorOf(ReconcilerContext ctx, VisualElement real)
        {
            if (s_held.TryGetValue(real, out var held) && held.Partner is { } partner
                && s_held.TryGetValue(partner, out var partnerHeld) && partnerHeld.Role == Role.Anchor)
            {
                Retire(ctx, partner);
            }
        }

        // Puts parent's layout list back in physical order, where an arrangement left a relocated element's layout
        // node at another child's index.
        private static void Realign(ReconcilerContext ctx, VisualElement parent)
        {
            if (!ctx.ZLayerHosts.TryGetValue(parent, out var record) || !record.Swapped)
            {
                return;
            }
            var children = s_layout;
            children.Clear();
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                children.Add(parent.hierarchy[i]);
            }
            ChildLayoutOrder.Rebuild(parent, children);
            children.Clear();
            record.Swapped = false;
        }

        // Moves a to b's index and b to a's, leaving every other child where it is.
        private static void Exchange(VisualElement parent, VisualElement a, VisualElement b)
        {
            var rank = new Dictionary<VisualElement, int>(parent.hierarchy.childCount);
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                rank[parent.hierarchy[i]] = i;
            }
            (rank[a], rank[b]) = (rank[b], rank[a]);
            parent.hierarchy.Sort((x, y) => rank[x].CompareTo(rank[y]));
        }

        // Whether parent lays its children out as flex or grid items, the only ones a flex order moves in CSS. The
        // bundled sheet realises `.grid` as a wrapping flex row, so both are told by their class.
        private static bool IsFlexOrGrid(VisualElement parent)
            => parent.ClassListContains("flex") || parent.ClassListContains("grid");

        // Whether real's own classes position it, as CSS's `position` other than static does: the absolute or
        // relative utility, or a V.Anchored element, which positions itself absolutely.
        private static bool IsPositioned(ReconcilerContext ctx, VisualElement real)
        {
            var classElement = ClassElementOf(ctx, real);
            return classElement.ClassListContains("absolute") || classElement.ClassListContains("relative")
                || ctx.AnchoredBindings.ContainsKey(classElement) || ctx.AnchoredBindings.ContainsKey(real);
        }

        // Whether real is out of flex flow. `.relative` follows `.absolute` in the bundled sheet, so it wins where
        // both are on the list.
        private static bool IsAbsolute(ReconcilerContext ctx, VisualElement real)
        {
            var classElement = ClassElementOf(ctx, real);
            return (classElement.ClassListContains("absolute") && !classElement.ClassListContains("relative"))
                || ctx.AnchoredBindings.ContainsKey(classElement) || ctx.AnchoredBindings.ContainsKey(real);
        }

        private sealed class SlotEntry
        {
            public VisualElement Slot = null!;
            public VisualElement Real = null!;
            public int Declared;
            public int? Z;
            public int Order;
            public bool Tracked;
            public bool Positioned;
            public bool Absolute;
            public bool Relocate;
            public Role Band;
            public VisualElement? Partner;
            public VisualElement? Anchor;
            public bool Detached;

            // Drops the elements, so the pool holds none of them between calls.
            public void Release()
            {
                Slot = null!;
                Real = null!;
                Partner = null;
                Anchor = null;
            }

            public void Reset(VisualElement slot, VisualElement real, int declared)
            {
                Slot = slot;
                Real = real;
                Declared = declared;
                Z = null;
                Order = 0;
                Tracked = false;
                Positioned = false;
                Absolute = false;
                Relocate = false;
                Band = Role.MiddleReal;
                Partner = null;
                Anchor = null;
                Detached = false;
            }
        }

        // Arrange's working state, reused from call to call so arranging a parent allocates nothing once the
        // buffers have grown to its size. Arrange is not re-entered: nothing it calls arranges a parent.
        private static readonly List<SlotEntry> s_entryPool = new();
        private static readonly List<SlotEntry> s_entries = new();
        private static readonly List<SlotEntry> s_back = new();
        private static readonly List<SlotEntry> s_front = new();
        private static readonly List<(VisualElement Element, int Order, int Declared, int Kind)> s_middle = new();
        private static readonly List<(VisualElement Host, VisualElement Follower)> s_followers = new();
        private static readonly List<(VisualElement Host, VisualElement Leader)> s_leaders = new();
        private static readonly List<VisualElement> s_outlineNodes = new();
        private static readonly List<VisualElement> s_desired = new();
        private static readonly HashSet<VisualElement> s_placed = new();
        private static readonly Dictionary<VisualElement, int> s_rank = new();
        private static readonly List<VisualElement> s_layout = new();
        private static readonly List<VisualElement> s_retiredHere = new();
        private static readonly System.Comparison<SlotEntry> s_compareBand = CompareBand;
        private static readonly System.Comparison<(VisualElement Element, int Order, int Declared, int Kind)> s_compareMiddle =
            CompareMiddle;
        private static readonly System.Comparison<VisualElement> s_compareRank = CompareRank;

        // Arranges parent's children into the order CSS paints them in, relocating and returning elements as that
        // requires (see the type's own comment for the layout of the list). parked freezes which elements are
        // relocated, since a parked diff still addresses parent's slot occupants.
        private static void Arrange(ReconcilerContext ctx, VisualElement parent, bool parked)
        {
            if (!ctx.ZLayerHosts.TryGetValue(parent, out var record) && !HoldsTrackedChild(ctx, parent))
            {
                return;
            }
            var entries = s_entries;
            entries.Clear();
            try
            {
                CollectEntries(ctx, parent, entries);
                // Every move below is by index, which UI Toolkit applies to the layout list at the same index.
                Realign(ctx, parent);
                ArrangeEntries(ctx, parent, parked, record, entries);
            }
            finally
            {
                foreach (var entry in entries)
                {
                    entry.Release();
                }
                entries.Clear();
            }
        }

        private static bool HoldsTrackedChild(ReconcilerContext ctx, VisualElement parent)
        {
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    continue;
                }
                var real = ctx.ZLayerPlaceholders.TryGetValue(child, out var placed) ? placed : child;
                if (ctx.StackingFacts.ContainsKey(real) || ctx.PinnedGhosts.TryGetValue(real, out _))
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectEntries(ReconcilerContext ctx, VisualElement parent, List<SlotEntry> entries)
        {
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    continue;
                }
                var real = ctx.ZLayerPlaceholders.TryGetValue(child, out var placed) ? placed : child;
                if (s_entryPool.Count <= entries.Count)
                {
                    s_entryPool.Add(new SlotEntry());
                }
                var entry = s_entryPool[entries.Count];
                entry.Reset(child, real, entries.Count);
                entries.Add(entry);
                // A placeholder whose element never joined the parent (a drain an abort cut short) keeps it.
                if (!ReferenceEquals(real, child) && !ReferenceEquals(real.parent, parent))
                {
                    entry.Detached = true;
                    continue;
                }
                if (ctx.StackingFacts.TryGetValue(real, out var facts))
                {
                    entry.Tracked = true;
                    entry.Z = facts.Z;
                    entry.Absolute = IsAbsolute(ctx, real);
                    entry.Order = entry.Absolute || !IsFlexOrGrid(parent) ? 0 : facts.Order;
                    entry.Positioned = IsPositioned(ctx, real);
                }
                if (ctx.PinnedGhosts.TryGetValue(real, out _))
                {
                    entry.Tracked = true;
                    entry.Z ??= PinnedGhostZ;
                    entry.Absolute = true;
                    entry.Positioned = true;
                    entry.Order = 0;
                }
            }
        }

        private static void ArrangeEntries(ReconcilerContext ctx, VisualElement parent, bool parked,
            ZLayerHostRecord? record, List<SlotEntry> entries)
        {
            var lift = parked ? record?.Lifted ?? false : PositionedPrecedesUnpositioned(entries);
            Classify(entries, parked, lift);

            var changed = false;
            foreach (var entry in entries)
            {
                if (!entry.Detached)
                {
                    changed |= Convert(ctx, parent, entry);
                }
            }
            changed |= SortToDesiredOrder(parent, entries);
            DrainTeardownsUnder(ctx, parent);

            record ??= new ZLayerHostRecord();
            record.Swapped = LayOutRelocated(parent, entries);
            record.Lifted = lift;
            if (AnyRelocated(entries))
            {
                ctx.ZLayerHosts[parent] = record;
            }
            else
            {
                ctx.ZLayerHosts.Remove(parent);
            }
            if (changed)
            {
                ReapplyChildWalkers(ctx, parent);
            }
        }

        private static void Classify(List<SlotEntry> entries, bool parked, bool lift)
        {
            var layoutMovable = ChildLayoutOrder.Available;
            foreach (var entry in entries)
            {
                if (entry.Detached)
                {
                    entry.Relocate = true;
                    continue;
                }
                var lifted = lift && entry.Tracked && (entry.Z == 0 || (entry.Z == null && entry.Positioned));
                entry.Relocate = parked ? !ReferenceEquals(entry.Slot, entry.Real) : Wanted(entry, lifted, layoutMovable);
                entry.Band = entry.Z is < 0 ? Role.BackReal
                    : (entry.Z is > 0 || lifted) ? Role.FrontReal
                    : Role.MiddleReal;
            }
        }

        private static bool Wanted(SlotEntry entry, bool lifted, bool layoutMovable)
            => entry.Tracked
                && ((entry.Z is { } z && z != 0) || entry.Order != 0 || lifted)
                // An in-flow element whose layout node cannot be kept at its slot keeps its slot.
                && (layoutMovable || entry.Absolute || (entry.Z == null && entry.Order != 0 && !lifted));

        // Sorts parent's children into the order BuildDesiredOrder gives. Returns whether any moved.
        private static bool SortToDesiredOrder(VisualElement parent, List<SlotEntry> entries)
        {
            BuildDesiredOrder(parent, entries);
            try
            {
                if (InOrder(parent, s_desired))
                {
                    return false;
                }
                s_rank.Clear();
                for (var i = 0; i < s_desired.Count; i++)
                {
                    s_rank[s_desired[i]] = i;
                }
                parent.hierarchy.Sort(s_compareRank);
                s_rank.Clear();
                return true;
            }
            finally
            {
                s_desired.Clear();
            }
        }

        private static bool AnyRelocated(List<SlotEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Relocate && !entry.Detached)
                {
                    return true;
                }
            }
            return false;
        }

        // Relocates entry's element or returns it to its slot as entry.Relocate says, and settles the box it lays out
        // by. Returns whether a slot occupant changed.
        private static bool Convert(ReconcilerContext ctx, VisualElement parent, SlotEntry entry)
        {
            var changed = false;
            var relocated = !ReferenceEquals(entry.Slot, entry.Real);
            if (entry.Relocate && !relocated)
            {
                var placeholder = CreatePlaceholder();
                Hold(placeholder, Role.Slot, entry.Real);
                ctx.ZLayerPlaceholders[placeholder] = entry.Real;
                parent.hierarchy.Add(placeholder);
                entry.Slot = placeholder;
                changed = true;
            }
            else if (!entry.Relocate && relocated)
            {
                Retire(ctx, entry.Slot);
                RetireAnchorOf(ctx, entry.Real);
                s_held.Remove(entry.Real);
                ctx.ZLayerPlaceholders.Remove(entry.Slot);
                ctx.ZLayerMembers.Remove(entry.Real);
                entry.Slot = entry.Real;
                changed = true;
            }
            if (!entry.Relocate)
            {
                return changed;
            }
            // The node an in-flow element lays out by goes where its flex order puts it: its placeholder's, when
            // that is its declared slot, or an anchor's.
            if (entry.Band != Role.MiddleReal && !entry.Absolute)
            {
                if (entry.Order == 0)
                {
                    RetireAnchorOf(ctx, entry.Real);
                    entry.Partner = entry.Slot;
                }
                else
                {
                    entry.Anchor = AnchorOf(entry.Real) ?? NewAnchor(parent, entry.Real);
                    entry.Partner = entry.Anchor;
                }
            }
            else
            {
                RetireAnchorOf(ctx, entry.Real);
            }
            Hold(entry.Real, entry.Band, entry.Partner, entry.Slot);
            ctx.ZLayerMembers[entry.Real] = new ZLayerMember(entry.Slot, parent, entry.Z, entry.Order);
            return changed;
        }

        // Puts each relocated in-flow element's layout node at its partner's index and the partner's at its own.
        // Returns whether any was.
        private static bool LayOutRelocated(VisualElement parent, List<SlotEntry> entries)
        {
            var layout = s_layout;
            layout.Clear();
            try
            {
                foreach (var entry in entries)
                {
                    if (!entry.Relocate || entry.Detached || entry.Partner == null)
                    {
                        continue;
                    }
                    if (layout.Count == 0)
                    {
                        for (var i = 0; i < parent.hierarchy.childCount; i++)
                        {
                            layout.Add(parent.hierarchy[i]);
                        }
                    }
                    var at = layout.IndexOf(entry.Real);
                    var partnerAt = layout.IndexOf(entry.Partner);
                    layout[at] = entry.Partner;
                    layout[partnerAt] = entry.Real;
                }
                if (layout.Count == 0)
                {
                    return false;
                }
                ChildLayoutOrder.Rebuild(parent, layout);
                return true;
            }
            finally
            {
                layout.Clear();
            }
        }

        // CSS paints an unpositioned in-flow child before every positioned one: positioned children (and those at
        // z-index 0) need lifting only where one would otherwise come before an unpositioned child that paints.
        private static bool PositionedPrecedesUnpositioned(List<SlotEntry> entries)
        {
            (int Order, int Declared)? firstPositioned = null;
            foreach (var entry in entries)
            {
                var positioned = entry.Tracked && (entry.Z == 0 || (entry.Z == null && entry.Positioned));
                if (positioned && (firstPositioned == null || Precedes((entry.Order, entry.Declared), firstPositioned.Value)))
                {
                    firstPositioned = (entry.Order, entry.Declared);
                }
            }
            if (firstPositioned == null)
            {
                return false;
            }
            foreach (var entry in entries)
            {
                var unpositioned = !entry.Detached && !entry.Positioned && entry.Z == null
                    && !StyleOutOfFlowChild.HasNoBox(entry.Real) && !StyleOutOfFlowChild.IsOutOfFlow(entry.Real);
                if (unpositioned && Precedes(firstPositioned.Value, (entry.Order, entry.Declared)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Precedes((int Order, int Declared) a, (int Order, int Declared) b)
            => a.Order != b.Order ? a.Order < b.Order : a.Declared < b.Declared;

        private static VisualElement? AnchorOf(VisualElement real)
            => s_held.TryGetValue(real, out var held) && held.Partner is { } partner
                && s_held.TryGetValue(partner, out var partnerHeld) && partnerHeld.Role == Role.Anchor
                ? partner
                : null;

        private static VisualElement NewAnchor(VisualElement parent, VisualElement real)
        {
            var anchor = CreateAnchor();
            Hold(anchor, Role.Anchor, real);
            parent.hierarchy.Add(anchor);
            return anchor;
        }

        // The paint order, into s_desired: the back band, the declared slots with the in-flow elements placed by
        // flex order, the front band, then whatever else the parent holds in its present order. A ring band or a
        // face border overlay follows the element it sits after, and a backdrop layer leads the element it sits
        // before.
        private static void BuildDesiredOrder(VisualElement parent, List<SlotEntry> entries)
        {
            s_followers.Clear();
            s_leaders.Clear();
            s_outlineNodes.Clear();
            VisualElement? host = null;
            VisualElement? leader = null;
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                // An outline paints after everything its stacking context holds (OutlineHosting), so it follows no
                // element and stays behind all of them.
                if (child.ClassListContains(RingOverlay.OutlineMarkerClass))
                {
                    s_outlineNodes.Add(child);
                    continue;
                }
                if (child.ClassListContains(RingOverlay.MarkerClass) || child.ClassListContains(FaceBorderOverlay.MarkerClass))
                {
                    if (host != null)
                    {
                        s_followers.Add((host, child));
                    }
                    continue;
                }
                if (child.ClassListContains(BackdropFilterLayer.MarkerClass))
                {
                    leader = child;
                    continue;
                }
                if (leader != null)
                {
                    s_leaders.Add((child, leader));
                    leader = null;
                }
                host = child;
            }

            s_back.Clear();
            s_front.Clear();
            s_middle.Clear();
            foreach (var entry in entries)
            {
                s_middle.Add((entry.Slot, 0, entry.Declared, 0));
                if (!entry.Relocate || entry.Detached)
                {
                    continue;
                }
                switch (entry.Band)
                {
                    case Role.BackReal:
                        s_back.Add(entry);
                        break;
                    case Role.FrontReal:
                        s_front.Add(entry);
                        break;
                    default:
                        s_middle.Add((entry.Real, entry.Order, entry.Declared, 1));
                        break;
                }
                if (entry.Anchor != null)
                {
                    s_middle.Add((entry.Anchor, entry.Order, entry.Declared, 1));
                }
            }
            s_back.Sort(s_compareBand);
            s_front.Sort(s_compareBand);
            s_middle.Sort(s_compareMiddle);

            s_desired.Clear();
            s_placed.Clear();
            PlaceLeadingInternals(parent);
            foreach (var entry in s_back) Place(entry.Real);
            foreach (var item in s_middle) Place(item.Element);
            foreach (var entry in s_front) Place(entry.Real);
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var child = parent.hierarchy[i];
                if (!s_outlineNodes.Contains(child) && s_placed.Add(child))
                {
                    s_desired.Add(child);
                }
            }
            foreach (var node in s_outlineNodes)
            {
                s_desired.Add(node);
            }
            s_placed.Clear();
            s_followers.Clear();
            s_leaders.Clear();
            s_outlineNodes.Clear();
            s_back.Clear();
            s_front.Clear();
            s_middle.Clear();
        }

        // What an element holds ahead of its children, which paint over it: an inset ring band, a SceneView's
        // camera feed and a Particles element's layers.
        private static void PlaceLeadingInternals(VisualElement parent)
        {
            for (var i = 0; i < parent.hierarchy.childCount; i++)
            {
                var leading = parent.hierarchy[i];
                if (leading.ClassListContains(RingOverlay.InsetMarkerClass)
                    || leading.ClassListContains(SceneViewDriver.FeedMarkerClass)
                    || leading.ClassListContains(ParticlesDriver.LayerMarkerClass))
                {
                    Place(leading);
                }
            }
        }

        private static void Place(VisualElement element)
        {
            if (!s_placed.Add(element))
            {
                return;
            }
            foreach (var (host, leader) in s_leaders)
            {
                if (ReferenceEquals(host, element) && s_placed.Add(leader))
                {
                    s_desired.Add(leader);
                }
            }
            s_desired.Add(element);
            foreach (var (host, follower) in s_followers)
            {
                if (ReferenceEquals(host, element) && s_placed.Add(follower))
                {
                    s_desired.Add(follower);
                }
            }
        }

        // z, then flex order, then tree order: CSS stacks equal z values in order-modified document order.
        private static int CompareBand(SlotEntry a, SlotEntry b)
        {
            var byZ = (a.Z ?? 0).CompareTo(b.Z ?? 0);
            if (byZ != 0) return byZ;
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : a.Declared.CompareTo(b.Declared);
        }

        private static int CompareMiddle((VisualElement Element, int Order, int Declared, int Kind) a,
            (VisualElement Element, int Order, int Declared, int Kind) b)
            => a.Order != b.Order ? a.Order.CompareTo(b.Order)
                : a.Declared != b.Declared ? a.Declared.CompareTo(b.Declared)
                : a.Kind.CompareTo(b.Kind);

        private static int CompareRank(VisualElement a, VisualElement b) => s_rank[a].CompareTo(s_rank[b]);

        private static bool InOrder(VisualElement parent, List<VisualElement> desired)
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(parent.hierarchy[i], desired[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // Removes the retired children of parent at once: Arrange runs where no diff holds an index into it.
        private static void DrainTeardownsUnder(ReconcilerContext ctx, VisualElement parent)
        {
            if (ctx.PendingZLayerRemovals.Count == 0)
            {
                return;
            }
            s_retiredHere.Clear();
            foreach (var retired in ctx.PendingZLayerRemovals)
            {
                if (ReferenceEquals(retired.parent, parent))
                {
                    s_retiredHere.Add(retired);
                }
            }
            foreach (var retired in s_retiredHere)
            {
                retired.RemoveFromHierarchy();
                s_held.Remove(retired);
                ctx.PendingZLayerRemovals.Remove(retired);
            }
            s_retiredHere.Clear();
        }

        // The child walkers that read the children in flex order re-run once that order or the elements in it moved
        // without the parent's own patch, which is what re-runs them otherwise.
        private static void ReapplyChildWalkers(ReconcilerContext ctx, VisualElement parent)
        {
            if (ctx.GapManipulators.TryGetValue(parent, out var gap)) gap.Reapply();
            if (ctx.GridManipulators.TryGetValue(parent, out var grid)) grid.Apply();
            if (ctx.DivideManipulators.TryGetValue(parent, out var divide)) divide.Reapply();
        }
    }
}
