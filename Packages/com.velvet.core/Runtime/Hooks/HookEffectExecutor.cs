using System;
using System.Collections.Generic;

namespace Velvet
{
    // Shared 2-pass (cleanup -> factory) execution logic for UseLayoutEffect / UseEffect.
    //
    // An exception thrown by an effect setup or cleanup is routed to the owning fiber's nearest Error
    // Boundary via ComponentBoundarySearch.PropagateException — the same path a render-phase throw takes —
    // so an effect error triggers the boundary fallback. When no boundary exists,
    // PropagateException falls back to Debug.LogException, preserving the prior log-only behavior.
    internal static class HookEffectExecutor
    {
        // Runs every entry in the pending list with a 2-pass strategy.
        // Pass 1: invoke all cleanups. Pass 2: invoke all factories and set the new cleanup.
        // fiber: the owning fiber, used to route an effect throw to its nearest Error Boundary.
        // pending: Effect slots whose factory should run this commit.
        // mountDoubleInvoke:
        // When true and StrictMode is enabled, runs an extra cleanup -> setup cycle after the initial setup.
        // Set only on the mount commit; update commits pass false. The diagnostic
        // doubles effects on mount only, so doubling on update would tear down and re-establish external
        // resources (subscriptions / sockets) of a deps-changed effect mid-frame.
        internal static void RunPendingEffects(ComponentFiber fiber, List<HookEffectSlot> pending, bool mountDoubleInvoke = false)
        {
            if (pending == null || pending.Count == 0) return;

            RunCleanups(fiber, pending);
            RunFactoriesAndClear(fiber, pending, mountDoubleInvoke);
        }

        // Runs the factory (setup) pass for every pending slot, then clears the list. Split out from
        // RunPendingEffects so the passive-effect drain can run a tree-wide cleanup phase
        // across every fiber FIRST, then a tree-wide setup phase — without re-running the cleanups that
        // RunPendingEffects bundles in. Callers that want the single-fiber cleanup→setup
        // pair should call RunPendingEffects instead.
        internal static void RunFactoriesAndClear(ComponentFiber fiber, List<HookEffectSlot> pending, bool mountDoubleInvoke = false)
        {
            if (pending == null || pending.Count == 0) return;

            RunFactories(fiber, pending);

#if UNITY_EDITOR
            // StrictMode diagnostic: on mount, run an extra cleanup -> setup
            // cycle so a mounted effect immediately exercises mount -> unmount(cleanup) -> mount(setup). Surfaces
            // cleanup that is not symmetric with setup (a subscription added in setup but not removed in cleanup,
            // or a cleanup that throws). The factory just ran above, so each slot already holds the live cleanup;
            // invoking it then re-invoking the factory completes the double cycle before the list is cleared.
            if (mountDoubleInvoke && FiberStrictMode.Enabled)
            {
                RunCleanups(fiber, pending);
                RunFactories(fiber, pending);
            }
#endif

            pending.Clear();
        }

        private static void RunFactories(ComponentFiber fiber, List<HookEffectSlot> pending)
        {
            for (var i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                try
                {
                    entry.Cleanup = entry.EffectFactory?.Invoke();
                }
                catch (Exception ex)
                {
                    ComponentBoundarySearch.PropagateException(fiber, ex);
                }
            }
        }

        // Runs only the cleanups for every entry in the pending list. Factories are not invoked.
        internal static void RunCleanups(ComponentFiber fiber, List<HookEffectSlot> entries)
        {
            if (entries == null) return;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                // Detach the cleanup BEFORE invoking it: a cleanup that throws routes through
                // PropagateException, whose boundary fallback can synchronously unmount this fiber and re-enter
                // CleanupAll over the same slots. Nulling first guarantees each cleanup runs at most once even
                // under that re-entrancy (it would otherwise fire a second time from the nested unmount).
                var cleanup = entry.Cleanup;
                entry.Cleanup = null;
                try
                {
                    cleanup?.Invoke();
                }
                catch (Exception ex)
                {
                    ComponentBoundarySearch.PropagateException(fiber, ex);
                }
            }
        }

        // Promotes each slot's staged HookEffectSlot.NextDeps to HookEffectSlot.LastDeps.
        // Called once after the render-phase loop settles so the committed comparison baseline reflects the final
        // (settled) attempt's deps rather than a discarded intermediate attempt.
        internal static void CommitEffectDeps(List<HookEffectSlot> effects)
        {
            if (effects == null) return;

            for (var i = 0; i < effects.Count; i++)
            {
                effects[i].LastDeps = effects[i].NextDeps;
            }
        }

        // Full cleanup: runs the cleanup for every entry in all and clears both lists.
        // Usable for both UseLayoutEffect and UseEffect on Unmount.
        internal static void CleanupAll(ComponentFiber fiber, List<HookEffectSlot> all, List<HookEffectSlot> pending)
        {
            if (all == null) return;

            RunCleanups(fiber, all);
            all.Clear();
            pending?.Clear();
        }
    }
}
