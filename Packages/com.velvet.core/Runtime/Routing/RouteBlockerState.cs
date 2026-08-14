using System;

namespace Velvet
{
    /// <summary>Current state of a Blocker.</summary>
    public enum RouteBlockerStatus
    {
        /// <summary>Idle, not currently blocking.</summary>
        Idle,
        /// <summary>Currently blocking a navigation. <see cref="RouteBlockerState.Attempt"/> holds the attempt details.</summary>
        Blocked,
        /// <summary>
        /// <see cref="RouteBlockerState.Proceed"/> has released the block and the navigation it held is on
        /// its way. <see cref="RouteBlockerState.Attempt"/> still reports that navigation, and this Blocker
        /// is consulted about no navigation at all until that one settles.
        /// </summary>
        Proceeding,
    }

    /// <summary>
    /// State object held by an individual Blocker.
    /// UI components observe this object to drive the display of a block dialog.
    /// </summary>
    public sealed class RouteBlockerState
    {
        /// <summary>Current block state.</summary>
        public RouteBlockerStatus Status { get; internal set; } = RouteBlockerStatus.Idle;
        /// <summary>
        /// Where the navigation this Blocker holds was heading when the predicate saw it, which is what a
        /// dialog names the destination from. Not what <see cref="Proceed"/> re-issues — that is the request
        /// the caller made, and a Guard redirect makes the two differ. null when
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
        /// Every other Blocker is released with it — the ones still blocking and the ones that had already
        /// proceeded alike. Does nothing unless <see cref="Status"/> is
        /// <see cref="RouteBlockerStatus.Blocked"/>.
        /// </summary>
        public void Reset()
        {
            if (Status != RouteBlockerStatus.Blocked)
            {
                return;
            }

            var abandon = _abandon;
            InternalReset();
            abandon?.Invoke();
        }

        /// <summary>
        /// Resets the state without re-issuing or abandoning anything (internal use before a navigation
        /// starts, and once the navigation this Blocker released has settled).
        /// </summary>
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
