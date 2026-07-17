#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Brackets a discrete user-input handler: marks <see cref="FiberWorkLoop.IsInDiscreteEvent"/> for its
    /// duration so hook-triggered renders take the Urgent lane, then — at the outermost discrete boundary —
    /// flushes the owning context's immediate batch synchronously so the update commits in the same frame.
    /// The flag is restored before the flush so updates scheduled by effects that run during the flush take
    /// the Normal lane; they still commit within this same synchronous flush (the drain loops until its
    /// queue is quiet — React's setState-in-commit semantics — with the maximum-update-depth cap bounding a
    /// runaway feedback pair), just without escalating the discrete Urgent classification. Shared by
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
                FiberWorkLoop.IsInDiscreteEvent = wasInDiscreteEvent;
                if (!wasInDiscreteEvent)
                {
                    batchScheduler?.FlushImmediate();
                }
            }
        }
    }
}
