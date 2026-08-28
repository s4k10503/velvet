namespace Velvet
{
    // Lane priority + frame-budget math. Pure functions of FiberUpdatePriority
    // (and a fiber's pending lane queue): the highest pending lane, whether a lane flushes on the
    // immediate vs delayed tier, and the per-lane time-slice budget. The scheduling that consumes these
    // (enqueue / flush / continue) lives in FiberWorkLoop, keeping the work-loop driver
    // separate from this lane math.
    internal static class FiberLane
    {
        // Synchronous frame budget (milliseconds). 0 disables time-slicing: the reconcile runs to completion in
        // one pass. Used for the Urgent / Normal lanes (user-input-driven updates are never
        // interrupted), initial mount, and nested host reconciles.
        internal const double FrameBudgetMs = 0;

        // Frame budget (milliseconds) for time-sliced reconciliation on the Transition lane. Only
        // the fast path (a flat list of host leaves in ReconcileIndexed / ReconcileKeyed) honors a
        // non-zero budget and can pause/resume; a tree containing components / Providers / Fragments / Suspense
        // / Memo takes the general path, which is a single synchronous live-context walk (yielding mid-walk
        // would strand pushed Provider scopes) and ignores the budget. Layout effects are deferred until the
        // reconcile has no pending work (see FlushState / ContinueReconcile), so a
        // paused commit never runs UseLayoutEffect against a not-yet-attached UseRef.
        internal const double TimeSlicedBudgetMs = 5;

        // The highest-priority pending lane on the fiber (lowest enum value wins); Transition when the
        // lane queue is empty. Shared by ScheduleRerender's escalation check.
        internal static FiberUpdatePriority GetHighestPendingPriority(ComponentFiber fiber)
        {
            if (fiber.LaneQueue.Count == 0)
            {
                return FiberUpdatePriority.Transition;
            }
            return fiber.LaneQueue.Min;
        }

        // Whether priority flushes on the next-frame (immediate) tier rather than the
        // delayed tier. Urgent and Normal flush on the immediate tier; Transition is delayed by
        // DelayedTierDelayMs. Single source of truth for tier membership, shared by the
        // already-dirty escalation in ScheduleRerender and the routing in ScheduleFlush.
        internal static bool SchedulesOnImmediateTier(FiberUpdatePriority priority)
            => priority is FiberUpdatePriority.Urgent or FiberUpdatePriority.Normal;

        // Frame budget for a flush of priority. Transition slices against timeSlicedBudgetMs so a
        // large flat-list diff can pause/resume across frames; Urgent and Normal run synchronously
        // (user-input-driven updates are never interrupted). Only the reconciler fast path acts on
        // a non-zero budget — see TimeSlicedBudgetMs.
        internal static double BudgetForLane(FiberUpdatePriority priority, double timeSlicedBudgetMs)
            => priority is FiberUpdatePriority.Transition
                ? timeSlicedBudgetMs
                : FrameBudgetMs;
    }
}
