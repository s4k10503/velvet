using System;
using System.Collections.Generic;

namespace Velvet
{
    // The commit-time effect phase — passive/layout/insertion effect running. After a reconcile
    // commits the host tree, this runs UseInsertionEffect -> UseLayoutEffect synchronously and schedules
    // UseEffect for the next frame boundary, in post-order (children before parents). Also owns effect
    // cleanup on unmount / orphaning and the deferred-inline / passive drains. Self-contained over
    // ComponentFiber + ReconcilerContext state; the imperative-handle commit it shares lives in
    // FiberHookCommit and is called back into here.
    internal static class FiberEffects
    {
        #region Effects

        // Runs insertion effects (Hooks.UseInsertionEffect) synchronously.
        // Insertion effects fire before layout effects of the same commit, so every commit site invokes this
        // immediately before the layout-effect cleanup pass.
        private static void RunInsertionEffects(ComponentFiber fiber, bool mountDoubleInvoke = false)
            => HookEffectExecutor.RunPendingEffects(fiber, fiber.PendingInsertionEffects, mountDoubleInvoke);

        // Runs, ahead of a ref setup, the insertion effects pending on the component that rendered the ref's element and
        // on the components above it, child before parent, as React's mutation phase runs a commit's insertion effects
        // before its layout phase attaches refs; the layout commit then finds them run. It runs no StrictMode mount
        // double-invoke: an entry's mount flag sits on the deferred stack, which this does not search.
        internal static void RunInsertionEffectsAheadOfRef(ComponentFiber? owner)
        {
            for (var fiber = owner; fiber != null; fiber = fiber.Parent)
            {
                // A tree a Suspense hides commits none of the layout work its render queued, as TakeBatch says.
                if (fiber.LayoutEffectsHidden) continue;
                HookEffectExecutor.RunPendingEffects(fiber, fiber.PendingInsertionEffects);
            }
        }

        internal static void CleanupAllInsertionEffects(ComponentFiber fiber)
            => HookEffectExecutor.CleanupAll(fiber, fiber.InsertionEffects, fiber.PendingInsertionEffects);

        // Single-source-of-truth commit sequence invoked by every top-level commit entry
        // (Mount, FlushState, ContinueReconcile).
        // Bundles the deferred-descendants drain + this fiber's own insertion / handle / layout /
        // scheduled-passive sub-passes so the order is documented in one place and a future
        // sub-pass insertion (or a 2-phase separation of insertion vs layout across the entire
        // subtree, splitting DOM mutation from layout-effect commit) edits a single
        // site instead of three. Layout effects walk bottom-up so every
        // deferred descendant commits before this fiber's own layout effects.
        internal static void CommitSubtreeEffects(ComponentFiber fiber, bool mountDoubleInvoke = false)
        {
            // During a batch drain, defer the effect commit to the end of the drain so all fibers' renders
            // (mutations) land before any layout effect runs (commit-phase order). Outside a drain
            // (mount / synchronous flush) run inline as before — there is no other fiber to order against.
            var ctx = fiber.Reconciler?.Context;
            if (ctx is { DeferDrainLayoutEffects: true })
            {
                ctx.PendingDrainLayoutEffects.Add((fiber, mountDoubleInvoke));
                return;
            }
            // A fiber without a reconciler has been unmounted.
            if (ctx == null) return;
            if (ctx.HeldRowLayoutEffects.Count > 0)
            {
                ctx.HeldRowLayoutEffects.Peek().Add((fiber, mountDoubleInvoke));
                return;
            }
            CommitLayoutBatch(ctx, fiber, new List<(ComponentFiber Fiber, bool IsMount)>(1) { (fiber, mountDoubleInvoke) });
        }

        // Runs the effect commits deferred during a batch drain (see CommitSubtreeEffects) after every fiber in
        // the batch has rendered, as one commit, so the layout cleanups of every fiber the drain re-rendered run
        // before any of their setups. Called at the end of the outer drain. The defer flag is cleared first so a
        // layout effect that triggers further work commits inline.
        internal static void FlushDeferredDrainLayoutEffects(ReconcilerContext ctx)
        {
            ctx.DeferDrainLayoutEffects = false;
            CommitDeferred(ctx, ctx.PendingDrainLayoutEffects);
        }

        // Runs the effect commits a range render held, once it has placed its rows. Each is scoped to its row, as
        // the commit of the row's own mount was: the pass that renders the list can hold fibers of its own whose
        // refs it has not attached yet. A row a failed or disposed range discarded is unmounted, which cleared its
        // pending lists, and CommitLayoutBatch runs no setup of an unmounted fiber, so it runs nothing. The range's
        // caller commits the stranded work after.
        internal static void CommitHeldRowLayoutEffects(
            ReconcilerContext ctx, List<(ComponentFiber fiber, bool mountDoubleInvoke)> held)
        {
            for (var i = 0; i < held.Count; i++)
            {
                var (fiber, isMount) = held[i];
                CommitLayoutBatch(ctx, fiber, new List<(ComponentFiber Fiber, bool IsMount)>(1) { (fiber, isMount) });
            }
        }

        private static void CommitDeferred(ReconcilerContext ctx, List<(ComponentFiber fiber, bool mountDoubleInvoke)> pending)
        {
            // Clear in a finally so a throwing effect (e.g. an imperative-handle factory, which is unguarded
            // user code) does not leave entries to accumulate / re-run on the next drain.
            try
            {
                var roots = new List<(ComponentFiber Fiber, bool IsMount)>(pending.Count);
                for (var i = 0; i < pending.Count; i++)
                {
                    if (pending[i].fiber.IsMounted) roots.Add(pending[i]);
                }
                CommitLayoutBatch(ctx, null, roots);
            }
            finally
            {
                pending.Clear();
            }
            CommitStrandedLayoutWork(ctx);
        }

        // React disconnects the layout effects of a tree a Suspense hides — its imperative handles among them —
        // keeping its state, and reconnects them when the tree is revealed. While hidden, the fiber's layout work
        // is dropped from the commit (TakeBatch), a mount's included: one this render first mounted under the
        // Suspense has set nothing up for the cleanups below to take down, and the reveal sets it up.
        internal static void HideLayoutEffects(ComponentFiber fiber)
        {
            // Each pass that renders the boundary while it shows its fallback hides the fiber again, and must not
            // clear a ref another component has written since.
            if (fiber.LayoutEffectsHidden) return;
            fiber.LayoutEffectsHidden = true;
            HookEffectExecutor.RunCleanups(fiber, fiber.LayoutEffects);
            if (fiber.ImperativeHandleSlots == null) return;
            foreach (var slot in fiber.ImperativeHandleSlots) slot.HandleRef?.Set(null);
        }

        // Queues every layout effect of a hidden fiber for the commit that reveals it, whatever its deps, has that
        // commit create every imperative handle again — from the last committed factory where the reveal does not
        // render the fiber — as React calls create() on reconnect: the hidden elements were removed, and a handle
        // built over one would outlive it. The passive work held while hidden is scheduled with them.
        internal static void ShowLayoutEffects(ComponentFiber fiber, ReconcilerContext ctx)
        {
            if (!fiber.LayoutEffectsHidden) return;
            fiber.LayoutEffectsHidden = false;
            if (fiber.LayoutEffects != null)
            {
                var pending = fiber.PendingLayoutEffects ??= new List<HookEffectSlot>();
                foreach (var slot in fiber.LayoutEffects)
                {
                    if (!pending.Contains(slot)) pending.Add(slot);
                }
            }
            if (fiber.ImperativeHandleSlots != null)
            {
                foreach (var slot in fiber.ImperativeHandleSlots)
                {
                    if (slot.NextFactory == null)
                    {
                        slot.NextFactory = slot.Factory;
                        slot.NextHandleRef = slot.HandleRef;
                    }
                    slot.NextNeedsRecompute = true;
                }
            }
            ctx.DeferredInlineLayoutEffectFibers.Push((fiber, IsMount: false, ctx.CurrentPass));
            ScheduleRunEffects(fiber);
        }

        // Commits the inline fibers on the deferred stack and the pending caught-error reports where no commit
        // is on the stack to do it. An entry that commits, or that renders or runs effects, has to end with this
        // call: a follow-up commit its layout setups left waits here, and so does a fallback a boundary showed
        // where no commit of a live root reaches it. A boundary's catch calls it as well, which is what commits a
        // fallback shown from a frame callback. With a render, a reconcile, a batch drain, an effect commit or a
        // ref-setup drain on the stack it does nothing, and the call at the end of what encloses it does the work.
        // What a parked time-sliced pass pushed stays on the stack for the commit that completes the pass.
        internal static void CommitStrandedLayoutWork(ReconcilerContext ctx)
        {
            if (IsCommitOnTheStack(ctx)) return;
            var commits = 0;
            while (HasStrandedWork(ctx))
            {
                // Bounded, so a chain of follow-up commits that never settles surfaces as this exception rather
                // than a hang.
                if (commits++ == MaxFollowUpCommits)
                {
                    DropStrandedWork(ctx);
                    throw new InvalidOperationException(
                        $"Velvet: layout effects kept leaving work for a follow-up commit, {MaxFollowUpCommits} follow-up"
                        + " commits in a row.");
                }
                CommitLayoutBatch(ctx, null, s_noRoots);
            }
            // What is left undelivered is held by a parked pass or names a boundary that is gone, which nothing
            // delivers.
            ctx.PendingCaughtErrorReports.RemoveAll(static report => !report.Boundary.IsMounted);
        }

        private const int MaxFollowUpCommits = 100;

        private static readonly List<(ComponentFiber Fiber, bool IsMount)> s_noRoots = new();

        private static bool IsCommitOnTheStack(ReconcilerContext ctx)
            => FiberAmbientStack.Current != null
                || ctx.SharedReconcileDepth > 0
                || ctx.DeferDrainLayoutEffects
                || ctx.HeldRowLayoutEffects.Count > 0
                || ctx.EffectCommitDepth > 0
                || ctx.IsDrainingRefAttaches;

        private static bool HasStrandedWork(ReconcilerContext ctx)
        {
            foreach (var entry in ctx.DeferredInlineLayoutEffectFibers)
            {
                if (!IsHeld(entry.Pass)) return true;
            }
            var reports = ctx.PendingCaughtErrorReports;
            for (var i = 0; i < reports.Count; i++)
            {
                if (IsDue(ctx, reports[i], null)) return true;
            }
            return false;
        }

        // Keeps what a parked pass holds and drops the rest, which the bound gave up on: left in place, it would
        // run the next entry to end with CommitStrandedLayoutWork into the same chain.
        private static void DropStrandedWork(ReconcilerContext ctx)
        {
            var stack = ctx.DeferredInlineLayoutEffectFibers;
            var held = new List<(ComponentFiber Fiber, bool IsMount, Reconciler? Pass)>(stack.Count);
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                if (IsHeld(entry.Pass)) held.Add(entry);
            }
            for (var i = held.Count - 1; i >= 0; i--) stack.Push(held[i]);
            ctx.PendingCaughtErrorReports.RemoveAll(report => IsDue(ctx, report, null));
        }

        // A parked pass holds back what it pushed, as DrainRefAttaches holds back the ref setups it queued: its
        // elements are not all in place, and the ref setups it queued have not run.
        private static bool IsHeld(Reconciler? pass) => pass is { HasPendingWork: true };

        // A boundary an ancestor boundary replaced before its report was delivered is gone before its fallback
        // commits, and its report is dropped with it.
        private static bool IsDue(
            ReconcilerContext ctx,
            (ComponentFiber Boundary, Exception Error, ErrorInfo Info, long Sequence) report,
            ComponentFiber? scope)
            => report.Boundary.IsMounted && !HasLayoutEffectsWaiting(ctx, report.Boundary)
                && IsInScope(report.Boundary, scope);

        // A boundary whose own entry still waits on the stack reports in the commit that takes the entry, after its
        // own layout effects: one a parked pass mounted or re-rendered, in the commit that completes the pass.
        private static bool HasLayoutEffectsWaiting(ReconcilerContext ctx, ComponentFiber boundary)
        {
            foreach (var entry in ctx.DeferredInlineLayoutEffectFibers)
            {
                if (ReferenceEquals(entry.Fiber, boundary)) return true;
            }
            return false;
        }

        // scope null is the whole tree. Otherwise it is the fiber being committed and what lies below it; what
        // lies elsewhere is another part of the tree, which the CommitStrandedLayoutWork ending the entry
        // commits after this commit.
        private static bool IsInScope(ComponentFiber fiber, ComponentFiber? scope)
        {
            if (scope == null) return true;
            for (ComponentFiber? f = fiber; f != null; f = f.Parent)
            {
                if (ReferenceEquals(f, scope)) return true;
            }
            return false;
        }

        // One layout commit over roots, the inline fibers on the deferred stack and the boundaries with a report
        // due, all within scope. It runs every layout-effect cleanup before any setup — a later fiber's cleanup
        // must not observe state an earlier fiber's setup wrote — and both passes in post-order: a child before
        // its parent, so a parent setup observes a child's already-committed imperative handle, and the fibers
        // under one parent in the order they rendered, a root or a boundary joining them at its position in the
        // tree. Insertion effects belong to the cleanup pass (the mutation phase); imperative handles and layout
        // setups to the setup pass. A boundary's reports are delivered right after its own setups: after the
        // layout effects of the fallback below it, before its ancestors'.
        private static void CommitLayoutBatch(
            ReconcilerContext ctx, ComponentFiber? scope, List<(ComponentFiber Fiber, bool IsMount)> roots)
        {
            ctx.EffectCommitDepth++;
            // A catch during this commit's own setups showed a fallback this commit has not laid out, so its
            // report waits for the follow-up commit that does.
            var reportCutoff = ctx.NextCaughtErrorSequence;
            try
            {
                var ordered = TakeBatch(ctx, scope, roots);
                // A fiber a cleanup's catch unmounts in this pass has had these lists cleared by its unmount.
                for (var i = 0; i < ordered.Count; i++)
                {
                    var (fiber, isMount) = ordered[i];
                    RunInsertionEffects(fiber, mountDoubleInvoke: isMount);
                    HookEffectExecutor.RunCleanups(fiber, fiber.PendingLayoutEffects);
                }
                for (var i = 0; i < ordered.Count; i++)
                {
                    var (fiber, isMount) = ordered[i];
                    // A setup's catch can unmount a fiber later in this pass, and a boundary unmounted that way
                    // must not deliver the report it still holds.
                    if (!fiber.IsMounted) continue;
                    // After every cleanup above, so what the insertion effects and layout cleanups wrote is in the
                    // layout the setups read, and per fiber, so what an earlier setup wrote is in it too.
                    if (FiberLayoutReflow.ReadsLayout(fiber)) FiberLayoutReflow.LayOutFor(ctx, fiber);
                    FiberHookCommit.RunImperativeHandleSlots(fiber);
                    HookEffectExecutor.RunFactoriesAndClear(fiber, fiber.PendingLayoutEffects, mountDoubleInvoke: isMount);
                    DeliverCaughtErrors(ctx, fiber, reportCutoff);
                }
                // A setup that mounted more inline children, a boundary's fallback among them, pushed them onto
                // the stack: they are a follow-up commit, which CommitStrandedLayoutWork runs once every commit
                // the entry holds is done.
                for (var i = 0; i < roots.Count; i++)
                {
                    ScheduleRunEffects(roots[i].Fiber, roots[i].IsMount);
                }
            }
            finally
            {
                ctx.EffectCommitDepth--;
            }
        }

        // Takes the batch off the stack and orders it. The stack was populated in DFS pre-order during reconcile
        // (parent before children, siblings left-to-right); a naive LIFO drain would run sibling B before
        // sibling A. What the batch does not take goes back in its push order. An effect that synchronously
        // pushes MORE deferred fibers lands on the stack for a later batch, not this already-captured one.
        private static List<(ComponentFiber Fiber, bool IsMount)> TakeBatch(
            ReconcilerContext ctx, ComponentFiber? scope, List<(ComponentFiber Fiber, bool IsMount)> roots)
        {
            var stack = ctx.DeferredInlineLayoutEffectFibers;
            var popped = new List<(ComponentFiber Fiber, bool IsMount, Reconciler? Pass)>(stack.Count);
            while (stack.Count > 0) popped.Add(stack.Pop());
            var taken = new List<(ComponentFiber Fiber, bool IsMount)>(popped.Count);
            for (var i = popped.Count - 1; i >= 0; i--)
            {
                var entry = popped[i];
                // MUTANT_SURVIVES(equivalent, guard removed): a fiber no longer mounted runs nothing in a batch.
                // Its unmount cleared its effect lists and handles, the setup pass skips it, and detached, it
                // precedes and follows nothing, so InsertInTreeOrder places nothing around it.
                if (!entry.Fiber.IsMounted) continue;
                // A tree a Suspense hides commits none of the layout work its render queued, as React discards
                // that work; ShowLayoutEffects queues all of it again for the commit that reveals it.
                if (entry.Fiber.LayoutEffectsHidden) continue;
                if (!IsHeld(entry.Pass) && IsInScope(entry.Fiber, scope))
                {
                    taken.Add((entry.Fiber, entry.IsMount));
                    continue;
                }
                stack.Push(entry);
            }

            // Dedup: the same fiber pushed twice (MountInline + a follow-up SubsumeFiberIntoThisPass bundled in
            // the same reconcile pass) must drain ONCE. Walk in reverse so the last entry wins — Mount is
            // architecturally first, so IsMount=false (the update) prevails. A root already pushed keeps its entry.
            var bufferPool = ctx.BufferPool;
            var pushedSet = bufferPool.RentFiberSet();
            var deduped = new List<(ComponentFiber Fiber, bool IsMount)>(taken.Count + roots.Count);
            for (var i = taken.Count - 1; i >= 0; i--)
            {
                if (pushedSet.Add(taken[i].Fiber)) deduped.Add(taken[i]);
            }
            deduped.Reverse();

            // The roots and the boundaries with a report due join what was pushed at their positions in the tree:
            // a drain's roots arrive in the order it flushed them, a root can sit below another root among fibers
            // that were pushed, and a boundary that caught shows its fallback without rendering, so unless it
            // rendered as well nothing pushed it.
            for (var i = roots.Count - 1; i >= 0; i--)
            {
                if (pushedSet.Add(roots[i].Fiber)) InsertInTreeOrder(deduped, roots[i]);
            }
            var reports = ctx.PendingCaughtErrorReports;
            for (var i = 0; i < reports.Count; i++)
            {
                if (IsDue(ctx, reports[i], scope) && pushedSet.Add(reports[i].Boundary))
                {
                    // MUTANT_SURVIVES(equivalent, literal): the mount flag reaches nothing on this boundary.
                    // One that joins here has not rendered since its last commit, so no layout or insertion
                    // effect of its own is pending.
                    InsertInTreeOrder(deduped, (reports[i].Boundary, false));
                }
            }

            var ordered = new List<(ComponentFiber Fiber, bool IsMount)>(deduped.Count);
            OrderByNearestStagedAncestorPostOrder(deduped, pushedSet, static e => e.Fiber, ordered);
            bufferPool.ReturnFiberSet(pushedSet);
            return ordered;
        }

        // The entry goes ahead of the first fiber it precedes in the tree. Its ancestors and descendants it
        // precedes neither way, and the post-order walk groups each under the nearest fiber of the batch above
        // it wherever it sits.
        private static void InsertInTreeOrder(
            List<(ComponentFiber Fiber, bool IsMount)> deduped, (ComponentFiber Fiber, bool IsMount) entry)
        {
            var slot = deduped.Count;
            for (var i = 0; i < deduped.Count; i++)
            {
                if (!PrecedesInTreeOrder(entry.Fiber, deduped[i].Fiber)) continue;
                slot = i;
                break;
            }
            deduped.Insert(slot, entry);
        }

        // Of two branches under one parent, the one earlier in its child list precedes. A fiber and its ancestor
        // or descendant read false both ways, as do two fibers with no common ancestor.
        private static bool PrecedesInTreeOrder(ComponentFiber a, ComponentFiber b)
        {
            for (var branchOfA = a; branchOfA.Parent != null; branchOfA = branchOfA.Parent)
            {
                var parent = branchOfA.Parent;
                ComponentFiber? branchOfB = null;
                for (var x = b; x != null; x = x.Parent)
                {
                    if (!ReferenceEquals(x.Parent, parent)) continue;
                    branchOfB = x;
                    break;
                }
                if (branchOfB == null) continue;
                // Both are children of parent, so the walk meets one of them.
                for (var sibling = parent.Child; !ReferenceEquals(sibling, branchOfB); sibling = sibling!.Sibling)
                {
                    if (ReferenceEquals(sibling, branchOfA)) return true;
                }
                return false;
            }
            // MUTANT_SURVIVES(unreachable, literal): every fiber a batch orders is mounted, and mounted fibers of one
            // context descend from its root fiber, so two of them always meet under a common parent above.
            return false;
        }

        // reportCutoff excludes a report caught after the batch delivering it was collected. CaughtErrorHandlerTests
        // holds the order the reports are delivered in.
        private static void DeliverCaughtErrors(ReconcilerContext ctx, ComponentFiber boundary, long reportCutoff)
        {
            var reports = ctx.PendingCaughtErrorReports;
            List<(Exception Error, ErrorInfo Info)>? due = null;
            var kept = 0;
            for (var i = 0; i < reports.Count; i++)
            {
                var report = reports[i];
                if (ReferenceEquals(report.Boundary, boundary) && report.Sequence < reportCutoff
                    && !HasLayoutEffectsWaiting(ctx, boundary))
                {
                    due ??= new List<(Exception Error, ErrorInfo Info)>();
                    due.Add((report.Error, report.Info));
                    continue;
                }
                reports[kept++] = report;
            }
            reports.RemoveRange(kept, reports.Count - kept);
            if (due == null) return;
            for (var i = 0; i < due.Count; i++)
            {
                FiberErrorBoundary.ReportCaughtError(ctx, due[i].Error, due[i].Info);
            }
        }

        internal static void CleanupAllLayoutEffects(ComponentFiber fiber)
            => HookEffectExecutor.CleanupAll(fiber, fiber.LayoutEffects, fiber.PendingLayoutEffects);

        // Runs the orphan fiber's effect cleanups WITHOUT touching DOM or finalizing dispose.
        // When a fiber is deleted, its layout effect cleanups must run BEFORE the deleted fiber's child host elements have
        // their refs detached and DOM removed. ChildReconciler invokes this on each orphan fiber
        // before its DOM-removal pass so cleanup closures observe still-valid refs.
        // Idempotent: subsequent CleanupAll passes (e.g. from Unmount) iterate empty
        // lists since CleanupAll clears the lists after running.
        internal static void RunOrphanFiberEffectCleanups(ComponentFiber fiber)
        {
            if (fiber == null || !fiber.IsMounted || fiber.IsDisposed) return;
            CleanupAllInsertionEffects(fiber);
            CleanupAllLayoutEffects(fiber);
            CleanupAllEffects(fiber);
        }

        // Schedules RunEffects exactly once at the next frame boundary.
        // Does not schedule duplicates even when multiple renders run consecutively.
        // mountDoubleInvoke is captured here (rather than at run time) because the async
        // effect runs after this scheduling site has returned: only the mount commit's schedule sets it true.
        // When a Mount-scheduled paint-tick is bundled with a subsequent update commit (parent
        // re-render reaches the same inline child via SubsumeFiberIntoThisPass before
        // the paint-tick fires), the early-return path downgrades PendingEffectsAreMount from
        // true to false so the mixed drain does not spuriously double-invoke update-staged entries
        // during the Editor-only double-invoke. The downgrade is one-way (true → false only); upgrading on a later
        // Mount call would require Mount to follow an update commit on the same fiber, which the
        // current architecture precludes (MountInline is the entry that creates the slot, update
        // commits always come after). The tradeoff: Mount-staged entries bundled with an update
        // lose their own mount double-invoke. A per-slot origin tag would preserve both
        // behaviors but is a more invasive refactor (deferred).
        internal static void ScheduleRunEffects(ComponentFiber fiber, bool mountDoubleInvoke = false)
        {
            if (fiber.PendingEffects == null || fiber.PendingEffects.Count == 0) return;
            if (fiber.EffectFlushScheduled)
            {
#if UNITY_EDITOR
                // A mount commit and a subsequent update commit can both schedule effects before the
                // first paint-tick fires (parent re-render reaches the same inline child via
                // SubsumeFiberIntoThisPass before MountInline's scheduled drain runs). The bundled
                // drain mixes Mount-staged and update-staged effects; downgrade the
                // mount-double-invoke flag so update-staged effects are not spuriously double-invoked.
                if (!mountDoubleInvoke) fiber.PendingEffectsAreMount = false;
#endif
                return;
            }
            if (fiber.MountPoint == null) return;
            fiber.EffectFlushScheduled = true;
#if UNITY_EDITOR
            fiber.PendingEffectsAreMount = mountDoubleInvoke;
#endif
            // Register the fiber into the context-level pending set and schedule a single tree-wide
            // drain (instead of one schedule.Execute(RunEffects) per fiber). The drain runs ALL
            // pending fibers' passive cleanups before ANY setup, both phases post-order
            // (child-before-parent) — the 2-phase passive commit. The per-fiber callback path
            // could only run a fiber's own cleanup+setup pair in scheduler order, never grouping
            // cleanups vs setups across fibers. Timing is unchanged: the drain still fires
            // asynchronously on the host scheduler (post-paint), not synchronously like layout effects.
            var context = fiber.Reconciler?.Context;
            if (context == null)
            {
                // No shared context (defensive): fall back to the standalone single-fiber drain.
                fiber.MountPoint.schedule.Execute(() => PanelSchedulerCallback.Run(fiber.MountPoint, fiber, RunEffects));
                return;
            }
            if (context.PendingPassiveEffectFiberSet.Add(fiber))
            {
                context.PendingPassiveEffectFibers.Add(fiber);
            }
            if (context.PassiveEffectDrainScheduled) return;
            // One drain serves the whole context and only that drain clears the latch below, so the
            // registration has to sit on a host outliving any single subtree: the batch scheduler's
            // tree-stable anchor, the same hazard SetAnchor exists for, one level out. Whichever fiber
            // stages first is the one that registers, and that is routinely a descendant.
            // PassiveEffectDrainHostTests fails if this moves back onto the staging fiber's MountPoint.
            var anchor = context.BatchScheduler.Anchor;
            // Armed only for a registration that actually happened: a context whose scheduler has no anchor
            // must not latch a drain nothing will run. That the production mount path sets the anchor before
            // any fiber stages — so this return is never on it — is held by PassiveEffectDrainArmingTests.
            if (anchor == null) return;
            context.PassiveEffectDrainScheduled = true;
            anchor.schedule.Execute(() => PanelSchedulerCallback.Run(anchor, context, DrainPassiveEffects));
        }

        // Tree-ordered, 2-phase passive (UseEffect) commit across every fiber staged in the current
        // batch. Phase 1 runs every pending fiber's effect cleanups; phase 2 runs every pending
        // fiber's effect setups. Both phases walk the staged fibers in post-order
        // (child-before-parent) so a child's passive effect commits before its parent's — mirroring
        // the layout-effect drain's ordering, but deferred to the post-paint scheduler tick rather
        // than committed synchronously. Reconstructs post-order from fiber.Parent ancestry
        // the same way TakeBatch does for the layout commit.
        private static void DrainPassiveEffects(ReconcilerContext context)
        {
            context.PassiveEffectDrainScheduled = false;
            // Snapshot and clear the staged set before running: an effect setup can synchronously
            // stage further passive effects (setState in a UseEffect), which must enqueue a NEW drain
            // rather than mutate the list we are iterating.
            if (context.PendingPassiveEffectFibers.Count == 0) return;
            context.EffectCommitDepth++;
            try
            {
                RunPassivePhases(context);
            }
            finally
            {
                context.EffectCommitDepth--;
            }
            CommitStrandedLayoutWork(context);
        }

        private static void RunPassivePhases(ReconcilerContext context)
        {
            var ordered = OrderFibersPostOrder(context.PendingPassiveEffectFibers);
            context.PendingPassiveEffectFibers.Clear();
            context.PendingPassiveEffectFiberSet.Clear();
            // A tree a Suspense hides runs none of the passive work its render queued, as React commits none of
            // that render; ShowLayoutEffects schedules what is held for the reveal.
            ordered.RemoveAll(HoldWhileHidden);

            // Phase 1: all cleanups, post-order. Clear EffectFlushScheduled here so a cleanup that
            // re-stages the same fiber re-arms scheduling cleanly.
            for (var i = 0; i < ordered.Count; i++)
            {
                var fiber = ordered[i];
                fiber.EffectFlushScheduled = false;
                if (fiber.IsMounted) HookEffectExecutor.RunCleanups(fiber, fiber.PendingEffects);
            }

            // Phase 2: all setups, post-order. Factories + the Editor-only mount double-invoke run
            // here, after every cleanup, matching the cleanup-before-setup ordering.
            for (var i = 0; i < ordered.Count; i++)
            {
                var fiber = ordered[i];
                if (!fiber.IsMounted)
                {
                    fiber.PendingEffects?.Clear();
#if UNITY_EDITOR
                    fiber.PendingEffectsAreMount = false;
#endif
                    continue;
                }
#if UNITY_EDITOR
                var mountDoubleInvoke = fiber.PendingEffectsAreMount;
                fiber.PendingEffectsAreMount = false;
                HookEffectExecutor.RunFactoriesAndClear(fiber, fiber.PendingEffects, mountDoubleInvoke);
#else
                HookEffectExecutor.RunFactoriesAndClear(fiber, fiber.PendingEffects);
#endif
            }
        }

        private static bool HoldWhileHidden(ComponentFiber fiber)
        {
            if (!fiber.LayoutEffectsHidden) return false;
            fiber.EffectFlushScheduled = false;
            return true;
        }

        // Reconstructs a post-order (child-before-parent, left-to-right) sequence over an arbitrary
        // subset of fibers using fiber.Parent ancestry — the same grouping
        // TakeBatch performs for layout effects. A fiber whose
        // nearest staged ancestor is present is emitted before that ancestor; staged roots keep their
        // relative staging order (siblings are staged left-to-right during reconcile).
        private static List<ComponentFiber> OrderFibersPostOrder(List<ComponentFiber> staged)
        {
            var stagedSet = new HashSet<ComponentFiber>(staged);
            var result = new List<ComponentFiber>(staged.Count);
            OrderByNearestStagedAncestorPostOrder(staged, stagedSet, static f => f, result);
            return result;
        }

        // The one post-order-by-nearest-staged-ancestor implementation shared by the layout-effect drain
        // (entries carry an IsMount flag) and the passive-effect drain (plain fibers): each staged entry is
        // grouped under its NEAREST staged ancestor — non-staged intermediates are skipped, so wrapper-mount
        // subtrees whose middle ancestors did not stage collapse naturally — then an iterative post-order
        // DFS over the resulting forest emits children before their parent into result, roots in their
        // relative staging order. Iterative (stack-based, recursion-free) to prevent .NET stack overflow on
        // deep inline-mount chains (1k+ nested Provider / Memo subtrees); the same iterative pattern that
        // FiberTreeTraversal.Visit documented away. fiberOf must be a compiler-cached static lambda so this
        // stays allocation-free per call beyond the grouping collections themselves.
        private static void OrderByNearestStagedAncestorPostOrder<T>(
            List<T> staged, HashSet<ComponentFiber> stagedSet, Func<T, ComponentFiber> fiberOf, List<T> result)
        {
            Dictionary<ComponentFiber, List<T>>? childrenByParent = null;
            var roots = new List<T>();
            for (var i = 0; i < staged.Count; i++)
            {
                var entry = staged[i];
                var ancestor = fiberOf(entry).Parent;
                while (ancestor != null && !stagedSet.Contains(ancestor)) ancestor = ancestor.Parent;
                if (ancestor == null)
                {
                    roots.Add(entry);
                }
                else
                {
                    childrenByParent ??= new Dictionary<ComponentFiber, List<T>>();
                    if (!childrenByParent.TryGetValue(ancestor, out var list))
                    {
                        list = new List<T>();
                        childrenByParent[ancestor] = list;
                    }
                    list.Add(entry);
                }
            }

            var workStack = new Stack<(int ChildIndex, T Entry)>();
            for (var r = 0; r < roots.Count; r++)
            {
                workStack.Push((0, roots[r]));
                while (workStack.Count > 0)
                {
                    var (childIndex, entry) = workStack.Pop();
                    if (childrenByParent != null
                        && childrenByParent.TryGetValue(fiberOf(entry), out var children)
                        && childIndex < children.Count)
                    {
                        // Resume the parent after this child's subtree completes, then descend.
                        workStack.Push((childIndex + 1, entry));
                        workStack.Push((0, children[childIndex]));
                        continue;
                    }
                    result.Add(entry);
                }
            }
        }

        // Runs async effects in 2-pass order (all cleanup → all effect) for a single fiber. Retained
        // for the no-shared-context fallback in ScheduleRunEffects and for the test
        // flush helper. Does nothing if already unmounted (Cleanup runs from CleanupAllEffects).
        private static void RunEffects(ComponentFiber fiber)
        {
            fiber.EffectFlushScheduled = false;
            if (!fiber.IsMounted) return;
#if UNITY_EDITOR
            var mountDoubleInvoke = fiber.PendingEffectsAreMount;
            fiber.PendingEffectsAreMount = false;
            HookEffectExecutor.RunPendingEffects(fiber, fiber.PendingEffects, mountDoubleInvoke);
#else
            HookEffectExecutor.RunPendingEffects(fiber, fiber.PendingEffects);
#endif
        }

        internal static void CleanupAllEffects(ComponentFiber fiber)
        {
            HookEffectExecutor.CleanupAll(fiber, fiber.Effects, fiber.PendingEffects);
            fiber.EffectFlushScheduled = false;
        }

        // Synchronously runs the tree-wide, 2-phase passive-effect drain for the reconcile context that
        // rootFiber belongs to (all cleanups before all setups, post-order). Mirrors the
        // production post-paint drain but fires immediately, so tests / Editor tooling observe the
        // passive ordering without waiting for the host scheduler. No-op when no passive effects are
        // pending.
        public static void FlushPendingPassiveEffects(ComponentFiber rootFiber)
        {
            var context = rootFiber?.Reconciler?.Context;
            if (context == null)
            {
                // No shared context (defensive): fall back to the single-fiber flush.
                if (rootFiber != null) RunEffects(rootFiber);
                return;
            }
            FlushPendingPassiveEffects(context);
        }

        // Context overload: the batch scheduler drives this before a synchronous discrete-event flush so a
        // prior commit's pending passive effects run before the new update's render. A no-op when nothing
        // is pending.
        internal static void FlushPendingPassiveEffects(ReconcilerContext context)
        {
            // An effect setup may setState and stage a fresh batch synchronously; drain until quiescent.
            // Bounded to surface a runaway effect-stages-effect loop as a test failure rather than a hang.
            var guard = 0;
            while (context.PendingPassiveEffectFibers.Count > 0)
            {
                if (guard++ > 10000)
                {
                    throw new InvalidOperationException(
                        "FlushPendingPassiveEffects: passive effects kept re-staging without settling.");
                }
                DrainPassiveEffects(context);
            }
        }

        #endregion
    }
}
