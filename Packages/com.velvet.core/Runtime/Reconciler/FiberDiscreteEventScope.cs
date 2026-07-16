#nullable enable
using System;

namespace Velvet
{
    /// <summary>
    /// Brackets a discrete user-input handler: marks <see cref="FiberWorkLoop.IsInDiscreteEvent"/> for its
    /// duration so hook-triggered renders take the Urgent lane, then — at the outermost discrete boundary —
    /// flushes the owning context's immediate batch synchronously so the update commits in the same frame.
    /// The flag is restored before the flush so updates scheduled by effects that run during the flush fall
    /// back to the Normal next-frame lane rather than recursing synchronously. Shared by
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
