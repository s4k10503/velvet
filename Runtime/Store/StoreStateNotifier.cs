using System;
using System.Collections.Generic;

namespace Velvet
{
    // Hand-rolled listener set, not an observable stream (no operators/buffering). Subscribe only
    // receives values pushed by later Notify calls; the current value is not replayed at subscribe
    // time — callers that need it read Value directly, and callers that need immediate delivery
    // invoke their listener themselves before subscribing.
    // Notification iterates an immutable snapshot of the listener set, rebuilt only when the set
    // changes (copy-on-write), so Notify allocates nothing in the steady state. A
    // listener that subscribes or unsubscribes re-entrantly during a notification does not
    // affect the in-flight pass: it observes the listener set captured when that pass began, and a
    // listener removed mid-pass still receives the delivery already in flight. Each delivery reads
    // the value that is current at call time, so a listener that re-entrantly pushes a newer value
    // supersedes the in-flight value for the listeners that follow it.
    // Single-threaded: no internal locking. All calls (Notify / Subscribe /
    // Dispose) must occur on the owning Store<TState>'s thread (the Unity
    // main thread).
    internal sealed class StoreStateNotifier<T> : IDisposable
    {
        private readonly List<Action<T>> _listeners = new();
        private Action<T>[]? _snapshot;
        private bool _disposed;

        public StoreStateNotifier(T initial)
        {
            Value = initial;
        }

        public T Value { get; private set; }

        // No-op after disposal.
        public void Notify(T value)
        {
            if (_disposed) return;
            Value = value;
            // Copy-on-write: reuse the cached array; it is rebuilt only on Subscribe/unsubscribe.
            var snapshot = _snapshot ??= _listeners.ToArray();
            foreach (var listener in snapshot)
            {
                // Deliver the live field, not the captured parameter: a listener that re-entrantly
                // pushes a newer value must not leave the listeners after it holding the superseded
                // one as their final observation.
                listener(Value);
            }
        }

        // Returns a no-op disposable after disposal instead of throwing.
        public IDisposable Subscribe(Action<T> listener)
        {
            if (_disposed) return NoopDisposable.Instance;
            _listeners.Add(listener);
            _snapshot = null;
            return new Subscription(this, listener);
        }

        // Subsequent Notify calls are no-ops.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listeners.Clear();
            _snapshot = null;
        }

        private sealed class Subscription : IDisposable
        {
            private StoreStateNotifier<T>? _owner;
            private Action<T>? _listener;

            public Subscription(StoreStateNotifier<T> owner, Action<T> listener)
            {
                _owner = owner;
                _listener = listener;
            }

            public void Dispose()
            {
                if (_owner == null) return;
                _owner._listeners.Remove(_listener!);
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
