using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static class MotionTweenTiming
    {
        // One tween's timing as it last wrote it, which the element takes back while that tween is the latest
        // still playing.
        internal sealed class Play
        {
            public StyleList<TimeValue> Duration;
            public StyleList<TimeValue> Delay;
            public StyleList<EasingFunction> Curve;
        }

        private sealed class Hold
        {
            public StyleList<TimeValue> Duration;
            public StyleList<TimeValue> Delay;
            public StyleList<EasingFunction> Curve;
            public StyleList<TimeValue> WrittenDuration;
            public StyleList<TimeValue> WrittenDelay;
            public StyleList<EasingFunction> WrittenCurve;
            // An enter and an exit can play on one element at once, and a cancelled exit's reversal carries its
            // play on. In start order: the last one times the element, and the saved timing goes back only once
            // none remains.
            public readonly List<Play> Plays = new();
        }

        private static readonly ConditionalWeakTable<VisualElement, Hold> s_holds = new();
        private static readonly ConditionalWeakTable<VisualElement, Hold>.CreateValueCallback s_save = Save;

        internal static Play Begin(VisualElement element)
        {
            var play = new Play();
            s_holds.GetValue(element, s_save).Plays.Add(play);
            return play;
        }

        private static Hold Save(VisualElement element)
        {
            var style = element.style;
            var hold = new Hold
            {
                Duration = Copy(style.transitionDuration),
                Delay = Copy(style.transitionDelay),
                Curve = Copy(style.transitionTimingFunction),
            };
            // MUTANT_SURVIVES(equivalent, line removed): ApplyTransitionStyles, Begin's only caller, writes timing
            // before anything else can, and that write's Adopt leaves each saved slot equal to the one read above.
            Record(style, hold);
            return hold;
        }

        internal static void Write(VisualElement element, List<TimeValue>? duration,
            List<EasingFunction>? curve, List<TimeValue>? delay)
        {
            var style = element.style;
            var holding = s_holds.TryGetValue(element, out var hold);
            if (holding) Adopt(style, hold);
            // Copies, so no slot is ever written from a list the scheduler pools or caches, whatever the engine
            // keeps of an assigned list.
            if (duration != null) style.transitionDuration = new List<TimeValue>(duration);
            if (curve != null) style.transitionTimingFunction = new List<EasingFunction>(curve);
            if (delay != null) style.transitionDelay = new List<TimeValue>(delay);
            if (!holding) return;
            Record(style, hold);
            var latest = hold.Plays[hold.Plays.Count - 1];
            (latest.Duration, latest.Delay, latest.Curve) = (hold.WrittenDuration, hold.WrittenDelay, hold.WrittenCurve);
        }

        internal static void End(VisualElement element, Play? play)
        {
            if (!s_holds.TryGetValue(element, out var hold) || !hold.Plays.Remove(play!)) return;
            var style = element.style;
            Adopt(style, hold);
            if (hold.Plays.Count > 0)
            {
                var latest = hold.Plays[hold.Plays.Count - 1];
                (style.transitionDuration, style.transitionDelay, style.transitionTimingFunction) =
                    (latest.Duration, latest.Delay, latest.Curve);
                Record(style, hold);
                return;
            }
            s_holds.Remove(element);
            (style.transitionDuration, style.transitionDelay, style.transitionTimingFunction) =
                (hold.Duration, hold.Delay, hold.Curve);
        }

        internal static void Forget(VisualElement element) => s_holds.Remove(element);

        // Only differences still observable from the last temporary value replace saved timing.
        private static void Adopt(IStyle style, Hold hold)
        {
            if (!Same(style.transitionDuration, hold.WrittenDuration)) hold.Duration = Copy(style.transitionDuration);
            if (!Same(style.transitionDelay, hold.WrittenDelay)) hold.Delay = Copy(style.transitionDelay);
            if (!Same(style.transitionTimingFunction, hold.WrittenCurve)) hold.Curve = Copy(style.transitionTimingFunction);
        }

        private static void Record(IStyle style, Hold hold)
        {
            (hold.WrittenDuration, hold.WrittenDelay, hold.WrittenCurve) =
                (Copy(style.transitionDuration), Copy(style.transitionDelay), Copy(style.transitionTimingFunction));
        }

        // Keep snapshots independent of the lists returned to the scheduler's pool.
        private static StyleList<T> Copy<T>(StyleList<T> list) =>
            list.value is { } value ? new StyleList<T>(new List<T>(value)) : list;

        private static bool Same<T>(StyleList<T> a, StyleList<T> b)
        {
            var (x, y) = (a.value, b.value);
            if (a.keyword != b.keyword || x?.Count != y?.Count) return false;
            // MUTANT_SURVIVES(equivalent, literal): a slot recorded with no list is one the hold already saved
            // under the same keyword, so adopting it again saves what the hold holds.
            if (x == null) return true;
            for (var i = 0; i < x.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(x[i], y![i])) return false;
            }
            return true;
        }
    }
}
