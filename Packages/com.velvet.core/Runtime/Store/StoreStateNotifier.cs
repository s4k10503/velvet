using System;
using System.Collections.Generic;

namespace Velvet
{
    // Holds the current state value and a hand-rolled set of listeners (not an observable stream). Listeners
    // registered through Subscribe are notified of each value pushed through
    // Notify; the current value is not replayed on subscribe (subsequent-only).
    // Callers that need the current value read Value directly, and callers that need
    // immediate delivery invoke their listener themselves before subscribing.
    // Notification iterates an immutable snapshot of the listener set, rebuilt only when the set
    // changes (copy-on-write), so Notify allocates nothing in the steady state. A
    // listener that subscribes, unsubscribes, or pushes re-entrantly during a notification does not
    // affect the in-flight pass: it observes the snapshot captured when that pass began, and a
    // listener removed mid-pass still receives the value already being delivered.
    // Single-threaded: no internal locking. All calls (Notify / Subscribe /
    // Dispose) must occur on the owning Store<TState>'s thread (the Unity
    // main thread).
    // T: State value type.
    internal sealed class StoreStateNotifier<T> : IDisposable
    {
        private readonly List<Action<T>> _listeners = new();
        private Action<T>[] _snapshot;
        private bool _disposed;

        public StoreStateNotifier(T initial)
        {
            Value = initial;
        }

        public T Value { get; private set; }

        // Updates the current value and notifies every listener. No-op after disposal.
        public void Notify(T value)
        {
            if (_disposed) return;
            Value = value;
            // Copy-on-write: reuse the cached array; it is rebuilt only on Subscribe/unsubscribe.
            var snapshot = _snapshot ??= _listeners.ToArray();
            foreach (var listener in snapshot)
            {
                listener(value);
            }
        }

        // Registers a listener for subsequent values; the current value is not replayed. Returns a
        // disposable that removes the listener.
        public IDisposable Subscribe(Action<T> listener)
        {
            if (_disposed) return NoopDisposable.Instance;
            _listeners.Add(listener);
            _snapshot = null;
            return new Subscription(this, listener);
        }

        // Clears all listeners. Subsequent Notify calls are no-ops.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listeners.Clear();
            _snapshot = null;
        }

        private sealed class Subscription : IDisposable
        {
            private StoreStateNotifier<T> _owner;
            private Action<T> _listener;

            public Subscription(StoreStateNotifier<T> owner, Action<T> listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                if (_owner == null) return;
                _owner._listeners.Remove(_listener);
                _owner._snapshot = null;
                _owner = null;
                _listener = null;
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
