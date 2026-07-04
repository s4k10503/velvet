using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// Registry of navigation blockers for a <see cref="Router"/>. A registered blocker can veto or defer a
    /// navigation attempt (e.g. an unsaved-changes prompt); backs the <c>UseBlocker</c> hook.
    /// </summary>
    public sealed class RouteBlockerManager
    {
        private readonly List<BlockerEntry> _blockers = new();

        #region Register

        /// <summary>
        /// Registers a synchronous Blocker. Disposing the returned <see cref="IDisposable"/> unregisters it.
        /// </summary>
        /// <param name="shouldBlock">Function that receives a navigation attempt and returns true to block.</param>
        /// <param name="state">State object for this blocker. <see cref="RouteBlockerState.Block"/> is invoked when blocking.</param>
        public IDisposable Register(Func<NavigationAttempt, bool> shouldBlock, RouteBlockerState state)
        {
            var entry = new BlockerEntry { SyncCheck = shouldBlock, State = state };
            _blockers.Add(entry);
            return new BlockerRegistration(this, entry);
        }

        /// <summary>
        /// Registers an asynchronous Blocker. Disposing the returned <see cref="IDisposable"/> unregisters it.
        /// </summary>
        /// <param name="shouldBlock">Async function that receives a navigation attempt and returns true to block.</param>
        /// <param name="state">State object for this blocker. <see cref="RouteBlockerState.Block"/> is invoked when blocking.</param>
        public IDisposable Register(Func<NavigationAttempt, CancellationToken, UniTask<bool>> shouldBlock, RouteBlockerState state)
        {
            var entry = new BlockerEntry { AsyncCheck = shouldBlock, State = state };
            _blockers.Add(entry);
            return new BlockerRegistration(this, entry);
        }

        #endregion

        #region Check

        /// <summary>
        /// Evaluates the registered Blockers asynchronously.
        /// </summary>
        /// <remarks>
        /// When multiple Blockers are registered, every one of them is evaluated (no short-circuit).
        /// Every Blocker that blocks transitions its State to Blocked.
        /// </remarks>
        internal async UniTask<bool> CheckAsync(NavigationAttempt attempt, CancellationToken cancellationToken = default)
        {
            var anyBlocked = false;
            // ToArray() snapshots the list so a blocker that unregisters during an await does not mutate it.
            foreach (var entry in _blockers.ToArray())
            {
                // An entry carries exactly one of SyncCheck / AsyncCheck; anything else contributes nothing.
                bool blocked;
                if (entry.SyncCheck != null)
                {
                    blocked = entry.SyncCheck(attempt);
                }
                else if (entry.AsyncCheck != null)
                {
                    blocked = await entry.AsyncCheck(attempt, cancellationToken);
                }
                else
                {
                    continue;
                }

                if (blocked)
                {
                    entry.State.Block(attempt);
                    anyBlocked = true;
                }
            }
            return anyBlocked;
        }

        #endregion

        #region ResetAllBlocked

        /// <summary>
        /// Resets every Blocker that is currently blocked, without invoking callbacks.
        /// Called from <see cref="Router"/> at the start of a new navigation attempt.
        /// </summary>
        public void ResetAllBlocked()
        {
            foreach (var entry in _blockers)
            {
                if (entry.State.Status == RouteBlockerStatus.Blocked)
                {
                    entry.State.InternalReset();
                }
            }
        }

        #endregion

        #region Internal

        private void Unregister(BlockerEntry entry) => _blockers.Remove(entry);

        // Private class - mutable public fields are used intentionally.
        // Not referenced externally, so promoting them to properties would just add noise.
        private sealed class BlockerEntry
        {
            public Func<NavigationAttempt, bool>? SyncCheck;
            public Func<NavigationAttempt, CancellationToken, UniTask<bool>>? AsyncCheck;
            public RouteBlockerState State = null!;
        }

        private sealed class BlockerRegistration : IDisposable
        {
            private readonly RouteBlockerManager _manager;
            private readonly BlockerEntry _entry;
            private bool _disposed;

            public BlockerRegistration(RouteBlockerManager manager, BlockerEntry entry)
            {
                _manager = manager;
                _entry = entry;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _manager.Unregister(_entry);
            }
        }

        #endregion
    }
}
