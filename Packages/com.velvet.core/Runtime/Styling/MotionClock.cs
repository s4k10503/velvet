#nullable enable
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The time source a mounted tree's own motion drivers advance on, chosen per mount through
    /// <see cref="MountOptions.MotionClock"/>. It governs a <see cref="TransitionType.Spring"/> or
    /// <see cref="TransitionType.Bezier"/> variant play, its <see cref="StyleTransitionConfig.DelaySec"/>
    /// included, and the <c>animate-*</c> loops. A
    /// <see cref="TransitionType.Tween"/> hands its interpolation to UI Toolkit's own transitions, which no clock
    /// chosen here reaches; a mount on any other clock than <see cref="Realtime"/> warns once when one plays.
    /// <c>Documentation~/motion.md</c> owns the full scope.
    /// </summary>
    /// <remarks>
    /// Derive from this class for a clock the application controls itself — one a frame-step capture advances by
    /// a fixed amount, for instance. On any clock but <see cref="Realtime"/>, a spring or bezier play steps by
    /// the distance <see cref="NowSec"/> moved since its previous tick, and an <c>animate-*</c> loop shows the
    /// phase of the distance since it started, so a clock that does not move holds both where they are.
    /// </remarks>
    public abstract class MotionClock
    {
        /// <summary>
        /// The default, and the behaviour a tree mounted without a clock has: a spring or bezier play advances
        /// with the panel scheduler's own time, and an <c>animate-*</c> loop with
        /// <c>Time.realtimeSinceStartupAsDouble</c>, which is what <see cref="NowSec"/> reads.
        /// </summary>
        public static MotionClock Realtime { get; } = new RealtimeClock();

        /// <summary>
        /// Reads <c>Time.timeAsDouble</c>, so a spring or bezier play and an <c>animate-*</c> loop move only as far
        /// as the game's own scaled time has moved — for UI motion that should keep pace with gameplay.
        /// </summary>
        public static MotionClock GameTime { get; } = new GameTimeClock();

        /// <summary>Constructs a clock. Derive to supply <see cref="NowSec"/>.</summary>
        protected MotionClock()
        {
        }

        /// <summary>The clock's current reading, in seconds. Only differences between readings are used.</summary>
        public abstract double NowSec { get; }

        // True for Realtime alone: its spring and bezier plays step by the panel scheduler's interval, the time
        // their scheduled items fire on, rather than by NowSec (StyleAnimationScheduler.StartSpringTick).
        internal virtual bool StepsOnPanelTime => false;

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
