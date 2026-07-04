using System;

namespace Velvet
{
    // Sentinel exception thrown by Use<T> against a pending async resource.
    // RenderAndReconcile catches it and asks the nearest Suspense Boundary to display the fallback.
    // Uses only a singleton instance to distinguish from regular exceptions, and does not retain a stack trace.
    // A pending async read aborts the in-progress render by throwing this signal, which unwinds the
    // stack up to the nearest Suspense Boundary so it can show its fallback until the resource settles.
    // This class must be caught only by Velvet's internal RenderAndReconcile and never by user code.
    internal sealed class FiberSuspendSignal : Exception
    {
        public static readonly FiberSuspendSignal Instance = new();

        private FiberSuspendSignal() { }
    }
}
