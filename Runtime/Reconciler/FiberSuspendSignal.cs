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

        // The signal the drain of a pass's Portals raises when a child of one suspended. It leaves a pass whose walk
        // has already committed its rows, where Instance leaves one whose walk is still in flight, and the catch
        // that abandons the pass reads which of the two it is by identity.
        public static readonly FiberSuspendSignal AfterTheWalk = new();

        private FiberSuspendSignal() { }
    }
}
