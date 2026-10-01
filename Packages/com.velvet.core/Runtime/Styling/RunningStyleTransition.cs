#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The opacity transition UI Toolkit is running for an element, which its panel's StylePropertyAnimationSystem keeps
    // in a running set internal to it and so is read by reflection: when it started, how long it runs, its curve, its
    // start and end, and what a reversal of it would run back to. Where that cannot be read, a carry taking the
    // element's opacity over starts again from where the element is resolved.
    // Given_AMemberInsideTheDelayOfAFade_When_ANewLeadTakesTheId_Then_ItsOwnRunsAsTheEngineRunsIt fails when the read
    // stops giving the running transition.
    internal static class RunningStyleTransition
    {
        private static readonly Assembly s_engine = typeof(VisualElement).Assembly;
        private static readonly PropertyInfo? s_system = s_engine.GetType("UnityEngine.UIElements.BaseVisualElementPanel")
            ?.GetProperty("styleAnimationSystem", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Type? s_systemType = s_engine.GetType("UnityEngine.UIElements.StylePropertyAnimationSystem");
        private static readonly FieldInfo? s_floats = s_systemType?.GetField("m_Floats", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? s_now = s_systemType?.GetField("m_CurrentTime", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? s_running = s_floats?.FieldType.GetField("running");
        private static readonly Type? s_propertyId = s_engine.GetType("UnityEngine.UIElements.StyleSheets.StylePropertyId");
        private static readonly MethodInfo? s_indexOf = s_propertyId == null ? null
            : s_running?.FieldType.GetMethod("IndexOf", new[] { typeof(VisualElement), s_propertyId, typeof(int).MakeByRefType() });
        private static readonly FieldInfo? s_timings = s_running?.FieldType.GetField("timing");
        private static readonly FieldInfo? s_styles = s_running?.FieldType.GetField("style");
        private static readonly Type? s_timingType = s_timings?.FieldType.GetElementType();
        private static readonly Type? s_styleType = s_styles?.FieldType.GetElementType();
        private static readonly FieldInfo? s_startTime = s_timingType?.GetField("startTime");
        private static readonly FieldInfo? s_duration = s_timingType?.GetField("duration");
        private static readonly FieldInfo? s_curve = s_timingType?.GetField("easingCurve");
        private static readonly FieldInfo? s_shortening = s_timingType?.GetField("reversingShorteningFactor");
        private static readonly FieldInfo? s_eased = s_timingType?.GetField("easedProgress");
        private static readonly FieldInfo? s_from = s_styleType?.GetField("startValue");
        private static readonly FieldInfo? s_to = s_styleType?.GetField("endValue");
        private static readonly FieldInfo? s_reversingStart = s_styleType?.GetField("reversingAdjustedStartValue");
        private static readonly object? s_opacity = OpacityId();
        private static readonly bool s_readable = Array.TrueForAll(
            new[] { s_system, s_floats, s_now, s_running, s_indexOf, s_timings, s_styles, s_startTime, s_duration, s_curve, s_shortening,
                s_eased, s_from, s_to, s_reversingStart, s_opacity }, m => m != null);

        private static object? OpacityId() => s_propertyId != null && Enum.TryParse(s_propertyId, "Opacity", out var opacity) ? opacity : null;

        private static readonly object?[] s_args = new object?[3];

        // Has the carry run on from the element's running opacity transition, if it has one.
        public static void TakeOverOpacity(VisualElement element, LayoutIdCarry carry)
        {
            var system = s_readable ? s_system!.GetValue(element.panel) : null;
            // MUTANT_SURVIVES(unreachable): the panels the tests mount run a StylePropertyAnimationSystem, whose members
            // all resolve on the editor this package declares.
            if (s_systemType?.IsInstanceOfType(system) != true) return;
            var floats = s_floats!.GetValue(system);
            // None until the panel first runs a float transition.
            if (floats == null) return;
            // A member whose type or shape has changed under the read fails it as one gone does.
            try
            {
                var running = s_running!.GetValue(floats);
                (s_args[0], s_args[1], s_args[2]) = (element, s_opacity, null);
                if (s_indexOf!.Invoke(running, s_args) is not true) return;
                var index = (int)s_args[2]!;
                var timing = ((Array)s_timings!.GetValue(running)).GetValue(index);
                var style = ((Array)s_styles!.GetValue(running)).GetValue(index);
                var elapsedSec = (double)s_now!.GetValue(system) - (double)s_startTime!.GetValue(timing);
                carry.TakeOver(((float)s_from!.GetValue(style), (float)s_to!.GetValue(style), (float)s_duration!.GetValue(timing), (float)elapsedSec),
                    (Func<float, float>)s_curve!.GetValue(timing),
                    ((float)s_reversingStart!.GetValue(style), (float)s_shortening!.GetValue(timing), (float)s_eased!.GetValue(timing)));
            }
            catch (Exception)
            {
            }
        }
    }
}
