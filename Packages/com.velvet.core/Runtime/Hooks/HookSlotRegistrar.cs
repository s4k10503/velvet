// annotations only: incremental nullable hygiene. See the comment at the top of Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Generic helper for the "render-guard -> null-coalesce -> bump index -> new-or-existing branch" pattern.
    // Centralizes the logic shared by position-based slot registrations such as UseLayoutEffect / UseEffect.
    internal static class HookSlotRegistrar
    {
        // Registers an effect slot with deps comparison.
        // The current render's deps are staged on HookEffectSlot.NextDeps and compared against the
        // committed HookEffectSlot.LastDeps; HookEffectSlot.LastDeps is promoted only
        // once the render-phase loop settles (FiberRenderer calls HookEffectExecutor.CommitEffectDeps). A
        // render-phase state-update re-run discards intermediate attempts, so leaving the baseline at the committed
        // render is what lets the settled attempt's deps be compared against the committed render rather than a
        // discarded attempt.
        // When deduplicatePending is true, skips adding to pendingEffects
        // if the slot is already present. Both UseEffect and UseLayoutEffect require this: the pending
        // list persists across a render-phase state-update re-run (it is cleared once per RenderAndReconcile,
        // not per re-run), so a slot whose deps change between attempts could otherwise be added twice.
        internal static void RegisterEffect(
            ref List<HookEffectSlot> effects,
            ref List<HookEffectSlot> pendingEffects,
            ref int hookIndex,
            Func<Action> factory,
            object?[] deps,
            bool deduplicatePending = false,
            bool diagnosticPass = false)
        {
            effects ??= new List<HookEffectSlot>();
            pendingEffects ??= new List<HookEffectSlot>();

            // The hook cursor must still advance so the StrictMode purity check observes a consistent hook
            // count and later slots resolve to the right index.
            var index = hookIndex++;

            // The StrictMode diagnostic render re-invokes the effect hooks only to advance the cursor for the
            // purity check. It must not overwrite the committed effect factory with the diagnostic closure, nor
            // re-queue the effect: the committed render already registered the correct factory and pending work.
            if (diagnosticPass)
            {
                return;
            }

            if (index >= effects.Count)
            {
                // New (mounting) slot: LastDeps stays null until the render settles, so a discarded render-phase
                // attempt that appended this slot is rolled back by truncating the lists to the committed length,
                // and the settled attempt re-adds it as a fresh mount.
                var entry = new HookEffectSlot { EffectFactory = factory, NextDeps = deps };
                effects.Add(entry);
                pendingEffects.Add(entry);
                return;
            }

            var existing = effects[index];
            existing.EffectFactory = factory;
            existing.NextDeps = deps;

            if (deps == null || !ObjectIs.AreEqualDeps(existing.LastDeps, deps))
            {
                if (!deduplicatePending || !pendingEffects.Contains(existing))
                    pendingEffects.Add(existing);
            }
        }

        internal static void RegisterLayoutEffect(
            ref List<HookEffectSlot> effects,
            ref List<HookEffectSlot> pendingEffects,
            ref int hookIndex,
            Func<Action> factory,
            object?[] deps,
            bool diagnosticPass = false)
            => RegisterEffect(ref effects, ref pendingEffects, ref hookIndex, factory, deps,
                deduplicatePending: true, diagnosticPass: diagnosticPass);
    }
}
