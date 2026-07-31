using System;
using UnityEngine.Profiling;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Counts the GC.Alloc sample blocks a delegate charges to the thread that runs it.
    /// </summary>
    /// <remarks>
    /// The count is read while the recorder is still filtered to this thread, and the restore happens
    /// after. Reading on the other side of the restore charges the delegate for allocations made by every
    /// other thread in the process, which no delegate can prevent. The restore is not optional: the
    /// recorder is process-global, so leaving it filtered would silently narrow every later reader.
    /// </remarks>
    public static class GCAllocationProbe
    {
        public static int SampleBlocksDuring(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var recorder = Recorder.Get("GC.Alloc");

            // Disabling flushes what the recorder captured before this call — its own construction among
            // it — so the count below starts from the delegate rather than from the measurement setup.
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            var blocks = 0;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                blocks = recorder.sampleBlockCount;
                recorder.CollectFromAllThreads();
            }
            return blocks;
        }
    }
}
