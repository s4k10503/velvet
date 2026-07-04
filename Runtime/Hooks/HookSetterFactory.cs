using System;

namespace Velvet
{
    // Helper that builds setter / dispatcher closures for UseState / UseReducer.
    internal static class HookSetterFactory
    {
        // Builds a setter closure for UseState.
        // Equal-value updates do not request a re-render (identity-based bailout).
        // The caller must invoke this exactly once when the slot is created and cache the resulting
        // delegate. Calling it on every render would keep producing unnecessary closures.
        internal static Action<T> CreateStateSetter<T>(
            HookStateSlot<T> slot,
            Func<bool> isDisposed,
            Action requestRender)
        {
            return newValue =>
            {
                if (isDisposed()) return;
                if (ObjectIs.AreEqual(slot.Value, newValue)) return;
                slot.Value = newValue;
                requestRender();
            };
        }

        // Builds a dispatch closure for UseReducer.
        // The reducer is updated via the slot on every render, so the latest function is always used.
        // Bailout uses identity semantics (NaN equals itself, ±0 distinguished, reference equality
        // for objects) so a record/struct reducer that returns a new instance with identical content still propagates.
        // Deliberate design choice here: dispatch applies the reducer EAGERLY against the latest
        // committed reducer/state, rather than queuing actions and replaying them through the reducer captured
        // at the next render. For a pure reducer the two are observationally identical —
        // multiple dispatches in one tick chain through slot.Value in order, giving the same fold. They
        // diverge only when the reducer FUNCTION is swapped on every render (a discouraged anti-pattern):
        // a queue-and-replay model would re-run against the render-time reducer, whereas this uses the latest. A
        // queue-and-replay path would add ordering/batching complexity (and regression surface) to benefit only
        // that anti-pattern, so eager application is kept on purpose.
        // The caller must invoke this exactly once when the slot is created and cache the resulting
        // delegate. Calling it on every render would keep producing unnecessary closures.
        internal static Action<TAction> CreateReducerDispatch<TState, TAction>(
            ReducerSlot<TState, TAction> slot,
            Func<bool> isDisposed,
            Action requestRender)
        {
            return action =>
            {
                if (isDisposed()) return;
                var newState = slot.Reducer(slot.Value, action);
                if (ObjectIs.AreEqual(slot.Value, newState)) return;
                slot.Value = newState;
                requestRender();
            };
        }
    }
}
