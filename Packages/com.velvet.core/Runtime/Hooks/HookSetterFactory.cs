using System;

namespace Velvet
{
    internal static class HookSetterFactory
    {
        // The caller must invoke each Create* exactly once when the slot is created and cache the resulting
        // delegate. Calling it on every render would keep producing unnecessary closures.
        internal static Action<T> CreateStateSetter<T>(HookStateSlot<T> slot, ComponentFiber fiber)
            => newValue => Dispatch(slot, fiber, new StateAction<T>(newValue, null));

        internal static Action<Func<T, T>> CreateStateUpdater<T>(HookStateSlot<T> slot, ComponentFiber fiber)
        {
            return updater =>
            {
                if (updater == null) throw new ArgumentNullException(nameof(updater));
                Dispatch(slot, fiber, new StateAction<T>(default!, updater));
            };
        }

        internal static Action<TAction> CreateReducerDispatch<TState, TAction>(
            ReducerSlot<TState, TAction> slot, ComponentFiber fiber)
            => action => Dispatch(slot, fiber, action);

        private static void Dispatch<TState, TAction>(
            HookUpdateQueueSlot<TState, TAction> slot, ComponentFiber fiber, TAction action)
        {
            if (fiber.IsDisposed) return;
            if (slot.Enqueue(action, FiberWorkLoop.HookUpdateLane(fiber)))
            {
                FiberWorkLoop.RequestRenderFromHook(fiber);
            }
        }
    }
}
