#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The playback a group of Motion plays shares, as Framer Motion's GroupAnimation and the Web Animations API's
    // Animation hold it: whether it is paused, the rate it advances at, and the plays still running in it. A
    // Spring or Bezier play started under one runs on the playback's time rather than the panel's
    // (StyleAnimationScheduler.StartOnPlayback, StepOnPlayback); SequenceWalker owns one per sequence.
    internal sealed class MotionPlayback
    {
        private readonly List<(StyleAnimationScheduler Scheduler, VisualElement Element, object Play)> _plays = new();
        private float _rate = 1f;

        public bool IsPaused { get; set; }

        // Framer Motion's speed and the Web Animations API's playbackRate, short of the negative rates both read
        // as reverse: a rate is finite and zero or more, and zero holds every play where it is.
        public float Rate
        {
            get => _rate;
            set
            {
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(value), value,
                        "A playback rate is a finite number, zero or more.");
                }
                _rate = value;
            }
        }

        // The playback seconds a frame of `dtSec` covers.
        public float Scale(float dtSec) => IsPaused ? 0f : dtSec * _rate;

        // A play stays listed until a later Track finds it no longer running, so the list holds the running plays,
        // those CancelPlays holds, and those that ended since the last play started.
        public void Track(StyleAnimationScheduler scheduler, VisualElement element, object play)
        {
            _plays.RemoveAll(entry => !entry.Scheduler.IsRunning(entry.Element, entry.Play));
            _plays.Add((scheduler, element, play));
        }

        // Framer Motion's cancel() on each play: it returns to its starting values and stops there, without its
        // completion (StyleAnimationScheduler.CancelPlay). The held plays stay listed for ClearPlays.
        public void CancelPlays()
        {
            foreach (var (scheduler, element, play) in _plays.ToArray())
            {
                scheduler.CancelPlay(element, play);
            }
        }

        // Stops every play, held ones included, and takes its values off its element, which then shows the pose
        // its classes name.
        public void ClearPlays()
        {
            var plays = _plays.ToArray();
            _plays.Clear();
            foreach (var (scheduler, element, play) in plays)
            {
                scheduler.ClearPlay(element, play);
            }
        }
    }
}
