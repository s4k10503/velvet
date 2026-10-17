#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Brackets a discrete user-input handler: marks <see cref="FiberWorkLoop.IsInDiscreteEvent"/> for its
    /// duration so hook-triggered renders take the Urgent lane, then — at the outermost discrete boundary —
    /// runs the pending passive effects of earlier commits and flushes the owning context's immediate batch
    /// synchronously, so the update commits in the same frame. That commit is a sync-lane one, so it runs its
    /// own passive effects as it ends (<c>FiberBatchScheduler.DrainImmediatePasses</c>). The flag is restored
    /// before both, so an update an effect schedules takes the Normal lane: one from the earlier commits'
    /// effects commits within this same flush, and one from this commit's waits for the next drain. A state
    /// update issued during the commit still commits within the same flush (the drain loops until its queue
    /// is quiet, with the maximum-update-depth cap bounding a runaway feedback pair). Shared by
    /// <c>FiberEventBindingManager</c> (clicks, value changes, discrete pointer/key bindings) and the
    /// drag-and-drop session (<c>OnDragStart</c>/<c>OnDragEnd</c>/<c>OnDragCancel</c>), so a drag commit
    /// behaves exactly like a click handler's.
    /// </summary>
    internal static class FiberDiscreteEventScope
    {
        public static void Run(Action? handler, FiberBatchScheduler? batchScheduler)
        {
            var wasInDiscreteEvent = FiberWorkLoop.IsInDiscreteEvent;
            FiberWorkLoop.IsInDiscreteEvent = true;
            try
            {
                handler?.Invoke();
            }
            finally
            {
                try
                {
                    // What a transition action deferred past an await (FiberWorkLoop.DefersAwaitContinuations)
                    // runs once the handler has returned and ahead of this event's flush, which commits it.
                    if (!wasInDiscreteEvent) FiberWorkLoop.DrainDeferredAwaitContinuations();
                }
                finally
                {
                    FiberWorkLoop.IsInDiscreteEvent = wasInDiscreteEvent;
                }
                if (!wasInDiscreteEvent)
                {
                    // Flushed here as well as before the drain's render (FiberBatchScheduler.Drain), so an event
                    // that schedules no update still runs the effects of the commit before it. The event's own
                    // flush still runs even if a runaway passive-effect loop throws its guard.
                    try
                    {
                        batchScheduler?.FlushPendingPassiveEffects();
                    }
                    finally
                    {
                        batchScheduler?.FlushImmediate();
                    }
                }
            }
        }
    }
}
