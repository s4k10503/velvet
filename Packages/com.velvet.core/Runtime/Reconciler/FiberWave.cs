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
    // While a wave is open, a top-level render records into the context's pass rather than ending one of its own
    // (Reconciler.ReconcileCatching), and a fiber's effect commit waits (FiberEffects.CommitSubtreeEffects). End
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
            ctx.DeferDrainLayoutEffects = true;
            ctx.Pass.Journal.Hold();
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
                // MUTANT_SURVIVES(equivalent, line removed): a refresh re-derives a region from the tree as it stands,
                // so refreshing a region a later wave lists again changes nothing; keeping it costs the walk.
                regions.Clear();
            }
        }

        // What the ops and placements of the wave's pass threw, each queued for the boundary that takes an error of the
        // root whose render recorded it (ComponentBoundarySearch.CaptureFromRoot), which renders its fallback in a
        // follow-up commit, as React's captureCommitPhaseError queues an error its commit meets. A root the wave's own renders deleted is passed
        // over, since that deletion replaced what its render wrote. Each route runs whether or not the one before it
        // threw, and the first one's exception is rethrown once all of them have run.
        private static void RouteFailures(ReconcilerContext ctx)
        {
            var failures = ctx.Pass.Failures;
            ExceptionDispatchInfo? first = null;
            try
            {
                for (var i = 0; i < failures.Count; i++)
                {
                    var (root, error) = failures[i];
                    if (root.IsBeingDeleted || root.IsDisposed) continue;
                    var failure = Capture(root, error);
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
        private static ExceptionDispatchInfo? Capture(ComponentFiber root, Exception error)
        {
            try
            {
                ComponentBoundarySearch.CaptureFromRoot(root, error);
                return null;
            }
            catch (Exception exception)
            {
                return ExceptionDispatchInfo.Capture(exception);
            }
        }
    }
}
