#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The time source a mounted tree's motion advances on, chosen per mount through
    /// <see cref="MountOptions.MotionClock"/>: every <see cref="StyleTransitionConfig"/> play whatever its
    /// <see cref="StyleTransitionConfig.Type"/>, its <see cref="StyleTransitionConfig.DelaySec"/> included, a
    /// <c>layoutId</c> move, a <c>filter-*</c> transition, the <c>animate-*</c> loops, and the delta
    /// <see cref="Hooks.UseFrame(System.Action{float}, int)"/> hands its callback.
    /// <c>Documentation~/motion.md</c> owns the full scope.
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
        /// runs on UI Toolkit's own transitions, the motion Velvet drives itself and
        /// <see cref="Hooks.UseFrame(System.Action{float}, int)"/> advance with the panel scheduler's own time, and an
        /// <c>animate-*</c> loop and a <c>filter-*</c> transition with <c>Time.realtimeSinceStartupAsDouble</c>,
        /// which is what <see cref="NowSec"/> reads.
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

        // The clock of the mount that created an element, for the motion that starts on it from code holding no
        // mount: a filter-* transition starts in the arbitrary-value resolver, which a hover manipulator runs too.
        // An element no mount recorded answers with the nearest recorded ancestor, and Realtime past the root.
        private static readonly ConditionalWeakTable<VisualElement, MotionClock> s_recorded = new();
        // The mounts on another clock than Realtime still mounted. While there is none every answer is Realtime and
        // nothing is recorded, so a tree on the default clock pays no table entry per element.
        private static int s_liveMounts;

        internal static void RetainMount() => s_liveMounts++;

        internal static void ReleaseMount()
        {
            if (--s_liveMounts == 0)
            {
                s_recorded.Clear();
            }
        }

        // Every clock while one is retained, Realtime included: a Realtime mount under an element another mount
        // recorded answers with its own.
        internal static void Record(VisualElement element, MotionClock clock)
        {
            if (s_liveMounts == 0)
            {
                return;
            }
            s_recorded.AddOrUpdate(element, clock);
        }

        internal static void Forget(VisualElement element) => s_recorded.Remove(element);

        internal static MotionClock Of(VisualElement element)
        {
            if (s_liveMounts == 0)
            {
                return Realtime;
            }
            for (var current = element; current != null; current = current.hierarchy.parent)
            {
                if (s_recorded.TryGetValue(current, out var clock))
                {
                    return clock;
                }
            }
            return Realtime;
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
