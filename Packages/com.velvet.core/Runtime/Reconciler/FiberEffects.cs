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

        // Runs the effects pending change that were queued during Render, in 2-pass order
        // (all cleanup → all effect). mountDoubleInvoke is forwarded so the
        // Editor-only effect double-cycle runs on the mount commit only.
        private static void RunLayoutEffects(ComponentFiber fiber, bool mountDoubleInvoke = false)
            => HookEffectExecutor.RunPendingEffects(fiber, fiber.PendingLayoutEffects, mountDoubleInvoke);

        // Runs insertion effects (Hooks.UseInsertionEffect) synchronously.
        // Insertion effects fire before layout effects of the same commit, so every commit site invokes this
        // immediately before RunLayoutEffects.
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
            DrainDeferredInlineLayoutEffects(fiber);
            RunInsertionEffects(fiber, mountDoubleInvoke);
            FiberHookCommit.RunImperativeHandleSlots(fiber);
            RunLayoutEffects(fiber, mountDoubleInvoke);
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

        // Drains layout effects that MountInline deferred while the parent
        // expansion was still committing child elements (DOM mutation + ref attach). Layout
        // effects run after the DOM mutations + ref attach are committed for the
        // entire subtree, so every inline-mounted fiber's UseLayoutEffect observes
        // already-attached refs. Top-level reconcile entries (Mount,
        // FlushState) invoke this before their own RunLayoutEffects
        // so the root commits last; MountInline never calls it directly because nested inline
        // mounts must all settle before any layout effect runs. Drained in LIFO order so the
        // deepest fiber runs first — layout effects commit children
        // before their parent (bottom-up), so a parent layout effect that reads a child's
        // imperative handle / measured size observes the child's already-applied effect.
        private static void DrainDeferredInlineLayoutEffects(ComponentFiber rootFiber)
        {
            var stack = rootFiber.Reconciler?.Context?.DeferredInlineLayoutEffectFibers;
            if (stack == null || stack.Count == 0) return;
            var bufferPool = rootFiber.Reconciler.Context.BufferPool;

            // Outer loop: an inline-effect callback can synchronously push more deferred fibers
            // (e.g., a UseLayoutEffect that mounts another inline child). The old `while Pop`
            // picked them up incrementally; our snapshot-based pass needs to re-drain explicitly.
            while (stack.Count > 0)
            {
                // The stack was populated in DFS pre-order during reconcile (parent before children,
                // siblings in left-to-right reconcile-walk order). Naive LIFO drain (`while Pop`)
                // would run sibling B before sibling A — layout effects visit
                // siblings left-to-right then their parent (post-order DFS, LtR). Reconstruct the
                // post-order sequence via fiber.Parent ancestry.
                var entries = new List<(ComponentFiber Fiber, bool IsMount)>(stack.Count);
                while (stack.Count > 0) entries.Add(stack.Pop());
                entries.Reverse();

                // Dedup: the same fiber pushed twice (MountInline + a follow-up
                // RenderInlineForExpansion bundled in the same reconcile pass) must drain ONCE,
                // not twice. Walk in reverse so the last (latest semantics) entry wins — Mount is
                // architecturally first in this scenario, so IsMount=false (the update) prevails.
                var pushedSet = bufferPool.RentFiberSet();
                var deduped = new List<(ComponentFiber Fiber, bool IsMount)>(entries.Count);
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    if (pushedSet.Add(entries[i].Fiber)) deduped.Add(entries[i]);
                }
                deduped.Reverse();

                // Group entries by their nearest pushed ancestor. Non-pushed intermediates are
                // skipped so wrapper-mount subtrees whose middle ancestors did not defer collapse
                // naturally.
                Dictionary<ComponentFiber, List<(ComponentFiber Fiber, bool IsMount)>> childrenByParent = null;
                var roots = new List<(ComponentFiber Fiber, bool IsMount)>();
                for (var i = 0; i < deduped.Count; i++)
                {
                    var entry = deduped[i];
                    var ancestor = entry.Fiber.Parent;
                    while (ancestor != null && !pushedSet.Contains(ancestor)) ancestor = ancestor.Parent;
                    if (ancestor == null)
                    {
                        roots.Add(entry);
                    }
                    else
                    {
                        childrenByParent ??= new Dictionary<ComponentFiber, List<(ComponentFiber Fiber, bool IsMount)>>();
                        if (!childrenByParent.TryGetValue(ancestor, out var list))
                        {
                            list = new List<(ComponentFiber Fiber, bool IsMount)>();
                            childrenByParent[ancestor] = list;
                        }
                        list.Add(entry);
                    }
                }

                for (var i = 0; i < roots.Count; i++) DrainPostOrderIterative(roots[i], childrenByParent);

                bufferPool.ReturnFiberSet(pushedSet);
            }
        }

        // Iterative post-order DFS drain. Recursion-free to prevent .NET stack overflow on deep
        // inline-mount chains (1k+ nested Provider / Memo subtrees); the same iterative pattern
        // that FiberTreeTraversal.Visit documented away.
        private static void DrainPostOrderIterative(
            (ComponentFiber Fiber, bool IsMount) root,
            Dictionary<ComponentFiber, List<(ComponentFiber Fiber, bool IsMount)>> childrenByParent)
        {
            var workStack = new Stack<(int ChildIndex, (ComponentFiber Fiber, bool IsMount) Entry)>();
            workStack.Push((0, root));
            while (workStack.Count > 0)
            {
                var (childIndex, entry) = workStack.Pop();
                if (childrenByParent != null && childrenByParent.TryGetValue(entry.Fiber, out var children) && childIndex < children.Count)
                {
                    // Resume parent after this child completes, then descend into the next child.
                    workStack.Push((childIndex + 1, entry));
                    workStack.Push((0, children[childIndex]));
                    continue;
                }
                // All children visited (or none) — commit this fiber's deferred effects.
                // Mount commits run the Editor-only double-invoke; re-expansion commits (parent
                // re-render reaching an existing inline child) only run prior cleanup + new setup
                // for deps-changed slots. Insertion effects fire before layout effects.
                // UseImperativeHandle commits between insertion and layout effects so a
                // parent layout effect / handle factory observes the child's handle already
                // written into its parent ref (imperative handles are part of the
                // layout-effect phase). 2-phase separation (all-insertion → all-layout across the
                // subtree, splitting DOM mutation from layout-effect commit) is tracked
                // separately for the broader CommitSubtreeEffects refactor.
                var deferred = entry.Fiber;
                if (deferred == null || deferred.IsDisposed) continue;
                RunInsertionEffects(deferred, mountDoubleInvoke: entry.IsMount);
                FiberHookCommit.RunImperativeHandleSlots(deferred);
                RunLayoutEffects(deferred, mountDoubleInvoke: entry.IsMount);
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
            Dictionary<ComponentFiber, List<ComponentFiber>> childrenByParent = null;
            var roots = new List<ComponentFiber>();
            for (var i = 0; i < staged.Count; i++)
            {
                var fiber = staged[i];
                var ancestor = fiber.Parent;
                while (ancestor != null && !stagedSet.Contains(ancestor)) ancestor = ancestor.Parent;
                if (ancestor == null)
                {
                    roots.Add(fiber);
                }
                else
                {
                    childrenByParent ??= new Dictionary<ComponentFiber, List<ComponentFiber>>();
                    if (!childrenByParent.TryGetValue(ancestor, out var list))
                    {
                        list = new List<ComponentFiber>();
                        childrenByParent[ancestor] = list;
                    }
                    list.Add(fiber);
                }
            }

            var result = new List<ComponentFiber>(staged.Count);
            for (var i = 0; i < roots.Count; i++) EmitPostOrder(roots[i], childrenByParent, result);
            return result;
        }

        private static void EmitPostOrder(
            ComponentFiber root,
            Dictionary<ComponentFiber, List<ComponentFiber>> childrenByParent,
            List<ComponentFiber> result)
        {
            // Iterative post-order DFS (recursion-free) to avoid .NET stack overflow on deep chains.
            var workStack = new Stack<(int ChildIndex, ComponentFiber Fiber)>();
            workStack.Push((0, root));
            while (workStack.Count > 0)
            {
                var (childIndex, fiber) = workStack.Pop();
                if (childrenByParent != null && childrenByParent.TryGetValue(fiber, out var children) && childIndex < children.Count)
                {
                    workStack.Push((childIndex + 1, fiber));
                    workStack.Push((0, children[childIndex]));
                    continue;
                }
                result.Add(fiber);
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
