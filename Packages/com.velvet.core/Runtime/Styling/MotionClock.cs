#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The time source a mounted tree's motion advances on, chosen per mount through
    /// <see cref="MountOptions.MotionClock"/>: every <see cref="StyleTransitionConfig"/> play whatever its
    /// <see cref="StyleTransitionConfig.Type"/>, its <see cref="StyleTransitionConfig.DelaySec"/> included, and the
    /// <c>animate-*</c> loops. <c>Documentation~/motion.md</c> owns the full scope.
    /// </summary>
    /// <remarks>
    /// Derive from this class for a clock the application controls itself — one a frame-step capture advances by
    /// a fixed amount, for instance. On any clock but <see cref="Realtime"/>, motion steps by the distance
    /// <see cref="NowSec"/> moved since its previous frame, so a clock that does not move holds it where it is.
    /// </remarks>
    public abstract class MotionClock
    {
        /// <summary>
        /// The default, and the behaviour a tree mounted without a clock has: a <see cref="TransitionType.Tween"/>
        /// runs on UI Toolkit's own transitions, a spring or bezier play advances with the panel scheduler's own time,
        /// and an <c>animate-*</c> loop with <c>Time.realtimeSinceStartupAsDouble</c>, which is what
        /// <see cref="NowSec"/> reads.
        /// </summary>
        public static MotionClock Realtime { get; } = new RealtimeClock();

        /// <summary>
        /// Reads <c>Time.timeAsDouble</c>, so the tree's motion moves only as far as the game's own scaled time has
        /// moved — for UI motion that should keep pace with gameplay.
        /// </summary>
        public static MotionClock GameTime { get; } = new GameTimeClock();

        /// <summary>Constructs a clock. Derive to supply <see cref="NowSec"/>.</summary>
        protected MotionClock()
        {
        }

        /// <summary>The clock's current reading, in seconds. Only differences between readings are used.</summary>
        public abstract double NowSec { get; }

        // True for Realtime alone: its motion steps by the panel scheduler's interval, the time its scheduled items
        // fire on, rather than by NowSec, and its Tween plays run on UI Toolkit's own transitions.
        internal virtual bool StepsOnPanelTime => false;

        // The seconds one recurring tick advances motion by. lastSec is the reading the previous tick took, which
        // Realtime leaves unread.
        internal float StepSince(ref double lastSec, TimerState ts)
        {
            if (StepsOnPanelTime)
            {
                // TimerState.start is the previous callback's time for a repeating item (or the schedule time
                // for the first firing), so deltaTime is already exactly the elapsed interval this tick needs.
                return ts.deltaTime / 1000f;
            }
            var now = NowSec;
            var dt = (float)(now - lastSec);
            lastSec = now;
            return dt;
        }

        private sealed class RealtimeClock : MotionClock
        {
            public override double NowSec => Time.realtimeSinceStartupAsDouble;

            internal override bool StepsOnPanelTime => true;
        }

        private sealed class GameTimeClock : MotionClock
        {
            public override double NowSec => Time.timeAsDouble;
        }
    }
}
