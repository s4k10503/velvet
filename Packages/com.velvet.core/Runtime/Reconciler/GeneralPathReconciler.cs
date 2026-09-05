#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The general (inline-expansion + live-context commit) reconcile path. When a child container holds a
    // ComponentNode / Provider / Fragment / Suspense / Memo / AnimatePresence (anything wrapper-less or
    // rendering), the flat Indexed/Keyed fast path cannot apply: this collaborator walks the new tree once
    // under live context, expanding transparent nodes inline into the parent's slot range, rendering each
    // Component in-scope of its ancestor Providers, and committing each emitted host leaf (CreateElement /
    // PatchNode) before the removals and the LIS reorder run in FinalizeGeneralCommit. The old side is
    // reproduced structurally (no render) so the diff matches the previously committed leaf order. Suspense
    // and AnimatePresence are expanded wrapper-less here, including their suspend/rollback and enter/exit
    // ghost machinery — the ghost-leak-sensitive core. Identity keys + patch-compatibility resolve through
    // the shared ReconcileKeying; the AnimatePresence variant-exit resolution lives here (TryResolveVariantExit).
    internal sealed class GeneralPathReconciler
    {
        private readonly ReconcilerContext _ctx;
        private readonly FiberNodePatcher _patcher;
        private readonly FiberNodeFactory _factory;
        private readonly FiberElementCleaner _cleaner;
        // Shared with the keyed/indexed fast path: LIS anchor computation + non-anchor re-placement.
        private readonly ChildElementPlacement _placement;
        // Shared identity-key resolution (same instance the fast path uses).
        private readonly ReconcileKeying _keying;

        public GeneralPathReconciler(ReconcilerContext ctx, FiberNodePatcher patcher,
            FiberNodeFactory factory, FiberElementCleaner cleaner, ChildElementPlacement placement,
            ReconcileKeying keying)
        {
            _ctx = ctx;
            _patcher = patcher;
            _factory = factory;
            _cleaner = cleaner;
            _placement = placement;
            _keying = keying;
        }

        #region Commit

        // Per-call state for the general (expansion) reconcile path. The new-side live-context walk
        // commits each emitted leaf — CreateElement / PatchNode while the enclosing
        // Providers are still pushed on the live ComponentContextStack — so an element
        // descendant (and any Component nested inside it) renders in-scope of its ancestor Providers
        // without a pre-captured snapshot. The committed VEs are recorded here in new order and the
        // post-walk FinalizeGeneralCommit performs the removals and the LIS reorder
        // (neither needs live context). This is a depth-first descent that reconciles + renders
        // each node under live context, then places its host element on the way back up; the
        // flat fast path (pure-leaf containers) keeps the time-sliced Indexed/Keyed machinery.
        internal sealed class GeneralCommitState
        {
            public VisualElement? Parent;
            public int SlotStart;
            public VNode?[]? OldNodes;
            public Dictionary<ChildKey, (int index, VNode? node)> OldKeyMap = null!;
            public HashSet<ChildKey> UsedKeys = null!;
            public HashSet<ChildKey> ReplacedKeys = null!;
            public HashSet<int> OrphanedOldIndices = null!;
            public List<(VisualElement? element, bool isExisting)> NewElements = null!;
            // Key committed for each NewElements entry (parallel list), so a
            // speculative subtree (Suspense primary) can be rolled back on suspend.
            public List<ChildKey> CommittedKeys = null!;
            public int NewIndex;
        }

        // Runs effect cleanups for fibers present on the old side but absent on the new side
        // (orphans), before any DOM removal. Scoped to this reconcile call's expansion.
        internal void RunOrphanEffectCleanups(
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers)
        {
            if (oldFibers.Count == 0) return;
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) FiberEffects.RunOrphanFiberEffectCleanups(fiber);
            }
        }

        // The three tables whose contents are meaningful only within one Reconcile call: they are rented
        // together at its start and returned together at its end, and the orphan diff (old \ new) is valid
        // only over a matched pair. Scoping them as one value is what keeps a sibling fiber's re-render —
        // which reconciles under a different parent and slot range — from falsely orphaning fibers it never
        // walked. Taken by `in`, since a reconcile pass must not allocate to hand them over.
        internal readonly struct InlinePairing
        {
            internal List<ComponentFiber> OldFibers { get; init; }
            internal HashSet<ComponentFiber> NewFibers { get; init; }
            internal ProviderPairTable OldProviders { get; init; }
        }

        // Fully disposes orphan fibers (old-side, absent on the new side) and unregisters
        // them. Effect cleanups already ran via RunOrphanEffectCleanups.
        internal void SweepOrphans(
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers)
        {
            if (oldFibers.Count == 0) return;
            foreach (var fiber in oldFibers)
            {
                if (!newFibers.Contains(fiber)) _ctx.ComponentRegistry.DisposeAndRemove(fiber);
            }
        }

        // Reconcile entry for a container whose new children require inline expansion (they contain a
        // ComponentNode / ContextProviderNode / FragmentNode / SuspenseNode / MemoNode). The new tree
        // is walked once under live context: Providers push (and stay pushed through the subtree),
        // Components render, and each emitted host leaf is matched against oldNodes
        // and committed via CommitLeaf while the live stack still reflects its ancestor
        // Providers. Removals and the LIS reorder run afterwards in FinalizeGeneralCommit.
        // Returns whether the removal pass ran, which an abort raised anywhere earlier in the pass skips.
        internal bool ReconcileGeneral(
            VisualElement? parent,
            VNode?[] oldNodes,
            VNode?[] newChildren,
            int slotStart,
            in InlinePairing pairing)
        {
            var oldFibers = pairing.OldFibers;
            var newFibers = pairing.NewFibers;
            var oldProviders = pairing.OldProviders;
            var pool = _ctx.BufferPool;
            var commit = new GeneralCommitState
            {
                Parent = parent,
                SlotStart = slotStart,
                OldNodes = oldNodes,
                OldKeyMap = pool.RentOldKeyMap(),
                UsedKeys = pool.RentKeySet(),
                ReplacedKeys = pool.RentReplacedKeySet(),
                OrphanedOldIndices = pool.RentOrphanedIndexSet(),
                NewElements = pool.RentElementList(),
                CommittedKeys = new List<ChildKey>(),
                NewIndex = 0,
            };
            try
            {
                // Build the old-key → (domIndex, node) map. Duplicate keys register the earlier index
                // as orphaned (it will be removed) — mirrors ReconcileKeyedSync's Pass 2 BuildMap.
                for (var i = 0; i < oldNodes.Length; i++)
                {
                    _keying.RegisterOldKey(oldNodes[i], i, commit.OldKeyMap, commit.OrphanedOldIndices);
                }

                // Live-context walk: emit + commit each new leaf under its ancestor Providers.
                var prevFlag = _ctx.ContextValueChanged;
                var walk = pool.RentInlineWalk();
                walk.IsNewSide = true;
                walk.Parent = parent;
                walk.SlotStart = slotStart;
                walk.OldFibers = oldFibers;
                walk.NewFibers = newFibers;
                walk.OldProvidersForPairing = oldProviders;
                walk.Commit = commit;
                try
                {
                    ExpandInlineRecursive(walk, newChildren, FiberKeying.WalkRoot);
                }
                finally
                {
                    _ctx.ContextValueChanged = prevFlag;
                    pool.ReturnInlineWalk(walk);
                }

                // Orphan effect cleanups run BEFORE the DOM-removal pass (Finalize → RemoveElement),
                // mirroring the flat path: a deleted FunctionComponent's effect cleanups fire while its
                // Ref.Current is still valid, then the DOM is removed. The sweep (full dispose) runs after.
                RunOrphanEffectCleanups(oldFibers, newFibers);
                var removalsRan = !_ctx.IsAborted;
                if (removalsRan) FinalizeGeneralCommit(commit);
                SweepOrphans(oldFibers, newFibers);
                return removalsRan;
            }
            finally
            {
                pool.Return(commit.OldKeyMap);
                pool.ReturnKeySet(commit.UsedKeys);
                pool.ReturnReplacedKeySet(commit.ReplacedKeys);
                pool.ReturnOrphanedIndexSet(commit.OrphanedOldIndices);
                pool.Return(commit.NewElements);
            }
        }

        // Matches one emitted new leaf against the old leaves and commits it in place under the live
        // context: an existing element of the same identity is patched (its children reconcile via
        // PatchCommon while ancestor Providers are still pushed), otherwise a fresh element is
        // created (its children reconcile via CreateElement under the same live context). The
        // element is recorded in GeneralCommitState.NewElements in new order; its final
        // placement is decided by FinalizeGeneralCommit. Existing elements stay at their
        // old DOM position (PatchNode preserves parent child order), created elements are orphans.
        private void CommitLeaf(VNode? node, GeneralCommitState commit)
        {
            var parent = commit.Parent!;
            var slotStart = commit.SlotStart;
            var newIdx = commit.NewIndex++;
            var key = _keying.ReconcileKey(node, newIdx);

            // Old leaf i is committed at parent.children[slotStart + i] (the previous render placed
            // leaves in expansion order; patches stay in place and creates are orphans, so the bound
            // holds throughout the walk). The childCount guard degrades a stale oldNodes/DOM mismatch
            // (e.g. after a prior aborted/suspended commit) to a fresh create instead of throwing
            // IndexOutOfRange — the time-sliced keyed path asserts this invariant; the general path
            // can be re-entered mid-suspend so it guards defensively.
            var oldMatched = commit.OldKeyMap.TryGetValue(key, out var old)
                && LogicalChildSlots.TryGetPhysical(parent, slotStart + old.index, out _);
            if (oldMatched && commit.UsedKeys.Contains(key))
            {
                // A second new-side sibling resolved the same old entry its first occurrence already
                // claimed: re-matching would alias two rows onto one element or retroactively remove
                // the patched one via ReplacedKeys. Mirror the old-side duplicate guard: warn and
                // fall through to a fresh create so every declared row commits.
                FiberLogger.LogWarning("GeneralPathReconciler",
                    $"Duplicate key detected among new siblings: {key}. " +
                    "The repeated sibling mounts a fresh element; give each sibling a unique key.");
                oldMatched = false;
            }
            if (oldMatched)
            {
                var existingDom = parent.ElementAt(LogicalChildSlots.ToPhysical(parent, slotStart + old.index));
                if (ReconcileKeying.CanPatch(old.node, node))
                {
                    var actual = _patcher.ResolveWrapped(existingDom);
                    _patcher.PatchNode(actual, old.node, node);
                    if (_ctx.IsAborted) return;
                    // Re-fetch: a WrapElement wrapper swap may change the element reference at this index.
                    existingDom = parent.ElementAt(LogicalChildSlots.ToPhysical(parent, slotStart + old.index));
                    commit.NewElements.Add((existingDom, true));
                    // Mark the old key consumed only AFTER the patch succeeds. PatchNode can re-enter a
                    // child reconcile that throws FiberSuspendSignal (a suspending descendant) or set
                    // IsAborted; recording UsedKeys before that would leave a stale entry that survives
                    // RollbackCommitTo (which un-uses only keys recorded in CommittedKeys) and wrongly
                    // suppress the old element's removal, leaving primary content beside the fallback.
                    commit.UsedKeys.Add(key);
                }
                else
                {
                    var newElement = _factory.CreateElement(node);
                    commit.NewElements.Add((newElement, false));
                    // Type-swap (old element removed, new created). Recorded after CreateElement
                    // succeeds, for the same suspend/abort safety as the patch branch above.
                    commit.UsedKeys.Add(key);
                    commit.ReplacedKeys.Add(key);
                }
            }
            else
            {
                var newElement = _factory.CreateElement(node);
                commit.NewElements.Add((newElement, false));
            }
            // Parallel to the single NewElements entry added on every committed (non-throwing,
            // non-aborted) path, so a speculative subtree (Suspense primary) can roll its commits
            // back on suspend.
            commit.CommittedKeys.Add(key);
        }

        // Emits one expanded leaf: commits it in place under live context (general path) or collects
        // it into the flat structural result (old-side / fast-path expansion).
        private void Emit(VNode? node, List<VNode>? result, GeneralCommitState? commit)
        {
            if (commit != null) CommitLeaf(node, commit);
            else if (node != null) result!.Add(node);
        }

        // Rolls a speculative subtree's commits back to preCount entries. A
        // created orphan element (isExisting == false) was never placed and is not reached by
        // FinalizeGeneralCommit, so its poolable leaves are reclaimed via
        // FiberElementCleaner.ReturnRolledBackOrphan; a patched existing element's key
        // is un-used so FinalizeGeneralCommit removes it (the discarded subtree — a
        // suspended Suspense primary — is replaced by the fallback). A container orphan (e.g.
        // V.Div) is dropped (GC) — its DOM was never placed — but Velvet inline-expands a
        // ComponentNode in its children into a registered fiber whose MountPoint is the orphan
        // container, so the fiber would linger in ComponentRegistry with effects
        // queued against a dead VE. The fibers added during this speculative span that mounted
        // onto a created orphan VE are disposed here so their effect cleanup fires and the
        // registry entry is freed; a later resolve recreates them cleanly. Fibers mounted onto the
        // parent's children directly (wrapper-less inline at the suspended slot, not nested under
        // a created container) are retained — those slots are re-filled by the fallback / by the
        // later resolve's re-expansion, which reuses the retained subtree.
        // GeneralCommitState.NewIndex rewinds so the replacement subtree re-emits at
        // the same flat positions.
        private void RollbackCommitTo(GeneralCommitState commit, int preCount,
            HashSet<ComponentFiber>? fibersBefore = null,
            HashSet<ComponentFiber>? newFibers = null)
        {
            var orphanContainers = CollectOrphanContainers(commit, preCount);
            if (orphanContainers != null)
            {
                if (newFibers != null)
                {
                    List<ComponentFiber>? drop = null;
                    foreach (var f in newFibers)
                    {
                        if (fibersBefore != null && fibersBefore.Contains(f)) continue;
                        if (f == null || f.MountPoint == null) continue;
                        if (IsInsideOrphan(f.MountPoint, orphanContainers))
                        {
                            (drop ??= new List<ComponentFiber>()).Add(f);
                        }
                    }
                    if (drop != null)
                    {
                        foreach (var f in drop) newFibers.Remove(f);
                    }
                }
                _ctx.ComponentRegistry.DisposeFibersUnder(orphanContainers);
            }
            for (var i = commit.NewElements.Count - 1; i >= preCount; i--)
            {
                var key = commit.CommittedKeys[i];
                commit.UsedKeys.Remove(key);
                commit.ReplacedKeys.Remove(key);
                var (element, isExisting) = commit.NewElements[i];
                if (!isExisting)
                {
                    _cleaner.ReturnRolledBackOrphan(element);
                }
            }
            commit.NewElements.RemoveRange(preCount, commit.NewElements.Count - preCount);
            commit.CommittedKeys.RemoveRange(preCount, commit.CommittedKeys.Count - preCount);
            commit.NewIndex = preCount;
        }

        // A created container orphan (e.g. V.Div) reconciled its declared children during CreateElement, so
        // a Component child of that container is registered in ComponentRegistry with its MountPoint
        // pointing into the (about-to-be-dropped) orphan subtree — including fibers that were registered by
        // an INNER ReconcileChildren call and therefore are NOT in the rolling-back scope's `newFibers` set.
        // The caller disposes every inline fiber whose MountPoint sits inside the returned range so its
        // effect cleanup runs and the deferred layout-effect drain short-circuits via IsDisposed.
        // Every created orphan enters the set, with no attempt to exclude the ones that cannot hold a fiber.
        // Excluding by element type was wrong twice over — a subclass and the type itself both reach here via
        // V.Custom<T>, which declares children for any T — and the question is unanswerable from the element
        // in any case, since what decides it is whether the NODE declared children. The set is a containment
        // filter, so a surplus member costs a hash entry and changes nothing: a leaf holding no fiber
        // contributes no fiber to dispose. The exact-type dispatch in FiberElementCleaner.ReturnToPool asks a
        // different question — may this element enter the shared pool — and stays a type test.
        private static HashSet<VisualElement>? CollectOrphanContainers(GeneralCommitState commit, int preCount)
        {
            HashSet<VisualElement>? orphanContainers = null;
            for (var i = preCount; i < commit.NewElements.Count; i++)
            {
                var (element, isExisting) = commit.NewElements[i];
                if (isExisting || element == null) continue;
                (orphanContainers ??= new HashSet<VisualElement>()).Add(element);
            }
            return orphanContainers;
        }

        private static bool IsInsideOrphan(
            VisualElement mountPoint,
            HashSet<VisualElement> orphanContainers)
        {
            for (var ve = mountPoint; ve != null; ve = ve.parent)
            {
                if (orphanContainers.Contains(ve)) return true;
            }
            return false;
        }

        // Removes old leaves not reused by the walk, then re-places the committed elements into
        // [slotStart, slotStart + NewElements.Count) with the minimum number of DOM moves via
        // a patience-sort LIS (anchors stay put). Mirrors the removal + LIS reorder tail of
        // ReconcileKeyedSync with linearEnd == 0 (the live-context walk performs
        // no linear prefix pass — all matching happened in CommitLeaf).
        private void FinalizeGeneralCommit(GeneralCommitState commit)
        {
            var parent = commit.Parent!;
            var slotStart = commit.SlotStart;
            var oldNodes = commit.OldNodes!;
            var newElements = commit.NewElements;

            // Removal (reverse so not-yet-visited indices stay valid).
            for (var i = oldNodes.Length - 1; i >= 0; i--)
            {
                var key = _keying.ReconcileKey(oldNodes[i], i);
                if (commit.OrphanedOldIndices.Contains(i)
                    || !commit.UsedKeys.Contains(key)
                    || commit.ReplacedKeys.Contains(key))
                {
                    _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                }
            }

            // LIS reorder over the post-removal DOM positions. linearEnd == 0 here (the live-context
            // walk performs no linear prefix pass), so the region begins at slotStart.
            var range = new ChildElementPlacement.PlacementRange
            {
                SlotStart = slotStart,
                ScanStart = slotStart,
                OldLen = oldNodes.Length,
                LogicalNewLen = newElements.Count,
            };
            _placement.ComputeAnchorsAndReorder(parent, newElements, in range);
        }

        #endregion

        #region Inline expansion

        // Rented from ReconcilerBufferPool once per outer walk and returned at that walk's exit, never
        // per recursion.
        //
        // Do NOT hold a single cached instance on this class: the walk re-enters itself through the commit
        // (CommitLeaf -> PatchNode -> ReconcileChildren -> ChildReconciler.Reconcile -> back here), so the
        // nested walk would overwrite the outer one's fields mid-descent.
        //
        // Do NOT fold this into GeneralCommitState either: the old-side structural walk runs with a null
        // Commit and still needs every other field here.
        //
        // Providers is read only on the old-side branch and OldProvidersForPairing only on the new-side
        // branch, and no recursion flips IsNewSide — so one copy of each per walk is exact. ReconcileGeneral
        // cannot break that by construction: it always walks the new side and has no old-side Providers
        // parameter to hand over. ExpandInlineForReconcile takes both tables from its caller, so that is the
        // entry carrying the runtime check.
        internal sealed class InlineWalk
        {
            // Flat structural output (old-side / fast-path expansion). Null on the general commit path,
            // where Commit is what receives each emitted leaf instead.
            public List<VNode>? Result;
            public bool IsNewSide;
            public VisualElement? Parent;
            public int SlotStart;
            public List<ComponentFiber> OldFibers = null!;
            public HashSet<ComponentFiber> NewFibers = null!;
            // Filled by a Suspense expansion that suspended and read by every enclosing one in this walk,
            // which leaves those fibers' marks alone: NewFibers is one set for the whole walk, so an
            // enclosing delta contains a nested Suspense's primary subtree, and an enclosing Suspense
            // resolving is not the inner one resolving. Held on the walk rather than rented per expansion,
            // since the enclosing loop reads it after the inner one has returned its own buffers.
            public readonly HashSet<ComponentFiber> OffscreenPrimaries = new();
            public ProviderPairTable? Providers;
            public ProviderPairTable? OldProvidersForPairing;
            public GeneralCommitState? Commit;
            // Expansion-order position of the next new-side Provider, the fallback pairing for a Provider
            // whose structural position has no counterpart on the old side. Monotonic for the whole walk:
            // nothing rewinds it, including a Suspense rollback that discards a primary subtree it has
            // already advanced through — matching the old side, which reproduces exactly one branch.
            public int NewProviderOrdinal;

            // Every field a walk may set is scrubbed here: a stale reference surviving into the next
            // rent would silently splice one walk's destination or fiber accumulators into another's.
            public void Clear()
            {
                Result = null;
                IsNewSide = false;
                Parent = null;
                SlotStart = 0;
                OldFibers = null!;
                NewFibers = null!;
                Providers = null;
                OldProvidersForPairing = null;
                Commit = null;
                NewProviderOrdinal = 0;
                OffscreenPrimaries.Clear();
            }
        }

        // The old side's Providers, recorded two ways so the new side can prefer the precise pairing without
        // losing the one it replaces.
        //
        // ByPosition is the primary: a Provider is compared against whatever held its structural position last
        // render, which is unaffected by Providers appearing or disappearing elsewhere in the walk.
        //
        // InWalkOrder is the fallback, and is exactly the pairing that predates ByPosition. A structural
        // position is not stable across everything: an unkeyed Provider's own contribution is its sibling
        // index, so `cond ? new[]{ p } : new[]{ banner, p }` moves it even though the Provider sequence itself
        // never changed. Falling back to walk order there keeps that case pairing as it always did, and makes
        // the whole scheme monotone — a position hit can only be more accurate than the ordinal it replaces,
        // and a position miss is never worse than the behavior it replaced. An explicit key on the Provider
        // pins its OWN contribution through such a shift, but not the levels above it: an unkeyed Fragment or
        // Component that moves takes the key's subtree with it.
        internal sealed class ProviderPairTable
        {
            private readonly Dictionary<ProviderPairKey, ContextProviderNode> _byPosition = new();
            private readonly List<ContextProviderNode> _inWalkOrder = new();

            public void Record(ProviderPairKey key, ContextProviderNode provider)
            {
                // Two Providers DO share a position when siblings share one explicit key: a key replaces the
                // node index, so the pair is genuinely indistinguishable here (and, far more remotely, on a
                // path hash collision). Keeping the first is what walk order would have done; the second then
                // pairs by walk order instead, and its consumers are re-notified every reconcile while the two
                // values differ. Unlike the duplicate-key guards in the leaf and ComponentNode branches, this
                // one does not warn: the duplicate is only visible from the OLD side here, whereas those warn
                // where the repeated sibling is emitted.
                _byPosition.TryAdd(key, provider);
                _inWalkOrder.Add(provider);
            }

            public ContextProviderNode? Match(ProviderPairKey key, int walkOrdinal)
            {
                if (_byPosition.TryGetValue(key, out var atPosition)) return atPosition;
                return walkOrdinal < _inWalkOrder.Count ? _inWalkOrder[walkOrdinal] : null;
            }

            public void Clear()
            {
                _byPosition.Clear();
                _inWalkOrder.Clear();
            }
        }

        // A node type that forces the live-context inline-expansion slow path: a ComponentNode renders, a
        // Provider/Fragment is transparent, a Suspense/Memo/AnimatePresence expands inline (wrapper-less),
        // and a null is filtered. Both the fast/slow routing in NeedsExpansion and the early-out inside
        // ExpandInlineForReconcile gate on this single predicate, so the two cannot drift out of lockstep.
        private static bool RequiresInlineExpansion(VNode? n)
            => n is FragmentNode or ContextProviderNode or ComponentNode or SuspenseNode or MemoNode or AnimatePresenceNode or null;

        // Whether nodes contains a node type that requires the inline-expansion walk. When false the
        // container is a flat list of host leaves and takes the fast path (the time-sliced Indexed/Keyed diff).
        internal static bool NeedsExpansion(VNode?[] nodes)
        {
            if (nodes == null) return false;
            foreach (var n in nodes)
            {
                if (RequiresInlineExpansion(n))
                {
                    return true;
                }
            }
            return false;
        }

        // Expansion variant invoked by Reconcile that inlines wrapper-less node types
        // (ContextProviderNode, FragmentNode) into the flat VNode array consumed by the
        // Indexed/Keyed reconciler. Old-side (isNewSide=false) is structural:
        // it walks the input tree without pushing context onto the live stack, recording each Provider under
        // its ProviderPairKey. New-side (isNewSide=true) pushes each Provider's value onto the stack, then —
        // while the value is still pushed — pairs against the old Provider that held the same position via
        // oldProvidersForPairing and dispatches NotifyContextChanged when the
        // value changed; finally pops. The push → notify → recurse → pop order guarantees the
        // propagated snapshot includes the new value.
        internal VNode?[] ExpandInlineForReconcile(
            VNode?[] nodes,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            ProviderPairTable? providers = null,
            ProviderPairTable? oldProvidersForPairing = null)
        {
            AssertProviderTableMatchesSide(isNewSide, providers, oldProvidersForPairing);

            if (nodes == null || nodes.Length == 0) return Array.Empty<VNode>();

            // Fast path: no inline expansion required. ComponentNode is always expanded inline
            // (function components emit no DOM element), so its presence forces
            // the slow path even when no Fragment / Provider exists. SuspenseNode is expanded inline
            // too (wrapper-less: its children/fallback are spliced into the parent's slot range), so a
            // top-level Suspense — e.g. a boundary fiber whose body is just <Suspense> being re-rendered
            // through its own Reconcile — must take the slow path; otherwise the Suspense is treated as
            // an opaque leaf and never swaps fallback↔children.
            var needsExpand = false;
            foreach (var n in nodes)
            {
                if (RequiresInlineExpansion(n))
                {
                    needsExpand = true;
                    break;
                }
            }
            if (!needsExpand) return nodes;

            var buffer = _ctx.BufferPool.RentNodeList();
            var prevFlag = _ctx.ContextValueChanged;
            var walk = _ctx.BufferPool.RentInlineWalk();
            walk.Result = buffer;
            walk.IsNewSide = isNewSide;
            walk.Parent = parent;
            walk.SlotStart = slotStart;
            walk.OldFibers = oldFibers;
            walk.NewFibers = newFibers;
            walk.Providers = providers;
            walk.OldProvidersForPairing = oldProvidersForPairing;
            try
            {
                ExpandInlineRecursive(walk, nodes, FiberKeying.WalkRoot);
                return buffer.Count == 0 ? Array.Empty<VNode>() : buffer.ToArray();
            }
            finally
            {
                if (isNewSide) _ctx.ContextValueChanged = prevFlag;
                _ctx.BufferPool.ReturnNodeList(buffer);
                _ctx.BufferPool.ReturnInlineWalk(walk);
            }
        }

        // The walk keeps one Providers / OldProvidersForPairing pair for its whole descent, which is exact
        // only because each table is consumed on exactly one side (Providers collects old-side Providers;
        // OldProvidersForPairing is read when a new-side Provider pushes) and no recursion flips the side.
        // A caller that hands in the off-side table is expressing an intent this walk silently drops, so
        // fail loudly here rather than at the far end of a diff that quietly used the wrong Providers.
        // Checked ahead of the empty-input and flat-leaf early-outs: the caller's intent is just as wrong
        // when the container happens to hold nothing that needs expanding.
        private static void AssertProviderTableMatchesSide(
            bool isNewSide,
            ProviderPairTable? providers,
            ProviderPairTable? oldProvidersForPairing)
        {
            UnityEngine.Debug.Assert(
                isNewSide ? providers == null : oldProvidersForPairing == null,
                "[Velvet] GeneralPathReconciler.ExpandInlineForReconcile: the new side only reads "
                + "oldProvidersForPairing and the old side only fills providers; the table for the other "
                + "side is never consumed.");
        }

        private void ExpandInlineRecursive(
            InlineWalk walk,
            VNode?[] nodes,
            WalkPosition position)
        {
            var result = walk.Result;
            var isNewSide = walk.IsNewSide;
            var commit = walk.Commit;

            for (var nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                var node = nodes[nodeIndex];
                switch (node)
                {
                    case null:
                        continue;
                    case FragmentNode fragment:
                        if (fragment.Children != null)
                        {
                            var childPosition = FiberKeying.FragmentChild(
                                position, fragment.Key, nodeIndex);
                            ExpandInlineRecursive(walk, fragment.Children, childPosition);
                        }
                        break;
                    case ContextProviderNode provider when !isNewSide:
                    {
                        // Recorded BEFORE the descent, so the walk order the new side counts in places an
                        // enclosing Provider ahead of the ones it wraps.
                        walk.Providers?.Record(
                            FiberKeying.ProviderPosition(
                                _ctx.FiberStack.Current, position, provider.Key, nodeIndex),
                            provider);
                        if (provider.Children != null)
                        {
                            var childPosition = FiberKeying.ProviderChild(
                                position, provider.Key, nodeIndex);
                            ExpandInlineRecursive(walk, provider.Children, childPosition);
                        }
                        break;
                    }
                    case ContextProviderNode provider:
                        ExpandNewSideProvider(walk, provider, position, nodeIndex);
                        break;
                    case ComponentNode component:
                        ExpandComponentInline(walk, component, position, nodeIndex);
                        break;
                    case OutletNode:
                        // Wrapper-emitting node: CreateElement(Outlet) / PatchNode(Outlet) resolve the
                        // matched route during this walk's commit (live context), reading
                        // RouterContext.Location / Depth from the live stack and pushing Depth+1 around
                        // the route Component's mount. No pre-captured snapshot / owner is needed.
                        _keying.RegisterScopedKey(node, position.Scope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                    case MemoNode memo:
                        // Memo emits no DOM: resolve its inner via the dep cache and
                        // expand it inline so a Suspense / Component / Provider inner is handled
                        // wrapper-less in the parent's slot range. The inner renders in live context
                        // (the enclosing Provider is still pushed on the new side), so no pre-captured
                        // snapshot is needed.
                        ExpandMemoInline(walk, memo, position, nodeIndex);
                        break;
                    case SuspenseNode suspense:
                        ExpandSuspenseInline(walk, suspense, position, nodeIndex);
                        break;
                    case AnimatePresenceNode presence:
                        // DOM-less: AnimatePresence emits no wrapper. Its keyed children expand directly
                        // into the parent's slot range (so the parent's flex / wrap / gap reach them), with
                        // enter / exit / stagger played on each keyed child's anchor element. Old/new sides
                        // are reproduced from the per-boundary presence state, mirroring ExpandSuspenseInline.
                        // Depth marker: Motion nodes created while a presence expansion is on the
                        // stack are presence-managed (initial/exit are live); a standalone Motion
                        // mount sees depth 0 and warns that those props are inert.
                        _ctx.PresenceExpansionDepth++;
                        try
                        {
                            ExpandAnimatePresenceInline(walk, presence, position, nodeIndex);
                        }
                        finally
                        {
                            _ctx.PresenceExpansionDepth--;
                        }
                        break;
                    case BaseElementNode:
                        // Regular element: CreateElement / PatchNode reconciles its children via the
                        // host's ReconcileChildren during this walk's commit, so descendant Components
                        // render in-scope of their ancestor Providers without a pre-captured snapshot.
                        _keying.RegisterScopedKey(node, position.Scope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                    default:
                        _keying.RegisterScopedKey(node, position.Scope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                }
            }
        }

        // New side: push value first so the notification snapshot includes it.
        private void ExpandNewSideProvider(
            InlineWalk walk,
            ContextProviderNode provider,
            WalkPosition position,
            int nodeIndex)
        {
            provider.PushContext(_ctx.ComponentContextStack);
            try
            {
                var pairKey = FiberKeying.ProviderPosition(
                    _ctx.FiberStack.Current, position, provider.Key, nodeIndex);
                // Neither a structural position nor a walk-order counterpart means this Provider
                // is mounting here: its consumers mount with it and read the live cursor, so
                // there is nothing to notify.
                var oldProvider = walk.OldProvidersForPairing?.Match(
                    pairKey, walk.NewProviderOrdinal);
                walk.NewProviderOrdinal++;
                if (oldProvider != null && provider.HasValueChanged(oldProvider))
                {
                    NotifyContextValueChange(provider);
                }
                if (provider.Children != null)
                {
                    var childPosition = FiberKeying.ProviderChild(
                        position, provider.Key, nodeIndex);
                    ExpandInlineRecursive(walk, provider.Children, childPosition);
                }
            }
            finally
            {
                provider.PopContext(_ctx.ComponentContextStack);
            }
        }

        private void ExpandComponentInline(
            InlineWalk walk,
            ComponentNode component,
            WalkPosition position,
            int nodeIndex)
        {
            // Error-boundary behavior: once a sibling earlier in this
            // expansion has aborted via TryCatch.SetAborted, subsequent inline
            // ComponentNode mounts must not run their Body — otherwise their fiber
            // becomes registered with the new key but state never bound to the user
            // tree, blocking proper re-mount on the next normal render.
            if (_ctx.IsAborted) return;
            var identity = component.ResolvedIdentity;
            var slotKey = component.Key ?? FiberKeying.ResolveInlinePositionKey(
                position, nodeIndex, _ctx.ComponentRegistry.InlinePositionKeyBoxes);
            // The scope member of this component's own registry key. Read at this level and never carried
            // into its output: ExpandFiberPreviousTree pushes the fiber below, which is where the reading
            // stops answering — ReconcilerContext.PortalChildKeyScope owns why that has to be so.
            var portalScope = _ctx.PortalChildKeyScopeHere;
            var commit = walk.Commit;
            var result = walk.Result;
            if (walk.IsNewSide)
            {
                // Direct, live-context descent: the component renders
                // in-scope of its ancestor Providers, still pushed on the live
                // ComponentContextStack during this walk. UseContext reads that live cursor,
                // so no per-fiber snapshot is captured here. An isolated re-render later
                // reconstructs the enclosing Providers via FiberContextSpine.
                var parentFiber = _ctx.FiberStack.Current;
                // The fiber's output occupies parent.children from this slot; the
                // emitted-leaf count so far maps 1:1 to parent's slot range. The general
                // (commit) path commits leaves into NewElements; the structural (collect)
                // path accumulates them in result.
                var emittedCount = commit != null ? commit.NewElements.Count : result!.Count;
                var currentSlotStart = walk.SlotStart + emittedCount;
                // Two same-identity siblings sharing one explicit key resolve to the
                // SAME registry fiber; expanding it once per sibling would emit one
                // component's DOM twice while its slot bookkeeping tracks only the last
                // position (with hook state shared across both copies). Mirror the
                // leaf-level duplicate guard: warn and skip the repeat before
                // GetOrCreate can clobber the first occurrence's slot.
                var priorFiber = _ctx.ComponentRegistry.TryGetFiberForInlineKey(parentFiber, slotKey, identity, portalScope, walk.Parent);
                if (priorFiber != null && walk.NewFibers.Contains(priorFiber))
                {
                    FiberLogger.LogWarning("GeneralPathReconciler",
                        $"Duplicate component key detected among siblings: '{slotKey}'. " +
                        "The repeated sibling is skipped; give each sibling a unique key.");
                    return;
                }
                var fiber = _ctx.ComponentRegistry.GetOrCreateInline(
                    component, parentFiber, slotKey, walk.Parent, currentSlotStart, portalScope);
                walk.NewFibers.Add(fiber);
                var preCount = emittedCount;
                ExpandFiberPreviousTree(walk, fiber, component, position, nodeIndex);
                fiber.MountSlotCount = (commit != null ? commit.NewElements.Count : result!.Count) - preCount;
            }
            else
            {
                // Old-side (structural) walk: look up the previously rendered fiber by the
                // same registry key the new side registered under. FiberStack.Push mirrors the new
                // side so nested old-side components resolve against the same parent fiber they were
                // registered with; without the symmetric push the lookup parent would
                // diverge and the diff would treat reused fibers as orphans.
                var fiber = _ctx.ComponentRegistry.TryGetFiberForInlineKey(_ctx.FiberStack.Current, slotKey, identity, portalScope, walk.Parent);
                if (fiber != null)
                {
                    ExpandFiberPreviousTree(walk, fiber, component, position, nodeIndex);
                    // Post-order add: a directly-nested component must precede its
                    // parent in oldFibers so the orphan sweep's forward walk tears the
                    // subtree down bottom-up — a descendant's effect cleanups complete
                    // before an ancestor's, matching the commit-phase deletion order.
                    walk.OldFibers.Add(fiber);
                }
            }
        }

        // FiberKeying.ComponentChild restarts SlotPath here, so the descendants' slotKeys are scoped to
        // THIS fiber's body output. Otherwise the same descendant would compute different slotKeys when the
        // enclosing fiber re-renders independently (setState) vs when its outer parent re-renders. A
        // registry lookup mismatch would dispose the descendant fiber and reset its state.
        //
        // FiberStack.Push around the recursion is required so that nested inline ComponentNodes encountered
        // while walking fiber.PreviousTree are appended as children of THIS fiber, not the outer caller's
        // current fiber. Without it, a Parent → Child component chain would link Child.Parent to the outer
        // root fiber (the caller's Current), bypassing the Parent fiber entirely and breaking ErrorBoundary
        // search / context propagation walks. The same invariant applies here: a fiber stays the current
        // work-in-progress while its children are created from its body output.
        //
        // Both sides of the walk descend identically; only how they obtained the fiber differs.
        private void ExpandFiberPreviousTree(
            InlineWalk walk,
            ComponentFiber fiber,
            ComponentNode component,
            WalkPosition position,
            int nodeIndex)
        {
            if (fiber.PreviousTree == null || fiber.PreviousTree.Length == 0) return;

            _ctx.FiberStack.Push(fiber);
            // Moved with the FiberStack push, for the same reason: what this descent stamps onto its children
            // belongs to THIS fiber's output, not the outer caller's.
            var enclosingFiberTree = _ctx.CurrentFiberTree;
            _ctx.CurrentFiberTree = fiber.PreviousTree;
            try
            {
                var componentPosition = FiberKeying.ComponentChild(position, component.Key, nodeIndex);
                ExpandInlineRecursive(walk, fiber.PreviousTree, componentPosition);
            }
            finally
            {
                _ctx.CurrentFiberTree = enclosingFiberTree;
                _ctx.FiberStack.Pop();
            }
        }

        // Inline-expands a MemoNode. A memo component emits no DOM — it resolves to
        // an inner element that is reconciled like any other child. The dep cache is keyed by a
        // stable position scope (fragment scope + node index) — not a per-pass visitation counter —
        // so the old-side and new-side expansion passes resolve to aligned cache entries: the old
        // side runs first (ExpandInlineForReconcile expands old before new) and reads
        // the previously cached inner, while the new side recomputes only when the dependency array
        // changed. The resolved inner is expanded recursively so a Suspense / Component / Provider
        // inner is handled wrapper-less in the parent's slot range.
        private void ExpandMemoInline(
            InlineWalk walk,
            MemoNode memo,
            WalkPosition position,
            int nodeIndex)
        {
            var innerPosition = FiberKeying.MemoInner(position, nodeIndex);
            var cacheKey = FiberKeying.MemoCacheKey(memo.Key, innerPosition.Scope!);
            var (inner, previousCached) = _ctx.FiberMemoCache.GetOrCompute(cacheKey, memo);
            if (previousCached != null)
            {
                // A deps change just replaced the cached inner tree. The memo wrapper is opaque to the
                // recycle walk (a fiber's retired tree never descends into it), so this is the only
                // point that can retire the replaced subtree's rented objects; the sweep's owner mark
                // spares whatever the replacement or the owner's committed state still shares with it,
                // and the pass-scoped release staging keeps the rest un-rentable until the pass ends.
                FiberTreeReturn.ReturnRetiredTree(
                    FiberTreeReturn.NormalizeToArray(previousCached), _ctx.FiberStack.Current);
            }
            if (inner == null) return;
            // Recurse under this memo's own scope so a nested Memo's position key (or an inner
            // Component's slot key) cannot collide with this memo's scope — e.g. an outer and an
            // inner unkeyed Memo both at node index 0 would otherwise share cacheKey "{scope}/m0".
            ExpandInlineRecursive(walk, new[] { inner }, innerPosition);
        }

        // Bumps the propagation generation (only on the first change of this reconcile pass to
        // dedup nested Providers covering the same key) and walks the fiber subtree under
        // FiberStack.Current to schedule context-dependent consumers for re-render.
        // Each consumer re-reads the new value LIVE from the cursor on its re-render: this walk only
        // marks consumers dirty; the value is read at render time, so no
        // snapshot is propagated here.
        internal void NotifyContextValueChange(ContextProviderNode newProvider)
        {
            if (!_ctx.ContextValueChanged)
            {
                // int.MinValue collides with the no-dedup sentinel used by NotifyContextChanged.
                _ctx.ContextPropagationGeneration = _ctx.ContextPropagationGeneration == int.MaxValue
                    ? 1
                    : _ctx.ContextPropagationGeneration + 1;
            }
            _ctx.ContextValueChanged = true;

            var fiberRoot = _ctx.FiberStack.Current;
            UnityEngine.Debug.Assert(fiberRoot != null,
                "[Velvet] GeneralPathReconciler.NotifyContextValueChange: FiberStack.Current is null. " +
                "Context live propagation is skipped for this provider.");
            if (fiberRoot != null)
            {
                FiberTreeTraversal.NotifyContextChanged(
                    fiberRoot, newProvider.ContextKey, _ctx.ContextPropagationGeneration);
            }
        }

        #endregion

        #region Suspense

        // A reused (bailed-out) child does not re-throw FiberSuspendSignal, so an unrelated parent
        // re-render would otherwise reveal an empty primary while a descendant is still loading. The scan
        // is scoped to the children added during this expansion (not the whole boundary subtree) so an
        // async sibling outside the Suspense does not keep the boundary suspended.
        //
        // A nested Suspense boundary owns its own descendants' suspension, so its pending primary must not
        // keep the outer boundary suspended: nested boundary fibers, and any fiber whose nearest boundary
        // is a nested one, are skipped. The delta already contains every fiber in this Suspense's primary
        // subtree, so a per-fiber own-slot check covers descendants without re-walking.
        private static bool AnyPrimaryChildStillPending(
            HashSet<ComponentFiber> newFibers,
            HashSet<ComponentFiber> fibersBefore,
            ComponentFiber? boundaryFiber)
        {
            foreach (var fiber in newFibers)
            {
                if (fibersBefore.Contains(fiber)) continue;
                if (fiber.IsSuspenseBoundary) continue;
                var nested = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber);
                if (nested != null && !ReferenceEquals(nested, boundaryFiber)) continue;
                if (ComponentBoundarySearch.HasPendingAsyncSlot(fiber)) return true;
            }

            return false;
        }

        // Wrapper-less Suspense expansion. The Suspense emits no container
        // VisualElement: its children are expanded inline into result so they sit
        // directly in the parent's slot range and, on the new side, render in-scope of any enclosing
        // Provider (no pre-captured snapshot needed). The fiber rendering this Suspense
        // (FiberStack.Current) becomes the boundary so a descendant's
        // FiberSuspendSignal routes here via FindNearestSuspenseBoundary. If a
        // descendant suspends during the new-side render, the partial primary output is discarded (the
        // partially-mounted fibers stay registered so a later resolve re-render reuses them with their
        // state) and the fallback subtree is expanded instead. The children-vs-fallback decision is
        // recorded via ReconcilerContext.SetSuspenseFallbackShown keyed by (boundary,
        // position) so the old-side structural walk reproduces the committed subtree for the diff.
        // Primary and fallback children use distinct fragment scopes so their fibers never collide.
        private void ExpandSuspenseInline(
            InlineWalk walk,
            SuspenseNode suspense,
            WalkPosition position,
            int nodeIndex)
        {
            var result = walk.Result;
            var newFibers = walk.NewFibers;
            var offscreenPrimaries = walk.OffscreenPrimaries;
            var commit = walk.Commit;
            var boundaryFiber = _ctx.FiberStack.Current;
            var suspenseKey = FiberKeying.SuspenseKey(position.Scope, suspense.Key, nodeIndex);
            var primaryPosition = FiberKeying.SuspenseSubtree(
                position, suspenseKey, suspense.Key, nodeIndex, isFallback: false);
            var fallbackPosition = FiberKeying.SuspenseSubtree(
                position, suspenseKey, suspense.Key, nodeIndex, isFallback: true);

            if (walk.IsNewSide)
            {
                if (boundaryFiber != null) boundaryFiber.IsSuspenseBoundary = true;
                var preCount = commit != null ? commit.NewElements.Count : result!.Count;
                var suspended = false;
                // Snapshot the fiber set so the post-expansion pending check can be scoped to THIS
                // Suspense's own primary children (the fibers newly added during its expansion).
                var fibersBefore = _ctx.BufferPool.RentFiberSet();
                fibersBefore.UnionWith(newFibers);
                try
                {
                    if (suspense.Children is { Length: > 0 })
                    {
                        try
                        {
                            ExpandInlineRecursive(walk, suspense.Children, primaryPosition);
                        }
                        catch (FiberSuspendSignal)
                        {
                            suspended = true;
                        }
                    }
                    if (!suspended)
                    {
                        suspended = AnyPrimaryChildStillPending(newFibers, fibersBefore, boundaryFiber);
                    }
                    // Mark THIS Suspense's primary children (the fibers added during the children
                    // expansion) as offscreen iff suspended. The offscreen guard in FlushState defers
                    // their lane flush while suspended (their slot is occupied by the fallback). The
                    // fallback subtree is expanded below, so this loop never reaches it and this Suspense
                    // leaves it flushable; what marks a nested Suspense's fallback subtree is the
                    // enclosing expansion, whose own fallback occupies that slot too.
                    //
                    // A nested Suspense that suspended has already answered for the fibers it created, and
                    // this delta contains them, so its answer stands.
                    foreach (var f in newFibers)
                    {
                        if (fibersBefore.Contains(f)) continue;
                        if (!offscreenPrimaries.Contains(f)) f.IsOffscreen = suspended;
                        if (suspended) offscreenPrimaries.Add(f);
                    }
                    // Rollback and fallback expansion must run while fibersBefore is still live
                    // (rented from the pool, contents intact). Performing them after the finally
                    // would observe a Cleared / re-rented set, silently breaking the fibersBefore
                    // exclusion in RollbackCommitTo.
                    if (suspended)
                    {
                        if (commit != null) RollbackCommitTo(commit, preCount, fibersBefore, newFibers);
                        else if (result!.Count > preCount) result.RemoveRange(preCount, result.Count - preCount);
                        if (suspense.Fallback != null)
                        {
                            ExpandInlineRecursive(walk, new[] { suspense.Fallback }, fallbackPosition);
                        }
                    }
                }
                finally
                {
                    _ctx.BufferPool.ReturnFiberSet(fibersBefore);
                }
                // Records this Suspense's decision under its own position key. FlushState's offscreen guard
                // reads the boundary-level answer derived from those keys, so a sibling Suspense expanded
                // later in this same walk cannot clear it.
                _ctx.SetSuspenseFallbackShown(boundaryFiber, suspenseKey, suspended);
            }
            else
            {
                var wasFallback = _ctx.IsSuspenseFallbackShown(boundaryFiber, suspenseKey);
                var nodesToExpand = wasFallback
                    ? (suspense.Fallback != null ? new[] { suspense.Fallback } : Array.Empty<VNode>())
                    : (suspense.Children ?? Array.Empty<VNode>());
                if (nodesToExpand.Length > 0)
                {
                    ExpandInlineRecursive(walk, nodesToExpand,
                        wasFallback ? fallbackPosition : primaryPosition);
                }
            }
        }

        #endregion

        #region AnimatePresence

        // Wrapper-less AnimatePresence expansion (by design: AnimatePresence emits
        // no host element of its own). Its keyed children expand directly into the parent's slot range, so the
        // parent's flex / wrap / gap reach them. Per-boundary state
        // (ReconcilerContext.PresenceBoundaryState, keyed by (boundary fiber, position key)
        // like Suspense) records the leaf composition committed to the DOM so the old-side structural walk
        // reproduces it for the diff. The new side emits the current children plus still-exiting "ghost"
        // children (kept mounted until their exit animation finishes), then plays enter / exit / stagger on
        // each keyed child's <em>anchor</em> (its first emitted element) — element create / patch / remove /
        // reorder are handled by the surrounding general-commit machinery (CommitLeaf /
        // FinalizeGeneralCommit), matched by key. Exit is reconcile-driven: when an exit
        // animation completes, the key is flagged and the boundary re-rendered; the next render stops
        // emitting that child so the diff removes its leaves (no out-of-band DOM mutation that would shift
        // sibling slots).

        // Everything one expansion pass writes as it walks its entries, in one place so a PlayExit
        // completion — which can fire either synchronously (see the Settled comment in
        // ExpandAnimatePresenceInline) or long after this pass's own stack frames are gone — always observes
        // the same mutable cell. Must be a reference type: C# forbids a lambda from capturing a ref
        // parameter, so none of this can be threaded through the entry walkers as `ref`.
        private sealed class PresencePassTally
        {
            public bool Settled;
            public List<Action>? Deferred;
            // Stagger ordinals: exits count only the ghosts that actually animate, enters count every child
            // emitted, so the two advance independently.
            public int ExitIndex;
            public int VisualIndex;
            public int AnimatedExitCount;
            public bool RemovedInstantThisRender;
        }

        // One AnimatePresence boundary's expansion, as every per-entry step of it sees it: where the walk
        // emits, which boundary state it reconciles against, the three key sets the plan was built from, the
        // two flags decided once for the whole pass, and the tally the entries write to. Taken by `in` — a
        // pass runs per reconcile of a presence boundary, so bundling it must not allocate.
        private readonly struct PresenceExpansion
        {
            internal InlineWalk Walk { get; init; }
            internal WalkPosition Position { get; init; }
            internal ReconcilerContext.PresenceBoundaryState State { get; init; }
            internal ComponentFiber? BoundaryFiber { get; init; }
            internal AnimatePresenceNode Presence { get; init; }
            internal List<(string key, VNode node)> PrevCommitted { get; init; }
            internal List<(string key, VNode node)> NextCommitted { get; init; }
            internal List<(string key, VNode node)> NewKeyed { get; init; }
            internal bool BlockEnters { get; init; }
            internal bool FirstRender { get; init; }
            internal PresencePassTally Tally { get; init; }
        }

        // Old side: reproduce the committed leaf composition (including exiting ghosts) so the diff's old
        // leaves match the live DOM. No state mutation, no animation.
        private void ReproduceCommittedPresence(
            InlineWalk walk,
            (ComponentFiber? boundary, VisualElement? parent, string presenceKey) stateKey,
            WalkPosition presencePosition)
        {
            if (!_ctx.PresenceStates.TryGetValue(stateKey, out var oldState)) return;

            foreach (var (key, node) in oldState.Committed)
            {
                EmitPresenceChildAsAnchor(walk, node,
                    FiberNodeFactory.FindFirstMotionDescendant(node), key, presencePosition, out _);
            }
        }

        // mode="wait": while any previously-committed child is still exiting, hold back
        // brand-new keys so the exit fully completes before the new child mounts / enters. The exit's
        // completion already re-renders the boundary; on that render no ghost remains, so the withheld
        // child emits and enters. Returning ghosts (a key re-added mid-exit) and persisting children are
        // never withheld — only keys absent from the committed set.
        private static bool ShouldBlockPresenceEnters(
            AnimatePresenceNode presence,
            List<(string key, VNode node)> prevCommitted,
            HashSet<string> newKeySet,
            ReconcilerContext.PresenceBoundaryState state)
        {
            if (presence.Mode != AnimatePresenceMode.Wait) return false;

            foreach (var (key, node) in prevCommitted)
            {
                if (newKeySet.Contains(key)) continue;
                if (state.ExitComplete.Contains(key)) continue;
                var ghostMotion = FiberNodeFactory.FindFirstMotionDescendant(node);
                if (ResolveExitTransition(ghostMotion)?.HasExitAnimation == true) return true;
            }
            return false;
        }

        // Committed emission order: the current children in new order, with each previously
        // committed key now absent spliced back at the index it held among its previous
        // siblings. An exiting child must hold its slot among unchanged neighbors for the
        // whole exit (only a popLayout-style mode pulls it out of flow); appending ghosts
        // after every current child instead yanked a non-last exiting item behind its later
        // siblings — and physically reordered the DOM — the instant its exit began.
        // Finished / instant-removed ghosts are dropped by the per-entry walk over the result.
        private static void BuildPresenceEmissionPlan(
            List<(string key, VNode node)> newKeyed,
            List<(string key, VNode node)> prevCommitted,
            HashSet<string> newKeySet,
            List<(string key, VNode node)> plan)
        {
            foreach (var entry in newKeyed) plan.Add(entry);
            var previousIndex = 0;
            foreach (var (key, node) in prevCommitted)
            {
                if (!newKeySet.Contains(key))
                {
                    plan.Insert(previousIndex < plan.Count ? previousIndex : plan.Count, (key, node));
                }
                previousIndex++;
            }
        }

        // staggerDirection sweeps last-to-first over the children that actually animate-exit this render (a
        // no-op for the default forward direction, but needed to reverse), so the total has to be known
        // before the first exit is dispatched.
        private static int CountAnimatedExits(
            List<(string key, VNode node)> prevCommitted,
            HashSet<string> newKeySet,
            ReconcilerContext.PresenceBoundaryState state)
        {
            var exitCount = 0;
            foreach (var (key, node) in prevCommitted)
            {
                if (newKeySet.Contains(key) || state.ExitComplete.Contains(key)) continue;
                if (ResolveExitTransition(FiberNodeFactory.FindFirstMotionDescendant(node))?.HasExitAnimation == true)
                {
                    exitCount++;
                }
            }
            return exitCount;
        }

        private void ExpandAnimatePresenceInline(
            InlineWalk walk,
            AnimatePresenceNode presence,
            WalkPosition position,
            int nodeIndex)
        {
            var parent = walk.Parent;
            var commit = walk.Commit;
            var boundaryFiber = _ctx.FiberStack.Current;
            var presencePosition = FiberKeying.Presence(position, presence.Key, nodeIndex);
            // FiberKeying.PresenceKey always composes onto its parent, so this scope is never null.
            var presenceKey = presencePosition.Scope!;
            // Parent is part of the key so an AnimatePresence nested inside a real element does not collide
            // with an outer one at the same (fiber, scope, index). See ReconcilerContext.PresenceStates.
            var stateKey = (boundaryFiber, parent, presenceKey);

            if (!walk.IsNewSide)
            {
                _ctx.MarkPresenceReproduced(stateKey);
                ReproduceCommittedPresence(walk, stateKey, presencePosition);
                return;
            }

            var firstRender = !_ctx.PresenceStates.TryGetValue(stateKey, out var state);
            if (firstRender)
            {
                state = new ReconcilerContext.PresenceBoundaryState();
                _ctx.PresenceStates[stateKey] = state;
            }
            if (_ctx.CurrentPortalPlaceholder != null)
            {
                state!.OwningPortalPlaceholder = _ctx.CurrentPortalPlaceholder;
            }

            var newKeyed = _factory.BuildKeyedMapCopy(presence.Children);
            var newKeySet = _ctx.BufferPool.RentPresenceKeySet();
            var prevCommitted = _ctx.BufferPool.RentKeyedList();
            var nextCommitted = _ctx.BufferPool.RentKeyedList();
            var plan = _ctx.BufferPool.RentKeyedList();
            try
            {
                foreach (var entry in state.Committed) prevCommitted.Add(entry);
                foreach (var (key, _) in newKeyed) newKeySet.Add(key);

                var blockEnters = ShouldBlockPresenceEnters(presence, prevCommitted, newKeySet, state);
                BuildPresenceEmissionPlan(newKeyed, prevCommitted, newKeySet, plan);
                var exitCount = CountAnimatedExits(prevCommitted, newKeySet, state);

                // An exit's completion callback normally runs long after this method has returned (a tween's
                // scheduled timeout, a spring's settled tick). A spring exit whose variant pair touches no
                // spring-animatable channel (MotionSpringDriver.Create returns null — e.g. an exit variant whose
                // only delta is a keyword length like `w-auto` or a semantic theme token, neither of which
                // carries a number to interpolate) is the one case that completes SYNCHRONOUSLY, from inside
                // the PlayExit call below,
                // before this pass has finished building nextCommitted for every other key and before this
                // pass's own state.ExitComplete.Clear() further down. Running such a completion's bookkeeping
                // immediately would have that same Clear() wipe the ExitComplete entry it just added, so the
                // re-render it schedules finds the ghost "not complete" again, replays PlayExit, and repeats
                // forever. exitPass.Settled tracks whether this pass's own bookkeeping (below) has already
                // run; a completion that fires before then is queued and drained once it has, so its
                // ExitComplete.Add survives into the render it schedules — a genuinely async completion
                // always finds tally.Settled already true (this method returned long before it fires)
                // and runs immediately, unchanged.
                var tally = new PresencePassTally { AnimatedExitCount = exitCount };
                var pass = new PresenceExpansion
                {
                    Walk = walk,
                    Position = presencePosition,
                    State = state,
                    BoundaryFiber = boundaryFiber,
                    Presence = presence,
                    PrevCommitted = prevCommitted,
                    NextCommitted = nextCommitted,
                    NewKeyed = newKeyed,
                    BlockEnters = blockEnters,
                    FirstRender = firstRender,
                    Tally = tally,
                };

                foreach (var (key, node) in plan)
                {
                    if (!newKeySet.Contains(key))
                    {
                        ExpandPresenceGhostEntry(in pass, key, node);
                        continue;
                    }

                    ExpandPresenceEnterEntry(in pass, key, node);
                }

                // onExitComplete fires once the exiting children are gone. When every removed child
                // had NO exit animation (all instant-removed above) no PlayExit callback runs to fire it, so fire it
                // here — but only when no animated exit is still in flight (those fire it when the Exiting set drains).
                // Contained the same way as RunExitComplete's animated-exit path above: a throwing callback
                // must not skip the state.Committed/exitPass.Settled bookkeeping that follows, or the next
                // render reproduces a stale old side.
                if (commit != null && tally.RemovedInstantThisRender && state.Exiting.Count == 0)
                {
                    try
                    {
                        presence.OnExitComplete?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ComponentBoundarySearch.PropagateException(boundaryFiber, ex);
                    }
                }

                // Unlike RunExitComplete above, nothing here needs an explicit boundaryFiber.IsDisposed
                // guard even though the callback above can (via a cascading ancestor catch) dispose it:
                // ComponentRegistry.UnregisterFiber synchronously prunes this boundary's PresenceStates
                // entry as part of that same disposal, so `state` is already an orphaned, unreferenced
                // object by the time control returns here — mutating it further is a no-op, not a hazard.
                // 3) Commit the new composition for the next old-side reproduction. Exit-complete keys were
                //    not re-emitted this render (their leaves are being removed), so drop them.
                state.Committed.Clear();
                foreach (var entry in nextCommitted) state.Committed.Add(entry);
                state.ExitComplete.Clear();

                // This pass's own bookkeeping has settled — a synchronous exit completion queued above can now
                // run safely (see PresencePassTally.Settled's declaration comment): its ExitComplete.Add
                // survives past this point instead of being wiped by the Clear() just above.
                tally.Settled = true;
                if (tally.Deferred != null)
                {
                    foreach (var completion in tally.Deferred) completion();
                }

                // Marked here rather than beside stateKey above, so an expansion that unwound is not
                // counted as having rendered this presence again.
                _ctx.MarkPresenceReRendered(stateKey);
            }
            finally
            {
                _ctx.BufferPool.Return(newKeyed);
                _ctx.BufferPool.ReturnPresenceKeySet(newKeySet);
                _ctx.BufferPool.Return(prevCommitted);
                _ctx.BufferPool.Return(nextCommitted);
                _ctx.BufferPool.Return(plan);
            }
        }

        // Ghost branch of the per-plan-entry walk: a previously-committed key now absent from the new
        // children, spliced into the plan at its old position (see ExpandAnimatePresenceInline). A finished
        // exit is dropped (not emitted → the diff removes its leaves); a child without an exit animation is
        // removed immediately; otherwise the child stays mounted in its old slot and its exit is started once.
        private void ExpandPresenceGhostEntry(in PresenceExpansion pass, string key, VNode node)
        {
            var walk = pass.Walk;
            var state = pass.State;
            var boundaryFiber = pass.BoundaryFiber;
            var commit = walk.Commit;
            if (state.ExitComplete.Contains(key))
            {
                state.Exiting.Remove(key);
                // Once the exit detached the ghost's element, the old-side reproduction can no longer
                // recurse into the ghost's subtree (the Motion's PreviousTree was cleared), so its
                // inline fibers escape the orphan sweep. Dispose them explicitly via the tracked anchor
                // before the diff removes the leaves — otherwise a same-key re-entry would re-pair the
                // undisposed fiber as a zombie whose local state updates no longer re-render.
                DisposeExitedGhostFibers(state, key);
                // Leaving the committed set is the ghost node's last live root (presence bookkeeping
                // was what kept it alive past its emitting render), so retire its pooled objects here.
                // The entry must leave state.Committed FIRST: the sweep's mark reads that very list
                // (other retirements must spare live ghosts), and a still-listed entry would spare
                // this sweep's own target. prevCommitted is a copy, so the loop is unaffected.
                RemovePresenceCommittedEntry(state.Committed, key);
                // The key's leaves are leaving the DOM for good — its memoized Motion element
                // must retire with them, or a pooled element could be resurrected as a later
                // dispatch target.
                state.MotionElements.Remove(key);
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(node), boundaryFiber);
                return;
            }

            var ghostMotionNode = FiberNodeFactory.FindFirstMotionDescendant(node);
            var ghostTransition = ResolveExitTransition(ghostMotionNode);
            if (ghostTransition?.HasExitAnimation != true)
            {
                // No exit animation → immediate removal (skip emitting; the diff reaps the leaves).
                state.Exiting.Remove(key);
                pass.Tally.RemovedInstantThisRender = true;
                // Same as the finished-exit drop above: leave the committed set, then retire.
                RemovePresenceCommittedEntry(state.Committed, key);
                // Same memoized-element retirement as the finished-exit drop above.
                state.MotionElements.Remove(key);
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(node), boundaryFiber);
                return;
            }

            var ghostAnchor = EmitPresenceChildAsAnchor(walk, node, ghostMotionNode, key, pass.Position,
                out var ghostMotionElement);
            // A ghost reproduces the SAME committed node on both diff sides, so the patch that
            // would re-record the Motion's element bails on reference equality — fall back to
            // the per-key memo the live emissions kept (see PresenceBoundaryState.MotionElements).
            if (commit != null)
            {
                if (ghostMotionElement != null) state.MotionElements[key!] = ghostMotionElement;
                else state.MotionElements.TryGetValue(key!, out ghostMotionElement);
            }

            // Track the live ghost anchor so the drop path (exit complete) can dispose the subtree
            // fibers under it — see DisposeExitedGhostFibers.
            if (commit != null && ghostAnchor != null) state.ExitAnchors[key] = ghostAnchor;

            if (commit != null && ghostAnchor != null && state.Exiting.Add(key))
            {
                StartPresenceExit(in pass, key, ghostAnchor, ghostMotionElement, ghostMotionNode);
                pass.Tally.ExitIndex++;
            }

            pass.NextCommitted.Add((key, node));
        }

        // Starts one ghost's exit animation: the enter cancellations and the PopLayout pin that must precede
        // it, then the reconcile-driven completion that flags the finished exit and re-renders the boundary.
        private void StartPresenceExit(
            in PresenceExpansion pass,
            string key,
            VisualElement ghostAnchor,
            VisualElement? ghostMotionElement,
            MotionNode? ghostMotionNode)
        {
            var presence = pass.Presence;
            var tally = pass.Tally;
            var exitIndex = tally.ExitIndex;
            _ctx.StyleAnimationScheduler.CancelEnter(ghostAnchor);
            // A wrapped Motion's variant enter ran on its own element, not the anchor —
            // cancel there too (idempotent when both are the same element).
            if (ghostMotionElement != null && !ReferenceEquals(ghostMotionElement, ghostAnchor))
            {
                _ctx.StyleAnimationScheduler.CancelEnter(ghostMotionElement);
            }
            if (presence.Mode == AnimatePresenceMode.PopLayout)
            {
                PinExitingChildOutOfFlow(ghostAnchor);
            }
            var capturedKey = key;
            var capturedState = pass.State;
            var capturedBoundary = pass.BoundaryFiber;
            var capturedOnExitComplete = presence.OnExitComplete;
            // `exit`: when the resolved Motion declares an exit variant label, animate from the
            // resting variants[animate] to variants[exit]; otherwise use the transition's
            // ExitFrom/ExitTo. The variant swap targets the Motion's OWN element (where the
            // resting variant classes live), which for a wrapped Motion is not the anchor —
            // without a resolved element the variant path is unavailable and the classic,
            // anchor-targeted transition plays instead.
            var variantExit = ghostMotionElement != null ? TryResolveVariantExit(ghostMotionNode) : null;
            var exitTransition = variantExit ?? ghostMotionNode?.Transition;
            var exitTarget = variantExit != null ? ghostMotionElement! : ghostAnchor;
            // For a variant exit the From classes ARE the resting variants[animate]; if this exit is
            // cancelled (the key is re-added before it finishes) the element must return to that resting
            // variant rather than be left without it (interrupt handling).
            void RunExitComplete()
            {
                if (_ctx.IsDisposed) return;
                // Reconcile-driven removal: flag the finished exit and re-render the boundary;
                // the next render stops emitting this child and the diff removes its leaves.
                capturedState.Exiting.Remove(capturedKey);
                capturedState.ExitComplete.Add(capturedKey);
                // onExitComplete fires once the exiting set drains (the last
                // in-flight exit finished). Cancelled exits (key re-entered) remove from Exiting
                // elsewhere and do not reach here, so they never trigger it. Contained: a
                // throwing callback must not skip the ghost-drop re-render scheduled below,
                // mirroring HookEffectExecutor's effect-exception containment.
                if (capturedState.Exiting.Count == 0)
                {
                    try
                    {
                        capturedOnExitComplete?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        ComponentBoundarySearch.PropagateException(capturedBoundary, ex);
                    }
                }
                if (capturedBoundary != null && capturedBoundary.IsDisposed)
                {
                    // The callback above threw and an ancestor boundary's fallback already
                    // replaced capturedBoundary's whole subtree while handling it — there is no
                    // ghost left to drop a re-render for, and ScheduleRerender has no disposed
                    // guard of its own (unlike the public RequestRenderFromHook/
                    // RequestTransitionRerender), so scheduling here would just pin a disposed
                    // fiber dirty in the batch scheduler forever.
                }
                else if (capturedBoundary != null)
                {
                    // The boundary's own hook inputs are unchanged (the exit finished out of band,
                    // not via a state update), so an auto-memoized boundary would return its cached
                    // VNode and the reconciler would bail — the AnimatePresence would never re-expand
                    // and the finished ghost would linger forever. Invalidate the memo so the
                    // re-render re-walks the children and the ghost-drop runs, mirroring the Suspense
                    // reveal path (InvalidateMemoCache + FiberWorkLoop.RequestRenderFromHook).
                    capturedBoundary.InvalidateMemoCache();
                    FiberWorkLoop.ScheduleRerender(capturedBoundary, FiberUpdatePriority.Normal);
                }
                else
                {
                    // No owning component fiber to re-render (a top-level AnimatePresence reconciled
                    // straight onto a VisualElement). The exit animation finished but the reconcile
                    // that drops the ghost can't be scheduled, so the element would silently linger.
                    // Mount AnimatePresence inside a component (V.Mount establishes a root fiber) so
                    // exit completion can remove the child. Warn rather than leak in silence.
                    // Intentional: the supported path is V.Mount; reconciling straight onto a bare
                    // element leaves no owner to drive the ghost-removal re-render.
                    FiberLogger.LogWarning("AnimatePresence",
                        "Exit completed but the presence has no owning component fiber to re-render, "
                        + "so the exited child cannot be removed. Mount AnimatePresence inside a "
                        + "component (e.g. via V.Mount) rather than reconciling it onto a bare element.");
                }
            }
            _ctx.StyleAnimationScheduler.PlayExit(exitTarget, exitTransition, () =>
            {
                // See the Settled comment in ExpandAnimatePresenceInline: a synchronous completion (fired
                // from inside this very PlayExit call) is queued instead of run inline.
                if (tally.Settled) RunExitComplete();
                else (tally.Deferred ??= new List<Action>()).Add(RunExitComplete);
            }, restoreFromOnCancel: variantExit != null,
                additionalDelaySec: presence.StaggerDelaySec(exitIndex, tally.AnimatedExitCount));
        }

        // Live branch of the per-plan-entry walk: the key is present in the new children (a genuine re-entry
        // whose exit was cancelled or already completed, or a first-time add). Emits the child, reconciles
        // exit/enter bookkeeping against its previous ghost state (if any), and dispatches its enter animation.
        private void ExpandPresenceEnterEntry(in PresenceExpansion pass, string key, VNode node)
        {
            var walk = pass.Walk;
            var state = pass.State;
            var presence = pass.Presence;
            var prevCommitted = pass.PrevCommitted;
            var commit = walk.Commit;
            // Withhold a brand-new child under mode="wait" while exits are in flight (see above). The
            // linear prevCommitted scan is bounded: this only runs when blockEnters is set, and wait-mode
            // targets single-child swaps, so prevCommitted holds ~1 entry.
            if (pass.BlockEnters && !PresenceContainsKey(prevCommitted, key))
            {
                return;
            }

            // Resolved BEFORE emission (not just once): shared by the PopLayout restore below (it needs
            // the re-added node's OWN class list, not the ghost's pre-pin one), the enter dispatch
            // further down, and — via EmitPresenceChildAsAnchor — FiberNodeFactory's standalone-enter
            // gate, so CreateElement can tell this SAME node (which the dispatch below is about to
            // explicitly animate) apart from every OTHER Motion the emission below might create.
            var motion = FiberNodeFactory.FindFirstMotionDescendant(node);
            var anchor = EmitPresenceChildAsAnchor(walk, node, motion, key, pass.Position,
                out var motionElement);
            // Same memo discipline as the ghost path: record when this emission resolved the
            // element (create or genuine patch), fall back to the memo when a no-op re-render's
            // reference-equal patch bailed before recording.
            if (commit != null)
            {
                if (motionElement != null) state.MotionElements[key!] = motionElement;
                else state.MotionElements.TryGetValue(key!, out motionElement);
            }

            if (commit != null && anchor != null)
            {
                var wasExiting = state.Exiting.Remove(key);
                var wasExitComplete = state.ExitComplete.Remove(key);
                if (wasExiting || wasExitComplete)
                {
                    RetireReplacedGhostNode(state, prevCommitted, key, node, pass.BoundaryFiber);
                }
                // The key is present again. If a completed exit's ghost was still awaiting its drop and the
                // re-entry mounted a FRESH element (the detached ghost can't be reproduced, so the new
                // anchor differs), its inline fibers escaped the orphan sweep exactly as on the normal drop
                // path — dispose them via the tracked anchor before re-pairing, else this same-render
                // re-entry resurrects them as a zombie. A cancel-exit instead reproduces the SAME still-
                // attached element (anchor == the stale one), so just drop the now-current reference.
                var freshReplacement =
                    state.ExitAnchors.TryGetValue(key, out var staleAnchor) && !ReferenceEquals(staleAnchor, anchor);
                if (freshReplacement)
                {
                    DisposeExitedGhostFibers(state, key);
                }
                else
                {
                    state.ExitAnchors.Remove(key!);
                }
                if (wasExiting)
                {
                    CancelInterruptedPresenceExit(anchor, motionElement, presence, node);
                }
                else if (wasExitComplete)
                {
                    RestoreAfterCompletedPresenceExit(anchor, motionElement, motion, presence, node,
                        freshReplacement);
                }

                var isEnter = wasExiting || wasExitComplete || !PresenceContainsKey(prevCommitted, key);
                if (isEnter)
                {
                    PlayPresenceEnter(in pass, motion, anchor, motionElement, wasExiting);
                }
            }

            pass.NextCommitted.Add((key, node));
            pass.Tally.VisualIndex++;
        }

        // The re-entry replaces the ghost's node in the committed set. The OLD node was kept alive only by
        // presence bookkeeping (a ghost is never part of the boundary's own render output), so this
        // replacement is its last reference — retire it. The entry leaves state.Committed first, or the
        // sweep's mark (which reads that list) would spare its own target; prevCommitted is a copy, so the
        // caller's loop over it is unaffected.
        private static void RetireReplacedGhostNode(
            ReconcilerContext.PresenceBoundaryState state,
            List<(string key, VNode node)> prevCommitted,
            string key,
            VNode node,
            ComponentFiber? boundaryFiber)
        {
            var ghostNode = FindPresenceCommittedNode(prevCommitted, key);
            if (ghostNode == null || ReferenceEquals(ghostNode, node)) return;

            RemovePresenceCommittedEntry(state.Committed, key);
            FiberTreeReturn.ReturnRetiredTree(
                FiberTreeReturn.NormalizeToArray(ghostNode), boundaryFiber);
        }

        private void CancelInterruptedPresenceExit(
            VisualElement anchor,
            VisualElement? motionElement,
            AnimatePresenceNode presence,
            VNode node)
        {
            _ctx.StyleAnimationScheduler.CancelExit(anchor);
            // A wrapped Motion's variant exit ran on its own element, not the anchor — the
            // cancel (whose reversal restores the resting variant) must land there too.
            if (motionElement != null && !ReferenceEquals(motionElement, anchor))
            {
                _ctx.StyleAnimationScheduler.CancelExit(motionElement);
            }
            if (presence.Mode == AnimatePresenceMode.PopLayout)
            {
                // The anchor's OWN class list, not motion's: PinExitingChildOutOfFlow pinned
                // `anchor` (this keyed child's own top-level element), which for a Div wrapping
                // a Motion (the z-managed shape) is a DIFFERENT element — with a different
                // class list — than the nested Motion FindFirstMotionDescendant resolved.
                RestorePopLayoutChildToFlow(anchor, (node as BaseElementNode)?.ClassNames);
            }
        }

        // A COMPLETED exit's re-entry (the drop render was preempted) reproduces a still-attached element
        // that nothing downstream un-parks: there is no pending animation left to cancel, so the
        // interruption restores that CancelInterruptedPresenceExit handles never run. Reverse the exit-time
        // mutations here so the enter branches start from the same state a fresh mount would.
        private static void RestoreAfterCompletedPresenceExit(
            VisualElement anchor,
            VisualElement? motionElement,
            MotionNode? motion,
            AnimatePresenceNode presence,
            VNode node,
            bool freshReplacement)
        {
            if (presence.Mode == AnimatePresenceMode.PopLayout && !freshReplacement)
            {
                // The out-of-flow pin outlives its exit (only the drop would have removed the
                // element). Skipped for a fresh replacement: the pin lives on the discarded
                // ghost, and nulling a never-pinned element's geometry slots would erase
                // resolver-applied inline values a transparent-wrapper child cannot re-resolve.
                RestorePopLayoutChildToFlow(anchor, (node as BaseElementNode)?.ClassNames);
            }
            if (motionElement == null) return;

            // The completed swap left the element AT variants[exit] with the resting
            // variants[animate] stripped; the no-initial enter branch plays nothing
            // and the class diff cannot help (MotionAppliedClasses still records the
            // resting set as applied). Resolved from the RE-ADDED node's variants — the
            // ghost node is already retired, so the exact applied arrays are gone; a
            // re-add that also changed the variants map may leave the old exit class
            // behind, or skip this restoration entirely when the exit label no longer
            // resolves — the same staleness any heuristic over the new declaration has.
            var completedExit = TryResolveVariantExit(motion);
            if (completedExit != null)
            {
                StyleAnimationClassUtils.RemoveClasses(motionElement, completedExit.ExitToClasses);
                StyleAnimationClassUtils.AddClasses(motionElement, completedExit.ExitFromClasses);
            }
        }

        private void PlayPresenceEnter(
            in PresenceExpansion pass,
            MotionNode? motion,
            VisualElement? anchor,
            VisualElement? motionElement,
            bool wasExiting)
        {
            if (ResolveEnterTransition(motion) != null)
            {
                var presence = pass.Presence;
                // The Initial flag only suppresses the enter animation on the AnimatePresence's
                // very first mount; later additions always animate.
                if (!pass.FirstRender || presence.Initial)
                {
                    DispatchPresenceEnter(motion, anchor, motionElement, wasExiting,
                        presence.StaggerDelaySec(pass.Tally.VisualIndex, pass.NewKeyed.Count),
                        pass.BoundaryFiber);
                }
                else
                {
                    InvokeEnterComplete(motion, pass.BoundaryFiber);
                }
            }
        }

        // The enter paths that fire the callback in-pass rather than handing it to
        // StyleAnimationScheduler share this so the containment is written once, and it is the same
        // containment RunExitComplete gives the other half of the pair: the emission this sits inside has
        // bookkeeping still to do, and a user callback must not be what stops it.
        private static void InvokeEnterComplete(MotionNode motion, ComponentFiber? boundaryFiber)
        {
            try
            {
                motion.OnEnterComplete?.Invoke();
            }
            catch (Exception ex)
            {
                ComponentBoundarySearch.PropagateException(boundaryFiber, ex);
            }
        }

        // What StyleAnimationScheduler is handed, rather than the user's own delegate: a play whose
        // duration is zero — StyleTransitionConfig.None is one — completes inside the Play* call that
        // starts it (StyleAnimationScheduler.ValidateDuration), and the enter dispatches make that call
        // from inside the pass.
        internal static Action? ContainedEnterComplete(MotionNode motion, ComponentFiber? boundaryFiber)
            => motion.OnEnterComplete == null ? null : () => InvokeEnterComplete(motion, boundaryFiber);

        // A variant Motion (carrying variants + animate) manages its resting state through variant classes:
        // variants[animate] is applied at mount and restored by CancelExit on an exit-cancel. So it only ever
        // plays a VARIANT enter (when an `initial` label is declared) and must NOT fall through to the classic
        // preset enter — the default StyleTransition.Fade would replay a fade-in on top of the resting variant
        // on every add / interrupt. The variant swap targets the Motion's OWN element (where those resting
        // classes live), which for a wrapped Motion is not the anchor; without a resolved element the variant
        // path is unavailable and the classic enter plays on the anchor.
        //
        // A cancelled exit reproduces the SAME still-attached element — not a first mount — so `initial` does
        // not reapply: CancelExit already reverses the element toward its resting variant with the transition
        // kept alive, and replaying initial→animate here would re-seed the declared initial pose (a jump) and
        // restart the full enter duration from it.
        private void DispatchPresenceEnter(
            MotionNode motion,
            VisualElement? anchor,
            VisualElement? motionElement,
            bool wasExiting,
            float staggerDelaySec,
            ComponentFiber? boundaryFiber)
        {
            var isVariantMotion = motionElement != null && motion.Variants != null && motion.Animate != null;
            if (isVariantMotion && !wasExiting
                && TryResolveVariantInitial(motion, out var fromClasses, out var toClasses,
                    out var enterTransition)
                && enterTransition != null)
            {
                // `initial`: enter from variants[initial] to variants[animate] (kept as the persistent
                // resting state).
                _ctx.StyleAnimationScheduler.PlayVariantEnter(motionElement, fromClasses, toClasses,
                    enterTransition, ContainedEnterComplete(motion, boundaryFiber), staggerDelaySec);
            }
            else if (isVariantMotion)
            {
                // Variant Motion without `initial`: rest at variants[animate], no enter anim.
                InvokeEnterComplete(motion, boundaryFiber);
            }
            else
            {
                _ctx.StyleAnimationScheduler.PlayEnter(anchor, motion.Transition,
                    ContainedEnterComplete(motion, boundaryFiber), staggerDelaySec);
            }
        }

        private static bool PresenceContainsKey(
            List<(string key, VNode node)> list, string? key)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].key == key) return true;
            }
            return false;
        }

        private static VNode? FindPresenceCommittedNode(
            List<(string key, VNode node)> list, string? key)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].key == key) return list[i].node;
            }
            return null;
        }

        private static void RemovePresenceCommittedEntry(
            List<(string key, VNode node)> list, string? key)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].key == key)
                {
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        // AnimatePresenceMode.PopLayout: the instant a child's exit starts, pull it out of layout flow and pin
        // it via absolute positioning at the last rect Yoga resolved for it in-flow (anchor.layout is parent-
        // relative), so still-present siblings reflow into its place immediately while the exit animation
        // finishes on top. Skipped when any component is non-finite (an EditMode pass with no forced layout
        // leaves `.layout` at NaN) — the child then degrades to a normal in-flow exit rather than being pinned
        // to garbage coordinates.
        //
        // layout.x/y is a flow-resolved position that already bakes in the child's own leading margin (an
        // explicit m-* utility, or the inter-child margin StyleGapManipulator writes for gap-*) — the box
        // simply renders shifted by that margin. Absolute positioning keeps the margin active too (UI Toolkit
        // offsets the border box by left/top AND the still-set margin, matching CSS), so pinning at the raw
        // layout rect would apply the same margin twice and jump the child by it the instant the exit starts.
        // Subtracting the resolved margin back out of left/top cancels exactly that double count, so the
        // pinned rect reproduces the in-flow position pixel for pixel.
        private static void PinExitingChildOutOfFlow(VisualElement anchor)
        {
            var rect = anchor.layout;
            if (!float.IsFinite(rect.x) || !float.IsFinite(rect.y)
                || !float.IsFinite(rect.width) || !float.IsFinite(rect.height))
            {
                return;
            }

            var resolved = anchor.resolvedStyle;
            var marginLeft = resolved.marginLeft;
            var marginTop = resolved.marginTop;

            anchor.style.position = Position.Absolute;
            anchor.style.left = rect.x - marginLeft;
            anchor.style.top = rect.y - marginTop;
            anchor.style.width = rect.width;
            anchor.style.height = rect.height;
        }

        // Reverses PinExitingChildOutOfFlow when a PopLayout exit is cancelled (its key re-added before the
        // exit finished): clears the same five inline styles back to StyleKeyword.Null so the child rejoins
        // its parent's normal layout flow. A no-op (harmless) when the exit was never pinned (non-finite
        // layout at exit-start), since clearing an already-null style is idempotent.
        //
        // Nulling width/height erases whatever geometry a resolver-applied arbitrary-value class (w-[..],
        // h-[..]) owns — those live in the SAME inline slots this method clears and have no USS rule to fall
        // back to, unlike a named utility (w-full) whose class rule keeps applying underneath. classNames is
        // the re-added ANCHOR's OWN class array (not anchor.GetClasses(): arbitrary-value tokens resolve
        // straight to inline style and are deliberately never added to the USS class list, so the element's
        // class list itself never contains them) — the anchor's own declared classes, not necessarily the
        // Motion's: PinExitingChildOutOfFlow always pins the ANCHOR (the keyed child's own top-level
        // element), which for a z-managed presence child wrapping a Motion in a Div is the Div, with its own,
        // separate class list from the nested Motion's. The caller resolves it from the CURRENT node —
        // already patched by EmitPresenceChild, which runs before this restore — so a re-add that also
        // changed props re-applies the new value rather than a stale pre-pin one.
        //
        // CancelExit (the caller's previous statement) deliberately leaves transition-property: all /
        // transition-duration active on this exact element so the variant's OWN reversal (e.g. opacity
        // fading back toward its resting value) keeps interpolating instead of popping. This geometry fixup
        // is bookkeeping, not an animated property, but with that transition still live any value it passes
        // through — including the Null this method itself writes before re-asserting the resting one — is
        // just as eligible to animate, so the position/left/top/width/height correction would visibly tween
        // in on top of the resting geometry instead of landing on it immediately. Suspending the transition
        // for exactly this method's writes and restoring it immediately after keeps the reversal (which never
        // touches these five properties) running untouched while the geometry itself snaps.
        private static void RestorePopLayoutChildToFlow(VisualElement anchor, string[]? classNames)
        {
            var savedProperty = anchor.style.transitionProperty;
            var savedDuration = anchor.style.transitionDuration;
            var savedTimingFunction = anchor.style.transitionTimingFunction;
            var savedDelay = anchor.style.transitionDelay;
            anchor.style.transitionProperty = StyleKeyword.Null;
            anchor.style.transitionDuration = StyleKeyword.Null;
            anchor.style.transitionTimingFunction = StyleKeyword.Null;
            anchor.style.transitionDelay = StyleKeyword.Null;

            anchor.style.position = StyleKeyword.Null;
            anchor.style.left = StyleKeyword.Null;
            anchor.style.top = StyleKeyword.Null;
            anchor.style.width = StyleKeyword.Null;
            anchor.style.height = StyleKeyword.Null;
            if (classNames != null)
            {
                FiberNodePatcher.ReapplyArbitraryValues(anchor, classNames);
            }

            anchor.style.transitionProperty = savedProperty;
            anchor.style.transitionDuration = savedDuration;
            anchor.style.transitionTimingFunction = savedTimingFunction;
            anchor.style.transitionDelay = savedDelay;
        }

        // Disposes the inline/wrapper fibers mounted under an exit-completed ghost's anchor element. Needed
        // because a Motion whose exit detached its element clears its PreviousTree, so the old-side
        // reproduction stops recursing into the ghost's subtree and the orphan sweep (oldFibers \ newFibers)
        // no longer sees those fibers — they would leak and be re-paired as a zombie on a same-key re-entry.
        private void DisposeExitedGhostFibers(ReconcilerContext.PresenceBoundaryState state, string? key)
        {
            if (!state.ExitAnchors.TryGetValue(key!, out var anchor))
            {
                return;
            }

            state.ExitAnchors.Remove(key!);
            if (anchor == null)
            {
                return;
            }

            // A child drop is infrequent (one per finished exit), so the single-element set alloc is fine.
            _ctx.ComponentRegistry.DisposeFibersUnder(new HashSet<VisualElement> { anchor });
        }

        // Expands one keyed AnimatePresence child (a Motion, or a transparent Provider / Fragment / Memo /
        // Suspense resolving to one) into the parent's slot range via ExpandInlineRecursive,
        // under a render-stable per-child scope. Returns the child's anchor element — the first element it
        // emitted into GeneralCommitState.NewElements — for enter / exit animation, or null on
        // the structural (old-side) walk or when the child emitted nothing.
        private VisualElement? EmitPresenceChild(
            InlineWalk walk,
            VNode? node,
            string? key,
            WalkPosition presencePosition)
        {
            var commit = walk.Commit;
            var startIdx = commit != null ? commit.NewElements.Count : walk.Result!.Count;
            var childPosition = FiberKeying.PresenceChild(presencePosition, key);
            ExpandInlineRecursive(walk, new[] { node }, childPosition);

            if (commit != null && commit.NewElements.Count > startIdx)
            {
                var committed = commit.NewElements[startIdx].element;
                // A z-managed keyed child's first committed element is its PLACEHOLDER — the real content's
                // own layer-container placement is deferred to the post-pass drain (FiberZLayerCoordinator),
                // which has not run yet at this exact synchronous point — every caller of this method
                // dispatches enter/exit animation and PopLayout pinning against the returned anchor, so
                // resolving through the registry here, once, means the REAL element is what actually tweens
                // (and what PinExitingChildOutOfFlow pins) instead of a zero-size proxy that visibly does
                // nothing. This resolves correctly even for a BRAND-NEW mount reached this same synchronous
                // pass: FiberZLayerCoordinator.EnqueueMount (and RelocateFromOrdinarySlot's own deferred
                // branch) register the placeholder->real pair the instant the placeholder is created — well
                // before the drain that alone would place the real element in its layer container — precisely
                // so this lookup never has to fall back to the placeholder for a child this method's own
                // caller is about to explicitly animate.
                return _ctx.ZLayerPlaceholders.TryGetValue(committed, out var real) ? real : committed;
            }
            return null;
        }

        // Wraps EmitPresenceChild with ReconcilerContext.PresenceAnchorMotion bookkeeping: anchorMotion is
        // whichever MotionNode this keyed child's enter/exit is dispatched against (this method's caller's own
        // FindFirstMotionDescendant resolution — null when the child has none, e.g. a plain Div wrapper), and
        // recording it for the exact dynamic extent of the expansion below lets FiberNodeFactory.CreateElement
        // tell that ONE node apart from every OTHER Motion the expansion creates (nested deeper, sitting under
        // a non-anchor wrapper, or a later sibling keyed child) — only the anchor must skip its own standalone
        // enter (this class already plays it explicitly), everything else keeps its normal mount behavior.
        // Saved and RESTORED (not just cleared) around the call so a nested AnimatePresence inside this keyed
        // child's own subtree — which sets its own anchor for ITS keyed children — does not leave the outer
        // anchor cleared once its own expansion returns and this child's subtree keeps unwinding.
        // anchorMotionElement: the anchor Motion's own live element, as recorded by CreateElement /
        // PatchMotion during this very expansion — the target the caller's variant enter/exit classes must
        // land on (the resting variant classes live there, not on a wrapper anchor). Null when the emission
        // did not reach the Motion (or there is none); callers fall back to the classic, anchor-targeted
        // transition then.
        private VisualElement? EmitPresenceChildAsAnchor(
            InlineWalk walk,
            VNode? node,
            MotionNode? anchorMotion,
            string? key,
            WalkPosition presencePosition,
            out VisualElement? anchorMotionElement)
        {
            var previousAnchor = _ctx.PresenceAnchorMotion;
            var previousAnchorElement = _ctx.PresenceAnchorMotionElement;
            _ctx.PresenceAnchorMotion = anchorMotion;
            _ctx.PresenceAnchorMotionElement = null;
            try
            {
                var emitted = EmitPresenceChild(walk, node, key, presencePosition);
                anchorMotionElement = _ctx.PresenceAnchorMotionElement;
                return emitted;
            }
            finally
            {
                _ctx.PresenceAnchorMotion = previousAnchor;
                _ctx.PresenceAnchorMotionElement = previousAnchorElement;
            }
        }

        // Resolves the from/to class arrays for an `initial` variant enter: fromClasses =
        // variants[Initial], toClasses = variants[Animate]. Returns false (no
        // variant-initial enter; caller falls back to the classic transition) unless the Motion sets its own
        // Initial + Animate + Variants and the initial label maps to a non-empty class string. Internal (not
        // private): FiberNodeFactory calls this too, to play the same variant enter on a standalone Motion
        // (outside any AnimatePresence) at element-creation time.
        // transition is what the enter plays on: the TARGET variant's own (variants[Animate]) when it declares
        // one, else the Motion's — the enter's destination pose is what an enter's timing belongs to, the same
        // way the exit resolution below reads variants[Exit] rather than the resting pose it leaves. Null only
        // where neither carries one, which leaves the caller nothing to play.
        internal static bool TryResolveVariantInitial(MotionNode? motion, out string[]? fromClasses,
            out string[]? toClasses, out StyleTransitionConfig? transition)
        {
            fromClasses = null;
            toClasses = null;
            transition = null;
            if (motion?.Initial == null || motion.Animate == null || motion.Variants == null
                || !motion.Variants.TryGetValue(motion.Initial, out var from) || string.IsNullOrEmpty(from.ClassName))
            {
                return false;
            }

            motion.Variants.TryGetValue(motion.Animate, out var to);
            fromClasses = V.ParseClassNames(from.ClassName);
            toClasses = V.ParseClassNames(to.ClassName ?? string.Empty);
            transition = to.Transition ?? motion.Transition;
            return true;
        }

        // PlayPresenceEnter's gate reads this rather than the Motion's own transition because that gate
        // skips the whole enter, OnEnterComplete included, and a Motion's only timing can sit on the pose.
        internal static StyleTransitionConfig? ResolveEnterTransition(MotionNode? motion)
            => TryResolveVariantInitial(motion, out _, out _, out var transition) ? transition : motion?.Transition;

        // The three gates that decide how a removal is treated — ShouldBlockPresenceEnters,
        // CountAnimatedExits and the ghost gate in ExpandPresenceGhostEntry — read this so they agree with
        // the config StartPresenceExit then plays. Disagreeing costs each of them something different,
        // measured on a Motion whose own transition is None beside a timed exit pose: the ghost gate reaps
        // the child before that pose's timing can play at all, the count collapses a reversed stagger to
        // forward order, and Wait mode admits a new child while an exit is still running.
        internal static StyleTransitionConfig? ResolveExitTransition(MotionNode? motion)
            => TryResolveExitVariant(motion, out _, out _, out var transition) ? transition : motion?.Transition;

        // The exit-variant preconditions, in one place so the gates above and TryResolveVariantExit below
        // cannot drift apart on which configurations count as a variant exit.
        private static bool TryResolveExitVariant(MotionNode? motion, out string restingClass,
            out string exitClass, out StyleTransitionConfig? transition)
        {
            restingClass = string.Empty;
            exitClass = string.Empty;
            transition = null;
            if (motion?.Exit == null || motion.Animate == null || motion.Variants == null
                || !motion.Variants.TryGetValue(motion.Exit, out var exit) || string.IsNullOrEmpty(exit.ClassName))
            {
                return false;
            }

            motion.Variants.TryGetValue(motion.Animate, out var resting);
            restingClass = resting.ClassName ?? string.Empty;
            exitClass = exit.ClassName!;
            transition = exit.Transition ?? motion.Transition;
            return true;
        }

        // Builds the exit transition for an `exit` variant: the element animates from its resting
        // variants[Animate] (ExitFromClass) to variants[Exit] (ExitToClass), on the timing
        // ResolveExitTransition resolves, before unmount. Returns null (caller falls back to the classic
        // transition) unless the Motion sets its own Exit + Animate + Variants with a non-empty exit class and
        // a transition resolves for it. The caller
        // supplies the element the swap targets — the Motion's own, so a wrapped Motion's exit variant
        // animates the same element its resting variant classes live on.
        internal static StyleTransitionConfig? TryResolveVariantExit(MotionNode? motion)
        {
            if (!TryResolveExitVariant(motion, out var restingClass, out var exitClass, out var transition)
                || transition == null)
            {
                return null;
            }

            // WithExitClasses copies every timing/spring/per-property-override knob (including Type/Stiffness/
            // Damping/Mass, so a spring-configured Motion's variant EXIT is also spring-driven and hands off to
            // a reversal spring on an exit-cancel instead of silently falling back to a tween) and replaces
            // only the exit class pair — a single source for that knob list instead of hand-copying it here.
            return transition.WithExitClasses(restingClass, exitClass);
        }

        #endregion
    }
}
