using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Auxiliary structure that represents the Lane scheduling state on a Fiber.
    /// A minimal model of the per-fiber and per-subtree pending-update lane bitsets.
    /// </summary>
    internal sealed class LaneState
    {
        public SortedSet<FiberUpdatePriority>? Queue;
        public bool IsInTransition;
        public int TransitionStarvationCounter;

        public void Clear()
        {
            Queue?.Clear();
            IsInTransition = false;
            TransitionStarvationCounter = 0;
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
        /// True while this fiber is a primary (hidden) child of a wrapper-less Suspense boundary that
        /// is currently showing its fallback. Set by <c>ChildReconciler.ExpandSuspenseInline</c> when
        /// the boundary suspends and cleared when it reveals. <see cref="FiberWorkLoop.FlushState"/>'s
        /// offscreen guard defers a lane flush for offscreen fibers (their slot is occupied by the
        /// fallback) while still allowing the visible fallback subtree to flush (the
        /// fallback renders normally; only the primary subtree is offscreen).
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
        /// A minimal model of the per-fiber and per-subtree pending-update lane bitsets.
        /// Allocated only on demand (lazy init preserves zero-allocation for most components).
        /// </summary>
        internal LaneState? Lanes { get; set; }

        internal LaneState EnsureLanes() => Lanes ??= new LaneState();

        /// <summary>Null-safe getter / lazy-init setter for Lane operations (used by FiberWorkLoop).</summary>
        internal SortedSet<FiberUpdatePriority>? LaneQueue
        {
            get => Lanes?.Queue;
            set => EnsureLanes().Queue = value;
        }

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

        /// <summary>Clears the pending flag on every transition slot (called when the transition lane settles).</summary>
        internal void ClearAllTransitionPending()
        {
            if (TransitionSlots == null)
            {
                return;
            }
            foreach (var slot in TransitionSlots)
            {
                slot.IsPending = false;
            }
        }

        internal bool IsInTransition
        {
            get => Lanes?.IsInTransition ?? false;
            set => EnsureLanes().IsInTransition = value;
        }

        internal int TransitionStarvationCounter
        {
            get => Lanes?.TransitionStarvationCounter ?? 0;
            set => EnsureLanes().TransitionStarvationCounter = value;
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
        /// owned by this fiber. The host VisualElement backing this fiber.
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

        // Disposes every slot in the list and empties it; no-op when the list was never allocated.
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
