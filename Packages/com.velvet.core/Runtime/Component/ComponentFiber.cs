using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Bitmask over the four <see cref="FiberUpdatePriority"/> values, used as the per-fiber pending-lane
    /// set. The priority space is fixed at exactly 4 members (Urgent=0 .. Transition=3), and this type sits
    /// on the scheduler's hottest path (<see cref="FiberWorkLoop.ScheduleRerender"/> /
    /// <see cref="FiberWorkLoop.FlushState"/> run on every hook-driven update), so membership is tracked with
    /// a single byte rather than a general-purpose ordered-set container: no per-enrollment node allocation,
    /// no comparer indirection.
    /// </summary>
    internal struct FiberLaneSet
    {
        private byte _mask;

        /// <summary>Number of lanes currently enrolled (0-4).</summary>
        internal readonly int Count
        {
            get
            {
                var count = 0;
                for (var bit = 0; bit < 4; bit++)
                {
                    if ((_mask & (1 << bit)) != 0)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        /// <summary>
        /// The numerically-lowest (highest-priority) enrolled lane. Undefined on an empty set by contract —
        /// every call site guards with <see cref="Count"/> or <see cref="Contains"/> before reading Min — but
        /// resolves to <c>default</c> (Urgent) rather than throwing, so a caller that skips the guard fails
        /// open into a plausible-looking value instead of an exception on the render hot path.
        /// </summary>
        internal readonly FiberUpdatePriority Min
        {
            get
            {
                for (var bit = 0; bit < 4; bit++)
                {
                    if ((_mask & (1 << bit)) != 0)
                    {
                        return (FiberUpdatePriority)bit;
                    }
                }
                return default;
            }
        }

        internal readonly bool Contains(FiberUpdatePriority priority) => (_mask & (1 << (int)priority)) != 0;

        /// <summary>
        /// Enrolls <paramref name="priority"/>. Returns true iff it was not already enrolled — callers key
        /// first-enrollment-vs-coalesced-re-add decisions (e.g. the transition-starvation clock reset in
        /// <see cref="FiberWorkLoop.ScheduleRerender"/>) off this return value.
        /// </summary>
        internal bool Add(FiberUpdatePriority priority)
        {
            var bit = (byte)(1 << (int)priority);
            if ((_mask & bit) != 0)
            {
                return false;
            }
            _mask |= bit;
            return true;
        }

        /// <summary>Un-enrolls <paramref name="priority"/>. Returns true iff it had been enrolled.</summary>
        internal bool Remove(FiberUpdatePriority priority)
        {
            var bit = (byte)(1 << (int)priority);
            if ((_mask & bit) == 0)
            {
                return false;
            }
            _mask &= (byte)~bit;
            return true;
        }

        /// <summary>
        /// Un-enrolls everything outside <paramref name="lanes"/>. A caller that satisfied what was pending
        /// cannot express that as a set difference against a before-image, because <see cref="Add"/> is
        /// idempotent: a re-enrolment of an already-pending lane leaves the mask identical, so subtracting
        /// the before-image would discard it. It states the survivors instead — see
        /// <c>FiberRenderer.SubsumeFiberIntoThisPass</c>.
        /// </summary>
        internal void RetainAll(FiberLaneSet lanes) => _mask &= lanes._mask;

        internal void Clear() => _mask = 0;
    }

    /// <summary>
    /// Lazily allocated via <see cref="ComponentFiber.EnsureLanes"/> so a fiber that never enrolls in a
    /// lane pays no allocation for it. Holds only this fiber's own pending-lane queue
    /// (<see cref="FiberLaneSet"/>) and transition state — never a per-subtree aggregate.
    /// </summary>
    internal sealed class LaneState
    {
        public FiberLaneSet Queue;
        // Which lanes an enrolment request named since the last reset, as opposed to which the queue gained.
        // The two differ exactly when a request coalesces onto a lane already pending, and that is the case a
        // subsuming render's settle has to keep — see FiberRenderer.SubsumeFiberIntoThisPass.
        public FiberLaneSet LanesRequestedSinceReset;
        public int TransitionStarvationCounter;
        // The settle sweep keys off the Transition label's presence, which starvation promotion erases
        // (relabelling the lane to Normal) while the promoted work may still be queued — e.g. parked
        // behind a co-pending Urgent drain. Without this marker the sweep would read the erased label
        // as "settled" and clear isPending before the promoted content commits.
        public bool HasPromotedTransition;

        public void Clear()
        {
            Queue.Clear();
            LanesRequestedSinceReset.Clear();
            TransitionStarvationCounter = 0;
            HasPromotedTransition = false;
        }
    }

    /// <summary>
    /// Identity that persists across re-renders for one component instance.
    /// Forms a parent/child linked-list tree via Parent / Child / Sibling pointers, and
    /// holds hook slots / context dependencies / refs / error boundary / suspense boundary.
    /// </summary>
    /// <remarks>
    /// Hook slots are aggregated on the Fiber. Render / commit / lane scheduling is driven by static method groups
    /// on <see cref="FiberRenderer"/> that take a fiber as argument (module-level functions operating on a
    /// fiber rather than instance methods).
    /// </remarks>
    public sealed class ComponentFiber
    {
        public ComponentFiber? Parent { get; private set; }
        public ComponentFiber? Child { get; private set; }
        public ComponentFiber? Sibling { get; private set; }

        /// <summary>
        /// Dispatch slot for re-render scheduling triggered by context value changes.
        /// Used by <see cref="FiberTreeTraversal.NotifyContextChanged"/> to notify consumers; the production
        /// handler queues the work on the Lane queue (<c>FiberUpdatePriority.Normal</c>) so context propagation
        /// commits at the next schedule cycle alongside other hook updates.
        /// </summary>
        /// <remarks>
        /// A cached static delegate is assigned via <see cref="FiberRenderer.CreateRoot"/>, eliminating per-fiber
        /// delegate allocation (zero-allocation design). Tests assign a mock delegate directly to verify dispatch behavior.
        /// </remarks>
        public Action<ComponentFiber>? RequestRenderForContextHandler;

        /// <summary>
        /// The propagation generation in which <see cref="RequestRenderForContextHandler"/> was last dispatched
        /// via <see cref="FiberTreeTraversal.NotifyContextChanged"/>.
        /// Sentinel for deduping double render-request of a consumer that depends on multiple keys within the same
        /// reconcile pass. The production path passes a positive generation from
        /// <see cref="ReconcilerContext.ContextPropagationGeneration"/>, so the comparison with the initial value -1
        /// (never fired) guarantees dispatch on the first invocation.
        /// Tests that do not require dedup pass through the <see cref="int.MinValue"/> sentinel without updating this field.
        /// </summary>
        internal int LastForceRenderGeneration { get; set; } = -1;

        /// <summary>
        /// Render delegate for a function component (the function reference that produces this fiber's tree).
        /// The wrapped tree from V.Mount or ComponentNode.Body via ComponentRegistry is stored here.
        /// </summary>
        internal Func<VNode>? Body { get; set; }

        /// <summary>
        /// Props value supplied by <c>V.Component&lt;TProps&gt;</c>. Re-assigned on each parent render
        /// so the closure-captured props seen by <see cref="Body"/> stay current.
        /// </summary>
        internal object? Props { get; set; }

        public bool IsErrorBoundary { get; internal set; }
        public bool IsSuspenseBoundary { get; internal set; }

        /// <summary>
        /// True while this fiber is a primary (hidden) child of a wrapper-less Suspense that is currently
        /// showing its fallback. Written by <c>GeneralPathReconciler.ExpandSuspenseInline</c> over the
        /// fibers that expansion created, less the ones a nested Suspense that suspended created: that
        /// boundary owns its own primary subtree, so an enclosing one resolving leaves it
        /// hidden. <see cref="FiberWorkLoop.FlushState"/>'s
        /// offscreen guard defers a lane flush for offscreen fibers (their slot is occupied by the
        /// fallback). It is per-fiber rather than per-boundary because one component fiber can render
        /// several Suspense nodes: that Suspense's own visible fallback subtree, and a sibling Suspense's
        /// children, sit under the same boundary and must still flush.
        /// </summary>
        internal bool IsOffscreen { get; set; }

        internal List<ContextDependency> Dependencies { get; private set; } = new();

        // Staging list for the render in progress: context reads land here and are swapped into
        // Dependencies only when the render settles, so a render that throws partway cannot leave
        // the committed list empty/partial (which would silently detach the fiber from future
        // Provider-change notifications). Swapped by reference — no per-render allocation.
        private List<ContextDependency> _stagedDependencies = new();
        private bool _isStagingDependencies;

        internal List<IFiberAsyncResource> AsyncSlots { get; } = new();
        private int _asyncSlotCursor;

        internal int AsyncSlotCursor => _asyncSlotCursor;

        internal int NextAsyncSlotIndex() => _asyncSlotCursor++;

        internal void ResetAsyncSlotCursor() => _asyncSlotCursor = 0;

        internal void DisposeAsyncSlots()
        {
            for (var i = 0; i < AsyncSlots.Count; i++)
            {
                AsyncSlots[i]?.Dispose();
            }
            AsyncSlots.Clear();
            _asyncSlotCursor = 0;
        }

        #region Hook Slots

        // Each List is lazily allocated (null treated as empty) since most components use only a subset of hooks.

        internal List<HookStateSlot>? StateSlots;
        internal List<HookStoreSlot>? StoreSlots;
        internal List<HookEffectSlot>? Effects;
        internal List<HookEffectSlot>? PendingEffects;
        internal List<HookEffectSlot>? LayoutEffects;
        internal List<HookEffectSlot>? PendingLayoutEffects;
        internal List<HookEffectSlot>? InsertionEffects;
        internal List<HookEffectSlot>? PendingInsertionEffects;
        internal List<HookCallbackSlot>? CallbackSlots;
        internal List<HookImperativeHandleSlot>? ImperativeHandleSlots;
        internal List<HookRefSlot>? RefSlots;
        internal List<HookBlockerSlot>? BlockerSlots;
        internal List<HookMemoSlot>? MemoSlots;
        internal List<HookMemoValueSlot>? MemoValueSlots;
        internal List<HookMutationSlot>? MutationSlots;
        internal List<HookIdSlot>? IdSlots;
        internal List<HookDeferredValueSlot>? DeferredValueSlots;
        internal List<HookOptimisticSlot>? OptimisticSlots;

        /// <summary>
        /// Cache field for the onCompleted callback of <see cref="Hooks.Use{T}"/> (Suspense) across re-renders,
        /// reducing GC allocations from per-render to once per fiber (zero-allocation design).
        /// </summary>
        internal Action? AsyncResourceCompletedCallback;

        /// <summary>
        /// Per-call-position transition slots for <see cref="Hooks.UseTransition"/>. Each call gets its own
        /// pending flag and a reference-stable starter, so two transitions in one component report independent
        /// <c>isPending</c> values. Lazily allocated; null treated as empty.
        /// </summary>
        internal List<HookTransitionSlot>? TransitionSlots;

        /// <summary>
        /// The transition slots — on this fiber or on any other component's — whose callback enrolled the
        /// Transition lane here, so the drain that commits this fiber's work reaches every slot waiting on it
        /// without walking the tree. The reverse of <see cref="HookTransitionSlot.EnrolledFibers"/>. Lazily
        /// allocated; null treated as empty.
        /// </summary>
        internal List<HookTransitionSlot>? EnrolledTransitionSlots;

        /// <summary>Position cursors per hook kind within one render cycle.</summary>
        internal HookIndexTable Indices;

        /// <summary>
        /// Whether the component is currently mounted (between Mount() completion and Unmount() start).
        /// Used to guard effects / state updates against an unmounted fiber.
        /// </summary>
        public bool IsMounted { get; internal set; }

        /// <summary>
        /// Flag indicating that Render() is currently executing. Referenced by HookGuard to validate
        /// the hook invocation context.
        /// </summary>
        public bool IsRendering { get; internal set; }

        /// <summary>
        /// True only while the component BODY is on the stack (a render-phase-loop attempt or the
        /// StrictMode diagnostic invocation) — a strict subset of <see cref="IsRendering"/>, which
        /// spans the whole render-and-commit flush. The distinction decides what a setter for this
        /// fiber's own state means: inside the body it is a render-phase update (discard the
        /// attempt and re-run), while later in the same flush (a callback ref invoked during the
        /// patch, an event dispatched from a detach) it schedules an ordinary follow-up render —
        /// treating commit-phase writes as render-phase ones silently discarded them, desyncing the
        /// slot value from the committed UI and poisoning the setter's equality bail.
        /// </summary>
        internal bool IsInRenderPhase;

#if UNITY_EDITOR
        /// <summary>
        /// Set only while the StrictMode diagnostic (throwaway second) render runs. Hooks consult this to
        /// suppress writes that would otherwise corrupt the already-committed render: effect registration does
        /// not overwrite the committed effect factory or re-queue pending effects, and externally visible writes
        /// such as <c>UseImperativeHandle</c>'s ref set are skipped. Idempotent reads (UseState / UseMemo /
        /// UseCallback with equal deps) are unaffected. Always cleared in the diagnostic pass's finally.
        /// </summary>
        internal bool IsStrictDiagnosticPass { get; set; }
#endif

        /// <summary>
        /// Set when a hook setter for this fiber's own state fires while this fiber is rendering
        /// (a render-phase setState on the currently rendering component). The render loop in
        /// <see cref="FiberRenderer"/> discards the in-progress output and re-runs Render() until
        /// no further render-phase update is requested, so render-phase updates are processed before
        /// the render commits.
        /// </summary>
        internal bool HasRenderPhaseUpdate { get; set; }

        /// <summary>
        /// Number of consecutive render-phase re-runs in the current render loop. Incremented on
        /// each synchronous re-run and reset once Render() settles without requesting another
        /// render-phase update. Exceeding <see cref="FiberBeginWork.RenderPhaseUpdateLimit"/> throws
        /// to surface an unconditional render-phase setState (a "too many re-renders" infinite loop).
        /// </summary>
        internal int RenderPhaseSetStateCounter { get; set; }

        /// <summary>
        /// Whether re-render is needed at the next FlushState (an update was requested via Hook setter / dispatch /
        /// store subscription). Marks the fiber dirty so it is re-rendered on the next flush.
        /// </summary>
        public bool IsDirty { get; internal set; }

        /// <summary>
        /// Whether the component is already disposed. Used as a no-op guard on paths such as calling a setter
        /// from within Render.
        /// </summary>
        public bool IsDisposed { get; internal set; }

        /// <summary>
        /// Scheduling state holding the Lane queue / Transition state.
        /// Allocated only on demand (lazy init preserves zero-allocation for most components).
        /// </summary>
        internal LaneState? Lanes { get; set; }

        internal LaneState EnsureLanes() => Lanes ??= new LaneState();

        /// <summary>
        /// Null-safe read of the pending-lane mask (used by FiberWorkLoop / FiberLane's query paths). An
        /// unallocated <see cref="Lanes"/> (fiber currently clean) reads as an empty <see cref="FiberLaneSet"/>
        /// without allocating, preserving the lazy-init invariant. This is a read-only view: FiberLaneSet's
        /// Add/Remove/Clear mutate the struct in place and a property getter can only hand back a copy of it
        /// (CS1612), so enrollment/removal call sites go through <see cref="EnsureLanes"/> / <see cref="Lanes"/>
        /// directly against the backing <see cref="LaneState.Queue"/> field instead of through this accessor.
        /// </summary>
        internal FiberLaneSet LaneQueue => Lanes?.Queue ?? default;

        /// <summary>
        /// True while any <see cref="Hooks.UseTransition"/> slot on this fiber is pending. Derived from the
        /// per-slot flags (each <c>useTransition()</c> tracks its own pending); this is the aggregate query.
        /// </summary>
        internal bool IsTransitionPending
        {
            get
            {
                if (TransitionSlots == null)
                {
                    return false;
                }
                foreach (var slot in TransitionSlots)
                {
                    if (slot.IsPending)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Records that a write made under <paramref name="slot"/>'s open transition scope enrolled the
        /// Transition lane on this fiber, so that slot stays pending past its callback until this fiber
        /// commits the write.
        /// </summary>
        internal void EnrolTransitionSlot(HookTransitionSlot slot)
        {
            slot.EnrolledFibers ??= new List<ComponentFiber>();
            if (slot.EnrolledFibers.Contains(this))
            {
                return;
            }
            slot.EnrolledFibers.Add(this);
            (EnrolledTransitionSlots ??= new List<HookTransitionSlot>()).Add(slot);
        }

        /// <summary>
        /// Releases every transition slot waiting on this fiber's Transition work and clears the pending flag
        /// on those left with nothing outstanding anywhere. Called where that work is established as
        /// committed, and from the two places it will never commit from: unmount, and the scheduler dropping
        /// a starvation-promoted lane at its update-depth cap.
        /// </summary>
        internal void DischargeTransitionEnrolments(bool requestLocalRender = false)
        {
            if (EnrolledTransitionSlots == null)
            {
                return;
            }
            foreach (var slot in EnrolledTransitionSlots)
            {
                slot.EnrolledFibers?.Remove(this);
                if (!SettleIfNothingOutstanding(slot))
                {
                    continue;
                }
                // A pre-render settle needs no separate local render because this fiber's imminent render reads
                // the clear. A post-commit settle does: its completed render read the flag while it was still lit.
                if (ReferenceEquals(slot.DeclaringFiber, this))
                {
                    if (requestLocalRender)
                    {
                        RequestRenderForClearedPending(slot);
                    }
                    continue;
                }
                // Anywhere else nothing is left to take the indicator off the screen. Where this drain does
                // reach the declaring fiber, its render subsumes the lane requested here and it retires
                // unrendered — see FiberRenderer.SettleSubsumedFiber.
                RequestRenderForClearedPending(slot);
            }
            EnrolledTransitionSlots.Clear();
        }

        /// <summary>
        /// Asks the component that declared <paramref name="slot"/> for the render that observes its now-false
        /// <c>isPending</c>. Nothing renders a component because its flag moved, so a clear reached from a path
        /// that renders nobody — a drain of some other component's tree, an async action's completion — leaves
        /// the indicator on screen without this.
        /// </summary>
        internal static void RequestRenderForClearedPending(HookTransitionSlot slot)
        {
            var declaring = slot.DeclaringFiber;
            if (declaring is not { IsMounted: true, IsDisposed: false })
            {
                return;
            }
            // Urgent inside a discrete handler, on the rule an ordinary state update made there follows: the
            // clear belongs to the interaction being serviced and commits with it. Never the Transition lane,
            // though, even where this is reached inside an open transition scope — this render is what takes
            // the indicator down, and the delayed tier would hold it up for its own delay.
            FiberWorkLoop.ScheduleRerender(
                declaring,
                FiberWorkLoop.IsInDiscreteEvent ? FiberUpdatePriority.Urgent : FiberUpdatePriority.Normal);
        }

        /// <summary>
        /// Settles this fiber's transition bookkeeping where its Transition work is established as committed:
        /// the slots that enrolled work here are released, and those of them — plus this fiber's own — left
        /// with nothing outstanding anywhere clear their pending flag.
        /// </summary>
        internal void SettleTransitionPending()
        {
            DischargeTransitionEnrolments();
            ClearSettledTransitionPending();
        }

        /// <summary>
        /// Settles transition work after its terminal reconcile slice committed, then requests the render that
        /// takes down any indicator whose last render observed it as pending.
        /// </summary>
        internal void SettleTransitionPendingAfterCommit()
        {
            DischargeTransitionEnrolments(requestLocalRender: true);
            ClearSettledTransitionPending();
        }

        /// <summary>
        /// Clears the pending flag on this fiber's own transition slots with nothing outstanding, leaving the
        /// rest lit. Used where the pending Transition lane is not evidence either way: see
        /// <c>FiberRenderer.SubsumeFiberIntoThisPass</c>, whose surviving lane may have been requested by a
        /// different hook during the render it settles.
        /// </summary>
        internal void ClearSettledTransitionPending()
        {
            if (TransitionSlots == null)
            {
                return;
            }
            foreach (var slot in TransitionSlots)
            {
                SettleIfNothingOutstanding(slot);
            }
        }

        // True when this cleared a flag that was lit, which is what a caller owing the declaring component a
        // render keys off.
        // A callback still on the stack settles at its own exit instead: it can enrol further fibers, and
        // nothing would light the flag again after a clear here. An awaiting async action is the same case
        // one await out — its updates have not been scheduled yet, so nothing outstanding does not mean
        // settled, and its completion path is what clears the flag.
        private static bool SettleIfNothingOutstanding(HookTransitionSlot slot)
        {
            if (slot.HasActiveOwner || slot.IsAsyncInFlight || slot.HasQueuedWork)
            {
                return false;
            }
            var wasPending = slot.IsPending;
            slot.IsPending = false;
            return wasPending;
        }

        /// <summary>
        /// Opens the window <c>FiberRenderer.SubsumeFiberIntoThisPass</c>'s settle reads, resetting both records it
        /// consumes: which lanes the render asks for again, and which transition slots enrolled work here.
        /// Everything queued before the window is what that render satisfies, so the settle runs ahead of the
        /// render — as it does in <c>FiberWorkLoop.FlushState</c>, whose commit render is likewise the one
        /// that observes <c>isPending</c> already false.
        /// </summary>
        /// <remarks>
        /// <see cref="Lanes"/> is left unallocated on purpose — a fiber with no <see cref="LaneState"/> has
        /// nothing pending, and <see cref="EnsureLanes"/> here would allocate one for the lane-less majority
        /// of inline re-renders.
        /// </remarks>
        internal void OpenSubsumedRenderWindow()
        {
            Lanes?.LanesRequestedSinceReset.Clear();
            DischargeTransitionEnrolments();
        }

        /// <summary>
        /// Hands every transition slot back to nobody, called from unmount. The slot list survives an unmount
        /// so a remount reuses it, and an async action that outlives the unmount cannot run its own release —
        /// so without this a remounted component's first <c>startTransition</c> would find the slot still
        /// owned and join a transition that no longer exists.
        /// </summary>
        internal void ReleaseTransitionSlotOwnership()
        {
            if (TransitionSlots == null)
            {
                return;
            }
            foreach (var slot in TransitionSlots)
            {
                slot.OwnerGeneration++;
                slot.OwnerDepth = 0;
                slot.AsyncOwnerDepth = 0;
                ClearTransitionEnrolments(slot);
                slot.IsPending = false;
            }
        }

        // Unwinds both sides of the enrolment record, so no fiber is left holding a slot that has stopped
        // waiting on it.
        private static void ClearTransitionEnrolments(HookTransitionSlot slot)
        {
            if (slot.EnrolledFibers == null)
            {
                return;
            }
            foreach (var fiber in slot.EnrolledFibers)
            {
                fiber.EnrolledTransitionSlots?.Remove(slot);
            }
            slot.EnrolledFibers.Clear();
        }

        internal int TransitionStarvationCounter
        {
            get => Lanes?.TransitionStarvationCounter ?? 0;
            set => EnsureLanes().TransitionStarvationCounter = value;
        }

        internal bool HasPromotedTransition
        {
            get => Lanes?.HasPromotedTransition ?? false;
            set => EnsureLanes().HasPromotedTransition = value;
        }

        // Per-hook-kind counts from the previous render, compared against this render's counts to enforce a
        // stable hook count (rules of hooks). -1 means no prior render, so the check is skipped on mount (the
        // dispose/recycle path resets these to -1). The *Runtime trio below is validated in player builds too;
        // the editor-only set above it drives the editor-only stable-hook-count diagnostics.
#if UNITY_EDITOR
        internal int PrevHookCount = -1;
        internal int PrevLayoutEffectHookCount = -1;
        internal int PrevInsertionEffectHookCount = -1;
        internal int PrevEffectHookCount = -1;
        internal int PrevImperativeHandleHookCount = -1;
        internal int PrevBlockerHookCount = -1;
        internal int PrevIdHookCount = -1;
        internal int PrevDeferredValueHookCount = -1;
        internal int PrevOptimisticHookCount = -1;
        internal int PrevMutationHookCount = -1;

        // Resets every editor-only per-hook-kind baseline to -1 (no prior render), so a re-mount or a
        // discarded render does not compare against stale counts and trip a false stable-hook-count diagnostic.
        // One site to update when a new hook category is added.
        internal void ResetEditorHookCountBaselines()
        {
            PrevHookCount = -1;
            PrevBlockerHookCount = -1;
            PrevLayoutEffectHookCount = -1;
            PrevInsertionEffectHookCount = -1;
            PrevEffectHookCount = -1;
            PrevImperativeHandleHookCount = -1;
            PrevIdHookCount = -1;
            PrevDeferredValueHookCount = -1;
            PrevOptimisticHookCount = -1;
            PrevMutationHookCount = -1;
        }
#endif
        internal int PrevStateHookCountRuntime = -1;
        internal int PrevStoreHookCountRuntime = -1;
        internal int PrevAsyncHookCountRuntime = -1;

        /// <summary>Cumulative count of successful renders. Used for debugging / profiling.</summary>
        public int RenderCount;

        /// <summary>
        /// The Reconciler instance responsible for reconciling this Fiber subtree. Currently per-Fiber
        /// (each component has its own Reconciler).
        /// </summary>
        internal Reconciler? Reconciler { get; set; }

        /// <summary>
        /// Set on the top-level child fiber of a detached mount (a Portal's drained children, or a
        /// VirtualList's controller-rendered items). Those subtrees mount outside the normal parent-walked
        /// reconcile, so an isolated re-render's spine parent-walk cannot reach the host's enclosing
        /// Providers. This carries the context that enclosed the detached mount so
        /// <see cref="FiberContextSpine"/> can rebuild it directly. Null for every non-detached-top fiber.
        /// </summary>
        internal DetachedMountContext? DetachedMountContext { get; set; }

        /// <summary>
        /// The VisualElement into which this fiber's rendered output is committed as children.
        /// In wrapper-mounted mode this is a dedicated wrapper VE owned by the fiber; in
        /// inline-mounted mode (<see cref="IsInlineMounted"/>=true) it is a parent VE shared with
        /// sibling fibers, with the sub-range
        /// <c>[<see cref="MountSlotStart"/>, <see cref="MountSlotStart"/> + <see cref="MountSlotCount"/>)</c>
        /// owned by this fiber.
        /// </summary>
        internal UnityEngine.UIElements.VisualElement? MountPoint { get; set; }

        /// <summary>
        /// Absolute starting index in <see cref="MountPoint"/>.children at which this fiber's
        /// rendered output begins. Always 0 in wrapper-mounted mode; non-zero for inline-mounted
        /// fibers that share <see cref="MountPoint"/> with sibling fibers.
        /// </summary>
        internal int MountSlotStart { get; set; }

        /// <summary>
        /// Number of slots in <see cref="MountPoint"/>.children currently owned by this fiber.
        /// The sentinel <c>-1</c> means "owns the entire children list" (wrapper-mounted default);
        /// non-negative values are used by inline-mounted fibers. Updated after each render when
        /// the output VNode count changes; the delta is propagated to subsequent sibling fibers by
        /// shifting their <see cref="MountSlotStart"/>.
        /// </summary>
        internal int MountSlotCount { get; set; } = -1;

        /// <summary>
        /// True when this fiber's output VEs sit directly in <see cref="MountPoint"/>.children at
        /// the <see cref="MountSlotStart"/> sub-range (shared with sibling fibers). False when a
        /// dedicated wrapper VE is used and the fiber owns the entire MountPoint's children.
        /// </summary>
        internal bool IsInlineMounted { get; set; }

        /// <summary>
        /// The Portal placeholder whose children reconcile last placed this inline fiber, or null for one
        /// placed outside any Portal. A Portal's top-level Component child mounts inline with the portal
        /// TARGET as its <see cref="MountPoint"/>, so it is a sibling of the elements a portal teardown
        /// removes rather than a descendant of any of them; this is what tells the teardown which of the
        /// several Portals sharing that target the fiber belongs to.
        /// <see cref="MountSlotStart"/> cannot answer that: a NEIGHBOURING Portal's own children changing
        /// on the same target moves this fiber's output along it without rewriting it.
        /// Rewritten on every placement rather than at creation only, so it names where the fiber renders
        /// now; a fiber the teardown must reach that no placement ever named is reached instead through the
        /// parent index (ComponentRegistry.DisposeInlineFibersOwnedByPortal owns which is which).
        /// </summary>
        internal UnityEngine.UIElements.VisualElement? OwningPortalPlaceholder { get; set; }

        /// <summary>The VNode array fixed by the previous reconcile. Serves as the "old" side for the next reconcile.</summary>
        internal VNode?[]? PreviousTree { get; set; }

        /// <summary>Reference to the previous tree retained during a pending time-sliced reconcile.</summary>
        internal VNode?[]? PendingOldTree { get; set; }

        /// <summary>
        /// Frame budget (milliseconds) chosen for the in-flight reconcile by the lane that started it. A resume
        /// (<c>FiberWorkLoop.ContinueReconcile</c>) reads it so the continuation runs at the same budget, keeping
        /// a Transition slice time-sliced across frames. 0 for synchronous lanes.
        /// </summary>
        internal double PendingReconcileBudgetMs { get; set; }

        /// <summary>
        /// Whether the lane that started the in-flight reconcile was carrying transition work, so a resume
        /// restores <c>FiberWorkLoop.IsRenderingTransitionLane</c> to the same answer. A parked slice can
        /// still evaluate component bodies: <c>GeneralPathReconciler.NeedsExpansion</c> looks one level
        /// down, so a container of host children whose own descendants are components takes the
        /// time-sliced path and expands them on resume.
        /// </summary>
        internal bool PendingReconcileDrainsTransitionWork { get; set; }

        /// <summary>Sentinel indicating whether the asynchronous effect flush has been scheduled via schedule.Execute.</summary>
        internal bool EffectFlushScheduled { get; set; }

#if UNITY_EDITOR
        /// <summary>
        /// Captured at async effect scheduling time: true only when the scheduled flush belongs to the mount
        /// commit. The async effect runs after the scheduling site returns, so this carries the mount/update
        /// distinction to <c>RunEffects</c> for the StrictMode effect double-cycle (which doubles on mount only).
        /// </summary>
        internal bool PendingEffectsAreMount { get; set; }
#endif

        /// <summary>
        /// Ref passed by the parent via <c>V.Component&lt;TRef&gt;(componentRef:)</c>. Retrieved via the
        /// <c>ForwardedRef&lt;T&gt;()</c> hook.
        /// </summary>
        internal IHookRefSetter? ExternalRef { get; set; }

        /// <summary>
        /// Fallback factory registered by a function-style Error Boundary via <see cref="Hooks.UseFallback"/>.
        /// Called from the <see cref="FiberErrorBoundary.TryCatch"/> path when a child exception is caught,
        /// returning a fallback VNode. Overwritten on each render (Hook rule: must always be called).
        /// </summary>
        internal Func<Exception, ErrorInfo, VNode>? FallbackFactory { get; set; }

        /// <summary>
        /// True while this boundary is in the middle of rendering/reconciling its own fallback UI. An
        /// exception raised by that fallback content (rather than the original throw it is responding to)
        /// re-enters <see cref="FiberErrorBoundary.TryCatch"/> for this SAME fiber via the normal
        /// per-fiber render catch; the guard makes that re-entrant call decline immediately instead of
        /// attempting to show the (already failing) fallback again, so propagation continues to the next
        /// ancestor boundary instead of recursing without bound.
        /// </summary>
        internal bool IsShowingFallback { get; set; }

        /// <summary>
        /// Set when this boundary's own fallback content throws while <see cref="IsShowingFallback"/> is
        /// true (the re-entrant <see cref="FiberErrorBoundary.TryCatch"/> call this triggers declines and
        /// records it here instead of recursing). Read once, immediately after the fallback's Reconcile
        /// call returns, to tell "the fallback rendered cleanly" apart from "the fallback's own content
        /// failed and was logged or escalated elsewhere" — both leave the Reconcile call itself returning
        /// normally, so nothing else observes the difference. Reset before each fallback attempt.
        /// </summary>
        internal bool FallbackContentFailed { get; set; }

        /// <summary>
        /// Calls <c>Set(null)</c> on every ref registered by <see cref="UseImperativeHandle"/>.
        /// Responsible for resetting the parent-side <c>Ref&lt;T&gt;.Current</c> to null.
        /// </summary>
        internal void ClearImperativeHandleSlots()
        {
            if (ImperativeHandleSlots == null) return;
            foreach (var entry in ImperativeHandleSlots)
            {
                entry.HandleRef?.Set(null);
            }
            ImperativeHandleSlots.Clear();
        }

        private static void DisposeAndClear<T>(List<T>? slots) where T : IDisposable
        {
            if (slots == null) return;
            foreach (var slot in slots)
            {
                slot?.Dispose();
            }
            slots.Clear();
        }

        /// <summary>
        /// Disposes all Store subscription slots. Called on Unmount.
        /// </summary>
        internal void DisposeStoreSlots() => DisposeAndClear(StoreSlots);

        /// <summary>
        /// Disposes all Blocker registration handles. Called on Unmount.
        /// </summary>
        internal void DisposeBlockerSlots() => DisposeAndClear(BlockerSlots);

        /// <summary>
        /// Releases all memoization slots. Called on Unmount; severs references to CachedResult (VNode) and LastDeps.
        /// </summary>
        internal void DisposeMemoSlots()
        {
            if (MemoSlots == null) return;
            MemoSlots.Clear();
        }

        /// <summary>
        /// Marks all memoization slots as stale so the next render takes the cache-miss path and rebuilds
        /// its VNode subtree. The slot list (and per-slot index) is preserved so SG / ILPP emitted code
        /// still maps slot index → slot correctly across the invalidation.
        /// </summary>
        /// <remarks>
        /// Use when a commit path needs the component's VNode subtree re-walked even though the component's
        /// own hook inputs are unchanged (the caller is responsible for triggering the re-render).
        /// </remarks>
        internal void InvalidateMemoCache()
        {
            if (MemoSlots == null) return;
            foreach (var slot in MemoSlots)
            {
                if (slot == null) continue;
                slot.LastDeps = null;
                slot.CachedResult = null;
            }
        }

        /// <summary>
        /// Cancels and disposes all mutation slots' in-flight CancellationTokenSources. Called on Unmount;
        /// prevents resolved continuations from mutating disposed fiber state.
        /// </summary>
        internal void DisposeMutationSlots() => DisposeAndClear(MutationSlots);

        #endregion

        internal void RegisterContextDependency(object context)
        {
            var target = _isStagingDependencies ? _stagedDependencies : Dependencies;
            for (var i = 0; i < target.Count; i++)
            {
                if (target[i].Context == context) return;
            }
            target.Add(new ContextDependency { Context = context });
        }

        internal bool HasDependencyOn(object context)
        {
            for (var i = 0; i < Dependencies.Count; i++)
            {
                if (Dependencies[i].Context == context) return true;
            }
            return false;
        }

        // Starts collecting this render attempt's context reads into the staging list, leaving the
        // committed Dependencies untouched until the attempt settles.
        internal void BeginDependencyStaging()
        {
            _stagedDependencies.Clear();
            _isStagingDependencies = true;
        }

        // Promotes the settled attempt's reads to the committed list by swapping the two lists.
        internal void CommitStagedDependencies()
        {
            if (!_isStagingDependencies) return;
            (Dependencies, _stagedDependencies) = (_stagedDependencies, Dependencies);
            _isStagingDependencies = false;
        }

        // Drops an unsettled attempt's reads (throw / suspend / diagnostic pass), keeping the
        // committed list exactly as the last successful render left it.
        internal void DiscardStagedDependencies()
        {
            if (!_isStagingDependencies) return;
            _isStagingDependencies = false;
            _stagedDependencies.Clear();
        }

        public object? Ref { get; set; }

        public void AppendChild(ComponentFiber child)
        {
            if (child.Parent != null && child.Parent != this)
            {
                child.Parent.RemoveChild(child);
            }

            child.Parent = this;
            child.Sibling = null;

            if (Child == null)
            {
                Child = child;
                return;
            }

            var tail = Child;
            while (tail.Sibling != null)
            {
                tail = tail.Sibling;
            }
            tail.Sibling = child;
        }

        public void RemoveChild(ComponentFiber child)
        {
            if (Child == null || child.Parent != this)
            {
                return;
            }

            if (Child == child)
            {
                Child = child.Sibling;
            }
            else
            {
                var prev = Child;
                while (prev.Sibling != null && prev.Sibling != child)
                {
                    prev = prev.Sibling;
                }
                if (prev.Sibling == child)
                {
                    prev.Sibling = child.Sibling;
                }
            }

            child.Parent = null;
            child.Sibling = null;
        }

        public void Detach()
        {
            Parent?.RemoveChild(this);
        }
    }

    /// <summary>
    /// Subscription entry pushed onto ComponentFiber.Dependencies on a UseContext call.
    /// Used to determine whether the entry matches a given context when propagating Provider value changes to consumers.
    /// </summary>
    internal sealed class ContextDependency
    {
        public object? Context { get; set; }
    }
}
