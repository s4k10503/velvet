#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The playback a group of Motion plays shares, as Framer Motion's GroupAnimation and the Web Animations API's
    // Animation hold it: whether it is paused, the rate it advances at, and the plays still running in it. A
    // Spring or Bezier play started under one steps by the playback's time rather than the panel's
    // (StyleAnimationScheduler.StepSpringOnPlayback, StepBezierOnPlayback); SequenceWalker owns one per sequence.
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

        // A play stays listed until a later Track finds it no longer running, so the list holds the running plays
        // and those that ended since the last play started.
        public void Track(StyleAnimationScheduler scheduler, VisualElement element, object play)
        {
            _plays.RemoveAll(entry => !entry.Scheduler.IsRunning(entry.Element, entry.Play));
            _plays.Add((scheduler, element, play));
        }

        // The Web Animations API's cancel() on each running play: its effect comes off the element, which shows the
        // pose its classes name, and its completion never runs.
        public void CancelPlays()
        {
            var plays = _plays.ToArray();
            _plays.Clear();
            foreach (var (scheduler, element, play) in plays)
            {
                scheduler.CancelPlay(element, play);
            }
        }
    }
}
