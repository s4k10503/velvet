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
        /// Every Blocker that blocks transitions its State to Blocked and takes <paramref name="resume"/> as
        /// what its <see cref="RouteBlockerState.Proceed"/> re-issues.
        /// </remarks>
        /// <param name="attempt">The navigation attempt each Blocker decides on.</param>
        /// <param name="resume">Re-issues <paramref name="attempt"/>; invoked by <see cref="RouteBlockerState.Proceed"/>.</param>
        /// <param name="cancellationToken">Token forwarded to each asynchronous Blocker.</param>
        internal async UniTask<bool> CheckAsync(NavigationAttempt attempt, Action resume,
            CancellationToken cancellationToken = default)
        {
            var anyBlocked = false;
            // ToArray() snapshots the list so a blocker that unregisters during an await does not mutate it.
            foreach (var entry in _blockers.ToArray())
            {
                // A Blocker whose Proceed() released this navigation is not asked about it again — the user
                // has already answered. It returns to Idle when the navigation commits, so the window this
                // skip covers is the resumed navigation and anything that supersedes it.
                if (entry.State.Status == RouteBlockerStatus.Proceeding)
                {
                    continue;
                }

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

                // A superseded attempt must not flip any state: the token is the navigation's own,
                // cancelled when a newer navigation takes over, and the caller is about to discard
                // this result as Cancelled — a Blocked state would strand a confirm-UI until some
                // unrelated future navigation resets it.
                if (cancellationToken.IsCancellationRequested)
                {
                    return anyBlocked;
                }

                // Skip entries whose registration died during this pass (the snapshot keeps them
                // iterable, but their owner unmounted or an earlier blocker's decision tore them
                // down): nothing live is waiting on their state.
                if (blocked && _blockers.Contains(entry))
                {
                    entry.State.Block(attempt, resume);
                    anyBlocked = true;
                }
            }
            return anyBlocked;
        }

        #endregion

        #region ResetAllBlocked

        /// <summary>
        /// Resets every Blocker that is currently blocked, without re-issuing its attempt.
        /// Called from <see cref="Router"/> at the start of a new navigation attempt.
        /// </summary>
        /// <remarks>
        /// A <see cref="RouteBlockerStatus.Proceeding"/> Blocker is left alone: the attempt starting here may
        /// be the one it released, and returning it to Idle would let <see cref="CheckAsync"/> block that
        /// attempt a second time. <see cref="ClearProceeding"/> is what ends that state.
        /// </remarks>
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

        /// <summary>
        /// Returns every <see cref="RouteBlockerStatus.Proceeding"/> Blocker to Idle. Called from
        /// <see cref="Router"/> once a navigation has committed, which is what re-arms a Blocker that
        /// released one.
        /// </summary>
        internal void ClearProceeding()
        {
            foreach (var entry in _blockers)
            {
                if (entry.State.Status == RouteBlockerStatus.Proceeding)
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
