using System;
using System.Collections.Generic;

namespace Velvet
{
    // The work-loop + lane scheduling driver for Velvet function components.
    // Owns re-render-request intake (from hooks / contexts / transitions), lane-priority
    // classification + queue enrollment, the flush / continue driver loop that turns pending
    // lanes into RenderAndReconcile passes, transition-starvation promotion, and the
    // discrete-event Urgent gate. The render core (RenderAndReconcile) lives in FiberRenderer, with
    // hook-slot commit in FiberHookCommit and committed-tree pooling in FiberTreeReturn; this class drives it.
    internal static class FiberWorkLoop
    {
        // Delay (milliseconds) for the Deferred priority.
        private const int DeferredDelayMs = 100;

        // Number of FlushState invocations before promoting the Transition Lane to Deferred.
        private const int TransitionStarvationThreshold = 30;

        // Set while a discrete user-input event handler (click, change, pointer down/up, key down/up, focus/blur)
        // is executing. A hook-triggered render requested during a discrete event takes the Urgent lane and the
        // originating context's immediate batch is flushed synchronously when the handler returns, so the
        // UI updates before the next frame. Process-global because Unity dispatches one input event at a time on the main
        // thread; FiberEventBindingManager brackets each discrete handler to set and clear it.
        // Known limitation, intentional: because the flag is process-global, an update to a *different*
        // router/panel context that happens while some other context's discrete handler runs is also
        // lane-classified as Urgent. This is harmless: the synchronous flush is performed only for the owning
        // context (the one whose handler is running) — see the owning-context check at the flush site — so the
        // misclassified context's update still drains on its normal frame boundary. A context-scoped flag
        // would instead risk diverging lane classification from the actual synchronous-flush decision, so the
        // process-global flag is kept deliberately.
        internal static bool IsInDiscreteEvent;

        // Internal API that requests a render via a Hook (UseState / UseReducer setter, UseStore subscription).
        // Takes the Urgent lane while a discrete event handler is running (IsInDiscreteEvent),
        // otherwise the Normal lane (a state setter invoked outside a discrete event).
        // setState calls made after an await (outside a discrete-event bracket and outside a transition) take the
        // Normal lane and are coalesced by the frame-boundary FiberBatchScheduler into one drain, so multiple
        // post-await setters commit in a single render — async auto-batching, with no opt-in.
        // The request is silently ignored if the fiber is disposed or not mounted.
        // fiber: Fiber whose state changed.
        public static void RequestRenderFromHook(ComponentFiber fiber)
        {
            if (fiber.IsDisposed || !fiber.IsMounted)
            {
                return;
            }

            // Render-phase setState: the setter updates this fiber's own state while this same fiber
            // is rendering. Such updates are processed synchronously (discard the in-progress
            // output and re-run Render() before committing) instead of scheduling a next-frame
            // re-render. Flag the fiber and let the render loop in RenderAndReconcile re-run; the
            // state slot already holds the new value by the time this is called. A setter for a
            // *different* fiber that fires during this fiber's render falls through to the regular
            // next-frame schedule below.
            if (fiber.IsRendering && ReferenceEquals(FiberAmbientStack.Current, fiber))
            {
                fiber.HasRenderPhaseUpdate = true;
                return;
            }

            if (fiber.IsInTransition)
            {
                ScheduleRerender(fiber, FiberUpdatePriority.Transition);
                return;
            }

            ScheduleRerender(fiber, IsInDiscreteEvent ? FiberUpdatePriority.Urgent : FiberUpdatePriority.Normal);
        }

        // Transition-lane re-render request dedicated to UseDeferredValue.
        // Achieves value-level deferral by scheduling directly into the Transition lane without going through
        // urgent (Normal) (RequestRenderFromHook branches between Normal/Transition based on IsInTransition,
        // but this method always uses Transition).
        // The request is silently ignored if the fiber is disposed or not mounted.
        // fiber: Fiber to schedule a Transition-lane render on.
        public static void RequestTransitionRerender(ComponentFiber fiber)
        {
            if (fiber.IsDisposed || !fiber.IsMounted)
            {
                return;
            }
            ScheduleRerender(fiber, FiberUpdatePriority.Transition);
        }

        internal static void ScheduleRerender(ComponentFiber fiber, FiberUpdatePriority priority)
        {
            fiber.LaneQueue ??= new SortedSet<FiberUpdatePriority>();

            // Record the current highest priority before adding.
            // If the same Lane already exists, Add returns false (coalesced).
            var prevHighest = FiberLane.GetHighestPendingPriority(fiber);

            fiber.LaneQueue.Add(priority);

            // When adding the Transition Lane anew, reset the starvation counter.
            if (priority == FiberUpdatePriority.Transition)
            {
                fiber.TransitionStarvationCounter = 0;
            }

            if (!fiber.IsDirty)
            {
                fiber.IsDirty = true;
                ScheduleFlush(fiber, priority);
            }
            else if (priority < prevHighest && FiberLane.SchedulesOnImmediateTier(priority))
            {
                // The new lane outranks what was already scheduled. When it routes to the immediate tier
                // (Urgent / Normal) but the fiber is currently only enrolled on the delayed tier
                // (prevHighest = Deferred / Transition), enroll it on the immediate tier too so the next-frame
                // and end-of-discrete-event flush can drain it; the per-fiber lane queue still orders the
                // actual drain. ScheduleImmediate dedups, so re-enrolling a fiber already on the immediate
                // tier is a no-op. A more-urgent delayed lane (Deferred over Transition) needs no re-enroll:
                // the fiber is already on the delayed tier with the same delay.
                ScheduleFlush(fiber, priority);
            }
        }

        // Routes the fiber's flush through the tree-wide FiberBatchScheduler so concurrent
        // dirty fibers sharing one ReconcilerContext coalesce into a single frame-boundary
        // drain. Normal / Urgent enqueue on the next-frame tier; Deferred / Transition enqueue on the
        // delayed tier (kept at DeferredDelayMs). The per-fiber lane queue is still drained
        // one lane per FlushState inside the batch, preserving priority ordering and the
        // Deferred delay.
        private static void ScheduleFlush(ComponentFiber fiber, FiberUpdatePriority priority)
        {
            var scheduler = fiber.Reconciler?.Context.BatchScheduler;
            if (scheduler == null)
            {
                // No live Reconciler (disposed mid-flight): fall back to the per-fiber schedule so the
                // request is not silently dropped while the fiber is still mounted.
                fiber.MountPoint?.schedule.Execute(() => FlushState(fiber));
                return;
            }

            if (FiberLane.SchedulesOnImmediateTier(priority))
            {
                scheduler.ScheduleImmediate(fiber);
            }
            else
            {
                scheduler.ScheduleDelayed(fiber, DeferredDelayMs);
            }
        }

        // Flushes the dirty state and runs RenderAndReconcile + RunLayoutEffects.
        // Normally invoked automatically via schedule.Execute.
        // In test environments (no panel attached), call manually after a hook setter fires to confirm
        // immediate reflection.
        // RunLayoutEffects is intentionally outside the OnRenderError guard.
        // An error thrown from an effect is not routed to an Error Boundary; Velvet
        // instead try-catches each effect individually and emits via Debug.LogException.
        // Returns immediately if the fiber is not mounted or not dirty.
        // fiber: Fiber whose pending lane updates should be flushed.
        public static void FlushState(ComponentFiber fiber)
        {
            if (!fiber.IsMounted) return;
            if (!fiber.IsDirty)
            {
                fiber.ClearAllTransitionPending();
                return;
            }

            // Offscreen guard: a fiber inside a wrapper-less Suspense boundary that is currently showing its
            // fallback must not flush independently — its host slot is occupied by the fallback. The boundary's
            // own re-render (scheduled when the resource resolved) re-attempts the primary subtree and commits
            // the reveal in one pass: a resolved resource schedules the boundary itself, not the
            // suspended child. Leave IsDirty set so that re-render picks this fiber up via the expansion.
            var suspendedBoundaries = fiber.Reconciler?.Context.SuspendedBoundaries;
            if (suspendedBoundaries is { Count: > 0 })
            {
                var enclosingBoundary = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber);
                if (enclosingBoundary != null && !ReferenceEquals(enclosingBoundary, fiber)
                    && suspendedBoundaries.Contains(enclosingBoundary))
                {
                    // Defer only PRIMARY (offscreen) descendants — their slot is occupied by the
                    // fallback, so an independent flush would write into the fallback's range. A visible
                    // fallback-subtree fiber (no offscreen ancestor up to the boundary) may flush
                    // normally (the fallback renders while the primary is offscreen).
                    for (var f = fiber; f != null && !ReferenceEquals(f, enclosingBoundary); f = f.Parent)
                    {
                        if (f.IsOffscreen) return;
                    }
                }
            }

            // Detached-host guard: an inline-mounted fiber commits its body into a slot RANGE of a MountPoint it
            // shares with sibling fibers (the parent expansion owns the range; PreviousTree is the patch baseline
            // the parent re-reads). While that host is detached from the mounted tree — e.g. an AnimatePresence ghost
            // whose Motion was just removed from the DOM but whose inner fiber is not yet disposed, kept alive by a
            // store subscription — an independent flush would reconcile into the off-tree container and advance
            // PreviousTree past the live DOM. The parent's next re-render then re-expands this fiber against that
            // advanced baseline while the live container still holds the smaller committed set, over-indexing the
            // inner reconcile (parent.ElementAt(slotStart + i) out of range). Defer instead: leave IsDirty set so a
            // later parent re-render (which re-attaches / re-commits this fiber) reconciles it with the baseline and
            // the DOM in agreement. A root / wrapper-mounted fiber owns its whole MountPoint and is exempt. The
            // check is panel-independent (an EditMode test mounts onto a panel-less root), so it compares
            // VE-roots rather than panel attachment: detachment = the host no longer shares the root's VE-tree.
            //
            // The early return is BEFORE the lane-queue bookkeeping below, so the pending lane stays queued and is
            // deliberately NOT removed or rescheduled here. Rescheduling on the same delayed tier would re-flush,
            // find the host still detached, defer again — a busy-loop. The fiber instead waits for the parent
            // re-render that re-attaches (and re-commits) it, or for disposal, which scrubs the lane queue and dirty
            // flag (SettleSubsumedFiber / Unmount). A further update on the same delayed tier coalesces onto the
            // queued lane without rescheduling (IsDirty is already set, and Transition/Deferred do not re-enrol on
            // the immediate tier). A higher-priority Urgent/Normal update DOES re-enrol and re-flush, but that flush
            // hits this same guard and harmlessly re-defers. Either way a detached fiber never flushes independently.
            if (fiber.IsInlineMounted && IsHostDetachedFromRoot(fiber))
            {
                return;
            }

            PromoteStarvedTransitionLane(fiber);

            // The lane being drained decides this flush's frame budget (Transition / Deferred may time-slice;
            // Urgent / Normal stay synchronous). Capture it from the lane before removing it. With no pending
            // lane (e.g. a context-driven flush) the reconcile runs synchronously.
            var flushBudget = FiberLane.FrameBudgetMs;

            if (fiber.LaneQueue != null && fiber.LaneQueue.Count > 0)
            {
                var flushingLane = fiber.LaneQueue.Min;
                flushBudget = FiberLane.BudgetForLane(flushingLane);
                fiber.LaneQueue.Remove(flushingLane);

                if (fiber.LaneQueue.Count > 0)
                {
                    ScheduleFlush(fiber, fiber.LaneQueue.Min);
                }
                else
                {
                    fiber.IsDirty = false;
                }

                if (fiber.LaneQueue == null || fiber.LaneQueue.Count == 0 || !fiber.LaneQueue.Contains(FiberUpdatePriority.Transition))
                {
                    fiber.ClearAllTransitionPending();
                }
            }
            else
            {
                fiber.IsDirty = false;
                fiber.ClearAllTransitionPending();
            }

            // A resume (ContinueReconcile) reads this so it continues at the same budget the starting lane chose.
            fiber.PendingReconcileBudgetMs = flushBudget;
            FiberRenderer.RenderAndReconcile(fiber, flushBudget);
            // Defer layout / passive effects while a time-sliced reconcile is still paused: a parked commit has
            // only partially mutated the DOM, so a UseLayoutEffect reading a UseRef to a not-yet-attached node
            // would observe null. ContinueReconcile's terminal chunk runs these once the work completes.
            if (fiber.Reconciler?.HasPendingWork != true)
            {
                // Bottom-up commit — descendant effects run before this fiber's so a
                // parent effect that reads a child imperative handle / measured size observes the
                // child's already-applied effect. The drain pops fibers in LIFO order (deepest first).
                FiberEffects.CommitSubtreeEffects(fiber);
                // This independent flush may have toggled a descendant's class / controlled value without
                // re-rendering an enclosing has- element; re-derive the registered has- elements now that the
                // DOM mutations are committed so a has- ancestor that did not itself reconcile is not left stale.
                // Scoped to this flush's region (the fiber's MountPoint subtree) — see RefreshHasVariants.
                FiberNodePatcher.RefreshHasVariants(fiber.Reconciler?.Context, fiber.MountPoint);
            }
        }

        // True when this inline-mounted fiber's MountPoint no longer shares a VE-root with the tree the root fiber
        // was mounted onto — i.e. the host slot range was removed from the live tree (an AnimatePresence ghost's
        // Motion detached, a route subtree swapped out) but this fiber is not yet disposed. Independent flushes
        // while detached desync PreviousTree from the live DOM (see the FlushState guard). Panel-independent so an
        // EditMode test (mounted onto a panel-less root) is not falsely flagged: a still-attached inline fiber and
        // the root fiber resolve to the same VE-root; a detached host resolves to a different one.
        private static bool IsHostDetachedFromRoot(ComponentFiber fiber)
        {
            var host = fiber.MountPoint;
            if (host == null) return false;

            // Walk to the nearest MOUNT ROOT: the app root (Parent == null) or a detached-mount top (a Portal's
            // drained children / a VirtualList's controller items, which legitimately mount into a SEPARATE
            // VE-tree). Comparing against that boundary — not the absolute app root — means a Portal child is judged
            // against the portal target's tree, so its in-place re-render is not falsely flagged as detached.
            var mountRoot = fiber;
            while (mountRoot.Parent != null && mountRoot.DetachedMountContext == null)
            {
                mountRoot = mountRoot.Parent;
            }
            var rootHost = mountRoot.MountPoint;
            if (rootHost == null) return false;

            return VeRoot(host) != VeRoot(rootHost);
        }

        private static UnityEngine.UIElements.VisualElement VeRoot(UnityEngine.UIElements.VisualElement ve)
        {
            while (ve.parent != null) ve = ve.parent;
            return ve;
        }

        // Resumes the suspended state of time-sliced reconciliation in the next frame.
        // Invoked automatically via schedule.Execute. Exposed to tests (the UIToolkit scheduler does not
        // advance in EditMode) so a parked slice can be driven to completion manually.
        internal static void ContinueReconcile(ComponentFiber fiber)
        {
            if (!fiber.IsMounted) return;
            if (fiber.Reconciler == null) return;
            var mountPoint = fiber.MountPoint;
            if (mountPoint == null) return;
            if (!fiber.Reconciler.HasPendingWork) return;

            var fiberPushed = FiberRenderer.PushFiber(fiber);
            try
            {
                // Resume at the budget the starting lane chose so a Transition slice keeps time-slicing.
                // An inline-mount fiber commits its child-count delta incrementally across resume slices, so
                // each slice's delta must be propagated to following siblings here exactly as the initial
                // RenderAndReconcile pass does — otherwise a following parked sibling's captured slotStart goes
                // stale against the rows this slice just inserted / removed.
                if (fiber.IsInlineMounted)
                {
                    var beforeChildCount = fiber.MountPoint?.childCount ?? 0;
                    fiber.Reconciler.ContinueReconcile(fiber.PendingReconcileBudgetMs);
                    var afterChildCount = fiber.MountPoint?.childCount ?? 0;
                    FiberCommitWork.PropagateInlineSlotShift(fiber, afterChildCount - beforeChildCount);
                }
                else
                {
                    fiber.Reconciler.ContinueReconcile(fiber.PendingReconcileBudgetMs);
                }

                if (fiber.Reconciler.HasPendingWork)
                {
                    mountPoint.schedule.Execute(() => ContinueReconcile(fiber));
                }
                else
                {
                    FiberTreeReturn.ReturnPooledObjects(fiber.PendingOldTree);
                    fiber.PendingOldTree = null;
                    // The commit is now fully applied — run the insertion / layout / passive effects FlushState
                    // deferred while this reconcile was paused. Runs once, only on the terminal chunk.
                    // Bottom-up — descendant effects (LIFO drain) before this fiber's.
                    FiberEffects.CommitSubtreeEffects(fiber);
                    // Mirror FlushState's settled-flush pass: re-derive registered has- elements so a
                    // time-sliced flush that toggled a descendant's class / controlled value reflects on a
                    // has- ancestor that did not itself reconcile. Scoped to this flush's region (the fiber's
                    // MountPoint subtree) — see RefreshHasVariants.
                    FiberNodePatcher.RefreshHasVariants(fiber.Reconciler?.Context, fiber.MountPoint);
                }
            }
            catch (Exception ex)
            {
                FiberTreeReturn.ReturnPooledObjects(fiber.PendingOldTree);
                fiber.PendingOldTree = null;
                FiberErrorBoundary.OnRenderError(fiber, ex);
            }
            finally
            {
                FiberRenderer.PopFiber(fiber, fiberPushed);
            }
        }

        private static void PromoteStarvedTransitionLane(ComponentFiber fiber)
        {
            if (fiber.LaneQueue == null || !fiber.LaneQueue.Contains(FiberUpdatePriority.Transition))
            {
                fiber.TransitionStarvationCounter = 0;
                return;
            }

            fiber.TransitionStarvationCounter++;

            if (fiber.TransitionStarvationCounter >= TransitionStarvationThreshold)
            {
                fiber.LaneQueue.Remove(FiberUpdatePriority.Transition);

                if (!fiber.LaneQueue.Contains(FiberUpdatePriority.Deferred))
                {
                    fiber.LaneQueue.Add(FiberUpdatePriority.Deferred);
                }

                fiber.TransitionStarvationCounter = 0;
            }
        }

        // Wrapper that runs internal SetState at Transition priority.
        // UseState setters (etc.) within updates are automatically scheduled on the lowest-priority Lane.
        // Nested StartTransition calls join the outer transition: the inner call runs its
        // updates at Transition priority but does not start a new transition or independently clear
        // isPending; only the outermost call manages the pending flag.
        // fiber: Fiber whose updates are demoted to Transition priority.
        // updates: Action whose state mutations should run at Transition priority. Must not be null.
        public static void StartTransition(ComponentFiber fiber, HookTransitionSlot slot, Action updates)
        {
            if (updates == null) throw new ArgumentNullException(nameof(updates));

            // Re-entrant call: join the outer transition. IsInTransition is already set, so the updates run
            // at Transition priority; the outer call owns the pending lifecycle (this slot's flag is left to
            // the owning StartTransition so a nested call cannot leak a pending flag with no clear point).
            if (fiber.IsInTransition)
            {
                updates();
                return;
            }

            slot.IsPending = true;
            fiber.IsInTransition = true;
            try
            {
                updates();
            }
            finally
            {
                fiber.IsInTransition = false;
                if (!fiber.IsDirty)
                {
                    slot.IsPending = false;
                }
            }
        }

        // Asynchronous StartTransition (an async callback: StartTransition(async () => ...)).
        // isPending stays true across every await inside asyncUpdates and is
        // cleared only after the returned task completes (and no Transition-lane render remains queued). State
        // updates made before, between, and after awaits are scheduled on the Transition lane. Nested
        // StartTransition calls join this transition.
        // fiber: Fiber whose updates are demoted to Transition priority.
        // asyncUpdates: Async action whose state mutations run at Transition priority. Must not be null.
        // A task that completes when asyncUpdates has fully run.
        public static async Cysharp.Threading.Tasks.UniTask StartTransition(
            ComponentFiber fiber, HookTransitionSlot slot, Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates)
        {
            if (asyncUpdates == null) throw new ArgumentNullException(nameof(asyncUpdates));

            // Re-entrant async call: join the outer transition. The outer call owns the pending lifecycle.
            if (fiber.IsInTransition)
            {
                await asyncUpdates();
                return;
            }

            slot.IsPending = true;
            fiber.IsInTransition = true;
            try
            {
                // Keep IsInTransition true across awaits so any setState the continuation performs is routed to
                // the Transition lane and isPending remains true until the whole async action settles.
                await asyncUpdates();
            }
            finally
            {
                fiber.IsInTransition = false;
                if (fiber.IsDisposed
                    || fiber.LaneQueue == null
                    || !fiber.LaneQueue.Contains(FiberUpdatePriority.Transition))
                {
                    slot.IsPending = false;
                }
            }
        }
    }
}
