#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The transitions UI Toolkit is running for an element, which its panel's StylePropertyAnimationSystem keeps in a
    // running set per value type internal to it, and so are read by reflection: when one started, how long it runs, its
    // curve, its start and end, and what a reversal of it would run back to. Where that cannot be read, a carry taking
    // the element's value over starts again from where it stands.
    // Given_AMemberInsideTheDelayOfAFade_When_ANewLeadTakesTheId_Then_ItsOwnRunsAsTheEngineRunsIt fails when the read
    // stops giving the running transition.
    internal static class RunningStyleTransition
    {
        // The members of one value type's running set.
        private sealed class RunningSet
        {
            public RunningSet(string valuesField)
            {
                Values = s_systemType?.GetField(valuesField, BindingFlags.NonPublic | BindingFlags.Instance);
                Running = Values?.FieldType.GetField("running");
                IndexOf = Running?.FieldType.GetMethod("IndexOf");
                Timings = Running?.FieldType.GetField("timing");
                Styles = Running?.FieldType.GetField("style");
                var timing = Timings?.FieldType.GetElementType();
                var style = Styles?.FieldType.GetElementType();
                (StartTime, Duration, Curve, Shortening) = (timing?.GetField("startTime"), timing?.GetField("duration"),
                    timing?.GetField("easingCurve"), timing?.GetField("reversingShorteningFactor"));
                (From, To, ReversingStart) = (style?.GetField("startValue"), style?.GetField("endValue"),
                    style?.GetField("reversingAdjustedStartValue"));
                Readable = Array.TrueForAll(
                    new MemberInfo?[] { Values, Running, IndexOf, Timings, Styles, StartTime, Duration, Curve, Shortening, From, To, ReversingStart },
                    m => m != null);
            }

            public readonly FieldInfo? Values;
            public readonly FieldInfo? Running;
            public readonly MethodInfo? IndexOf;
            public readonly FieldInfo? Timings;
            public readonly FieldInfo? Styles;
            public readonly FieldInfo? StartTime;
            public readonly FieldInfo? Duration;
            public readonly FieldInfo? Curve;
            public readonly FieldInfo? Shortening;
            public readonly FieldInfo? From;
            public readonly FieldInfo? To;
            public readonly FieldInfo? ReversingStart;
            public readonly bool Readable;
        }

        private static readonly Assembly s_engine = typeof(VisualElement).Assembly;
        private static readonly PropertyInfo? s_system = s_engine.GetType("UnityEngine.UIElements.BaseVisualElementPanel")
            ?.GetProperty("styleAnimationSystem", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Type? s_systemType = s_engine.GetType("UnityEngine.UIElements.StylePropertyAnimationSystem");
        private static readonly FieldInfo? s_now = s_systemType?.GetField("m_CurrentTime", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly Type? s_propertyId = s_engine.GetType("UnityEngine.UIElements.StyleSheets.StylePropertyId");
        private static readonly RunningSet s_floats = new("m_Floats");
        private static readonly RunningSet s_rotates = new("m_Rotate");
        private static readonly RunningSet s_lengths = new("m_Lengths");
        private static readonly string[] s_corners =
            { "BorderTopLeftRadius", "BorderTopRightRadius", "BorderBottomRightRadius", "BorderBottomLeftRadius" };

        private static readonly object?[] s_args = new object?[3];

        // Has the carry run on from the element's running opacity transition, if it has one.
        public static void TakeOverOpacity(VisualElement element, LayoutIdCarry carry) =>
            TakeOver(element, carry, s_floats, "Opacity", value => (float)value);

        public static void TakeOverRotate(VisualElement element, LayoutIdCarry carry) =>
            TakeOver(element, carry, s_rotates, "Rotate", value => ((Rotate)value).angle.ToDegrees());

        // A corner, in LayoutIdLook's corner order, carried in the given unit; one running in another is not taken over.
        public static void TakeOverRadius(VisualElement element, int corner, LayoutIdCarry carry, LengthUnit unit) =>
            TakeOver(element, carry, s_lengths, s_corners[corner], value => ((Length)value).unit == unit ? ((Length)value).value : float.NaN);

        private static void TakeOver(VisualElement element, LayoutIdCarry carry, RunningSet set, string property, Func<object, float> read)
        {
            var system = set.Readable && s_now != null && s_propertyId != null ? s_system?.GetValue(element.panel) : null;
            // MUTANT_SURVIVES(unreachable): the panels the tests mount run a StylePropertyAnimationSystem, whose members
            // all resolve on the editor this package declares.
            if (s_systemType?.IsInstanceOfType(system) != true) return;
            var values = set.Values!.GetValue(system);
            // None until the panel first runs a transition of that value type.
            if (values == null) return;
            var running = set.Running!.GetValue(values);
            (s_args[0], s_args[1], s_args[2]) = (element, Enum.Parse(s_propertyId!, property), null);
            if (set.IndexOf!.Invoke(running, s_args) is not true) return;
            var index = (int)s_args[2]!;
            var timing = ((Array)set.Timings!.GetValue(running)).GetValue(index);
            var style = ((Array)set.Styles!.GetValue(running)).GetValue(index);
            var (from, to, reversingStart) = (read(set.From!.GetValue(style)), read(set.To!.GetValue(style)), read(set.ReversingStart!.GetValue(style)));
            if (float.IsNaN(from + to + reversingStart)) return;
            var elapsedSec = (double)s_now!.GetValue(system) - (double)set.StartTime!.GetValue(timing);
            carry.TakeOver((from, to, (float)set.Duration!.GetValue(timing), (float)elapsedSec), (Func<float, float>)set.Curve!.GetValue(timing),
                (reversingStart, (float)set.Shortening!.GetValue(timing)));
        }
    }
}
