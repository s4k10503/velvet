#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.UIElements;

namespace Velvet
{
    // Diff algorithm for child-node arrays.
    // Matches old and new child nodes by key (or position when unkeyed) and emits the minimal set of
    // mount / patch / move / remove operations to transform one array into the other.
    // Parent ownership precondition:
    // Reconcile / ReconcileIndexed / ReconcileKeyed own the slot range
    // [slotStart, slotStart + oldNodes.Length) of parent's children. The
    // invariant is parent.childCount >= slotStart + oldNodes.Length; slots outside this
    // range (siblings owned by other fibers or static children) are not touched. With the default
    // slotStart = 0 the reconciler owns the entire children list as the single tenant.
    // External code must not mutate slots inside the reconciler's range, otherwise oldNodes
    // indices diverge from DOM indices and the wrong element gets patched or removed. Velvet
    // provides separate-target management via FiberVirtualListController and
    // FiberPortalRegistry.
    internal sealed class ChildReconciler
    {
        // Sentinel marking the initial start of the Remove phase.
        // When IndexedReconcileState.ResumeIndex equals this value, the Remove phase
        // begins reverse traversal from oldNodes.Length - 1.
        private const int RemovePhaseStartSentinel = -1;

        private readonly ReconcilerContext _ctx;
        private readonly FiberNodePatcher _patcher;
        private readonly FiberNodeFactory _factory;
        private readonly FiberElementCleaner _cleaner;
        // LIS anchor computation + non-anchor re-placement for the keyed/general commit.
        private readonly ChildElementPlacement _placement;
        // Identity-key resolution shared with the general path.
        private readonly ReconcileKeying _keying;
        // Inline-expansion + live-context commit path (Component/Provider/Fragment/Suspense/Memo/
        // AnimatePresence containers). The fast Indexed/Keyed diff below handles flat host-leaf lists.
        private readonly GeneralPathReconciler _general;
        // Stopwatch for frame-budget measurement.
        // Recursive invocations through IReconcilerHost.ReconcileChildren always run with
        // frameBudgetMs=0, so Restart() is never called twice. Violating this assumption distorts
        // the outer budget measurement.
        private Stopwatch? _stopwatch;

        // Suspended state of time-sliced reconciliation.
        // null indicates no suspension (the normal state).
        internal IndexedReconcileState? PendingIndexedState { get; private set; }

        // Suspended state of keyed reconciliation. null indicates no suspension.
        // The intermediate buffers used by Pass 2 are owned by this state (not returned to the
        // Pool while suspended).
        internal KeyedReconcileState? PendingKeyedState { get; private set; }

        public ChildReconciler(ReconcilerContext ctx, FiberNodePatcher patcher, FiberNodeFactory factory, FiberElementCleaner cleaner)
        {
            _ctx = ctx;
            _patcher = patcher;
            _factory = factory;
            _cleaner = cleaner;
            _placement = new ChildElementPlacement(ctx.BufferPool);
            _keying = new ReconcileKeying(ctx);
            _general = new GeneralPathReconciler(ctx, patcher, factory, cleaner, _placement, _keying);
        }

        // Applies the CanPatch-gated patch-or-replace decision at one DOM slot: patches the existing
        // element in place when the pair is patch-compatible, otherwise removes the existing element
        // (when slotExists) and inserts a freshly created one at the same slot. Shared by the
        // Common-phase indexed loop and both keyed Pass-1 linear-scan implementations (sync and
        // time-sliced) so this eager remove-then-create sequence cannot independently drift the way
        // FiberVirtualListController's own separately hand-rolled copy did — it skipped the CanPatch
        // gate entirely, patching a same-key type flip onto a stale element.
        //   slotExists: false only for the indexed diff's DOM-desync recovery, where a transient
        //     AnimatePresence overlap can leave the live range shorter than the baseline; the keyed
        //     prefix scan's slot always exists by construction (Pass 1 never changes childCount ahead
        //     of index i).
        //   Removal deliberately stays BEFORE CreateElement (remove-then-create), not
        //     create-then-remove: an inline-mounted component fiber is keyed by
        //     (parentFiber, positionKey, identity) — NOT by which VisualElement currently hosts it
        //     (FiberRenderer.SetupMount's comment on the registry key; ComponentRegistry.cs). If the
        //     OLD element (and the same-keyed nested fiber ComponentRegistry still has registered
        //     under it) is still present when CreateElement builds the NEW element's same-keyed
        //     child, the registry hands back the OLD, still-live fiber instead of mounting fresh —
        //     confirmed by a regression in ComponentStateReparityTests
        //     (Given_ANestedComponentWithAdvancedState_When_TheHostElementTypeChanges_...) when this
        //     was tried. FiberElementCleaner.CleanupElement's own comment on DisposeFibersUnder
        //     explains why: an inline fiber under a torn-down host is otherwise invisible to the
        //     normal orphan sweep and "would survive to be re-paired as a zombie on a same-key
        //     re-entry" — which is exactly what create-then-remove reintroduces.
        //   newElement is ALWAYS inserted, abort or not: _ctx.IsAborted only ever becomes true
        //     via FiberErrorBoundary.TryCatch -> SetAborted, which fires exclusively on TryShowFallback's
        //     SUCCESS path (fiber.Reconciler.Reconcile(fiber.MountPoint, ..., fallbackTree) returned
        //     without throwing and FallbackContentFailed is false) — i.e. by the time CreateElement
        //     returns, newElement already holds a fully, successfully rendered subtree (the boundary's
        //     fallback content in place of whatever threw beneath it), not a half-built one. Discarding
        //     it and leaving the slot empty would be strictly worse: an error boundary that catches must
        //     never leave the DOM with nothing where its fallback should be. Velvet has no render/commit
        //     phase split that could defer this decision, so the nearest equivalent is "don't throw away
        //     content the boundary already finished building".
        // Returns true when the caller must stop scanning further siblings, which is narrower than
        // discarding anything: the boundary already resolved the failure, so any later slot's
        // patch/replace would be racing a reconcile the caller no longer owns end-to-end.
        private bool PatchOrReplaceAtSlot(
            in ChildSlot slot, VNode? oldNode, VNode? newNode, bool slotExists)
        {
            var parent = slot.Parent;
            // LOGICAL, converted per DOM touch. It is deliberately not pre-converted into a physical index:
            // the removal below shifts every physical index after it, so a value captured before it would be
            // stale by the insert.
            var logicalIndex = slot.SlotStart + slot.Index;
            if (slotExists && ReconcileKeying.CanPatch(oldNode, newNode))
            {
                var domElement = parent.ElementAt(LogicalChildSlots.ToPhysical(parent, logicalIndex));
                var actualElement = _patcher.ResolveWrapped(domElement);
                _patcher.PatchNode(actualElement, oldNode, newNode);
                return false;
            }

            if (slotExists)
            {
                _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, logicalIndex));
            }
            var newElement = _factory.CreateElement(newNode);
            // ToPhysical clamps by construction — a slot past the last rendered child resolves to the
            // position just after it — so the caller no longer chooses between two index formulas.
            parent.Insert(LogicalChildSlots.ToPhysical(parent, logicalIndex), newElement);
            return _ctx.IsAborted;
        }

        // One child's address inside its parent. The range start stays separate from the sibling index
        // rather than being pre-summed: the keyed path reconciles an unkeyed node by its raw index while
        // addressing the DOM at the sum, so collapsing the two would lose the keying coordinate.
        internal readonly struct ChildSlot
        {
            internal VisualElement Parent { get; init; }
            internal int SlotStart { get; init; }
            internal int Index { get; init; }
        }

        // The old-key map, the two consumed-key sets and the result list that one keyed Pass 2 threads
        // through every node it processes. Bundled because the per-node step is shared by the synchronous
        // loop and by the time-sliced one, which restores the same four from its parked state — and a
        // combination mixing one pass's tables with another's would silently misplace a row.
        internal readonly struct KeyedPassTables
        {
            internal Dictionary<ChildKey, (int index, VNode? node)> OldKeyMap { get; init; }
            internal HashSet<ChildKey> UsedKeys { get; init; }
            internal HashSet<ChildKey> ReplacedKeys { get; init; }
            internal List<(VisualElement? element, bool isExisting)> NewElements { get; init; }
        }

        public void Reconcile(VisualElement? parent, VNode?[] oldChildren, VNode?[] newChildren,
            double frameBudgetMs = 0, int slotStart = 0, int slotLimit = int.MaxValue)
        {
            if (_ctx.IsAborted) return;

            // Slot indices are LOGICAL throughout: a slot counts rendered children only, and the
            // reconciler-invisible ones (a z-index layer container, a filter bounds spacer, a ring band)
            // are skipped by the one conversion applied where the DOM is actually touched — see
            // LogicalChildSlots. This entry used to fold a back z-layer container's leading offset into
            // slotStart instead, which worked only while every invisible child was pinned to one end of the
            // list; a band that must sit beside its own element cannot be. Nothing is corrected here now,
            // and the parked-slot rebasing that existed to keep those baked-in offsets true across a
            // container appearing or disappearing is gone with it: a logical slot does not move when an
            // invisible child does.

            // New entries discard any prior suspended state. ContinueXxx calls ReconcileXxxFrom
            // directly and bypasses this path, so discarding here does not break a Continue run.
            // Nested calls entered via PatchNode during ContinueXxx also pass through here, but at
            // that point ContinueXxx has already taken ownership of pending (= null), so this is a no-op.
            PendingIndexedState = null;
            DiscardPendingKeyedState();

            // ContextProviderNode / ComponentNode / OutletNode are expanded inline (no wrapper VE
            // emitted). Old-side expansion is structural-only (no context push, no fiber render).
            // New-side expansion pushes each Provider's value onto the stack and, while it is
            // still pushed, notifies dependent fibers if the value changed vs the old Provider that
            // held the same tree position (see ProviderPairKey), then pops — so propagation snapshots
            // include the new value. ComponentNode children render synchronously during new-side
            // expansion via ComponentRegistry.GetOrCreateInline; the fiber's PreviousTree is
            // expanded recursively in place of the ComponentNode.
            //
            // oldFibers and newFibers accumulate inline-mounted ComponentFiber references seen
            // during each side's expansion. The diff (oldFibers \ newFibers) identifies orphan
            // fibers whose ComponentNode disappeared from the new tree; they are disposed after
            // the reconcile so fiber-side resources are released. This pairing is scoped to a
            // single Reconcile call, so a sibling fiber's setState re-render (which calls
            // Reconcile with a different parent/slotStart) does not falsely orphan unrelated
            // siblings under the same anchor.
            var oldProviders = _ctx.BufferPool.RentProviderTable();
            var oldFibers = _ctx.BufferPool.RentFiberList();
            var newFibers = _ctx.BufferPool.RentFiberSet();
            var pairing = new GeneralPathReconciler.InlinePairing
            {
                OldFibers = oldFibers,
                NewFibers = newFibers,
                OldProviders = oldProviders,
            };
            VNode?[] oldNodes;
            try
            {
                // Old side is always expanded structurally into the flat leaf array used for matching.
                // (No context push, no render — it reproduces the previously committed leaf order.)
                oldNodes = _general.ExpandInlineForReconcile(oldChildren, isNewSide: false, parent, slotStart, oldFibers, newFibers, oldProviders);

                if (GeneralPathReconciler.NeedsExpansion(newChildren))
                {
                    // General path: a single live-context walk commits each emitted leaf
                    // (CreateElement / PatchNode) while its ancestor Providers are still pushed, so
                    // element descendants render in-scope without a pre-captured snapshot. Orphan
                    // effect-cleanup + sweep and the LIS reorder are performed inside.
                    _general.ReconcileGeneral(parent, oldNodes, newChildren, slotStart, in pairing);
                }
                else
                {
                    // Fast path: the container is a flat list of host leaves (no ComponentNode /
                    // ContextProviderNode / FragmentNode / SuspenseNode / MemoNode / null). This
                    // retains the time-sliced Indexed/Keyed diff state machine unchanged. The keyed
                    // path is selected if either side carries a key (unifying keyed/unkeyed transitions).
                    _general.RunOrphanEffectCleanups(oldFibers, newFibers);
                    var newNodes = newChildren ?? Array.Empty<VNode>();
                    if (_keying.HasAnyKey(newNodes) || _keying.HasAnyKey(oldNodes))
                    {
                        ReconcileKeyed(parent, oldNodes, newNodes, frameBudgetMs, slotStart, slotLimit);
                    }
                    else
                    {
                        ReconcileIndexed(parent, oldNodes, newNodes, frameBudgetMs, slotStart, slotLimit);
                    }
                    _general.SweepOrphans(oldFibers, newFibers);
                }
            }
            finally
            {
                _ctx.BufferPool.ReturnProviderTable(oldProviders);
                _ctx.BufferPool.ReturnFiberList(oldFibers);
                _ctx.BufferPool.ReturnFiberSet(newFibers);
            }
        }

        // Mounts every Portal that FiberNodeFactory.CreateElement queued during the
        // reconcile pass that just finished. Each pending Portal's target reconcile re-enters
        // Reconcile, which may enqueue more Portals (nested case) — they flow into
        // the same queue and are drained in FIFO order until empty so each Portal mounts at
        // target.childCount that already reflects every earlier-drained Portal's contribution.
        // Called exclusively by Reconciler.Reconcile's top-level finally so the
        // single-top-level-loop invariant holds across nested PatchPortal recursion.
        internal void DrainPendingPortalMounts()
        {
            HashSet<ComponentFiber>? childFibersBefore = null;
            while (_ctx.PendingPortalMounts.Count > 0)
            {
                var (placeholder, node, target, contextSnapshot, logicalParent) = _ctx.PendingPortalMounts.Dequeue();
                // A queue entry can outlive its placeholder: a Suspense primary that suspends rolls
                // its created elements back as ORPHANS before this drain runs (the entry survives
                // the rollback), and an ErrorBoundary abort mid-drain reaches the later entries
                // with the pass already dead. Mounting would create — or populate — a host for a
                // subtree that no longer exists, so skip without touching any host.
                if (_ctx.IsAborted || placeholder.parent == null)
                {
                    continue;
                }
                // Resolve the deferred targets: a same-panel portal (a registered id, or the container
                // the caller passed) arrived with its target resolved at enqueue; a layer portal
                // creates (or reuses) the per-layer framework host here, and a world-space node
                // creates its per-instance host here — the placeholder is attached by now, so the
                // declaring panel whose settings/theme the host copies is known.
                VNode?[] children;
                switch (node)
                {
                    case PortalNode { Layer: { } layer } layerPortal:
                        (target, children) = ResolveLayerPortalTarget(placeholder, layerPortal, layer);
                        break;
                    case WorldSpaceNode worldSpaceNode:
                        (target, children) = ResolveWorldSpacePortalTarget(placeholder, worldSpaceNode);
                        break;
                    case PortalNode samePanelPortal:
                        (target, children) = ResolveSamePanelPortalTarget(target, samePanelPortal);
                        break;
                    case ZLayerMountNode zLayerMount:
                        // Not a host mount: the real element is already fully built (CreateElement / a
                        // patch-time none-to-z transition ran its whole child reconcile inline, under live
                        // context, like any ordinary sibling) — only its CONTAINER placement was deferred,
                        // because placeholder.parent (the stacking parent) is only knowable now. No target
                        // resolution, no nested Reconcile, no PortalState entry: resolve the layer placement
                        // and move on to the next queued entry. `this` is passed through so a first-of-its-
                        // sign container creation can rebase a park THIS SAME instance's Reconcile() call just
                        // captured (see RebasePendingSlotStartIfTargeting) — invisible to any other lookup
                        // until this whole top-level call returns.
                        FiberZLayerCoordinator.ResolveQueuedMount(_ctx, placeholder, zLayerMount);
                        continue;
                    default:
                        // Only PortalNode / WorldSpaceNode enqueue deferred mounts; anything else is
                        // a missing branch for a new node kind and must fail loudly rather than
                        // mount nothing in silence.
                        FiberLogger.LogWarning("Portal",
                            $"Unsupported deferred host mount node: {node.GetType().Name}. Entry skipped.");
                        continue;
                }
                var resolvedTarget = target!;
                // Append after the target's existing rendered children: LogicalChildSlots.Count is the
                // logical slot the next child takes, and the conversion at each DOM touch is what keeps a
                // trailing filter bounds-spacer last and skips a leading z-layer container. Stored logical,
                // which is the basis every consumer of PortalState.SlotStart now shares — it no longer has to
                // be re-derived against the target's live physical structure, so a container appearing or
                // disappearing between this mount and a later patch cannot move it.
                var slotStart = LogicalChildSlots.Count(resolvedTarget);
                // Restore the context that enclosed the Portal's tree position (captured at enqueue) so the
                // children mount under their enclosing Providers / MotionContext rather than an empty cursor.
                // The children's own reconcile pushes/pops on top of these and balances out, so popping the
                // snapshot afterwards returns the cursor to empty.
                var stack = _ctx.ComponentContextStack;
                if (contextSnapshot != null)
                {
                    for (var s = 0; s < contextSnapshot.Count; s++)
                    {
                        stack.PushRaw(contextSnapshot[s].Key, contextSnapshot[s].Value);
                    }
                }
                // A Portal child registers in ComponentRegistry under whatever fiber is current
                // while it mounts, and a later patch of the same Portal reaches
                // FiberNodePatcher.PatchPortalChildren from the reconcile of the tree the declaring
                // component itself returned — with that component's own fiber current. So the declaring
                // fiber is the half of the registry key a patch looks the child up by, and the mount has
                // to agree or the first patch misses its own fiber and builds a second one. This drain
                // runs from the top-level reconcile finally instead, long after that fiber came off the
                // stack, so it pushes the one captured at enqueue (LogicalParent) back for the nested
                // reconcile below. Same push, for the same registry-parent reason, as the VirtualList
                // detached-item scope (Reconciler.BeginDetachedItemScope). A Portal reconciled outside any
                // component body has no declaring fiber, and then neither side has one to key on.
                var declaringFiber = logicalParent is { IsDisposed: false } ? logicalParent : null;
                // Snapshot the anchor's direct children before, so the fibers added by this Portal's
                // reconcile can be identified afterwards and stamped with the context needed to reconstruct
                // their enclosing Providers on an isolated re-render (the spine cannot otherwise reach them).
                var drainAnchor = declaringFiber ?? _ctx.FiberStack.Current;
                CaptureAnchorChildren(drainAnchor, ref childFibersBefore);
                // The nested Reconcile below re-enters ChildReconciler.Reconcile on THIS SAME instance, whose
                // entry unconditionally discards PendingIndexedState/PendingKeyedState as stale state left by a
                // finished pass — correct for a genuine fresh top-level call, but wrong here: this drain runs
                // from the OUTER pass's own top-level finally (Reconciler.Reconcile / Reconciler.ContinueReconcile),
                // so when that outer pass just parked (budget exhausted mid-list, with a Portal enqueued right
                // before parking), the entry-clear below would destroy the park before the caller ever observes
                // HasPendingWork — the continuation then never resumes and the remaining rows silently never
                // commit. Move the parked state out of the instance fields first — never through
                // DiscardPendingKeyedState, which returns PendingKeyedState's pooled buffers to the shared pool;
                // a nested keyed Pass 2 below can rent the exact same pool types and would be handed back, and
                // mutate, the very buffers this parked state still owns — so the nested entry-clear becomes a
                // no-op, then restore both fields verbatim once this one nested call returns. Always safe to
                // restore unconditionally: the call below never carries a frameBudgetMs argument (defaults to 0),
                // and at budget 0 ReconcileIndexedFrom's `budgeted` gate is false while ReconcileKeyed routes to
                // the fully synchronous ReconcileKeyedSync — neither can ever set new pending state of its own, so
                // there is nothing legitimate from the nested call itself to preserve instead. Scoped to just this
                // call (not the whole drain loop) so FiberZLayerCoordinator's own rebase of a self-caused park
                // (RebaseParkedSlotsForContainerChange, triggered only between loop iterations or after the loop
                // via DrainTeardowns, never while this call is on the stack) still sees the real parked state
                // everywhere else in the drain.
                var parkedIndexed = PendingIndexedState;
                var parkedKeyed = PendingKeyedState;
                PendingIndexedState = null;
                PendingKeyedState = null;
                // Same set-and-restore as FiberNodePatcher.PatchPortalChildren, and for the same reason:
                // this is the mount half of the pair that stamps the fibers a Portal's own children
                // reconcile creates. A nested Portal enqueued here is drained by a later turn of this loop,
                // with the field already restored, so its own entry sets its own placeholder.
                var enclosingPortal = _ctx.CurrentPortalPlaceholder;
                var enclosingChildScope = _ctx.PortalChildKeyScope;
                _ctx.CurrentPortalPlaceholder = placeholder;
                // Pushing the declaring fiber gives this Portal's children the registry parent that
                // fiber's own in-tree children already have, so the two would otherwise collide on a
                // whole key. The scope is the member that separates them —
                // ReconcilerContext.PortalChildKeyScope owns it.
                _ctx.PortalChildKeyScope = placeholder;
                if (declaringFiber != null) _ctx.FiberStack.Push(declaringFiber);
                try
                {
                    Reconcile(resolvedTarget, Array.Empty<VNode>(), children, slotStart: slotStart);
                }
                finally
                {
                    if (declaringFiber != null) _ctx.FiberStack.Pop();
                    _ctx.PortalChildKeyScope = enclosingChildScope;
                    _ctx.CurrentPortalPlaceholder = enclosingPortal;
                    if (contextSnapshot != null)
                    {
                        for (var s = contextSnapshot.Count - 1; s >= 0; s--)
                        {
                            stack.PopRaw(contextSnapshot[s].Key);
                        }
                    }
                    PendingIndexedState = parkedIndexed;
                    PendingKeyedState = parkedKeyed;
                }
                StampDrainedChildren(drainAnchor, childFibersBefore, contextSnapshot, children, logicalParent);
                // The growth this mount contributed, in logical slots — the same basis as slotStart.
                var slotLength = LogicalChildSlots.Count(resolvedTarget) - slotStart;
                _ctx.PortalState[placeholder] = new PortalSlotInfo(
                    resolvedTarget, slotStart, slotLength, (node as PortalNode)?.TargetId, logicalParent);
            }
            // Same safe (post-pass, no diff in flight) context as the drain above: a container that lost its
            // last member this pass tears down here, never synchronously mid-diff. `this` mirrors
            // ResolveQueuedMount's own reason above (a self-caused park from THIS instance's own pass).
            FiberZLayerCoordinator.DrainTeardowns(_ctx);
        }

        // Taken by ref so one set serves the whole drain loop: each entry's snapshot is consumed by its own
        // StampDrainedChildren call before the next entry clears it.
        private static void CaptureAnchorChildren(ComponentFiber? anchor, ref HashSet<ComponentFiber>? before)
        {
            if (anchor == null) return;
            (before ??= new HashSet<ComponentFiber>()).Clear();
            for (var f = anchor.Child; f != null; f = f.Sibling)
            {
                before.Add(f);
            }
        }

        // Stamps the fibers this drain entry just added under anchor with what an isolated re-render needs
        // to rebuild their enclosing Providers — the same diff-against-a-before-set that
        // FiberNodePatcher.StampNewTopLevelChildren applies on the patch half of the pair.
        private static void StampDrainedChildren(
            ComponentFiber? anchor, HashSet<ComponentFiber>? before,
            List<KeyValuePair<object, object>>? contextSnapshot, VNode?[] children, ComponentFiber? logicalParent)
        {
            if (anchor == null) return;
            DetachedMountContext? detachedContext = null;
            for (var f = anchor.Child; f != null; f = f.Sibling)
            {
                if (before!.Contains(f)) continue;
                detachedContext ??= new DetachedMountContext(contextSnapshot, children, anchor, logicalParent);
                f.DetachedMountContext = detachedContext;
            }
        }

        private (VisualElement Target, VNode?[] Children) ResolveLayerPortalTarget(
            VisualElement placeholder, PortalNode layerPortal, UILayer layer)
        {
            if (!_ctx.LayerHosts.TryGetValue(layer, out var layerHost)
                || layerHost.Document == null)
            {
                // Also lands here when a scene unload killed a previously created host:
                // the dead record is replaced so new portals mount into a live panel.
                layerHost = PanelHostFactory.CreateLayerHost(layer, placeholder.panel, _ctx);
                _ctx.LayerHosts[layer] = layerHost;
            }
            else
            {
                // Recurring re-sync point for late declaring resolution and runtime
                // drift (see SyncDeclaring).
                PanelHostFactory.SyncDeclaring(layerHost, layer, placeholder.panel, _ctx);
            }
            var target = layerHost.Document.rootVisualElement;
            var children = layerPortal.Children ?? Array.Empty<VNode>();
            FiberFocusNavigator.ConfigureChainedPlaceholder(placeholder, layerHost,
                layerPortal.FocusOrder == PanelFocusOrder.Chained, _ctx);
            return (target, children);
        }

        private (VisualElement Target, VNode?[] Children) ResolveWorldSpacePortalTarget(
            VisualElement placeholder, WorldSpaceNode worldSpaceNode)
        {
            var record = PanelHostFactory.CreateWorldSpaceHost(worldSpaceNode, placeholder.panel, _ctx);
            _ctx.WorldSpaceBindings[placeholder] = record;
            var target = record.Document.rootVisualElement;
            var children = worldSpaceNode.Children ?? Array.Empty<VNode>();
            FiberFocusNavigator.ConfigureChainedPlaceholder(placeholder, record,
                worldSpaceNode.FocusOrder == PanelFocusOrder.Chained, _ctx);
            return (target, children);
        }

        private (VisualElement Target, VNode?[] Children) ResolveSamePanelPortalTarget(
            VisualElement? target, PortalNode samePanelPortal)
        {
            // The target was resolved (non-null) at enqueue: the create path never queues a registry
            // portal without one, and an element-valued portal arrives already holding its container.
            var children = samePanelPortal.Children ?? Array.Empty<VNode>();
            // Same-panel synthetic-bubbling bridge: attached ONCE per resolved target,
            // guarded by SamePanelPortalBridges (see its own comment for why the guard
            // lives here rather than at a single creation call site — unlike a layer/
            // world-space host, a same-panel target is an ordinary, already-existing
            // element with no one-time "just created it" moment to hook). Never attached
            // from FiberPortalRegistry.Register itself: that registry is a bare global
            // static with no ReconcilerContext to guard duplicate attaches with or later
            // dispose the bridge through.
            var samePanelTarget = target!;
            if (!_ctx.SamePanelPortalBridges.ContainsKey(samePanelTarget))
            {
                _ctx.SamePanelPortalBridges[samePanelTarget] =
                    FiberCrossPanelEventDispatcher.AttachBridge(samePanelTarget, _ctx);
            }
            return (samePanelTarget, children);
        }

        // Resumes a suspended IndexedReconcile.
        // Does nothing when PendingIndexedState is null.
        internal void ContinueIndexed(double frameBudgetMs)
        {
            if (PendingIndexedState == null) return;

            var state = PendingIndexedState.Value;
            PendingIndexedState = null;
            ReconcileIndexedFrom(state.Parent, state.OldNodes, state.NewNodes,
                state.ResumePhase, state.ResumeIndex, frameBudgetMs, state.SlotStart, state.SlotLimit);
        }

        // Resumes a suspended KeyedReconcile.
        // Does nothing when PendingKeyedState is null.
        internal void ContinueKeyed(double frameBudgetMs)
        {
            if (PendingKeyedState == null) return;

            var state = PendingKeyedState;
            PendingKeyedState = null;
            ReconcileKeyedFrom(state, frameBudgetMs);
        }

        // Shifts the captured SlotStart of whichever time-sliced state is currently parked by
        // delta. Called when a preceding sibling fiber re-renders with a child-count
        // delta while this fiber's reconcile is suspended mid-pass: the sibling's mutation physically
        // shifts this fiber's already-committed rows within the shared parent, so the parked
        // SlotStart (a captured absolute offset) must move by the same delta or the resume would
        // write the remaining rows at stale absolute indices and corrupt both slots. No-op when nothing
        // is parked.
        internal void RebasePendingSlotStart(int delta)
        {
            if (delta == 0) return;

            if (PendingIndexedState.HasValue)
            {
                var s = PendingIndexedState.Value;
                PendingIndexedState = new IndexedReconcileState(
                    s.Parent, s.OldNodes, s.NewNodes, s.ResumePhase, s.ResumeIndex,
                    s.SlotStart + delta, ShiftSlotLimit(s.SlotLimit, delta));
            }
            else if (PendingKeyedState != null)
            {
                PendingKeyedState.SlotStart += delta;
                PendingKeyedState.SlotLimit = ShiftSlotLimit(PendingKeyedState.SlotLimit, delta);
            }
        }

        // SlotLimit is the next inline sibling's MountSlotStart (an absolute parent index), which
        // PropagateInlineSlotShift moves by the same delta when a preceding sibling resizes — so a parked
        // limit must shift in lockstep with SlotStart or the resumed Common-phase boundary check
        // (slotStart + i < Math.Min(childCount, slotLimit)) would test against a stale end and patch/create
        // the wrong row at the seam. The int.MaxValue sentinel (this fiber is the last tenant, unbounded)
        // is preserved unshifted.
        private static int ShiftSlotLimit(int slotLimit, int delta)
            => slotLimit == int.MaxValue ? slotLimit : slotLimit + delta;


        // Discards a suspended KeyedReconcile state and returns the held Pool buffers.
        // Invoked on a new Reconcile or during Dispose.
        internal void DiscardPendingKeyedState()
        {
            if (PendingKeyedState == null) return;
            ReleaseKeyedBuffers(PendingKeyedState);
            PendingKeyedState = null;
        }

        #region Reconciliation Strategies

        // Entry point — placed outside the region to make the strategy-selection (indexed / keyed)
        // entry visible.
        // Index-based reconciliation. Used when no keys are present.
        // When frameBudgetMs > 0, frame-budget control is enabled and the work suspends on overrun.
        private void ReconcileIndexed(VisualElement? parent, VNode?[] oldNodes, VNode?[] newNodes,
            double frameBudgetMs, int slotStart = 0, int slotLimit = int.MaxValue)
        {
            ReconcileIndexedFrom(parent, oldNodes, newNodes,
                IndexedReconcilePhase.Common, startIndex: 0, frameBudgetMs, slotStart, slotLimit);
        }

        // Recovers from a baseline/DOM desync where the live container is SHORTER than this fiber's keyed
        // baseline claims (a transient AnimatePresence exit-ghost overlap under rapid re-key / nav). When the
        // fiber's range [slotStart, slotStart + oldNodes.Length) does not fit in the live children, the keyed
        // diff's positional invariant is broken, so rebuild the range from the authoritative new tree: remove the
        // shortened live range, then create + insert every new node. Returns true when it handled the desync
        // (the caller must then skip the normal diff). A no-op (false) on the normal path.
        //
        // slotLimit bounds the END of THIS fiber's slot range (the next inline-mount sibling's MountSlotStart, or
        // int.MaxValue when this fiber is the last/only tenant of the parent). Several inline fibers share one
        // parent, so the rebuild must rebuild ONLY [slotStart, slotLimit) — deleting to parent.childCount would
        // destroy a following sibling's committed rows (its keyed range was over-deleted, then its elements pooled
        // and rented back into this fiber's rebuild). Bounding by slotLimit also makes the desync detection more
        // precise: the diff over-indexes whenever the baseline exceeds THIS fiber's live rows (it would read
        // parent.ElementAt past slotLimit, into a sibling), even when trailing siblings would otherwise pad the
        // raw childCount past the baseline.
        private bool TryRebuildDesyncedSlotRange(
            VisualElement? parent, VNode?[]? oldNodes, VNode?[]? newNodes, int slotStart, int slotLimit)
        {
            if (parent == null || oldNodes == null || newNodes == null) return false;
            // The LOGICAL count, not raw childCount: a filter bounds-spacer on a skewed / shadowed +
            // filtered parent is not a keyed row, so it must not pad `available` (masking a real desync) nor be
            // the first element the removal loop below tears out.
            var rangeEnd = Math.Min(LogicalChildSlots.Count(parent), slotLimit);
            var available = rangeEnd - slotStart;
            if (available < 0) available = 0;
            if (oldNodes.Length <= available) return false;

            // Logical slots, converted per step: the walk is downward so each removal only shifts slots
            // after the one just removed, and an invisible child interleaved in the range is skipped rather
            // than torn out with the rendered ones.
            for (var i = rangeEnd - 1; i >= slotStart; i--)
            {
                _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, i));
                if (_ctx.IsAborted) return true;
            }
            for (var i = 0; i < newNodes.Length; i++)
            {
                var element = _factory.CreateElement(newNodes[i]);
                if (_ctx.IsAborted) return true;
                parent.Insert(LogicalChildSlots.ToPhysical(parent, slotStart + i), element);
            }
            return true;
        }

        #region Indexed Pass

        // Inputs fixed for the whole indexed reconcile. A readonly struct passed `in`, NOT a heap state
        // object like the keyed path's KeyedReconcileState: the unkeyed diff is the framework's hottest
        // list update and almost always runs at a zero budget with nothing to park, so a per-reconcile
        // allocation here would be paid by every plain list render. The resumable cursor (phase + index)
        // deliberately stays out of this struct — it lives in the dispatcher's locals while running and in
        // IndexedReconcileState while parked.
        private readonly struct IndexedPassInputs
        {
            public readonly VisualElement Parent;
            public readonly VNode?[] OldNodes;
            public readonly VNode?[] NewNodes;
            public readonly int SlotStart;
            public readonly int SlotLimit;

            public IndexedPassInputs(
                VisualElement parent, VNode?[] oldNodes, VNode?[] newNodes, int slotStart, int slotLimit)
            {
                Parent = parent;
                OldNodes = oldNodes;
                NewNodes = newNodes;
                SlotStart = slotStart;
                SlotLimit = slotLimit;
            }

            // Derived on demand rather than stored, so the shared-prefix length has exactly one definition
            // for the dispatcher and all three phases.
            public int CommonLength => Math.Min(OldNodes.Length, NewNodes.Length);
        }

        // State-machine dispatcher for indexed reconciliation, mirroring ReconcileKeyedFrom: each phase is
        // its own method, the successor phase and its start index are assigned here, and a phase that
        // yields has already parked its own resume point.
        private void ReconcileIndexedFrom(
            VisualElement? parent,
            VNode?[] oldNodes,
            VNode?[] newNodes,
            IndexedReconcilePhase startPhase,
            int startIndex,
            double frameBudgetMs,
            int slotStart = 0,
            int slotLimit = int.MaxValue)
        {
            // Normally a no-op because `Reconcile()` already cleared the state on entry, but kept as
            // a safety net for future internal callers that invoke `ReconcileIndexedFrom` directly.
            // Resumption via ContinueIndexed (startPhase != Common or startIndex != 0) must not
            // overwrite the saved state, so leave it untouched.
            if (startPhase == IndexedReconcilePhase.Common && startIndex == 0)
            {
                PendingIndexedState = null;
                DiscardPendingKeyedState();
            }

            if (parent == null) return;

            var inputs = new IndexedPassInputs(parent, oldNodes, newNodes, slotStart, slotLimit);

            if (frameBudgetMs > 0)
            {
                _stopwatch ??= new Stopwatch();
                _stopwatch.Restart();
            }

            var phase = startPhase;
            var index = startIndex;
            while (phase != IndexedReconcilePhase.Done)
            {
                switch (phase)
                {
                    case IndexedReconcilePhase.Common:
                        if (!CommonPhase(in inputs, index, frameBudgetMs)) return;
                        phase = IndexedReconcilePhase.Remove;
                        index = RemovePhaseStartSentinel;
                        break;
                    case IndexedReconcilePhase.Remove:
                        if (!RemovePhase(in inputs, index, frameBudgetMs)) return;
                        phase = IndexedReconcilePhase.Add;
                        index = inputs.CommonLength;
                        break;
                    case IndexedReconcilePhase.Add:
                        if (!AddPhase(in inputs, index, frameBudgetMs)) return;
                        phase = IndexedReconcilePhase.Done;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"[ChildReconciler] Unhandled IndexedReconcilePhase: {phase}");
                }
                // An error-boundary abort parks nothing and must end the pass here — unlike a yield, which
                // already parked its own resume point, an aborted phase reports completion so this is the
                // only place that can stop the machine before it advances into the next phase's work.
                if (_ctx.IsAborted) return;
            }
        }

        // Parks the resume point for a phase that ran out of budget. Only ever called where the caller has
        // proven another iteration remains, so HasPendingWork never reports a resume that has nothing left.
        private void ParkIndexedResume(
            in IndexedPassInputs inputs, IndexedReconcilePhase resumePhase, int resumeIndex)
        {
            PendingIndexedState = new IndexedReconcileState(
                inputs.Parent, inputs.OldNodes, inputs.NewNodes,
                resumePhase, resumeIndex, slotStart: inputs.SlotStart, slotLimit: inputs.SlotLimit);
            _stopwatch!.Stop();
        }

        // Returns false only on a yield (this phase parked its resume point); true when the phase is done
        // with the range — including when an abort cut it short, which the dispatcher detects separately.
        private bool CommonPhase(in IndexedPassInputs inputs, int startIndex, double frameBudgetMs)
        {
            var parent = inputs.Parent;
            var oldNodes = inputs.OldNodes;
            var newNodes = inputs.NewNodes;
            var slotStart = inputs.SlotStart;
            var slotLimit = inputs.SlotLimit;
            var commonLength = inputs.CommonLength;
            var budgeted = frameBudgetMs > 0;

            for (var i = startIndex; i < commonLength; i++)
            {
                if (_ctx.IsAborted) return true;

                // Identical VNode instances are guaranteed to be diff-free. Skipping the DOM
                // lookup and DiffProps invocation suppresses outlier GC allocations on the
                // no-change path.
                if (ReferenceEquals(oldNodes[i], newNodes[i]))
                {
                    if (budgeted && _stopwatch!.Elapsed.TotalMilliseconds > frameBudgetMs)
                    {
                        ParkIndexedResume(in inputs, IndexedReconcilePhase.Common, resumeIndex: i + 1);
                        return false;
                    }
                    continue;
                }

                // DOM-desync recovery: the baseline (oldNodes) is a positional prefix of the live children,
                // but a transient AnimatePresence overlap (an exit ghost whose VE was dropped, or a key
                // re-entered mid-exit) can leave the live container SHORTER than the baseline claims, so the
                // slot this index expects no longer exists. Rather than over-index parent.ElementAt, create the
                // model's child to converge the DOM toward the (authoritative) new tree; the missing tail is
                // built the same way and the Remove phase skips the absent slots.
                // Bound by slotLimit (this fiber's range end), not just childCount: for a non-last inline
                // tenant childCount includes the following sibling's rows, so an unbounded check would treat a
                // sibling row as this fiber's and PATCH it. Out of range → create within this fiber's range.
                // One walk answers both "is the slot occupied" and "where is it", where a Count bound
                // check would scan the whole child list per element on a phase that mutates nothing.
                var slotExists = slotStart + i < slotLimit
                    && LogicalChildSlots.TryGetPhysical(parent, slotStart + i, out _);
                var slot = new ChildSlot { Parent = parent, SlotStart = slotStart, Index = i };
                if (PatchOrReplaceAtSlot(in slot, oldNodes[i], newNodes[i], slotExists))
                {
                    return true;
                }

                if (budgeted && _stopwatch!.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    ParkIndexedResume(in inputs, IndexedReconcilePhase.Common, resumeIndex: i + 1);
                    return false;
                }
            }

            return true;
        }

        // Removal proceeds from the tail, so the resume index is "the tail index to process" and
        // RemovePhaseStartSentinel means "begin at oldNodes.Length - 1". Decoding the sentinel and the
        // guard against re-encoding it (below) describe the same encoding and must move together.
        private bool RemovePhase(in IndexedPassInputs inputs, int startIndex, double frameBudgetMs)
        {
            var parent = inputs.Parent;
            var slotStart = inputs.SlotStart;
            var commonLength = inputs.CommonLength;
            var budgeted = frameBudgetMs > 0;

            var removeStart = startIndex == RemovePhaseStartSentinel ? inputs.OldNodes.Length - 1 : startIndex;
            for (var i = removeStart; i >= commonLength; i--)
            {
                if (_ctx.IsAborted) return true;

                // DOM-desync recovery (see the Common phase): when the live container is shorter than the
                // baseline claims, the tail slot to remove may not exist — skip it instead of over-indexing.
                // One walk resolves both the existence test and the physical index it removes at.
                if (!LogicalChildSlots.TryGetPhysical(parent, slotStart + i, out var removeAt))
                {
                    continue;
                }
                _cleaner.RemoveElement(parent, removeAt);

                if (budgeted && _stopwatch!.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    var nextRemoveIndex = i - 1;
                    if (nextRemoveIndex >= commonLength)
                    {
                        // Park only when another iteration remains. nextRemoveIndex ==
                        // RemovePhaseStartSentinel (-1) is this phase's own "begin at the tail" encoding,
                        // so parking that value would restart the whole phase instead of resuming it —
                        // the nextRemoveIndex >= commonLength test is what keeps the two apart.
                        ParkIndexedResume(in inputs, IndexedReconcilePhase.Remove, nextRemoveIndex);
                        return false;
                    }
                    // nextRemoveIndex < commonLength: no Remove iterations remain, so nothing is parked and
                    // the pass falls into Add already over budget. Deliberate, not an oversight — Add runs a
                    // budget check after its first element, bounding the overrun to one element. Do not
                    // "fix" this into a yield.
                }
            }

            return true;
        }

        private bool AddPhase(in IndexedPassInputs inputs, int startIndex, double frameBudgetMs)
        {
            var parent = inputs.Parent;
            var newNodes = inputs.NewNodes;
            var slotStart = inputs.SlotStart;
            var budgeted = frameBudgetMs > 0;

            for (var i = startIndex; i < newNodes.Length; i++)
            {
                if (_ctx.IsAborted) return true;

                var newElement = _factory.CreateElement(newNodes[i]);
                if (_ctx.IsAborted) return true;
                // Insert at the absolute slot to avoid colliding with siblings outside this fiber's
                // range when slotStart > 0. When the range covers the entire children list, this is
                // equivalent to Add (slotStart + i == parent.childCount at this point). Clamp to childCount so a
                // DOM-desync resume directly into this phase (the live range shrank since the slice was parked)
                // appends rather than over-indexing — symmetric with the Common-phase create-on-missing guard.
                parent.Insert(LogicalChildSlots.ToPhysical(parent, slotStart + i), newElement);

                if (budgeted && _stopwatch!.Elapsed.TotalMilliseconds > frameBudgetMs)
                {
                    var nextAddIndex = i + 1;
                    if (nextAddIndex < newNodes.Length)
                    {
                        // Park only when another iteration remains.
                        // No pending is needed when the final element has already been processed.
                        ParkIndexedResume(in inputs, IndexedReconcilePhase.Add, nextAddIndex);
                        return false;
                    }
                    // nextAddIndex >= newNodes.Length: all Add iterations done → fall through.
                }
            }

            return true;
        }

        #endregion

        // Entry point — placed outside the region; routes the Keyed path to sync or time-sliced execution.
        // Key-based reconciliation (two passes + time-slicing support).
        // Pass 1: linear scan from the head, fast-patching the prefix where keys match.
        // Pass 2: handle the remainder via map lookup.
        // A two-pass keyed-children diff (prefix fast-patch then map lookup); because Velvet yields per VNode
        // at the ChildReconciler layer, every VNode within each phase can suspend on a budget check.
        // When frameBudgetMs > 0, the suspendable state-machine path is taken.
        // When 0 or below, execution is fully synchronous as before (no state object allocation).
        private void ReconcileKeyed(VisualElement? parent, VNode?[] oldNodes, VNode?[] newNodes,
            double frameBudgetMs, int slotStart = 0, int slotLimit = int.MaxValue)
        {
            if (frameBudgetMs <= 0)
            {
                ReconcileKeyedSync(parent, oldNodes, newNodes, slotStart, slotLimit);
                return;
            }

            var state = new KeyedReconcileState
            {
                Parent = parent,
                OldNodes = oldNodes,
                NewNodes = newNodes,
                Phase = KeyedReconcilePhase.Pass1Linear,
                LinearEnd = 0,
                ResumeIndex = 0,
                SlotStart = slotStart,
                SlotLimit = slotLimit,
            };
            ReconcileKeyedFrom(state, frameBudgetMs);
        }

        #region Keyed Pass — Sync

        // Fully synchronous version of keyed reconciliation, run when frameBudgetMs == 0: walks the diff to
        // completion inline instead of allocating and driving a KeyedReconcileState, since a zero budget
        // means there is nothing to resume later and the state machine's buffers would be pure overhead.
        private void ReconcileKeyedSync(VisualElement? parent, VNode?[] oldNodes, VNode?[] newNodes,
            int slotStart = 0, int slotLimit = int.MaxValue)
        {
            // DOM-desync recovery: the keyed diff relies on "oldNodes index == live DOM index" (Pass 1 scans
            // and Pass 2 looks up parent.ElementAt(slotStart + index)). A transient AnimatePresence ghost overlap
            // can leave the live range SHORTER than the baseline, breaking that invariant. Per-site index guards
            // here would mis-patch by stale index, so instead rebuild this fiber's slot range from the
            // authoritative new tree (crash-free, immediately convergent; the next clean reconcile runs the normal
            // keyed diff). See ReconcilerDesyncRecoveryTests for the deterministic guard-contract regression.
            if (TryRebuildDesyncedSlotRange(parent, oldNodes, newNodes, slotStart, slotLimit)) return;
            if (parent == null) return;

            if (!RunLinearPrefixPass(parent, oldNodes, newNodes, slotStart, out var linearEnd)) return;
            if (TrySuffixTrimFastPath(parent, oldNodes, newNodes, slotStart, linearEnd)) return;
            ReconcileKeyedRemainder(parent, oldNodes, newNodes, slotStart, linearEnd);
        }

        // False when this pass finished the whole reconcile on its own — aborted mid-patch, every entry
        // matched, or what remained was a pure tail insert or tail remove.
        // Invariants:
        //   Advance only while keys match (break on oldKey != newKey).
        //   Each iteration permits only the following operations:
        //     1. In-place patch (CanPatch=true).
        //     2. Remove + insert at the same index (CanPatch=false).
        //   Both keep parent.childCount unchanged and do not move elements at indices below i.
        //   Therefore, after this pass completes, the DOM state
        //     parent.ElementAt(slotStart + j) == newNodes[j]
        //   holds for the range 0..linearEnd-1.
        //   Indices outside [slotStart, slotStart + max(old.Length, new.Length)) are untouched,
        //   so the invariant
        //     oldNodes[i].index == slotStart + i
        //   continues to hold in ReconcileKeyedRemainder (for i ≥ linearEnd).
        private bool RunLinearPrefixPass(VisualElement parent, VNode?[] oldNodes, VNode?[] newNodes,
            int slotStart, out int linearEnd)
        {
            var commonLength = Math.Min(oldNodes.Length, newNodes.Length);
            linearEnd = 0;
            for (var i = 0; i < commonLength; i++)
            {
                // Reference-identical VNodes are diff-free (immutable per render; auto-memoization hands
                // back the cached instance). Skip the key compare, DOM lookup, and PatchNode — identity
                // implies key equality, so the prefix advances and the in-place DOM slot stays valid.
                if (!ReferenceEquals(oldNodes[i], newNodes[i]))
                {
                    var oldKey = _keying.EffectiveKey(oldNodes[i]);
                    var newKey = _keying.EffectiveKey(newNodes[i]);
                    if (oldKey != newKey) break;

                    var slot = new ChildSlot { Parent = parent, SlotStart = slotStart, Index = i };
                    if (PatchOrReplaceAtSlot(in slot, oldNodes[i], newNodes[i],
                            slotExists: true))
                    {
                        return false;
                    }
                }
                // Update only when an operation succeeded; left unchanged on break.
                linearEnd = i + 1;
            }

            // All entries matched linearly.
            if (linearEnd == oldNodes.Length && linearEnd == newNodes.Length) return false;

            // Tail-add only.
            if (linearEnd == oldNodes.Length)
            {
                for (var i = linearEnd; i < newNodes.Length; i++)
                    parent.Insert(LogicalChildSlots.ToPhysical(parent, slotStart + i), _factory.CreateElement(newNodes[i]));
                return false;
            }

            // Tail-remove only.
            if (linearEnd == newNodes.Length)
            {
                for (var i = oldNodes.Length - 1; i >= linearEnd; i--)
                    _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                return false;
            }

            return true;
        }

        // True when the whole remaining diff was committed here.
        // Symmetric counterpart to the linear head scan: match key-equal, patch-compatible pairs from the
        // TAIL inward (count only — no mutation yet). The win this targets is pure-prepend (and a leading
        // change with an unchanged tail): the whole map build + LIS reorder is skipped when the
        // middle that remains after trimming both ends collapses to a single contiguous insert OR remove.
        // A reorder (both middles non-empty) genuinely needs the LIS, so it falls through to the remainder
        // pass with NO mutation having happened here. Guards baked into the conditions:
        //   - unkeyed parity: ReconcileKey is Positional(index) for an unkeyed node, so an unkeyed tail
        //     matches only when its old and new indices coincide (length unchanged) — never across the
        //     index shift a prepend introduces, where pure-LIS would remount by position.
        //   - patch-in-place only: a CanPatch=false tail would need remove+insert (shifting absolute DOM
        //     indices and stranding an AnimatePresence exit timer), so it stops the trim and defers to LIS.
        //   - the desync rebuild in the caller still runs first (this prepass never sees a short live range).
        private bool TrySuffixTrimFastPath(VisualElement parent, VNode?[] oldNodes, VNode?[] newNodes,
            int slotStart, int linearEnd)
        {
            var suffix = CountTrimmableSuffix(oldNodes, newNodes, linearEnd);
            var oldMidEnd = oldNodes.Length - suffix; // exclusive end of the old middle window
            var newMidEnd = newNodes.Length - suffix; // exclusive end of the new middle window
            // Take the fast path only when the middle collapses to a single insert OR remove AND every key is
            // distinct on both sides. The element ORDER the prepass commits is the new key order by
            // construction, so a duplicate key never reorders — but Pass 2 de-duplicates a repeated key (one
            // element for N occurrences) whereas inserting/patching positionally would not, so a duplicate must
            // defer to the remainder pass to preserve that established behavior. Unkeyed nodes carry
            // Positional(index) keys,
            // which are inherently distinct, so an unkeyed list is never rejected by this check.
            if (suffix == 0 || (oldMidEnd != linearEnd && newMidEnd != linearEnd)
                || !AllReconcileKeysUnique(oldNodes) || !AllReconcileKeysUnique(newNodes))
            {
                return false;
            }

            CommitSuffixTrim(parent, oldNodes, newNodes, slotStart, linearEnd, suffix);
            return true;
        }

        // How many key-equal, patch-compatible pairs the tails share, counted inward and without mutating
        // anything — the caller decides whether the shape that remains is worth the fast path.
        private int CountTrimmableSuffix(VNode?[] oldNodes, VNode?[] newNodes, int linearEnd)
        {
            var suffix = 0;
            while (oldNodes.Length - suffix > linearEnd && newNodes.Length - suffix > linearEnd)
            {
                var oi = oldNodes.Length - 1 - suffix;
                var ni = newNodes.Length - 1 - suffix;
                var o = oldNodes[oi];
                var n = newNodes[ni];
                if (!ReferenceEquals(o, n))
                {
                    if (!_keying.ReconcileKey(o, oi).Equals(_keying.ReconcileKey(n, ni))) break;
                    if (!ReconcileKeying.CanPatch(o, n)) break;
                }
                suffix++;
            }

            return suffix;
        }

        // Stops on abort mid-way like every other commit here: the caller has already reported the diff as
        // handled, and the aborted pass leaves the DOM for the next reconcile to converge.
        private void CommitSuffixTrim(VisualElement parent, VNode?[] oldNodes, VNode?[] newNodes,
            int slotStart, int linearEnd, int suffix)
        {
            var oldMidEnd = oldNodes.Length - suffix;
            var newMidEnd = newNodes.Length - suffix;
            // Patch the matched suffix in place at its OLD DOM positions (Pass 1 left the tail untouched,
            // so oldNodes[oldMidEnd + k] is still at slotStart + oldMidEnd + k). The middle insert/remove
            // below then shifts these elements to their final positions via UI Toolkit's auto-shift.
            for (var k = 0; k < suffix; k++)
            {
                var oi = oldMidEnd + k;
                var ni = newMidEnd + k;
                if (ReferenceEquals(oldNodes[oi], newNodes[ni])) continue;
                var actualElement = _patcher.ResolveWrapped(parent.ElementAt(LogicalChildSlots.ToPhysical(parent, slotStart + oi)));
                _patcher.PatchNode(actualElement, oldNodes[oi], newNodes[ni]);
                if (_ctx.IsAborted) return;
            }

            if (oldMidEnd == linearEnd)
            {
                // Old middle empty → pure insert of new[linearEnd, newMidEnd) ahead of the suffix.
                for (var i = linearEnd; i < newMidEnd; i++)
                {
                    parent.Insert(LogicalChildSlots.ToPhysical(parent, slotStart + i), _factory.CreateElement(newNodes[i]));
                    if (_ctx.IsAborted) return;
                }
            }
            else
            {
                // New middle empty → pure remove of old[linearEnd, oldMidEnd). Reverse so each removal
                // leaves the not-yet-removed indices intact.
                for (var i = oldMidEnd - 1; i >= linearEnd; i--)
                {
                    _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                    if (_ctx.IsAborted) return;
                }
            }
        }

        private void ReconcileKeyedRemainder(VisualElement parent, VNode?[] oldNodes, VNode?[] newNodes,
            int slotStart, int linearEnd)
        {
            var pool = _ctx.BufferPool;
            var oldKeyMap = pool.RentOldKeyMap();
            var usedKeys = pool.RentKeySet();
            var replacedKeys = pool.RentReplacedKeySet();
            var newElements = pool.RentElementList();
            var tables = new KeyedPassTables
            {
                OldKeyMap = oldKeyMap,
                UsedKeys = usedKeys,
                ReplacedKeys = replacedKeys,
                NewElements = newElements,
            };
            // Records indices of old elements overwritten by a duplicate key.
            // Normally empty; only used when a duplicate-key warning is triggered.
            var orphanedOldIndices = pool.RentOrphanedIndexSet();

            try
            {
                // The i in oldNodes[i] is the index in the oldNodes array. Because Pass 1
                // updated 0..linearEnd-1 in place, the invariant
                //   oldNodes index == DOM index
                // holds at this point. An unkeyed node reconciles by its full sibling index i so the
                // same array slot patches across renders; the new-side walk below looks up the same
                // index.
                for (var i = linearEnd; i < oldNodes.Length; i++)
                {
                    _keying.RegisterOldKey(oldNodes[i], i, oldKeyMap, orphanedOldIndices);
                }

                for (var i = linearEnd; i < newNodes.Length; i++)
                {
                    if (_ctx.IsAborted) return;
                    var slot = new ChildSlot { Parent = parent, SlotStart = slotStart, Index = i };
                    if (ProcessKeyedNode(in slot, newNodes[i], in tables)) return;
                }

                // Reverse walk so each RemoveElement leaves not-yet-visited indices intact. An
                // unkeyed node's key is its full sibling index i, the same value the build loop used.
                for (var i = oldNodes.Length - 1; i >= linearEnd; i--)
                {
                    if (ShouldRemoveOldKeyedEntry(oldNodes[i], i, usedKeys, replacedKeys, orphanedOldIndices))
                    {
                        _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                    }
                }

                // After the removal phase the DOM holds only the retained isExisting elements (their
                // element.parent == parent; created/replaced orphans have parent == null). Compute the
                // LIS over their current positions — the anchors that stay put — then re-place the rest.
                // The region starts at slotStart + linearEnd (the linear-prefix matches are in place).
                var range = new ChildElementPlacement.PlacementRange
                {
                    SlotStart = slotStart,
                    ScanStart = slotStart + linearEnd,
                    OldLen = oldNodes.Length,
                    LogicalNewLen = newNodes.Length,
                };
                _placement.ComputeAnchorsAndReorder(parent, newElements, in range);
            }
            finally
            {
                pool.Return(oldKeyMap);
                pool.ReturnKeySet(usedKeys);
                pool.ReturnReplacedKeySet(replacedKeys);
                pool.Return(newElements);
                pool.ReturnOrphanedIndexSet(orphanedOldIndices);
            }
        }

        // True when every node's ReconcileKey is distinct across the list. The suffix-trim fast path inserts /
        // patches positionally and so would render N elements for a key repeated N times, whereas Pass 2
        // de-duplicates it; the prepass therefore defers a duplicate-key list to Pass 2. Unkeyed nodes carry
        // Positional(index) keys, which are inherently distinct, so an unkeyed list always passes. Uses a pooled
        // key set (no allocation after warmup) and is only reached on the collapse-to-insert/remove shapes.
        private bool AllReconcileKeysUnique(VNode?[] nodes)
        {
            if (nodes.Length < 2) return true;
            var seen = _ctx.BufferPool.RentKeySet();
            try
            {
                for (var i = 0; i < nodes.Length; i++)
                {
                    if (!seen.Add(_keying.ReconcileKey(nodes[i], i))) return false;
                }
                return true;
            }
            finally
            {
                _ctx.BufferPool.ReturnKeySet(seen);
            }
        }

        #endregion

        #region Keyed Pass — Time-sliced

        // State-machine dispatcher for keyed reconciliation.
        // Transitions through phases and returns buffers on yield (budget overrun) or bailout (exception).
        // Pass2BuildMap resumes indexing from state.LinearEnd rather than 0: the common prefix Pass1Linear
        // already matched 1:1 by position, so those entries need no key-map lookup in Pass 2.
        private void ReconcileKeyedFrom(KeyedReconcileState state, double frameBudgetMs)
        {
            // DOM-desync recovery at a fresh start (see ReconcileKeyedSync): when the live range is shorter than
            // the keyed baseline, rebuild from the new tree rather than diff by a broken positional invariant.
            // Only on the un-resumed entry — mid-pass resume already holds DOM-consistent state.
            if (state.Phase == KeyedReconcilePhase.Pass1Linear && state.LinearEnd == 0 && state.ResumeIndex == 0
                && TryRebuildDesyncedSlotRange(state.Parent, state.OldNodes, state.NewNodes, state.SlotStart, state.SlotLimit))
            {
                return;
            }

            if (frameBudgetMs > 0)
            {
                _stopwatch ??= new Stopwatch();
                _stopwatch.Restart();
            }

            try
            {
                while (state.Phase != KeyedReconcilePhase.Done)
                {
                    var completed = state.Phase switch
                    {
                        KeyedReconcilePhase.Pass1Linear => Pass1Linear(state, frameBudgetMs),
                        KeyedReconcilePhase.TailAdd => Pass1TailAdd(state, frameBudgetMs),
                        KeyedReconcilePhase.TailRemove => Pass1TailRemove(state, frameBudgetMs),
                        KeyedReconcilePhase.Pass2BuildMap => Pass2BuildMap(state, frameBudgetMs),
                        KeyedReconcilePhase.Pass2Process => Pass2Process(state, frameBudgetMs),
                        KeyedReconcilePhase.Pass2Remove => Pass2Remove(state, frameBudgetMs),
                        KeyedReconcilePhase.Pass2Reorder => Pass2Reorder(state, frameBudgetMs),
                        _ => throw new InvalidOperationException(
                            $"[ChildReconciler] Unhandled KeyedReconcilePhase: {state.Phase}"),
                    };
                    if (!completed) return; // yielded: PendingKeyedState set
                }
                ReleaseKeyedBuffers(state);
            }
            catch
            {
                // On exception, discard pending and ensure buffers are returned.
                PendingKeyedState = null;
                ReleaseKeyedBuffers(state);
                throw;
            }
        }

        #region Pass 1

        private bool Pass1Linear(KeyedReconcileState state, double frameBudgetMs)
        {
            var parent = state.Parent!;
            var oldNodes = state.OldNodes!;
            var newNodes = state.NewNodes!;
            var slotStart = state.SlotStart;
            var commonLength = Math.Min(oldNodes.Length, newNodes.Length);

            for (var i = state.ResumeIndex; i < commonLength; i++)
            {
                if (AbortIfCanceled(state)) return true;

                // Reference-identical VNodes are diff-free; skip the compare + patch (see ReconcileKeyedSync).
                if (!ReferenceEquals(oldNodes[i], newNodes[i]))
                {
                    var oldKey = _keying.EffectiveKey(oldNodes[i]);
                    var newKey = _keying.EffectiveKey(newNodes[i]);
                    if (oldKey != newKey) break;

                    var slot = new ChildSlot { Parent = parent, SlotStart = slotStart, Index = i };
                    if (PatchOrReplaceAtSlot(in slot, oldNodes[i], newNodes[i],
                            slotExists: true))
                    {
                        state.Phase = KeyedReconcilePhase.Done;
                        return true;
                    }
                }
                state.LinearEnd = i + 1;

                var next = i + 1;
                if (next < commonLength && TryYield(state, next, frameBudgetMs)) return false;
            }

            // Branch after Pass 1 completes.
            var linearEnd = state.LinearEnd;
            if (linearEnd == oldNodes.Length && linearEnd == newNodes.Length)
            {
                state.Phase = KeyedReconcilePhase.Done;
                return true;
            }
            if (linearEnd == oldNodes.Length)
            {
                state.Phase = KeyedReconcilePhase.TailAdd;
                state.ResumeIndex = linearEnd;
                return true;
            }
            if (linearEnd == newNodes.Length)
            {
                state.Phase = KeyedReconcilePhase.TailRemove;
                state.ResumeIndex = oldNodes.Length - 1;
                return true;
            }
            RentPass2Buffers(state);
            state.Phase = KeyedReconcilePhase.Pass2BuildMap;
            state.ResumeIndex = linearEnd;
            return true;
        }

        private bool Pass1TailAdd(KeyedReconcileState state, double frameBudgetMs)
        {
            var parent = state.Parent!;
            var newNodes = state.NewNodes!;
            var slotStart = state.SlotStart;

            for (var i = state.ResumeIndex; i < newNodes.Length; i++)
            {
                if (AbortIfCanceled(state)) return true;

                var newElement = _factory.CreateElement(newNodes[i]);
                if (AbortIfCanceled(state)) return true;
                parent.Insert(LogicalChildSlots.ToPhysical(parent, slotStart + i), newElement);

                var next = i + 1;
                if (next < newNodes.Length && TryYield(state, next, frameBudgetMs)) return false;
            }
            state.Phase = KeyedReconcilePhase.Done;
            return true;
        }

        private bool Pass1TailRemove(KeyedReconcileState state, double frameBudgetMs)
        {
            var parent = state.Parent!;
            var slotStart = state.SlotStart;

            for (var i = state.ResumeIndex; i >= state.LinearEnd; i--)
            {
                if (AbortIfCanceled(state)) return true;

                _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));

                var next = i - 1;
                if (next >= state.LinearEnd && TryYield(state, next, frameBudgetMs)) return false;
            }
            state.Phase = KeyedReconcilePhase.Done;
            return true;
        }

        #endregion

        #region Pass 2

        private bool Pass2BuildMap(KeyedReconcileState state, double frameBudgetMs)
        {
            var oldNodes = state.OldNodes!;
            var oldKeyMap = state.OldKeyMap!;
            var orphanedOldIndices = state.OrphanedOldIndices!;

            for (var i = state.ResumeIndex; i < oldNodes.Length; i++)
            {
                if (AbortIfCanceled(state)) return true;

                _keying.RegisterOldKey(oldNodes[i], i, oldKeyMap, orphanedOldIndices);

                var next = i + 1;
                if (next < oldNodes.Length && TryYield(state, next, frameBudgetMs)) return false;
            }

            state.Phase = KeyedReconcilePhase.Pass2Process;
            state.ResumeIndex = state.LinearEnd;
            return true;
        }

        private bool Pass2Process(KeyedReconcileState state, double frameBudgetMs)
        {
            var parent = state.Parent!;
            var newNodes = state.NewNodes!;
            var oldKeyMap = state.OldKeyMap!;
            var usedKeys = state.UsedKeys!;
            var replacedKeys = state.ReplacedKeys!;
            var newElements = state.NewElements!;
            var tables = new KeyedPassTables
            {
                OldKeyMap = oldKeyMap,
                UsedKeys = usedKeys,
                ReplacedKeys = replacedKeys,
                NewElements = newElements,
            };
            var slotStart = state.SlotStart;

            for (var i = state.ResumeIndex; i < newNodes.Length; i++)
            {
                if (AbortIfCanceled(state)) return true;

                // ProcessKeyedNode checks _ctx.IsAborted internally and returns true mid-node on abort; the
                // time-sliced loop owns the state.Phase = Done transition (what AbortIfCanceled does for the
                // loop-boundary checks).
                var slot = new ChildSlot { Parent = parent, SlotStart = slotStart, Index = i };
                if (ProcessKeyedNode(in slot, newNodes[i], in tables))
                {
                    state.Phase = KeyedReconcilePhase.Done;
                    return true;
                }

                var next = i + 1;
                if (next < newNodes.Length && TryYield(state, next, frameBudgetMs)) return false;
            }

            state.Phase = KeyedReconcilePhase.Pass2Remove;
            state.ResumeIndex = state.OldNodes!.Length - 1;
            return true;
        }

        // The LIS is computed synchronously right after this pass finishes, not time-sliced alongside it:
        // ComputeLisAnchors maps each retained element onto its post-removal live DOM position, so every
        // removal in this pass must have already happened or the mapped indices would reflect elements
        // still occupying slots they are about to vacate.
        private bool Pass2Remove(KeyedReconcileState state, double frameBudgetMs)
        {
            var parent = state.Parent!;
            var oldNodes = state.OldNodes!;
            var orphanedOldIndices = state.OrphanedOldIndices!;
            var usedKeys = state.UsedKeys!;
            var replacedKeys = state.ReplacedKeys!;
            var slotStart = state.SlotStart;

            for (var i = state.ResumeIndex; i >= state.LinearEnd; i--)
            {
                if (AbortIfCanceled(state)) return true;

                if (ShouldRemoveOldKeyedEntry(oldNodes[i], i, usedKeys, replacedKeys, orphanedOldIndices))
                {
                    _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                }

                var next = i - 1;
                if (next >= state.LinearEnd && TryYield(state, next, frameBudgetMs)) return false;
            }

            // Remove completed → compute the LIS synchronously and store lisIndices in state.
            ComputeLis(state);
            state.Phase = KeyedReconcilePhase.Pass2Reorder;
            state.ResumeIndex = 0;
            return true;
        }

        // Pass 2 Reorder: re-places only the non-LIS-anchor elements, in ONE indivisible slice, via the same
        // order-faithful right→left neighbour-anchored walk the synchronous paths use (ReorderToNewElementOrder).
        //
        // Anchoring each moved element on its already-placed right-neighbour ELEMENT (not an absolute slot) is
        // what makes a fully ROTATED list reorder correctly: an LIS anchor block stranded at the wrong absolute
        // slots no longer mis-slots a moved element among the anchors. The old time-sliced reorder used an
        // absolute parent.Insert(slotStart + LinearEnd + i, e) walk and reproduced that rotation bug under
        // frame pressure.
        //
        // Unlike the linear/match/remove passes, the reorder runs to completion without yielding. The walk
        // anchors each placement on the element placed just before it (or, for the right-most, on the first
        // sibling past this range), and it inserts freshly created elements that are not yet in the parent —
        // both of which change this fiber's live row count as they go. Yielding mid-reorder would let a sibling
        // fiber sharing the same parent reconcile against a half-rebuilt range, and would require propagating
        // each partial insertion as a slot rebase to every other parked sibling. Completing the reorder
        // atomically sidesteps that entirely (no two siblings are ever parked mid-reorder at once); the
        // linear/remove passes — where the bulk of a large diff's create/patch work lives — still time-slice, so
        // the budget is honoured for the expensive part, and the reorder is pure DOM repositioning of the
        // already-built elements. regionStart = SlotStart + LinearEnd (the keyed region begins after the linear
        // prefix), matching the sync keyed path's ReorderToNewElementOrder(..., slotStart + linearEnd).
        private bool Pass2Reorder(KeyedReconcileState state, double frameBudgetMs)
        {
            if (AbortIfCanceled(state)) return true;

            var regionStart = state.SlotStart + state.LinearEnd;
            _placement.ReorderToNewElementOrder(state.Parent, state.NewElements!, state.LisIndices!, regionStart);

            state.Phase = KeyedReconcilePhase.Done;
            return true;
        }

        #endregion

        #region Buffer Management

        // Computes the LIS synchronously and stores it in state.LisIndices, the only buffer that
        // outlives this call — it is retained until the Reorder phase and returned by
        // ReleaseKeyedBuffers. The scan starts after the linear prefix (slotStart + LinearEnd).
        // One-shot O(N log N) computation; the runtime is short relative to N, so per-VNode yields
        // are unnecessary. Run as a single batch immediately after Pass2Remove completes to finalize
        // the input to the Reorder phase.
        private void ComputeLis(KeyedReconcileState state)
        {
            var parent = state.Parent;
            var slotStart = state.SlotStart;

            var lisIndices = _ctx.BufferPool.RentIntSet();
            state.LisIndices = lisIndices;

            var range = new ChildElementPlacement.PlacementRange
            {
                SlotStart = slotStart,
                ScanStart = slotStart + state.LinearEnd,
                OldLen = state.OldNodes!.Length,
                LogicalNewLen = state.NewNodes!.Length,
            };
            _placement.ComputeLisAnchors(parent!, state.NewElements!, in range, lisIndices);
        }



        // Rents Pass 2's intermediate buffers from the Pool and stores them on state.
        // Called only at the end of Pass1Linear when transition to Pass 2 is confirmed.
        // Skipped when Pass 1 ends via TailAdd / TailRemove / full match, avoiding unnecessary Pool
        // operations in lightweight cases.
        private void RentPass2Buffers(KeyedReconcileState state)
        {
            var pool = _ctx.BufferPool;
            state.OldKeyMap = pool.RentOldKeyMap();
            state.UsedKeys = pool.RentKeySet();
            state.ReplacedKeys = pool.RentReplacedKeySet();
            state.NewElements = pool.RentElementList();
            state.OrphanedOldIndices = pool.RentOrphanedIndexSet();
        }

        private void ReleaseKeyedBuffers(KeyedReconcileState state)
        {
            var pool = _ctx.BufferPool;
            if (state.OldKeyMap != null) { pool.Return(state.OldKeyMap); state.OldKeyMap = null; }
            if (state.UsedKeys != null) { pool.ReturnKeySet(state.UsedKeys); state.UsedKeys = null; }
            if (state.ReplacedKeys != null) { pool.ReturnReplacedKeySet(state.ReplacedKeys); state.ReplacedKeys = null; }
            if (state.NewElements != null) { pool.Return(state.NewElements); state.NewElements = null; }
            if (state.OrphanedOldIndices != null) { pool.ReturnOrphanedIndexSet(state.OrphanedOldIndices); state.OrphanedOldIndices = null; }
            if (state.LisIndices != null) { pool.ReturnIntSet(state.LisIndices); state.LisIndices = null; }
        }

        #endregion

        #region State Machine Helpers

        // If the Abort flag is set, advances phase to Done and returns true.
        // Buffer release runs only once at the dispatcher tail (the abort site only updates phase
        // to avoid the null-guarded ReleaseKeyedBuffers being invoked multiple times from various sites).
        private bool AbortIfCanceled(KeyedReconcileState state)
        {
            if (!_ctx.IsAborted) return false;
            state.Phase = KeyedReconcilePhase.Done;
            return true;
        }

        // On budget overrun, saves pending state and returns true.
        // Callers guarantee that nextResumeIndex stays within the processed range (a pending entry
        // outside the range would spuriously set HasPendingWork to true).
        private bool TryYield(KeyedReconcileState state, int nextResumeIndex, double frameBudgetMs)
        {
            if (frameBudgetMs <= 0) return false;
            // Precondition: ReconcileKeyedFrom initialized _stopwatch when frameBudgetMs > 0.
            // The early return above ensures we only reach this point with frameBudgetMs > 0, so it is non-null.
            if (_stopwatch!.Elapsed.TotalMilliseconds <= frameBudgetMs) return false;
            state.ResumeIndex = nextResumeIndex;
            PendingKeyedState = state;
            _stopwatch.Stop();
            return true;
        }

        #endregion

        #endregion

        #endregion

        #region Helpers

        // IReconcilerHost entry for a Provider value change observed outside inline expansion (a Provider
        // patched in place by FiberNodePatcher). Forwards to the general path, which owns the live
        // context-propagation walk shared with the inline-expansion Provider push.
        internal void NotifyContextValueChange(ContextProviderNode newProvider)
            => _general.NotifyContextValueChange(newProvider);

        // Keyed Pass 2 reads the existing DOM element for a reused key at slotStart + old.index. That holds
        // because Pass 1 only performs in-place updates and never changes parent's child order, so old.index
        // (the oldNodes array index) equals the DOM index minus slotStart; Pass 2's PatchNode recursion adds
        // or removes no direct children of parent, so the mapping stays stable. The boundary check below is
        // all that needs asserting — type consistency between the DOM element and oldNodes[old.index] is
        // guaranteed because a same-key replacement (Pass 1 CanPatch=false) remove+inserts at the same index,
        // and a Pass-2 CanPatch=false routes through replacedKeys + CreateElement rather than patching the
        // stale element as the new type. Both the synchronous and time-sliced keyed paths gate on this.
        private static void AssertDomIndexInvariant(int slotStart, int oldIndex, VisualElement parent)
        {
            // The logical count, not a physical one, so an invisible child cannot loosen the bound and mask a
            // real Pass-1 ordering violation. Computed INSIDE the Debug.Assert arguments rather than into a
            // local: Debug.Assert is [Conditional("UNITY_ASSERTIONS")], so the whole O(childCount) walk is
            // compiled out of a player build along with the call. A local would have run in every build.
            UnityEngine.Debug.Assert(
                slotStart + oldIndex < LogicalChildSlots.Count(parent),
                $"[ChildReconciler] DOM index invariant violated: slotStart + old.index={slotStart + oldIndex} "
                + $">= renderedChildCount={LogicalChildSlots.Count(parent)}. "
                + "Pass 1 may have modified parent's child order.");
        }

        // Processes one new-side keyed node: matches it against oldKeyMap and patches the existing element in
        // place, or creates a replacement (CanPatch=false) / a brand-new element (no match), appending the
        // result to newElements. Returns true when the reconcile was aborted mid-node (after a patch/create) so
        // the caller must stop. Shared by the synchronous (ReconcileKeyedSync) and time-sliced (Pass2Process)
        // keyed Pass-2 loops so their per-node match/patch/create semantics cannot drift; each loop keeps only
        // its own abort-handling and TryYield scaffolding around this call.
        // NOT folded into PatchOrReplaceAtSlot despite the same CanPatch gate: Pass 2 defers old-element
        // removal (recorded in replacedKeys, actually removed by a later reverse pass) instead of removing
        // eagerly, appends to a newElements list instead of inserting at a live DOM index, and re-fetches
        // the patched element to observe a WrapElement swap — three real differences in shape, not just
        // naming, so forcing this through the same helper would need as many bool flags as it has callers.
        private bool ProcessKeyedNode(in ChildSlot slot, VNode? newNode, in KeyedPassTables tables)
        {
            var parent = slot.Parent;
            var slotStart = slot.SlotStart;
            var newElements = tables.NewElements;
            var key = _keying.ReconcileKey(newNode, slot.Index);

            if (tables.OldKeyMap.TryGetValue(key, out var old))
            {
                if (!tables.UsedKeys.Add(key))
                {
                    // A second new-side sibling resolved the same old entry. Re-matching would alias
                    // two logical rows onto one element (the reorder pass then collapses them,
                    // silently dropping a row) or, on a type swap, retroactively remove the element
                    // the first occurrence patched. Mirror the old-side duplicate guard: warn and
                    // mount a fresh element so every declared row commits.
                    FiberLogger.LogWarning("ChildReconciler",
                        $"Duplicate key detected among new siblings: {key}. " +
                        "The repeated sibling mounts a fresh element; give each sibling a unique key.");
                    var duplicateElement = _factory.CreateElement(newNode);
                    if (_ctx.IsAborted) return true;
                    UnityEngine.Debug.Assert(duplicateElement.parent == null,
                        "[ChildReconciler] _factory.CreateElement must return an orphaned VisualElement (no parent).");
                    newElements.Add((duplicateElement, false));
                    return false;
                }
                AssertDomIndexInvariant(slotStart, old.index, parent);
                var existingDomElement = parent.ElementAt(LogicalChildSlots.ToPhysical(parent, slotStart + old.index));

                if (ReconcileKeying.CanPatch(old.node, newNode))
                {
                    // Reference-identical VNodes are diff-free; reuse the existing element without patching.
                    // A same-instance node cannot trigger a WrapElement swap, so no re-fetch is needed.
                    if (!ReferenceEquals(old.node, newNode))
                    {
                        var actualElement = _patcher.ResolveWrapped(existingDomElement);
                        _patcher.PatchNode(actualElement, old.node, newNode);
                        if (_ctx.IsAborted) return true;
                        // Re-fetch after PatchNode: a WrapElement wrapper swap may change the DOM element
                        // reference at this index.
                        existingDomElement = parent.ElementAt(LogicalChildSlots.ToPhysical(parent, slotStart + old.index));
                    }
                    newElements.Add((existingDomElement, true));
                }
                else
                {
                    tables.ReplacedKeys.Add(key);
                    var newElement = _factory.CreateElement(newNode);
                    if (_ctx.IsAborted) return true;
                    UnityEngine.Debug.Assert(newElement.parent == null,
                        "[ChildReconciler] _factory.CreateElement must return an orphaned VisualElement (no parent).");
                    newElements.Add((newElement, false));
                }
            }
            else
            {
                var newElement = _factory.CreateElement(newNode);
                if (_ctx.IsAborted) return true;
                UnityEngine.Debug.Assert(newElement.parent == null,
                    "[ChildReconciler] _factory.CreateElement must return an orphaned VisualElement (no parent).");
                newElements.Add((newElement, false));
            }

            return false;
        }

        // True when the old entry at index i is not retained by the new tree and must be removed: it was
        // orphaned by a duplicate key, its key was consumed by no new node, or its key was consumed but
        // replaced because CanPatch returned false. Shared by both keyed Pass-2 reverse-removal loops.
        private bool ShouldRemoveOldKeyedEntry(
            VNode? oldNode, int i, HashSet<ChildKey> usedKeys, HashSet<ChildKey> replacedKeys,
            HashSet<int> orphanedOldIndices)
        {
            var key = _keying.ReconcileKey(oldNode, i);
            return orphanedOldIndices.Contains(i)
                || !usedKeys.Contains(key)
                || replacedKeys.Contains(key);
        }

        #endregion
    }
}
