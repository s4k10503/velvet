#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The transitions UI Toolkit is running for an element, which its panel's StylePropertyAnimationSystem keeps in a
    // running set per value type internal to it, and so are read by reflection: when one started, how long it runs, its
    // curve, its eased progress, its start, current and end values, and what a reversal of it would run back to. Where
    // that cannot be read, a carry taking the element's value over starts again from where it stands.
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
                IndexOf = s_propertyId == null ? null
                    : Running?.FieldType.GetMethod("IndexOf", new[] { typeof(VisualElement), s_propertyId, typeof(int).MakeByRefType() });
                Timings = Running?.FieldType.GetField("timing");
                Styles = Running?.FieldType.GetField("style");
                var timing = Timings?.FieldType.GetElementType();
                var style = Styles?.FieldType.GetElementType();
                (StartTime, Duration, Curve, Shortening, Eased) = (timing?.GetField("startTime"), timing?.GetField("duration"),
                    timing?.GetField("easingCurve"), timing?.GetField("reversingShorteningFactor"), timing?.GetField("easedProgress"));
                (From, To, ReversingStart, Current) = (style?.GetField("startValue"), style?.GetField("endValue"),
                    style?.GetField("reversingAdjustedStartValue"), style?.GetField("currentValue"));
                Readable = Array.TrueForAll(
                    new MemberInfo?[] { Values, Running, IndexOf, Timings, Styles, StartTime, Duration, Curve, Shortening, Eased, From, To, ReversingStart, Current },
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
            public readonly FieldInfo? Eased;
            public readonly FieldInfo? From;
            public readonly FieldInfo? To;
            public readonly FieldInfo? ReversingStart;
            public readonly FieldInfo? Current;
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

        // A corner, in LayoutIdLook's corner order, as UI Toolkit draws it now: where a transition of it runs in the given
        // radius's unit, its current value, else that radius.
        public static Length CurrentRadius(VisualElement element, int corner, Length radius)
        {
            if (Find(element, s_lengths, s_corners[corner]) is not { } found) return radius;
            try
            {
                var current = (Length)s_lengths.Current!.GetValue(found.Style);
                return current.unit == radius.unit ? current : radius;
            }
            catch (Exception)
            {
                return radius;
            }
        }

        private static void TakeOver(VisualElement element, LayoutIdCarry carry, RunningSet set, string property, Func<object, float> read)
        {
            if (Find(element, set, property) is not { } found) return;
            // A member whose type or shape has changed under the read fails it as one gone does.
            try
            {
                var (system, timing, style) = found;
                var (from, to, reversingStart) = (read(set.From!.GetValue(style)), read(set.To!.GetValue(style)), read(set.ReversingStart!.GetValue(style)));
                if (float.IsNaN(from + to + reversingStart)) return;
                var elapsedSec = (double)s_now!.GetValue(system) - (double)set.StartTime!.GetValue(timing);
                carry.TakeOver((from, to, (float)set.Duration!.GetValue(timing), (float)elapsedSec), (Func<float, float>)set.Curve!.GetValue(timing),
                    (reversingStart, (float)set.Shortening!.GetValue(timing), (float)set.Eased!.GetValue(timing)));
            }
            catch (Exception)
            {
            }
        }

        // The element's running transition of a property: the animation system, and the transition's timing and values.
        // Null where none runs or it cannot be read.
        private static (object System, object Timing, object Style)? Find(VisualElement element, RunningSet set, string property)
        {
            var system = set.Readable && s_now != null ? s_system?.GetValue(element.panel) : null;
            // MUTANT_SURVIVES(unreachable): the panels the tests mount run a StylePropertyAnimationSystem, whose members
            // all resolve on the editor this package declares.
            if (s_systemType?.IsInstanceOfType(system) != true) return null;
            if (!Enum.TryParse(s_propertyId!, property, out var id)) return null;
            var values = set.Values!.GetValue(system);
            // None until the panel first runs a transition of that value type.
            if (values == null) return null;
            try
            {
                var running = set.Running!.GetValue(values);
                (s_args[0], s_args[1], s_args[2]) = (element, id, null);
                if (set.IndexOf!.Invoke(running, s_args) is not true) return null;
                var index = (int)s_args[2]!;
                return (system!, ((Array)set.Timings!.GetValue(running)).GetValue(index), ((Array)set.Styles!.GetValue(running)).GetValue(index));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
