#nullable enable
using System;
using Unity.Profiling;
using UnityEngine.UIElements;

namespace Velvet
{
    // Static reconciler / renderer for Velvet function components.
    // A set of static methods taking a fiber as argument drives render / commit and fiber lifecycle.
    // Holds no per-component class instance; all state is aggregated on ComponentFiber.
    // Public entry points: CreateRoot (V.Mount path), CreateChild (ComponentRegistry path),
    // Mount, Unmount, Dispose.
    // RenderAndReconcile orchestrates the per-render work-state machine but delegates the two phases:
    // the render phase (body invocation, render-phase loop) lives in FiberBeginWork, with the hook-count
    // check in HookCountSentinel, and the commit phase (host-tree application + inline-slot geometry) in
    // FiberCommitWork.
    // Re-render-request intake and lane scheduling (the work-loop driver) live in FiberWorkLoop;
    // context value changes route through RequestRenderForContext here, and async resolves through
    // NotifyAsyncResourceCompleted.
    internal static class FiberRenderer
    {
        // Profiler marker that encompasses Render + Reconcile.
        // In the profiler, the parent/child structure is Velvet.Render > Velvet.Reconcile.
        private static readonly ProfilerMarker s_renderMarker = new("Velvet.Render");

        // Cached static method-group delegate to eliminate per-fiber delegate allocation.
        // Assigned to ComponentFiber.RequestRenderForContextHandler during Fiber initialization in
        // CreateRoot / CreateChild.
        private static readonly Action<ComponentFiber> s_requestRenderForContextHandler = RequestRenderForContext;

        #region Factory

        // Creates a root fiber on the V.Mount path. MountedTree retains this return value.
        // body: Function-component body that produces the VNode tree on each render.
        // isErrorBoundary: When true, marks this fiber as eligible to catch child render exceptions.
        // The created root ComponentFiber, not yet mounted.
        public static ComponentFiber CreateRoot(Func<VNode> body, bool isErrorBoundary = false)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            return new ComponentFiber
            {
                Body = body,
                IsErrorBoundary = isErrorBoundary,
                RequestRenderForContextHandler = s_requestRenderForContextHandler,
            };
        }

        // Creates a child fiber on the ComponentRegistry path. The created fiber is not yet attached to a tree.
        // The caller should attach it to a tree (e.g. via ComponentFiber.AppendChild) before calling
        // Mount.
        // body: Function-component body that produces the VNode tree on each render.
        // isErrorBoundary: When true, marks this fiber as eligible to catch child render exceptions.
        // The created child ComponentFiber, not yet attached or mounted.
        public static ComponentFiber CreateChild(Func<VNode> body, bool isErrorBoundary = false)
            => CreateRoot(body, isErrorBoundary);

        #endregion

        #region Lifecycle

        // Attaches the fiber to mountPoint and runs the initial render + layout effects.
        // fiber: Fiber to mount. Must not already be mounted.
        // mountPoint: VisualElement that hosts the rendered tree. Must not be null.
        // sharedContext, onCaughtError, motionClock: see SetupMount.
        public static void Mount(
            ComponentFiber fiber, VisualElement? mountPoint, ReconcilerContext? sharedContext = null,
            Action<Exception, ErrorInfo>? onCaughtError = null, MotionClock? motionClock = null)
        {
            SetupMount(fiber, mountPoint, sharedContext, onCaughtError, motionClock);
            var context = fiber.Reconciler!.Context;
            RenderAndReconcile(fiber);
            FiberEffects.CommitSubtreeEffects(fiber, mountDoubleInvoke: true);
            FiberEffects.CommitStrandedLayoutWork(context);
            // The setState-in-commit guarantee is entry-point-agnostic: a callback ref or layout
            // effect that writes state during THIS mount (the measure-in-ref pattern) must commit
            // before the caller regains control, exactly as a drain-driven flush loops until quiet —
            // otherwise the first painted frame shows the pre-write state.
            fiber.Reconciler?.Context.BatchScheduler.FlushImmediate();
        }

        // Inline-mount variant for wrapper-less fibers. The fiber's render output is held on the
        // fiber's ComponentFiber.PreviousTree for the caller (typically
        // GeneralPathReconciler.ExpandInlineRecursive) to incorporate into the parent expansion;
        // no Reconcile is issued from the fiber itself. The caller is responsible for placing the
        // output VEs into parent.children at slotStart.
        // Subsequent setState-triggered re-renders use the fiber's own Reconciler with slot-range
        // addressing (the deferReconcile flag is only honored on this initial mount path).
        // sharedContext: see SetupMount.
        public static void MountInline(ComponentFiber fiber, VisualElement? parent, int slotStart, ReconcilerContext? sharedContext = null)
        {
            SetupMount(fiber, parent, sharedContext);
            fiber.IsInlineMounted = true;
            fiber.MountSlotStart = slotStart;
            RenderAndReconcile(fiber, deferReconcile: true);
            // Layout effects setup runs AFTER the DOM mutations + ref attach are committed for
            // the entire subtree. Inline-mount
            // defers child CreateElement to the parent expansion, which happens AFTER MountInline
            // returns, and the ref setups it queues run later still (ReconcilerContext.DrainRefAttaches),
            // so running LayoutEffects here would observe
            // stale (null) refs. Push the fiber onto the deferred stack and let the top-level
            // reconcile entry drain it (LIFO = bottom-up) before its own layout-effect commit so the
            // root commits last.
            var mountContext = fiber.Reconciler!.Context;
            mountContext.DeferredInlineLayoutEffectFibers.Push((fiber, IsMount: true, mountContext.CurrentPass));
            FiberEffects.ScheduleRunEffects(fiber, mountDoubleInvoke: true);
        }

        // Synchronously re-renders an already-mounted inline fiber into the caller's batch pass and settles
        // it there — the caller (parent expansion) sees the fresh ComponentFiber.PreviousTree, and the lanes
        // that render satisfied are un-enrolled. One entry point rather than two, because the render opens
        // the window the settle reads and a settle reached without one retains against whatever was
        // requested since the last window closed.
        //
        // Like MountInline it skips nothing but SetupMount (already initialized) on the render side; unlike
        // MountInline it settles, since a fiber that was already mounted can have lanes pending that this
        // render just satisfied. Effects are pushed onto the shared deferred drain so the top-level
        // reconcile entry runs them bottom-up with the rest of the subtree: a deps-changed effect must run
        // its prior cleanup + new setup during the layout-effect commit; staged effects would otherwise be
        // cleared by the next RenderAndReconcile without running.
        internal static void SubsumeFiberIntoThisPass(ComponentFiber fiber)
        {
            if (!fiber.IsMounted)
            {
                throw new InvalidOperationException(
                    "FiberRenderer.SubsumeFiberIntoThisPass: fiber must already be mounted.");
            }
            fiber.OpenSubsumedRenderWindow();
            RenderAndReconcile(fiber, deferReconcile: true);
            // The render above can dispose this fiber, which nulls Reconciler — RenderAndReconcile's own
            // post-render arm reads the field for that same reason and names the cascade that does it.
            // There is then no pass left to subsume into: the two effect queues below would stage a layout
            // effect and a paint-tick effect against a disposed fiber.
            var subsumedReconciler = fiber.Reconciler;
            if (subsumedReconciler == null) return;
            // Update commit: drain side runs prior cleanup + new setup (deps-comparing) without
            // the Editor-only mount double-invoke. ScheduleRunEffects forwards the same flag so the
            // passive (UseEffect) cleanup+setup pair fires at the next paint-tick.
            var subsumedContext = subsumedReconciler.Context;
            subsumedContext.DeferredInlineLayoutEffectFibers.Push((fiber, IsMount: false, subsumedContext.CurrentPass));
            FiberEffects.ScheduleRunEffects(fiber, mountDoubleInvoke: false);
            SettleSubsumedFiber(fiber);
        }

        // Settles a fiber that the render above just subsumed into the ancestor's batch pass. Every update
        // pending BEFORE that render coalesces into it: the render ran with the fiber's latest state, so
        // those lanes are satisfied at once. It un-enrolls every lane the render did not itself ask for
        // again, and when that empties the queue it
        // also clears the dirty flag and the transition-pending slots and drops the fiber from BOTH
        // batch-scheduler tiers, so a later drain does not independently re-process it — without which a
        // higher-priority lane queued on the child (e.g. Transition on the delayed tier) would be stranded
        // and silently dropped by FlushState's not-dirty early-return.
        //
        // A lane the render itself requested is not one that render satisfied, and survives. What it requested
        // has to be recorded as the render runs rather than derived from a before-image of the queue, because
        // a request that coalesces onto an already-pending lane leaves no trace in the mask — see
        // FiberLaneSet.RetainAll. A body scheduling during its own render is the reachable case
        // (UseDeferredValue re-queues its Transition lane on every render that is not the deferred commit),
        // and losing it left a deferred value fed from a prop never committing at all.
        private static void SettleSubsumedFiber(ComponentFiber fiber)
        {
            // Direct field access through Lanes (not the fiber.LaneQueue read accessor): EnsureLanes would
            // allocate a LaneState for the lane-less majority of subsumed fibers (every non-memoized child
            // re-render lands here), defeating the documented lazy-allocation invariant, and a FiberLaneSet
            // handed back by a property getter is a copy — RetainAll() through it would mutate a throwaway
            // value instead of the backing queue.
            if (fiber.Lanes != null)
            {
                fiber.Lanes.Queue.RetainAll(fiber.Lanes.LanesRequestedSinceReset);
                fiber.Lanes.LanesRequestedSinceReset.Clear();
                // The subsuming render committed the fiber's latest (eagerly-written) state, so any
                // starvation-promoted work is satisfied too — a marker left true on the now-clean fiber
                // would mis-time a LATER transition's settle (its sweep would be skipped while other lanes
                // queue).
                fiber.Lanes.HasPromotedTransition = false;
            }

            if (fiber.LaneQueue.Count > 0)
            {
                // Re-enrol, for two reasons the surviving lane cannot tell apart here. A drain empties its
                // tier's pending set before flushing any fiber, so when the settle runs inside one, an
                // enrolment predating that pass is already gone. And a fiber that was dirty carrying only
                // Normal was enrolled on the immediate tier alone: a Transition request satisfies neither
                // term of ScheduleRerender's escalation check, so it scheduled nothing, and RetainAll has
                // just dropped the Normal that was holding the only enrolment it had.
                // ScheduleFlush dedups, so asking unconditionally costs nothing when the lane has a tier.
                fiber.IsDirty = true;
                FiberWorkLoop.ScheduleFlush(fiber, fiber.LaneQueue.Min);
                // A surviving Transition lane keeps isPending lit only for the slot that queued it. The
                // lane is what the render asked for again, so it may equally be a UseDeferredValue's, and
                // reading the lane alone held the flag up until that unrelated deferral drained.
                fiber.ClearSettledTransitionPending();
                return;
            }

            fiber.IsDirty = false;
            fiber.SettleTransitionPending();
            fiber.Reconciler?.Context.BatchScheduler.Remove(fiber);
        }

        // sharedContext: the ReconcilerContext this fiber must join, when its creator already knows
        // one authoritatively — ComponentRegistry passes its own _ctx here (both its inline and
        // wrapper-mounted GetOrCreate paths), since a fiber it creates always belongs to that
        // context regardless of tree position. Deriving the context from fiber.Parent?.Reconciler?.Context
        // instead is unreliable: fiber.Parent is only set when _ctx.FiberStack.Current was non-null at
        // AppendChild time, which requires an ancestor fiber's render to already be in progress
        // (RenderAndReconcile / TryCatch push it). A bare Reconciler.Reconcile() call — not driven
        // through V.Mount, e.g. a hand-authored tree in a test — has nothing on FiberStack, so a nested
        // ComponentNode's fiber.Parent stays null even though ComponentRegistry itself is unambiguously
        // running inside a real, non-orphaned context. Falling back to bootstrapping a fresh, unrelated
        // Reconciler+ReconcilerContext there silently detaches the fiber from the caller's registries /
        // FiberStack / IsAborted flag. Left null only by V.Mount's direct root-fiber path,
        // which has no context to join and must bootstrap its own (this fiber becomes the owner).
        // onCaughtError and motionClock are written only onto a context this call bootstraps, before the render
        // that follows it, since that first render can already throw into a boundary and start a mount enter.
        private static void SetupMount(
            ComponentFiber fiber, VisualElement? mountPoint, ReconcilerContext? sharedContext = null,
            Action<Exception, ErrorInfo>? onCaughtError = null, MotionClock? motionClock = null)
        {
            if (fiber.IsMounted)
            {
                throw new InvalidOperationException(
                    "FiberRenderer: Fiber is already mounted. Call Unmount() first.");
            }

            fiber.MountPoint = mountPoint ?? throw new ArgumentNullException(nameof(mountPoint));
            // Inline-mounted descendants share the root's ReconcilerContext so registry lookups
            // resolve to the same fiber instance regardless of which fiber's Reconciler is currently
            // running. Wrapper-mounted fibers without a
            // parent fiber (root mount path) bootstrap a fresh Reconciler that owns its ctx.
            var parentCtx = sharedContext ?? fiber.Parent?.Reconciler?.Context;
            fiber.Reconciler = parentCtx != null
                ? new Reconciler(parentCtx)
                : new Reconciler();
            fiber.IsMounted = true;

            // The batch scheduler registers its single drain callback on a tree-stable anchor: the
            // root mount element. A descendant's MountPoint may detach from the panel (route change /
            // conditional render) before the next frame, which would stop a scheduled item registered
            // on it and strand still-mounted fibers that joined the same batch. The root mount element
            // outlives every descendant in the tree, so a callback registered on it always fires.
            if (parentCtx == null)
            {
                fiber.Reconciler.Context.BatchScheduler.SetAnchor(mountPoint);
                if (onCaughtError != null) fiber.Reconciler.Context.OnCaughtError = onCaughtError;
                if (motionClock != null) fiber.Reconciler.Context.StyleAnimationScheduler.MountOn(motionClock);
            }

            // On the Unmount → Mount path that reuses the same fiber, clear IsDisposed so that setter closures
            // become active again (Mount after Dispose is unsupported: state such as LaneQueue is not restored).
            fiber.IsDisposed = false;
        }

        // Unmounts the fiber and runs child VisualElement removal and effect cleanup.
        // Returns immediately if the fiber is not mounted.
        // fiber: Fiber to unmount.
        public static void Unmount(ComponentFiber fiber)
        {
            if (!fiber.IsMounted)
            {
                return;
            }

            fiber.IsMounted = false;
            fiber.IsDirty = false;
            // Drop this fiber from any pending batch drain so a still-scheduled flush callback does not
            // run RenderAndReconcile on a torn-down fiber. FlushState also early-returns on !IsMounted,
            // but removing here keeps the pending set from retaining a dead fiber reference until drain.
            fiber.Reconciler?.Context.BatchScheduler.Remove(fiber);
            // For components whose LaneState has not been allocated (Lane never used), do not call Clear() to
            // preserve zero-allocation.
            fiber.Lanes?.Clear();
            // The lane queue cleared above is where another component's transition was waiting for its work
            // to commit, so the slots enrolled here settle now: nothing is left to commit it.
            fiber.DischargeTransitionEnrolments();
            fiber.ReleaseTransitionSlotOwnership();

            // A mid-pass unmount takes its own deferred inline baselines with it: the end-of-pass
            // drain would otherwise sweep them AFTER this teardown nulls PreviousTree, disposes the
            // slots, and severs Parent — an empty mark set that would recycle ancestor-held memoized
            // nodes still owed a comeback render. Sweeping here, while the chain is intact, keeps
            // those protections; outside a pass the queue is simply empty.
            ReclaimDeferredInlineEntries(fiber);

            // Commit-phase deletion is post-order DFS — the deepest descendant's cleanups
            // must complete before the parent's. Reorder: child VE removal + child fiber dispose
            // run first (their effect cleanups fire via FiberEffects.RunOrphanFiberEffectCleanups
            // while DI scopes / subscriptions / CTS held by this fiber are still alive), then this
            // fiber's own cleanup. While a time-sliced reconcile is pending, PreviousTree diverges
            // from the actual DOM contents; FiberElementCleaner.RemoveElement silently skips
            // out-of-range indices so only existing elements are removed. Any pending
            // PendingIndexedState is cleared at the top of this Reconcile call.
            // The committed tree, the parked baseline, and the fiber's own slot-held node roots
            // (collected BEFORE the slots are disposed below) are retired together at the bottom of
            // this method, AFTER slot disposal: with this fiber's own slots already empty, the
            // sweep's live mark reduces to ancestor-held memoized roots (nodes that flowed into
            // this tree as props and survive for their owner's comeback render), so everything this
            // fiber alone held — including a node a slot cached while it was toggled out of the
            // output — is recycled instead of stranding when the slot lists are cleared.
            var slotRoots = FiberTreeReturn.CollectSlotRootsForUnmount(fiber);
            if (fiber.IsDisposed)
            {
                // Terminal teardown (Dispose sets the flag before calling here): state slots never
                // serve a remount, so their element-in-state roots retire with everything else. This
                // must happen INSIDE Unmount, while the parent chain is still attached — the final
                // sweep's mark walks that chain, and ancestor-held nodes that flowed into this
                // fiber's state stay spared. The slots are cleared now so the sweep's own mark does
                // not re-spare its targets; disposed setters are no-ops, so nothing repopulates them.
                var stateRoots = FiberTreeReturn.CollectStateSlotRootsForDispose(fiber);
                if (stateRoots != null)
                {
                    (slotRoots ??= new System.Collections.Generic.List<VNode>()).AddRange(stateRoots);
                }
                fiber.StateSlots?.Clear();
            }
            VNode?[]? retiredTree = null;
            if (fiber.MountPoint != null && fiber.PreviousTree != null)
            {
                // Push this fiber while reconciling its tree to empty so the old-side walk looks up its
                // inline child fibers under the same parent fiber they were registered with (tree-position
                // keying). RenderAndReconcile pushes the fiber for the same reason; Unmount drives the
                // reconcile directly, so it must push here too — otherwise the child fibers are not found
                // and their emitted VEs are not removed.
                var unmountPushedOnto = PushFiber(fiber);
                try
                {
                    FiberCommitWork.ReconcileOwnRows(fiber, fiber.PreviousTree, Array.Empty<VNode>(), frameBudgetMs: 0);
                }
                finally
                {
                    PopFiber(unmountPushedOnto);
                }
            }
            // Captured unconditionally: a fiber whose MountPoint is already gone still owes its
            // committed tree's pooled parts back to the pool.
            retiredTree = fiber.PreviousTree;

            FiberEffects.CleanupAllInsertionEffects(fiber);
            FiberEffects.CleanupAllLayoutEffects(fiber);
            FiberEffects.CleanupAllEffects(fiber);
            FiberOutletScope.Release(fiber);
            fiber.DisposeBlockerSlots();
            fiber.DisposeStoreSlots();
            fiber.DisposeMemoSlots();
            fiber.DisposeMutationSlots();
            fiber.CallbackSlots?.Clear();
            fiber.MemoValueSlots?.Clear();
            fiber.ClearImperativeHandleSlots();
            fiber.RefSlots?.Clear();
            // A remount is a first render, and every hook decides that by finding its index past the end of
            // its slot list. A surviving deferred-value slot puts the index inside the list, so the
            // initialValue overload skips the branch that returns initialValue and schedules the transition,
            // and commits the previous mount's deferred-toward value instead.
            //
            // TransitionSlots deliberately survives alongside it: ReleaseTransitionSlotOwnership above
            // scrubs what an unmount must not carry over, and UseTransitionTests distinguishes an unowned
            // reused slot from a fresh one by identity — which a clear would make unaskable.
            fiber.DeferredValueSlots?.Clear();
            // Optimistic slots go for the same reason, since a remount must not fold the entries a previous
            // mount added.
            fiber.ClearOptimisticSlots();
            // ExternalRef is re-injected by the parent on re-mount, mirroring how an imperative handle
            // is cleared on unmount and re-established on the next mount.
            // The ComponentRegistry path idempotently re-invokes SetExternalRef, so this is safe.
            fiber.ExternalRef = null;
            fiber.HasCommittedHookCounts = false;

            // Scrub the detached-mount marker so a fiber re-mounted (pooled) for a normal position does not
            // inherit a prior consumer's enclosing-context snapshot. Re-set on the next detached mount
            // (Portal drain / VirtualList item render) if it is again a detached-top fiber.
            fiber.DetachedMountContext = null;

            fiber.PreviousTree = null;
            fiber.SourceNode = null;
            fiber.SourceTree = null;
            fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
            // Detach the parked baseline BEFORE the sweep — the mark treats owner.PendingOldTree as
            // live, and a still-attached reference would spare this very sweep's target.
            var parkedTree = fiber.PendingOldTree;
            fiber.PendingOldTree = null;
            FiberTreeReturn.ReturnRetiredTreesForUnmount(fiber, retiredTree, parkedTree, slotRoots);
            fiber.Reconciler?.Dispose();
            fiber.Reconciler = null;

            fiber.DisposeAsyncSlots();
            fiber.Detach();
            // The fiber itself is retained so re-mount can reuse its state slots rather than re-initialize them.
        }

        // Completely disposes the fiber. Cannot be re-mounted (state slots are not restored).
        // Idempotent: subsequent calls are no-ops.
        // fiber: Fiber to dispose.
        public static void Dispose(ComponentFiber fiber)
        {
            if (fiber.IsDisposed)
            {
                return;
            }

            // Make remaining setters / store subscriptions / async OnCompleted into no-ops.
            // Closures captured by Hooks.UseXxx are gated through this flag.
            fiber.IsDisposed = true;

            Unmount(fiber);
#if UNITY_EDITOR
            DevTools.VelvetDevToolsRegistry.Unregister(fiber);
#endif
            // Defensively force false against the early-return path of Unmount() (when !IsMounted).
            fiber.IsDirty = false;
            // Drop the reference entirely and leave it to GC (avoids unnecessary allocation by EnsureLanes()
            // through proxy setters).
            fiber.Lanes = null;
            fiber.MountPoint = null;
        }

        #endregion

        #region Render

        // Schedules a re-render for context value changes via the Lane queue (FiberUpdatePriority.Normal).
        // Invoked from FiberTreeTraversal.NotifyContextChanged via
        // ComponentFiber.RequestRenderForContextHandler.
        // Commit timing is unified with other hook-driven updates (UseState / UseStore / Suspense boundary swap),
        // so the affected fiber re-renders in the next schedule cycle rather than synchronously.
        // Returns immediately if the fiber is unmounted.
        // fiber: Fiber whose subscribed context value changed.
        public static void RequestRenderForContext(ComponentFiber fiber)
        {
            FiberWorkLoop.RequestRenderFromHook(fiber);
        }

        // A boundary reconciling its own output catches a render error below it inside that reconcile, as the walk
        // catches one below a boundary it expands, and shows its fallback once the reconcile has returned
        // (FiberErrorBoundary.ShowCaughtFallback). Where it catches on the aborting path instead — an element
        // callback's error — inside another component's pass, a VirtualList row mounting during that pass, the
        // abort it raises belongs to this reconcile alone: the
        // enclosing pass goes on, as it does around a boundary caught in the walk. The flag is what tells that
        // abort from one an ancestor raised, which must stand: RenderAndReconcile clears it ahead of the body, so
        // only a fallback this render swapped in has set it. A deferred render keeps its abort even so: it swaps
        // one in only in the drain of a parked pass ahead of this call, into rows the walk around it is diffing,
        // and that walk must stop.
        private static void ReconcileRenderedTree(
            ComponentFiber fiber, VNode?[] oldTree, VNode?[] newTree, double frameBudgetMs, bool deferReconcile)
        {
            var passContext = fiber.Reconciler?.Context;
            var caught = FiberCommitWork.ReconcileIntoSlotRange(fiber, oldTree, newTree, frameBudgetMs, deferReconcile);
            if (caught != null) FiberErrorBoundary.ShowCaughtFallback(fiber, caught);
            if (!deferReconcile && fiber.FallbackReplacedPreviousTree) passContext!.IsAborted = false;
        }

        // Returns whether this render's output became the fiber's PreviousTree.
        internal static bool RenderAndReconcile(ComponentFiber fiber, double frameBudgetMs = 0, bool deferReconcile = false)
        {
            if (fiber.IsRendering)
            {
                throw new InvalidOperationException(
                    "FiberRenderer: Do not invoke UseState setters etc. from within Render().");
            }

            // Hooks.cs (the function-component path) resolves the fiber from FiberAmbientStack.Current and uses
            // Fiber.IsRendering to determine the Render() context, so set it here.
            fiber.IsRendering = true;
            FiberAmbientStack.Push(fiber);
            var retainedOutput = false;
#if UNITY_EDITOR
            // Body output committed by this render, captured for the post-commit double-invoke diagnostic pass.
            // Set only on the success path where the reconciler retained the tree, so the diagnostic never
            // runs against an aborted / discarded output.
            VNode?[]? diagnosticCommittedTree = null;
#endif
            VNode?[]? prevPendingOldTree = null;
            VNode?[]? oldTree = null;
            // Committed baseline of the passive-effect list: entries staged by EARLIER renders and
            // not yet flushed (the flush is deferred to the frame boundary) must survive an
            // exception thrown by THIS render — see the catch block.
            var committedPendingEffectCount = fiber.PendingEffects?.Count ?? 0;
            var pushedOnto = PushFiber(fiber);
            // Spine-rewalk-with-bailout: an isolated re-render (a standalone entry, not a
            // nested expansion render) starts with an empty live context cursor, so reconstruct the
            // enclosing Providers from the committed spine before Render() reads them via UseContext. A
            // nested expansion render (SharedReconcileDepth > 0) already has the cursor populated by the
            // ongoing walk, and a root fiber (Parent == null) pushes its own Providers during its reconcile.
            var reconstructSpine = fiber.Parent != null
                && (fiber.Reconciler?.Context.SharedReconcileDepth ?? 1) == 0;
            var contextSpine = reconstructSpine ? FiberContextSpine.Push(fiber) : default;
            try
            {
                using var _ = s_renderMarker.Auto();

                // Clear once before the render-phase loop. The committed pending-layout length is
                // captured just below; each discarded attempt is truncated back to it, and the settled
                // attempt re-collects exactly the layout effects it renders. UseLayoutEffect runs
                // synchronously in the layout-effect commit right after this returns, so the list is empty again
                // before the next RenderAndReconcile.
                fiber.PendingLayoutEffects?.Clear();
                fiber.PendingInsertionEffects?.Clear();
                // Cleared here rather than where it is read, so a fallback swapped over this fiber's tree
                // before this render began is not read as this one's.
                fiber.FallbackReplacedPreviousTree = false;

                var rendered = FiberBeginWork.RunRenderPhaseLoop(fiber);

                // Before the settle promotes any staged deps: a render refused for fewer hooks must leave
                // every slot's committed deps where the last committed render left them.
                HookCountSentinel.ValidateAndCommit(fiber);

                FiberBeginWork.CommitSettledHookDeps(fiber);

                var newTree = FiberErrorBoundary.OutputOf(fiber, FiberTreeReturn.NormalizeToArray(rendered));
                oldTree = fiber.PreviousTree ?? Array.Empty<VNode>();

                if (fiber.Reconciler?.HasPendingWork == true)
                {
                    // This resume runs outside the bracket FiberWorkLoop.ContinueReconcile puts around its
                    // own, so it stamps the children it reaches against whatever array the caller left
                    // current rather than against this fiber's. The ReconcileIntoSlotRange below supersedes
                    // those stamps for every child its own diff reaches, and the two must stay in this
                    // order — ComponentFiber.SourceTree owns what a stamp left from the wrong pass costs.
                    FiberCommitWork.DrainPendingWork(fiber);
                }

                prevPendingOldTree = fiber.PendingOldTree;
                fiber.PendingOldTree = null;
                if (prevPendingOldTree != null)
                {
                    // The parked baseline is no longer read by a paused pass once captured here (the
                    // force-drain above completed any remaining work), so retirement sweeps elsewhere
                    // stop treating it as live.
                    fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
                }

                ReconcileRenderedTree(fiber, oldTree, newTree, frameBudgetMs, deferReconcile);

                // Reconciler can be nulled mid-render when a descendant disposes this fiber
                // (e.g. an ErrorBoundary unmount cascade that re-enters this fiber's owner).
                // A deleted fiber is treated as a no-op for post-render bookkeeping
                // rather than continuing to schedule work on it.
                var reconciler = fiber.Reconciler;
                // FinishTopLevelPass samples the abort flag before the ref-setup drain it ends with, so a
                // boundary catching inside that drain is not in the sample — this fiber's own included.
                if (reconciler == null || reconciler.LastTopLevelWasAborted || fiber.FallbackReplacedPreviousTree)
                {
                    // A disposed fiber must not retain its newly rendered tree (post-commit
                    // would no longer see it). Aborted reconciles are likewise discarded so the
                    // next render starts from the pre-throw PreviousTree. Only the DISCARDED output
                    // (and a superseded parked baseline) retire here — never oldTree, which IS the
                    // retained PreviousTree baseline and must keep its pooled parts.
                    FiberTreeReturn.ReturnRetiredTree(newTree, fiber);
                    FiberCommitWork.ReturnSupersededParkedBaseline(fiber, prevPendingOldTree, oldTree);
                }
                else
                {
                    // Commit the new tree BEFORE retiring the old one so the recycle sweep can mark
                    // the committed state live (a memo hit legitimately shares nodes across the two).
                    fiber.PreviousTree = newTree;
                    retainedOutput = true;
                    // A pass of this fiber that completes renders whatever an earlier one suspended on.
                    fiber.SuspendedOn = null;
                    FiberCommitWork.ReturnOldTreeAfterReconcile(fiber, reconciler, oldTree, prevPendingOldTree, deferReconcile);
#if UNITY_EDITOR
                    // The double-invoke diagnostic compares this fiber's own body output against a re-render of
                    // the same body, so it is independent of reconcile / commit timing. Inline mounts
                    // (deferReconcile) commit their leaves later via the parent expansion, but that expansion
                    // reads only fiber.PreviousTree and never re-invokes the body or the hook cursor — so the
                    // diagnostic is safe to run here for inline mounts too. Under the wrapper-less architecture
                    // inline mount is the common case, so gating it out would disable the diagnostic for nearly
                    // every component.
                    diagnosticCommittedTree = newTree;
#endif
                }
                fiber.RenderCount++;
            }
            catch (Exception ex)
            {
                FiberCommitWork.ReturnSupersededParkedBaseline(fiber, prevPendingOldTree, oldTree);
                if (fiber.PendingOldTree != null)
                {
                    fiber.Reconciler?.Context.ParkedBaselineFibers.Remove(fiber);
                    // Detach before retiring: the sweep's own mark treats owner.PendingOldTree as
                    // live, and a still-attached reference would spare this very sweep's target.
                    var abortedBaseline = fiber.PendingOldTree;
                    fiber.PendingOldTree = null;
                    FiberTreeReturn.ReturnRetiredTree(abortedBaseline, fiber);
                }
                fiber.PendingLayoutEffects?.Clear();
                fiber.PendingInsertionEffects?.Clear();
                // Unlike the two lists above (rebuilt every render and flushed synchronously),
                // PendingEffects intentionally persists across renders until the deferred flush
                // runs it — and the settled deps of an already-committed entry were promoted at its
                // commit, so no later successful render would ever re-stage a wiped stable-deps
                // mount effect. Truncate this render's additions only, mirroring the render-phase
                // retry discipline, instead of discarding earlier commits' still-pending work.
                FiberHookCommit.TruncateTo(fiber.PendingEffects, committedPendingEffectCount);
                // A boundary above caught a render below this one, inside a walk this render encloses: a drain
                // of this fiber's parked work, or a VirtualList item mounting.
                if (ex is BoundaryCaughtSignal) throw;
                if (ex is FiberSuspendSignal)
                {
                    if (fiber.Parent != null)
                    {
                        // Delegate to the parent reconcile's SuspenseNode handling. The finally's PopFiber runs.
                        throw;
                    }
                    SuspendWithoutBoundary(fiber);
                    // MUTANT_SURVIVES(equivalent): the one caller reading the result never renders a parentless fiber.
                    // That caller is NotifyAsyncResourceCompleted, which renders only below a boundary found from the parent.
                    return false;
                }
                FiberErrorBoundary.OnRenderError(fiber, ex);
            }
            finally
            {
                // Pop the spine Providers re-pushed for this isolated render, restoring the cursor.
                // Runs after Render + Reconcile (descendants re-rendered during the expansion needed the
                // spine as their base) and is a no-op for nested / root renders (default handle).
                contextSpine.Unwind();
                // A throw/suspend exits with this render's context reads still staged; drop them so
                // the committed dependency list stays exactly as the last successful render left it
                // (no-op on the success path, where CommitSettledHookDeps already swapped).
                fiber.DiscardStagedDependencies();
                PopFiber(pushedOnto);
                fiber.IsRendering = false;
                // Any setState arriving after IsRendering clears belongs to the regular next-frame
                // schedule, so the render-phase flag must not leak into the next render loop. The
                // counter is reset here (not only on the settle / limit paths) so that exiting the
                // loop via an exception or suspend signal cannot carry a stale count into the next
                // render of a surviving fiber, which would otherwise trip the limit spuriously.
                fiber.HasRenderPhaseUpdate = false;
                fiber.RenderPhaseSetStateCounter = 0;
                FiberAmbientStack.Pop();
            }

#if UNITY_EDITOR
            // Editor-only double-invoke check: runs strictly AFTER the commit above
            // completes, so an impure render that throws on its second invocation cannot abort the
            // valid first commit. The committed tree is captured only on the success path; a null tree
            // means the render threw / aborted / deferred and no diagnostic is owed.
            if (diagnosticCommittedTree != null)
            {
                DoubleInvokeRenderForStrictMode(fiber, diagnosticCommittedTree);
            }
#endif
            return retainedOutput;
        }

        // Sweeps and removes this fiber's own entries from the context's deferred inline-baseline
        // queue — see the Unmount call site for why this must run before teardown empties the
        // fiber's mark roots.
        private static void ReclaimDeferredInlineEntries(ComponentFiber fiber)
        {
            var queue = fiber.Reconciler?.Context.DeferredInlineOldTreeReturns;
            if (queue == null || queue.Count == 0) return;
            for (var i = queue.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(queue[i].Owner, fiber)) continue;
                var tree = queue[i].Tree;
                queue.RemoveAt(i);
                FiberTreeReturn.ReturnRetiredTree(tree, fiber);
            }
        }

        // Pushes this fiber onto the Reconciler-side FiberStack. When a new Component is created during
        // Reconcile, ComponentRegistry uses FiberStack.Current as the parent to AppendChild.
        // Returns the stack pushed onto, or null when there was none; PopFiber takes it back. The stack is
        // handed back rather than re-read from the fiber at the pop because a boundary catching the fiber's
        // own failure disposes it in between, which nulls its Reconciler, and the push would then outlive it.
        internal static FiberStack? PushFiber(ComponentFiber fiber)
        {
            var stack = fiber.Reconciler?.Context.FiberStack;
            stack?.Push(fiber);
            return stack;
        }

        internal static void PopFiber(FiberStack? pushedOnto) => pushedOnto?.Pop();

#if UNITY_EDITOR
        // Renders fiber's body a second time as a throwaway diagnostic when
        // FiberStrictMode.Enabled is set, reusing the hook state of the committed pass (the
        // positional hook slots persist on the fiber, so a cursor reset re-reads the same values). Compares the
        // structural signature of the committed tree and the diagnostic tree, logging an error on divergence to
        // surface an impure render.
        // Runs strictly after the committed render finishes (commit done, ComponentFiber.IsRendering
        // already false). The diagnostic pass is fully isolated from the commit:
        // ComponentFiber.IsStrictDiagnosticPass makes effect registration and externally
        // visible hook writes (e.g. UseImperativeHandle.Set) no-ops, so the committed effect factory and parent
        // refs are untouched.
        // The diagnostic tree is never reconciled; it is recursively returned to the VNode pool here.
        // A throw during the diagnostic render is caught and logged only — the valid commit stands.
        // This pass writes no hook-count baseline; a hook call past one throws into the catch below.
        // A render-phase setState observed during the diagnostic is logged as impure and
        // ComponentFiber.HasRenderPhaseUpdate is cleared so it cannot leak into the next render.
        private static void DoubleInvokeRenderForStrictMode(ComponentFiber fiber, VNode?[] committedTree)
        {
            if (!FiberStrictMode.Enabled || fiber.IsDisposed || fiber.Reconciler == null) return;
            // A boundary showing the fallback it caught committed that fallback rather than its body's output.
            if (fiber.CaughtError != null) return;
            // The mount root's Body is a constant passthrough closure over the caller-built tree
            // (V.Mount wires `() => tree` — the only CreateRoot caller), so a second invocation
            // returns the SAME node graph the commit owns: there is no render purity to validate,
            // and recycling the diagnostic output would wipe the committed tree's props/events in
            // place and alias them into the shared pools. Nested component fibers still run the
            // diagnostic individually, which is where the real coverage lives.
            if (fiber.Parent == null) return;

            var committedSignature = FiberStrictMode.ComputeSignature(committedTree);

            fiber.IsStrictDiagnosticPass = true;
            fiber.IsRendering = true;
            FiberAmbientStack.Push(fiber);
            var pushedOnto = PushFiber(fiber);
            // Match the main render's spine reconstruction so the diagnostic reads the same live context.
            // The main render unwound its spine in RenderAndReconcile's finally; without re-pushing it the
            // consumer would read the context default here and report a spurious "impure render" divergence.
            // A nested diagnostic (depth > 0) already has the cursor populated by the enclosing expansion.
            var reconstructSpine = fiber.Parent != null
                && (fiber.Reconciler?.Context.SharedReconcileDepth ?? 1) == 0;
            var contextSpine = reconstructSpine ? FiberContextSpine.Push(fiber) : default;
            VNode?[]? diagnosticTree = null;
            try
            {
                FiberBeginWork.ResetHookIndex(fiber);
                // The diagnostic's context reads are staged and later discarded, so the committed
                // dependency list (matching the real committed render) stays intact.
                fiber.BeginDependencyStaging();
                fiber.HasRenderPhaseUpdate = false;
                // The diagnostic re-runs the BODY through the same render-phase window the real
                // render-phase loop uses — the impure-setState check below reads the flag it sets.
                diagnosticTree = FiberTreeReturn.NormalizeToArray(FiberBeginWork.InvokeBodyInRenderPhase(fiber));

                var diagnosticSignature = FiberStrictMode.ComputeSignature(diagnosticTree);
                if (!string.Equals(committedSignature, diagnosticSignature, StringComparison.Ordinal))
                {
                    FiberLogger.LogError("StrictMode",
                        $"FiberRenderer: {Hooks.ComponentName(fiber)} produced different output across a double render." +
                        " The render body is impure — it must be a pure function of props / state / context." +
                        $" First: {committedSignature} Second: {diagnosticSignature}");
                }
                else if (fiber.HasRenderPhaseUpdate)
                {
                    FiberLogger.LogError("StrictMode",
                        $"FiberRenderer: {Hooks.ComponentName(fiber)} scheduled a render-phase update during the" +
                        " StrictMode double render. A hook setter is being called unconditionally during Render().");
                }
            }
            catch (FiberSuspendSignal)
            {
                // The committed render suspended (showed fallback); re-reading the still-pending resource on
                // the diagnostic render re-raises the signal. This is the normal Suspense protocol, not an
                // impurity — skip the comparison silently.
            }
            catch (Exception ex)
            {
                // A render that succeeded once but throws on its second invocation is non-deterministic.
                // Report it without disturbing the already-committed tree; the diagnostic must never crash
                // a fiber that renders correctly in a player build.
                FiberLogger.LogError("StrictMode",
                    $"FiberRenderer: {Hooks.ComponentName(fiber)} threw on its second (diagnostic) render but" +
                    " succeeded on the first. The render body is non-deterministic.");
                FiberLogger.LogException("StrictMode", ex);
            }
            finally
            {
                // The diagnostic tree is discarded (never reconciled), so its pooled props / events /
                // child arrays have no later retirement point — recycle it here to avoid a pool drain.
                // The owner mark is essential: a memo hit during the diagnostic render returns the
                // COMMITTED cached subtree, so parts of this discarded tree are the live tree.
                FiberTreeReturn.ReturnRetiredTree(diagnosticTree, fiber);
                contextSpine.Unwind();
                // Discard the diagnostic's staged context reads; the committed list stays as the
                // real render left it.
                fiber.DiscardStagedDependencies();
                PopFiber(pushedOnto);
                FiberAmbientStack.Pop();
                fiber.IsRendering = false;
                fiber.IsStrictDiagnosticPass = false;
                fiber.HasRenderPhaseUpdate = false;
                fiber.RenderPhaseSetStateCounter = 0;
            }
        }
#endif

        #endregion

        #region FiberAsyncResource resolve commit path

        // A suspend that reaches the fiber whose pass it is, with no walk above that pass for a Suspense
        // expansion to catch it in. The boundary above renders again with this fiber dirty, so its walk renders
        // this fiber's update again where that expansion catches the suspend: a memoized fiber left clean
        // would bail there on equal props, and the update it holds would never render. Searched from the
        // parent: a boundary this fiber renders has already caught whatever suspended inside it.
        internal static void SuspendPassOwner(ComponentFiber fiber)
        {
            var boundary = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber.Parent!);
            if (boundary == null)
            {
                SuspendWithoutBoundary(fiber);
                return;
            }
            FiberWorkLoop.RequestRenderFromHook(fiber);
            boundary.InvalidateMemoCache();
            FiberWorkLoop.RequestRenderFromHook(boundary);
        }

        private static void SuspendWithoutBoundary(ComponentFiber fiber)
        {
            fiber.SuspendedOn = fiber.Reconciler!.Context.SuspendingReader;
            FiberLogger.LogWarning("Suspense",
                $"FiberRenderer: {Hooks.ComponentName(fiber)} suspended with no Suspense boundary above it;" +
                " it renders again when the resource resolves. Wrap with V.Suspense().");
        }

        // Every pass on the walk that suspended on this fiber's read is retried, not only the outermost: an inner
        // one the outer retry would bail on, memoized with equal props, would otherwise never render what it
        // suspended on. The outer retry subsumes the inner ones it reaches, since they are then dirty.
        private static void RetrySuspendedPasses(ComponentFiber fiber)
        {
            for (var current = fiber; current != null; current = current.Parent)
            {
                if (!ReferenceEquals(current.SuspendedOn, fiber))
                {
                    continue;
                }
                current.SuspendedOn = null;
                FiberWorkLoop.RequestRenderFromHook(current);
            }
        }

        // Commit path called by Hooks.Use (Suspense in function components) when an FiberAsyncResource resolves.
        // Uses a partial Lane scheme: step 1 (child sync RenderAndReconcile) + step 2 (boundary swap goes
        // through the Lane queue).
        // The notification is silently ignored if the fiber is disposed or not mounted.
        // fiber: Fiber that owns the FiberAsyncResource slot which just resolved.
        public static void NotifyAsyncResourceCompleted(ComponentFiber fiber)
        {
            if (fiber.IsDisposed || !fiber.IsMounted) return;
            // Re-entrancy guard: if async resolves synchronously during render, defer to schedule.
            if (fiber.IsRendering)
            {
                fiber.MountPoint?.schedule.Execute(
                    () => PanelSchedulerCallback.Run(fiber.MountPoint, fiber, NotifyAsyncResourceCompleted));
                return;
            }
            // Searched from the parent, as React takes the nearest Suspense above the component that suspended: a
            // boundary this fiber renders itself wraps its output, never its own read.
            var boundary = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber.Parent!);
            if (boundary == null)
            {
                // What React retries is the work that suspended on this read; with none waiting on it, nothing
                // renders, and a pass that suspended on another read takes this value up when it is retried.
                RetrySuspendedPasses(fiber);
                return;
            }
            // Settle the child's subtree to its resolved output. The child's host slot is currently occupied by
            // the fallback, so render WITHOUT committing (deferReconcile): the boundary's re-render below commits
            // the fallback→children reveal in one pass: a resolved resource schedules the boundary itself, not the
            // child. This single render handles all three resolve outcomes: a resolved child settles its
            // PreviousTree for the boundary to reuse; a faulted child's Use<T> throws a real exception that routes
            // to the error boundary via OnRenderError; a still-pending child re-throws FiberSuspendSignal and keeps
            // the fallback.
            // Read before the render, which can dispose this fiber when an error boundary above it catches.
            var context = fiber.Reconciler!.Context;
            var catchesBeforeTheRender = context.NextCaughtErrorSequence;
            // The render below is settled as a subsuming one is (SubsumeFiberIntoThisPass), or the boundary's re-walk
            // finds the fiber still dirty and renders it a second time for work this render already did. Two fibers
            // keep the unsettled path. A wrapper-mounted one is excluded conservatively. One holding a transition's
            // work would have it discharged ahead of a render that can suspend again, which can clear isPending
            // while the content it waits on is still off screen.
            var settles = fiber.IsInlineMounted && fiber.EnrolledTransitionSlots is not { Count: > 0 };
            if (settles) fiber.OpenSubsumedRenderWindow();
            bool retainedOutput;
            try
            {
                retainedOutput = RenderAndReconcile(fiber, deferReconcile: true);
            }
            catch (FiberSuspendSignal)
            {
                return;
            }
            // An error boundary that caught the render above has shown its fallback for this error already, and a
            // retry that renders the faulted child again has it caught and reported a second time.
            if (context.NextCaughtErrorSequence == catchesBeforeTheRender)
            {
                // Invalidate the (possibly memoized) boundary so its re-render re-walks the now-resolved
                // children instead of bailing out, then schedule it on the Normal lane to commit the reveal.
                boundary!.InvalidateMemoCache();
                FiberWorkLoop.RequestRenderFromHook(boundary);
                // A render whose output was not retained satisfied nothing.
                if (settles && retainedOutput) SettleSubsumedFiber(fiber);
            }
            FiberEffects.CommitStrandedLayoutWork(context);
        }

        #endregion
    }
}
