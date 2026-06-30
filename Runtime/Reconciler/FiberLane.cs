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

        // Frame budget (milliseconds) for time-sliced reconciliation on the Transition / Deferred lanes. Only
        // the fast path (a flat list of host leaves in ReconcileIndexed / ReconcileKeyed) honors a
        // non-zero budget and can pause/resume; a tree containing components / Providers / Fragments / Suspense
        // / Memo takes the general path, which is a single synchronous live-context walk (yielding mid-walk
        // would strand pushed Provider scopes) and ignores the budget. Layout effects are deferred until the
        // reconcile has no pending work (see FlushState / ContinueReconcile), so a
        // paused commit never runs UseLayoutEffect against a not-yet-attached UseRef.
        internal const double TimeSlicedBudgetMs = 5;

        // Test-only override for the time-sliced budget. Negative means "no override" (production value).
        // EditMode tests set this to a tiny value to force a deterministic pause after one node, because the
        // real 5 ms budget rarely overruns on a small list and the UIToolkit scheduler does not advance in
        // EditMode. Mirrors the existing test-controlled IsInDiscreteEvent static.
        internal static double TimeSlicedBudgetOverrideForTest = -1;

        // The highest-priority pending lane on the fiber (lowest enum value wins); Transition when the
        // lane queue is empty. Shared by ScheduleRerender's escalation check.
        internal static FiberUpdatePriority GetHighestPendingPriority(ComponentFiber fiber)
        {
            if (fiber.LaneQueue == null || fiber.LaneQueue.Count == 0)
            {
                return FiberUpdatePriority.Transition;
            }
            return fiber.LaneQueue.Min;
        }

        // Whether priority flushes on the next-frame (immediate) tier rather than the
        // delayed tier. Urgent and Normal flush on the immediate tier; Deferred and Transition are delayed by
        // DeferredDelayMs. Single source of truth for tier membership, shared by the
        // already-dirty escalation in ScheduleRerender and the routing in ScheduleFlush.
        internal static bool SchedulesOnImmediateTier(FiberUpdatePriority priority)
            => priority is FiberUpdatePriority.Urgent or FiberUpdatePriority.Normal;

        // Frame budget for a flush of priority. Transition and Deferred get the time-sliced
        // budget so a large flat-list diff can pause/resume across frames; Urgent and Normal run synchronously
        // (user-input-driven updates are never interrupted). Only the reconciler fast path acts on
        // a non-zero budget — see TimeSlicedBudgetMs.
        internal static double BudgetForLane(FiberUpdatePriority priority)
        {
            if (priority is not (FiberUpdatePriority.Transition or FiberUpdatePriority.Deferred))
            {
                return FrameBudgetMs;
            }
            return TimeSlicedBudgetOverrideForTest >= 0 ? TimeSlicedBudgetOverrideForTest : TimeSlicedBudgetMs;
        }
    }
}
