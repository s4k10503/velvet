using System;
using System.Collections.Generic;

namespace Velvet
{
    // The cached snapshot keeps subscription changes out of the in-flight pass. Callbacks receive the
    // live Value so a nested Notify supersedes the outer value when that pass resumes.
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

        public void Notify(T value)
        {
            if (_disposed) return;
            Value = value;
            var snapshot = _snapshot ??= _listeners.ToArray();
            foreach (var listener in snapshot)
            {
                listener(Value);
            }
        }

        public IDisposable Subscribe(Action<T> listener)
        {
            if (_disposed) return NoopDisposable.Instance;
            _listeners.Add(listener);
            _snapshot = null;
            return new Subscription(this, listener);
        }

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
