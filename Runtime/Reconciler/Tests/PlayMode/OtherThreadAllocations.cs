using System;
using System.Threading;
using UnityEngine.Profiling;

namespace Velvet.Tests.Performance
{
    // A known number of allocations made on a thread of its own, so an allocation canary can tell a reading of the
    // running thread from a reading of every thread. The thread starts at construction and allocates once
    // AllocateAndWait, or Dispose, releases it.
    internal sealed class OtherThreadAllocations : IDisposable
    {
        private readonly int _count;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _go = new(false);
        private readonly ManualResetEventSlim _done = new(false);

        internal OtherThreadAllocations(int count)
        {
            _count = count;
            _thread = new Thread(Run) { IsBackground = true };
            _thread.Start();
        }

        internal void AllocateAndWait()
        {
            _go.Set();
            _done.Wait();
        }

        public void Dispose()
        {
            _go.Set();
            _thread.Join();
            _go.Dispose();
            _done.Dispose();
        }

        private void Run()
        {
            Profiler.BeginThreadProfiling("Velvet.Tests", nameof(OtherThreadAllocations));
            try
            {
                _go.Wait();
                for (var i = 0; i < _count; i++)
                {
                    GC.KeepAlive(new byte[16]);
                }
                _done.Set();
            }
            finally
            {
                Profiler.EndThreadProfiling();
            }
        }
    }
}
