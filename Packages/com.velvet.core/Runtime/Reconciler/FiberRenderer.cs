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
    // the render phase (body invocation, render-phase loop, hook-count validation) lives in
    // FiberBeginWork, and the commit phase (host-tree application + inline-slot geometry) in FiberCommitWork.
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
        public static void Mount(ComponentFiber fiber, VisualElement? mountPoint)
        {
            SetupMount(fiber, mountPoint);
            RenderAndReconcile(fiber);
            FiberEffects.CommitSubtreeEffects(fiber, mountDoubleInvoke: true);
        }

        // Inline-mount variant for wrapper-less fibers. The fiber's render output is held on the
        // fiber's ComponentFiber.PreviousTree for the caller (typically
        // ChildReconciler.ExpandInlineRecursive) to incorporate into the parent expansion;
        // no Reconcile is issued from the fiber itself. The caller is responsible for placing the
        // output VEs into parent.children at slotStart.
        // Subsequent setState-triggered re-renders use the fiber's own Reconciler with slot-range
        // addressing (the deferReconcile flag is only honored on this initial mount path).
        public static void MountInline(ComponentFiber fiber, VisualElement? parent, int slotStart)
        {
            SetupMount(fiber, parent);
            fiber.IsInlineMounted = true;
            fiber.MountSlotStart = slotStart;
            RenderAndReconcile(fiber, deferReconcile: true);
            // Layout effects setup runs AFTER the DOM mutations + ref attach are committed for
            // the entire subtree. Inline-mount
            // defers child CreateElement / InvokeRefCallback to the parent expansion which
            // happens AFTER MountInline returns, so running LayoutEffects here would observe
            // stale (null) refs. Push the fiber onto the deferred stack and let the top-level
            // reconcile entry drain it (LIFO = bottom-up) before its own RunLayoutEffects so the
            // root commits last.
            fiber.Reconciler!.Context.DeferredInlineLayoutEffectFibers.Push((fiber, IsMount: true));
            FiberEffects.ScheduleRunEffects(fiber, mountDoubleInvoke: true);
        }

        // Synchronously re-renders an already-mounted inline fiber so the caller (parent
        // expansion) sees the fresh ComponentFiber.PreviousTree. Mirrors
        // MountInline but skips SetupMount (already initialized). Effects
        // are pushed onto the shared deferred drain so the top-level reconcile entry runs them
        // bottom-up with the rest of the subtree: a deps-changed effect must run its
        // prior cleanup + new setup during the layout-effect commit; staged effects would
        // otherwise be cleared by the next RenderAndReconcile without running).
        public static void RenderInlineForExpansion(ComponentFiber fiber)
        {
            if (!fiber.IsMounted)
            {
                throw new InvalidOperationException(
                    "FiberRenderer.RenderInlineForExpansion: fiber must already be mounted.");
            }
            RenderAndReconcile(fiber, deferReconcile: true);
            // Update commit: drain side runs prior cleanup + new setup (deps-comparing) without
            // the Editor-only mount double-invoke. ScheduleRunEffects forwards the same flag so the
            // passive (UseEffect) cleanup+setup pair fires at the next paint-tick.
            fiber.Reconciler!.Context.DeferredInlineLayoutEffectFibers.Push((fiber, IsMount: false));
            FiberEffects.ScheduleRunEffects(fiber, mountDoubleInvoke: false);
        }

        // Settles a fiber that an ancestor's flush just subsumed by re-rendering it inline (via
        // RenderInlineForExpansion) in the same batch pass. Every pending
        // update on a component coalesces into the single top-down render: the subsuming render already ran with the
        // fiber's latest state, so all of its pending lanes are satisfied at once. This clears the whole lane
        // queue (not just the highest lane), clears the dirty flag and transition-pending slots, and drops the
        // fiber from BOTH batch-scheduler tiers so a later drain does not independently re-process it — without
        // which a higher-priority lane queued on the child (e.g. Transition on the delayed tier) would be
        // stranded and silently dropped by FlushState's not-dirty early-return.
        internal static void SettleSubsumedFiber(ComponentFiber fiber)
        {
            fiber.LaneQueue?.Clear();
            fiber.IsDirty = false;
            fiber.ClearAllTransitionPending();
            fiber.Reconciler?.Context.BatchScheduler.Remove(fiber);
        }

        private static void SetupMount(ComponentFiber fiber, VisualElement? mountPoint)
        {
            if (fiber.IsMounted)
            {
                throw new InvalidOperationException(
                    "FiberRenderer: Fiber is already mounted. Call Unmount() first.");
            }

            fiber.MountPoint = mountPoint ?? throw new ArgumentNullException(nameof(mountPoint));
            // Inline-mounted descendants share the root's ReconcilerContext so registry lookups
            // by (anchor, slotKey, identity) resolve to the same fiber instance regardless of
            // which fiber's Reconciler is currently running. Wrapper-mounted fibers without a
            // parent fiber (root mount path) bootstrap a fresh Reconciler that owns its ctx.
            var parentCtx = fiber.Parent?.Reconciler?.Context;
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
            // preserve zero-allocation. LaneState.Clear initializes all four queue/transition-related fields.
            fiber.Lanes?.Clear();

            // Commit-phase deletion is post-order DFS — the deepest descendant's cleanups
            // must complete before the parent's. Reorder: child VE removal + child fiber dispose
            // run first (their effect cleanups fire via FiberEffects.RunOrphanFiberEffectCleanups
            // while DI scopes / subscriptions / CTS held by this fiber are still alive), then this
            // fiber's own cleanup. While a time-sliced reconcile is pending, PreviousTree diverges
            // from the actual DOM contents; FiberElementCleaner.RemoveElement silently skips
            // out-of-range indices so only existing elements are removed. Any pending
            // PendingIndexedState is cleared at the top of this Reconcile call.
            if (fiber.MountPoint != null && fiber.PreviousTree != null)
            {
                // Push this fiber while reconciling its tree to empty so the old-side walk looks up its
                // inline child fibers under the same parent fiber they were registered with (tree-position
                // keying). RenderAndReconcile pushes the fiber for the same reason; Unmount drives the
                // reconcile directly, so it must push here too — otherwise the child fibers are not found
                // and their emitted VEs are not removed.
                var unmountPushed = PushFiber(fiber);
                try
                {
                    fiber.Reconciler!.Reconcile(fiber.MountPoint, fiber.PreviousTree, Array.Empty<VNode>());
                }
                finally
                {
                    PopFiber(fiber, unmountPushed);
                }
                FiberTreeReturn.ReturnPooledObjects(fiber.PreviousTree);
            }

            FiberEffects.CleanupAllInsertionEffects(fiber);
            FiberEffects.CleanupAllLayoutEffects(fiber);
            FiberEffects.CleanupAllEffects(fiber);
            fiber.DisposeBlockerSlots();
            fiber.DisposeStoreSlots();
            fiber.DisposeMemoSlots();
            fiber.DisposeMutationSlots();
            fiber.CallbackSlots?.Clear();
            fiber.MemoValueSlots?.Clear();
            fiber.ClearImperativeHandleSlots();
            fiber.RefSlots?.Clear();
            // ExternalRef is re-injected by the parent on re-mount, mirroring how an imperative handle
            // is cleared on unmount and re-established on the next mount.
            // The ComponentRegistry path idempotently re-invokes SetExternalRef, so this is safe.
            fiber.ExternalRef = null;
#if UNITY_EDITOR
            fiber.ResetEditorHookCountBaselines();
#endif
            fiber.PrevStateHookCountRuntime = -1;
            fiber.PrevStoreHookCountRuntime = -1;
            fiber.PrevAsyncHookCountRuntime = -1;

            // Scrub the detached-mount marker so a fiber re-mounted (pooled) for a normal position does not
            // inherit a prior consumer's enclosing-context snapshot. Re-set on the next detached mount
            // (Portal drain / VirtualList item render) if it is again a detached-top fiber.
            fiber.DetachedMountContext = null;

            fiber.PreviousTree = null;
            FiberTreeReturn.ReturnPooledObjects(fiber.PendingOldTree);
            fiber.PendingOldTree = null;
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

        internal static void RenderAndReconcile(ComponentFiber fiber, double frameBudgetMs = 0, bool deferReconcile = false)
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
#if UNITY_EDITOR
            var renderSucceeded = false;
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
            var fiberPushed = PushFiber(fiber);
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
                // synchronously in RunLayoutEffects right after this returns, so the list is empty again
                // before the next RenderAndReconcile.
                fiber.PendingLayoutEffects?.Clear();
                fiber.PendingInsertionEffects?.Clear();

                var rendered = FiberBeginWork.RunRenderPhaseLoop(fiber);

                FiberBeginWork.CommitSettledHookDeps(fiber);

                FiberBeginWork.ValidateRuntimeHookCounts(fiber);

#if UNITY_EDITOR
                FiberBeginWork.ValidateEditorHookCounts(fiber);
                renderSucceeded = true;
#endif
                var newTree = FiberTreeReturn.NormalizeToArray(rendered);
                oldTree = fiber.PreviousTree ?? Array.Empty<VNode>();

                if (fiber.Reconciler?.HasPendingWork == true)
                {
                    FiberCommitWork.DrainPendingWork(fiber);
                }

                prevPendingOldTree = fiber.PendingOldTree;
                fiber.PendingOldTree = null;

                FiberCommitWork.ReconcileIntoSlotRange(fiber, oldTree, newTree, frameBudgetMs, deferReconcile);

                // Reconciler can be nulled mid-render when a descendant disposes this fiber
                // (e.g. an ErrorBoundary unmount cascade that re-enters this fiber's owner).
                // A deleted fiber is treated as a no-op for post-render bookkeeping
                // rather than continuing to schedule work on it.
                var reconciler = fiber.Reconciler;
                FiberCommitWork.ReturnOldTreeAfterReconcile(fiber, reconciler, oldTree, prevPendingOldTree, deferReconcile);

                if (reconciler == null || reconciler.LastTopLevelWasAborted)
                {
                    // A disposed fiber must not retain its newly rendered tree (post-commit
                    // would no longer see it). Aborted reconciles are likewise discarded so the
                    // next render starts from the pre-throw PreviousTree.
                    FiberTreeReturn.ReturnPooledObjects(newTree);
                }
                else
                {
                    fiber.PreviousTree = newTree;
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
                if (prevPendingOldTree != null && prevPendingOldTree != oldTree)
                {
                    FiberTreeReturn.ReturnPooledObjects(prevPendingOldTree);
                }
                if (fiber.PendingOldTree != null)
                {
                    FiberTreeReturn.ReturnPooledObjects(fiber.PendingOldTree);
                    fiber.PendingOldTree = null;
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
                if (ex is FiberSuspendSignal)
                {
                    if (fiber.Parent != null)
                    {
                        // Delegate to the parent reconcile's SuspenseNode handling. The finally's PopFiber runs.
                        throw;
                    }
                    FiberLogger.LogWarning("Suspense",
                        "FiberRenderer: FiberSuspendSignal propagated without finding a Suspense boundary." +
                        " Wrap with V.Suspense().");
                    return;
                }
                FiberErrorBoundary.OnRenderError(fiber, ex);
            }
            finally
            {
#if UNITY_EDITOR
                if (!renderSucceeded)
                {
                    fiber.ResetEditorHookCountBaselines();
                }
#endif
                // Pop the spine Providers re-pushed for this isolated render, restoring the cursor.
                // Runs after Render + Reconcile (descendants re-rendered during the expansion needed the
                // spine as their base) and is a no-op for nested / root renders (default handle).
                contextSpine.Unwind();
                // A throw/suspend exits with this render's context reads still staged; drop them so
                // the committed dependency list stays exactly as the last successful render left it
                // (no-op on the success path, where CommitSettledHookDeps already swapped).
                fiber.DiscardStagedDependencies();
                PopFiber(fiber, fiberPushed);
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
        }

        // Pushes this fiber onto the Reconciler-side FiberStack. When a new Component is created during
        // Reconcile, ComponentRegistry uses FiberStack.Current as the parent to AppendChild.
        // True if successfully pushed (a corresponding Pop is required).
        internal static bool PushFiber(ComponentFiber fiber)
        {
            if (fiber.Reconciler == null) return false;
            fiber.Reconciler.Context.FiberStack.Push(fiber);
            return true;
        }

        internal static void PopFiber(ComponentFiber fiber, bool wasPushed)
        {
            if (wasPushed && fiber.Reconciler != null)
            {
                fiber.Reconciler.Context.FiberStack.Pop();
            }
        }

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
        // The hook-count sentinel and its baselines are not touched by this pass.
        // A render-phase setState observed during the diagnostic is logged as impure and
        // ComponentFiber.HasRenderPhaseUpdate is cleared so it cannot leak into the next render.
        private static void DoubleInvokeRenderForStrictMode(ComponentFiber fiber, VNode?[] committedTree)
        {
            if (!FiberStrictMode.Enabled || fiber.IsDisposed || fiber.Reconciler == null) return;

            var committedSignature = FiberStrictMode.ComputeSignature(committedTree);

            fiber.IsStrictDiagnosticPass = true;
            fiber.IsRendering = true;
            FiberAmbientStack.Push(fiber);
            var fiberPushed = PushFiber(fiber);
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
                diagnosticTree = FiberTreeReturn.NormalizeToArray(FiberBeginWork.Render(fiber));

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
                // The diagnostic tree is discarded (never reconciled), so its descendant pooled props / events /
                // child arrays have no owner to recycle them — return the whole tree recursively to avoid a
                // pool drain that the committed (owned) tree would not cause.
                FiberTreeReturn.ReturnPooledTreeRecursive(diagnosticTree);
                contextSpine.Unwind();
                // Discard the diagnostic's staged context reads; the committed list stays as the
                // real render left it.
                fiber.DiscardStagedDependencies();
                PopFiber(fiber, fiberPushed);
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
                fiber.MountPoint?.schedule.Execute(() => NotifyAsyncResourceCompleted(fiber));
                return;
            }
            var boundary = ComponentBoundarySearch.FindNearestSuspenseBoundary(fiber);
            var underBoundary = boundary != null && !ReferenceEquals(boundary, fiber);
            // Settle the child's subtree to its resolved output. Under a wrapper-less Suspense boundary the
            // child's host slot is currently occupied by the fallback, so render WITHOUT committing
            // (deferReconcile): the boundary's re-render below commits the fallback→children reveal in one
            // pass: a resolved resource schedules the boundary itself, not the child. This single
            // render handles all three resolve outcomes: a resolved child settles its PreviousTree for the
            // boundary to reuse; a faulted child's Use<T> throws a real exception that routes to the error
            // boundary via OnRenderError; a still-pending child re-throws FiberSuspendSignal and keeps the
            // fallback. Without a boundary (plain async) the child commits its own slot directly.
            try
            {
                RenderAndReconcile(fiber, deferReconcile: underBoundary);
            }
            catch (FiberSuspendSignal)
            {
                return;
            }
            if (!underBoundary && fiber.Reconciler?.HasPendingWork != true)
            {
                // Plain async (no Suspense boundary): the resolved child commits its own slot in the
                // call above, so its UseImperativeHandle factory must run here. Under a boundary the
                // child's commit is driven by the boundary's re-render below (Mount path = Run is
                // invoked there); skipping the call avoids committing a handle that the boundary's
                // re-render is about to discard.
                FiberHookCommit.RunImperativeHandleSlots(fiber);
            }
            if (underBoundary)
            {
                // Invalidate the (possibly memoized) boundary so its re-render re-walks the now-resolved
                // children instead of bailing out, then schedule it on the Normal lane to commit the reveal.
                boundary!.InvalidateMemoCache();
                FiberWorkLoop.RequestRenderFromHook(boundary);
            }
        }

        #endregion
    }
}
