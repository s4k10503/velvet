#nullable enable
using System;
using System.Runtime.ExceptionServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // React renders the components one update reaches before its commit applies any of their host writes, deletion
    // cleanups or disposals, and commits their effects together. A wave is that unit here: a drain of either tier
    // (FiberBatchScheduler.Drain), or the body of a door — a mount (FiberRenderer.RenderMountedWork), a flush of one
    // fiber (FiberWorkLoop.FlushStateWork) or a resumed slice (FiberWorkLoop.ContinueReconcileWork) — with what it
    // renders before its commit. Those bodies open a wave only where a door runs them; run as the sub-work of an entry
    // already on the stack, they open none.
    //
    // While the wave records, a top-level render records into the context's pass rather than ending one of its own
    // (Reconciler.ReconcileCatching; Reconciler.FinishTopLevelPass owns where a Portal's mount ends the pass early),
    // and a fiber's effect commit waits (FiberEffects.CommitSubtreeEffects). End
    // applies the pass once the renders are done; Commit runs the one layout batch. The opener ends its wave in a
    // finally that sits inside the one that commits it, so a throw leaves neither the pass recording nor the commit
    // waiting. The commit lowers the deferral before it runs any effect, so a drain it starts opens a wave of its own.
    internal static class FiberWave
    {
        internal static void Open(ReconcilerContext ctx)
        {
            UnityEngine.Debug.Assert(
                !ChildListWrites.PassIsOpen && ctx.SharedReconcileDepth == 0 && !ctx.DeferDrainLayoutEffects,
                "A wave opened inside a pass or ahead of another wave's commit.");
            ctx.WaveOpen = true;
            ctx.WaveRecords = true;
            ctx.WaveOwesIndicatorFlush = false;
            ctx.WaveKeepsBatchOrder = false;
            ctx.DeferDrainLayoutEffects = true;
            ctx.Pass.Journal.Hold();
        }

        // A wave runs each fiber's insertion effects and layout cleanups, and each replaced ref's detach, as its pass
        // applies, at the fiber's or the element's turn (ChildListWrites.RecordTurnEnd, RecordRefDetach); the layout
        // commit runs what is left in tree order. The two agree only where the pass holds a turn for exactly what the
        // commit would run, each where the tree puts it, so the wave leaves all of it to the layout commit where it
        // - applied its writes up to a Portal's or a WorldSpace's mount, whose components mount behind them;
        // - parked a render, whose cleanups wait for the commit that completes it;
        // - resumed one, whose earlier slices left their turns to passes that have applied;
        // - rendered a component a settle owes a render, after the renders that settle followed, wherever it sits;
        // - revealed a component still holding insertion effects, which no turn holds where it did not render;
        // - mounted a component holding insertion effects under a component a commit has taken, whose rows React places
        //   before those effects run, and the wave's writes place, or place an element holding, where a container's walk
        //   ends, behind the component's turn (KeepBatchOrderForAPlacement).
        internal static void KeepBatchOrder(ReconcilerContext ctx)
        {
            if (ctx.WaveOpen) ctx.WaveKeepsBatchOrder = true;
        }

        // A mount root stands for React's host root, which a commit has always taken.
        internal static void KeepBatchOrderForAPlacement(ComponentFiber fiber)
        {
            if (fiber.HasCommitted || (fiber.PendingInsertionEffects?.Count ?? 0) == 0) return;
            if (fiber.Parent is { IsMountRoot: false, HasCommitted: false }) return;
            if (fiber.Reconciler?.Context is { } ctx) KeepBatchOrder(ctx);
        }

        // Applies the pass the wave's renders recorded into, commits the hook state they staged, routes what the
        // writes threw and refreshes the has- regions of the flushes the wave rendered, each step whether or not the
        // one before it threw.
        internal static void End(ReconcilerContext ctx)
        {
            ctx.WaveOpen = false;
            ctx.WaveRecords = false;
            try
            {
                if (ctx.Pass.Recording) ctx.Root!.FinishWavePass();
            }
            finally
            {
                try
                {
                    if (ctx.Pass.Journal.IsHeld) ctx.Pass.Journal.Close();
                }
                finally
                {
                    try
                    {
                        RouteFailures(ctx);
                    }
                    finally
                    {
                        RefreshHasVariantRegions(ctx);
                    }
                }
            }
        }

        // A door's commit: the layout batch of what the wave deferred, then the end of the render a layoutId reads,
        // which a drain runs in its own end instead (Reconciler's drain callbacks).
        internal static void Commit(ReconcilerContext ctx)
        {
            try
            {
                CommitDeferred(ctx);
            }
            finally
            {
                MotionLayoutIdDriver.EndRender(ctx);
            }
        }

        // The drag session's overlay join follows the layout effects but does not depend on them completing.
        internal static void CommitDeferred(ReconcilerContext ctx)
        {
            try
            {
                // Ahead of the layout effects, which read the tree the wave committed.
                try
                {
                    PointerEventsScope.OnDrainEnd(ctx);
                }
                finally
                {
                    FiberEffects.FlushDeferredDrainLayoutEffects(ctx);
                }
            }
            finally
            {
                ctx.ActiveDrag?.JoinOverlaysAfterCommit();
            }
        }

        // A flush the wave renders has its has- region refreshed once the wave's writes have applied, ahead of the
        // wave's commit, as a flush outside a wave refreshes its own after its pass applies.
        internal static void RefreshHasVariants(ReconcilerContext? ctx, VisualElement? region)
        {
            if (ctx is { WaveOpen: true })
            {
                ctx.WaveHasVariantRegions.Add(region);
                return;
            }
            FiberNodePatcher.RefreshHasVariants(ctx, region);
        }

        private static void RefreshHasVariantRegions(ReconcilerContext ctx)
        {
            var regions = ctx.WaveHasVariantRegions;
            try
            {
                for (var i = 0; i < regions.Count; i++) FiberNodePatcher.RefreshHasVariants(ctx, regions[i]);
            }
            finally
            {
                regions.Clear();
            }
        }

        // What the ops and placements of the wave's pass threw, each queued for the boundary the search from where it was
        // filed to start finds (ComponentBoundarySearch.CaptureFrom), which renders its fallback in a follow-up commit. A
        // failure whose element was on a panel as it threw and is off one by now is not reported: where the wave's own
        // writes took the element off, React would never have committed it, as it commits no host of a render it
        // discarded. Each route runs whether or not the one before it threw, and the first one's exception is rethrown
        // once all of them have run.
        private static void RouteFailures(ReconcilerContext ctx)
        {
            var failures = ctx.Pass.Failures;
            ExceptionDispatchInfo? first = null;
            try
            {
                for (var i = 0; i < failures.Count; i++)
                {
                    var (start, owner, error, onPanel) = failures[i];
                    if (onPanel is { panel: null }) continue;
                    var failure = Capture(start, owner, error);
                    first ??= failure;
                }
            }
            finally
            {
                failures.Clear();
            }
            first?.Throw();
        }

        // What the route itself threw, which leaves the routes behind it to run.
        private static ExceptionDispatchInfo? Capture(ComponentFiber? start, ComponentFiber owner, Exception error)
        {
            try
            {
                ComponentBoundarySearch.CaptureFrom(start, owner, error);
                return null;
            }
            catch (Exception exception)
            {
                return ExceptionDispatchInfo.Capture(exception);
            }
        }
    }
}
