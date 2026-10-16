#nullable enable
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The rate a native transition is moving a style at, read off the animation system UI Toolkit keeps for the
    /// panel, which a spring that interrupts the transition starts with as Framer Motion starts a value's next
    /// animation with the value's velocity. A member that does not resolve reads as no velocity.
    /// </summary>
    internal static class NativeTransitionVelocity
    {
        private const double SlopeStep = 1.0 / 1000.0;

        private static readonly PropertyInfo? s_system = EngineMember.PanelStyleAnimationSystem.ResolveProperty();
        private static readonly FieldInfo? s_valuesByProperty = EngineMember.AnimationSystemValues.ResolveField();
        private static readonly FieldInfo? s_time = EngineMember.AnimationSystemTime.ResolveField();
        private static readonly FieldInfo? s_running = EngineMember.AnimationValuesRunning.ResolveField();
        private static readonly MethodInfo? s_indexOf = EngineMember.AnimationDataSetIndexOf.ResolveMethod();
        private static readonly FieldInfo? s_timing = EngineMember.AnimationDataSetTiming.ResolveField();
        private static readonly FieldInfo? s_style = EngineMember.AnimationDataSetStyle.ResolveField();
        private static readonly FieldInfo? s_start = EngineMember.AnimationTimingStart.ResolveField();
        private static readonly FieldInfo? s_duration = EngineMember.AnimationTimingDuration.ResolveField();
        private static readonly FieldInfo? s_easing = EngineMember.AnimationTimingEasing.ResolveField();
        private static readonly FieldInfo? s_from = EngineMember.AnimationStyleStart.ResolveField();
        private static readonly FieldInfo? s_to = EngineMember.AnimationStyleEnd.ResolveField();

        internal static bool Resolves => s_system != null && s_valuesByProperty != null && s_time != null
            && s_running != null && s_indexOf != null && s_timing != null && s_style != null && s_start != null
            && s_duration != null && s_easing != null && s_from != null && s_to != null;

        /// <summary>
        /// The transition of <paramref name="propertyId"/> (a boxed <c>StylePropertyId</c>) on <paramref name="owner"/>
        /// as a rate of <paramref name="magnitude"/> of its start and end values per second, or null when none is
        /// running or the engine does not give it up. Zero before its delay has passed and after its end.
        /// </summary>
        internal static float? Of(VisualElement owner, object propertyId, Func<object, float?> magnitude)
        {
            if (!Resolves || owner.panel == null) return null;
            try
            {
                return Locate(owner, propertyId) is { } running ? RateOf(running, magnitude) : null;
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException or InvalidCastException
                or NullReferenceException)
            {
                return null;
            }
        }

        // The animation system's record of the transition: the timing and style entries and the time it last updated.
        private sealed record Running(object Timing, object Style, double Now);

        private static Running? Locate(VisualElement owner, object propertyId)
        {
            var system = s_system!.GetValue(owner.panel);
            if (system == null || s_valuesByProperty!.GetValue(system) is not IDictionary byProperty
                || !byProperty.Contains(propertyId)) return null;
            var values = byProperty[propertyId]!;
            var running = Closed(s_running!, ClosedValues(values)).GetValue(values);
            if (running == null) return null;
            var set = running.GetType();
            var args = new object[] { owner, propertyId, 0 };
            if (!(bool)Closed(s_indexOf!, set).Invoke(running, args)) return null;
            var index = (int)args[2];
            return new Running(
                ((Array)Closed(s_timing!, set).GetValue(running)!).GetValue(index)!,
                ((Array)Closed(s_style!, set).GetValue(running)!).GetValue(index)!,
                (double)s_time!.GetValue(system)!);
        }

        private static float? RateOf(Running running, Func<object, float?> magnitude)
        {
            var timingType = running.Timing.GetType();
            var startTime = (double)Closed(s_start!, timingType).GetValue(running.Timing)!;
            var duration = (float)Closed(s_duration!, timingType).GetValue(running.Timing)!;
            var easing = (Func<float, float>)Closed(s_easing!, timingType).GetValue(running.Timing)!;
            var styleType = running.Style.GetType();
            var from = magnitude(Closed(s_from!, styleType).GetValue(running.Style)!);
            var to = magnitude(Closed(s_to!, styleType).GetValue(running.Style)!);
            if (from == null || to == null) return null;
            if (duration <= 0f || running.Now < startTime || running.Now >= startTime + duration) return 0f;
            var progress = (float)((running.Now - startTime) / duration);
            var before = Mathf.Max(0f, progress - (float)SlopeStep);
            var after = Mathf.Min(1f, progress + (float)SlopeStep);
            if (after <= before) return 0f;
            return (to.Value - from.Value) * (easing(after) - easing(before)) / (after - before) / duration;
        }

        private static Type ClosedValues(object values)
        {
            for (var type = values.GetType(); type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == s_running!.DeclaringType) return type;
            }
            throw new ArgumentException("Not a Values<T>.", nameof(values));
        }

        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.DeclaredOnly;

        // The member of a closed generic type that an open definition's member stands for, matched by metadata token.
        private static FieldInfo Closed(FieldInfo open, Type closedDeclaring)
        {
            foreach (var field in closedDeclaring.GetFields(Declared))
            {
                if (field.MetadataToken == open.MetadataToken) return field;
            }
            throw new ArgumentException($"{open.Name} is not a field of {closedDeclaring}.");
        }

        private static MethodInfo Closed(MethodInfo open, Type closedDeclaring)
        {
            foreach (var method in closedDeclaring.GetMethods(Declared))
            {
                if (method.MetadataToken == open.MetadataToken) return method;
            }
            throw new ArgumentException($"{open.Name} is not a method of {closedDeclaring}.");
        }

        /// <summary>
        /// The names of the generic members that do not close over a float animation, which a rename or a retyped
        /// container shows as here before it shows as a velocity that never arrives.
        /// </summary>
        internal static string[] Unclosed()
        {
            if (!Resolves) return new[] { "an unresolved member" };
            var unclosed = new System.Collections.Generic.List<string>();
            void Check(string name, Action close)
            {
                try { close(); }
                catch (Exception exception) when (exception is ArgumentException or NullReferenceException
                    or InvalidOperationException) { unclosed.Add(name); }
            }
            var values = s_running!.DeclaringType!.MakeGenericType(typeof(float));
            var running = Closed(s_running, values).FieldType;
            var timing = Closed(s_timing!, running).FieldType.GetElementType()!;
            var style = Closed(s_style!, running).FieldType.GetElementType()!;
            Check(nameof(s_indexOf), () => Closed(s_indexOf!, running));
            Check(nameof(s_start), () => Closed(s_start!, timing));
            Check(nameof(s_duration), () => Closed(s_duration!, timing));
            Check(nameof(s_easing), () => Closed(s_easing!, timing));
            Check(nameof(s_from), () => Closed(s_from!, style));
            Check(nameof(s_to), () => Closed(s_to!, style));
            return unclosed.ToArray();
        }
    }
}
