using System;
using System.Collections.Generic;

namespace Velvet
{
    // JavaScript's await resumes in a microtask, once the code that called startTransition has returned, so
    // nothing an action runs past an await is in the transition it started. An await of a VelvetTask that
    // had already completed is held to that: its continuation runs once a discrete handler has returned,
    // or on the next main-thread tick outside one. An awaiter Velvet does not own (Task, ValueTask,
    // Awaitable) is not, since C# continues inline wherever the awaiter reports completion;
    // UseTransitionTests pins that side with an await of Task.CompletedTask.
    //
    // The work loop sets OpenScopes as it opens and closes a transition scope and drains Queued; the awaiter
    // reads the gate and fills the queue, so Async names nothing of the work loop.
    internal static class AwaitContinuationGate
    {
        internal static int OpenScopes;

        internal static bool Defers => OpenScopes > 0 && VelvetMainThread.IsCurrent;

        internal static readonly Queue<Action> Queued = new();

        internal static void Defer(Action continuation) => Queued.Enqueue(continuation);

        // VelvetMainThread.Capture drops the drain it had posted, and this queue goes with it.
        internal static void Discard() => Queued.Clear();
    }
}
