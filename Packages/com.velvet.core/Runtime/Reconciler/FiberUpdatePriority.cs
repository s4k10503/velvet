namespace Velvet
{
    /// <summary>
    /// Lane priority for a scheduled re-render. The lane is chosen from the scheduling context, not passed
    /// as an argument. A <c>UseDeferredValue</c> derivation asks for the Transition lane directly; a
    /// hook-driven state update is classified from its context, tested in this order: the Transition lane
    /// while a <c>StartTransition</c> callback is running and has not yet suspended; failing that, the
    /// Urgent lane while a discrete event handler runs; failing that, the Normal lane. A starved Transition
    /// lane is promoted to Normal. Lower numeric values indicate higher priority; a fiber's lane queue
    /// drains lowest-value-first.
    /// </summary>
    public enum FiberUpdatePriority
    {
        /// <summary>
        /// Highest priority. Drains ahead of every other lane within a fiber's lane queue. Belongs to
        /// <c>FiberBatchScheduler</c>'s Immediate tier (drained by <c>DrainImmediate</c> / registered via
        /// <c>ScheduleImmediate</c>), alongside <see cref="Normal"/>.
        /// </summary>
        Urgent = 0,
        /// <summary>
        /// Default priority for state updates that are not part of a transition. A starved Transition lane
        /// is promoted to it, coalescing with the very traffic that kept preempting it, so the starved
        /// work commits with the next Normal drain instead of waiting behind it. Belongs to
        /// <c>FiberBatchScheduler</c>'s Immediate tier (drained by <c>DrainImmediate</c> / registered via
        /// <c>ScheduleImmediate</c>).
        /// </summary>
        Normal = 1,
        /// <summary>
        /// Low priority. Its flush is delayed by a fixed delay. Belongs to <c>FiberBatchScheduler</c>'s
        /// Delayed tier (drained by <c>DrainDelayed</c> / registered via <c>ScheduleDelayed</c>), alongside
        /// <see cref="Transition"/>.
        /// </summary>
        Deferred = 2,
        /// <summary>
        /// Lowest priority. Taken by the updates a <c>StartTransition</c> callback schedules before it first
        /// suspends, and by <c>UseDeferredValue</c> derivations.
        /// Its flush is delayed so higher-priority lanes commit first, and once it has been starved for a
        /// fixed number of flushes it is promoted to Normal — draining in that same flush, or right after
        /// any co-pending Urgent drains; <c>isPending</c> survives until the promoted work commits. Belongs
        /// to <c>FiberBatchScheduler</c>'s Delayed tier (drained by <c>DrainDelayed</c> / registered via
        /// <c>ScheduleDelayed</c>) until a starvation promotion moves it to the Immediate tier.
        /// </summary>
        Transition = 3,
    }
}
