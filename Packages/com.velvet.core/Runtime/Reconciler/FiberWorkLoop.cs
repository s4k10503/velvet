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
        private const int DelayedTierDelayMs = 100;

        // The FlushState invocation at which a continuously-pending Transition lane is promoted
        // (see PromoteStarvedTransitionLane); it survives threshold-1 preempted flushes.
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

        // The transition calls whose callback is running synchronously right now. A Transition-lane enrolment
        // made while these are open is credited to each of them that still owns its slot (see
        // MarkTransitionWorkQueued), so a nested or joined call credits the calls it sits inside as well.
        // Ambient rather than per-fiber, because the fiber owning the state a callback writes need not be the
        // one the transition was started on — a setter received as a prop is the case the per-fiber form
        // missed.
        // Nothing re-opens the scope for an async action's continuation: inferring one from the fiber's
        // in-flight transitions instead gave the Transition lane to unrelated writes in that window.
        // Unsynchronised, so it holds only while one caller pushes and pops at a time.
        private static readonly List<HookTransitionSlot> OpenTransitionScopes = new();

        // UseDeferredValue has to tell the deferred commit from an ordinary render it must keep withholding
        // from, and with no lane visible to it the pending value promotes on whatever renders next, urgent or
        // not. Covers one RenderAndReconcile call or one resumed slice — not the drain that contains it, which
        // flushes many fibers at their own lanes.
        // Ambient rather than per-fiber: a flush's render reaches descendant component bodies through the
        // reconcile, and a deferred value one of those holds belongs to this pass just as much.
        // Saved and restored, so a flush nested inside a render leaves the enclosing answer intact.
        internal static bool IsRenderingTransitionLane;

        // Internal API that requests a render via a Hook (UseState / UseReducer setter, UseStore subscription).
        // Takes the Transition lane while a StartTransition callback is running synchronously; otherwise the
        // Urgent lane while a discrete event handler is running (IsInDiscreteEvent), and the Normal lane elsewhere.
        // setState calls that land on the Normal lane are coalesced by the frame-boundary FiberBatchScheduler into
        // one drain, so several post-await setters commit in a single render — async auto-batching, with no opt-in.
        // The request is silently ignored if the fiber is disposed or not mounted.
        // fiber: Fiber whose state changed.
        public static void RequestRenderFromHook(ComponentFiber fiber)
        {
            if (fiber.IsDisposed || !fiber.IsMounted)
            {
                return;
            }

            // Render-phase setState: the setter updates this fiber's own state while this same
            // fiber's BODY is on the stack. Such updates are processed synchronously (discard the
            // in-progress output and re-run Render() before committing) instead of scheduling a
            // next-frame re-render. Flag the fiber and let the render loop in RenderAndReconcile
            // re-run; the state slot already holds the new value by the time this is called. The
            // gate is the render-phase window, NOT IsRendering: a write landing later in the same
            // flush (a callback ref invoked during the patch, an event dispatched from a detach) is
            // a commit-phase update that falls through to the regular schedule below (a state update
            // issued during commit schedules a follow-up pass) — where the flag would be consumed
            // by nobody and cleared, silently dropping the write. A setter for a *different* fiber
            // that fires during this fiber's render also falls through.
            if (fiber.IsInRenderPhase && ReferenceEquals(FiberAmbientStack.Current, fiber))
            {
                fiber.HasRenderPhaseUpdate = true;
                return;
            }

            // Tested ahead of the discrete-event gate below, because a discrete handler calling
            // startTransition is the ordinary way to start one and its updates are still the transition's.
            if (OpenTransitionScopes.Count > 0)
            {
                // Attributed on this branch rather than inside ScheduleRerender, which would also charge
                // UseDeferredValue's own Transition-lane request (RequestTransitionRerender) to a
                // transition slot that did not ask for it.
                MarkTransitionWorkQueued(fiber);
                ScheduleRerender(fiber, FiberUpdatePriority.Transition);
                return;
            }

            ScheduleRerender(fiber, IsInDiscreteEvent ? FiberUpdatePriority.Urgent : FiberUpdatePriority.Normal);
        }

        // Transition-lane re-render request dedicated to UseDeferredValue.
        // Achieves value-level deferral by scheduling directly into the Transition lane without going through
        // urgent (Normal): RequestRenderFromHook classifies by the surrounding scheduling scope, which for a
        // deferred value is whatever render happened to observe the changed input.
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
            // Record the current highest priority before adding.
            // If the same Lane already exists, Add returns false (coalesced).
            var prevHighest = FiberLane.GetHighestPendingPriority(fiber);

            // Goes through EnsureLanes()/Lanes directly (not the fiber.LaneQueue read accessor): FiberLaneSet
            // is a struct, so a property getter can only hand back a copy — Add must mutate the backing
            // LaneState.Queue field in place to enroll for real.
            var lanes = fiber.EnsureLanes();
            var enrolled = lanes.Queue.Add(priority);
            // Records the request whether or not it changed the queue. Only a caller that resets this first
            // reads it (FiberRenderer.SubsumeFiberIntoThisPass), so it is otherwise inert history.
            lanes.LanesRequestedSinceReset.Add(priority);

            // A coalesced re-add must NOT restart the starvation clock: it measures how long the lane
            // has been continuously pending, so sustained re-scheduling (e.g. a per-frame
            // transition-tier update) cannot indefinitely defeat the promotion while higher-priority
            // work keeps preempting the flush. The genuine-enrol reset is still required: without it a
            // lane that drained normally would inherit the previous cycle's residual count and promote
            // early.
            if (priority == FiberUpdatePriority.Transition && enrolled)
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
                // (prevHighest = Transition), enroll it on the immediate tier too so the next-frame
                // and end-of-discrete-event flush can drain it; the per-fiber lane queue still orders the
                // actual drain. ScheduleImmediate dedups, so re-enrolling a fiber already on the immediate
                // tier is a no-op.
                ScheduleFlush(fiber, priority);
            }
        }

        // Routes the fiber's flush through the tree-wide FiberBatchScheduler so concurrent
        // dirty fibers sharing one ReconcilerContext coalesce into a single frame-boundary
        // drain. Normal / Urgent enqueue on the next-frame tier; Transition enqueues on the
        // delayed tier (kept at DelayedTierDelayMs). The per-fiber lane queue is still drained
        // one lane per FlushState inside the batch, preserving priority ordering and the
        // delayed-tier delay.
        internal static void ScheduleFlush(ComponentFiber fiber, FiberUpdatePriority priority)
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
                scheduler.ScheduleDelayed(fiber, DelayedTierDelayMs);
            }
        }

        // Flushes the dirty state and runs RenderAndReconcile + the layout-effect commit.
        // Normally invoked automatically via schedule.Execute.
        // In test environments (no panel attached), call manually after a hook setter fires to confirm
        // immediate reflection.
        // The layout-effect commit is intentionally outside the OnRenderError guard.
        // An error thrown from an effect is not routed to an Error Boundary; Velvet
        // instead try-catches each effect individually and emits via Debug.LogException.
        // Returns immediately if the fiber is not mounted or not dirty.
        // fiber: Fiber whose pending lane updates should be flushed.
        public static void FlushState(ComponentFiber fiber) => FlushState(fiber, FiberLane.TimeSlicedBudgetMs);

        // The time-sliced budget travels on the call rather than being read from FiberLane, so a caller that
        // needs one flush to park deterministically cannot leave that budget set for an unrelated later flush
        // to inherit.
        internal static void FlushState(ComponentFiber fiber, double timeSlicedBudgetMs)
        {
            if (!fiber.IsMounted) return;
            if (!fiber.IsDirty)
            {
                fiber.SettleTransitionPending();
                return;
            }

            // Offscreen guard: a fiber inside a wrapper-less Suspense boundary that is currently showing its
            // fallback must not flush independently — its host slot is occupied by the fallback. The boundary's
            // own re-render (scheduled when the resource resolved) re-attempts the primary subtree and commits
            // the reveal in one pass: a resolved resource schedules the boundary itself, not the
            // suspended child. Leave IsDirty set so that re-render picks this fiber up via the expansion.
            var context = fiber.Reconciler?.Context;
            if (context is { AnyBoundaryShowingFallback: true })
            {
                var enclosingBoundary = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber);
                if (enclosingBoundary != null && !ReferenceEquals(enclosingBoundary, fiber)
                    && context.IsBoundaryShowingFallback(enclosingBoundary))
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
            // re-render that re-attaches (and re-commits) it, which settles every lane that re-render subsumes
            // (FiberRenderer.SubsumeFiberIntoThisPass), or for disposal, which scrubs the queue and dirty flag outright (Unmount).
            // A further update on the same delayed tier coalesces onto the
            // queued lane without rescheduling (IsDirty is already set, and Transition does not re-enrol on
            // the immediate tier). A higher-priority Urgent/Normal update DOES re-enrol and re-flush, but that flush
            // hits this same guard and harmlessly re-defers. Either way a detached fiber never flushes independently.
            if (fiber.IsInlineMounted && IsHostDetachedFromRoot(fiber))
            {
                return;
            }

            PromoteStarvedTransitionLane(fiber);

            // The lane being drained decides this flush's frame budget (Transition may time-slice;
            // Urgent / Normal stay synchronous). Capture it from the lane before removing it. With no pending
            // lane (e.g. a context-driven flush) the reconcile runs synchronously.
            var flushBudget = FiberLane.FrameBudgetMs;
            var drainsTransitionWork = false;

            if (fiber.LaneQueue.Count > 0)
            {
                var flushingLane = fiber.LaneQueue.Min;
                flushBudget = FiberLane.BudgetForLane(flushingLane, timeSlicedBudgetMs);
                // A starvation-promoted lane carries the transition's own updates under the Normal label, and
                // the marker that says so is cleared a few lines below, so read it here. Withholding a deferred
                // value from the promoted drain would put it back behind the traffic promotion exists to escape.
                drainsTransitionWork = flushingLane == FiberUpdatePriority.Transition
                    || (flushingLane == FiberUpdatePriority.Normal && fiber.HasPromotedTransition);
                // A non-empty read above means Lanes was already allocated (Queue is only ever populated
                // through EnsureLanes()), so the backing field is safe to mutate directly here — the
                // fiber.LaneQueue accessor itself only ever hands back a read-only copy of the mask.
                fiber.Lanes!.Queue.Remove(flushingLane);

                // The promoted marker retires at the drain that commits its content — a
                // starvation-promoted lane rides the Normal label (see HasPromotedTransition).
                if (flushingLane == FiberUpdatePriority.Normal)
                {
                    fiber.HasPromotedTransition = false;
                }

                if (fiber.LaneQueue.Count > 0)
                {
                    ScheduleFlush(fiber, fiber.LaneQueue.Min);
                }
                else
                {
                    fiber.IsDirty = false;
                }

                // The Transition label alone is not the settled signal — starvation promotion erases it
                // while the promoted work is still queued (possibly parked behind an Urgent drain), so
                // the promoted marker must ALSO be clear before the pending flags may sweep.
                if (ShouldSettleBeforeReconcile(fiber, drainsTransitionWork))
                {
                    fiber.SettleTransitionPending();
                }
            }
            else
            {
                fiber.IsDirty = false;
                fiber.SettleTransitionPending();
            }

            // A resume (ContinueReconcile) reads both of these so it continues at the same budget the starting
            // lane chose, and answers the deferred-commit question the same way this pass does.
            fiber.PendingReconcileBudgetMs = flushBudget;
            fiber.PendingReconcileDrainsTransitionWork = drainsTransitionWork;
            var wasRenderingTransitionLane = IsRenderingTransitionLane;
            IsRenderingTransitionLane = drainsTransitionWork;
            try
            {
                FiberRenderer.RenderAndReconcile(fiber, flushBudget);
            }
            finally
            {
                IsRenderingTransitionLane = wasRenderingTransitionLane;
            }
            // Defer layout / passive effects while a time-sliced reconcile is still paused: a parked commit has
            // only partially mutated the DOM, so a UseLayoutEffect reading a UseRef to a not-yet-attached node
            // would observe null. ContinueReconcile's terminal chunk runs these once the work completes.
            if (fiber.Reconciler?.HasPendingWork != true)
            {
                SettleCompletedTransition(fiber);
                // Bottom-up commit — descendant effects run before this fiber's so a
                // parent effect that reads a child imperative handle / measured size observes the
                // child's already-applied effect. The drain pops fibers in LIFO order (deepest first).
                FiberEffects.CommitSubtreeEffects(fiber);
                // This independent flush may have toggled a descendant's class / controlled value without
                // re-rendering an enclosing has- element; re-derive the registered has- elements now that the
                // DOM mutations are committed so a has- ancestor that did not itself reconcile is not left stale.
                // Scoped to this flush's region (the fiber's MountPoint subtree) — see RefreshHasVariants.
                FiberNodePatcher.RefreshHasVariants(fiber.Reconciler?.Context, fiber.MountPoint);
                FlushCompletedTransitionIndicator(fiber);
            }
        }

        private static void SettleCompletedTransition(ComponentFiber fiber)
        {
            if (fiber.PendingReconcileDrainsTransitionWork)
            {
                fiber.SettleTransitionPendingAfterCommit();
            }
        }

        private static bool ShouldSettleBeforeReconcile(ComponentFiber fiber, bool drainsTransitionWork)
            => !drainsTransitionWork
                && (fiber.LaneQueue.Count == 0
                    || (!fiber.LaneQueue.Contains(FiberUpdatePriority.Transition)
                        && !fiber.HasPromotedTransition));

        private static void FlushCompletedTransitionIndicator(ComponentFiber fiber)
        {
            if (fiber.PendingReconcileDrainsTransitionWork)
            {
                fiber.Reconciler?.Context.BatchScheduler.FlushImmediate();
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
                // The resumed slice can still expand components, so it answers the deferred-commit question
                // the same way the pass that parked it did — see PendingReconcileDrainsTransitionWork.
                var wasRenderingTransitionLane = IsRenderingTransitionLane;
                IsRenderingTransitionLane = fiber.PendingReconcileDrainsTransitionWork;
                try
                {
                    if (fiber.IsInlineMounted)
                    {
                        // Logical (container-blind) count, not raw childCount: this same ContinueReconcile call
                        // now also drains any Portal / z-layer mount this slice enqueued (see Reconciler.
                        // ContinueReconcile), and a first-of-its-sign container the drain creates or removes
                        // would otherwise leak its own +-1 into this fiber's delta exactly like
                        // FiberCommitWork.ReconcileIntoSlotRange's own measurement — see
                        // LogicalMountPointChildCount.
                        var beforeChildCount = FiberCommitWork.LogicalMountPointChildCount(fiber.MountPoint);
                        fiber.Reconciler.ContinueReconcile(fiber.PendingReconcileBudgetMs);
                        var afterChildCount = FiberCommitWork.LogicalMountPointChildCount(fiber.MountPoint);
                        FiberCommitWork.PropagateInlineSlotShift(fiber, afterChildCount - beforeChildCount);
                    }
                    else
                    {
                        fiber.Reconciler.ContinueReconcile(fiber.PendingReconcileBudgetMs);
                    }
                }
                finally
                {
                    IsRenderingTransitionLane = wasRenderingTransitionLane;
                }

                if (fiber.Reconciler.HasPendingWork)
                {
                    mountPoint.schedule.Execute(() => ContinueReconcile(fiber));
                }
                else
                {
                    fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
                    // Detach before retiring: the sweep's own mark treats owner.PendingOldTree as
                    // live, and a still-attached reference would spare this very sweep's target.
                    var completedBaseline = fiber.PendingOldTree;
                    fiber.PendingOldTree = null;
                    FiberTreeReturn.ReturnRetiredTree(completedBaseline, fiber);
                    SettleCompletedTransition(fiber);
                    // The commit is now fully applied — run the insertion / layout / passive effects FlushState
                    // deferred while this reconcile was paused. Runs once, only on the terminal chunk.
                    // Bottom-up — descendant effects (LIFO drain) before this fiber's.
                    FiberEffects.CommitSubtreeEffects(fiber);
                    // Mirror FlushState's settled-flush pass: re-derive registered has- elements so a
                    // time-sliced flush that toggled a descendant's class / controlled value reflects on a
                    // has- ancestor that did not itself reconcile. Scoped to this flush's region (the fiber's
                    // MountPoint subtree) — see RefreshHasVariants.
                    FiberNodePatcher.RefreshHasVariants(fiber.Reconciler?.Context, fiber.MountPoint);
                    // The setState-in-commit guarantee is entry-point-agnostic: this terminal slice's
                    // commits may have invoked callback refs that wrote state (the measure-in-ref
                    // pattern), and the resume runs outside any batch drain — flush now so the write
                    // commits before the frame yields. Safe here: the pass completed (no pending
                    // work) and the reconcile-active bracket was exited by ContinueReconcile above.
                    fiber.Reconciler?.Context.BatchScheduler.FlushImmediate();
                }
            }
            catch (Exception ex)
            {
                fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
                // Detach before retiring — same self-mark hazard as the completion path above.
                var abortedBaseline = fiber.PendingOldTree;
                fiber.PendingOldTree = null;
                FiberTreeReturn.ReturnRetiredTree(abortedBaseline, fiber);
                if (fiber.PendingReconcileDrainsTransitionWork)
                {
                    fiber.SettleTransitionPending();
                }
                FiberErrorBoundary.OnRenderError(fiber, ex);
            }
            finally
            {
                FiberRenderer.PopFiber(fiber, fiberPushed);
            }
        }

        private static void PromoteStarvedTransitionLane(ComponentFiber fiber)
        {
            if (!fiber.LaneQueue.Contains(FiberUpdatePriority.Transition))
            {
                fiber.TransitionStarvationCounter = 0;
                return;
            }

            fiber.TransitionStarvationCounter++;

            if (fiber.TransitionStarvationCounter >= TransitionStarvationThreshold)
            {
                // Promote to Normal so the starved work coalesces with the very traffic that kept
                // preempting it and drains in this flush (the caller drains the queue's minimum next;
                // co-pending Urgent drains still go first, parking the promoted lane on the immediate
                // tier, not back behind the sustained stream that starved it). An expired lane must
                // flush synchronously, abandoning time-slicing, so starvation is bounded no matter how
                // relentless the higher-priority traffic is. The promoted marker keeps the settle sweep
                // honest about the erased Transition label (see HasPromotedTransition), so isPending
                // still clears at (not before) the commit that renders the promoted content.
                // Contains(Transition) above guarantees Lanes was already allocated; mutate the backing
                // field directly, as the fiber.LaneQueue accessor only hands back a read-only copy.
                fiber.Lanes!.Queue.Remove(FiberUpdatePriority.Transition);
                fiber.Lanes.Queue.Add(FiberUpdatePriority.Normal);
                fiber.HasPromotedTransition = true;
                fiber.TransitionStarvationCounter = 0;
            }
        }

        // Wrapper that runs internal SetState at Transition priority.
        // UseState setters (etc.) called during updates are scheduled on the lowest-priority Lane, whichever
        // fiber owns the state slot they write.
        // A re-entrant call on the SAME slot joins the call that owns it: the inner call runs its updates at
        // Transition priority but does not start a new transition or independently clear isPending. A call on
        // a different slot is a concurrent transition and owns its own pending flag, whether or not another
        // slot's transition is in flight.
        // fiber: Fiber whose transition slot tracks this call's pending state.
        // updates: Action whose state mutations should run at Transition priority. Must not be null.
        public static void StartTransition(ComponentFiber fiber, HookTransitionSlot slot, Action updates)
        {
            if (updates == null) throw new ArgumentNullException(nameof(updates));

            // The scope opens here too — classification is ambient, so a starter that outlived its component
            // still marks what its callback writes on live ones. Only the pending bookkeeping below is
            // skipped: the flag is read during a render, and a disposed component has none left.
            if (fiber.IsDisposed)
            {
                RunInTransitionScope(slot, updates);
                return;
            }

            if (!slot.HasActiveOwner)
            {
                slot.IsPending = true;
                slot.OwnerGeneration++;
            }
            var ownerGeneration = slot.OwnerGeneration;
            slot.OwnerDepth++;
            try
            {
                RunInTransitionScope(slot, updates);
            }
            finally
            {
                if (slot.OwnerGeneration == ownerGeneration)
                {
                    slot.OwnerDepth--;
                    // A callback that enrolled nothing has settled the moment it returns, whatever else the
                    // fiber is busy with — the previous fiber-wide dirty test held isPending up for the
                    // duration of another slot's work. Once it did enrol, what settles it is the drain of
                    // each fiber it enrolled — see ComponentFiber.DischargeTransitionEnrolments.
                    if (!slot.HasActiveOwner && !slot.HasQueuedWork)
                    {
                        var showedPending = slot.IsPending && slot.LastRenderedPending;
                        slot.IsPending = false;
                        // A callback can render the declaring component without enrolling anything on it —
                        // driving a flush of work queued before the call, say — and that render read the
                        // flag this clear invalidates.
                        if (showedPending)
                        {
                            ComponentFiber.RequestRenderForClearedPending(slot);
                        }
                    }
                }
            }
        }

        private static void MarkTransitionWorkQueued(ComponentFiber fiber)
        {
            foreach (var slot in OpenTransitionScopes)
            {
                // An unmount driven from inside the callback releases the slots of the fiber it tears down
                // (ComponentFiber.ReleaseTransitionSlotOwnership), and a released slot is one a remount hands
                // to the next transition — which would then wait on an enrolment its own callback never made.
                if (!slot.HasActiveOwner)
                {
                    continue;
                }
                fiber.EnrolTransitionSlot(slot);
            }
        }

        private static void RunInTransitionScope(HookTransitionSlot slot, Action body)
        {
            OpenTransitionScopes.Add(slot);
            try
            {
                body();
            }
            finally
            {
                OpenTransitionScopes.RemoveAt(OpenTransitionScopes.Count - 1);
            }
        }

        // The async starter runs its callback through this overload and awaits the returned task outside the
        // scope, so the transition covers everything the callback runs before it first suspends — not
        // everything before its first `await`, which is a different boundary whenever the awaited task had
        // already completed. UseTransitionTests holds both sides of that difference. Awaiting inside would
        // extend the scope across every await the action takes.
        private static T RunInTransitionScope<T>(HookTransitionSlot slot, Func<T> body)
        {
            OpenTransitionScopes.Add(slot);
            try
            {
                return body();
            }
            finally
            {
                OpenTransitionScopes.RemoveAt(OpenTransitionScopes.Count - 1);
            }
        }

        // Asynchronous StartTransition (an async callback: StartTransition(async () => ...)).
        // isPending stays true across every await inside asyncUpdates and is cleared once the returned task
        // completes and every fiber its callback enrolled has discharged that work — by committing it, by
        // unmounting, or by the scheduler dropping the starvation-promoted lane it rode at the update-depth
        // cap. Unmounting the declaring component clears the flag ahead of any of that, the release taking
        // the slot off this call (ReleaseTransitionSlotOwnership). The
        // updates the action makes before it first suspends are scheduled on the Transition lane (see
        // RunInTransitionScope for where that boundary falls); reaching it past a suspension means wrapping
        // those updates in a further StartTransition call, which joins this transition. A call on another
        // slot does not.
        // fiber: Fiber whose transition slot tracks this call's pending state.
        // asyncUpdates: Async action whose run up to its first suspension is at Transition priority. Must not
        // be null.
        // A task that completes when asyncUpdates has fully run.
        public static async Cysharp.Threading.Tasks.UniTask StartTransition(
            ComponentFiber fiber, HookTransitionSlot slot, Func<Cysharp.Threading.Tasks.UniTask> asyncUpdates)
        {
            if (asyncUpdates == null) throw new ArgumentNullException(nameof(asyncUpdates));

            // Same disposed guard as the sync overload: the scope, without the flag.
            if (fiber.IsDisposed)
            {
                await RunInTransitionScope(slot, asyncUpdates);
                return;
            }

            if (!slot.HasActiveOwner)
            {
                slot.IsPending = true;
                slot.OwnerGeneration++;
            }
            // While the action is awaiting (before its setState calls land) the fiber can be entirely
            // clean, and a drain callback armed earlier that fires on that clean fiber must not read the
            // empty lane queue as this transition having settled (see SettleTransitionPending).
            slot.AsyncOwnerDepth++;
            slot.OwnerDepth++;
            var ownerGeneration = slot.OwnerGeneration;
            var suspended = false;
            try
            {
                // The call stays inside the try: asyncUpdates need not be an async method, and one that is not
                // can throw before handing a task back, which must still reach the release below.
                var action = RunInTransitionScope(slot, asyncUpdates);
                // A task still pending where the scope has already closed is a callback that suspended, since
                // the scope closes exactly where it hands the task back. Pinned by the completion case for an
                // action that never suspended, which fails if that stops answering.
                suspended = action.Status == Cysharp.Threading.Tasks.UniTaskStatus.Pending;
                await action;
            }
            finally
            {
                // An unmount forces the release this task can no longer perform, so a task settling afterwards
                // must not write over whatever took the slot since — see ReleaseTransitionSlotOwnership.
                if (slot.OwnerGeneration == ownerGeneration)
                {
                    slot.AsyncOwnerDepth--;
                    slot.OwnerDepth--;
                    // Same slot-scoped exit as the sync overload.
                    if (!slot.HasActiveOwner && (fiber.IsDisposed || !slot.HasQueuedWork))
                    {
                        var wasPending = slot.IsPending;
                        slot.IsPending = false;
                        // A task continuation renders nothing on its own, so a flag lit across a suspension
                        // needs this unconditionally. An action that never suspended is held to what the
                        // synchronous overload's exit does — ask only where the component's last render
                        // read the flag lit.
                        if (wasPending && (suspended || slot.LastRenderedPending))
                        {
                            ComponentFiber.RequestRenderForClearedPending(slot);
                        }
                    }
                }
            }
        }
    }
}
