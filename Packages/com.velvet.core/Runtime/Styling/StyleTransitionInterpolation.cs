#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// CSS's interpolation of the two longhands UI Toolkit runs a transition of as a switch where CSS transitions them
    /// under <c>transition-behavior: normal</c>: <c>visibility</c>, visible for the whole of a transition either end of
    /// which is visible, and <c>background-position-x</c> / <c>-y</c>, which slide. From a transition's run event
    /// (<see cref="StyleTransitionBehavior"/>), UI Toolkit eases it along a curve that wraps the one it would otherwise
    /// ease on.
    /// </summary>
    /// <remarks>
    /// The curve returns a progress on the side of a half that shows CSS's value, and for a slide writes CSS's position
    /// over the start UI Toolkit shows below a half. A reversal is shortened by the reversed transition's eased progress
    /// times its shortening factor, so where the progress returned is not the curve's own, the factor is rewritten to
    /// keep that product. TransitionInterpolationPanelTests fails when the side stops showing CSS's value, and its
    /// reversal cases when the product stops being kept.
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

        internal static bool Interpolates(StylePropertyName property)
            => property == s_visibility || property == s_positionX || property == s_positionY;

        /// <summary>
        /// Has the transition UI Toolkit runs of the element's property, one <see cref="Interpolates"/> names, show CSS's
        /// value from the frame on screen on. A slide CSS runs through a length plus a percentage, which a
        /// <see cref="BackgroundPosition"/> cannot hold, is switched as UI Toolkit switches it, on StyleEaseCurve's curve;
        /// a transition a member that does not resolve keeps from being read runs as it started.
        /// </summary>
        internal static void Take(VisualElement element, StylePropertyName property)
        {
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
                // A run event reaches every hold on the element's way up, which can be more than one: a portal row's and
                // a mount root's above the row's target.
                if (set.Curve.GetValue(timing) is not Func<float, float> current || current.Target is Run)
                {
                    return;
                }
                var startTime = (double)set.StartTime.GetValue(timing)!;
                var duration = (float)set.Duration.GetValue(timing)!;
                // The curve StyleEaseCurve gives such a run, which it leaves to this class to give.
                var curve = StyleEaseCurve.TakenOver(element, property.ToString(), entry.Id, startTime, current);
                var shortening = (float)set.Shortening.GetValue(timing)!;
                var eases = Shape(property, new Run.Parts(entry, curve, shortening), set.Start.GetValue(style)!,
                    set.End.GetValue(style)!)?.Curve ?? curve;
                if (ReferenceEquals(eases, current))
                {
                    return;
                }
                set.Curve.SetValue(timing, eases);
                timings.SetValue(timing, index);
                var elapsed = (double)s_systemTime!.GetValue(entry.AnimationSystem)! - startTime;
                if (elapsed >= 0.0)
                {
                    // Eased once the slot holds the curve and read again after: Run.Write writes the factor into it.
                    var eased = eases((float)elapsed / duration);
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
                var toVisible = (int)to == (int)Visibility.Visible;
                return toVisible == ((int)from == (int)Visibility.Visible) ? null : new VisibilityRun(parts, toVisible);
            }
            return Slide.Of((BackgroundPosition)from, (BackgroundPosition)to, property == s_positionY) is { } slide
                ? new SlideRun(parts, slide)
                : null;
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

            protected abstract float Select(float progress);

            // The start UI Toolkit is to show at the progress, or null for the transition's own.
            protected virtual object? StartAt(float progress) => null;

            // Inside UI Toolkit's animation update, so nothing escapes.
            private void Write(float shortening, object? start)
            {
                try
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
                catch (Exception)
                {
                }
            }

            private float Ease(float t)
            {
                var progress = _parts.Inner(t);
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
        }

        private sealed class VisibilityRun : Run
        {
            private readonly bool _toVisible;

            public VisibilityRun(Parts parts, bool toVisible) : base(parts) => _toVisible = toVisible;

            // Visible strictly between the ends, the nearer end outside them.
            protected override float Select(float progress)
                => progress <= 0f || progress >= 1f ? progress
                    : _toVisible ? Math.Max(progress, 0.5f) : Math.Min(progress, BelowHalf);
        }

        private sealed class SlideRun : Run
        {
            private readonly Slide _slide;

            public SlideRun(Parts parts, Slide slide) : base(parts) => _slide = slide;

            protected override float Select(float progress) => Math.Min(progress, BelowHalf);

            protected override object? StartAt(float progress) => _slide.At(progress);
        }

        // Both ends of a slide as an offset in one unit from one edge.
        private readonly struct Slide
        {
            private readonly BackgroundPositionKeyword _edge;
            private readonly LengthUnit _unit;
            private readonly float _from;
            private readonly float _to;

            private Slide(BackgroundPositionKeyword edge, LengthUnit unit, float from, float to)
            {
                _edge = edge;
                _unit = unit;
                _from = from;
                _to = to;
            }

            public BackgroundPosition At(float progress)
                => new(_edge, new Length(Mathf.LerpUnclamped(_from, _to, progress), _unit));

            // CSS interpolates a position as a percentage of the room the image leaves plus a length, both from the
            // left or top edge; null where a value between the ends needs both terms.
            public static Slide? Of(BackgroundPosition from, BackgroundPosition to, bool vertical)
            {
                EdgeTerms(from, out var fromPercent, out var fromPixels);
                EdgeTerms(to, out var toPercent, out var toPixels);
                var near = vertical ? BackgroundPositionKeyword.Top : BackgroundPositionKeyword.Left;
                if (fromPixels == 0f && toPixels == 0f)
                {
                    return new Slide(near, LengthUnit.Percent, fromPercent, toPercent);
                }
                if (fromPercent == 0f && toPercent == 0f)
                {
                    return new Slide(near, LengthUnit.Pixel, fromPixels, toPixels);
                }
                if (fromPercent == 100f && toPercent == 100f)
                {
                    var far = vertical ? BackgroundPositionKeyword.Bottom : BackgroundPositionKeyword.Right;
                    return new Slide(far, LengthUnit.Pixel, -fromPixels, -toPixels);
                }
                return null;
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
            private readonly VisualElement _element;
            private readonly object?[] _arguments = new object?[3];
            public readonly object AnimationSystem;
            public readonly object Values;
            public readonly object Id;
            public readonly Set Set;

            public Entry(VisualElement element, object system, object values, object id, Set set)
            {
                _element = element;
                AnimationSystem = system;
                Values = values;
                Id = id;
                Set = set;
            }

            public bool TryLocate(out object running, out int index)
            {
                running = Set.Running.GetValue(Values)!;
                (_arguments[0], _arguments[1], _arguments[2]) = (_element, Id, null);
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
