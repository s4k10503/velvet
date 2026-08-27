namespace Velvet.TestUtilities
{
    /// <summary>
    /// Test-only extension methods for MountedTree.
    /// </summary>
    public static class MountedTreeTestExtensions
    {
        /// <summary>
        /// Reaches the tree-wide <see cref="FiberBatchScheduler"/> behind a mounted tree — every fixture that
        /// drains a tier (DrainImmediateForTest/DrainDelayedForTest) or inspects pending counts needs this same
        /// accessor path. Test-only.
        /// </summary>
        // Bypasses: nothing — it reaches internal state a test cannot name, and production reaches the same scheduler the same way.
        internal static FiberBatchScheduler GetSchedulerForTest(this MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        /// <summary>
        /// Immediately flushes the dirty state after a hook setter fires and triggers a re-render.
        /// Iteratively walks the entire tree produced by V.Mount along the fiber tree and calls
        /// FiberWorkLoop.FlushState on each fiber.
        /// Test-only. Must not be used from production code.
        /// </summary>
        // Bypasses: the batch scheduler's tier and drain buffer: production calls FiberWorkLoop.FlushState from inside DrainImmediate, over the buffer that pass built.
        public static void FlushStateForTest(this MountedTree mounted)
        {
            FiberTreeTraversal.Visit(mounted.Root, FiberWorkLoop.FlushState);
        }

        /// <summary>
        /// Immediately fires any pending UseEffect (post-paint async) callbacks for the whole tree
        /// produced by V.Mount, via FiberRenderer's tree-wide 2-phase passive drain: every pending
        /// fiber's cleanups run before any setup, both phases post-order (child-before-parent) — the
        /// same ordering production observes on the post-paint scheduler tick.
        /// Test-only. Must not be used from production code.
        /// </summary>
        // Bypasses: the drain's registration and its anchor: production reaches the passive flush through FiberBatchScheduler.FlushPendingPassiveEffects, set by Reconciler as SetPassiveEffectFlush and fired from schedule.Execute.
        public static void FlushEffectsForTest(this MountedTree mounted)
        {
            FiberEffects.FlushPendingPassiveEffects(mounted.Root);
        }

        /// <summary>
        /// Schedules a re-render of <paramref name="fiber"/> on the given lane, the same path a hook setter
        /// takes once the lane is decided. Production picks the lane from the surrounding scheduling context
        /// (Transition inside StartTransition or for a UseDeferredValue derivation, Urgent inside a discrete
        /// event handler, Normal otherwise), so a test that wants a specific lane names it here rather than
        /// arranging the context that would pick it. It mutates only the lane queue and the batch-scheduler
        /// tier; the flush itself still runs through FlushStateForTest or a FiberBatchScheduler drain entry
        /// point.
        /// Test-only. Must not be used from production code.
        /// </summary>
        // Bypasses: lane selection: production picks the lane from the surrounding scheduling context rather than being handed one.
        public static void ScheduleRerenderForTest(this ComponentFiber fiber, FiberUpdatePriority priority)
        {
            FiberWorkLoop.ScheduleRerender(fiber, priority);
        }

        // Small enough that the very first budget check of a reconcile pass already reads over it. Internal so a
        // fixture can pin that this exact value — not the production default — is what a flush threaded.
        internal const double TinyTimeSlicedBudgetMs = 0.0001;

        /// <summary>
        /// Flushes <paramref name="fiber"/>'s highest pending lane with a time-sliced budget too small to admit
        /// a single node, so a Transition flush parks after its first iteration regardless of host
        /// speed — the real budget rarely overruns on a list small enough for a test to build, and the UIToolkit
        /// scheduler does not advance in EditMode. The budget travels on this one call: it is captured on the
        /// fiber as the resume budget (so <see cref="DrainTimeSlicedReconcileForTest"/> continues at the same
        /// tiny budget) and is invisible to every other fiber's flush.
        /// Test-only. Must not be used from production code.
        /// </summary>
        // Bypasses: the real time-slice budget, which no production caller passes.
        public static void FlushStateWithTinyBudgetForTest(this ComponentFiber fiber)
        {
            FiberWorkLoop.FlushState(fiber, TinyTimeSlicedBudgetMs);
        }

        /// <summary>
        /// True while a time-sliced reconcile started by <paramref name="fiber"/> is paused with work still
        /// pending (the fast-path diff exceeded its frame budget and parked). Test-only.
        /// </summary>
        // Bypasses: nothing — it reads Reconciler.HasPendingWork, which production reads too.
        public static bool HasPendingReconcileWorkForTest(this ComponentFiber fiber)
            => fiber.Reconciler?.HasPendingWork == true;

        /// <summary>
        /// Drives a parked time-sliced reconcile to completion by manually invoking the resume that the
        /// UIToolkit scheduler would otherwise fire on each frame boundary (the scheduler does not advance in
        /// EditMode). Each call resumes one frame's worth of work at the budget the starting lane chose.
        /// Test-only. Must not be used from production code.
        /// </summary>
        // Bypasses: the frame boundary: production resumes a parked reconcile from the UIToolkit scheduler, one frame at a time.
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
