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
        private sealed class GeneralCommitState
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
        internal void ReconcileGeneral(
            VisualElement? parent,
            VNode?[] oldNodes,
            VNode?[] newChildren,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode> oldProviders)
        {
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
                var positionCounters = pool.RentPositionCounter();
                var prevFlag = _ctx.ContextValueChanged;
                var newProviderIndex = 0;
                try
                {
                    ExpandInlineRecursive(newChildren, result: null, isNewSide: true, parent, slotStart,
                        oldFibers, newFibers, providers: null, oldProvidersForPairing: oldProviders,
                        positionCounters, ref newProviderIndex, fragmentKeyScope: null, commit);
                }
                finally
                {
                    _ctx.ContextValueChanged = prevFlag;
                    pool.ReturnPositionCounter(positionCounters);
                }

                // Orphan effect cleanups run BEFORE the DOM-removal pass (Finalize → RemoveElement),
                // mirroring the flat path: a deleted FunctionComponent's effect cleanups fire while its
                // Ref.Current is still valid, then the DOM is removed. The sweep (full dispose) runs after.
                RunOrphanEffectCleanups(oldFibers, newFibers);
                if (!_ctx.IsAborted) FinalizeGeneralCommit(commit);
                SweepOrphans(oldFibers, newFibers);
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
                && slotStart + old.index < parent.childCount;
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
                var existingDom = parent.ElementAt(slotStart + old.index);
                if (ReconcileKeying.CanPatch(old.node, node))
                {
                    var actual = _patcher.ResolveWrapped(existingDom);
                    _patcher.PatchNode(actual, old.node, node);
                    if (_ctx.IsAborted) return;
                    // Re-fetch: a WrapElement wrapper swap may change the element reference at this index.
                    existingDom = parent.ElementAt(slotStart + old.index);
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
            // A created container orphan (e.g. V.Div) reconciled its declared children during
            // CreateElement, so a Component child of that container is registered in
            // ComponentRegistry with its MountPoint pointing into the (about-to-be-dropped) orphan
            // subtree — including fibers that were registered by an INNER ReconcileChildren call
            // and therefore are NOT in this scope's `newFibers` set. Dispose every inline fiber
            // whose MountPoint sits inside the orphan range so its effect cleanup runs and the
            // deferred layout-effect drain short-circuits via IsDisposed.
            HashSet<VisualElement>? orphanContainers = null;
            for (var i = preCount; i < commit.NewElements.Count; i++)
            {
                var (element, isExisting) = commit.NewElements[i];
                if (isExisting || element == null) continue;
                if (element is Label or Toggle or Slider or TextField)
                {
                    // Poolable leaves cannot host inline-mounted descendant fibers; skip.
                    continue;
                }
                (orphanContainers ??= new HashSet<VisualElement>()).Add(element);
            }
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
                    _cleaner.RemoveElement(parent, slotStart + i);
                }
            }

            // LIS reorder over the post-removal DOM positions. linearEnd == 0 here (the live-context
            // walk performs no linear prefix pass), so the region begins at slotStart.
            _placement.ComputeAnchorsAndReorder(parent, newElements, slotStart, slotStart,
                oldNodes.Length, newElements.Count);
        }

        #endregion

        #region Inline expansion

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
        // it walks the input tree without pushing context onto the live stack. New-side
        // (isNewSide=true) pushes each Provider's value onto the stack, then —
        // while the value is still pushed — pairs against the corresponding old Provider via
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
            List<ContextProviderNode>? providers = null,
            List<ContextProviderNode>? oldProvidersForPairing = null)
        {
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
            var positionCounters = _ctx.BufferPool.RentPositionCounter();
            var prevFlag = _ctx.ContextValueChanged;
            var newProviderIndex = 0;
            try
            {
                ExpandInlineRecursive(nodes, buffer, isNewSide, parent, slotStart, oldFibers, newFibers, providers, oldProvidersForPairing, positionCounters, ref newProviderIndex, fragmentKeyScope: null);
                return buffer.Count == 0 ? Array.Empty<VNode>() : buffer.ToArray();
            }
            finally
            {
                if (isNewSide) _ctx.ContextValueChanged = prevFlag;
                _ctx.BufferPool.ReturnNodeList(buffer);
                _ctx.BufferPool.ReturnPositionCounter(positionCounters);
            }
        }

        private void ExpandInlineRecursive(
            VNode?[] nodes,
            List<VNode>? result,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode>? providers,
            List<ContextProviderNode>? oldProvidersForPairing,
            Dictionary<object, int> positionCounters,
            ref int newProviderIndex,
            string? fragmentKeyScope,
            GeneralCommitState? commit = null)
        {
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
                            var childScope = FiberKeying.FragmentChildScope(
                                fragmentKeyScope, fragment.Key, nodeIndex);
                            ExpandInlineRecursive(fragment.Children, result, isNewSide, parent, slotStart, oldFibers, newFibers, providers, oldProvidersForPairing, positionCounters, ref newProviderIndex, childScope, commit);
                        }
                        break;
                    case ContextProviderNode provider when !isNewSide:
                        providers?.Add(provider);
                        if (provider.Children != null)
                        {
                            var providerScope = FiberKeying.ProviderChildScope(
                                fragmentKeyScope, provider.Key, nodeIndex);
                            ExpandInlineRecursive(provider.Children, result, isNewSide: false, parent, slotStart, oldFibers, newFibers, providers, null, positionCounters, ref newProviderIndex, providerScope, commit);
                        }
                        break;
                    case ContextProviderNode provider:
                        // New side: push value first so the notification snapshot includes it.
                        provider.PushContext(_ctx.ComponentContextStack);
                        try
                        {
                            var pairIndex = newProviderIndex++;
                            if (oldProvidersForPairing != null
                                && pairIndex < oldProvidersForPairing.Count
                                && provider.HasValueChanged(oldProvidersForPairing[pairIndex]))
                            {
                                NotifyContextValueChange(provider);
                            }
                            if (provider.Children != null)
                            {
                                var providerScope = FiberKeying.ProviderChildScope(
                                    fragmentKeyScope, provider.Key, nodeIndex);
                                ExpandInlineRecursive(provider.Children, result, isNewSide: true, parent, slotStart, oldFibers, newFibers, providers: null, oldProvidersForPairing, positionCounters, ref newProviderIndex, providerScope, commit);
                            }
                        }
                        finally
                        {
                            provider.PopContext(_ctx.ComponentContextStack);
                        }
                        break;
                    case ComponentNode component:
                    {
                        // Error-boundary behavior: once a sibling earlier in this
                        // expansion has aborted via TryCatch.SetAborted, subsequent inline
                        // ComponentNode mounts must not run their Body — otherwise their fiber
                        // becomes registered with the new key but state never bound to the user
                        // tree, blocking proper re-mount on the next normal render.
                        if (_ctx.IsAborted) break;
                        var identity = component.ResolvedIdentity;
                        var slotKey = component.Key ?? FiberKeying.ResolveInlinePositionKey(positionCounters, identity, _ctx.ComponentRegistry.InlinePositionKeyBoxes);
                        // When recursing into fiber.PreviousTree, the descendants' slotKeys must be
                        // scoped to THIS fiber's body output — not shared with the surrounding
                        // siblings' position counters. Otherwise the same descendant would compute
                        // different slotKeys when the enclosing fiber re-renders independently
                        // (setState) vs when its outer parent re-renders (where preceding siblings
                        // share the position scope and shift the counters). A registry lookup
                        // mismatch would dispose the descendant fiber and reset its state.
                        //
                        // FiberStack.Push around the recursion is required so that nested inline
                        // ComponentNodes encountered while walking fiber.PreviousTree are appended
                        // as children of THIS fiber, not the outer caller's current fiber. Without
                        // it, a Parent → Child component chain would link Child.Parent to the outer
                        // root fiber (the caller's Current), bypassing the Parent fiber entirely and
                        // breaking ErrorBoundary search / context propagation walks. The same invariant
                        // applies here: a fiber stays the current work-in-progress
                        // while its children are created from its body output.
                        if (isNewSide)
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
                            var currentSlotStart = slotStart + emittedCount;
                            // Two same-identity siblings sharing one explicit key resolve to the
                            // SAME registry fiber; expanding it once per sibling would emit one
                            // component's DOM twice while its slot bookkeeping tracks only the last
                            // position (with hook state shared across both copies). Mirror the
                            // leaf-level duplicate guard: warn and skip the repeat before
                            // GetOrCreate can clobber the first occurrence's slot.
                            var priorFiber = _ctx.ComponentRegistry.TryGetFiberForInlineKey(parentFiber, slotKey, identity);
                            if (priorFiber != null && newFibers.Contains(priorFiber))
                            {
                                FiberLogger.LogWarning("GeneralPathReconciler",
                                    $"Duplicate component key detected among siblings: '{slotKey}'. " +
                                    "The repeated sibling is skipped; give each sibling a unique key.");
                                break;
                            }
                            var fiber = _ctx.ComponentRegistry.GetOrCreateInline(
                                component, parentFiber, slotKey, parent, currentSlotStart);
                            newFibers.Add(fiber);
                            // FiberStack.Push keeps nested inline components linked under THIS fiber
                            // (a fiber stays the current work-in-progress while its
                            // children are created from its body output).
                            var preCount = emittedCount;
                            if (fiber.PreviousTree != null && fiber.PreviousTree.Length > 0)
                            {
                                var childCounters = _ctx.BufferPool.RentPositionCounter();
                                _ctx.FiberStack.Push(fiber);
                                try
                                {
                                    var componentScope = FiberKeying.ComponentChildScope(
                                        fragmentKeyScope, component.Key, nodeIndex);
                                    ExpandInlineRecursive(fiber.PreviousTree, result, isNewSide: true, parent, slotStart, oldFibers, newFibers, providers: null, oldProvidersForPairing, childCounters, ref newProviderIndex, componentScope, commit);
                                }
                                finally
                                {
                                    _ctx.FiberStack.Pop();
                                    _ctx.BufferPool.ReturnPositionCounter(childCounters);
                                }
                            }
                            fiber.MountSlotCount = (commit != null ? commit.NewElements.Count : result!.Count) - preCount;
                        }
                        else
                        {
                            // Old-side (structural) walk: look up the previously rendered fiber by the
                            // same tree-position key the new side registered under — (parent fiber,
                            // position key, identity). FiberStack.Push mirrors the new side so nested
                            // old-side components resolve against the same parent fiber they were
                            // registered with; without the symmetric push the lookup parent would
                            // diverge and the diff would treat reused fibers as orphans.
                            var fiber = _ctx.ComponentRegistry.TryGetFiberForInlineKey(_ctx.FiberStack.Current, slotKey, identity);
                            if (fiber != null)
                            {
                                if (fiber.PreviousTree != null && fiber.PreviousTree.Length > 0)
                                {
                                    var childCounters = _ctx.BufferPool.RentPositionCounter();
                                    _ctx.FiberStack.Push(fiber);
                                    try
                                    {
                                        var componentScope = FiberKeying.ComponentChildScope(
                                            fragmentKeyScope, component.Key, nodeIndex);
                                        ExpandInlineRecursive(fiber.PreviousTree, result, isNewSide: false, parent, slotStart, oldFibers, newFibers, providers, null, childCounters, ref newProviderIndex, componentScope, commit);
                                    }
                                    finally
                                    {
                                        _ctx.FiberStack.Pop();
                                        _ctx.BufferPool.ReturnPositionCounter(childCounters);
                                    }
                                }
                                // Post-order add: a directly-nested component must precede its
                                // parent in oldFibers so the orphan sweep's forward walk tears the
                                // subtree down bottom-up — a descendant's effect cleanups complete
                                // before an ancestor's, matching the commit-phase deletion order.
                                oldFibers.Add(fiber);
                            }
                        }
                        break;
                    }
                    case OutletNode:
                        // Wrapper-emitting node: CreateElement(Outlet) / PatchNode(Outlet) resolve the
                        // matched route during this walk's commit (live context), reading
                        // RouterContext.Location / Depth from the live stack and pushing Depth+1 around
                        // the route Component's mount. No pre-captured snapshot / owner is needed.
                        _keying.RegisterScopedKey(node, fragmentKeyScope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                    case MemoNode memo:
                        // Memo emits no DOM: resolve its inner via the dep cache and
                        // expand it inline so a Suspense / Component / Provider inner is handled
                        // wrapper-less in the parent's slot range. The inner renders in live context
                        // (the enclosing Provider is still pushed on the new side), so no pre-captured
                        // snapshot is needed.
                        ExpandMemoInline(memo, result, isNewSide, parent, slotStart, oldFibers,
                            newFibers, providers, oldProvidersForPairing, ref newProviderIndex,
                            fragmentKeyScope, nodeIndex, commit);
                        break;
                    case SuspenseNode suspense:
                        ExpandSuspenseInline(suspense, result, isNewSide, parent, slotStart, oldFibers,
                            newFibers, providers, oldProvidersForPairing, ref newProviderIndex,
                            fragmentKeyScope, nodeIndex, commit);
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
                            ExpandAnimatePresenceInline(presence, result, isNewSide, parent, slotStart,
                                oldFibers, newFibers, providers, oldProvidersForPairing, ref newProviderIndex,
                                fragmentKeyScope, nodeIndex, commit);
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
                        _keying.RegisterScopedKey(node, fragmentKeyScope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                    default:
                        _keying.RegisterScopedKey(node, fragmentKeyScope, nodeIndex);
                        Emit(node, result, commit);
                        break;
                }
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
            MemoNode memo,
            List<VNode>? result,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode>? providers,
            List<ContextProviderNode>? oldProvidersForPairing,
            ref int newProviderIndex,
            string? fragmentKeyScope,
            int nodeIndex,
            GeneralCommitState? commit = null)
        {
            var memoScope = FiberKeying.MemoScope(fragmentKeyScope, nodeIndex);
            var cacheKey = FiberKeying.MemoCacheKey(memo.Key, memoScope);
            var inner = _ctx.FiberMemoCache.GetOrCompute(cacheKey, memo);
            if (inner == null) return;
            var counters = _ctx.BufferPool.RentPositionCounter();
            try
            {
                // Recurse under this memo's own scope so a nested Memo's position key (or an inner
                // Component's slot key) cannot collide with this memo's scope — e.g. an outer and an
                // inner unkeyed Memo both at node index 0 would otherwise share cacheKey "{scope}/m0".
                ExpandInlineRecursive(new[] { inner }, result, isNewSide, parent, slotStart,
                    oldFibers, newFibers, providers, oldProvidersForPairing, counters,
                    ref newProviderIndex, memoScope, commit);
            }
            finally
            {
                _ctx.BufferPool.ReturnPositionCounter(counters);
            }
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

        // Wrapper-less Suspense expansion. The Suspense emits no container
        // VisualElement: its children are expanded inline into result so they sit
        // directly in the parent's slot range and, on the new side, render in-scope of any enclosing
        // Provider (no pre-captured snapshot needed). The fiber rendering this Suspense
        // (FiberStack.Current) becomes the boundary so a descendant's
        // FiberSuspendSignal routes here via FindNearestSuspenseBoundary. If a
        // descendant suspends during the new-side render, the partial primary output is discarded (the
        // partially-mounted fibers stay registered so a later resolve re-render reuses them with their
        // state) and the fallback subtree is expanded instead. The children-vs-fallback decision is
        // recorded in ReconcilerContext.SuspenseFallbackShown keyed by (boundary,
        // position) so the old-side structural walk reproduces the committed subtree for the diff.
        // Primary and fallback children use distinct fragment scopes so their fibers never collide.
        private void ExpandSuspenseInline(
            SuspenseNode suspense,
            List<VNode>? result,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode>? providers,
            List<ContextProviderNode>? oldProvidersForPairing,
            ref int newProviderIndex,
            string? fragmentKeyScope,
            int nodeIndex,
            GeneralCommitState? commit = null)
        {
            var boundaryFiber = _ctx.FiberStack.Current;
            var suspenseKey = FiberKeying.SuspenseKey(fragmentKeyScope, suspense.Key, nodeIndex);
            var primaryScope = FiberKeying.SuspenseSubtreeScope(suspenseKey, isFallback: false);
            var fallbackScope = FiberKeying.SuspenseSubtreeScope(suspenseKey, isFallback: true);
            var stateKey = (boundaryFiber, suspenseKey);

            if (isNewSide)
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
                        var counters = _ctx.BufferPool.RentPositionCounter();
                        try
                        {
                            ExpandInlineRecursive(suspense.Children, result, isNewSide: true, parent, slotStart,
                                oldFibers, newFibers, providers: null, oldProvidersForPairing, counters,
                                ref newProviderIndex, primaryScope, commit);
                        }
                        catch (FiberSuspendSignal)
                        {
                            suspended = true;
                        }
                        finally
                        {
                            _ctx.BufferPool.ReturnPositionCounter(counters);
                        }
                    }
                    // A reused (bailed-out) child does not re-throw FiberSuspendSignal, so an unrelated
                    // parent re-render would otherwise reveal an empty primary while a descendant is still
                    // loading. If any of THIS Suspense's primary children still holds a pending async
                    // resource, keep the fallback. Scope the scan to the children added during this
                    // expansion (not the whole boundary subtree) so an async sibling outside the Suspense
                    // does not keep the boundary suspended (suspension is scoped to the boundary subtree).
                    if (!suspended)
                    {
                        foreach (var f in newFibers)
                        {
                            if (fibersBefore.Contains(f)) continue;
                            // A nested Suspense boundary owns its own descendants' suspension, so its
                            // pending primary must not keep THIS (outer) boundary suspended.
                            // Skip nested boundary fibers and any fiber whose nearest boundary
                            // is a nested one. The delta already contains every fiber in this Suspense's
                            // primary subtree, so a per-fiber own-slot check covers descendants without
                            // re-walking (and without crossing nested boundaries).
                            if (f.IsSuspenseBoundary) continue;
                            var nested = ComponentBoundarySearch.FindNearestSuspenseBoundary(f);
                            if (nested != null && !ReferenceEquals(nested, boundaryFiber)) continue;
                            if (ComponentBoundarySearch.HasPendingAsyncSlot(f))
                            {
                                suspended = true;
                                break;
                            }
                        }
                    }
                    // Mark THIS Suspense's primary children (the fibers added during the children
                    // expansion) as offscreen iff suspended. The offscreen guard in FlushState defers
                    // their lane flush while suspended (their slot is occupied by the fallback), but the
                    // fallback subtree — expanded below and never marked — remains flushable (the
                    // fallback renders normally; only the primary subtree is offscreen).
                    foreach (var f in newFibers)
                    {
                        if (!fibersBefore.Contains(f)) f.IsOffscreen = suspended;
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
                            var fbCounters = _ctx.BufferPool.RentPositionCounter();
                            try
                            {
                                ExpandInlineRecursive(new[] { suspense.Fallback }, result, isNewSide: true, parent,
                                    slotStart, oldFibers, newFibers, providers: null, oldProvidersForPairing,
                                    fbCounters, ref newProviderIndex, fallbackScope, commit);
                            }
                            finally
                            {
                                _ctx.BufferPool.ReturnPositionCounter(fbCounters);
                            }
                        }
                    }
                }
                finally
                {
                    _ctx.BufferPool.ReturnFiberSet(fibersBefore);
                }
                _ctx.SuspenseFallbackShown[stateKey] = suspended;
                if (boundaryFiber != null)
                {
                    // Mark/unmark the boundary as suspended so FlushState defers an offscreen descendant's
                    // lane flush to this boundary's re-render (which commits the reveal in one pass).
                    if (suspended) _ctx.SuspendedBoundaries.Add(boundaryFiber);
                    else _ctx.SuspendedBoundaries.Remove(boundaryFiber);
                }
            }
            else
            {
                var wasFallback = _ctx.SuspenseFallbackShown.TryGetValue(stateKey, out var shown) && shown;
                var nodesToExpand = wasFallback
                    ? (suspense.Fallback != null ? new[] { suspense.Fallback } : Array.Empty<VNode>())
                    : (suspense.Children ?? Array.Empty<VNode>());
                if (nodesToExpand.Length > 0)
                {
                    var counters = _ctx.BufferPool.RentPositionCounter();
                    try
                    {
                        ExpandInlineRecursive(nodesToExpand, result, isNewSide: false, parent, slotStart,
                            oldFibers, newFibers, providers, null, counters, ref newProviderIndex,
                            wasFallback ? fallbackScope : primaryScope, commit);
                    }
                    finally
                    {
                        _ctx.BufferPool.ReturnPositionCounter(counters);
                    }
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
        private void ExpandAnimatePresenceInline(
            AnimatePresenceNode presence,
            List<VNode>? result,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode>? providers,
            List<ContextProviderNode>? oldProvidersForPairing,
            ref int newProviderIndex,
            string? fragmentKeyScope,
            int nodeIndex,
            GeneralCommitState? commit)
        {
            var boundaryFiber = _ctx.FiberStack.Current;
            var presenceKey = FiberKeying.PresenceKey(fragmentKeyScope, presence.Key, nodeIndex);
            // Parent is part of the key so an AnimatePresence nested inside a real element does not collide
            // with an outer one at the same (fiber, scope, index). See ReconcilerContext.PresenceStates.
            var stateKey = (boundaryFiber, parent, presenceKey);

            if (!isNewSide)
            {
                // Old side: reproduce the committed leaf composition (including exiting ghosts) so the
                // diff's old leaves match the live DOM. No state mutation, no animation.
                if (_ctx.PresenceStates.TryGetValue(stateKey, out var oldState))
                {
                    foreach (var (key, node) in oldState.Committed)
                    {
                        EmitPresenceChild(node, key, presenceKey, result, isNewSide: false, parent, slotStart,
                            oldFibers, newFibers, providers, oldProvidersForPairing, ref newProviderIndex, commit);
                    }
                }
                return;
            }

            var firstRender = !_ctx.PresenceStates.TryGetValue(stateKey, out var state);
            if (firstRender)
            {
                state = new ReconcilerContext.PresenceBoundaryState();
                _ctx.PresenceStates[stateKey] = state;
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

                // mode="wait": while any previously-committed child is still exiting, hold back
                // brand-new keys so the exit fully completes before the new child mounts / enters. The exit's
                // completion already re-renders the boundary; on that render no ghost remains, so the withheld
                // child emits and enters. Returning ghosts (a key re-added mid-exit) and persisting children are
                // never withheld — only keys absent from the committed set.
                var blockEnters = false;
                if (presence.Mode == AnimatePresenceMode.Wait)
                {
                    foreach (var (key, node) in prevCommitted)
                    {
                        if (newKeySet.Contains(key)) continue;
                        if (state.ExitComplete.Contains(key)) continue;
                        var ghostMotion = FiberNodeFactory.FindFirstMotionDescendant(node);
                        if (ghostMotion?.Transition is { DurationSec: > 0f })
                        {
                            blockEnters = true;
                            break;
                        }
                    }
                }

                // Committed emission order: the current children in new order, with each previously
                // committed key now absent spliced back at the index it held among its previous
                // siblings. An exiting child must hold its slot among unchanged neighbors for the
                // whole exit (only a popLayout-style mode pulls it out of flow); appending ghosts
                // after every current child instead yanked a non-last exiting item behind its later
                // siblings — and physically reordered the DOM — the instant its exit began.
                // Finished / instant-removed ghosts are dropped during the walk below.
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

                // Count the children that will actually animate-exit this render, so staggerDirection can sweep
                // last-to-first over them (a no-op for the default forward direction, but needed to reverse).
                var exitCount = 0;
                foreach (var (key, node) in prevCommitted)
                {
                    if (newKeySet.Contains(key) || state.ExitComplete.Contains(key)) continue;
                    if (FiberNodeFactory.FindFirstMotionDescendant(node)?.Transition is { DurationSec: > 0f }) exitCount++;
                }

                var visualIndex = 0;
                var exitIndex = 0;
                var removedInstantThisRender = false;
                foreach (var (key, node) in plan)
                {
                    if (!newKeySet.Contains(key))
                    {
                        // Ghost: a previously-committed key absent from the new children, spliced into
                        // the plan at its old position. A finished exit is dropped (not emitted → the
                        // diff removes its leaves); a child without an exit animation is removed
                        // immediately; otherwise the child stays mounted in its old slot and its exit
                        // is started once.
                        if (state.ExitComplete.Contains(key))
                        {
                            state.Exiting.Remove(key);
                            // Once the exit detached the ghost's element, the old-side reproduction can no longer
                            // recurse into the ghost's subtree (the Motion's PreviousTree was cleared), so its
                            // inline fibers escape the orphan sweep. Dispose them explicitly via the tracked anchor
                            // before the diff removes the leaves — otherwise a same-key re-entry would re-pair the
                            // undisposed fiber as a zombie whose local state updates no longer re-render.
                            DisposeExitedGhostFibers(state, key);
                            continue;
                        }

                        var ghostMotionNode = FiberNodeFactory.FindFirstMotionDescendant(node);
                        var ghostTransition = ghostMotionNode?.Transition;
                        if (ghostTransition is not { DurationSec: > 0f })
                        {
                            // No exit animation → immediate removal (skip emitting; the diff reaps the leaves).
                            state.Exiting.Remove(key);
                            removedInstantThisRender = true;
                            continue;
                        }

                        var ghostAnchor = EmitPresenceChild(node, key, presenceKey, result, isNewSide: true, parent, slotStart,
                            oldFibers, newFibers, providers, oldProvidersForPairing, ref newProviderIndex, commit);

                        // Track the live ghost anchor so the drop path (exit complete) can dispose the subtree
                        // fibers under it — see DisposeExitedGhostFibers.
                        if (commit != null && ghostAnchor != null) state.ExitAnchors[key] = ghostAnchor;

                        if (commit != null && ghostAnchor != null && state.Exiting.Add(key))
                        {
                            _ctx.StyleAnimationScheduler.CancelEnter(ghostAnchor);
                            if (presence.Mode == AnimatePresenceMode.PopLayout)
                            {
                                PinExitingChildOutOfFlow(ghostAnchor);
                            }
                            var capturedKey = key;
                            var capturedState = state;
                            var capturedBoundary = boundaryFiber;
                            var capturedOnExitComplete = presence.OnExitComplete;
                            // `exit`: when the direct Motion child declares an exit variant label, animate from the
                            // resting variants[animate] to variants[exit]; otherwise use the transition's ExitFrom/ExitTo.
                            var variantExit = TryResolveVariantExit(node, ghostMotionNode);
                            var exitTransition = variantExit ?? ghostTransition;
                            // For a variant exit the From classes ARE the resting variants[animate]; if this exit is
                            // cancelled (the key is re-added before it finishes) the element must return to that resting
                            // variant rather than be left without it (interrupt handling).
                            _ctx.StyleAnimationScheduler.PlayExit(ghostAnchor, exitTransition, () =>
                            {
                                if (_ctx.IsDisposed) return;
                                // Reconcile-driven removal: flag the finished exit and re-render the boundary;
                                // the next render stops emitting this child and the diff removes its leaves.
                                capturedState.Exiting.Remove(capturedKey);
                                capturedState.ExitComplete.Add(capturedKey);
                                // onExitComplete fires once the exiting set drains (the last
                                // in-flight exit finished). Cancelled exits (key re-entered) remove from Exiting
                                // elsewhere and do not reach here, so they never trigger it.
                                if (capturedState.Exiting.Count == 0)
                                {
                                    capturedOnExitComplete?.Invoke();
                                }
                                if (capturedBoundary != null)
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
                            }, restoreFromOnCancel: variantExit != null,
                                additionalDelaySec: presence.StaggerDelaySec(exitIndex, exitCount));
                            exitIndex++;
                        }

                        nextCommitted.Add((key, node));
                        continue;
                    }

                    // Withhold a brand-new child under mode="wait" while exits are in flight (see above). The
                    // linear prevCommitted scan is bounded: this only runs when blockEnters is set, and wait-mode
                    // targets single-child swaps, so prevCommitted holds ~1 entry.
                    if (blockEnters && !PresenceContainsKey(prevCommitted, key))
                    {
                        continue;
                    }

                    var anchor = EmitPresenceChild(node, key, presenceKey, result, isNewSide: true, parent, slotStart,
                        oldFibers, newFibers, providers, oldProvidersForPairing, ref newProviderIndex, commit);

                    if (commit != null && anchor != null)
                    {
                        var wasExiting = state.Exiting.Remove(key);
                        var wasExitComplete = state.ExitComplete.Remove(key);
                        // The key is present again. If a completed exit's ghost was still awaiting its drop and the
                        // re-entry mounted a FRESH element (the detached ghost can't be reproduced, so the new
                        // anchor differs), its inline fibers escaped the orphan sweep exactly as on the normal drop
                        // path — dispose them via the tracked anchor before re-pairing, else this same-render
                        // re-entry resurrects them as a zombie. A cancel-exit instead reproduces the SAME still-
                        // attached element (anchor == the stale one), so just drop the now-current reference.
                        if (state.ExitAnchors.TryGetValue(key, out var staleAnchor) && !ReferenceEquals(staleAnchor, anchor))
                        {
                            DisposeExitedGhostFibers(state, key);
                        }
                        else
                        {
                            state.ExitAnchors.Remove(key!);
                        }
                        if (wasExiting)
                        {
                            _ctx.StyleAnimationScheduler.CancelExit(anchor);
                            if (presence.Mode == AnimatePresenceMode.PopLayout)
                            {
                                RestorePopLayoutChildToFlow(anchor);
                            }
                        }

                        var isEnter = wasExiting || wasExitComplete || !PresenceContainsKey(prevCommitted, key);
                        if (isEnter)
                        {
                            var motion = FiberNodeFactory.FindFirstMotionDescendant(node);
                            if (motion?.Transition != null)
                            {
                                // The Initial flag only suppresses the enter animation on the AnimatePresence's
                                // very first mount; later additions always animate.
                                if (!firstRender || presence.Initial)
                                {
                                    // A variant Motion (direct child carrying variants + animate) manages its resting
                                    // state through variant classes: variants[animate] is applied at mount and restored
                                    // by CancelExit on an exit-cancel. So it only ever plays a VARIANT enter (when an
                                    // `initial` label is declared) and must NOT fall through to the classic preset
                                    // enter — the default StyleTransition.Fade would replay a fade-in on top of the
                                    // resting variant on every add / interrupt.
                                    var isVariantMotion = ReferenceEquals(node, motion)
                                        && motion.Variants != null && motion.Animate != null;
                                    // A cancelled exit reproduces the SAME still-attached element —
                                    // not a first mount — so `initial` does not reapply: CancelExit
                                    // already reverses the element toward its resting variant with
                                    // the transition kept alive, and replaying initial→animate here
                                    // would re-seed the declared initial pose (a jump) and restart
                                    // the full enter duration from it.
                                    if (isVariantMotion && !wasExiting
                                        && TryResolveVariantInitial(motion, out var fromClasses, out var toClasses))
                                    {
                                        // `initial`: enter from variants[initial] to variants[animate]
                                        // (kept as the persistent resting state).
                                        var t = motion.Transition;
                                        _ctx.StyleAnimationScheduler.PlayVariantEnter(anchor, fromClasses, toClasses,
                                            t.DurationSec, t.Easing, t.DelaySec,
                                            motion.OnEnterComplete, presence.StaggerDelaySec(visualIndex, newKeyed.Count),
                                            propertyOverrides: t.PropertyOverrides);
                                    }
                                    else if (isVariantMotion)
                                    {
                                        // Variant Motion without `initial`: rest at variants[animate], no enter anim.
                                        motion.OnEnterComplete?.Invoke();
                                    }
                                    else
                                    {
                                        _ctx.StyleAnimationScheduler.PlayEnter(anchor, motion.Transition,
                                            motion.OnEnterComplete, presence.StaggerDelaySec(visualIndex, newKeyed.Count));
                                    }
                                }
                                else
                                {
                                    motion.OnEnterComplete?.Invoke();
                                }
                            }
                        }
                    }

                    nextCommitted.Add((key, node));
                    visualIndex++;
                }

                // onExitComplete fires once the exiting children are gone. When every removed child
                // had NO exit animation (all instant-removed above) no PlayExit callback runs to fire it, so fire it
                // here — but only when no animated exit is still in flight (those fire it when the Exiting set drains).
                if (commit != null && removedInstantThisRender && state.Exiting.Count == 0)
                {
                    presence.OnExitComplete?.Invoke();
                }

                // 3) Commit the new composition for the next old-side reproduction. Exit-complete keys were
                //    not re-emitted this render (their leaves are being removed), so drop them.
                state.Committed.Clear();
                foreach (var entry in nextCommitted) state.Committed.Add(entry);
                state.ExitComplete.Clear();
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

        private static bool PresenceContainsKey(
            List<(string key, VNode node)> list, string? key)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].key == key) return true;
            }
            return false;
        }

        // AnimatePresenceMode.PopLayout: the instant a child's exit starts, pull it out of layout flow and pin
        // it via absolute positioning at the last rect Yoga resolved for it in-flow (anchor.layout is parent-
        // relative), so still-present siblings reflow into its place immediately while the exit animation
        // finishes on top. Skipped when any component is non-finite (an EditMode pass with no forced layout
        // leaves `.layout` at NaN) — the child then degrades to a normal in-flow exit rather than being pinned
        // to garbage coordinates.
        private static void PinExitingChildOutOfFlow(VisualElement anchor)
        {
            var rect = anchor.layout;
            if (!float.IsFinite(rect.x) || !float.IsFinite(rect.y)
                || !float.IsFinite(rect.width) || !float.IsFinite(rect.height))
            {
                return;
            }

            anchor.style.position = Position.Absolute;
            anchor.style.left = rect.x;
            anchor.style.top = rect.y;
            anchor.style.width = rect.width;
            anchor.style.height = rect.height;
        }

        // Reverses PinExitingChildOutOfFlow when a PopLayout exit is cancelled (its key re-added before the
        // exit finished): clears the same five inline styles back to StyleKeyword.Null so the child rejoins
        // its parent's normal layout flow. A no-op (harmless) when the exit was never pinned (non-finite
        // layout at exit-start), since clearing an already-null style is idempotent.
        private static void RestorePopLayoutChildToFlow(VisualElement anchor)
        {
            anchor.style.position = StyleKeyword.Null;
            anchor.style.left = StyleKeyword.Null;
            anchor.style.top = StyleKeyword.Null;
            anchor.style.width = StyleKeyword.Null;
            anchor.style.height = StyleKeyword.Null;
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
            VNode? node,
            string? key,
            string? presenceScope,
            List<VNode>? result,
            bool isNewSide,
            VisualElement? parent,
            int slotStart,
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            List<ContextProviderNode>? providers,
            List<ContextProviderNode>? oldProvidersForPairing,
            ref int newProviderIndex,
            GeneralCommitState? commit)
        {
            var startIdx = commit != null ? commit.NewElements.Count : result!.Count;
            var childScope = FiberKeying.PresenceChildScope(presenceScope, key);
            var counters = _ctx.BufferPool.RentPositionCounter();
            try
            {
                ExpandInlineRecursive(new[] { node }, result, isNewSide, parent, slotStart,
                    oldFibers, newFibers, providers, oldProvidersForPairing, counters,
                    ref newProviderIndex, childScope, commit);
            }
            finally
            {
                _ctx.BufferPool.ReturnPositionCounter(counters);
            }

            if (commit != null && commit.NewElements.Count > startIdx)
            {
                return commit.NewElements[startIdx].element;
            }
            return null;
        }

        // Resolves the from/to class arrays for an `initial` variant enter: fromClasses =
        // variants[Initial], toClasses = variants[Animate]. Returns false (no
        // variant-initial enter; caller falls back to the classic transition) unless the Motion sets its own
        // Initial + Animate + Variants and the initial label maps to a non-empty class string. Internal (not
        // private): FiberNodeFactory calls this too, to play the same variant enter on a standalone Motion
        // (outside any AnimatePresence) at element-creation time.
        internal static bool TryResolveVariantInitial(MotionNode? motion, out string[]? fromClasses, out string[]? toClasses)
        {
            fromClasses = null;
            toClasses = null;
            if (motion?.Initial == null || motion.Animate == null || motion.Variants == null
                || !motion.Variants.TryGetValue(motion.Initial, out var fromClass) || string.IsNullOrEmpty(fromClass))
            {
                return false;
            }

            motion.Variants.TryGetValue(motion.Animate, out var toClass);
            fromClasses = V.ParseClassNames(fromClass);
            toClasses = V.ParseClassNames(toClass ?? string.Empty);
            return true;
        }

        // Builds the exit transition for an `exit` variant: the element animates from its resting
        // variants[Animate] (ExitFromClass) to variants[Exit] (ExitToClass), keeping this Motion's
        // transition timing, before unmount. Returns null (caller falls back to the classic transition) unless this
        // is the direct Motion child (node == motion, so anchor IS its element) and it sets its own
        // Exit + Animate + Variants with a non-empty exit class.
        internal static StyleTransitionConfig? TryResolveVariantExit(VNode? node, MotionNode? motion)
        {
            if (!ReferenceEquals(node, motion) || motion?.Exit == null || motion.Animate == null || motion.Variants == null
                || !motion.Variants.TryGetValue(motion.Exit, out var exitClass) || string.IsNullOrEmpty(exitClass))
            {
                return null;
            }

            motion.Variants.TryGetValue(motion.Animate, out var restingClass);
            var timing = motion.Transition!;
            return new StyleTransitionConfig
            {
                ExitFromClass = restingClass ?? string.Empty,
                ExitToClass = exitClass,
                DurationSec = timing.DurationSec,
                Easing = timing.Easing,
                ExitEasing = timing.ExitEasing,
                DelaySec = timing.DelaySec,
                PropertyOverrides = timing.PropertyOverrides,
            };
        }

        #endregion
    }
}
