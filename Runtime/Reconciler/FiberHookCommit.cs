using System.Collections.Generic;

namespace Velvet
{
    // Commit-of-hooks helpers: promote a settled render attempt's staged hook state to the
    // committed baseline, and run the layout-phase imperative-handle commit. Extracted from the
    // render core (FiberRenderer.RenderAndReconcile calls the four Commit* helpers + the
    // render-phase rollback; FiberEffects / NotifyAsyncResourceCompleted run the imperative
    // handles after subtree commit).
    internal static class FiberHookCommit
    {
        // Rolls a discarded render-phase attempt's effect registrations back to the committed footprint.
        // Effect slots only grow during a render (RegisterEffect appends; LastDeps is never mutated), so
        // truncating the slot and pending lists to their committed lengths is sufficient to undo the attempt;
        // the settled attempt then re-registers from the committed baseline.
        internal static void DiscardRenderPhaseAttemptEffects(
            ComponentFiber fiber,
            int committedLayoutEffectCount,
            int committedInsertionEffectCount,
            int committedEffectCount,
            int committedPendingLayoutCount,
            int committedPendingInsertionCount,
            int committedPendingEffectCount)
        {
            TruncateTo(fiber.LayoutEffects, committedLayoutEffectCount);
            TruncateTo(fiber.InsertionEffects, committedInsertionEffectCount);
            TruncateTo(fiber.Effects, committedEffectCount);
            TruncateTo(fiber.PendingLayoutEffects, committedPendingLayoutCount);
            TruncateTo(fiber.PendingInsertionEffects, committedPendingInsertionCount);
            TruncateTo(fiber.PendingEffects, committedPendingEffectCount);
        }

        internal static void TruncateTo(List<HookEffectSlot>? list, int count)
        {
            if (list != null && list.Count > count)
            {
                list.RemoveRange(count, list.Count - count);
            }
        }

        internal static void CommitCallbackSlots(List<HookCallbackSlot>? slots)
        {
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                slots[i].Callback = slots[i].NextCallback;
                slots[i].LastDeps = slots[i].NextDeps;
            }
        }

        internal static void CommitMemoSlots(List<HookMemoSlot>? slots)
        {
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                slots[i].LastDeps = slots[i].NextDeps;
                slots[i].CachedResult = slots[i].NextCachedResult;
            }
        }

        internal static void CommitMemoValueSlots(List<HookMemoValueSlot>? slots)
        {
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                slots[i].Commit();
            }
        }

        // Layout-phase commit for UseImperativeHandle slots: adopts a swapped HandleRef, invokes the
        // staged factory (when deps changed), writes the resulting handle into the parent ref, and
        // advances LastDeps. All slot mutation lives here (not split across render-phase) so a
        // time-sliced or async-resumed render that arrives before this layout phase fires does not
        // observe stale-but-promoted LastDeps and silently skip its factory. Runs after subtree commit
        // so the factory observes the subtree's already-attached element / handle refs: imperative
        // handles run in the layout phase, after the DOM mutations + ref attach are committed for
        // the entire subtree.
        internal static void RunImperativeHandleSlots(ComponentFiber fiber)
        {
            var slots = fiber.ImperativeHandleSlots;
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.NextFactory == null) continue;
                // Adopt a swapped ref atomically with the write below: detach the previous parent ref,
                // point at the new one, then write the (recomputed or committed) handle in the same step.
                if (!ReferenceEquals(slot.HandleRef, slot.NextHandleRef))
                {
                    slot.HandleRef?.Set(null);
                    slot.HandleRef = slot.NextHandleRef;
                }
                if (slot.NextNeedsRecompute)
                {
                    slot.Handle = slot.NextFactory();
                }
                slot.HandleRef?.Set(slot.Handle);
                slot.LastDeps = slot.NextDeps;
                slot.NextFactory = null;
            }
        }

        internal static void CommitBlockerSlots(List<HookBlockerSlot>? slots)
        {
            if (slots == null) return;
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot.NextNeedsReregister)
                {
                    slot.Registration?.Dispose();
                    slot.Registration = slot.NextRegister?.Invoke();
                }
                slot.LastDeps = slot.NextDeps;
                slot.NextRegister = null;
            }
        }
    }
}
