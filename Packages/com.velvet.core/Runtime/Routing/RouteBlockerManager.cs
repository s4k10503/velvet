using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>Coordinates the navigation Blockers registered with a <see cref="Router"/>.</summary>
    public sealed class RouteBlockerManager
    {
        // In registration order: the last one is the Blocker a navigation consults.
        private readonly List<BlockerEntry> _blockers = new();

        // MUTANT_SURVIVES(equivalent, boundary): with it always true, Check answers false over an empty list.
        // What this spares a router with no Blocker is the argument Router.Consult builds, not a decision.
        internal bool HasBlockers => _blockers.Count > 0;

        #region Register

        /// <summary>
        /// Registers a Blocker. A router consults only the one registered last, as React Router's does.
        /// Disposing the registration removes the Blocker and returns <paramref name="state"/> to
        /// <see cref="RouteBlockerStatus.Idle"/>, whatever it was holding.
        /// </summary>
        public IDisposable Register(Func<BlockerFunctionArgs, bool> shouldBlock, RouteBlockerState state)
        {
            if (shouldBlock == null) throw new ArgumentNullException(nameof(shouldBlock));
            if (state == null) throw new ArgumentNullException(nameof(state));
            var entry = new BlockerEntry(this, shouldBlock, state);
            _blockers.Add(entry);
            return entry;
        }

        // Swaps the predicate a registration answers with and keeps its place in the order, as React Router
        // keeps a blocker's key when its function changes.
        internal static void UpdatePredicate(IDisposable registration, Func<BlockerFunctionArgs, bool> shouldBlock)
            => ((BlockerEntry)registration).ShouldBlock = shouldBlock;

        #endregion

        #region Check

        // True when the Blocker consulted blocks the navigation, which it then holds.
        internal bool Check(BlockerFunctionArgs args, Action resume)
        {
            if (_blockers.Count == 0)
            {
                return false;
            }

            if (_blockers.Count > 1)
            {
                FiberLogger.LogWarning(nameof(Router), "A router only supports one blocker at a time");
            }

            var entry = _blockers[_blockers.Count - 1];
            if (entry.State.Status == RouteBlockerStatus.Proceeding)
            {
                return false;
            }

            // A predicate that disposes its own registration has withdrawn the Blocker it would answer for,
            // so its answer blocks nothing.
            if (!entry.ShouldBlock(args) || !entry.IsRegistered)
            {
                return false;
            }

            entry.State.Block(args.NextLocation, resume);
            return true;
        }

        #endregion

        #region Release

        // A committed navigation got past the Blockers, so every one of them starts over.
        internal void ResetAll() => ReturnToIdle(proceedingOnly: false);

        // A released navigation that ended without committing leaves nothing for a Blocker to be proceeding
        // with.
        internal void SettleProceeding() => ReturnToIdle(proceedingOnly: true);

        // Indexed from the end rather than enumerated, so a registration disposed from inside a state
        // change's announcement does not break the walk.
        private void ReturnToIdle(bool proceedingOnly)
        {
            // MUTANT_SURVIVES(equivalent, arithmetic): a walk started past the end meets the bound check below.
            // It skips the indices past the end and reaches the same entries.
            for (var i = _blockers.Count - 1; i >= 0; i--)
            {
                if (i >= _blockers.Count)
                {
                    continue;
                }

                var status = _blockers[i].State.Status;
                if (proceedingOnly ? status == RouteBlockerStatus.Proceeding : status != RouteBlockerStatus.Idle)
                {
                    _blockers[i].State.ToIdle();
                }
            }
        }

        #endregion

        #region Internal

        private sealed class BlockerEntry : IDisposable
        {
            private readonly RouteBlockerManager _manager;

            internal BlockerEntry(RouteBlockerManager manager, Func<BlockerFunctionArgs, bool> shouldBlock,
                RouteBlockerState state)
            {
                _manager = manager;
                ShouldBlock = shouldBlock;
                State = state;
            }

            internal Func<BlockerFunctionArgs, bool> ShouldBlock { get; set; }
            internal RouteBlockerState State { get; }
            internal bool IsRegistered { get; private set; } = true;

            public void Dispose()
            {
                if (!IsRegistered)
                {
                    return;
                }

                IsRegistered = false;
                _manager._blockers.Remove(this);
                if (State.Status != RouteBlockerStatus.Idle)
                {
                    State.ToIdle();
                }
            }
        }

        #endregion
    }
}
