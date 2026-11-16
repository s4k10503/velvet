using System;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Runs test code that calls a render or commit step directly, beneath the door production reaches it through, as
    /// the outermost entry that door would open. Reached through internals because production types carry no
    /// test-only members.
    /// </summary>
    internal static class FiberExecutionContextTestAccess
    {
        // Bypasses: the door: production reaches a render or commit step only inside FiberExecutionContext.Enter.
        internal static void RunAsEntryForTest(Action body)
            => FiberExecutionContext.Enter<Action>(null, body, static run => run());

        // Bypasses: the door: production disposes a VirtualList's controller where a pass's cleanup removes its list, inside that pass, or once its context is disposed (Reconciler.Dispose).
        internal static EntryDisposal DisposedAsEntryForTest(IDisposable target) => new(target);

        // Disposes its target, where a using statement ends, as the outermost entry.
        internal readonly struct EntryDisposal : IDisposable
        {
            private readonly IDisposable _target;

            internal EntryDisposal(IDisposable target) => _target = target;

            public void Dispose() => RunAsEntryForTest(_target.Dispose);
        }
    }
}
