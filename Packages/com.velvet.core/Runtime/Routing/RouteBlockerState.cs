using System;

namespace Velvet
{
    public enum RouteBlockerStatus
    {
        Idle,
        Blocked,
        /// <summary>
        /// <see cref="RouteBlockerState.Proceed"/> has released the block and the navigation it held is on
        /// its way. <see cref="RouteBlockerState.Attempt"/> still reports that navigation, and this Blocker
        /// is consulted about no navigation at all until that one settles.
        /// </summary>
        Proceeding,
    }

    /// <summary>Observable state for an individual navigation Blocker.</summary>
    public sealed class RouteBlockerState
    {
        public RouteBlockerStatus Status { get; internal set; } = RouteBlockerStatus.Idle;
        /// <summary>
        /// The transition presented to this Blocker's predicate. <see cref="Proceed"/> re-issues the caller's
        /// request instead, which can differ after a Guard redirect. null when
        /// <see cref="RouteBlockerStatus.Idle"/>.
        /// </summary>
        public NavigationAttempt? Attempt { get; internal set; }

        private Action? _resume;
        private Action? _abandon;

        /// <summary>
        /// Releases the block and re-issues the request the caller made — the whole navigation runs again,
        /// without consulting this Blocker — so a Guard that rewrote the destination rewrites it again, and
        /// a blocked Back or Forward lands on the slot that step resolves. Does nothing unless
        /// <see cref="Status"/> is <see cref="RouteBlockerStatus.Blocked"/>.
        /// </summary>
        public void Proceed()
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            // Set before the re-issue rather than after: the navigation it starts consults the Blockers, and
            // this status is what tells that pass to leave this one alone.
            Status = RouteBlockerStatus.Proceeding;
            var resume = _resume;
            _resume = null;
            resume?.Invoke();
        }

        /// <summary>
        /// Releases the block and abandons the navigation this Blocker held, leaving the router where it is.
        /// Every Blocker is released with it — this one, the others still blocking, and the ones that had
        /// already proceeded alike. Does nothing unless <see cref="Status"/> is
        /// <see cref="RouteBlockerStatus.Blocked"/>.
        /// </summary>
        public void Reset()
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            _abandon?.Invoke();
        }

        internal void InternalReset()
        {
            Status = RouteBlockerStatus.Idle;
            Attempt = null;
            _resume = null;
            _abandon = null;
        }

        internal void Block(NavigationAttempt attempt, Action resume, Action abandon)
        {
            Status = RouteBlockerStatus.Blocked;
            Attempt = attempt;
            _resume = resume;
            _abandon = abandon;
        }
    }
}
