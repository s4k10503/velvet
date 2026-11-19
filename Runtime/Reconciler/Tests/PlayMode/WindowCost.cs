using System;
using System.Diagnostics;
using UnityEngine.Profiling;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// What one run of an operation costs: the time it took, and the GC.Alloc sample blocks it charged to the
    /// thread that ran it.
    /// </summary>
    /// <remarks>
    /// The recorder is switched and read as <c>GCAllocationProbe</c> switches and reads it. One run is one
    /// sample here, where the probe takes the middle of three, because a benchmark reports the median of its
    /// samples and asserts nothing about any one of them.
    /// </remarks>
    internal readonly struct WindowCost
    {
        private WindowCost(double milliseconds, int allocationBlocks)
        {
            Milliseconds = milliseconds;
            AllocationBlocks = allocationBlocks;
        }

        internal double Milliseconds { get; }

        internal int AllocationBlocks { get; }

        internal static WindowCost Of(Action operation)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;

            var started = Stopwatch.GetTimestamp();
            long stopped;
            int blocks;
            try
            {
                operation();
            }
            finally
            {
                stopped = Stopwatch.GetTimestamp();
                recorder.enabled = false;
                blocks = recorder.sampleBlockCount;
                recorder.CollectFromAllThreads();
            }
            return new WindowCost(ElapsedMilliseconds(started, stopped), blocks);
        }

        internal static double ElapsedMilliseconds(long started, long stopped)
            => (stopped - started) * 1000.0 / Stopwatch.Frequency;
    }
}
