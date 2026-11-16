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
        // Bypasses: the door: production reaches a commit step only inside the entry a flush, a mount or a drain opens.
        internal static void RunAsEntryForTest(Action body)
            => FiberExecutionContext.Enter<Action>(null, body, static run => run());
    }
}
