using System;

namespace Velvet
{
    // The render phase for a function-component fiber.
    // Invokes the component body with a fresh hook cursor, runs the render-phase setState loop until the
    // output settles, then finishes the render by promoting the settled attempt's staged hook deps to the
    // committed baseline and validating the hook-call counts. Pure functions of the fiber; the orchestrator
    // (FiberRenderer.RenderAndReconcile) owns the try/catch/finally and calls these in order. The produced
    // tree is handed to FiberCommitWork for host-tree application.
    internal static class FiberBeginWork
    {
        // Upper bound on the total number of Render() attempts for a single fiber within one
        // RenderAndReconcile before throwing "Too many re-renders". Bounds runaway render-phase
        // updates; an unconditional render-phase setState would otherwise loop forever.
        internal const int RenderPhaseUpdateLimit = 25;

        // Reads state via hooks and invokes the function body that declares the VNode tree.
        // The body is stored in ComponentFiber.Body.
        internal static VNode Render(ComponentFiber fiber) => fiber.Body!();

        internal static void ResetHookIndex(ComponentFiber fiber)
        {
            // Hook slots are aggregated on the Fiber, so resetting only the Fiber-side cursor at the start of each
            // render (including each render-phase re-run) is sufficient.
            // PendingLayoutEffects is NOT cleared here: a render-phase re-run would otherwise drop a
            // mount layout effect added by an earlier attempt (a persistent slot with equal deps is
            // not re-added by HookSlotRegistrar). The clear happens once per RenderAndReconcile, before
            // the render-phase loop, so the surviving slot's factory is updated in place to the final
            // attempt's closure. PendingEffects (UseEffect / async after paint) is likewise not cleared:
            // it must be retained until RunEffects processes it at the next frame boundary.
            fiber.Indices.Reset();
            fiber.ResetAsyncSlotCursor();
        }

        // Runs the render-phase setState loop: with a fresh hook cursor each pass, invokes Render() and,
        // while a setter for this fiber's own state fired during Render() (HasRenderPhaseUpdate), discards
        // the throwaway attempt's effect registrations and re-runs until the output settles. Throws
        // "Too many re-renders" once RenderPhaseUpdateLimit is reached. Returns the settled body output.
        internal static VNode RunRenderPhaseLoop(ComponentFiber fiber)
        {
            // Committed effect-slot footprint, captured before the render-phase loop. Each effect slot's
            // LastDeps holds its committed deps and is never mutated during the loop (RegisterEffect stages
            // the current attempt's deps on NextDeps), so a discarded attempt is rolled back by truncating
            // the slot / pending lists to these lengths. A new (mount) slot appended by a discarded attempt
            // is therefore removed and re-added by the settled attempt as a fresh mount.
            var committedLayoutEffectCount = fiber.LayoutEffects?.Count ?? 0;
            var committedInsertionEffectCount = fiber.InsertionEffects?.Count ?? 0;
            var committedEffectCount = fiber.Effects?.Count ?? 0;
            var committedPendingLayoutCount = fiber.PendingLayoutEffects?.Count ?? 0;
            var committedPendingInsertionCount = fiber.PendingInsertionEffects?.Count ?? 0;
            var committedPendingEffectCount = fiber.PendingEffects?.Count ?? 0;

            // Render-phase setState loop: a setter for this fiber's own state that fires while
            // Render() runs sets HasRenderPhaseUpdate (see RequestRenderFromHook) instead of
            // scheduling a next-frame re-render. Discard the in-progress output and re-run
            // Render() (with a fresh hook cursor) until it settles, then reconcile the final
            // output once. Render-phase updates are processed before commit so a
            // derived-state normalization done during initial render lands in the same frame.
            // HasRenderPhaseUpdate is cleared just below before the loop, and RenderPhaseSetStateCounter is
            // reset in the caller's finally on every exit path, so each render begins with both already
            // cleared even after a prior exception / suspend path.
            fiber.HasRenderPhaseUpdate = false;
            VNode rendered;
            while (true)
            {
                ResetHookIndex(fiber);
                // Context reads are staged per attempt and swapped into the committed list only by
                // CommitSettledHookDeps: a render that throws partway must not leave the committed
                // list empty/partial, or the Provider-change walk would skip this fiber forever.
                fiber.BeginDependencyStaging();
                // The render-phase window opens strictly around the body: a setter firing inside it
                // is a render-phase update (this loop consumes the flag); one firing later in the
                // flush (commit phase) must schedule normally instead — see ComponentFiber.IsInRenderPhase.
                fiber.IsInRenderPhase = true;
                try
                {
                    rendered = Render(fiber);
                }
                finally
                {
                    fiber.IsInRenderPhase = false;
                }
                if (!fiber.HasRenderPhaseUpdate)
                {
                    break;
                }
                fiber.HasRenderPhaseUpdate = false;
                // Discard the effect registrations of this throwaway attempt so the next attempt
                // compares its deps against the committed render, not against this attempt.
                FiberHookCommit.DiscardRenderPhaseAttemptEffects(fiber,
                    committedLayoutEffectCount, committedInsertionEffectCount, committedEffectCount,
                    committedPendingLayoutCount, committedPendingInsertionCount, committedPendingEffectCount);
                // The throwaway attempt's tree is never reconciled, so this is its only retirement
                // point; without it every discarded attempt strands its rented bags in the pool's
                // rented-out set. The owner mark spares whatever a memo hit shared with committed
                // state or staged slots.
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(rendered), fiber);
                if (++fiber.RenderPhaseSetStateCounter >= RenderPhaseUpdateLimit)
                {
                    throw new InvalidOperationException(
                        "Too many re-renders. Velvet limits the number of renders to prevent an infinite loop." +
                        " A hook setter is being called unconditionally during Render().");
                }
            }
            return rendered;
        }

        // Settle: promote the final attempt's staged deps to the committed baseline so the next
        // render (and the effects scheduled this commit) compare against the deps that actually
        // committed, not a discarded render-phase attempt. The same applies to the other
        // deps-comparing hooks (UseCallback / UseMemo): their staged values are promoted here so a
        // discarded attempt cannot break callback referential stability or force a memo rebuild.
        internal static void CommitSettledHookDeps(ComponentFiber fiber)
        {
            // The settled attempt's context reads become the committed dependency list (list swap).
            fiber.CommitStagedDependencies();
            HookEffectExecutor.CommitEffectDeps(fiber.LayoutEffects);
            HookEffectExecutor.CommitEffectDeps(fiber.InsertionEffects);
            HookEffectExecutor.CommitEffectDeps(fiber.Effects);
            FiberHookCommit.CommitCallbackSlots(fiber.CallbackSlots);
            FiberHookCommit.CommitMemoSlots(fiber.MemoSlots);
            FiberHookCommit.CommitMemoValueSlots(fiber.MemoValueSlots);
            // UseImperativeHandle defers its entire commit (ref swap, factory invocation, parent ref write,
            // LastDeps advance) to the layout phase (RunImperativeHandleSlots) so (a) the factory observes
            // child element / handle refs attached during the same subtree commit (imperative
            // handles run in the layout phase, after the DOM mutations + ref attach are committed for the
            // entire subtree) and (b) a discarded / time-sliced render does not advance LastDeps before the factory
            // actually runs — otherwise a second render arriving in the parked window would see deps as
            // already-committed and silently skip its factory.
            FiberHookCommit.CommitBlockerSlots(fiber.BlockerSlots);
        }

        // Hook count sentinel: fail-fast at runtime against silent corruption from hook calls inside conditional branches.
        internal static void ValidateRuntimeHookCounts(ComponentFiber fiber)
        {
            var stateCount = fiber.Indices.StateHookIndex;
            var storeCount = fiber.Indices.StoreHookIndex;
            ValidateHookCountRuntime("UseState / UseReducer", fiber.PrevStateHookCountRuntime, stateCount);
            ValidateHookCountRuntime("UseStore", fiber.PrevStoreHookCountRuntime, storeCount);
            var asyncCount = fiber.AsyncSlotCursor;
            ValidateHookCountRuntime("Use", fiber.PrevAsyncHookCountRuntime, asyncCount);
            fiber.PrevStateHookCountRuntime = stateCount;
            fiber.PrevStoreHookCountRuntime = storeCount;
            fiber.PrevAsyncHookCountRuntime = asyncCount;
        }

#if UNITY_EDITOR
        // Editor-only hook-count sentinel for the deps-comparing / effect / id / value hooks, run alongside
        // ValidateRuntimeHookCounts. Advances each hook's committed call-count baseline after validating.
        internal static void ValidateEditorHookCounts(ComponentFiber fiber)
        {
            var hookCount = fiber.Indices.HookIndex;
            var blockerCount = fiber.Indices.BlockerHookIndex;
            var layoutEffectCount = fiber.Indices.LayoutEffectHookIndex;
            var insertionEffectCount = fiber.Indices.InsertionEffectHookIndex;
            var effectCount = fiber.Indices.EffectHookIndex;
            var imperativeHandleCount = fiber.Indices.ImperativeHandleHookIndex;
            var idCount = fiber.Indices.IdHookIndex;
            var deferredCount = fiber.Indices.DeferredValueHookIndex;
            var optimisticCount = fiber.Indices.OptimisticHookIndex;
            var mutationCount = fiber.Indices.MutationHookIndex;
            ValidateHookCallCount("UseCallback", fiber.PrevHookCount, hookCount);
            ValidateHookCallCount("UseBlocker", fiber.PrevBlockerHookCount, blockerCount);
            ValidateHookCallCount("UseLayoutEffect", fiber.PrevLayoutEffectHookCount, layoutEffectCount);
            ValidateHookCallCount("UseInsertionEffect", fiber.PrevInsertionEffectHookCount, insertionEffectCount);
            ValidateHookCallCount("UseEffect", fiber.PrevEffectHookCount, effectCount);
            ValidateHookCallCount("UseImperativeHandle", fiber.PrevImperativeHandleHookCount, imperativeHandleCount);
            ValidateHookCallCount("UseId", fiber.PrevIdHookCount, idCount);
            ValidateHookCallCount("UseDeferredValue", fiber.PrevDeferredValueHookCount, deferredCount);
            ValidateHookCallCount("UseOptimistic", fiber.PrevOptimisticHookCount, optimisticCount);
            ValidateHookCallCount("UseMutation", fiber.PrevMutationHookCount, mutationCount);
            fiber.PrevHookCount = hookCount;
            fiber.PrevBlockerHookCount = blockerCount;
            fiber.PrevLayoutEffectHookCount = layoutEffectCount;
            fiber.PrevInsertionEffectHookCount = insertionEffectCount;
            fiber.PrevEffectHookCount = effectCount;
            fiber.PrevImperativeHandleHookCount = imperativeHandleCount;
            fiber.PrevIdHookCount = idCount;
            fiber.PrevDeferredValueHookCount = deferredCount;
            fiber.PrevOptimisticHookCount = optimisticCount;
            fiber.PrevMutationHookCount = mutationCount;
        }
#endif

#if UNITY_EDITOR
        private static void ValidateHookCallCount(string hookName, int prevCount, int currentCount)
        {
            if (prevCount != -1 && currentCount != prevCount)
            {
                FiberLogger.LogError("Hooks",
                    $"FiberRenderer: {hookName} call count differs between previous render ({prevCount})" +
                    $" and current ({currentCount}). Hooks must not be called inside conditional branches (violation of the Rules of Hooks).");
            }
        }
#endif

        // Throws at runtime when a positional hook family's slot count (UseState / UseReducer, UseStore, or the
        // async Use family) differs from the previous render. Slot index mismatch leads directly to silent
        // value corruption (overwriting a different slot with the same type), so fail-fast.
        private static void ValidateHookCountRuntime(string hookName, int prevCount, int currentCount)
        {
            if (prevCount != -1 && currentCount != prevCount)
            {
                throw new InvalidOperationException(
                    $"FiberRenderer: {hookName} call count differs between previous render ({prevCount})" +
                    $" and current ({currentCount}). Hooks must not be called inside conditional branches (violation of the Rules of Hooks).");
            }
        }
    }
}
