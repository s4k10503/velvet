using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Counts the GC.Alloc sample blocks the running thread charges between <see cref="Start"/> and
    /// <see cref="Stop"/>, for a window that waits out exactly one frame boundary.
    /// </summary>
    /// <remarks>
    /// <see cref="Stop"/> reads the recorder while it is still enabled and again after disabling it, and returns the
    /// two together; <c>GCAllocationAcrossOneFrameCanaries</c> fails where that is not the whole window. It throws
    /// for a window that crossed no frame boundary or more than one.
    /// Test-only. Must not be used from production code.
    /// </remarks>
    public sealed class GCAllocationAcrossOneFrame
    {
        private readonly Recorder _recorder = Recorder.Get("GC.Alloc");
        private int _startFrame;

        public void Start()
        {
            _recorder.enabled = false;
            _recorder.FilterToCurrentThread();
            _recorder.enabled = true;
            _startFrame = Time.frameCount;
        }

        public int Stop()
        {
            var boundaries = Time.frameCount - _startFrame;
            var endedFrame = _recorder.sampleBlockCount;
            _recorder.enabled = false;
            var currentFrame = _recorder.sampleBlockCount;
            _recorder.CollectFromAllThreads();
            if (boundaries != 1)
            {
                throw new InvalidOperationException($"the window crossed {boundaries} frame boundaries, where it reads exactly one");
            }
            return endedFrame + currentFrame;
        }
    }
}
