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
            CommitSubtreeEffectsNow(fiber, mountDoubleInvoke);
        }

        private static void CommitSubtreeEffectsNow(ComponentFiber fiber, bool mountDoubleInvoke)
        {
            // React commits ALL layout-effect cleanups (the mutation phase) across a whole subtree before ANY
            // layout-effect setup (the layout phase). This root fiber and the inline children MountInline
            // deferred onto the drain stack form one subtree, so their layout effects run as one cleanup pass
            // then one setup pass with the root interleaved between: the root's cleanup runs before a child's
            // setup, so a child setup that writes shared state can no longer be observed by the root's cleanup —
            // React's [child.cleanup, root.cleanup, child.setup, root.setup]. Committing each child fully before
            // the root began (the child's setup ahead of the root's cleanup) inverted that pair. Imperative
            // handles and layout setups belong to the setup pass; insertion effects run in the cleanup pass
            // (React runs them during mutation).
            var stack = fiber.Reconciler?.Context.DeferredInlineLayoutEffectFibers;
            List<(ComponentFiber Fiber, bool IsMount)>? inlineBatch = null;
            if (stack is { Count: > 0 })
            {
                inlineBatch = new List<(ComponentFiber Fiber, bool IsMount)>(stack.Count);
                DrainInlineLayoutCleanupsOneBatch(fiber, inlineBatch);
            }
            RunInsertionEffects(fiber, mountDoubleInvoke);
            HookEffectExecutor.RunCleanups(fiber, fiber.PendingLayoutEffects);

            if (inlineBatch != null) RunInlineLayoutSetups(inlineBatch);
            FiberHookCommit.RunImperativeHandleSlots(fiber);
            HookEffectExecutor.RunFactoriesAndClear(fiber, fiber.PendingLayoutEffects, mountDoubleInvoke);

            // A setup (a child's or this root's) that mounted more inline children pushed them onto the drain
            // stack: they are a subsequent pass, committed after this root's layout phase — React runs
            // effect-mounted work in a follow-up commit, not the current one.
            DrainDeferredInlineLayoutEffects(fiber);
            ScheduleRunEffects(fiber, mountDoubleInvoke);
        }

        // Runs the effect commits deferred during a batch drain (see CommitSubtreeEffects), in
        // collection order, after every fiber in the batch has rendered. Called at the end of the outer drain.
        // The defer flag is cleared first so a layout effect that triggers further work commits inline.
        internal static void FlushDeferredDrainLayoutEffects(ReconcilerContext ctx)
        {
            ctx.DeferDrainLayoutEffects = false;
            var pending = ctx.PendingDrainLayoutEffects;
            if (pending.Count == 0) return;
            // Clear in a finally so a throwing effect (e.g. an imperative-handle factory, which is unguarded
            // user code) does not leave entries to accumulate / re-run on the next drain.
            try
            {
                // Index-walk (not foreach): a deferred effect that schedules more work commits inline now (flag
                // is false), so the list does not grow here — but guard against it defensively.
                for (var i = 0; i < pending.Count; i++)
                {
                    var (fiber, mountDoubleInvoke) = pending[i];
                    if (fiber.IsMounted && !fiber.IsDisposed)
                    {
                        CommitSubtreeEffectsNow(fiber, mountDoubleInvoke);
                    }
                }
            }
            finally
            {
                pending.Clear();
            }
        }

        // Commits inline fibers still on the deferred-inline stack, each batch in its own cleanup-then-setup
        // two-phase. MountInline defers a fiber's layout effects here instead of running them at mount so they
        // observe the parent expansion's already-attached child refs; CommitSubtreeEffectsNow drains the first
        // batch interleaved with its own root, then calls this to pick up any fibers a setup mounted. The outer
        // loop re-drains because an inline setup can synchronously push MORE deferred fibers (e.g. a
        // UseLayoutEffect that mounts another inline child) — each such push is a fresh subtree, committed after
        // the one that mounted it, matching how React runs effect-mounted work in a follow-up commit.
        private static void DrainDeferredInlineLayoutEffects(ComponentFiber rootFiber)
        {
            var stack = rootFiber.Reconciler?.Context.DeferredInlineLayoutEffectFibers;
            if (stack == null) return;
            while (stack.Count > 0)
            {
                var ordered = new List<(ComponentFiber Fiber, bool IsMount)>(stack.Count);
                DrainInlineLayoutCleanupsOneBatch(rootFiber, ordered);
                RunInlineLayoutSetups(ordered);
            }
        }

        // Snapshots the current deferred-inline stack as one batch, orders it post-order (children before
        // parents), and runs the layout-effect CLEANUP pass over that order into `ordered` — per fiber, its
        // insertion effects then its layout-effect cleanups. The caller feeds the same `ordered` list to
        // RunInlineLayoutSetups for the SETUP pass; splitting the two lets CommitSubtreeEffectsNow interleave
        // its root fiber's own cleanup/setup between them, so React's all-cleanups-before-all-setups holds
        // across the root/child boundary and not just within the child batch. Ordering off a snapshot and
        // running after is equivalent to running mid-walk: an effect that synchronously pushes MORE deferred
        // fibers lands on the stack and is picked up by the caller's re-drain, not this already-captured batch.
        private static void DrainInlineLayoutCleanupsOneBatch(
            ComponentFiber rootFiber, List<(ComponentFiber Fiber, bool IsMount)> ordered)
        {
            var reconciler = rootFiber.Reconciler;
            if (reconciler == null) return;
            var stack = reconciler.Context.DeferredInlineLayoutEffectFibers;
            if (stack == null || stack.Count == 0) return;
            var bufferPool = reconciler.Context.BufferPool;

            // The stack was populated in DFS pre-order during reconcile (parent before children, siblings
            // left-to-right). A naive LIFO drain would run sibling B before sibling A — layout effects visit
            // siblings left-to-right then their parent (post-order DFS, LtR). Reconstruct that order.
            var entries = new List<(ComponentFiber Fiber, bool IsMount)>(stack.Count);
            while (stack.Count > 0) entries.Add(stack.Pop());
            entries.Reverse();

            // Dedup: the same fiber pushed twice (MountInline + a follow-up RenderInlineForExpansion bundled in
            // the same reconcile pass) must drain ONCE. Walk in reverse so the last entry wins — Mount is
            // architecturally first, so IsMount=false (the update) prevails.
            var pushedSet = bufferPool.RentFiberSet();
            var deduped = new List<(ComponentFiber Fiber, bool IsMount)>(entries.Count);
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (pushedSet.Add(entries[i].Fiber)) deduped.Add(entries[i]);
            }
            deduped.Reverse();

            OrderByNearestStagedAncestorPostOrder(deduped, pushedSet, static e => e.Fiber, ordered);
            bufferPool.ReturnFiberSet(pushedSet);

            // Cleanup pass — React's mutation phase. Running every cleanup before any setup is the point of the
            // split: a later fiber's cleanup must not observe state an earlier fiber's setup wrote. Insertion
            // effects belong to the mutation phase too; imperative handles and layout setups defer to the setup
            // pass so a parent setup can observe a child's already-committed handle.
            for (var i = 0; i < ordered.Count; i++)
            {
                var (deferred, isMount) = ordered[i];
                if (deferred == null || deferred.IsDisposed) continue;
                RunInsertionEffects(deferred, mountDoubleInvoke: isMount);
                HookEffectExecutor.RunCleanups(deferred, deferred.PendingLayoutEffects);
            }
        }

        // Runs the layout-effect SETUP pass over a batch DrainInlineLayoutCleanupsOneBatch already ordered and
        // cleaned up: each fiber's imperative handles then its layout-effect setups, in post-order (a parent
        // setup observes a child's already-committed handle). A setup that mounts more inline children pushes
        // them onto the drain stack for the caller's re-drain.
        private static void RunInlineLayoutSetups(List<(ComponentFiber Fiber, bool IsMount)> ordered)
        {
            for (var i = 0; i < ordered.Count; i++)
            {
                var (deferred, isMount) = ordered[i];
                if (deferred == null || deferred.IsDisposed) continue;
                FiberHookCommit.RunImperativeHandleSlots(deferred);
                HookEffectExecutor.RunFactoriesAndClear(deferred, deferred.PendingLayoutEffects, mountDoubleInvoke: isMount);
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
        // re-render reaches the same inline child via RenderInlineForExpansion before
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
                // RenderInlineForExpansion before MountInline's scheduled drain runs). The bundled
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
                fiber.MountPoint.schedule.Execute(() => RunEffects(fiber));
                return;
            }
            if (context.PendingPassiveEffectFiberSet.Add(fiber))
            {
                context.PendingPassiveEffectFibers.Add(fiber);
            }
            if (!context.PassiveEffectDrainScheduled)
            {
                context.PassiveEffectDrainScheduled = true;
                fiber.MountPoint.schedule.Execute(() => DrainPassiveEffects(context));
            }
        }

        // Tree-ordered, 2-phase passive (UseEffect) commit across every fiber staged in the current
        // batch. Phase 1 runs every pending fiber's effect cleanups; phase 2 runs every pending
        // fiber's effect setups. Both phases walk the staged fibers in post-order
        // (child-before-parent) so a child's passive effect commits before its parent's — mirroring
        // the layout-effect drain's ordering, but deferred to the post-paint scheduler tick rather
        // than committed synchronously. Reconstructs post-order from fiber.Parent ancestry
        // the same way DrainDeferredInlineLayoutEffects does.
        private static void DrainPassiveEffects(ReconcilerContext context)
        {
            context.PassiveEffectDrainScheduled = false;
            // Snapshot and clear the staged set before running: an effect setup can synchronously
            // stage further passive effects (setState in a UseEffect), which must enqueue a NEW drain
            // rather than mutate the list we are iterating.
            if (context.PendingPassiveEffectFibers.Count == 0) return;
            var ordered = OrderFibersPostOrder(context.PendingPassiveEffectFibers);
            context.PendingPassiveEffectFibers.Clear();
            context.PendingPassiveEffectFiberSet.Clear();

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

        // Reconstructs a post-order (child-before-parent, left-to-right) sequence over an arbitrary
        // subset of fibers using fiber.Parent ancestry — the same grouping
        // DrainDeferredInlineLayoutEffects performs for layout effects. A fiber whose
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

        // Explicitly runs async effects from tests / Editor. In the normal flow they run automatically via
        // schedule.Execute, so user code does not need to call this.
        // fiber: Fiber whose pending async effects should be drained synchronously.
        public static void FlushEffects(ComponentFiber fiber) => RunEffects(fiber);

        // Synchronously runs the tree-wide, 2-phase passive-effect drain for the reconcile context that
        // rootFiber belongs to (all cleanups before all setups, post-order). Mirrors the
        // production post-paint drain but fires immediately, so tests / Editor tooling observe the
        // passive ordering without waiting for the host scheduler. No-op when no passive effects are
        // pending. Use this instead of per-fiber FlushEffects to preserve cross-fiber order.
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
        // prior commit's pending passive effects run before the new update's render — React's
        // flushPassiveEffects-before-update. A no-op when nothing is pending.
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
