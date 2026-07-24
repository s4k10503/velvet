using System;

namespace Velvet
{
    // Sentinel exception thrown by Use<T> against a pending async resource; unwinds the render stack
    // until RenderAndReconcile catches it and shows the nearest Suspense boundary's fallback until the
    // resource settles. Singleton instance with no retained stack trace, since it exists only as a
    // control-flow signal. Must be caught only by Velvet's internal RenderAndReconcile, never by user code.
    internal sealed class FiberSuspendSignal : Exception
    {
        public static readonly FiberSuspendSignal Instance = new();

        private FiberSuspendSignal() { }
    }
}
