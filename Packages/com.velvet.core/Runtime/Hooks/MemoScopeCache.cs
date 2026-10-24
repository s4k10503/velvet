using System;
using System.Collections.Generic;

namespace Velvet
{
    // The per-fiber cache the woven memoization gates read and write: a run of HookMemoSlot entries in
    // ComponentFiber.MemoSlots, one per scope the weaver cut a component body into. Hooks.TryGetMemoizedVNode and
    // Hooks.StoreMemoizedVNode are the one-scope form of the same three operations.
    internal static class MemoScopeCache
    {
        // Grants the rendering fiber `slotCount` slots to the first call its own [Component] method makes during
        // a render, and returns the index of the first. Any other call is granted none and gets -1, so the body
        // making it runs uncached: a woven component called as a plain method from another component's render or
        // from a V.Memoized factory, or outside any render.
        internal static int Reserve(RuntimeMethodHandle component, int slotCount)
        {
            if (slotCount <= 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            var fiber = FiberAmbientStack.Current;
            if (fiber == null || fiber.Indices.MemoHookIndex != 0 || !fiber.RendersComponent(component))
            {
                return -1;
            }
            fiber.Indices.MemoHookIndex = slotCount;
            var slots = fiber.MemoSlots ??= new List<HookMemoSlot>();
            while (slots.Count < slotCount)
            {
                slots.Add(new HookMemoSlot());
            }
            return 0;
        }

        // `scope` counts from `firstSlot`; a firstSlot of -1, which Reserve returns for a call granted no slot,
        // is a miss that stages nothing.
        internal static bool TryGet(int firstSlot, int scope, object?[]? deps, out VNode? cached)
        {
            if (deps == null) throw new ArgumentNullException(nameof(deps));
            cached = null;
            if (firstSlot < 0) return false;
            var fiber = FiberAmbientStack.Current;
            if (fiber == null) return false;
            var slot = fiber.MemoSlots![firstSlot + scope];
            slot.HookCallCountAtGate = fiber.HookCallCount;
            // Structural value equality would call a fresh-but-content-equal record class prop or context
            // value unchanged and hand back the cached VNode, so the render the reconciler had already
            // decided to make would produce the committed tree instead of the new one.
            // MUTANT_SURVIVES(equivalent, clause removed): AreEqualDeps reads a null committed list as unequal to the non-null deps checked above.
            if (!slot.RunsHooksPastGate && slot.LastDeps != null && ObjectIs.AreEqualDeps(slot.LastDeps, deps))
            {
                // Hit against the committed deps: reuse the committed VNode and stage the committed values so the
                // render-phase settle keeps them. (On a miss, Store stages the rebuilt result; without
                // this hit-side staging a discarded attempt's miss would otherwise be the last thing staged.)
                slot.NextDeps = slot.LastDeps;
                slot.NextCachedResult = slot.CachedResult;
                // A hit drops what the body built ahead of its gate, which the cached tree's retirement never reaches.
                VNodePool.DisownJournaledRentals();
                cached = slot.CachedResult;
                return true;
            }
            return false;
        }

        internal static void Store(int firstSlot, int scope, object?[]? deps, VNode result, string memberName)
        {
            if (firstSlot < 0) return;
            if (deps == null) throw new ArgumentNullException(nameof(deps));
            var fiber = Hooks.ResolveUncounted(memberName);
#if UNITY_EDITOR
            // The StrictMode diagnostic render is isolated from the commit: staging its throwaway
            // output here would first spare that tree from the diagnostic's own retirement sweep
            // (the mark reads NextCachedResult) and then strand it when the next real render
            // restages the slot — one leaked subtree per double render. Mirrors the other
            // externally-visible hook writes the diagnostic pass no-ops.
            if (fiber.IsStrictDiagnosticPass) return;
#endif
            var index = firstSlot + scope;
            if (fiber.MemoSlots == null || scope < 0 || index >= fiber.MemoSlots.Count)
            {
                throw new InvalidOperationException(
                    $"{Hooks.ComponentName(fiber)}: {memberName} slot index ({index}) is invalid."
                    + " Did you obtain the slot from the gate call immediately beforehand?");
            }
            var slot = fiber.MemoSlots[index];
            // A hook the build-time scan could not see — reached through an interface or virtual call, say — ran
            // between the gate and here. A hit skips that stretch, so it would skip the hook too; this body is
            // rebuilt on every render from now on, as an unwoven one is.
            if (fiber.HookCallCount != slot.HookCallCountAtGate)
            {
                slot.RunsHooksPastGate = true;
            }
            // Stage the freshly computed memo. The committed LastDeps / CachedResult are promoted when the
            // render-phase loop settles, so a discarded intermediate attempt cannot poison the committed baseline.
            slot.NextDeps = deps;
            slot.NextCachedResult = result;
        }
    }
}
