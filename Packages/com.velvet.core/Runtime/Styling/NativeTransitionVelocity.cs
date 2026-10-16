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
                var timing = ((Array)Closed(s_timing!, set).GetValue(running)!).GetValue(index)!;
                var style = ((Array)Closed(s_style!, set).GetValue(running)!).GetValue(index)!;
                var timingType = timing.GetType();
                var styleType = style.GetType();
                var startTime = (double)Closed(s_start!, timingType).GetValue(timing)!;
                var duration = (float)Closed(s_duration!, timingType).GetValue(timing)!;
                var easing = (Func<float, float>)Closed(s_easing!, timingType).GetValue(timing)!;
                var now = (double)s_time!.GetValue(system)!;
                var from = magnitude(Closed(s_from!, styleType).GetValue(style)!);
                var to = magnitude(Closed(s_to!, styleType).GetValue(style)!);
                if (from == null || to == null) return null;
                if (duration <= 0f || now < startTime || now >= startTime + duration) return 0f;
                var progress = (float)((now - startTime) / duration);
                var before = Mathf.Max(0f, progress - (float)SlopeStep);
                var after = Mathf.Min(1f, progress + (float)SlopeStep);
                if (after <= before) return 0f;
                var slope = (easing(after) - easing(before)) / (after - before);
                return (to.Value - from.Value) * slope / duration;
            }
            catch (Exception exception) when (exception is TargetInvocationException or TargetException
                or ArgumentException or MemberAccessException or NotSupportedException or InvalidCastException
                or NullReferenceException)
            {
                return null;
            }
        }

        private static Type ClosedValues(object values)
        {
            for (var type = values.GetType(); type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == s_running!.DeclaringType) return type;
            }
            throw new ArgumentException("Not a Values<T>.", nameof(values));
        }

        private static FieldInfo Closed(FieldInfo open, Type closedDeclaring)
            => FieldInfo.GetFieldFromHandle(open.FieldHandle, closedDeclaring.TypeHandle);

        private static MethodInfo Closed(MethodInfo open, Type closedDeclaring)
            => (MethodInfo)MethodBase.GetMethodFromHandle(open.MethodHandle, closedDeclaring.TypeHandle)!;
    }
}
