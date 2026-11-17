#nullable enable
using System;

namespace Velvet
{
    // Where an error goes that a layout effect, a passive effect or a ref callback throws while its commit runs, as
    // React's captureCommitPhaseError sends it: queued on the nearest boundary above the fiber that threw, which
    // renders its fallback in a follow-up commit. The commit that threw goes on, so the effects after the throwing
    // one still run, and nothing is mounted or unmounted from inside it.
    internal static class CommitErrors
    {
        // True where boundary takes the error. A boundary that registered no fallback passes it on, as
        // FiberErrorBoundary.TryCatch does where it renders none. The captures stay in the order they were thrown: one a
        // wave filed as its writes applied (ReconcilePass.Fail) is captured once they have, at the place it took then.
        internal static bool Capture(ComponentFiber boundary, ComponentFiber? thrower, Exception error, long sequence = -1)
        {
            var ctx = boundary.Reconciler?.Context;
            if (ctx == null) return false;
            if (boundary.IsMountRoot && boundary.IsDisposed)
            {
                // MountedTree.Dispose is unmounting the tree: it reports what was thrown once the unmount is done
                // (FiberErrorBoundary.ReportErrorsDuringDisposal).
                ctx.ErrorsDuringRootDisposal.Add((error, FiberErrorBoundary.InfoFor(boundary, thrower)));
                return true;
            }
            if (!boundary.IsMounted || boundary.FallbackFactory == null) return false;
            var info = FiberErrorBoundary.InfoFor(boundary, thrower);
            var at = sequence < 0 ? ctx.NextCaptureSequence++ : sequence;
            var captures = boundary.PendingCaptures ??= new();
            var index = captures.Count;
            while (index > 0 && captures[index - 1].Sequence > at) index--;
            captures.Insert(index, (error, info, at));
            if (!ctx.BoundariesOwedCaptures.Contains(boundary)) ctx.BoundariesOwedCaptures.Add(boundary);
            FiberWorkLoop.ScheduleRerender(boundary, FiberUpdatePriority.Urgent);
            // React flushes the sync work a passive effect's error queues as that flush ends, so the drain
            // that is flushing the effects renders the boundary rather than leaving it to the next frame.
            ctx.BatchScheduler.OweSyncLane();
            return true;
        }

        // A boundary an ancestor's fallback replaced holds its captures no longer.
        internal static void DropUnmounted(ReconcilerContext ctx)
        {
            var owed = ctx.BoundariesOwedCaptures;
            for (var i = owed.Count - 1; i >= 0; i--)
            {
                if (!owed[i].IsMounted) ctx.DropCapturesOf(owed[i]);
            }
        }

        // The sequence of the oldest capture a mounted boundary holds, or long.MaxValue where none does.
        internal static long OldestOwed(ReconcilerContext ctx)
        {
            DropUnmounted(ctx);
            var oldest = long.MaxValue;
            foreach (var boundary in ctx.BoundariesOwedCaptures)
            {
                // A fallback that has not committed holds its captures until its catch is reported (TakeCaptures).
                if (boundary.CatchUnreported) continue;
                var captures = boundary.PendingCaptures;
                if (captures is { Count: > 0 } && captures[0].Sequence < oldest) oldest = captures[0].Sequence;
            }
            return oldest;
        }
    }
}
