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
        // mountDoubleInvoke must be false on update commits: doubling there would tear down and re-establish
        // external resources (subscriptions / sockets) of a deps-changed effect mid-frame, not just exercise
        // the mount-only diagnostic.
        internal static void RunPendingEffects(ComponentFiber fiber, List<HookEffectSlot>? pending, bool mountDoubleInvoke = false)
        {
            if (pending == null || pending.Count == 0) return;

            RunCleanups(fiber, pending);
            RunFactoriesAndClear(fiber, pending, mountDoubleInvoke);
        }

        // Split out from RunPendingEffects so the passive-effect drain can run a tree-wide cleanup phase across
        // every fiber FIRST, then a tree-wide setup phase — without re-running the cleanups that
        // RunPendingEffects bundles in. Callers that want the single-fiber cleanup→setup
        // pair should call RunPendingEffects instead.
        internal static void RunFactoriesAndClear(ComponentFiber fiber, List<HookEffectSlot>? pending, bool mountDoubleInvoke = false)
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
                    // An error boundary's fallback can synchronously unmount fiber (the same re-entrancy
                    // RunCleanups guards against below). Stop running the remaining factories on it rather than
                    // standing up new resources whose cleanup pass already ran and will never run again.
                    if (fiber.IsDisposed) return;
                }
            }
        }

        internal static void RunCleanups(ComponentFiber fiber, List<HookEffectSlot>? entries)
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

        // Called once after the render-phase loop settles, so the committed comparison baseline reflects the
        // final (settled) attempt's deps rather than a discarded intermediate attempt.
        internal static void CommitEffectDeps(List<HookEffectSlot>? effects)
        {
            if (effects == null) return;

            for (var i = 0; i < effects.Count; i++)
            {
                effects[i].LastDeps = effects[i].NextDeps;
            }
        }

        // The unmount path for both UseLayoutEffect and UseEffect.
        internal static void CleanupAll(ComponentFiber fiber, List<HookEffectSlot>? all, List<HookEffectSlot>? pending)
        {
            if (all == null) return;

            RunCleanups(fiber, all);
            all.Clear();
            pending?.Clear();
        }
    }
}
