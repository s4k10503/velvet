namespace Velvet.TestUtilities
{
    /// <summary>
    /// Test-only extension methods for MountedTree.
    /// </summary>
    public static class MountedTreeTestExtensions
    {
        /// <summary>
        /// Immediately flushes the dirty state after a hook setter fires and triggers a re-render.
        /// Iteratively walks the entire tree produced by V.Mount along the fiber tree and calls
        /// FiberWorkLoop.FlushState on each fiber.
        /// Test-only. Must not be used from production code.
        /// </summary>
        public static void FlushStateForTest(this MountedTree mounted)
        {
            FiberTreeTraversal.Visit(mounted.Root, FiberWorkLoop.FlushState);
        }

        /// <summary>
        /// Immediately fires any pending UseEffect (post-paint async) callbacks for the whole tree
        /// produced by V.Mount, via FiberRenderer's tree-wide 2-phase passive drain: every pending
        /// fiber's cleanups run before any setup, both phases post-order (child-before-parent) — the
        /// same React ordering production observes on the post-paint scheduler tick.
        /// Test-only. Must not be used from production code.
        /// </summary>
        public static void FlushEffectsForTest(this MountedTree mounted)
        {
            FiberEffects.FlushPendingPassiveEffects(mounted.Root);
        }

        /// <summary>
        /// Schedules a re-render of <paramref name="fiber"/> on the given lane, the same path a hook setter
        /// takes once the lane is decided. Production picks the lane from the surrounding scheduling context
        /// (Transition for StartTransition / UseDeferredValue, Normal otherwise), so this is the only way a
        /// test can exercise the Urgent and Deferred lanes directly. It mutates only the lane queue and the
        /// batch-scheduler tier; the flush itself still runs through FlushStateForTest or a FiberBatchScheduler
        /// drain entry point.
        /// Test-only. Must not be used from production code.
        /// </summary>
        public static void ScheduleRerenderForTest(this ComponentFiber fiber, FiberUpdatePriority priority)
        {
            FiberWorkLoop.ScheduleRerender(fiber, priority);
        }

        /// <summary>
        /// True while a time-sliced reconcile started by <paramref name="fiber"/> is paused with work still
        /// pending (the fast-path diff exceeded its frame budget and parked). Test-only.
        /// </summary>
        public static bool HasPendingReconcileWorkForTest(this ComponentFiber fiber)
            => fiber.Reconciler?.HasPendingWork == true;

        /// <summary>
        /// Drives a parked time-sliced reconcile to completion by manually invoking the resume that the
        /// UIToolkit scheduler would otherwise fire on each frame boundary (the scheduler does not advance in
        /// EditMode). Each call resumes one frame's worth of work at the budget the starting lane chose.
        /// Test-only. Must not be used from production code.
        /// </summary>
        public static void DrainTimeSlicedReconcileForTest(this ComponentFiber fiber, int maxIterations = 1000)
        {
            var iterations = 0;
            while (fiber.Reconciler?.HasPendingWork == true)
            {
                if (iterations++ >= maxIterations)
                {
                    throw new System.InvalidOperationException(
                        $"DrainTimeSlicedReconcileForTest: {maxIterations} iterations exceeded without completion.");
                }
                FiberWorkLoop.ContinueReconcile(fiber);
            }
        }
    }
}
