using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static class MotionTweenTiming
    {
        // One layer of an element's timing: the element's own, which sets all three slots, or one tween's, which
        // sets those the tween wrote and no code has written since. A null slot is one the layer leaves alone.
        internal sealed class Play
        {
            public StyleList<TimeValue>? Duration;
            public StyleList<TimeValue>? Delay;
            public StyleList<EasingFunction>? Curve;
        }

        private sealed class Hold
        {
            public readonly Play Own = new();
            public StyleList<TimeValue> WrittenDuration;
            public StyleList<TimeValue> WrittenDelay;
            public StyleList<EasingFunction> WrittenCurve;
            // In start order. An enter and an exit can play on one element at once, and a cancelled exit's
            // reversal carries its play on.
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
            var hold = new Hold();
            (hold.Own.Duration, hold.Own.Delay, hold.Own.Curve) =
                (Copy(style.transitionDuration), Copy(style.transitionDelay), Copy(style.transitionTimingFunction));
            (hold.WrittenDuration, hold.WrittenDelay, hold.WrittenCurve) =
                (hold.Own.Duration.Value, hold.Own.Delay.Value, hold.Own.Curve.Value);
            return hold;
        }

        // The latest play's layer takes a write whichever play its caller acts for: these lists pair positionally
        // with transition-property, which is not layered, so only the lists on screen can match it.
        internal static void Write(VisualElement element, List<TimeValue>? duration,
            List<EasingFunction>? curve, List<TimeValue>? delay)
        {
            var hold = s_holds.GetValue(element, s_save);
            Adopt(element.style, hold);
            var layer = hold.Plays.Count > 0 ? hold.Plays[hold.Plays.Count - 1] : hold.Own;
            // Copies, so no slot is ever written from a list the scheduler pools or caches, whatever the engine
            // keeps of an assigned list.
            if (duration != null) layer.Duration = new StyleList<TimeValue>(new List<TimeValue>(duration));
            if (curve != null) layer.Curve = new StyleList<EasingFunction>(new List<EasingFunction>(curve));
            if (delay != null) layer.Delay = new StyleList<TimeValue>(new List<TimeValue>(delay));
            Apply(element.style, hold);
        }

        internal static void End(VisualElement element, Play? play)
        {
            if (!s_holds.TryGetValue(element, out var hold) || !hold.Plays.Remove(play!)) return;
            Adopt(element.style, hold);
            Apply(element.style, hold);
        }

        internal static bool Playing(VisualElement element) =>
            s_holds.TryGetValue(element, out var hold) && hold.Plays.Count > 0;

        internal static void Forget(VisualElement element) => s_holds.Remove(element);

        // A slot that differs from the value this class last wrote there is code's: it becomes the element's own, and
        // the tweens already playing stop supplying that slot until one of them writes it again.
        private static void Adopt(IStyle style, Hold hold)
        {
            if (!Same(style.transitionDuration, hold.WrittenDuration))
            {
                hold.Own.Duration = Copy(style.transitionDuration);
                foreach (var play in hold.Plays) play.Duration = null;
            }
            if (!Same(style.transitionDelay, hold.WrittenDelay))
            {
                hold.Own.Delay = Copy(style.transitionDelay);
                foreach (var play in hold.Plays) play.Delay = null;
            }
            if (!Same(style.transitionTimingFunction, hold.WrittenCurve))
            {
                hold.Own.Curve = Copy(style.transitionTimingFunction);
                foreach (var play in hold.Plays) play.Curve = null;
            }
        }

        private static void Apply(IStyle style, Hold hold)
        {
            var (duration, delay, curve) = (hold.Own.Duration!.Value, hold.Own.Delay!.Value, hold.Own.Curve!.Value);
            foreach (var play in hold.Plays)
            {
                duration = play.Duration ?? duration;
                delay = play.Delay ?? delay;
                curve = play.Curve ?? curve;
            }
            (style.transitionDuration, style.transitionDelay, style.transitionTimingFunction) = (duration, delay, curve);
            // Shares the layers' lists, which nothing changes once made.
            (hold.WrittenDuration, hold.WrittenDelay, hold.WrittenCurve) = (duration, delay, curve);
        }

        private static StyleList<T> Copy<T>(StyleList<T> list) =>
            list.value is { } value ? new StyleList<T>(new List<T>(value)) : list;

        private static bool Same<T>(StyleList<T> a, StyleList<T> b)
        {
            var (x, y) = (a.value, b.value);
            if (a.keyword != b.keyword || x?.Count != y?.Count) return false;
            for (var i = 0; i < x?.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(x![i], y![i])) return false;
            }
            return true;
        }
    }
}
