using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>Coordinates the navigation Blockers registered with a <see cref="Router"/>.</summary>
    public sealed class RouteBlockerManager
    {
        private readonly List<BlockerEntry> _blockers = new();

        // The pass below walks a snapshot, and one list serves every pass rather than one array each.
        // A pass runs caller code, so a second pass could start while this holds the first's snapshot:
        // `CheckAsync` takes its own copy when this one is already in use.
        private readonly List<BlockerEntry> _snapshot = new();
        private bool _snapshotInUse;

        #region Register

        /// <summary>
        /// Disposing stops future predicate checks. An already Blocked state remains answerable until its
        /// attempt settles.
        /// </summary>
        public IDisposable Register(Func<NavigationAttempt, bool> shouldBlock, RouteBlockerState state)
        {
            var entry = new BlockerEntry { SyncCheck = shouldBlock, State = state };
            _blockers.Add(entry);
            return new BlockerRegistration(this, entry);
        }

        /// <summary>
        /// Disposing stops future predicate checks. An already Blocked state remains answerable until its
        /// attempt settles.
        /// </summary>
        public IDisposable Register(Func<NavigationAttempt, CancellationToken, UniTask<bool>> shouldBlock, RouteBlockerState state)
        {
            var entry = new BlockerEntry { AsyncCheck = shouldBlock, State = state };
            _blockers.Add(entry);
            return new BlockerRegistration(this, entry);
        }

        #endregion

        #region Check

        internal async UniTask<bool> CheckAsync(NavigationAttempt attempt, Action resume,
            CancellationToken cancellationToken = default)
        {
            var anyBlocked = false;
            // Snapshotted rather than iterated live: a decision taken in this loop reaches Unregister or
            // RemoveSettledRegistrations, both of which remove from _blockers. Over the live list the entry
            // after a removed one is skipped and never consulted; over the snapshot it is visited, and the
            // IsRegistered guards are what decide whether it may still act.
            List<BlockerEntry> walking;
            var borrowed = !_snapshotInUse;
            if (borrowed)
            {
                _snapshotInUse = true;
                walking = _snapshot;
                walking.Clear();
                walking.AddRange(_blockers);
            }
            else
            {
                walking = new List<BlockerEntry>(_blockers);
            }

            try
            {
                foreach (var entry in walking)
                {
                    if (!entry.IsRegistered)
                    {
                        continue;
                    }

                    if (entry.State.Status == RouteBlockerStatus.Proceeding)
                    {
                        continue;
                    }

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

                    // After the await and before Block: the token is this navigation's own, so a newer
                    // navigation taking over mid-await means the caller discards this result as Cancelled,
                    // and a Blocked written here would strand a confirm UI until some unrelated later
                    // navigation resets it.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return anyBlocked;
                }

                if (blocked && entry.IsRegistered)
                {
                    entry.State.Block(attempt, resume, AbandonAttempt);
                    anyBlocked = true;
                }
            }
            return anyBlocked;
            }
            finally
            {
                if (borrowed)
                {
                    _snapshot.Clear();
                    _snapshotInUse = false;
                }
            }
        }

        #endregion

        #region Release

        /// <summary>Clears Blocked entries without rearming entries that already released the attempt.</summary>
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
        /// A reset abandons the shared attempt, preventing another Blocker from resuming an attempt that was
        /// already declined.
        /// </summary>
        internal void AbandonAttempt()
        {
            ResetAllBlocked();
            SettleProceeding();
        }

        /// <remarks>
        /// A registered Blocker still Blocked has not released or abandoned the attempt. Rearming the
        /// Blockers that already released it would put them back in its way before it can settle.
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
