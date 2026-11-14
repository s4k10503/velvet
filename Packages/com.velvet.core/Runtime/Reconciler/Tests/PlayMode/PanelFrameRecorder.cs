using System;
using UnityEngine.Profiling;

namespace Velvet.Tests.Performance
{
    /// <summary>
    /// What one runtime panel spent in the last frame that ended, phase by phase, read from the profiler markers
    /// the panel names after itself, and the GC.Alloc blocks the main thread charged in that frame.
    /// </summary>
    /// <remarks>
    /// The recorders stay enabled from <see cref="Start"/> to <see cref="Stop"/>, and <see cref="Capture"/> reads
    /// them while they are, so that a reading is the last whole frame's, which <c>PanelFrameRecorderCanaries</c>
    /// checks of the allocation count. The constructor throws when the panel has no marker by one of the expected
    /// names, and <see cref="RanEveryPhase"/> is how a caller refuses a frame that ran none of a phase.
    /// </remarks>
    internal sealed class PanelFrameRecorder
    {
        internal static readonly string[] Phases = { "TickScheduledActions", "ValidateLayout", "PrepareRepaint" };

        private readonly Recorder[] _phases;
        private readonly Recorder _allocation;
        private readonly double[] _milliseconds = new double[Phases.Length];
        private readonly int[] _blocks = new int[Phases.Length];

        internal PanelFrameRecorder(string panelName)
        {
            _phases = new Recorder[Phases.Length];
            for (var i = 0; i < Phases.Length; i++)
            {
                var marker = panelName + "." + Phases[i];
                var recorder = Recorder.Get(marker);
                if (!recorder.isValid)
                {
                    throw new InvalidOperationException($"no profiler marker is named '{marker}'");
                }
                _phases[i] = recorder;
            }
            _allocation = Recorder.Get("GC.Alloc");
        }

        internal int AllocationBlocks { get; private set; }

        internal bool RanEveryPhase => Array.IndexOf(_blocks, 0) < 0;

        internal double Milliseconds(int phase) => _milliseconds[phase];

        internal void Start()
        {
            foreach (var recorder in _phases)
            {
                recorder.enabled = false;
                recorder.enabled = true;
            }
            _allocation.enabled = false;
            _allocation.FilterToCurrentThread();
            _allocation.enabled = true;
        }

        internal void Capture()
        {
            for (var i = 0; i < Phases.Length; i++)
            {
                _milliseconds[i] = _phases[i].elapsedNanoseconds / 1e6;
                _blocks[i] = _phases[i].sampleBlockCount;
            }
            AllocationBlocks = _allocation.sampleBlockCount;
        }

        internal void Stop()
        {
            foreach (var recorder in _phases)
            {
                recorder.enabled = false;
            }
            _allocation.enabled = false;
            _allocation.CollectFromAllThreads();
        }
    }
}
