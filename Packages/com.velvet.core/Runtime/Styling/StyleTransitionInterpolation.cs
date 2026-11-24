#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS's interpolation of the longhands UI Toolkit runs a transition of as a switch where CSS transitions them under
    /// <c>transition-behavior: normal</c>: <c>visibility</c>, which shows <c>visible</c> wherever a transition with a
    /// visible end has an eased progress strictly between 0 and 1, and <c>background-position-x</c> / <c>-y</c>, which
    /// slide. From a transition's run event (<see cref="StyleTransitionBehavior"/>), UI Toolkit eases it along a curve
    /// that wraps the one it would otherwise ease on.
    /// </summary>
    /// <remarks>
    /// The curve returns a progress on the side of a half that shows CSS's value, and for a slide writes CSS's position
    /// over the start UI Toolkit shows below a half. A reversal of a transition gets the shortening factor
    /// clamp01(|1 - (1 - eased progress) * factor|) from it, so where the progress returned is not the curve's own, the
    /// factor is rewritten to keep (1 - eased progress) * factor what the curve's own progress gives.
    /// TransitionInterpolationPanelTests fails when the side stops showing CSS's value, and its reversal cases when that
    /// product stops being kept.
    /// </remarks>
    internal static class StyleTransitionInterpolation
    {
        private static readonly StylePropertyName s_visibility = new("visibility");
        private static readonly StylePropertyName s_positionX = new("background-position-x");
        private static readonly StylePropertyName s_positionY = new("background-position-y");

        // A progress below a half, which shows a transition's start where its own progress is at or past a half.
        private const float BelowHalf = 0.49999997f;

        private static readonly PropertyInfo? s_system = EngineMember.PanelStyleAnimationSystem.ResolveProperty();
        private static readonly FieldInfo? s_systemTime = EngineMember.AnimationSystemTime.ResolveField();
        private static readonly FieldInfo? s_valuesByProperty = EngineMember.AnimationSystemValues.ResolveField();
        private static readonly PropertyInfo? s_propertyId = EngineMember.StylePropertyNameId.ResolveProperty();
        private static readonly MethodInfo? s_updateValues = EngineMember.AnimationValuesUpdateValues.ResolveMethod();
        private static readonly MethodInfo? s_updateComputedStyleAt =
            EngineMember.AnimationValuesUpdateComputedStyleAt.ResolveMethod();

        private static readonly ConditionalWeakTable<VisualElement, Shown> s_shown = new();

        internal static bool Interpolates(StylePropertyName property)
            => property == s_visibility || property == s_positionX || property == s_positionY;

        /// <summary>
        /// Has the transition UI Toolkit runs of the element's property, one <see cref="Interpolates"/> names, show CSS's
        /// value from the frame on screen on. A transition a member that does not resolve keeps from being read runs as it
        /// started.
        /// </summary>
        internal static void Take(VisualElement element, StylePropertyName property)
        {
            Restarted(element, property);
            if (s_updateValues == null || s_updateComputedStyleAt == null)
            {
                return;
            }
            try
            {
                if (Locate(element, property) is not { } entry || !entry.TryLocate(out var running, out var index))
                {
                    return;
                }
                var set = entry.Set;
                var timings = (Array)set.Timings.GetValue(running)!;
                var timing = timings.GetValue(index)!;
                var style = ((Array)set.Styles.GetValue(running)!).GetValue(index)!;
                // A run event can reach more than one hold on the element's way up: a portal row's and a mount root's
                // above the row's target.
                if (set.Curve.GetValue(timing) is not Func<float, float> current || current.Target is Run)
                {
                    return;
                }
                var startTime = (double)set.StartTime.GetValue(timing)!;
                var duration = (float)set.Duration.GetValue(timing)!;
                // The curve StyleEaseCurve gives such a run, which it leaves to this class to give.
                var curve = StyleEaseCurve.TakenOver(element, property.ToString(), entry.Id, startTime, current);
                var shortening = (float)set.Shortening.GetValue(timing)!;
                var run = Shape(property, new Run.Parts(entry, curve, shortening), set.Start.GetValue(style)!,
                    set.End.GetValue(style)!);
                var eases = run?.Curve ?? curve;
                if (ReferenceEquals(eases, current))
                {
                    return;
                }
                if (run is VisibilityRun visibility)
                {
                    startTime = visibility.Dated(set, timing, startTime);
                }
                set.Curve.SetValue(timing, eases);
                timings.SetValue(timing, index);
                var elapsed = (double)s_systemTime!.GetValue(entry.AnimationSystem)! - startTime;
                if (elapsed >= 0.0)
                {
                    // Eased once the slot holds the curve and read again after: Run.Write writes the factor into it. A
                    // start dated back can leave the run past its end, which UI Toolkit ends on its next update.
                    var eased = eases(Math.Min((float)elapsed / duration, 1f));
                    timing = timings.GetValue(index)!;
                    set.Eased.SetValue(timing, eased);
                    timings.SetValue(timing, index);
                }
                s_updateValues.Invoke(entry.Values, null);
                s_updateComputedStyleAt.Invoke(entry.Values, new object[] { index });
            }
            catch (Exception)
            {
            }
        }

        // The set UI Toolkit keeps the element's transitions of the property in, or null where a member does not resolve,
        // the element is on no panel or the panel has run none of them.
        private static Entry? Locate(VisualElement element, StylePropertyName property)
        {
            if (s_system == null || s_systemTime == null || s_valuesByProperty == null || s_propertyId == null
                || element.panel == null)
            {
                return null;
            }
            var system = s_system.GetValue(element.panel);
            return system != null && s_systemTime.DeclaringType!.IsInstanceOfType(system)
                && s_valuesByProperty.GetValue(system) is IDictionary byProperty
                && s_propertyId.GetValue(property) is { } id && byProperty[id] is { } values
                && Set.Of(values) is { } set
                    ? new Entry(element, system, values, id, set)
                    : null;
        }

        private static Run? Shape(StylePropertyName property, Run.Parts parts, object from, object to)
        {
            if (property == s_visibility)
            {
                return (int)to == (int)from ? null : new VisibilityRun(parts, (int)from, (int)to);
            }
            return new SlideRun(parts, new Slide((BackgroundPosition)from, (BackgroundPosition)to, property == s_positionY));
        }

        private abstract class Run
        {
            private readonly Parts _parts;
            private float _shortening;
            public readonly Func<float, float> Curve;

            protected Run(Parts parts)
            {
                _parts = parts;
                // MUTANT_SURVIVES(equivalent, line removed): left at zero, its extra writes put the slot's own factor back.
                _shortening = parts.Shortening;
                Curve = Ease;
            }

            // The transition, the curve it would otherwise ease on and the shortening factor it started with.
            public readonly struct Parts
            {
                public readonly Entry Entry;
                public readonly Func<float, float> Inner;
                public readonly float Shortening;

                public Parts(Entry entry, Func<float, float> inner, float shortening)
                {
                    Entry = entry;
                    Inner = inner;
                    Shortening = shortening;
                }
            }

            protected VisualElement Element => _parts.Entry.Element;

            protected float Shortening => _parts.Shortening;

            // UI Toolkit's time for the update the curve is eased in.
            protected double Now => (double)s_systemTime!.GetValue(_parts.Entry.AnimationSystem)!;

            protected abstract float Select(float progress);

            // The start UI Toolkit is to show at the progress, or null for the transition's own.
            protected virtual object? StartAt(float progress) => null;

            private void Write(float shortening, object? start)
            {
                if (!_parts.Entry.TryLocate(out var running, out var index))
                {
                    return;
                }
                var set = _parts.Entry.Set;
                var timings = (Array)set.Timings.GetValue(running)!;
                var timing = timings.GetValue(index)!;
                set.Shortening.SetValue(timing, shortening);
                timings.SetValue(timing, index);
                _shortening = shortening;
                if (start == null)
                {
                    return;
                }
                var styles = (Array)set.Styles.GetValue(running)!;
                var style = styles.GetValue(index)!;
                set.Start.SetValue(style, start);
                styles.SetValue(style, index);
            }

            // Called inside UI Toolkit's animation update, so a failure in what this adds to the wrapped curve eases on
            // that curve rather than escaping.
            private float Ease(float t)
            {
                var progress = _parts.Inner(t);
                try
                {
                    var selected = Select(progress);
                    var start = StartAt(progress);
                    var shortening = selected == progress
                        ? _parts.Shortening
                        : _parts.Shortening * (1f - progress) / (1f - selected);
                    if (start != null || shortening != _shortening)
                    {
                        Write(shortening, start);
                    }
                    return selected;
                }
                catch (Exception)
                {
                    return progress;
                }
            }
        }

        private sealed class VisibilityRun : Run
        {
            private readonly int _from;
            private readonly int _to;
            private readonly Shown _shown;
            private int _shows;

            public VisibilityRun(Parts parts, int from, int to) : base(parts)
            {
                _from = from;
                _to = to;
                _shows = from;
                _shown = s_shown.GetValue(parts.Entry.Element, static _ => new Shown());
            }

            // Visible strictly between the ends, the nearer end outside them.
            protected override float Select(float progress)
            {
                var selected = progress <= 0f || progress >= 1f ? progress
                    : _to == (int)Visibility.Visible ? Math.Max(progress, 0.5f) : Math.Min(progress, BelowHalf);
                var shows = selected < 0.5f ? _from : _to;
                if (shows != _shows)
                {
                    _shows = shows;
                    _shown.Changed(shows, Now);
                }
                return selected;
            }

            // The start the run eases from, written into timing: for a value inherited from an ancestor's visibility
            // transition, dated back to that transition's style change, where CSS starts a descendant's transition of
            // an inherited value with its ancestor's; UI Toolkit starts it once the ancestor shows the change.
            public double Dated(Set set, object timing, double startTime)
            {
                var delay = (float)set.Delay.GetValue(timing)!;
                var lag = Math.Min(delay, 0f) * Shortening + Math.Max(delay, 0f);
                var origin = Inherited(Element, _to, startTime, lag) ?? startTime - lag;
                _shown.Origin = origin;
                var start = origin + lag;
                set.StartTime.SetValue(timing, start);
                return start;
            }
        }

        // What an element's visibility transitions last showed, which a descendant's run of the change it inherits dates
        // itself by (Inherited): the style change the element's run came from, when what it shows last changed, and to
        // what.
        private sealed class Shown
        {
            public double Origin = double.NaN;
            public double ChangedAt = double.NaN;
            public int Value = -1;

            public void Changed(int value, double at)
            {
                Value = value;
                ChangedAt = at;
            }
        }

        // A new visibility run of the element: its style change is unknown until Dated gives it or Ended reads it back.
        private static void Restarted(VisualElement element, StylePropertyName property)
        {
            if (property == s_visibility)
            {
                s_shown.GetValue(element, static _ => new Shown()).Origin = double.NaN;
            }
        }

        /// <summary>
        /// Records what the element shows once a transition of the property ends, where that is a visibility its run
        /// had not shown, for a descendant's transition the change starts.
        /// </summary>
        internal static void Ended(VisualElement element, StylePropertyName property)
        {
            if (property != s_visibility || !s_shown.TryGetValue(element, out var shown))
            {
                return;
            }
            var value = (int)element.resolvedStyle.visibility;
            if (value == shown.Value)
            {
                return;
            }
            var now = Now(element);
            if (double.IsNaN(shown.Origin))
            {
                // A run that ended in the update its run event came in, which Take could not date: its style change is
                // taken as this update's time less the duration and delay the element's transition lists.
                var (duration, delay, _) = StyleCascade.Transition(element, "visibility", null);
                shown.Origin = now - duration - delay;
            }
            shown.Changed(value, now);
        }

        private static double Now(VisualElement element)
            => element.panel != null && s_system?.GetValue(element.panel) is { } system
                ? (double)s_systemTime!.GetValue(system)!
                : double.NaN;

        // The style change of the nearest ancestor whose own visibility transition changed what it shows to value in the
        // update the element's run began after, its start lag past that update's time: a change the element inherits.
        private static double? Inherited(VisualElement element, int value, double startTime, float lag)
        {
            for (var ancestor = element.hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (!s_shown.TryGetValue(ancestor, out var shown) || shown.Value != value || double.IsNaN(shown.Origin))
                {
                    continue;
                }
                // Exact, as UI Toolkit takes a run's start as the same sum of its last update's time and the lag; the
                // inherited-run cases of TransitionInterpolationPanelTests fail where it stops doing so.
                if (shown.ChangedAt + lag == startTime)
                {
                    return shown.Origin;
                }
            }
            return null;
        }

        private sealed class SlideRun : Run
        {
            private readonly Slide _slide;

            public SlideRun(Parts parts, Slide slide) : base(parts) => _slide = slide;

            protected override float Select(float progress) => Math.Min(progress, BelowHalf);

            protected override object? StartAt(float progress) => _slide.At(progress, Element);
        }

        // Both ends of a slide as CSS interpolates a position: a percentage of the room the image leaves plus a length,
        // both from the left or top edge.
        private readonly struct Slide
        {
            private readonly float _fromPercent;
            private readonly float _fromPixels;
            private readonly float _toPercent;
            private readonly float _toPixels;
            private readonly bool _vertical;

            public Slide(BackgroundPosition from, BackgroundPosition to, bool vertical)
            {
                EdgeTerms(from, out _fromPercent, out _fromPixels);
                EdgeTerms(to, out _toPercent, out _toPixels);
                _vertical = vertical;
            }

            // In the unit both ends share where they share one, which follows the element's size as it changes; a value
            // holding both terms goes in pixels from the left or top edge, against the room on the frame it is written for.
            public BackgroundPosition At(float progress, VisualElement element)
            {
                var percent = Mathf.LerpUnclamped(_fromPercent, _toPercent, progress);
                var pixels = Mathf.LerpUnclamped(_fromPixels, _toPixels, progress);
                if (_fromPixels == 0f && _toPixels == 0f)
                {
                    return new BackgroundPosition(_vertical ? BackgroundPositionKeyword.Top : BackgroundPositionKeyword.Left,
                        new Length(percent, LengthUnit.Percent));
                }
                if (_fromPercent == 100f && _toPercent == 100f)
                {
                    return new BackgroundPosition(_vertical ? BackgroundPositionKeyword.Bottom : BackgroundPositionKeyword.Right,
                        new Length(-pixels));
                }
                return new BackgroundPosition(_vertical ? BackgroundPositionKeyword.Top : BackgroundPositionKeyword.Left,
                    new Length(BackgroundTile.Room(element, _vertical) * percent / 100f + pixels));
            }

            private static void EdgeTerms(BackgroundPosition position, out float percent, out float pixels)
            {
                if (position.keyword == BackgroundPositionKeyword.Center)
                {
                    (percent, pixels) = (50f, 0f);
                    return;
                }
                var farEdge = position.keyword == BackgroundPositionKeyword.Right
                    || position.keyword == BackgroundPositionKeyword.Bottom;
                var offset = position.offset;
                if (offset.unit == LengthUnit.Percent)
                {
                    (percent, pixels) = (farEdge ? 100f - offset.value : offset.value, 0f);
                    return;
                }
                var length = offset.unit == LengthUnit.Pixel ? offset.value : 0f;
                (percent, pixels) = farEdge ? (100f, -length) : (0f, length);
            }
        }

        // Where UI Toolkit keeps one element's transition of one property.
        private sealed class Entry
        {
            private readonly object?[] _arguments = new object?[3];
            public readonly VisualElement Element;
            public readonly object AnimationSystem;
            public readonly object Values;
            public readonly object Id;
            public readonly Set Set;

            public Entry(VisualElement element, object system, object values, object id, Set set)
            {
                Element = element;
                AnimationSystem = system;
                Values = values;
                Id = id;
                Set = set;
            }

            public bool TryLocate(out object running, out int index)
            {
                running = Set.Running.GetValue(Values)!;
                (_arguments[0], _arguments[1], _arguments[2]) = (Element, Id, null);
                var found = Set.IndexOf.Invoke(running, _arguments) is true;
                index = found ? (int)_arguments[2]! : -1;
                return found;
            }
        }

        // The members of the set one value type's transitions are kept in, closed over that type.
        private sealed class Set
        {
            private static readonly FieldInfo? s_running = EngineMember.AnimationValuesRunning.ResolveField();
            private static readonly Dictionary<Type, Set?> s_closed = new();

            public readonly FieldInfo Running;
            public readonly MethodInfo IndexOf;
            public readonly FieldInfo Timings;
            public readonly FieldInfo Styles;
            public readonly FieldInfo StartTime;
            public readonly FieldInfo Duration;
            public readonly FieldInfo Curve;
            public readonly FieldInfo Eased;
            public readonly FieldInfo Shortening;
            public readonly FieldInfo Delay;
            public readonly FieldInfo Start;
            public readonly FieldInfo End;

            // Throws where a member does not resolve or is not one of values', the Values<T> they are closed over.
            private Set(FieldInfo running, Type values)
            {
                Running = NativeTransitionVelocity.Closed(running, values);
                var set = Running.FieldType;
                IndexOf = NativeTransitionVelocity.Closed(Resolved(EngineMember.AnimationDataSetIndexOf.ResolveMethod()),
                    set);
                Timings = Closed(EngineMember.AnimationDataSetTiming, set);
                Styles = Closed(EngineMember.AnimationDataSetStyle, set);
                var timing = Timings.FieldType.GetElementType()!;
                var style = Styles.FieldType.GetElementType()!;
                StartTime = Closed(EngineMember.AnimationTimingStart, timing);
                Duration = Closed(EngineMember.AnimationTimingDuration, timing);
                Curve = Closed(EngineMember.AnimationTimingEasing, timing);
                Eased = Closed(EngineMember.AnimationTimingEased, timing);
                Shortening = Closed(EngineMember.AnimationTimingReversingShortening, timing);
                Delay = Closed(EngineMember.AnimationTimingDelay, timing);
                Start = Closed(EngineMember.AnimationStyleStart, style);
                End = Closed(EngineMember.AnimationStyleEnd, style);
            }

            private static FieldInfo Closed(EngineMember member, Type type)
                => NativeTransitionVelocity.Closed(Resolved(member.ResolveField()), type);

            private static T Resolved<T>(T? member) where T : MemberInfo
                => member ?? throw new MissingMemberException(nameof(StyleTransitionInterpolation));

            public static Set? Of(object values)
            {
                var type = values.GetType();
                if (!s_closed.TryGetValue(type, out var set))
                {
                    s_closed[type] = set = Close(type);
                }
                return set;
            }

            private static Set? Close(Type type)
            {
                for (var values = type; s_running != null && values != null; values = values.BaseType)
                {
                    if (!values.IsGenericType || values.GetGenericTypeDefinition() != s_running.DeclaringType)
                    {
                        continue;
                    }
                    try
                    {
                        return new Set(s_running, values);
                    }
                    catch (Exception exception) when (exception is ArgumentException or MissingMemberException)
                    {
                        break;
                    }
                }
                return null;
            }
        }
    }
}
