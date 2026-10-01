#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The style an element's rules cascade to beneath its inline values, which UI Toolkit caches under the element's
    // matchingRulesHash (StyleCache), internal to it and so read by reflection. Where that cannot be read, a reader
    // keeps the value it started from. Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_
    // Then_ItsOwnIsCarriedOnThatTransition fails when the read stops giving the cascaded value.
    internal static class StyleCascade
    {
        private static readonly FieldInfo? s_style =
            typeof(VisualElement).GetField("m_Style", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Type? s_type = s_style?.FieldType;
        private static readonly FieldInfo? s_hash = s_type?.GetField("matchingRulesHash");
        private static readonly MethodInfo? s_tryGet = s_type == null ? null
            : typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.StyleCache")?.GetMethod("TryGetValue",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(long), s_type.MakeByRefType() }, null);
        private static readonly PropertyInfo? s_opacity = s_type?.GetProperty("opacity");
        private static readonly PropertyInfo? s_property = s_type?.GetProperty("transitionProperty");
        private static readonly PropertyInfo? s_duration = s_type?.GetProperty("transitionDuration");
        private static readonly PropertyInfo? s_delay = s_type?.GetProperty("transitionDelay");
        private static readonly PropertyInfo? s_curve = s_type?.GetProperty("transitionTimingFunction");
        private static readonly PropertyInfo? s_rotate = s_type?.GetProperty("rotate");
        private static readonly PropertyInfo?[] s_radii =
        {
            s_type?.GetProperty("borderTopLeftRadius"), s_type?.GetProperty("borderTopRightRadius"),
            s_type?.GetProperty("borderBottomRightRadius"), s_type?.GetProperty("borderBottomLeftRadius"),
        };
        private static readonly bool s_readable = Array.TrueForAll(
            new MemberInfo?[] { s_style, s_hash, s_tryGet, s_opacity, s_property, s_duration, s_delay, s_curve, s_rotate, s_radii[0],
                s_radii[1], s_radii[2], s_radii[3] }, m => m != null);

        private static readonly object?[] s_args = new object?[2];

        // The element's cascaded opacity and the transition it runs opacity by (Transition). NaN and no transition
        // where the cached style cannot be read.
        public static (float Opacity, float DurationSec, float DelaySec, EasingMode Easing) Opacity(VisualElement element)
        {
            if (Style(element) is not { } style) return (float.NaN, 0f, 0f, EasingMode.Ease);
            var (durationSec, delaySec, easing) = Transition(element, style, "opacity", null);
            return ((float)s_opacity!.GetValue(style), durationSec, delaySec, easing);
        }

        // The transition the element runs a property by (Lists). None where the cached style cannot be read or names no
        // entry for the property.
        public static (float DurationSec, float DelaySec, EasingMode Easing) Transition(VisualElement element, string property,
            string? shorthand) =>
            Style(element) is { } style ? Transition(element, style, property, shorthand) : (0f, 0f, EasingMode.Ease);

        private static (float DurationSec, float DelaySec, EasingMode Easing) Transition(VisualElement element, object style,
            string property, string? shorthand) =>
            StyleFilterTransitionDriver.TryFindTransition(Lists(element, style), property, shorthand, out var durationMs, out var delayMs, out var easing)
                ? (durationMs / 1000f, delayMs / 1000f, easing)
                : (0f, 0f, EasingMode.Ease);

        // The lists the element runs its transitions by: the rules' transition-property with the element's own inline
        // duration, delay and curve lists wherever it holds them, as UI Toolkit combines the two, the inline
        // transition-property being a suspension's or a narrowing's (MotionNativeTransitionGuard). Null where the
        // cached style cannot be read.
        public static TransitionLists? Lists(VisualElement element) => Style(element) is { } style ? Lists(element, style) : null;

        private static TransitionLists Lists(VisualElement element, object style)
        {
            var (duration, delay, curve) = MotionNativeTransitionGuard.OwnTiming(element);
            return new TransitionLists(s_property!.GetValue(style) as List<StylePropertyName>,
                duration.keyword == StyleKeyword.Undefined ? duration.value : s_duration!.GetValue(style) as List<TimeValue>,
                delay.keyword == StyleKeyword.Undefined ? delay.value : s_delay!.GetValue(style) as List<TimeValue>,
                curve.keyword == StyleKeyword.Undefined ? curve.value : s_curve!.GetValue(style) as List<EasingFunction>);
        }

        // The key UI Toolkit caches the element's matched rules under, which changes with them; null where it cannot be
        // read.
        public static long? RulesHash(VisualElement element) => s_readable ? (long)s_hash!.GetValue(s_style!.GetValue(element)) : null;

        // A corner's radius as the rules declare it, in LayoutIdLook's corner order; null where the cached style cannot
        // be read.
        public static Length? Radius(VisualElement element, int corner) =>
            Style(element) is { } style ? (Length)s_radii[corner]!.GetValue(style) : null;

        // The rotate the rules declare, in degrees; NaN where the cached style cannot be read.
        public static float Rotate(VisualElement element) =>
            Style(element) is { } style ? ((Rotate)s_rotate!.GetValue(style)).angle.ToDegrees() : float.NaN;

        private static object? Style(VisualElement element)
        {
            // MUTANT_SURVIVES(equivalent): on the editor this package declares, every member resolves and this returns nothing.
            // Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_Then_ItsOwnIsCarriedOnThatTransition
            // fails where one does not.
            if (!s_readable) return null;
            s_args[0] = s_hash!.GetValue(s_style!.GetValue(element));
            s_args[1] = null;
            return s_tryGet!.Invoke(null, s_args) is true ? s_args[1] : null;
        }
    }
}
