#nullable enable
using System;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The transitions UI Toolkit is running for an element, which its panel's StylePropertyAnimationSystem keeps in a
    // running set per value type internal to it, and so are read through EngineMember: when one started, how long it runs,
    // its curve, its eased progress, its start and end values, and what a reversal of it would run back to. Where
    // that cannot be read, a carry taking the element's value over starts again from where it stands.
    // Given_AMemberInsideTheDelayOfAFade_When_ANewLeadTakesTheId_Then_ItsOwnRunsAsTheEngineRunsIt fails when the read
    // stops giving the running transition.
    internal static class RunningStyleTransition
    {
        private static readonly PropertyInfo? s_system = EngineMember.PanelStyleAnimationSystem.ResolveProperty();
        private static readonly FieldInfo? s_now = EngineMember.AnimationSystemTime.ResolveField();
        private static readonly FieldInfo? s_floats = EngineMember.AnimationSystemFloats.ResolveField();
        // Declared on the generic definitions; read through the members of the constructed type a value is (Close).
        private static readonly FieldInfo? s_running = EngineMember.AnimationValuesRunning.ResolveField();
        private static readonly MethodInfo? s_indexOf = EngineMember.AnimationDataSetIndexOf.ResolveMethod();
        private static readonly FieldInfo? s_timings = EngineMember.AnimationDataSetTiming.ResolveField();
        private static readonly FieldInfo? s_styles = EngineMember.AnimationDataSetStyle.ResolveField();
        private static readonly FieldInfo? s_startTime = EngineMember.AnimationTimingStart.ResolveField();
        private static readonly FieldInfo? s_duration = EngineMember.AnimationTimingDuration.ResolveField();
        private static readonly FieldInfo? s_curve = EngineMember.AnimationTimingEasing.ResolveField();
        private static readonly FieldInfo? s_eased = EngineMember.AnimationTimingEased.ResolveField();
        private static readonly FieldInfo? s_shortening = EngineMember.AnimationTimingReversingShortening.ResolveField();
        private static readonly FieldInfo? s_from = EngineMember.AnimationStyleStart.ResolveField();
        private static readonly FieldInfo? s_to = EngineMember.AnimationStyleEnd.ResolveField();
        private static readonly FieldInfo? s_reversingStart = EngineMember.AnimationStyleReversingStart.ResolveField();
        private static readonly object? s_opacityId = EngineMember.OpacityPropertyId.ResolveField()?.GetValue(null);
        private static readonly bool s_readable = Array.TrueForAll(
            new MemberInfo?[]
            {
                s_system, s_now, s_floats, s_running, s_indexOf, s_timings, s_styles, s_startTime, s_duration, s_curve, s_eased,
                s_shortening, s_from, s_to, s_reversingStart,
            },
            m => m != null);

        private static readonly object?[] s_args = new object?[3];

        // Has the carry run on from the element's running opacity transition, if it has one.
        public static void TakeOverOpacity(VisualElement element, LayoutIdCarry carry) =>
            TakeOver(element, carry, s_floats, s_opacityId, value => (float)value);

        private static void TakeOver(VisualElement element, LayoutIdCarry carry, FieldInfo? set, object? property, Func<object, float> read)
        {
            if (Find(element, set, property) is not { } found) return;
            // A member whose type or shape has changed under the read fails it as one gone does.
            try
            {
                var (system, timing, style) = found;
                var (from, to, reversingStart) = (read(Read(s_from!, style)), read(Read(s_to!, style)), read(Read(s_reversingStart!, style)));
                if (!AllNumbers(from, to, reversingStart)) return;
                var elapsedSec = (double)s_now!.GetValue(system) - (double)Read(s_startTime!, timing);
                carry.TakeOver((from, to, (float)Read(s_duration!, timing), (float)elapsedSec), (Func<float, float>)Read(s_curve!, timing),
                    (reversingStart, (float)Read(s_shortening!, timing), (float)Read(s_eased!, timing)));
            }
            catch (Exception)
            {
            }
        }

        // The element's running transition of a property: the animation system, and the transition's timing and values.
        // Null where none runs or it cannot be read.
        private static (object System, object Timing, object Style)? Find(VisualElement element, FieldInfo? set, object? property)
        {
            if (!s_readable || set == null || property == null || element.panel == null) return null;
            try
            {
                var system = s_system!.GetValue(element.panel);
                // MUTANT_SURVIVES(unreachable): the panels the tests mount run a StylePropertyAnimationSystem, whose members
                // all resolve on the editor this package declares.
                if (system == null || !s_now!.DeclaringType!.IsInstanceOfType(system)) return null;
                var values = set.GetValue(system);
                // None until the panel first runs a transition of that value type.
                if (values == null) return null;
                var running = Read(s_running!, values);
                (s_args[0], s_args[1], s_args[2]) = (element, property, null);
                if (Close(s_indexOf!, running.GetType()) is not MethodInfo indexOf || indexOf.Invoke(running, s_args) is not true) return null;
                var index = (int)s_args[2]!;
                return (system, ((Array)Read(s_timings!, running)).GetValue(index), ((Array)Read(s_styles!, running)).GetValue(index));
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool AllNumbers(float from, float to, float reversingStart) =>
            !float.IsNaN(from) && !float.IsNaN(to) && !float.IsNaN(reversingStart);

        private static object Read(FieldInfo field, object owner) => ((FieldInfo)Close(field, owner.GetType())).GetValue(owner);

        // The member as the constructed type the value is, or one it derives from, declares it. Every member read here is
        // declared on a generic definition (EngineMember).
        private static MemberInfo Close(MemberInfo member, Type type)
        {
            var definition = member.DeclaringType!;
            for (var constructed = type; constructed != null; constructed = constructed.BaseType)
            {
                if (!constructed.IsGenericType || constructed.GetGenericTypeDefinition() != definition) continue;
                return member is FieldInfo field
                    ? NativeTransitionVelocity.Closed(field, constructed)
                    : NativeTransitionVelocity.Closed((MethodInfo)member, constructed);
            }
            throw new InvalidOperationException($"{type} is not constructed from {definition}");
        }
    }
}
