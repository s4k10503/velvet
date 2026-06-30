using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// An <see cref="IDisposable"/> that wraps an <see cref="Action"/> and invokes it on the first
    /// <see cref="Dispose"/>. Subsequent calls are no-ops, so Dispose is idempotent. Handy for tests
    /// that return a disposable cleanup (e.g. from an effect) and want to observe it firing exactly once.
    /// </summary>
    public sealed class ActionDisposable : IDisposable
    {
        private Action _onDispose;

        public ActionDisposable(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            var onDispose = _onDispose;
            _onDispose = null;
            onDispose?.Invoke();
        }
    }
}
