using System;
using System.Collections.Generic;

using Unity.Profiling;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// VNode diffing engine.
    /// Compares the old and new VNode arrays and updates the VisualElement tree with minimal mutations.
    /// </summary>
    internal sealed class Reconciler : IDisposable, IReconcilerBridge, IReconcilerHost
    {
        private static readonly ProfilerMarker s_reconcileMarker = new("Velvet.Reconcile");
        private static readonly ProfilerMarker s_continueReconcileMarker = new("Velvet.ContinueReconcile");

        private readonly ReconcilerContext _ctx;
        private readonly ChildReconciler _childReconciler;
        private readonly FiberNodeFactory _factory;
        private readonly FiberNodePatcher _patcher;
        private readonly FiberElementCleaner _cleaner;

        /// <summary>
        /// True when this Reconciler created the <see cref="_ctx"/> itself (root Reconciler).
        /// False when the ctx was inherited from a parent fiber's Reconciler (inline-mounted
        /// descendants share the root's registries / stacks so cross-fiber lookups in
        /// <see cref="ComponentRegistry"/> resolve to the same fiber instance across renders).
        /// Only the owner disposes the ctx; non-owners only dispose their own Reconciler-local
        /// resources (e.g. pending time-sliced state).
        /// </summary>
        private readonly bool _ownsContext;

        /// <summary>
        /// Whether the most recent top-level Reconcile was aborted by an Error Boundary fallback.
        /// The caller (the render path of <c>FiberRenderer</c>) reads this immediately after Reconcile
        /// completes to decide whether <c>_previousTree</c> may be overwritten with the new tree.
        /// On abort, <c>TryShowFallback</c> has already updated <c>_previousTree</c> to the fallback tree,
        /// so overwriting it with newTree would diverge the fallback from the DOM.
        /// </summary>
        internal bool LastTopLevelWasAborted { get; private set; }

        internal ReconcilerContext Context => _ctx;

        public Reconciler()
            : this(new ReconcilerContext(), ownsContext: true)
        {
        }

        /// <summary>
        /// Inline-mounted descendants share the root Reconciler's <see cref="ReconcilerContext"/>
        /// so that <see cref="ComponentRegistry"/> lookups by <c>(anchor, slotKey, identity)</c>
        /// resolve to the same fiber instance regardless of which fiber's Reconciler is currently
        /// running. Each fiber still has its own Reconciler (and ChildReconciler) so per-fiber
        /// time-sliced pause/resume state is independent, but the registries / stacks
        /// (ComponentRegistry / FiberStack / ComponentContextStack / BufferPool / element-keyed
        /// side maps) are unified at the root.
        /// </summary>
        internal Reconciler(ReconcilerContext sharedContext)
            : this(sharedContext ?? throw new ArgumentNullException(nameof(sharedContext)), ownsContext: false)
        {
        }

        private Reconciler(ReconcilerContext ctx, bool ownsContext)
        {
            _ctx = ctx;
            _ownsContext = ownsContext;
            _cleaner = new FiberElementCleaner(_ctx);
            _patcher = new FiberNodePatcher(_ctx);
            _factory = new FiberNodeFactory(_ctx, _patcher);
            _childReconciler = new ChildReconciler(_ctx, _patcher, _factory, _cleaner);

            _factory.SetHost(this);
            _patcher.SetHost(this);
            _cleaner.SetHost(this);
            // The root Reconciler owns the bridge so internal elements (FiberVirtualListController etc.)
            // can re-enter the reconciler via _ctx.ReconcilerBridge. Inherited contexts already have a
            // bridge assigned; skipping the set avoids the fail-fast double-call check.
            if (ownsContext)
            {
                _ctx.SetReconcilerBridge(this);
                // Lets the batch scheduler block a synchronous discrete-event flush while any reconcile
                // pass (including a time-sliced resume that runs outside the batch Drain) is on the stack.
                _ctx.BatchScheduler.SetReconcileActiveProbe(() => _ctx.SharedReconcileDepth > 0);
                // React flushes pending passive effects before a new discrete-event update; wire the scheduler
                // to drain them at that boundary so an effect from a prior commit runs before the next render.
                _ctx.BatchScheduler.SetPassiveEffectFlush(() => FiberEffects.FlushPendingPassiveEffects(_ctx));
                // Brackets each drain to drive (1) the UseStore cross-tier tearing guard — pinning is active only
                // inside a drain, the immediate drain opens a fresh wave (reset = true) and the delayed drain
                // reuses it (reset = false) — and (2) the layout-effect commit phase: fibers defer their effect
                // commit during the drain so all renders precede any layout effect, flushed at drain end
                // (before the wave ends, so a layout effect still reads the pinned snapshot).
                _ctx.BatchScheduler.SetStoreSnapshotWaveCallbacks(
                    reset =>
                    {
                        _ctx.BeginStoreSnapshotWave(reset);
                        _ctx.DeferDrainLayoutEffects = true;
                    },
                    () =>
                    {
                        // End the snapshot wave even if a deferred layout effect throws, so the tearing guard is
                        // not left permanently active.
                        try
                        {
                            FiberEffects.FlushDeferredDrainLayoutEffects(_ctx);
                        }
                        finally
                        {
                            _ctx.EndStoreSnapshotWave();
                        }
                    });
            }
        }

        /// <summary>
        /// Whether frame-budget-controlled work remains pending.
        /// When true, call <see cref="ContinueReconcile"/> to resume.
        /// True when either the Indexed or Keyed path was suspended.
        /// </summary>
        public bool HasPendingWork
            => _childReconciler.PendingIndexedState.HasValue
               || _childReconciler.PendingKeyedState != null;

        /// <summary>
        /// Diff-updates the direct children of <paramref name="parent"/> within the slot range
        /// <c>[slotStart, slotStart + newChildren.Length)</c>. The default <paramref name="slotStart"/>
        /// of 0 reconciles the entire children list and is the canonical entry from V.Mount and
        /// IReconcilerHost.ReconcileChildren.
        /// </summary>
        /// <param name="parent">Parent element to update.</param>
        /// <param name="oldChildren">Previous VNode array.</param>
        /// <param name="newChildren">New VNode array.</param>
        /// <param name="frameBudgetMs">
        /// Frame budget in milliseconds. Zero or below disables budgeting (fully synchronous).
        /// When the budget is exceeded, work is suspended and <see cref="HasPendingWork"/> becomes true.
        /// </param>
        /// <param name="slotStart">
        /// Zero-based offset into <c>parent.children</c> at which this reconciliation operates. The
        /// virtual mount range is <c>[slotStart, slotStart + newChildren.Length)</c>, leaving slots
        /// outside this range untouched. Used by wrapper-less Component / Provider / Outlet fibers
        /// that share a parent VE with sibling slots.
        /// </param>
        public void Reconcile(VisualElement? parent, VNode?[] oldChildren, VNode?[] newChildren,
            double frameBudgetMs = 0, int slotStart = 0, int slotLimit = int.MaxValue)
        {
            if (_ctx.IsDisposed) { return; }

            // Shared depth across all Reconciler instances that observe this context. Each fiber
            // owns its own Reconciler (per-fiber pause/resume independence), but the context-keyed
            // EffectiveKeys registry only flushes when the outermost pass across the whole fiber
            // tree completes — instance-local depth would treat a child fiber's RenderAndReconcile
            // as a fresh top-level and clear entries sibling subtrees still need to consume.
            var isTopLevel = _ctx.SharedReconcileDepth == 0;

            ProfilerMarker.AutoScope profilerScope = default;
            if (isTopLevel)
            {
                profilerScope = s_reconcileMarker.Auto();
                // Stage pooled-object releases for the span of the pass so nothing retired mid-pass
                // (a boundary fallback swap, a mid-pass unmount) can be re-rented by a later factory
                // call in the SAME pass — see VNodePool's staging region.
                VNodePool.BeginReleaseScope();
            }

            _ctx.SharedReconcileDepth++;
            try
            {
                _childReconciler.Reconcile(parent, oldChildren, newChildren, frameBudgetMs, slotStart, slotLimit);
            }
            finally
            {
                _ctx.SharedReconcileDepth--;
                if (isTopLevel)
                {
                    LastTopLevelWasAborted = _ctx.IsAborted;
                    // The abort flag stops sibling work inside the pass that just ended; the deferred
                    // host mounts below are commit work for placeholders that SURVIVED it. A boundary
                    // rollback detaches its failed subtree's placeholders (the drain's per-entry
                    // liveness check skips those), while the fallback the boundary swapped in may have
                    // enqueued live portals of its own in this same pass — so the flag is consumed
                    // here rather than used to discard the queue wholesale, and the drain's nested
                    // reconciles run unimpeded.
                    _ctx.IsAborted = false;
                    try
                    {
                        _childReconciler.DrainPendingPortalMounts();
                    }
                    finally
                    {
                        try
                        {
                            // Always clear the queue at top-level boundary so partially drained passes do
                            // not leak placeholders into the next reconcile; an abort raised DURING the
                            // drain (a boundary inside a portal's children) is consumed at this boundary
                            // the same way.
                            _ctx.PendingPortalMounts.Clear();
                            _ctx.IsAborted = false;
                            // Declaring-resolution misses are scoped to one top-level pass: retrying the
                            // scan next pass is what lets a late-arriving declaring panel resolve.
                            _ctx.DeclaringResolveMisses.Clear();
                            // EffectiveKeys is scoped to one top-level pass. VNode references are fresh
                            // per render, so unconsumed entries would otherwise accumulate across renders.
                            _ctx.EffectiveKeys.Clear();
                            // Return the inline children's old trees (queued by RenderInlineForExpansion) to the
                            // VNode pool now that the whole pass is done using them as patch baselines — deferred
                            // to here to avoid a mid-pass use-after-return that duplicates re-expanded subtrees.
                            FiberTreeReturn.DrainDeferredInlineOldTreeReturns(_ctx);
                        }
                        finally
                        {
                            // Flush releases staged during the pass (AFTER the drain above, which stages
                            // more). Guaranteed even if the drain throws (its mark pass can execute a user
                            // IReadOnlyList via the slot probes): a skipped flush would wedge the pool in
                            // staging mode for the process lifetime — every later return staged, never
                            // flushed, pools starving — with no recovery outside the editor's domain reload.
                            VNodePool.EndReleaseScope();
                            profilerScope.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when an Error Boundary switches to its fallback.
        /// Signal to abort Reconcile of the remaining sibling nodes in the same batch.
        /// </summary>
        internal void SetAborted() => _ctx.IsAborted = true;

        /// <summary>
        /// Resumes time-sliced reconciliation from a suspended state.
        /// Call this when <see cref="HasPendingWork"/> is true.
        /// </summary>
        /// <remarks>
        /// Returns immediately (no-op) when <see cref="HasPendingWork"/> is false.
        /// Verifying <see cref="HasPendingWork"/> beforehand is recommended.
        /// </remarks>
        /// <param name="frameBudgetMs">
        /// Frame budget in milliseconds. Because <c>ReconcileIndexedFrom</c> internally restarts the
        /// <c>Stopwatch</c>, this acts as a "new budget" rather than "remaining budget."
        /// Zero or below disables budgeting (the remaining work runs synchronously to completion).
        /// </param>
        public void ContinueReconcile(double frameBudgetMs = 0)
        {
            if (_ctx.IsDisposed || !HasPendingWork) { return; }
            using var _ = s_continueReconcileMarker.Auto();

            // A resume continues the same pass an earlier time-slice suspended; it is NOT a fresh
            // top-level entry, so it must not run the top-level reset (abort / EffectiveKeys /
            // portal drain) that Reconcile's isTopLevel finally performs — clearing those mid-pass
            // would discard state sibling subtrees still need. That reset block lives only in
            // Reconcile, so incrementing the shared depth here can never trigger it. The bracket
            // exists so the reconcile-active probe (FiberBatchScheduler.FlushImmediate) reports
            // SharedReconcileDepth > 0 while a resume is on the stack: a resume is scheduled via
            // schedule.Execute, not through the batch Drain, so _draining is false during it, and
            // without the probe a discrete event dispatched synchronously inside a slice would
            // re-enter the reconciler on the stack.
            _ctx.SharedReconcileDepth++;
            // A resume slice rents and returns pooled objects like the original pass, so it gets the
            // same release staging bracket (nested entries are depth-counted inside VNodePool).
            VNodePool.BeginReleaseScope();
            try
            {
                // Indexed and Keyed never become pending simultaneously (the other is cleared when a new
                // Reconcile starts), so resuming whichever path is pending is sufficient.
                if (_childReconciler.PendingIndexedState.HasValue)
                {
                    _childReconciler.ContinueIndexed(frameBudgetMs);
                }
                else
                {
                    _childReconciler.ContinueKeyed(frameBudgetMs);
                }
            }
            finally
            {
                VNodePool.EndReleaseScope();
                _ctx.SharedReconcileDepth--;
            }
        }

        /// <summary>
        /// Re-bases the captured slot offset of an in-flight time-sliced reconcile by <paramref name="delta"/>.
        /// </summary>
        /// <remarks>
        /// Called when a preceding sibling fiber that shares this fiber's parent VE re-renders with a
        /// child-count delta while this fiber's reconcile is suspended (<see cref="HasPendingWork"/> is
        /// true). The sibling's insert / remove physically shifts this fiber's already-committed rows
        /// within the shared parent, so the suspended <c>slotStart</c> — a captured absolute offset into
        /// the parent's children — must move by the same delta. Without it the resume would write the
        /// remaining rows at stale absolute indices, corrupting both the sibling's and this fiber's slots.
        /// No-op when no work is pending or <paramref name="delta"/> is zero.
        /// </remarks>
        internal void RebasePendingSlotStart(int delta)
        {
            if (_ctx.IsDisposed || !HasPendingWork) { return; }
            _childReconciler.RebasePendingSlotStart(delta);
        }

        #region IReconcilerBridge

        VisualElement IReconcilerBridge.CreateElementForController(VNode node) => _factory.CreateElement(node);

        void IReconcilerBridge.CleanupElementForController(VisualElement element) => _cleaner.CleanupElement(element);

        VisualElement IReconcilerBridge.PatchNodeForController(VisualElement element, VNode oldNode, VNode newNode)
        {
            // The controller stores the slot's top-level element, which for a shadow-*/clip-path-*
            // item root is the structural WRAPPER; patch the real inner (the ChildReconciler does
            // the same via ResolveWrapped before every PatchNode).
            var inner = _patcher.ResolveWrapped(element);
            _patcher.PatchNode(inner, oldNode, newNode);
            // A class change can wrap/unwrap during the patch, swapping the slot's top-level
            // element; return the current outermost so the controller re-mounts the right node
            // (the parallel of ChildReconciler's post-patch re-fetch).
            return _patcher.ResolveOuter(inner);
        }

        object? IReconcilerBridge.BeginDetachedItemScope(
            ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext)
        {
            var stack = _ctx.ComponentContextStack;
            if (enclosingContext != null)
            {
                for (var i = 0; i < enclosingContext.Count; i++)
                {
                    stack.PushRaw(enclosingContext[i].Key, enclosingContext[i].Value);
                }
            }
            if (host == null) return null;
            // Item fibers created during the scope AppendChild onto FiberStack.Current; making it the host
            // links them under the host (so they share the host's ReconcilerContext / context cursor) and
            // lets the spine's parent-walk reach the host on an isolated re-render.
            _ctx.FiberStack.Push(host);
            // Snapshot the host's current children so End can identify the item fibers added by this scope.
            // A fresh set per call supports nested VirtualLists (an item that itself renders a VirtualList).
            var before = new HashSet<ComponentFiber>();
            for (var f = host.Child; f != null; f = f.Sibling)
            {
                before.Add(f);
            }
            return before;
        }

        void IReconcilerBridge.StampDetachedItemFibers(
            ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext, VNode itemVnode, object? scopeToken)
        {
            if (host == null || scopeToken is not HashSet<ComponentFiber> seen) return;
            // Stamp each fiber newly added under host since the prior call (set diff via Add). DescendantNodes is
            // THIS item's rendered vnode so the spine can re-push a Provider the renderer placed above the item's
            // consumer; anchor is the host the item fibers parent under (the registry-lookup parent the walk
            // matches the consumer against). seen also marks them so End's fallback does not re-stamp them.
            DetachedMountContext? detached = null;
            for (var f = host.Child; f != null; f = f.Sibling)
            {
                if (!seen.Add(f)) continue;
                detached ??= new DetachedMountContext(
                    enclosingContext, itemVnode != null ? new[] { itemVnode } : null, host);
                f.DetachedMountContext = detached;
            }
        }

        void IReconcilerBridge.EndDetachedItemScope(
            ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext, object? scopeToken)
        {
            if (host != null)
            {
                _ctx.FiberStack.Pop();
                if (scopeToken is HashSet<ComponentFiber> before)
                {
                    // Fallback for any item fiber not stamped per-item (StampDetachedItemFibers marks the ones it
                    // handled in `before`): only the enclosing snapshot is replayed, with no item-vnode walk.
                    DetachedMountContext? detached = null;
                    for (var f = host.Child; f != null; f = f.Sibling)
                    {
                        if (before.Contains(f)) continue;
                        detached ??= new DetachedMountContext(enclosingContext, descendantNodes: null, anchor: host);
                        f.DetachedMountContext = detached;
                    }
                }
            }
            var stack = _ctx.ComponentContextStack;
            if (enclosingContext != null)
            {
                for (var i = enclosingContext.Count - 1; i >= 0; i--)
                {
                    stack.PopRaw(enclosingContext[i].Key);
                }
            }
        }

        #endregion

        #region IReconcilerHost

        VisualElement IReconcilerHost.CreateElement(VNode node) => _factory.CreateElement(node);

        List<(string key, VNode node)> IReconcilerHost.BuildKeyedMapCopy(VNode?[] children) => _factory.BuildKeyedMapCopy(children);

        void IReconcilerHost.RemoveElement(VisualElement parent, int index) => _cleaner.RemoveElement(parent, index);

        void IReconcilerHost.RemoveElementDirect(VisualElement parent, VisualElement element) => _cleaner.RemoveElementDirect(parent, element);

        void IReconcilerHost.ReconcileChildren(VisualElement parent, VNode?[] oldChildren, VNode?[] newChildren, int slotStart)
            => _childReconciler.Reconcile(parent, oldChildren, newChildren, slotStart: slotStart);

        void IReconcilerHost.NotifyContextValueChange(ContextProviderNode newProvider)
            => _childReconciler.NotifyContextValueChange(newProvider);

        #endregion

        public void Dispose()
        {
            // Per-Reconciler pending state always needs releasing — even non-owners may have
            // suspended their own keyed run while sharing the root's registries.
            _childReconciler.DiscardPendingKeyedState();

            // Owner-only teardown: ctx-level state is the root's responsibility. Disposing it from
            // a child Reconciler would invalidate sibling fibers that still share the same ctx.
            if (!_ownsContext) return;

            _ctx.MarkDisposed();
            _ctx.BatchScheduler.Clear();
            _ctx.StyleAnimationScheduler.CancelAll();
            _ctx.EventManager.Clear();
            _ctx.ComponentRegistry.Dispose();
            _ctx.FiberMemoCache.DisposeAndReturnCachedTrees();
            _ctx.WrapperToInnerMap.Clear();
            // Detach every still-installed callback ref: an element the unmount reconcile skipped (a
            // parked time-sliced baseline diverged from the DOM, an aborted teardown) never passes
            // through the element cleaner, and the ref contract is that every attached ref detaches
            // when its root goes away — Ref<T>.Current must not keep pointing at a dead element and a
            // user cleanup releasing external resources must run. Best-effort per entry: one throwing
            // cleanup must not strand the rest.
            foreach (var (_, installedRef) in _ctx.RefCallbacks)
            {
                try
                {
                    installedRef.Cleanup?.Invoke();
                }
                catch (System.Exception ex)
                {
                    FiberLogger.LogException("Reconciler", ex);
                }
            }
            _ctx.RefCallbacks.Clear();
            // Shadowed elements hold paint + re-bake callbacks: detach so a still-mounted element released at
            // root disposal carries no Velvet residue. Then dispose the shared bake Material once (the baked
            // shadow textures are cached process-wide and outlive the reconciler, like the gradient bakes).
            foreach (var (element, binding) in _ctx.ShadowBindings)
            {
                DropShadowSilhouette.Detach(element, binding);
            }
            _ctx.ShadowBindings.Clear();
            DropShadowBaker.DisposeMaterial();
            // Clip wrappers hold a baked VectorImage (a ScriptableObject): still-mounted clipped
            // elements never pass through FiberElementCleaner at root disposal, so release here —
            // symmetric with the ShadowBindings Material teardown above.
            foreach (var binding in _ctx.ClipPathBindings.Values)
            {
                binding.DisposeImage();
            }
            _ctx.ClipPathBindings.Clear();
            // Ring overlays are plain native-border elements (no GPU resource), so just drop the entries; the
            // wrappers leave with their subtrees at disposal.
            _ctx.RingBindings.Clear();
            // Skewed elements hold paint/stash callbacks and an inline color suppression: detach so
            // a still-mounted element released at root disposal carries no Velvet residue.
            foreach (var (element, binding) in _ctx.SkewBindings)
            {
                SkewSilhouette.Detach(element, binding);
            }
            _ctx.SkewBindings.Clear();
            // border-dashed / border-dotted elements hold a paint/stash callback and a border-color
            // suppression: detach so a still-mounted element released at root disposal carries no residue.
            foreach (var (element, binding) in _ctx.BorderStyleBindings)
            {
                BorderStyleSilhouette.Detach(element, binding);
            }
            _ctx.BorderStyleBindings.Clear();
            // Dashed / dotted divider children hold a paint callback (not a style property, so unscrubbed by
            // the pool reset): detach each so a still-mounted divider at root disposal leaves no live delegate.
            foreach (var (element, binding) in _ctx.DivideDashBindings)
            {
                DivideDashPainter.Detach(element, binding);
            }
            _ctx.DivideDashBindings.Clear();
            // Gradient elements hold an inline background-image referencing a shared baked texture: clear
            // the inline image so a still-mounted element released at root disposal carries no residue
            // (the cached textures themselves are shared and outlive the reconciler).
            foreach (var (element, _) in _ctx.GradientBackgrounds)
            {
                GradientBackground.Clear(element);
            }
            _ctx.GradientBackgrounds.Clear();
            // animate-* motions hold a recurring scheduled tick: pause each (and restore the styles it drove)
            // so a still-mounted animated element released at root disposal stops ticking and carries no residue.
            foreach (var (element, binding) in _ctx.AnimationBindings)
            {
                StyleAnimateDriver.Detach(element, binding);
            }
            _ctx.AnimationBindings.Clear();
            // filter-* transitions hold a one-shot scheduled tick: pause + unregister each so a still-mounted
            // element released at root disposal stops ticking any in-flight filter tween.
            foreach (var (element, binding) in _ctx.FilterTransitionBindings)
            {
                StyleFilterTransitionDriver.Detach(element, binding);
            }
            _ctx.FilterTransitionBindings.Clear();
            // SceneView bindings own a live RenderTexture with a camera rendering into it: release both
            // ends so a still-mounted scene view at root disposal leaves no orphaned texture and no
            // camera left targeting one.
            foreach (var (element, binding) in _ctx.SceneViewBindings)
            {
                SceneViewDriver.Detach(element, binding);
            }
            _ctx.SceneViewBindings.Clear();
            // Particles bindings own a live GameObject (the hidden simulation host): destroy each so a
            // still-mounted particles element at root disposal leaves no orphaned system simulating.
            foreach (var (element, binding) in _ctx.ParticlesBindings)
            {
                ParticlesDriver.Detach(element, binding);
            }
            _ctx.ParticlesBindings.Clear();
            // Anchored bindings own a recurring projection tick: pause each so a still-mounted anchored
            // element at root disposal leaves nothing still ticking.
            foreach (var (element, binding) in _ctx.AnchoredBindings)
            {
                AnchoredDriver.Detach(element, binding);
            }
            _ctx.AnchoredBindings.Clear();
            // Focus-scope bindings own a registered attach callback each; the navigator's listener trios
            // (and any still-pending attach hooks) sit on elements that outlive this reconciler, so both
            // are released explicitly.
            foreach (var (element, binding) in _ctx.FocusScopeBindings)
            {
                FocusScopeDriver.Detach(element, binding);
            }
            _ctx.FocusScopeBindings.Clear();
            _ctx.ChainedPlaceholders.Clear();
            _ctx.ChainedHostRoots.Clear();
            FiberFocusNavigator.DetachAll(_ctx);
            // Drag-and-drop: an active session scrubs first (it holds pointer capture, inline styles and
            // classes on elements that outlive this reconciler), then the registries release their
            // bindings (the draggable armer and the overlay's forced inline state are the two that own
            // registered/forced element state).
            // Inline (not deferred) user cancel: a deferred item would fire against the disposed tree,
            // or never fire when the panel dies with it.
            _ctx.ActiveDrag?.CancelForTeardown(deferUserCallback: false);
            foreach (var (element, binding) in _ctx.DraggableBindings)
            {
                DndDraggableDriver.Detach(element, binding, _ctx);
            }
            _ctx.DraggableBindings.Clear();
            foreach (var (element, _) in _ctx.DragOverlayBindings)
            {
                DndOverlayDriver.Detach(element, _ctx);
            }
            _ctx.DragOverlayBindings.Clear();
            _ctx.DndScopeBindings.Clear();
            _ctx.DroppableBindings.Clear();
            foreach (var (element, manipulator) in _ctx.GestureManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.GestureManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.VariantManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.VariantManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.ConditionalVariantManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.ConditionalVariantManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.RelationalVariantManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.RelationalVariantManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.HasVariantManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.HasVariantManipulators.Clear();
            // Empty every pure side-table (structural / has-[.class]: / data-/aria- rules + store / supports-)
            // in one call, mirroring the per-element ClearElementSideTables used on cleanup.
            _ctx.ClearAllSideTables();
            foreach (var kv in _ctx.StackedVariantManipulators)
            {
                kv.Key.target.RemoveManipulator(kv.Value);
            }

            _ctx.StackedVariantManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.GapManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.GapManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.DivideManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.DivideManipulators.Clear();
            foreach (var (element, manipulator) in _ctx.GridManipulators)
            {
                element.RemoveManipulator(manipulator);
            }

            _ctx.GridManipulators.Clear();
            _ctx.PortalState.Clear();
            _ctx.PendingPortalMounts.Clear();
            // Layer and world-space hosts are framework-owned GameObjects with runtime-created
            // panel assets: destroy them so a disposed tree leaves no hidden panels behind. The
            // unmount reconcile already removed their children; a still-live layer host with zero
            // children (layers persist across child removals) dies here too. World-space hosts are
            // normally destroyed per placeholder by the cleaner — this sweep catches any still
            // live at root disposal.
            foreach (var record in _ctx.LayerHosts.Values)
            {
                PanelHostFactory.Destroy(record);
            }
            _ctx.LayerHosts.Clear();
            foreach (var record in _ctx.WorldSpaceBindings.Values)
            {
                PanelHostFactory.Destroy(record);
            }
            _ctx.WorldSpaceBindings.Clear();
            foreach (var controller in _ctx.VirtualListControllers.Values)
            {
                controller.Dispose();
            }

            _ctx.VirtualListControllers.Clear();
            // Outlet route scopes are user-supplied DI scopes (IRouteScope extends IDisposable):
            // dispose each so an Outlet still mounted at whole-reconciler teardown does not leak the
            // scope's resources, mirroring FiberElementCleaner's per-element Outlet-scope-dispose.
            foreach (var scope in _ctx.OutletScopes.Values)
            {
                scope.Dispose();
            }

            _ctx.OutletScopes.Clear();
            _ctx.OutletContainers.Clear();
            _ctx.PresenceStates.Clear();
        }
    }
}
