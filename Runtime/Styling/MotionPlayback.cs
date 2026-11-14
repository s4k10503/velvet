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
        // A play is listed from its start until StyleAnimationScheduler Untracks it: when it finishes, when
        // CancelPending stops it (a later play on its element, or CancelEnter), or when its held values are
        // released. CancelPlays keeps it listed, held.
        private readonly Dictionary<object, (StyleAnimationScheduler Scheduler, VisualElement Element)> _plays = new();
        private float _rate = 1f;

        public bool IsPaused { get; set; }

        // Framer Motion's speed and the Web Animations API's playbackRate, short of the negative rates both read
        // as reverse: a rate is finite and zero or more, and zero holds every play where it is. NaN fails the
        // comparison.
        public float Rate
        {
            get => _rate;
            set
            {
                if (!(value >= 0f) || float.IsInfinity(value))
                {
                    throw new System.ArgumentOutOfRangeException(nameof(value), value,
                        "A playback rate is a finite number, zero or more.");
                }
                _rate = value;
            }
        }

        // The playback seconds a frame of `dtSec` covers.
        public float Scale(float dtSec) => IsPaused ? 0f : dtSec * _rate;

        public void Track(StyleAnimationScheduler scheduler, VisualElement element, object play)
            => _plays[play] = (scheduler, element);

        // Framer Motion's cancel() on each play: it returns to its starting values and stops there, without its
        // completion (StyleAnimationScheduler.CancelPlay). The held plays stay listed for ReleaseHeldPlays.
        public void CancelPlays()
        {
            foreach (var (play, (scheduler, element)) in Listed())
            {
                scheduler.CancelPlay(element, play);
            }
        }

        // Takes the held plays' values off their elements, which then show the poses their classes name: what a
        // reseed does, once its own plays have started from them, to the ones none of its plays replaced.
        public void ReleaseHeldPlays()
        {
            foreach (var (play, (scheduler, element)) in Listed())
            {
                scheduler.ReleaseHeldPlay(element, play);
            }
        }

        public void Untrack(object play) => _plays.Remove(play);

        // A copy, since each call the loops above make can Untrack.
        private List<KeyValuePair<object, (StyleAnimationScheduler Scheduler, VisualElement Element)>> Listed()
            => new(_plays);
    }
}
