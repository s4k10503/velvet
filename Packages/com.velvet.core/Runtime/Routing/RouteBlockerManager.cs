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
        /// Blocking does not end the pass. A Blocker is passed over rather than consulted for as long as it
        /// is <see cref="RouteBlockerStatus.Proceeding"/>, whichever navigation reaches here over that span.
        /// </remarks>
        /// <param name="attempt">The navigation attempt being decided.</param>
        /// <param name="resume">Re-issues <paramref name="attempt"/>; invoked by <see cref="RouteBlockerState.Proceed"/>.</param>
        /// <param name="cancellationToken">Token forwarded to an asynchronous Blocker's predicate.</param>
        internal async UniTask<bool> CheckAsync(NavigationAttempt attempt, Action resume,
            CancellationToken cancellationToken = default)
        {
            var anyBlocked = false;
            // ToArray() snapshots the list so a blocker that unregisters during an await does not mutate it.
            foreach (var entry in _blockers.ToArray())
            {
                if (!entry.IsRegistered)
                {
                    continue;
                }

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
                if (blocked && entry.IsRegistered)
                {
                    entry.State.Block(attempt, resume, AbandonAttempt);
                    anyBlocked = true;
                }
            }
            return anyBlocked;
        }

        #endregion

        #region Release

        /// <summary>
        /// Resets every Blocker that is currently blocked, without re-issuing its attempt.
        /// Called from <see cref="Router"/> at the start of a new navigation attempt.
        /// </summary>
        /// <remarks>
        /// A <see cref="RouteBlockerStatus.Proceeding"/> Blocker is left alone: the attempt starting here may
        /// be the one it released, and returning it to Idle would put it back in the way of that attempt.
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
            RemoveSettledRegistrations();
        }

        /// <summary>
        /// Ends the attempt for every Blocker at once. Reached from <see cref="RouteBlockerState.Reset"/>,
        /// which is one Blocker answering for a navigation the router has already turned back: leaving the
        /// others holding it would let a later <c>Proceed</c> send the router at the destination that
        /// answer declined.
        /// </summary>
        internal void AbandonAttempt()
        {
            ResetAllBlocked();
            SettleProceeding();
        }

        /// <summary>
        /// Returns each <see cref="RouteBlockerStatus.Proceeding"/> Blocker to Idle, which is what arms it
        /// for the next navigation. Reached when a navigation commits, when a re-issued one ends without
        /// committing, and when an attempt is abandoned.
        /// </summary>
        /// <remarks>
        /// A registered Blocker still Blocked is a confirm nobody has answered yet, over an attempt that
        /// has therefore not finished. Arming the Blockers that already consented to it would put them back
        /// in the way of what they consented to, and the two would go on releasing each other in turn
        /// without it ever landing.
        /// </remarks>
        internal void SettleProceeding()
        {
            foreach (var entry in _blockers)
            {
                if (entry.IsRegistered && entry.State.Status == RouteBlockerStatus.Blocked)
                {
                    return;
                }
            }

            foreach (var entry in _blockers)
            {
                if (entry.State.Status == RouteBlockerStatus.Proceeding)
                {
                    entry.State.InternalReset();
                }
            }
            RemoveSettledRegistrations();
        }

        #endregion

        #region Internal

        private void Unregister(BlockerEntry entry)
        {
            entry.IsRegistered = false;
            if (entry.State.Status == RouteBlockerStatus.Idle)
            {
                _blockers.Remove(entry);
            }
            else if (entry.State.Status == RouteBlockerStatus.Blocked)
            {
                SettleProceeding();
            }
        }

        private void RemoveSettledRegistrations() =>
            _blockers.RemoveAll(entry => !entry.IsRegistered && entry.State.Status == RouteBlockerStatus.Idle);

        // Private class - mutable public fields are used intentionally.
        // Not referenced externally, so promoting them to properties would just add noise.
        private sealed class BlockerEntry
        {
            public Func<NavigationAttempt, bool>? SyncCheck;
            public Func<NavigationAttempt, CancellationToken, UniTask<bool>>? AsyncCheck;
            public RouteBlockerState State = null!;
            public bool IsRegistered = true;
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
