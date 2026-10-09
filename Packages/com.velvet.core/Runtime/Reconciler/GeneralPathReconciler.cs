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
            // The key each OldNodes entry was emitted under, index-aligned with it and owned by the fiber
            // OldOwners names from the map build on.
            public List<ChildKey> OldKeys = null!;
            public Dictionary<ChildKey, (int index, VNode? node)> OldKeyMap = null!;
            // The old leaves a new leaf has taken, patched or replaced, by index: two leaves sharing a key can
            // both be taken. FinalizeGeneralCommit narrows it to the ones patched.
            public HashSet<int> UsedOldIndices = null!;
            public HashSet<ChildKey> NewKeys = null!;
            public List<(VisualElement? element, bool isExisting)> NewElements = null!;
            // The fiber whose output emitted each OldNodes entry, index-aligned with it.
            public List<ComponentFiber?> OldOwners = null!;
            // One entry per NewElements entry (parallel list), truncated with it when a speculative subtree
            // (Suspense primary) rolls back: the old leaf it took (-1 for none), and whether it and every entry
            // before it took the old leaf at their own index.
            public List<CommittedLeaf> Committed = null!;
            // The component fibers whose expansion this walk completed, each with the NewElements index its
            // rows begin at and how many it emitted. FinalizeGeneralCommit writes them onto the fiber where it
            // places the rows. RollbackCommitTo leaves the entries of a suspended primary's fibers in place, so
            // those fibers are written the rows they had before the rollback.
            public List<(ComponentFiber Fiber, int FirstRow, int Rows)> Placements = null!;
            // The fibers of the component nodes this walk met after an abort and did not render.
            public HashSet<ComponentFiber>? SkippedByAbort;
            // The fibers a Suspense of this walk hid (true) or revealed (false), applied once FinalizeGeneralCommit
            // has placed what the walk emitted: React disconnects and reconnects them in its commit, and a walk
            // that rolls back or stops leaves on screen what was there.
            public List<(ComponentFiber Fiber, bool Hidden)>? OffscreenChanges;
            // The primary elements a Suspense of this walk hid (true) or revealed (false), each with its NewElements
            // row, applied with OffscreenChanges and on the same terms. RollbackCommitTo drops those of the rows it
            // takes back, whose elements can be in the pool by the time this is applied.
            public List<(int Row, VisualElement Element, bool Hidden)>? DisplayChanges;
            // The record each Suspense of this walk replaced, oldest first, put back where the walk throws.
            public List<(ComponentFiber? Boundary, VisualElement? Container, VisualElement? PortalScope, long Position,
                ReconcilerContext.SuspenseFallbackRecord? Before)>? RecordsBefore;
            // The offscreen state each fiber a Suspense of this walk hid or revealed had before, oldest first, put
            // back where the walk throws: the next walk reads it to decide which fibers it hides or reveals, and
            // OffscreenChanges, which would have hidden or shown their effects to match, is never applied.
            public List<(ComponentFiber Fiber, bool IsOffscreen, VisualElement? HiddenUnder,
                ComponentFiber? OffscreenUnder)>? OffscreenBefore;
            // Every old-leaf lookup of this walk goes through it; LogicalSlotCursor owns what it saves and when
            // it falls back.
            public LogicalSlotCursor OldSlots;
        }

        internal readonly record struct CommittedLeaf(int OldIndex, bool Linear, ChildKey Key);

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
            internal List<ComponentFiber?> OldOwners { get; init; }
            internal List<ChildKey> OldKeys { get; init; }
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

        // Reconcile entry for a container whose new children require inline expansion (NeedsExpansion
        // names the kinds), or whose old side reached a descendant fiber — ChildReconciler's call site
        // gives why that second one cannot take the time-sliced diff, and neither can this: nothing
        // here reads a frame budget, so a pass that enters here runs to completion. The new tree
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
                OldKeys = pairing.OldKeys,
                OldKeyMap = pool.RentOldKeyMap(),
                UsedOldIndices = pool.RentOrphanedIndexSet(),
                NewKeys = pool.RentKeySet(),
                NewElements = pool.RentElementList(),
                OldOwners = pairing.OldOwners,
                Committed = new List<CommittedLeaf>(),
                Placements = pool.RentPlacementList(),
            };
            var owner = _ctx.FiberStack.Current;
            try
            {
                if (commit.OldKeys.Count == 0)
                {
                    for (var i = 0; i < oldNodes.Length; i++) commit.OldKeys.Add(_keying.ReconcileKey(oldNodes[i], i));
                }
                for (var i = 0; i < oldNodes.Length; i++)
                {
                    commit.OldKeys[i] = commit.OldKeys[i].OwnedBy(commit.OldOwners[i]);
                    commit.OldKeyMap[commit.OldKeys[i]] = (i, oldNodes[i]);
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
                catch (Exception exception)
                {
                    // Nothing this walk created is placed before FinalizeGeneralCommit, so a throw out of it — a
                    // failure, or a suspend no Suspense span of this walk caught — leaves those elements to no
                    // caller: they go the way a suspended span's do.
                    RollbackCommitTo(commit, 0, fibersBefore: null, newFibers);
                    RestoreWhatTheSuspensesWrote(commit);
                    // A boundary above discards everything this walk rendered, so the components it mounted go
                    // too, where a suspended span keeps them for the retry.
                    if (exception is BoundaryCaughtSignal) DisposeFibersMountedBy(oldFibers, newFibers);
                    throw;
                }
                finally
                {
                    _ctx.ContextValueChanged = prevFlag;
                    pool.ReturnInlineWalk(walk);
                }

                // Ahead of the cleanups, which must not reach a fiber this keeps.
                if (_ctx.IsAborted)
                {
                    KeepFibersTheStoppedWalkLeft(
                        oldFibers, newFibers, commit.SkippedByAbort, OwnerKeepsItsNewTree(owner) ? owner : null);
                }
                // Orphan effect cleanups run BEFORE the DOM-removal pass (Finalize → RemoveElement),
                // mirroring the flat path: a deleted FunctionComponent's effect cleanups fire while its
                // Ref.Current is still valid, then the DOM is removed. The sweep (full dispose) runs after.
                RunOrphanEffectCleanups(oldFibers, newFibers);
                var removalsRan = !_ctx.IsAborted;
                if (removalsRan)
                {
                    FinalizeGeneralCommit(commit);
                    ApplyOffscreenChanges(commit);
                }
                else
                {
                    RollbackCommitTo(commit, 0, fibersBefore: null, newFibers);
                    // The fibers KeepFibersTheStoppedWalkLeft kept stand as the screen does, which is what this
                    // puts their offscreen state back to.
                    RestoreWhatTheSuspensesWrote(commit);
                }
                SweepOrphans(oldFibers, newFibers);
                return removalsRan;
            }
            finally
            {
                pool.ReturnPlacementList(commit.Placements);
                pool.Return(commit.OldKeyMap);
                pool.ReturnOrphanedIndexSet(commit.UsedOldIndices);
                pool.ReturnKeySet(commit.NewKeys);
                pool.Return(commit.NewElements);
            }
        }

        // Of the old fibers a stopped walk left out of newFibers, a dropped one goes and the rest stay. A
        // fiber is dropped where the tree its parent holds now, which the next render diffs from, was read by
        // this walk and gave it no node. That is so for a parent this walk reached, and for the container's
        // owner unless the owner's own render is the pass the abort discards. A node met after the abort is
        // skipped rather than rendered, so those are recorded and count as met. A dropped fiber's descendants
        // go with it. Kept fibers join newFibers so the cleanups and the sweep pass over them.
        private static void KeepFibersTheStoppedWalkLeft(
            List<ComponentFiber> oldFibers,
            HashSet<ComponentFiber> newFibers,
            HashSet<ComponentFiber>? skippedByAbort,
            ComponentFiber? ownerWithStandingTree)
        {
            List<ComponentFiber>? kept = null;
            HashSet<ComponentFiber>? dropped = null;
            // oldFibers lists a component after its own descendants, so walking it backwards meets each
            // parent before its children.
            for (var i = oldFibers.Count - 1; i >= 0; i--)
            {
                var fiber = oldFibers[i];
                if (newFibers.Contains(fiber)) continue;
                var parent = fiber.Parent;
                var parentTreeStands = parent != null
                    && (newFibers.Contains(parent) || ReferenceEquals(parent, ownerWithStandingTree));
                var droppedHere = parent != null
                    && (dropped?.Contains(parent) == true
                        || (parentTreeStands && skippedByAbort?.Contains(fiber) != true));
                if (droppedHere) (dropped ??= new HashSet<ComponentFiber>()).Add(fiber);
                else (kept ??= new List<ComponentFiber>()).Add(fiber);
            }
            if (kept == null) return;
            foreach (var fiber in kept) newFibers.Add(fiber);
        }

        // FiberRenderer discards an aborted render's tree by the flag the top-level pass sets, so the owner
        // whose own render is that pass loses the tree it rendered; an owner rendered inline in an enclosing
        // walk has already committed the one it holds.
        private bool OwnerKeepsItsNewTree(ComponentFiber? owner)
            => owner != null && !ReferenceEquals(owner.Reconciler, _ctx.CurrentPass);

        // Matches one emitted new leaf against the old leaves and commits it in place under the live
        // context: an existing element of the same identity is patched (its children reconcile via
        // PatchCommon while ancestor Providers are still pushed), otherwise a fresh element is
        // created (its children reconcile via CreateElement under the same live context). The
        // element is recorded in GeneralCommitState.NewElements in new order; its final
        // placement is decided by FinalizeGeneralCommit. Existing elements stay at their
        // old DOM position (PatchNode preserves parent child order), created elements are orphans.
        //
        // The key is owned by the fiber the walk has reached. A Component's subtree is expanded away before the
        // leaves are matched, so without the owner the diff sees two plain elements at one position and patches
        // the departing component's element into the arriving one. What the departing body left inside it then
        // has to leave through the diff, and an AnimatePresence's committed leaves reach an old side only from
        // ReconcilerContext.PresenceStates, keyed on the boundary fiber that rendered them: the reused element's
        // children reconcile under the arriving fiber, so that lookup misses and those leaves are absent from
        // the old side the removal pass reads.
        private void CommitLeaf(VNode? node, GeneralCommitState commit, ChildKey emittedKey)
        {
            var parent = commit.Parent!;
            var slotStart = commit.SlotStart;
            var key = emittedKey.OwnedBy(_ctx.FiberStack.Current);
            var oldNodes = commit.OldNodes!;

            if (!commit.NewKeys.Add(key))
            {
                FiberLogger.LogWarning("GeneralPathReconciler",
                    $"Duplicate key detected among new siblings: {key}. " +
                    "Both siblings render; give each sibling a unique key.");
            }
            var ordinal = commit.NewElements.Count;
            var linear = ordinal == 0 || commit.Committed[ordinal - 1].Linear;
            var oldIndex = MatchOldLeaf(commit, key, ordinal, linear, out var oldPhysical);
            if (oldIndex >= 0)
            {
                var oldNode = oldNodes[oldIndex];
                var existingDom = parent.ElementAt(oldPhysical);
                if (ReconcileKeying.CanPatch(oldNode, node))
                {
                    var actual = _patcher.ResolveWrapped(existingDom);
                    _patcher.PatchNode(actual, oldNode, node);
                    if (_ctx.IsAborted) return;
                    // Re-fetch: a WrapElement wrapper swap may change the element reference at this index.
                    commit.OldSlots.TryGetPhysical(parent, slotStart + oldIndex, out oldPhysical);
                    existingDom = parent.ElementAt(oldPhysical);
                    commit.NewElements.Add((existingDom, true));
                }
                else
                {
                    commit.NewElements.Add((_factory.CreateElement(node), false));
                }
                commit.UsedOldIndices.Add(oldIndex);
            }
            else
            {
                var newElement = _factory.CreateElement(node);
                commit.NewElements.Add((newElement, false));
            }
            commit.Committed.Add(new CommittedLeaf(oldIndex, linear && oldIndex == ordinal, key));
        }

        // React's reconcileChildrenArray: while each leaf so far took the old leaf at its own index, a leaf
        // whose key that old leaf carries takes it too, so siblings repeating a key in an unchanged order keep
        // their elements. After the first that does not, a key resolves to its last old leaf, and to none once
        // that leaf is taken.
        //
        // Old leaf i is committed at parent.children[slotStart + i] (the previous render placed leaves in
        // expansion order; patches stay in place and creates are orphans, so the bound holds throughout the
        // walk). The slot check degrades a stale oldNodes/DOM mismatch (e.g. after a prior aborted/suspended
        // commit) to a fresh create instead of throwing IndexOutOfRange — the time-sliced keyed path asserts
        // this invariant; the general path can be re-entered mid-suspend so it guards defensively.
        private static int MatchOldLeaf(
            GeneralCommitState commit, ChildKey key, int ordinal, bool linear, out int physical)
        {
            physical = -1;
            int oldIndex;
            if (linear && ordinal < commit.OldKeys.Count && commit.OldKeys[ordinal].Equals(key)
                && !commit.UsedOldIndices.Contains(ordinal))
            {
                oldIndex = ordinal;
            }
            else if (commit.OldKeyMap.TryGetValue(key, out var old) && !commit.UsedOldIndices.Contains(old.index))
            {
                oldIndex = old.index;
            }
            else
            {
                return -1;
            }
            return commit.OldSlots.TryGetPhysical(commit.Parent!, commit.SlotStart + oldIndex, out physical) ? oldIndex : -1;
        }

        // Emits one expanded leaf under the key its position gives it: commits it in place under live context
        // (general path) or collects it into the flat structural result (old-side / fast-path expansion),
        // recording beside it that key and the fiber the walk had reached. One method writes all three, so
        // neither KeysOut nor OwnersOut can fall behind Result.
        //
        // An unkeyed leaf is keyed by its slot under the SlotPath FiberKeying.ComponentChild restarts, not by a
        // count of the leaves emitted before it. With the owner the key carries, that matches a component's
        // elements within its own output, so a component matched by key finds them again wherever its siblings
        // moved it, and what an earlier sibling emits moves none of them.
        private void Emit(InlineWalk walk, VNode? node, WalkPosition position, int nodeIndex)
        {
            var key = ReconcileKeying.ScopedKey(node, position.Scope, nodeIndex, position.SlotPath);
            if (walk.Commit != null) CommitLeaf(node, walk.Commit, key);
            else if (node != null)
            {
                walk.Result!.Add(node);
                walk.KeysOut?.Add(key);
            }
            // MUTANT_SURVIVES(unreachable): no caller reaches this method with a null node.
            // Both call sites sit under a switch whose first arm is `case null: continue;`.
            if (walk.OwnersOut != null && node != null) walk.OwnersOut.Add(_ctx.FiberStack.Current);
        }

        // Rolls a speculative subtree's commits back to preCount entries. A
        // created orphan element (isExisting == false) was never placed and is not reached by
        // FinalizeGeneralCommit, so its poolable leaves are reclaimed via
        // FiberElementCleaner.ReturnRolledBackOrphan; a patched existing element's key
        // is un-used so FinalizeGeneralCommit removes it (the discarded subtree — a
        // suspended Suspense primary the boundary never committed — is replaced by the fallback). A container orphan (e.g.
        // V.Div) is dropped (GC) — its DOM was never placed — but Velvet inline-expands a
        // ComponentNode in its children into a registered fiber whose MountPoint is the orphan
        // container, so the fiber would linger in ComponentRegistry with effects
        // queued against a dead VE. The fibers added during this speculative span that mounted
        // onto a created orphan VE are disposed here so their effect cleanup fires and the
        // registry entry is freed; a later resolve recreates them cleanly. Fibers mounted onto the
        // parent's children directly (wrapper-less inline at the suspended slot, not nested under
        // a created container) are retained — those slots are re-filled by the fallback / by the
        // later resolve's re-expansion, which reuses the retained subtree.
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
            // The rolled-back leaves' keys leave NewKeys, so a boundary's fallback leaf carrying one of them is not
            // reported as a duplicate. What they put in UsedOldIndices stays: a Suspense primary's leaves are keyed
            // under its own scope (FiberKeying.SuspenseSubtree), which no later leaf of the walk carries, a
            // boundary's catch marks every old row of its output taken anyway (ForgetOldRowsOf), and the other
            // rollbacks end the walk.
            for (var i = commit.NewElements.Count - 1; i >= preCount; i--)
            {
                commit.NewKeys.Remove(commit.Committed[i].Key);
                var (element, isExisting) = commit.NewElements[i];
                if (!isExisting)
                {
                    _cleaner.ReturnRolledBackOrphan(element);
                }
            }
            commit.NewElements.RemoveRange(preCount, commit.NewElements.Count - preCount);
            commit.Committed.RemoveRange(preCount, commit.Committed.Count - preCount);
            commit.DisplayChanges?.RemoveAll(change => change.Row >= preCount);
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

        // One pass buckets the placements by parent, then each parent with two or more is walked once, so the
        // cost is the placements plus the child chains of those parents.
        private void CommitComponentOrder(List<(ComponentFiber Fiber, int FirstRow, int Rows)> placements)
        {
            // MUTANT_SURVIVES(equivalent): with fewer than two placements no parent gets two, so the loops below
            // reorder nothing.
            if (placements.Count < 2) return;
            var pool = _ctx.BufferPool;
            var byParent = pool.RentFiberBuckets();
            var placed = pool.RentFiberSet();
            var siblings = pool.RentFiberList();
            try
            {
                foreach (var (fiber, _, _) in placements)
                {
                    // A primary a Suspense keeps hidden keeps its rows, so its components take their order too.
                    if (fiber.Parent == null || !placed.Add(fiber)) continue;
                    if (!byParent.TryGetValue(fiber.Parent, out var ordered))
                    {
                        ordered = pool.RentFiberList();
                        byParent[fiber.Parent] = ordered;
                    }
                    ordered.Add(fiber);
                }
                foreach (var (parent, ordered) in byParent)
                {
                    // MUTANT_SURVIVES(equivalent): a single fiber replaces its own position, so CommitChildOrder
                    // would write back the chain it was handed.
                    if (ordered.Count < 2) continue;
                    siblings.Clear();
                    for (var sibling = parent.Child; sibling != null; sibling = sibling.Sibling)
                        siblings.Add(sibling);
                    // A child of parent is in placed exactly when it is in ordered, since each fiber has one parent.
                    var next = 0;
                    for (var i = 0; i < siblings.Count; i++)
                        if (placed.Contains(siblings[i])) siblings[i] = ordered[next++];
                    parent.CommitChildOrder(siblings);
                }
            }
            finally
            {
                foreach (var ordered in byParent.Values) pool.ReturnFiberList(ordered);
                pool.ReturnFiberBuckets(byParent);
                pool.ReturnFiberList(siblings);
                pool.ReturnFiberSet(placed);
            }
        }

        // Removes old leaves not reused by the walk, then re-places the committed elements into
        // [slotStart, slotStart + NewElements.Count) with the minimum number of DOM moves via
        // a patience-sort LIS (anchors stay put). Mirrors the removal + LIS reorder tail of
        // ReconcileKeyedSync with linearEnd == 0: all matching happened in CommitLeaf, and the leaves it
        // matched linearly are placed with the rest rather than ahead of them.
        private void FinalizeGeneralCommit(GeneralCommitState commit)
        {
            var parent = commit.Parent!;
            var slotStart = commit.SlotStart;
            var oldNodes = commit.OldNodes!;
            var newElements = commit.NewElements;

            // An old leaf stays only where a new leaf patched it; one replaced goes with the rest.
            var patched = commit.UsedOldIndices;
            patched.Clear();
            for (var j = 0; j < newElements.Count; j++)
            {
                if (newElements[j].isExisting) patched.Add(commit.Committed[j].OldIndex);
            }
            // Removal (reverse so not-yet-visited indices stay valid).
            for (var i = oldNodes.Length - 1; i >= 0; i--)
            {
                if (!patched.Contains(i))
                {
                    _cleaner.RemoveElement(parent, LogicalChildSlots.ToPhysical(parent, slotStart + i));
                }
            }

            // LIS reorder over the post-removal DOM positions, from slotStart for the reason above.
            var range = new ChildElementPlacement.PlacementRange
            {
                SlotStart = slotStart,
                ScanStart = slotStart,
                OldLen = oldNodes.Length,
                LogicalNewLen = newElements.Count,
            };
            _placement.ComputeAnchorsAndReorder(parent, newElements, in range);

            CommitComponentOrder(commit.Placements);
            foreach (var (fiber, firstRow, rows) in commit.Placements)
            {
                fiber.MountSlotStart = slotStart + firstRow;
                fiber.MountSlotCount = rows;
            }
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
            // The elements a Suspense of this walk hid, which an enclosing one revealing in the same walk leaves
            // hidden. Held on the walk for the reason OffscreenPrimaries is.
            public readonly HashSet<VisualElement> HiddenPrimaryElements = new();
            // The fiber the walk had reached as each leaf was emitted, index-aligned with Result. Filled
            // only where the caller hands a list in, which is the old side: the new side reads the live
            // FiberStack at the match instead.
            public List<ComponentFiber?>? OwnersOut;
            // The key each leaf was emitted under, index-aligned with Result, on the same terms as OwnersOut.
            public List<ChildKey>? KeysOut;
            public ProviderPairTable? Providers;
            public ProviderPairTable? OldProvidersForPairing;
            public GeneralCommitState? Commit;
            // Expansion-order position of the next new-side Provider, the fallback pairing for a Provider
            // whose structural position has no counterpart on the old side. Monotonic for the whole walk:
            // nothing rewinds it, including a Suspense rollback that discards a primary subtree it has
            // already advanced through — while the old side counts only the branches the boundary's record says
            // are in the container.
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
                OwnersOut = null;
                KeysOut = null;
                Providers = null;
                OldProvidersForPairing = null;
                Commit = null;
                NewProviderOrdinal = 0;
                OffscreenPrimaries.Clear();
                HiddenPrimaryElements.Clear();
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
            ProviderPairTable? oldProvidersForPairing = null,
            List<ComponentFiber?>? owners = null,
            List<ChildKey>? keys = null)
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
            if (!needsExpand)
            {
                // Nothing on this branch descends into a fiber, so every leaf belongs to the fiber the
                // walk entered under.
                if (owners != null)
                {
                    // MUTANT_SURVIVES(equivalent): nothing indexes this list past the array returned here.
                    // OldKeyMap is built over that array alone, so an entry beyond its last slot is never
                    // reached.
                    for (var i = 0; i < nodes.Length; i++) owners.Add(_ctx.FiberStack.Current);
                }
                // keys stays empty: no wrapper scoped these leaves, so each one's key is its own, which a caller
                // derives from the array returned where it needs one.
                return nodes;
            }

            var buffer = _ctx.BufferPool.RentNodeList();
            var prevFlag = _ctx.ContextValueChanged;
            var walk = _ctx.BufferPool.RentInlineWalk();
            walk.Result = buffer;
            walk.IsNewSide = isNewSide;
            walk.Parent = parent;
            walk.SlotStart = slotStart;
            walk.OldFibers = oldFibers;
            walk.NewFibers = newFibers;
            walk.OwnersOut = owners;
            walk.KeysOut = keys;
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
            var isNewSide = walk.IsNewSide;

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
                        ExpandAnimatePresenceInline(walk, presence, position, nodeIndex);
                        break;
                    case BaseElementNode:
                        // Regular element: CreateElement / PatchNode reconciles its children via the
                        // host's ReconcileChildren during this walk's commit, so descendant Components
                        // render in-scope of their ancestor Providers without a pre-captured snapshot.
                        Emit(walk, node, position, nodeIndex);
                        break;
                    default:
                        Emit(walk, node, position, nodeIndex);
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
            var identity = component.ResolvedIdentity;
            var slotKey = FiberKeying.ResolveInlineRegistryPositionKey(
                position, component.Key, nodeIndex,
                _ctx.ComponentRegistry.InlinePositionKeyBoxes,
                _ctx.ComponentRegistry.InlineExplicitPositionKeyBoxes);
            // Read the Portal scope member of this component's registry key before ExpandFiberPreviousTree
            // pushes the component fiber. ReconcilerContext.PortalChildKeyScope owns that boundary.
            var portalScope = _ctx.PortalChildKeyScopeHere;
            var commit = walk.Commit;
            // Error-boundary behavior: once a sibling earlier in this
            // expansion has aborted via TryCatch.SetAborted, subsequent inline
            // ComponentNode mounts must not run their Body — otherwise their fiber
            // becomes registered with the new key but state never bound to the user
            // tree, blocking proper re-mount on the next normal render.
            if (_ctx.IsAborted)
            {
                var skipped = commit == null ? null : _ctx.ComponentRegistry.TryGetFiberForInlineKey(
                    _ctx.FiberStack.Current, slotKey, identity, portalScope, walk.Parent);
                if (skipped != null) (commit!.SkippedByAbort ??= new HashSet<ComponentFiber>()).Add(skipped);
                return;
            }
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
                        $"Duplicate component key detected among siblings: '{component.Key ?? slotKey}'. " +
                        "The repeated sibling is skipped; give each sibling a unique key.");
                    return;
                }
                ComponentFiber fiber;
                try
                {
                    fiber = _ctx.ComponentRegistry.GetOrCreateInline(
                        component, parentFiber, slotKey, walk.Parent, currentSlotStart, portalScope);
                }
                catch (FiberSuspendSignal)
                {
                    // The component whose render suspended stays in the new tree, which a Suspense above keeps
                    // offscreen with its state, as React keeps it: left out of the walk's fibers, the pass's orphan
                    // sweep would dispose it, and with it the read that is to reveal it.
                    var suspended = _ctx.ComponentRegistry.TryGetFiberForInlineKey(
                        parentFiber, slotKey, identity, portalScope, walk.Parent);
                    if (suspended != null) walk.NewFibers.Add(suspended);
                    // A Suspense keeping its committed primary needs every leaf of it committed to hide them, so the
                    // walk goes on, and this component's rows are the output it last committed.
                    if (suspended == null || !_ctx.HoldSuspendInPrimary()) throw;
                    fiber = suspended;
                }
                walk.NewFibers.Add(fiber);
                var preCount = emittedCount;
                if (fiber.IsErrorBoundary) ExpandBoundaryInline(walk, fiber, component, position, nodeIndex, preCount);
                else ExpandFiberPreviousTree(walk, fiber, component, position, nodeIndex);
                if (commit != null) commit.Placements.Add((fiber, preCount, commit.NewElements.Count - preCount));
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

        // A render error below the boundary is caught here, as ExpandSuspenseInline catches a suspend, so the
        // walk goes on to the boundary's siblings. What the failed output committed is taken back, and the
        // fibers it added leave the walk: an old one is left to the orphan cleanups and the sweep, and a new
        // one, which neither reaches, is disposed here. The fallback is then expanded in the same rows, and the
        // catch is reported once it has rendered.
        private void ExpandBoundaryInline(
            InlineWalk walk,
            ComponentFiber boundary,
            ComponentNode component,
            WalkPosition position,
            int nodeIndex,
            int preCount)
        {
            var commit = walk.Commit!;
            var enterCompletionsBefore = _ctx.PendingEnterCompletions.Count;
            var fibersBefore = _ctx.BufferPool.RentFiberSet();
            fibersBefore.UnionWith(walk.NewFibers);
            try
            {
                BoundaryCaughtSignal? caught = null;
                boundary.CatchesInTheWalk = true;
                try
                {
                    ExpandFiberPreviousTree(walk, boundary, component, position, nodeIndex);
                }
                catch (BoundaryCaughtSignal signal) when (ReferenceEquals(signal.Boundary, boundary))
                {
                    caught = signal;
                }
                finally
                {
                    boundary.CatchesInTheWalk = false;
                }
                if (caught == null) return;

                RollbackCommitTo(commit, preCount, fibersBefore, walk.NewFibers);
                _ctx.PendingEnterCompletions.RemoveRange(
                    enterCompletionsBefore, _ctx.PendingEnterCompletions.Count - enterCompletionsBefore);
                DropFibersTheFailedOutputAdded(walk, fibersBefore);
                ForgetOldRowsOf(commit, boundary);
                // What the failed output recorded against the boundary itself, for an AnimatePresence or a
                // Suspense it rendered directly; its descendants' records go with the fibers dropped above.
                _ctx.PrunePresenceBoundaryState(boundary);
                _ctx.PruneSuspenseBoundaryState(boundary);
                boundary.IsShowingFallback = true;
                boundary.FallbackContentFailed = false;
                try
                {
                    ExpandFiberTree(walk, boundary, caught.FallbackTree, component, position, nodeIndex);
                }
                finally
                {
                    boundary.IsShowingFallback = false;
                }
                // An ancestor boundary that caught the fallback's own error on the aborting path has replaced this
                // one, and the original error goes no further, as PropagateException stops at a disposed boundary.
                if (boundary.IsDisposed) return;
                // Published only once the fallback has expanded: a catch on the aborting path during that
                // expansion reconciles this boundary's rows from the tree they still hold, the failed one. The
                // fallback is committed before the failed tree retires, as FiberErrorBoundary.TryShowFallback
                // orders it.
                var failedTree = boundary.PreviousTree;
                boundary.PreviousTree = caught.FallbackTree;
                FiberTreeReturn.ReturnRetiredTree(failedTree, boundary);
                if (boundary.FallbackContentFailed)
                {
                    // The fallback's own error went to the boundaries above and none caught it in this walk;
                    // the original error goes after it.
                    FiberErrorBoundary.PassOnTheCaughtError(boundary, caught);
                    return;
                }
                FiberErrorBoundary.RecordCatch(_ctx, boundary, caught.Error, caught.Info);
            }
            finally
            {
                // MUTANT_SURVIVES(equivalent): a set not handed back is never read again, and the next rent
                // makes a new one.
                _ctx.BufferPool.ReturnFiberSet(fibersBefore);
            }
        }

        // React unmounts a boundary's children before it renders the fallback, so no old row of the boundary is
        // matched by a fallback row: each is marked taken, which both arms of MatchOldLeaf decline, so it goes in
        // the removal pass, and one the failed output patched in place leaves with what that patch wrote.
        private static void ForgetOldRowsOf(GeneralCommitState commit, ComponentFiber boundary)
        {
            for (var i = 0; i < commit.OldOwners.Count; i++)
            {
                for (var owner = commit.OldOwners[i]; owner != null; owner = owner.Parent)
                {
                    if (!ReferenceEquals(owner, boundary)) continue;
                    commit.UsedOldIndices.Add(i);
                    break;
                }
            }
        }

        private void DisposeFibersMountedBy(List<ComponentFiber> oldFibers, HashSet<ComponentFiber> newFibers)
        {
            var old = new HashSet<ComponentFiber>(oldFibers);
            List<ComponentFiber>? mounted = null;
            foreach (var fiber in newFibers)
            {
                if (!old.Contains(fiber)) (mounted ??= new List<ComponentFiber>()).Add(fiber);
            }
            if (mounted == null) return;
            foreach (var fiber in mounted) _ctx.ComponentRegistry.DisposeAndRemove(fiber);
        }

        private void DropFibersTheFailedOutputAdded(InlineWalk walk, HashSet<ComponentFiber> fibersBefore)
        {
            List<ComponentFiber>? added = null;
            foreach (var fiber in walk.NewFibers)
            {
                if (!fibersBefore.Contains(fiber)) (added ??= new List<ComponentFiber>()).Add(fiber);
            }
            if (added == null) return;
            var old = new HashSet<ComponentFiber>(walk.OldFibers);
            foreach (var fiber in added)
            {
                walk.NewFibers.Remove(fiber);
                if (!old.Contains(fiber)) _ctx.ComponentRegistry.DisposeAndRemove(fiber);
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
            => ExpandFiberTree(walk, fiber, fiber.PreviousTree, component, position, nodeIndex);

        private void ExpandFiberTree(
            InlineWalk walk,
            ComponentFiber fiber,
            VNode?[]? tree,
            ComponentNode component,
            WalkPosition position,
            int nodeIndex)
        {
            // Ahead of the empty-tree return: a fiber rendering nothing now can render a presence later.
            if (_ctx.EnclosingPresenceChild is { } enclosingChild)
            {
                fiber.EnclosingPresence = enclosingChild.State;
                fiber.EnclosingPresenceKey = enclosingChild.Key;
            }

            // MUTANT_SURVIVES(equivalent, clause removed): an empty tree sets and restores the walk's fiber and tree
            // around a descent that expands no node.
            if (tree == null || tree.Length == 0) return;

            _ctx.FiberStack.Push(fiber);
            // Moved with the FiberStack push, for the same reason: what this descent stamps onto its children
            // belongs to THIS fiber's output, not the outer caller's.
            var enclosingFiberTree = _ctx.CurrentFiberTree;
            _ctx.CurrentFiberTree = tree;
            try
            {
                var componentPosition = FiberKeying.ComponentChild(position, component.Key, nodeIndex);
                ExpandInlineRecursive(walk, tree, componentPosition);
            }
            finally
            {
                _ctx.CurrentFiberTree = enclosingFiberTree;
                _ctx.FiberStack.Pop();
            }
        }

        // Inline-expands a MemoNode. A memo component emits no DOM — it resolves to
        // an inner element that is reconciled like any other child. The dep cache is keyed by where the
        // memo is written — not a per-pass visitation counter —
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
            var innerPosition = FiberKeying.MemoInner(position, memo.Key, nodeIndex);
            var memoPosition = FiberKeying.MemoAt(
                _ctx.FiberStack.Current, _ctx.PortalChildKeyScopeHere, memo.Key, position, innerPosition);
            var (inner, previousCached) = _ctx.FiberMemoCache.GetOrCompute(memoPosition, walk.Parent, memo);
            if (previousCached != null)
            {
                FiberTreeReturn.ReturnRetiredTree(
                    FiberTreeReturn.NormalizeToArray(previousCached), _ctx.FiberStack.Current, _ctx.FiberMemoCache);
            }
            if (inner == null) return;
            // Recurse under this memo's own position, not the parent's: an inner Component's slot key
            // and a nested Memo's own position both extend it, and MemoScope and WalkPathKind own what
            // then keeps either off an unkeyed Component's at the same node index.
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
        // A nested Suspense owns its own descendants' suspension, so its pending primary must not keep the outer
        // boundary suspended. Every nested Suspense of the delta has been expanded by now, and one that suspended
        // put the fibers of its primary in OffscreenPrimaries, so those are skipped: a nested Suspense the same
        // component renders has the outer one's boundary fiber, which no search up the fibers tells apart. A
        // pending fiber in a nested Suspense's fallback is the outer one's to wait on, as React's is. The delta
        // already contains every fiber in this Suspense's primary subtree, so a per-fiber own-slot check covers
        // descendants without re-walking.
        private static bool AnyPrimaryChildStillPending(InlineWalk walk, HashSet<ComponentFiber> fibersBefore)
        {
            foreach (var fiber in walk.NewFibers)
            {
                if (fibersBefore.Contains(fiber)) continue;
                if (fiber.IsSuspenseBoundary) continue;
                if (walk.OffscreenPrimaries.Contains(fiber)) continue;
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
        // descendant suspends during the new-side render of a primary the boundary has not committed, the
        // partial primary output is discarded (the partially-mounted fibers stay registered so a later resolve
        // re-render reuses them with their state) and the fallback subtree is expanded instead. A primary it has
        // committed is committed again whole and hidden ahead of the fallback (SetPrimaryHidden), so its elements
        // and the fibers mounted in them are kept for the reveal. The children-vs-fallback decision is
        // recorded via ReconcilerContext.SetSuspenseFallbackShown so the old-side structural walk
        // reproduces the committed subtree for the diff.
        // Primary and fallback children use distinct fragment scopes so their fibers never collide.
        private void ExpandSuspenseInline(
            InlineWalk walk,
            SuspenseNode suspense,
            WalkPosition position,
            int nodeIndex)
        {
            var result = walk.Result;
            var newFibers = walk.NewFibers;
            var commit = walk.Commit;
            var boundaryFiber = _ctx.FiberStack.Current;
            var suspenseKey = FiberKeying.SuspenseKey(position.Scope, suspense.Key, nodeIndex);
            var suspenseAt = FiberKeying.SuspenseAt(position, suspense.Key, nodeIndex);
            var primaryPosition = FiberKeying.SuspenseSubtree(
                position, suspenseKey, suspense.Key, nodeIndex, isFallback: false);
            var fallbackPosition = FiberKeying.SuspenseSubtree(
                position, suspenseKey, suspense.Key, nodeIndex, isFallback: true);

            if (walk.IsNewSide && _ctx.IsAborted)
            {
                // Nothing under a boundary the stopped walk meets renders, so no branch can be decided here: a
                // primary that renders nothing reads as settled. The branch the old side showed is walked
                // instead, which records its components as skipped, and the recorded choice stays as it was.
                ExpandCommittedSuspenseBranch(walk, suspense, boundaryFiber, suspenseAt, primaryPosition, fallbackPosition);
            }
            else if (walk.IsNewSide)
            {
                if (boundaryFiber != null) boundaryFiber.IsSuspenseBoundary = true;
                var portalScope = _ctx.PortalChildKeyScopeHere;
                var primaryWasHidden = _ctx.IsSuspensePrimaryHidden(boundaryFiber, walk.Parent, portalScope, suspenseAt);
                // A primary the boundary has committed is kept, hidden; one it has not committed is discarded.
                var revealed = _ctx.TakeSuspensePrimaryReproduced(boundaryFiber, walk.Parent, portalScope, suspenseAt);
                var retains = commit != null && (revealed || primaryWasHidden);
                var preCount = commit != null ? commit.NewElements.Count : result!.Count;
                bool suspended;
                bool hides;
                // Snapshot the fiber set so the post-expansion pending check can be scoped to THIS
                // Suspense's own primary children (the fibers newly added during its expansion).
                var fibersBefore = _ctx.BufferPool.RentFiberSet();
                fibersBefore.UnionWith(newFibers);
                var reportsBefore = _ctx.PendingCaughtErrorReports.Count;
                try
                {
                    suspended = ExpandSuspensePrimary(walk, suspense.Children, retains, primaryPosition);
                    var primaryEnd = commit != null ? commit.NewElements.Count : result!.Count;
                    if (!suspended)
                    {
                        suspended = AnyPrimaryChildStillPending(walk, fibersBefore)
                            || AnyRowStillPending(walk, commit!, preCount, primaryEnd);
                    }
                    hides = suspended && retains;
                    // Mark THIS Suspense's primary children (the fibers added during the children
                    // expansion) as offscreen iff suspended. The offscreen guard in FlushState defers
                    // their lane flush while suspended, to the render that reveals them. The
                    // fallback subtree is expanded below, so this marking never reaches it and this Suspense
                    // leaves it flushable; what marks a nested Suspense's fallback subtree is the
                    // enclosing expansion, whose own fallback occupies that slot too.
                    //
                    // A nested Suspense that suspended has already answered for the fibers it created, and
                    // this delta contains them, so its answer stands.
                    MarkPrimaryOffscreen(walk, fibersBefore, suspended, boundaryFiber);
                    // Rollback and fallback expansion must run while fibersBefore is still live
                    // (rented from the pool, contents intact). Performing them after the finally
                    // would observe a Cleared / re-rented set, silently breaking the fibersBefore
                    // exclusion in RollbackCommitTo.
                    if (suspended)
                    {
                        ForgetCatchesOfTheDiscardedPrimary(reportsBefore, fibersBefore, newFibers,
                            hides ? ElementsIn(commit!, preCount, primaryEnd) : NoElements);
                        if (hides) SetPrimaryHidden(walk, commit!, preCount, primaryEnd, boundaryFiber, hidden: true);
                        else if (commit != null) RollbackCommitTo(commit, preCount, fibersBefore, newFibers);
                        else if (result!.Count > preCount) result.RemoveRange(preCount, result.Count - preCount);
                        if (suspense.Fallback != null)
                        {
                            ExpandInlineRecursive(walk, new[] { suspense.Fallback }, fallbackPosition);
                        }
                    }
                    else if (primaryWasHidden)
                    {
                        SetPrimaryHidden(walk, commit!, preCount, primaryEnd, boundaryFiber, hidden: false);
                    }
                }
                finally
                {
                    _ctx.BufferPool.ReturnFiberSet(fibersBefore);
                }
                RecordSuspenseDecision(walk, boundaryFiber, portalScope, suspenseAt,
                    suspended ? new ReconcilerContext.SuspenseFallbackRecord(suspense, hides) : null);
            }
            else if (ExpandCommittedSuspenseBranch(walk, suspense, boundaryFiber, suspenseAt, primaryPosition, fallbackPosition))
            {
                _ctx.MarkSuspenseReproduced(boundaryFiber, walk.Parent, _ctx.PortalChildKeyScopeHere, suspenseAt);
            }
            else
            {
                _ctx.MarkSuspensePrimaryReproduced(boundaryFiber, walk.Parent, _ctx.PortalChildKeyScopeHere, suspenseAt);
            }
        }

        // Returns whether the primary suspended: at this expansion, or where HoldSuspendInPrimary held it.
        private bool ExpandSuspensePrimary(InlineWalk walk, VNode?[]? children, bool retains, WalkPosition primaryPosition)
        {
            _ctx.OpenSuspensePrimary(retains);
            bool suspended;
            try
            {
                ExpandInlineRecursive(walk, children ?? Array.Empty<VNode>(), primaryPosition);
            }
            // A retaining primary holds every suspend raised in it (HoldSuspendInPrimary), an inline component's
            // and a VirtualList row's, so one reaching here is a primary's that discards.
            catch (FiberSuspendSignal)
            {
                _ctx.MarkSuspensePrimarySuspended();
            }
            finally
            {
                suspended = _ctx.CloseSuspensePrimary();
            }
            return suspended;
        }

        // Records this Suspense's decision under its own position key, null being its children shown. FlushState's
        // offscreen guard reads the boundary-level answer derived from those keys, so a sibling Suspense expanded
        // later in this same walk cannot clear it.
        private void RecordSuspenseDecision(InlineWalk walk, ComponentFiber? boundaryFiber, VisualElement? portalScope,
            long suspenseAt, ReconcilerContext.SuspenseFallbackRecord? record)
        {
            _ctx.MarkSuspenseReRendered(boundaryFiber, walk.Parent, portalScope, suspenseAt);
            // A walk that throws puts the record back (ReconcileGeneral), since its removal pass never runs.
            if (walk.Commit != null)
            {
                (walk.Commit.RecordsBefore ??= new()).Add((boundaryFiber, walk.Parent, portalScope, suspenseAt,
                    _ctx.SuspenseRecordAt(boundaryFiber, walk.Parent, portalScope, suspenseAt)));
            }
            _ctx.SetSuspenseFallbackShown(boundaryFiber, walk.Parent, portalScope, suspenseAt, record);
        }

        // A catch a boundary the Suspense's own walk reached in the primary took is discarded with the render the
        // primary suspends in, as React discards a capture with the render that suspended: that boundary reports
        // nothing for it and renders its children again rather than the fallback it would otherwise keep
        // (FiberErrorBoundary.OutputOf). A boundary inside a host element of the primary is expanded by that
        // element's own reconcile, which this walk does not reach; where the primary is kept hidden, that element
        // and the boundary in it stay, so a boundary mounted inside one of keptElements is reached too. Where it is
        // discarded, the rollback disposes such a boundary with its element.
        // Read only, by a discarded primary, which keeps no element.
        private static readonly HashSet<VisualElement> NoElements = new();

        private void ForgetCatchesOfTheDiscardedPrimary(
            int reportsBefore, HashSet<ComponentFiber> fibersBefore, HashSet<ComponentFiber> newFibers,
            HashSet<VisualElement> keptElements)
        {
            var reports = _ctx.PendingCaughtErrorReports;
            for (var i = reports.Count - 1; i >= reportsBefore; i--)
            {
                var boundary = reports[i].Boundary;
                var inThisWalk = !fibersBefore.Contains(boundary) && newFibers.Contains(boundary);
                var inAKeptElement = HolderOf(boundary.MountPoint, keptElements) != null;
                if (!inThisWalk && !inAKeptElement) continue;
                boundary.CaughtError = null;
                reports.RemoveAt(i);
                // A memoized boundary would otherwise bail on the retry and expand the fallback it holds.
                FiberWorkLoop.RequestRenderFromHook(boundary);
            }
        }

        private void MarkPrimaryOffscreen(
            InlineWalk walk, HashSet<ComponentFiber> fibersBefore, bool suspended, ComponentFiber? boundaryFiber)
        {
            foreach (var f in walk.NewFibers)
            {
                if (fibersBefore.Contains(f)) continue;
                if (!walk.OffscreenPrimaries.Contains(f))
                {
                    // This Suspense is the innermost over a fiber its own walk mounted. A new-side walk always
                    // carries a commit: only ReconcileGeneral starts one.
                    SetOffscreen(walk.Commit!, f, suspended, hiddenUnder: null, suspended ? boundaryFiber : null);
                    (walk.Commit!.OffscreenChanges ??= new()).Add((f, suspended));
                }
                if (suspended) walk.OffscreenPrimaries.Add(f);
            }
        }

        // React hides a committed primary by setting display: none on its outermost host elements and reveals it by
        // taking that off again; the elements below them, and their state, are left alone. A Portal's children are
        // the outermost host elements of its subtree, so they are hidden and revealed beside its placeholder. An
        // element a nested Suspense of this walk hid stays hidden when an enclosing one reveals, since that one
        // still shows its fallback.
        //
        // The fibers mounted inside those elements were mounted by the elements' own reconciles, so this walk's
        // fiber delta, which MarkPrimaryOffscreen reads, does not hold them; they go offscreen and come back with
        // the element that holds them. The innermost Suspense hiding a fiber is the one that reveals it.
        private void SetPrimaryHidden(
            InlineWalk walk, GeneralCommitState commit, int from, int to, ComponentFiber? boundaryFiber, bool hidden)
        {
            var roots = new HashSet<VisualElement>();
            for (var i = from; i < to; i++)
            {
                // CommitLeaf records an element for every leaf it commits.
                var row = commit.NewElements[i].element!;
                if (!hidden && walk.HiddenPrimaryElements.Contains(row)) continue;
                AddWithPortalChildren(row, roots, i, commit, walk, hidden);
            }
            List<ComponentFiber>? mounted = new();
            _ctx.ComponentRegistry.CollectFibersUnder(roots, ref mounted);
            foreach (var fiber in mounted!)
            {
                if (hidden)
                {
                    // Already offscreen through the walk of a Suspense it is a child of, or inside an element
                    // hidden at or below these: that Suspense is the innermost.
                    // MUTANT_SURVIVES(equivalent, logic): that nested Suspense rewrites these fields before any reveal.
                    // Every walk reaching this Suspense expands the nested one first, whose hide or offscreen marking
                    // writes the fiber's HiddenUnder back ahead of the SetPrimaryHidden that reads it.
                    if (fiber.IsOffscreen && (fiber.HiddenUnder == null || IsAtOrUnder(fiber.HiddenUnder, roots))) continue;
                    var wasOffscreen = fiber.IsOffscreen;
                    SetOffscreen(commit, fiber, true, HolderOf(fiber.MountPoint, roots), boundaryFiber);
                    if (!wasOffscreen) (commit.OffscreenChanges ??= new()).Add((fiber, true));
                }
                else if (roots.Contains(fiber.HiddenUnder!))
                {
                    SetOffscreen(commit, fiber, false, hiddenUnder: null, offscreenUnder: null);
                    (commit.OffscreenChanges ??= new()).Add((fiber, false));
                    // FlushState deferred this fiber's update while it was hidden and left nothing scheduled, and a
                    // VirtualList row, the one HoldSuspendInPrimary held among them, is rendered by no walk.
                    if (fiber.IsDirty)
                    {
                        fiber.IsDirty = false;
                        FiberWorkLoop.RequestRenderFromHook(fiber);
                    }
                }
            }
        }

        private void AddWithPortalChildren(VisualElement element, HashSet<VisualElement> roots, int row,
            GeneralCommitState commit, InlineWalk walk, bool hidden)
        {
            roots.Add(element);
            if (hidden) walk.HiddenPrimaryElements.Add(element);
            (commit.DisplayChanges ??= new()).Add((row, element, hidden));
            foreach (var (placeholder, slots) in _ctx.PortalState)
            {
                // An entry with no target holds no slot, so its loop below runs no step.
                if (HolderOf(placeholder, roots) != element) continue;
                for (var slot = slots.SlotStart; slot < slots.SlotStart + slots.SlotLength; slot++)
                {
                    if (!LogicalChildSlots.TryGetPhysical(slots.Target!, slot, out var physical)) break;
                    AddWithPortalChildren(slots.Target![physical], roots, row, commit, walk, hidden);
                }
            }
        }

        // A VirtualList row is rendered by no walk of the boundary, and HoldSuspendInPrimary leaves the one it held
        // with no output, so a row under the primary's elements still waiting on a read is found here.
        private bool AnyRowStillPending(InlineWalk walk, GeneralCommitState commit, int from, int to)
        {
            // MUTANT_SURVIVES(equivalent, guard removed): with no wrapper-mounted fiber the collection finds none.
            if (!_ctx.ComponentRegistry.HasWrapperMountedFibers) return false;
            var roots = ElementsIn(commit, from, to);
            List<ComponentFiber>? rows = new();
            _ctx.ComponentRegistry.CollectWrapperFibersUnder(roots, ref rows);
            foreach (var row in rows!)
            {
                if (HiddenByANestedSuspense(walk, row.MountPoint!, roots)) continue;
                if (ComponentBoundarySearch.HasPendingAsyncSlot(row)) return true;
            }
            return false;
        }

        // A nested Suspense waiting on a row has hidden an element holding it by the time the enclosing one asks: one
        // in this walk hid rows of the range, which the enclosing Suspense has not hidden yet itself, and one in a
        // host element's own reconcile below them applied its hide when that reconcile ended. Told by element
        // rather than by boundary fiber, since one component can render both Suspenses.
        // The walk up meets one of roots: the only caller passes rows CollectWrapperFibersUnder found under them.
        private static bool HiddenByANestedSuspense(InlineWalk walk, VisualElement mount, HashSet<VisualElement> roots)
        {
            for (var ve = mount; ; ve = ve.parent)
            {
                if (roots.Contains(ve)) return walk.HiddenPrimaryElements.Contains(ve);
                if (SuspenseHiddenElements.IsHidden(ve)) return true;
            }
        }

        private static void SetOffscreen(GeneralCommitState commit, ComponentFiber fiber, bool offscreen,
            VisualElement? hiddenUnder, ComponentFiber? offscreenUnder)
        {
            (commit.OffscreenBefore ??= new()).Add((fiber, fiber.IsOffscreen, fiber.HiddenUnder, fiber.OffscreenUnder));
            fiber.IsOffscreen = offscreen;
            fiber.HiddenUnder = hiddenUnder;
            fiber.OffscreenUnder = offscreenUnder;
        }

        // For a walk whose removal pass does not run, so nothing those writes describe reached the screen.
        private void RestoreWhatTheSuspensesWrote(GeneralCommitState commit)
        {
            for (var i = commit.OffscreenBefore?.Count ?? 0; i-- > 0;)
            {
                var (fiber, offscreen, hiddenUnder, offscreenUnder) = commit.OffscreenBefore![i];
                fiber.IsOffscreen = offscreen;
                fiber.HiddenUnder = hiddenUnder;
                fiber.OffscreenUnder = offscreenUnder;
            }
            for (var i = commit.RecordsBefore?.Count ?? 0; i-- > 0;)
            {
                var (boundary, container, portalScope, position, before) = commit.RecordsBefore![i];
                _ctx.SetSuspenseFallbackShown(boundary, container, portalScope, position, before);
            }
        }

        private static HashSet<VisualElement> ElementsIn(GeneralCommitState commit, int from, int to)
        {
            var elements = new HashSet<VisualElement>();
            for (var i = from; i < to; i++)
            {
                elements.Add(commit.NewElements[i].element!);
            }
            return elements;
        }

        private static bool IsAtOrUnder(VisualElement element, HashSet<VisualElement> roots)
            => HolderOf(element, roots) != null;

        private static VisualElement? HolderOf(VisualElement? element, HashSet<VisualElement> roots)
        {
            for (var ve = element; ve != null; ve = ve.parent)
            {
                if (roots.Contains(ve)) return ve;
            }
            return null;
        }

        private void ApplyOffscreenChanges(GeneralCommitState commit)
        {
            if (commit.DisplayChanges != null)
            {
                foreach (var (_, element, hidden) in commit.DisplayChanges)
                {
                    if (hidden)
                    {
                        SuspenseHiddenElements.Hide(element);
                        _ctx.DetachRefsWhileHidden(element);
                        // UI Toolkit lets go of the focus of an element no longer displayed only at its next layout
                        // pass, which SuspenseHiddenFocusPanelTests pins; the commit that hides it lets go of it here.
                        var focused = element.focusController?.focusedElement as VisualElement;
                        if (SuspenseHiddenElements.IsAtOrUnderHidden(focused)) focused!.Blur();
                    }
                    else
                    {
                        SuspenseHiddenElements.Reveal(element);
                        _ctx.AttachRefsHiddenUnder(element);
                    }
                }
            }
            if (commit.OffscreenChanges == null) return;
            foreach (var (fiber, hidden) in commit.OffscreenChanges)
            {
                if (hidden) FiberEffects.HideLayoutEffects(fiber);
                else FiberEffects.ShowLayoutEffects(fiber, _ctx);
            }
        }

        // Walks what the boundary's record says is in the container — the fallback, behind the primary where that
        // is kept hidden, or else the primary — and says whether the fallback is shown.
        private bool ExpandCommittedSuspenseBranch(
            InlineWalk walk,
            SuspenseNode suspense,
            ComponentFiber? boundaryFiber,
            long suspenseAt,
            WalkPosition primaryPosition,
            WalkPosition fallbackPosition)
        {
            var portalScope = _ctx.PortalChildKeyScopeHere;
            var wasFallback = _ctx.IsSuspenseFallbackShown(boundaryFiber, walk.Parent, portalScope, suspenseAt);
            if (wasFallback && _ctx.IsSuspensePrimaryHidden(boundaryFiber, walk.Parent, portalScope, suspenseAt))
            {
                ExpandInlineRecursive(walk, suspense.Children ?? Array.Empty<VNode>(), primaryPosition);
            }
            var nodesToExpand = wasFallback
                ? (suspense.Fallback != null ? new[] { suspense.Fallback } : Array.Empty<VNode>())
                : (suspense.Children ?? Array.Empty<VNode>());
            ExpandInlineRecursive(walk, nodesToExpand, wasFallback ? fallbackPosition : primaryPosition);
            return wasFallback;
        }

        #endregion

        #region AnimatePresence

        // Wrapper-less AnimatePresence expansion (by design: AnimatePresence emits
        // no host element of its own). Its keyed children expand directly into the parent's slot range, so the
        // parent's flex / wrap / gap reach them. Per-boundary state
        // (ReconcilerContext.PresenceBoundaryState, keyed by (boundary fiber, position key)
        // like Suspense) records the leaf composition committed to the DOM so the old-side structural walk
        // reproduces it for the diff. The new side emits the current children plus still-exiting "ghost"
        // children (kept mounted until the exits they play finish or their Motions leave the tree), then plays
        // enter / exit / stagger on each keyed child's <em>anchor</em> (its first emitted element), and a
        // removed child's exit on the descendant Motions CollectDescendantExits finds as well — element create
        // / patch / remove / reorder are handled by the surrounding general-commit machinery (CommitLeaf /
        // FinalizeGeneralCommit), matched by key. Exit is reconcile-driven: when a child's last exit completes,
        // the key is flagged and the boundary re-rendered; the next render stops emitting that child so the
        // diff removes its leaves (no out-of-band DOM mutation that would shift sibling slots).

        // Everything one expansion pass writes as it walks its entries, in one place so a PlayExit
        // completion — which can fire either synchronously (see the completion comment in
        // ExpandAnimatePresenceInline) or long after this pass's own stack frames are gone — always observes
        // the same mutable cell. Must be a reference type: C# forbids a lambda from capturing a ref
        // parameter, so none of this can be threaded through the entry walkers as `ref`.
        private sealed class PresencePassTally
        {
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
            (ComponentFiber? boundary, VisualElement? parent, long presenceKey) stateKey,
            WalkPosition presencePosition)
        {
            if (!_ctx.PresenceStates.TryGetValue(stateKey, out var oldState)) return;

            foreach (var (key, node) in oldState.Committed)
            {
                var site = new PresenceChildSite { Walk = walk, Position = presencePosition };
                EmitPresenceChildAsAnchor(in site, node, FiberNodeFactory.FindFirstMotionDescendant(node), key, out _, out _);
            }
        }

        // mode="wait": while any previously-committed child is still exiting, hold back
        // brand-new keys so the exit fully completes before the new child mounts / enters. The exit's
        // completion already re-renders the boundary; on that render no ghost remains, so the withheld
        // child emits and enters. Returning ghosts (a key re-added mid-exit) and persisting children are
        // never withheld — only keys absent from the committed set.
        private bool ShouldBlockPresenceEnters(
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
                if (RemovalPlaysExit(state, key, node)) return true;
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
        private int CountAnimatedExits(
            List<(string key, VNode node)> prevCommitted,
            HashSet<string> newKeySet,
            ReconcilerContext.PresenceBoundaryState state)
        {
            var exitCount = 0;
            foreach (var (key, node) in prevCommitted)
            {
                if (newKeySet.Contains(key) || state.ExitComplete.Contains(key)) continue;
                if (RemovalPlaysExit(state, key, node))
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
            // Parent is part of the key so an AnimatePresence nested inside a real element does not collide
            // with an outer one at the same fiber and index. See ReconcilerContext.PresenceStates.
            var stateKey = (boundaryFiber, parent, presencePosition.SlotPath);

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

            // Every child counts as removed while the enclosing child is leaving, so each takes the ghost path below.
            var leaving = ReadEnclosingPresence(state!, presence, walk.Parent, commit != null);
            var givenKeyed = _factory.BuildKeyedMapCopy(presence.Children);
            var newKeyed = leaving ? _ctx.BufferPool.RentKeyedList() : givenKeyed;
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
                // carries a number to interpolate) completes SYNCHRONOUSLY, from inside the PlayExit call below,
                // and so does a child's exit wait whose last slot is an inner presence that stops propagating
                // inside this pass's re-emission of that child
                // (AnimatePresencePropagateTests.Given_PropagateTurnedOffThenOnMidExit_When_TheInnerPresenceIsWaitedOn_Then_TheOuterChildLeavesOnce).
                // Either reaches the completion before this pass has finished building nextCommitted for every
                // other key and before its own state.ExitComplete.Clear() further down. Running such a
                // completion's bookkeeping immediately would have that same Clear() wipe the ExitComplete entry
                // it just added, so the re-render it schedules finds the ghost "not complete" again, replays
                // PlayExit, and repeats forever. state.Expanding tracks whether this pass is under way; a
                // completion that fires meanwhile is queued on the state and drained once the pass's bookkeeping
                // has run, so its ExitComplete.Add survives into the render it schedules — a genuinely async
                // completion finds Expanding already false (this method returned long before it fires) and
                // runs immediately, unchanged.
                state.Expanding = true;
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

                if (commit != null)
                {
                    if (leaving && !firstRender) MountNewChildrenLeaving(in pass, givenKeyed);
                    RecordGivenKeys(state, givenKeyed);
                }

                // onExitComplete fires once the exiting children are gone. When every removed child
                // had NO exit animation (all instant-removed above) no PlayExit callback runs to fire it, so fire it
                // here — but only when no animated exit is still in flight (those fire it when the Exiting set drains).
                // Contained the same way as RunExitComplete's animated-exit path above: a throwing callback
                // must not skip the state.Committed bookkeeping that follows, or the next
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

                // This pass's own bookkeeping has settled — a completion queued above can now run safely: its
                // ExitComplete.Add survives past this point instead of being wiped by the Clear() just above.
                state.Expanding = false;
                if (state.DeferredCompletions is { } deferred)
                {
                    state.DeferredCompletions = null;
                    foreach (var completion in deferred) completion();
                }

                // Marked here rather than beside stateKey above, so an expansion that unwound is not
                // counted as having rendered this presence again.
                _ctx.MarkPresenceReRendered(stateKey);
            }
            finally
            {
                state!.Expanding = false;
                if (!ReferenceEquals(newKeyed, givenKeyed)) _ctx.BufferPool.Return(givenKeyed);
                _ctx.BufferPool.Return(newKeyed);
                _ctx.BufferPool.ReturnPresenceKeySet(newKeySet);
                _ctx.BufferPool.Return(prevCommitted);
                _ctx.BufferPool.Return(nextCommitted);
                _ctx.BufferPool.Return(plan);
            }
        }

        // The keys the expansion was given, kept to tell a key added since the last one.
        private static void RecordGivenKeys(
            ReconcilerContext.PresenceBoundaryState state, List<(string key, VNode node)> given)
        {
            state.PropKeys.Clear();
            foreach (var (key, _) in given) state.PropKeys.Add(key);
        }

        // A key given to this presence since its last expansion, while its enclosing child is leaving, mounts as
        // Framer's PresenceChild mounts it: not present, so already leaving.
        private void MountNewChildrenLeaving(in PresenceExpansion pass, List<(string key, VNode node)> given)
        {
            foreach (var (key, node) in given)
            {
                if (pass.State.PropKeys.Contains(key) || PresenceContainsKey(pass.PrevCommitted, key)) continue;
                MountChildLeaving(in pass, key, node);
            }
        }

        // The child mounts at its initial pose with no enter, and plays its exit from there as a ghost does, so it
        // counts in the enclosing child's wait while the presence's exits are running. A child that places nothing
        // is not kept.
        private void MountChildLeaving(in PresenceExpansion pass, string key, VNode node)
        {
            var state = pass.State;
            var motion = FiberNodeFactory.FindFirstMotionDescendant(node);
            var site = new PresenceChildSite
            {
                Walk = pass.Walk, Position = pass.Position, State = state, Absent = true, SuppressInitial = true,
                MountsLeaving = true,
            };
            var before = SnapshotEmission(pass.Walk);
            var anchor = EmitPresenceChildAsAnchor(in site, node, motion, key, out var motionElement, out _);
            if (anchor == null)
            {
                UndoEmission(pass.Walk, before);
                return;
            }
            state.LeavingMounts.Add(key);
            if (motionElement != null) state.MotionElements[key] = motionElement;
            state.ExitAnchors[key] = anchor;
            state.Exiting.Add(key);
            pass.NextCommitted.Add((key, node));
            StartPresenceExit(in pass, key, node, anchor, motionElement, motion);
            pass.Tally.ExitIndex++;
        }

        // Framer's usePresence(propagate): whether the enclosing presence's keyed child this presence sits in is
        // leaving, in which case it treats every child of its own as not present. The emission around this
        // expansion says so itself. A presence mounted in a render of its own has none around it, and takes the
        // child its host element sits in, else the one its fiber or an ancestor fiber was last expanded inside;
        // a re-render later on reads what was last recorded. Without propagate the enclosing child is not
        // consulted, and a slot held in its exit wait is given up, as Framer unregisters when subscribe turns
        // false. Records are written only by a committing expansion.
        private bool ReadEnclosingPresence(
            ReconcilerContext.PresenceBoundaryState state,
            AnimatePresenceNode presence,
            VisualElement? host,
            bool commits)
        {
            var emitting = _ctx.EnclosingPresenceChild;
            if (!emitting.HasValue && presence.Propagate && state.Enclosing == null && host != null)
            {
                emitting = NearestPresenceChild(_ctx.PresenceChildOf(host), FiberPresenceChild());
            }
            var leaving = presence.Propagate
                && (emitting.HasValue
                    ? !emitting.Value.IsPresent
                    : state.Enclosing != null && state.Enclosing.IsLeaving(state.EnclosingKey!));
            if (!commits) return leaving;

            if (emitting.HasValue)
            {
                state.Enclosing = emitting.Value.State;
                state.EnclosingKey = emitting.Value.Key;
            }
            state.Propagate = presence.Propagate;
            state.ExitedForEnclosing = leaving;
            if (!presence.Propagate) state.Registration?.Complete();
            return leaving;
        }

        // Of the child the host element sits in and the one the fiber was last expanded inside, the one nested in the
        // other: a fiber record naming a child below the host's is the nearer, and anything else leaves the host's.
        private static ReconcilerContext.PresenceChildContext? NearestPresenceChild(
            ReconcilerContext.PresenceChildContext? byHost,
            ReconcilerContext.PresenceChildContext? byFiber)
        {
            if (!byHost.HasValue || !byFiber.HasValue) return byHost ?? byFiber;
            for (var state = byFiber.Value.State; state != null; state = state.Enclosing)
            {
                if (ReferenceEquals(state.Enclosing, byHost.Value.State) && state.EnclosingKey == byHost.Value.Key)
                {
                    return byFiber;
                }
            }
            return byHost;
        }

        // The presence child the current fiber, else the nearest ancestor fiber that has one, was last expanded inside.
        private ReconcilerContext.PresenceChildContext? FiberPresenceChild()
        {
            for (var fiber = _ctx.FiberStack.Current; fiber != null; fiber = fiber.Parent)
            {
                if (fiber.EnclosingPresence != null)
                {
                    return new ReconcilerContext.PresenceChildContext(
                        fiber.EnclosingPresence, fiber.EnclosingPresenceKey!,
                        !fiber.EnclosingPresence.IsLeaving(fiber.EnclosingPresenceKey!));
                }
            }
            return null;
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
            // Once removed, a key coming back, mid-exit or later, is not the child Framer's PresenceChild held
            // initial: false for.
            state.InitialBlocked.Remove(key);
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
                RetirePresenceKeyEntries(state, key);
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(node), boundaryFiber);
                return;
            }

            if (!RemovalPlaysExit(state, key, node))
            {
                // No exit animation → immediate removal (skip emitting; the diff reaps the leaves).
                RemoveGhostAtOnce(in pass, key, node);
                return;
            }

            var ghostMotionNode = FiberNodeFactory.FindFirstMotionDescendant(node);
            var site = new PresenceChildSite
            {
                Walk = walk, Position = pass.Position, State = state, Absent = true,
                MountsLeaving = state.LeavingMounts.Contains(key),
            };
            // Only a child whose exit is a propagating presence's can place nothing and still be emitted.
            var emissionBefore = commit != null && PropagatingPresenceHoldsChildren(state, key)
                ? SnapshotEmission(walk)
                : null;
            var ghostAnchor = EmitPresenceChildAsAnchor(in site, node, ghostMotionNode, key, out var ghostMotionElement, out _);
            // A ghost reproduces the SAME committed node on both diff sides, so the patch that
            // would re-record the Motion's element bails on reference equality — fall back to
            // the per-key memo the live emissions kept (see PresenceBoundaryState.MotionElements).
            if (commit != null)
            {
                if (ghostMotionElement != null) state.MotionElements[key!] = ghostMotionElement;
                else state.MotionElements.TryGetValue(key!, out ghostMotionElement);
            }

            // An emission that placed nothing, a propagating presence whose children played no exit and left at
            // once, leaves no element to keep mounted, so there is no exit to wait for.
            if (commit != null && ghostAnchor == null)
            {
                UndoEmission(walk, emissionBefore);
                RemoveGhostAtOnce(in pass, key, node);
                return;
            }

            // Track the live ghost anchor so the drop path (exit complete) can dispose the subtree
            // fibers under it — see DisposeExitedGhostFibers.
            if (commit != null && ghostAnchor != null) state.ExitAnchors[key] = ghostAnchor;

            if (commit != null && ghostAnchor != null && state.Exiting.Add(key))
            {
                StartPresenceExit(in pass, key, node, ghostAnchor, ghostMotionElement, ghostMotionNode);
                pass.Tally.ExitIndex++;
            }

            pass.NextCommitted.Add((key, node));
        }

        private static (HashSet<ComponentFiber> Fibers, int Placements)? SnapshotEmission(InlineWalk walk)
            => (new HashSet<ComponentFiber>(walk.NewFibers), walk.Commit!.Placements.Count);

        // Takes back a ghost emission that placed nothing: the fibers it walked leave the walk, so the orphan
        // cleanups and the sweep dispose them as they do a child removed without being emitted, and the
        // placements it recorded for them go.
        private void UndoEmission(InlineWalk walk, (HashSet<ComponentFiber> Fibers, int Placements)? before)
        {
            if (before == null) return;
            DropFibersTheFailedOutputAdded(walk, before.Value.Fibers);
            var placements = walk.Commit!.Placements;
            placements.RemoveRange(before.Value.Placements, placements.Count - before.Value.Placements);
        }

        // The removal of a key that has nothing to exit: it leaves the committed set, then its entries and node
        // retire, and the pass reports an instant removal so the presence's onExitComplete still runs.
        private void RemoveGhostAtOnce(in PresenceExpansion pass, string key, VNode node)
        {
            var state = pass.State;
            state.Exiting.Remove(key);
            pass.Tally.RemovedInstantThisRender = true;
            // Same as the finished-exit drop: leave the committed set, then retire.
            RemovePresenceCommittedEntry(state.Committed, key);
            RetirePresenceKeyEntries(state, key);
            FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(node), pass.BoundaryFiber);
        }

        // Whether removing key plays an exit at all: its anchor's, a descendant Motion's, or the removal of a
        // propagating presence inside it that still holds children, which is when Framer's inner presence
        // registers with the child and so settles it through its own exit path.
        private bool RemovalPlaysExit(ReconcilerContext.PresenceBoundaryState state, string key, VNode node)
        {
            var anchor = FiberNodeFactory.FindFirstMotionDescendant(node);
            return ResolveExitTransition(anchor)?.HasExitAnimation == true
                || CollectDescendantExits(state, key, anchor, into: null) > 0
                || PropagatingPresenceHoldsChildren(state, key);
        }

        private static bool IsPropagatingPresenceOf(
            ReconcilerContext.PresenceBoundaryState inner,
            ReconcilerContext.PresenceBoundaryState outer,
            string key)
            => inner.Propagate && ReferenceEquals(inner.Enclosing, outer) && inner.EnclosingKey == key;

        // Read before the removal's emission, which is what puts that presence's children through the ghost path.
        private bool PropagatingPresenceHoldsChildren(ReconcilerContext.PresenceBoundaryState outer, string key)
        {
            if (_ctx.PresenceStates.Count < 2) return false;
            foreach (var inner in _ctx.PresenceStates.Values)
            {
                if (IsPropagatingPresenceOf(inner, outer, key) && inner.Committed.Count > 0) return true;
            }
            return false;
        }

        // Whether the child being emitted is leaving and holds a propagating presence. Such a presence has to
        // expand again for the child to take its exits, so a container inside the child reconciles through the
        // expansion walk even when its nodes are the identical instances on both sides, which the flat diff
        // skips unread.
        internal bool EmitsLeavingChildHoldingPropagatingPresence()
        {
            var emitting = _ctx.EnclosingPresenceChild;
            if (emitting == null || emitting.Value.IsPresent || _ctx.PresenceStates.Count < 2) return false;
            foreach (var inner in _ctx.PresenceStates.Values)
            {
                if (IsPropagatingPresenceOf(inner, emitting.Value.State, emitting.Value.Key)) return true;
            }
            return false;
        }

        // The propagating presences inside key's child whose exits are running for its removal, the ones the
        // child's exit waits on. A presence that has not expanded for this removal, or has nothing left running,
        // holds nothing.
        private List<ReconcilerContext.PresenceBoundaryState>? PresencesRunningExitsUnder(
            ReconcilerContext.PresenceBoundaryState outer, string key)
        {
            List<ReconcilerContext.PresenceBoundaryState>? running = null;
            if (_ctx.PresenceStates.Count < 2) return running;
            foreach (var inner in _ctx.PresenceStates.Values)
            {
                if (IsPropagatingPresenceOf(inner, outer, key) && inner.ExitedForEnclosing && inner.Exiting.Count > 0)
                {
                    (running ??= new List<ReconcilerContext.PresenceBoundaryState>()).Add(inner);
                }
            }
            return running;
        }

        // Walks down from the elements key's last committing emission placed, through z-managed placeholders
        // and portals, and counts the Motions there, the anchor aside, whose exit plays — however deep, behind a
        // component, a memo or a Suspense, up to an inner presence's children — adding each one's exit to into
        // when it is given. The anchor's own exit is StartPresenceExit's, but its exit label and orchestration
        // still reach its descendants.
        private int CollectDescendantExits(
            ReconcilerContext.PresenceBoundaryState state,
            string key,
            MotionNode? anchor,
            List<ReconcilerContext.PresenceDescendantExit>? into)
        {
            if (!state.ChildRoots.TryGetValue(key, out var roots)) return 0;
            var walk = new DescendantExitWalk { Roots = roots, Anchor = anchor, Into = into };
            var count = 0;
            foreach (var root in roots)
            {
                // A root the key no longer owns left the tree, and a pooled element can be serving another
                // mount by now.
                if (ReferenceEquals(_ctx.PresenceChildRoots.GetValueOrDefault(root).Roots, roots))
                {
                    count += CollectDescendantExitsUnder(root, null, null, in walk);
                }
            }
            return count;
        }

        // What every step of one CollectDescendantExits walk reads unchanged.
        private readonly struct DescendantExitWalk
        {
            internal List<VisualElement> Roots { get; init; }
            internal MotionNode? Anchor { get; init; }
            internal List<ReconcilerContext.PresenceDescendantExit>? Into { get; init; }
        }

        // inheritedExit is the exit label an ancestor Motion hands down and frame the orchestration an
        // ancestor's exit pose opened; CollectOwnExit says how a Motion reads and passes on each.
        private int CollectDescendantExitsUnder(
            VisualElement element,
            string? inheritedExit,
            MotionOrchestrationFrame? frame,
            in DescendantExitWalk walk)
        {
            if (_ctx.ZLayerPlaceholders.TryGetValue(element, out var real)) element = real;
            // The top of another presence child: an inner presence's, whose Motions are that presence's to exit.
            var owner = _ctx.PresenceChildRoots.GetValueOrDefault(element).Roots;
            if (owner != null && !ReferenceEquals(owner, walk.Roots))
            {
                return 0;
            }
            if (_ctx.PortalState.TryGetValue(element, out var portal))
            {
                return CollectPortalExits(portal, inheritedExit, frame, in walk);
            }

            var count = 0;
            if (_ctx.MotionNodes.TryGetValue(element, out var motion))
            {
                count += CollectOwnExit(element, motion, ref inheritedExit, ref frame, in walk);
            }
            for (var i = 0; i < element.hierarchy.childCount; i++)
            {
                // A layer container holds the z-managed elements whose placeholders the walk follows already.
                var child = element.hierarchy[i];
                if (FiberZLayerCoordinator.IsLayerContainer(child)) continue;
                count += CollectDescendantExitsUnder(child, inheritedExit, frame, in walk);
            }
            return count;
        }

        // A portal's children sit in its target, not under the placeholder the walk reached.
        private int CollectPortalExits(
            PortalSlotInfo portal,
            string? inheritedExit,
            MotionOrchestrationFrame? frame,
            in DescendantExitWalk walk)
        {
            // A portal with no target holds an empty range, so the loop never reads the target.
            var target = portal.Target!;
            var count = 0;
            for (var slot = portal.SlotStart; slot < portal.SlotStart + portal.SlotLength; slot++)
            {
                if (LogicalChildSlots.TryGetPhysical(target, slot, out var physical))
                {
                    count += CollectDescendantExitsUnder(target.hierarchy[physical], inheritedExit, frame, in walk);
                }
            }
            return count;
        }

        // One Motion's exit, returning 1 when it plays, and what it hands its descendants in place of
        // inheritedExit and frame. A Motion naming none of its labels exits to the inherited label, the way an
        // animate label propagates, and one naming any hands down only its own exit. A variant child claims the
        // frame's next slot, whether or not it plays an exit.
        private int CollectOwnExit(
            VisualElement element,
            MotionNode motion,
            ref string? inheritedExit,
            ref MotionOrchestrationFrame? frame,
            in DescendantExitWalk walk)
        {
            var label = MotionVariantResolver.IsControlling(motion) ? motion.Exit : inheritedExit;
            StyleTransitionConfig? exitTransition = null;
            var claimSec = MotionVariantResolver.IsVariantChild(motion) && frame != null
                ? frame.ClaimNextChildDelaySec()
                : 0f;
            var plays = 0;
            if (label != null)
            {
                MotionVariant pose = default;
                var hasPose = motion.Variants != null && motion.Variants.TryGetValue(label, out pose);
                // A label naming no pose plays the classic exit when the Motion names it itself, as an
                // anchor's does; an inherited one names nothing this Motion can play.
                exitTransition = hasPose ? pose.Transition ?? motion.Transition
                    : motion.Exit != null ? motion.Transition : null;
                if (exitTransition?.HasExitAnimation == true)
                {
                    if (!ReferenceEquals(motion, walk.Anchor))
                    {
                        plays = 1;
                        walk.Into?.Add(new ReconcilerContext.PresenceDescendantExit(element,
                            hasPose
                                ? exitTransition.WithExitClasses(RestingVariantClass(element), pose.ClassName ?? string.Empty)
                                : exitTransition,
                            RestoresResting: hasPose,
                            DelaySec: claimSec));
                    }
                }
            }
            inheritedExit = label;
            // A count reads no delay, so it opens no frame, and the diagnostic a frame can raise is left to the
            // walk that plays.
            if (walk.Into != null)
            {
                frame = FiberNodePatcher.ResolveChildOrchestration(motion, exitTransition,
                    childLabelChanged: label != null, frame, claimSec);
            }
            return plays;
        }

        // The variant classes the element rests at now: its exit swaps from these.
        private string RestingVariantClass(VisualElement element)
            => _ctx.MotionAppliedClasses.TryGetValue(element, out var applied)
                ? string.Join(" ", applied.VariantClasses)
                : string.Empty;

        // Starts one ghost's exit animation: the enter cancellations and the PopLayout pin that must precede
        // it, then the reconcile-driven completion that flags the finished exit and re-renders the boundary.
        private void StartPresenceExit(
            in PresenceExpansion pass,
            string key,
            VNode node,
            VisualElement ghostAnchor,
            VisualElement? ghostMotionElement,
            MotionNode? ghostMotionNode)
        {
            var presence = pass.Presence;
            _ctx.StyleAnimationScheduler.CancelEnter(ghostAnchor);
            // A wrapped Motion's variant enter ran on its own element, not the anchor —
            // cancel there too (idempotent when both are the same element).
            if (ghostMotionElement != null && !ReferenceEquals(ghostMotionElement, ghostAnchor))
            {
                _ctx.StyleAnimationScheduler.CancelEnter(ghostMotionElement);
            }
            if (PopsOutOfFlow(presence, node))
            {
                PinExitingChildOutOfFlow(ghostAnchor);
            }
            var capturedKey = key;
            var capturedState = pass.State;
            var capturedBoundary = pass.BoundaryFiber;
            var capturedOnExitComplete = presence.OnExitComplete;
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
                    // Framer's safeToRemove, which runs ahead of the presence's own onExitComplete.
                    capturedState.Registration?.Complete();
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
            // The child is removed once its exits have played and each lead its layoutIds passed to has landed. A key
            // coming back drops its landings (MotionLayoutIdDriver.Present), and a presence retired since completes
            // nothing, by the check SettleIfStillExiting makes.
            var waits = 1;
            void Settle()
            {
                if (--waits == 0) RunExitComplete();
            }
            waits += MotionLayoutIdDriver.Relegate(ghostAnchor, _ctx, () =>
            {
                if (_ctx.PresenceStates.ContainsValue(capturedState)) Settle();
            });
            DispatchPresenceExits(in pass, key, ghostAnchor, ghostMotionElement, ghostMotionNode, Settle);
        }

        // Plays one ghost's exits — its anchor's and its descendants' — under one wait that runs
        // runExitComplete once the last has completed or left the tree.
        private void DispatchPresenceExits(
            in PresenceExpansion pass,
            string key,
            VisualElement ghostAnchor,
            VisualElement? ghostMotionElement,
            MotionNode? ghostMotionNode,
            Action runExitComplete)
        {
            var tally = pass.Tally;
            var state = pass.State;
            // `exit`: when the resolved Motion declares an exit variant label, animate from the
            // resting variants[animate] to variants[exit]; otherwise use the transition's
            // ExitFrom/ExitTo. The variant swap targets the Motion's OWN element (where the
            // resting variant classes live), which for a wrapped Motion is not the anchor —
            // without a resolved element the variant path is unavailable and the classic,
            // anchor-targeted transition plays instead.
            // A child mounted already leaving rests at its initial pose, which is where its exit starts from.
            var variantExit = ghostMotionElement != null
                ? TryResolveVariantExit(ghostMotionNode,
                    state.LeavingMounts.Contains(key) ? RestingVariantClass(ghostMotionElement) : null)
                : null;
            var exitTransition = variantExit ?? ghostMotionNode?.Transition;
            var exitTarget = variantExit != null ? ghostMotionElement! : ClassicTarget(ghostAnchor, ghostMotionElement);
            // See the completion comment in ExpandAnimatePresenceInline: a completion that fires while this
            // presence is expanding, from inside one of the PlayExit calls below or from an inner presence settling
            // during an expansion of its own, is queued instead of run inline.
            void Settled()
            {
                if (state.Expanding) (state.DeferredCompletions ??= new List<Action>()).Add(runExitComplete);
                else runExitComplete();
            }

            var anchorPlays = ResolveExitTransition(ghostMotionNode)?.HasExitAnimation == true;
            var descendants = new List<ReconcilerContext.PresenceDescendantExit>();
            CollectDescendantExits(state, key, ghostMotionNode, descendants);
            var inners = PresencesRunningExitsUnder(state, key);
            if (!anchorPlays && descendants.Count == 0 && inners == null)
            {
                Settled();
                return;
            }

            ReconcilerContext.PresenceExitWait? wait = null;
            // Every exit is counted before the first play starts, so a completion one of them fires
            // synchronously cannot settle the wait while the rest are still to start.
            var exitWait = new ReconcilerContext.PresenceExitWait(anchorPlays, descendants, inners, Settled,
                tornDown => SettleExitWaitByTeardown(state, wait!, ghostAnchor, runExitComplete, tornDown));
            wait = exitWait;
            state.ExitWaits[key] = exitWait;
            foreach (var exit in descendants)
            {
                _ctx.StyleAnimationScheduler.CancelEnter(exit.Element);
                _ctx.PresenceDescendantExitWaits[exit.Element] = exitWait;
            }

            var staggerSec = pass.Presence.StaggerDelaySec(tally.ExitIndex, tally.AnimatedExitCount);
            // A spring or bezier exit reads the element's inline translate as PlayExit starts it and holds an axis
            // neither pose names there, so a hold lands before it. A tween exit writes its transition in PlayExit,
            // so a hold landed after moves on it where the exit plays on this element, as the resting USS classes
            // the enter's cancel put back do. The hold lands whether or not the anchor plays an exit, since the
            // enter StartPresenceExit cancelled never reaches the swap that would release it, and the descendants'
            // holds land on the same terms.
            var landsBeforeExit = exitTransition == null || !StyleAnimationScheduler.TweensOnSwap(exitTransition);
            var landsAfterExit = landsBeforeExit ? null : ghostMotionElement;
            if (landsBeforeExit && ghostMotionElement != null)
            {
                _patcher.LandInlineHold(ghostMotionElement);
            }
            if (anchorPlays)
            {
                var onExitSwap = variantExit != null
                    ? _patcher.PlanInlineExit(ghostMotionElement!, ghostMotionNode!.ClassNames, variantExit.ExitToClasses)
                    : null;
                // For a variant exit the From classes ARE the resting variants[animate]; if this exit is
                // cancelled (the key is re-added before it finishes) the element must return to that resting
                // variant rather than be left without it (interrupt handling).
                _ctx.StyleAnimationScheduler.PlayExit(exitTarget, exitTransition, exitWait.CompleteAnchor,
                    restoreFromOnCancel: variantExit != null, additionalDelaySec: staggerSec, onSwap: onExitSwap);
            }
            if (landsAfterExit != null)
            {
                _patcher.LandInlineHold(landsAfterExit);
            }
            foreach (var exit in descendants)
            {
                PlayDescendantExit(exit, exitWait, staggerSec);
            }
        }

        // A torn-down descendant settles the wait from inside FiberElementCleaner, which unmounting the whole
        // presence reaches as well, and so does an inner presence retired while the wait holds a slot for it
        // (tornDown is null then, and ghostAnchor is the only element to read a panel from). Where an element
        // has a panel the check waits for the next frame: a presence unmounted whole has retired its state by
        // then and fires no onExitComplete, while one still mounted, an enclosing presence whose inner presence
        // was retired alone, does fire it. With no panel the check runs at the teardown itself.
        private void SettleExitWaitByTeardown(
            ReconcilerContext.PresenceBoundaryState state,
            ReconcilerContext.PresenceExitWait wait,
            VisualElement ghostAnchor,
            Action runExitComplete,
            VisualElement? tornDown)
        {
            void SettleIfStillExiting()
            {
                if (_ctx.PresenceStates.ContainsValue(state) && state.ExitWaits.ContainsValue(wait))
                {
                    runExitComplete();
                }
            }
            var panel = tornDown?.panel ?? ghostAnchor.panel;
            if (panel != null) panel.visualTree.schedule.Execute(SettleIfStillExiting);
            else SettleIfStillExiting();
        }

        // Lands the descendant's hold, and writes its exit pose's inline values at the swap, on the anchor's terms
        // (DispatchPresenceExits).
        private void PlayDescendantExit(ReconcilerContext.PresenceDescendantExit exit,
            ReconcilerContext.PresenceExitWait wait, float staggerSec)
        {
            var element = exit.Element;
            var landsBeforeExit = !StyleAnimationScheduler.TweensOnSwap(exit.Config);
            if (landsBeforeExit)
            {
                _patcher.LandInlineHold(element);
            }
            var onExitSwap = exit.RestoresResting
                ? _patcher.PlanInlineExit(element, _ctx.MotionNodes.GetValueOrDefault(element)?.ClassNames,
                    exit.Config.ExitToClasses)
                : null;
            _ctx.StyleAnimationScheduler.PlayExit(element, exit.Config, () => wait.CompleteDescendant(element),
                restoreFromOnCancel: exit.RestoresResting, additionalDelaySec: staggerSec + exit.DelaySec,
                onSwap: onExitSwap);
            if (!landsBeforeExit)
            {
                _patcher.LandInlineHold(element);
            }
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
            state.LeavingMounts.Remove(key);
            var site = LiveEntrySite(in pass, key);
            var anchor = EmitPresenceChildAsAnchor(in site, node, motion, key, out var motionElement,
                out var anchorEmission);
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
                    CancelInterruptedPresenceExit(anchor, motionElement, motion, presence, node);
                    MotionLayoutIdDriver.Present(anchor, _ctx);
                }
                else if (wasExitComplete)
                {
                    RestoreAfterCompletedPresenceExit(anchor, motionElement, motion, presence, node,
                        freshReplacement);
                }
                if (wasExiting || wasExitComplete)
                {
                    ReleaseDescendantExits(state, key);
                }

                var isEnter = wasExiting || wasExitComplete || Remounted(anchorEmission, state, key)
                    || !PresenceContainsKey(prevCommitted, key);
                // The create path already played, or withheld, the enter of an anchor inheriting its labels.
                if (isEnter && !anchorEmission.EnterHandled)
                {
                    // A cancelled exit's reversal returns the same element to rest; a new one enters.
                    PlayPresenceEnter(in pass, motion, anchor, motionElement, wasExiting && !anchorEmission.Created);
                }
            }

            pass.NextCommitted.Add((key, node));
            pass.Tally.VisualIndex++;
        }

        // A live key whose anchor was created again under the same key — a type flip — remounts with an enter, as a
        // Framer PresenceChild's new child does, unless the key still withholds its first render's.
        private static bool Remounted(in AnchorEmission emission, ReconcilerContext.PresenceBoundaryState state, string key)
            => emission.Created && !state.InitialBlocked.Contains(key);

        // Where a live keyed child is emitted, and the stagger slot PlayPresenceEnter plays its enter in. A child
        // present at the first render under initial: false keeps withholding mount enters for as long as it
        // stays, as Framer's PresenceChild keeps the initial: false it was created with.
        private static PresenceChildSite LiveEntrySite(in PresenceExpansion pass, string key)
        {
            if (pass.FirstRender && !pass.Presence.Initial)
            {
                pass.State.InitialBlocked.Add(key);
            }
            return new PresenceChildSite
            {
                Walk = pass.Walk,
                Position = pass.Position,
                State = pass.State,
                SuppressInitial = pass.State.InitialBlocked.Contains(key),
                AnchorEnterDelaySec = pass.Presence.StaggerDelaySec(pass.Tally.VisualIndex, pass.NewKeyed.Count),
            };
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

        // A re-entry's half of the descendant exits StartPresenceExit started. A variant exit returns to the
        // resting pose the re-added child's reconcile recorded, as CancelInterruptedPresenceExit's does for the
        // anchor: one still playing through its cancel, one that completed by hand, since nothing is left to
        // cancel. Either way the resting inline values are written back over the exit's. A descendant that left
        // the tree is not touched.
        private void ReleaseDescendantExits(ReconcilerContext.PresenceBoundaryState state, string key)
        {
            if (!state.ExitWaits.Remove(key, out var wait)) return;
            foreach (var registration in wait.Registrations) registration.Release();
            for (var i = 0; i < wait.Descendants.Count; i++)
            {
                var exit = wait.Descendants[i];
                var element = exit.Element;
                var baseClasses = _ctx.MotionNodes.GetValueOrDefault(element)?.ClassNames;
                switch (wait.StatusOf(i))
                {
                    case ReconcilerContext.PresenceExitWait.DescendantStatus.Gone:
                        break;
                    case ReconcilerContext.PresenceExitWait.DescendantStatus.Playing:
                        _ctx.PresenceDescendantExitWaits.Remove(element);
                        var resting = _patcher.RestingClassSet(element, baseClasses);
                        _ctx.StyleAnimationScheduler.CancelExit(element, resting.VariantClasses, resting.Merged);
                        _patcher.RestoreInlineAfterExit(element, baseClasses);
                        break;
                    default:
                        _ctx.PresenceDescendantExitWaits.Remove(element);
                        StyleAnimationClassUtils.RemoveClasses(element, exit.Config.ExitToClasses);
                        var restored = exit.RestoresResting
                            ? _patcher.RestingClassSet(element, baseClasses).VariantClasses
                            : Array.Empty<string>();
                        StyleAnimationClassUtils.AddClasses(element, restored);
                        _patcher.RestoreInlineAfterExit(element, baseClasses);
                        break;
                }
            }
        }

        // Everything the presence keeps per key besides its committed entry.
        private static void RetirePresenceKeyEntries(ReconcilerContext.PresenceBoundaryState state, string key)
        {
            state.MotionElements.Remove(key);
            state.ChildRoots.Remove(key);
            state.ExitWaits.Remove(key);
            state.LeavingMounts.Remove(key);
        }

        private void CancelInterruptedPresenceExit(
            VisualElement anchor,
            VisualElement? motionElement,
            MotionNode? motion,
            AnimatePresenceNode presence,
            VNode node)
        {
            // The re-added node was reconciled onto the element before this runs, so the variant exit's cancel
            // restores the resting classes that reconcile recorded rather than the ones the exit started from.
            MotionAppliedClassSet? resting = motionElement != null
                ? _patcher.RestingClassSet(motionElement, motion?.ClassNames)
                : null;
            var anchorResting = ReferenceEquals(anchor, motionElement) ? resting : null;
            // Without a Motion of its own the key plays no exit on its anchor, and that anchor can be the element
            // of an inner presence's child that is exiting for its own reasons (the key rendering nothing but
            // that presence), which a cancel here would strand mid-exit.
            if (motion != null)
            {
                _ctx.StyleAnimationScheduler.CancelExit(anchor, anchorResting?.VariantClasses, anchorResting?.Merged);
            }
            // A wrapped Motion's variant exit ran on its own element, not the anchor — the
            // cancel (whose reversal restores the resting variant) must land there too.
            if (motionElement != null && !ReferenceEquals(motionElement, anchor))
            {
                _ctx.StyleAnimationScheduler.CancelExit(motionElement, resting?.VariantClasses, resting?.Merged);
            }
            if (motionElement != null)
            {
                _patcher.RestoreInlineAfterExit(motionElement, motion?.ClassNames);
            }
            if (PopsOutOfFlow(presence, node))
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
        private void RestoreAfterCompletedPresenceExit(
            VisualElement anchor,
            VisualElement? motionElement,
            MotionNode? motion,
            AnimatePresenceNode presence,
            VNode node,
            bool freshReplacement)
        {
            if (PopsOutOfFlow(presence, node) && !freshReplacement)
            {
                // The out-of-flow pin outlives its exit (only the drop would have removed the
                // element). Skipped for a fresh replacement: the pin lives on the discarded
                // ghost, and nulling a never-pinned element's geometry slots would erase
                // resolver-applied inline values a transparent-wrapper child cannot re-resolve.
                RestorePopLayoutChildToFlow(anchor, (node as BaseElementNode)?.ClassNames);
            }
            var completedExit = TryResolveVariantExit(motion);
            if (completedExit == null)
            {
                // A completed classic exit leaves its to classes on the anchor it played on.
                if (motion?.Transition != null)
                {
                    StyleAnimationClassUtils.RemoveClasses(ClassicTarget(anchor, motionElement), motion.Transition.ExitToClasses);
                }
                return;
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
            StyleAnimationClassUtils.RemoveClasses(motionElement, completedExit.ExitToClasses);
            StyleAnimationClassUtils.AddClasses(motionElement, completedExit.ExitFromClasses);
            _patcher.RestoreInlineAfterExit(motionElement, motion?.ClassNames);
        }

        private void PlayPresenceEnter(
            in PresenceExpansion pass,
            MotionNode? motion,
            VisualElement anchor,
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
                    _ctx.CompleteEnterAfterThePass(motion, pass.BoundaryFiber);
                }
            }
        }

        // The enter paths that fire the callback in-pass rather than handing it to
        // StyleAnimationScheduler share this so the containment is written once, and it is the same
        // containment RunExitComplete gives the other half of the pair: the emission this sits inside has
        // bookkeeping still to do, and a user callback must not be what stops it.
        internal static void InvokeEnterComplete(MotionNode motion, ComponentFiber? boundaryFiber)
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

        // A variant Motion (carrying variants and an animate label, its own or inherited) manages its resting state
        // through variant classes: variants[animate] is applied at mount and restored by CancelExit on an
        // exit-cancel. So it only ever plays a VARIANT enter (when an initial label resolves) and must NOT fall through to the classic
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
            VisualElement anchor,
            VisualElement? motionElement,
            bool wasExiting,
            float staggerDelaySec,
            ComponentFiber? boundaryFiber)
        {
            // Resolved as FiberNodeFactory.ResolveMountEnter resolves them, so an anchor inheriting its labels from
            // the Motion above the presence is a variant Motion here too.
            var stack = _ctx.ComponentContextStack;
            var animateLabel = MotionVariantResolver.LabelForChildren(motion, stack.Get(MotionContext.ActiveLabel));
            var initialLabel = MotionVariantResolver.InitialLabel(motion, stack.Get(MotionContext.InitialLabel));
            var isVariantMotion = motionElement != null && motion.Variants != null && animateLabel != null;
            if (isVariantMotion && !wasExiting
                && TryResolveVariantEnter(motion, initialLabel, animateLabel, out var fromClasses, out var toClasses,
                    out var enterTransition)
                && enterTransition != null)
            {
                // `initial`: enter from variants[initial] to variants[animate] (kept as the persistent
                // resting state).
                // The enter cancels the pose swap a label change started in this render, whatever its transition,
                // and with it the onSwap that would write the swap's held inline values, so they land first.
                _patcher.LandInlineHold(motionElement!);
                var onSwap = _patcher.HoldInlineForEnter(motionElement!, motion.ClassNames, fromClasses!,
                    enterTransition);
                _ctx.StyleAnimationScheduler.PlayVariantEnter(motionElement, fromClasses, toClasses,
                    enterTransition, ContainedEnterComplete(motion, boundaryFiber), staggerDelaySec, onSwap);
            }
            else if (isVariantMotion)
            {
                // Variant Motion without `initial`, or one whose exit was cancelled: rest at the animate pose.
                _ctx.CompleteEnterAfterThePass(motion, boundaryFiber);
            }
            else
            {
                // As for the variant enter above; of the classic enters, only a timed tween cancels it.
                var target = ClassicTarget(anchor, motionElement);
                if (motion.Transition != null && StyleAnimationScheduler.TweensOnSwap(motion.Transition))
                {
                    _patcher.LandInlineHold(target);
                }
                _ctx.StyleAnimationScheduler.PlayEnter(target, motion.Transition,
                    ContainedEnterComplete(motion, boundaryFiber), staggerDelaySec);
            }
        }

        // Where a classic enter or exit plays: the keyed child's anchor, which holds the Motion or is it, unless the
        // Motion's element sits outside it, as when the child places another element ahead of its Motion.
        private static VisualElement ClassicTarget(VisualElement anchor, VisualElement? motionElement)
            => motionElement != null && !anchor.Contains(motionElement) ? motionElement : anchor;

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

        // A keyed Fragment exits in flow, as under Framer's popLayout: its PopChild pins only an HTML element the
        // child's ref resolves to, which a Fragment's never does.
        private static bool PopsOutOfFlow(AnimatePresenceNode presence, VNode node)
            => presence.Mode == AnimatePresenceMode.PopLayout && node is not FragmentNode;

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
            // A clip wrapper is handed its inner's classNames. Reapplied to the inner, the values that place or
            // size it land back on the wrapper and the rest stay on the inner.
            if (classNames != null)
            {
                FiberNodePatcher.ReapplyArbitraryValues(ClipPathLayoutBox.InnerOf(anchor), classNames);
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

        // Expands one keyed AnimatePresence child into the parent's slot range via ExpandInlineRecursive,
        // under a render-stable per-child scope. Returns the child's anchor element — the first element it
        // emitted into GeneralCommitState.NewElements — for enter / exit animation, or null on
        // the structural (old-side) walk or when the child emitted nothing.
        private VisualElement? EmitPresenceChild(
            InlineWalk walk,
            VNode? node,
            string? key,
            WalkPosition presencePosition,
            ReconcilerContext.PresenceBoundaryState? state)
        {
            var commit = walk.Commit;
            var startIdx = commit != null ? commit.NewElements.Count : walk.Result!.Count;
            var emission = ++_ctx.PresenceChildEmissionCount;
            var childPosition = FiberKeying.PresenceChild(presencePosition, key);
            ExpandInlineRecursive(walk, new[] { node }, childPosition);

            if (commit != null)
            {
                RecordPresenceChildRoots(commit, startIdx, state!, key!, emission);
            }

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
            in PresenceChildSite site,
            VNode? node,
            MotionNode? anchorMotion,
            string? key,
            out VisualElement? anchorMotionElement,
            out AnchorEmission anchorEmission)
        {
            var previousAnchor = _ctx.PresenceAnchorMotion;
            var previousAnchorEnterHandled = _ctx.PresenceAnchorEnterHandled;
            var previousAnchorCreated = _ctx.PresenceAnchorCreated;
            var previousAnchorElement = _ctx.PresenceAnchorMotionElement;
            var previousAnchorEnterDelaySec = _ctx.PresenceAnchorEnterDelaySec;
            var previousEnclosing = _ctx.EnclosingPresenceChild;
            var previousMountsLeaving = _ctx.PresenceMountsLeaving;
            _ctx.PresenceMountsLeaving = site.MountsLeaving;
            if (site.State != null && key != null)
            {
                _ctx.EnclosingPresenceChild = new ReconcilerContext.PresenceChildContext(site.State, key, !site.Absent);
            }
            _ctx.PresenceAnchorMotion = anchorMotion;
            _ctx.PresenceAnchorMotionElement = null;
            _ctx.ComponentContextStack.Push(MotionContext.EntersBlocked, site.SuppressInitial);
            _ctx.PresenceAnchorEnterDelaySec = site.AnchorEnterDelaySec;
            _ctx.PresenceAnchorEnterHandled = false;
            _ctx.PresenceAnchorCreated = false;
            try
            {
                var emitted = EmitPresenceChild(site.Walk, node, key, site.Position, site.State);
                anchorMotionElement = _ctx.PresenceAnchorMotionElement;
                anchorEmission = new AnchorEmission
                {
                    EnterHandled = _ctx.PresenceAnchorEnterHandled,
                    Created = _ctx.PresenceAnchorCreated,
                };
                return emitted;
            }
            finally
            {
                _ctx.PresenceAnchorMotion = previousAnchor;
                _ctx.PresenceAnchorMotionElement = previousAnchorElement;
                _ctx.ComponentContextStack.Pop(MotionContext.EntersBlocked);
                _ctx.PresenceAnchorEnterDelaySec = previousAnchorEnterDelaySec;
                _ctx.PresenceAnchorEnterHandled = previousAnchorEnterHandled;
                _ctx.PresenceAnchorCreated = previousAnchorCreated;
                _ctx.EnclosingPresenceChild = previousEnclosing;
                _ctx.PresenceMountsLeaving = previousMountsLeaving;
            }
        }

        // What the create path reported about the anchor Motion during one emission: whether it played or withheld
        // the anchor's enter itself, and whether it created the anchor's element rather than patching it.
        private readonly struct AnchorEmission
        {
            internal bool EnterHandled { get; init; }
            internal bool Created { get; init; }
        }

        // Where a keyed child is emitted and on what terms: the state its roots are recorded in (null on the
        // old side, which commits nothing) and whether its Motions' mount enters are suppressed.
        private readonly struct PresenceChildSite
        {
            internal InlineWalk Walk { get; init; }
            internal WalkPosition Position { get; init; }
            internal ReconcilerContext.PresenceBoundaryState? State { get; init; }
            internal bool SuppressInitial { get; init; }
            internal float AnchorEnterDelaySec { get; init; }

            // The key is leaving: a presence expanding inside its subtree reads that as its enclosing child
            // not being present.
            internal bool Absent { get; init; }

            // The key was mounted already leaving, so its Motions rest at their initial pose.
            internal bool MountsLeaving { get; init; }
        }

        // Records the top-level elements this emission of key placed as the key's roots. An element an inner
        // presence claimed during this same emission — a keyed child whose own top is an AnimatePresence — stays
        // the inner child's and is no root of this one: what is under it is that presence's to exit. A claim
        // left by an earlier emission is overwritten, since this emission is what now places the element.
        private void RecordPresenceChildRoots(
            GeneralCommitState commit,
            int startIdx,
            ReconcilerContext.PresenceBoundaryState state,
            string key,
            long emission)
        {
            if (!state.ChildRoots.TryGetValue(key, out var roots))
            {
                roots = new List<VisualElement>();
                state.ChildRoots[key] = roots;
            }
            roots.Clear();
            for (var i = startIdx; i < commit.NewElements.Count; i++)
            {
                var placed = commit.NewElements[i].element!;
                var root = _ctx.ZLayerPlaceholders.TryGetValue(placed, out var real) ? real : placed;
                // MUTANT_SURVIVES(equivalent, boundary): no claim carries this emission's number before this loop
                // writes it, so a claim equal to it is never read.
                if (_ctx.PresenceChildRoots.GetValueOrDefault(root).Emission > emission) continue;
                _ctx.PresenceChildRoots[root] = new ReconcilerContext.PresenceChildRootOwner(roots, emission, state, key);
                roots.Add(root);
            }
        }

        // Resolves the from/to class arrays for an `initial` variant enter: fromClasses =
        // variants[Initial], toClasses = variants[Animate]. Returns false (no
        // variant-initial enter; caller falls back to the classic transition) unless the Motion sets its own
        // Initial + Animate + Variants, the initial label names a pose, and the enter has a transition. A pose applying no class starts the
        // enter from the Motion's own classes, as a Framer `initial` naming no value starts each animated value
        // from the one it already has. Internal (not private): FiberNodeFactory calls this too, to play the same
        // variant enter on a standalone Motion (outside any AnimatePresence) at element-creation time.
        // transition is what the enter plays on: the TARGET variant's own (variants[Animate]) when it declares
        // one, else the Motion's — the enter's destination pose is what an enter's timing belongs to, the same
        // way the exit resolution below reads variants[Exit] rather than the resting pose it leaves.
        internal static bool TryResolveVariantInitial(MotionNode? motion, out string[]? fromClasses,
            out string[]? toClasses, out StyleTransitionConfig? transition)
            => TryResolveVariantEnter(motion, motion?.Initial, motion?.Animate, out fromClasses, out toClasses,
                out transition);

        // TryResolveVariantInitial on the labels a Motion resolves, own or inherited.
        internal static bool TryResolveVariantEnter(MotionNode? motion, string? initialLabel, string? animateLabel,
            out string[]? fromClasses, out string[]? toClasses, out StyleTransitionConfig? transition)
        {
            fromClasses = null;
            toClasses = null;
            transition = null;
            if (initialLabel == null || animateLabel == null || motion?.Variants == null
                || !motion.Variants.TryGetValue(initialLabel, out var from))
            {
                return false;
            }

            motion.Variants.TryGetValue(animateLabel, out var to);
            fromClasses = V.ParseClassNames(from.ClassName);
            toClasses = V.ParseClassNames(to.ClassName ?? string.Empty);
            transition = to.Transition ?? motion.Transition;
            return transition != null;
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
                || !motion.Variants.TryGetValue(motion.Exit, out var exit))
            {
                return false;
            }

            motion.Variants.TryGetValue(motion.Animate, out var resting);
            restingClass = resting.ClassName ?? string.Empty;
            exitClass = exit.ClassName ?? string.Empty;
            transition = exit.Transition ?? motion.Transition;
            return true;
        }

        // Builds the exit transition for an `exit` variant: the element animates from its resting
        // variants[Animate] (ExitFromClass) to variants[Exit] (ExitToClass), on the timing
        // ResolveExitTransition resolves, before unmount. Returns null (caller falls back to the classic
        // transition) unless the Motion sets its own Exit + Animate + Variants, the exit label names a pose,
        // and a transition resolves for it. The caller supplies the element the swap targets — the Motion's
        // own, so a wrapped Motion's exit variant animates the same element its resting variant classes
        // live on. restingOverride stands in for variants[Animate] as the pose the exit starts from, for an
        // element resting at another.
        internal static StyleTransitionConfig? TryResolveVariantExit(MotionNode? motion, string? restingOverride = null)
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
            return transition.WithExitClasses(restingOverride ?? restingClass, exitClass);
        }

        #endregion
    }
}
