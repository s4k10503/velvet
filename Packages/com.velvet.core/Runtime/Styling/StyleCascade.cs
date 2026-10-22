#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The style an element's matching rules give it beneath its inline values, which UI Toolkit caches under the
    // element's matchingRulesHash (StyleCache). That cache is internal to the engine and so read by reflection;
    // where the members a read needs do not resolve, that read reports nothing and the caller keeps its own default.
    // Given_APulseOverANamedOpacity_When_AQuarterThroughItsLoop_Then_ItIsHalfwayFromThatOpacityToHalf,
    // Given_ASpinOverANamedRotation_When_AQuarterThroughItsLoop_Then_ItHasTurnedAQuarterFromThatRotation and
    // Given_APingOverAnOpacityAndAScale_When_ItsLoopStarts_Then_ItStartsFromThoseValues fail when the opacity,
    // rotate or scale read stops giving the cascaded value.
    internal static class StyleCascade
    {
        private static readonly FieldInfo? s_style = EngineMember.ElementComputedStyle.ResolveField();
        private static readonly FieldInfo? s_hash = EngineMember.ComputedStyleMatchingRulesHash.ResolveField();
        private static readonly MethodInfo? s_tryGet = EngineMember.TryGetComputedStyle.ResolveMethod();
        private static readonly PropertyInfo? s_opacity = EngineMember.ComputedStyleOpacity.ResolveProperty();
        private static readonly PropertyInfo? s_rotate = EngineMember.ComputedStyleRotate.ResolveProperty();
        private static readonly PropertyInfo? s_scale = EngineMember.ComputedStyleScale.ResolveProperty();
        private static readonly PropertyInfo? s_translate = EngineMember.ComputedStyleTranslate.ResolveProperty();
        private static readonly PropertyInfo? s_borderLeftColor = EngineMember.ComputedStyleBorderLeftColor.ResolveProperty();
        private static readonly PropertyInfo? s_borderRightColor = EngineMember.ComputedStyleBorderRightColor.ResolveProperty();
        private static readonly PropertyInfo? s_borderTopColor = EngineMember.ComputedStyleBorderTopColor.ResolveProperty();
        private static readonly PropertyInfo? s_borderBottomColor = EngineMember.ComputedStyleBorderBottomColor.ResolveProperty();

        private static readonly PropertyInfo? s_property = EngineMember.ComputedStyleTransitionProperty.ResolveProperty();
        private static readonly PropertyInfo? s_duration = EngineMember.ComputedStyleTransitionDuration.ResolveProperty();
        private static readonly PropertyInfo? s_delay = EngineMember.ComputedStyleTransitionDelay.ResolveProperty();
        private static readonly PropertyInfo? s_curve = EngineMember.ComputedStyleTransitionTimingFunction.ResolveProperty();
        // In LayoutIdLook's corner order.
        private static readonly PropertyInfo?[] s_radii =
        {
            EngineMember.ComputedStyleBorderTopLeftRadius.ResolveProperty(),
            EngineMember.ComputedStyleBorderTopRightRadius.ResolveProperty(),
            EngineMember.ComputedStyleBorderBottomRightRadius.ResolveProperty(),
            EngineMember.ComputedStyleBorderBottomLeftRadius.ResolveProperty(),
        };
        private static readonly bool s_readsTransitions = Array.TrueForAll(
            new MemberInfo?[] { s_opacity, s_property, s_duration, s_delay, s_curve }, m => m != null);

        private static readonly object?[] s_args = new object?[2];

        // The cached ComputedStyle boxed, or null where it cannot be read. Members of it are read off the result
        // by their PropertyInfo.
        public static object? Of(VisualElement element)
        {
            if (s_style == null || s_hash == null || s_tryGet == null) return null;
            s_args[0] = s_hash.GetValue(s_style.GetValue(element));
            s_args[1] = null;
            return s_tryGet.Invoke(null, s_args) is true ? s_args[1] : null;
        }

        // The element's cascaded opacity and how long it transitions opacity (Transition). NaN and no transition where
        // the cached style cannot be read.
        public static (float Opacity, float DurationSec) Opacity(VisualElement element)
        {
            if (Transitions(element) is not { } style) return (float.NaN, 0f);
            var (durationSec, _, _) = Transition(style, element, "opacity", null);
            return ((float)s_opacity!.GetValue(style), durationSec);
        }

        // The transition the element runs a property by (Lists). None where the cached style cannot be read or names no
        // entry for the property, and none while a driver holds the element's transitions suspended
        // (MotionNativeTransitionGuard.DriverSuspends), so that a value the driver writes is taken at once
        // (Given_ALeadMixingRotateWhileABezierDrivesIt_When_HalfTheMoveHasPassed_Then_ItsOwnRotateIsTheBeziersAtOnce).
        public static (float DurationSec, float DelaySec, EasingMode Easing) Transition(VisualElement element, string property,
            string? shorthand) =>
            Transitions(element) is { } style ? Transition(style, element, property, shorthand) : (0f, 0f, EasingMode.Ease);

        private static (float DurationSec, float DelaySec, EasingMode Easing) Transition(object style, VisualElement element,
            string property, string? shorthand)
        {
            if (MotionNativeTransitionGuard.DriverSuspends(element)) return (0f, 0f, EasingMode.Ease);
            return StyleFilterTransitionDriver.TryFindTransition(Lists(style, element), property, shorthand, out var durationMs,
                out var delayMs, out var easing)
                ? (durationMs / 1000f, delayMs / 1000f, easing)
                : (0f, 0f, EasingMode.Ease);
        }

        // The rules' transition-property with the element's own inline duration, delay and curve lists wherever it holds
        // them (MotionNativeTransitionGuard.OwnTiming), as UI Toolkit combines the two. Null where the cached style
        // cannot be read.
        public static TransitionLists? Lists(VisualElement element) =>
            Transitions(element) is { } style ? Lists(style, element) : null;

        private static TransitionLists Lists(object style, VisualElement element)
        {
            var (duration, delay, curve) = MotionNativeTransitionGuard.OwnTiming(element);
            return new TransitionLists(s_property!.GetValue(style) as List<StylePropertyName>,
                duration.keyword == StyleKeyword.Undefined ? duration.value : s_duration!.GetValue(style) as List<TimeValue>,
                delay.keyword == StyleKeyword.Undefined ? delay.value : s_delay!.GetValue(style) as List<TimeValue>,
                curve.keyword == StyleKeyword.Undefined ? curve.value : s_curve!.GetValue(style) as List<EasingFunction>);
        }

        private static object? Transitions(VisualElement element)
        {
            // MUTANT_SURVIVES(equivalent): on the editor this package declares, every member resolves and this returns nothing.
            // Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_Then_ItsOwnIsCarriedOnThatTransition
            // fails where one does not.
            if (!s_readsTransitions) return null;
            return Of(element);
        }

        // The key UI Toolkit caches the element's matched rules under, which changes with them; null where it cannot be
        // read. Read by an accessor compiled once, where FieldInfo.GetValue boxes both the style and the key on every pass
        // that reads it (Given_ANarrowedLeadMidMove_When_ItsProjectionRefreshes_Then_NothingIsAllocated); IL2CPP, which
        // compiles nothing at run time, reads it boxed, as ComponentPropsComparer's AOT arm does.
        public static long? RulesHash(VisualElement element)
        {
            if (s_style == null || s_hash == null) return null;
#if !ENABLE_IL2CPP
            if (s_compiledHash != null) return s_compiledHash(element);
#endif
            return (long)s_hash.GetValue(s_style.GetValue(element));
        }

#if !ENABLE_IL2CPP
        private static readonly Func<VisualElement, long>? s_compiledHash = CompileHash();

        private static Func<VisualElement, long>? CompileHash()
        {
            if (s_style == null || s_hash == null) return null;
            var element = System.Linq.Expressions.Expression.Parameter(typeof(VisualElement), "element");
            var hash = System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Field(element, s_style), s_hash);
            return System.Linq.Expressions.Expression.Lambda<Func<VisualElement, long>>(hash, element).Compile();
        }
#endif

        // A corner's radius as the rules declare it, in LayoutIdLook's corner order; null where the cached style cannot
        // be read. Read by an accessor compiled once, as RulesHash is, since a layoutId projection can read it on every pass
        // (Given_AHolderRoundedByItsRules_When_ItsRadiiAreReadAgain_Then_NothingIsAllocated).
        public static Length? Radius(VisualElement element, int corner)
        {
#if !ENABLE_IL2CPP
            if (s_compiledRadii is { } compiled)
            {
                var read = compiled[corner](element);
                return float.IsNaN(read.value) ? null : read;
            }
#endif
            return s_radii[corner] is { } radius && Of(element) is { } style ? (Length)radius.GetValue(style) : null;
        }

#if !ENABLE_IL2CPP
        private static readonly Func<VisualElement, Length>[]? s_compiledRadii = CompileRadii();

        // Each corner's reader gives a NaN length where the element's style is not cached.
        private static Func<VisualElement, Length>[]? CompileRadii()
        {
            var readers = Array.ConvertAll(s_radii, radius => radius == null ? null : Compile(radius, new Length(float.NaN)));
            return Array.IndexOf(readers, null) >= 0 ? null : readers!;
        }
#endif

        // The rotate the rules declare, in degrees; NaN where the cached style cannot be read. Read by an accessor compiled
        // once, since a layoutId lead mixing its rotate reads it on every pass
        // (Given_ALeadMixingItsRotate_When_ItsOwnRotateIsReadAgain_Then_NothingIsAllocated).
        public static float Rotate(VisualElement element)
        {
#if !ENABLE_IL2CPP
            if (s_compiledRotate != null) return s_compiledRotate(element).angle.ToDegrees();
#endif
            return Of(element) is { } style && TryReadRotate(style, out var rotate) ? rotate.angle.ToDegrees() : float.NaN;
        }

#if !ENABLE_IL2CPP
        // A NaN angle where the element's style is not cached.
        private static readonly Func<VisualElement, Rotate>? s_compiledRotate =
            s_rotate == null ? null : Compile<Rotate>(s_rotate, new Rotate(new Angle(float.NaN)));

        // A reader of one member of the element's cached style, giving fallback where the style is not cached; null where
        // the members it goes through do not resolve or it cannot be compiled.
        private static Func<VisualElement, T>? Compile<T>(PropertyInfo member, T fallback)
        {
            if (s_style == null || s_hash == null || s_tryGet == null) return null;
            try
            {
                var element = System.Linq.Expressions.Expression.Parameter(typeof(VisualElement), "element");
                var style = System.Linq.Expressions.Expression.Variable(s_style.FieldType, "style");
                var hash = System.Linq.Expressions.Expression.Field(System.Linq.Expressions.Expression.Field(element, s_style), s_hash);
                var body = System.Linq.Expressions.Expression.Block(new[] { style },
                    System.Linq.Expressions.Expression.Condition(System.Linq.Expressions.Expression.Call(s_tryGet, hash, style),
                        System.Linq.Expressions.Expression.Property(style, member),
                        System.Linq.Expressions.Expression.Constant(fallback, typeof(T))));
                return System.Linq.Expressions.Expression.Lambda<Func<VisualElement, T>>(body, element).Compile();
            }
            catch (Exception)
            {
                return null;
            }
        }
#endif

        public static bool TryReadOpacity(object style, out float opacity)
        {
            opacity = 1f;
            if (s_opacity == null) return false;
            opacity = (float)s_opacity.GetValue(style);
            return true;
        }

        public static bool TryReadRotate(object style, out Rotate rotate)
        {
            rotate = default;
            if (s_rotate == null) return false;
            rotate = (Rotate)s_rotate.GetValue(style);
            return true;
        }

        public static bool TryReadScale(object style, out Scale scale)
        {
            scale = default;
            if (s_scale == null) return false;
            scale = (Scale)s_scale.GetValue(style);
            return true;
        }

        public static bool TryReadTranslate(object style, out Translate translate)
        {
            translate = default;
            if (s_translate == null) return false;
            translate = (Translate)s_translate.GetValue(style);
            return true;
        }

        public static bool TryReadBorderColor(object style, DivideEdge edge, out UnityEngine.Color color)
        {
            color = default;
            var property = Property(edge);
            if (property == null) return false;
            color = (UnityEngine.Color)property.GetValue(style);
            return true;
        }

#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
        private static PropertyInfo? Property(DivideEdge edge) => edge switch
        {
            DivideEdge.Left => s_borderLeftColor,
            DivideEdge.Right => s_borderRightColor,
            DivideEdge.Top => s_borderTopColor,
            DivideEdge.Bottom => s_borderBottomColor,
        };
#pragma warning restore CS8524
    }

    // What an element's classes give the four slots a loop writes, read once and kept until the element's class list
    // changes or Invalidate is called, so that a steady tick does not go through reflection again. The class list is
    // the change signal for hover: and dark: (Given_APulseOverAHoverOpacity_... and Given_APulseOverADarkOpacity_...
    // in AnimateBaseValueTests); StyleAnimateDriver calls Invalidate for what changes the rules without it.
    internal struct CascadeSnapshot
    {
        private const int StaleTicks = 2;

        private int _key;
        private int _stale;
        private bool _force;
        private bool _read;
        private bool _hasOpacity;
        private bool _hasRotate;
        private bool _hasScale;
        private bool _hasTranslate;
        private float _opacity;
        private Rotate _rotate;
        private Scale _scale;
        private Translate _translate;

        public void Refresh(VisualElement element)
        {
            var key = ClassKey(element);
            if (_read && key == _key && !_force) return;
            _read = false;
            if (StyleCascade.Of(element) is not { } style) return;
            _hasOpacity = StyleCascade.TryReadOpacity(style, out _opacity);
            _hasRotate = StyleCascade.TryReadRotate(style, out _rotate);
            _hasScale = StyleCascade.TryReadScale(style, out _scale);
            _hasTranslate = StyleCascade.TryReadTranslate(style, out _translate);
            // The sentinel key of a list that cannot be read stays unmatched, so such an element reads every time.
            _key = key;
            _read = key != UnreadableKey;
        }

        // A signal that the element's rules may have changed without its class list doing so. The styles resolve after
        // the scheduler item that reads this runs, so the next read still sees the old rules; reading again on the tick
        // after catches the new ones.
        public void Invalidate() => _stale = StaleTicks;

        // Called once per frame ahead of its reads: a frame that follows an Invalidate reads the rules again.
        public void BeginFrame()
        {
            _force = _stale > 0;
            if (_force) _stale--;
        }

        public bool TryOpacity(out float opacity)
        {
            opacity = _opacity;
            return _hasOpacity;
        }

        public bool TryRotate(out Rotate rotate)
        {
            rotate = _rotate;
            return _hasRotate;
        }

        public bool TryScale(out Scale scale)
        {
            scale = _scale;
            return _hasScale;
        }

        public bool TryTranslate(out Translate translate)
        {
            translate = _translate;
            return _hasTranslate;
        }

        private const int UnreadableKey = int.MinValue;

        // The set of classes as a hash, read without allocating: GetClasses hands back the element's own list. The
        // hashes are added rather than chained, so the order the classes arrived in does not move the key.
        private static int ClassKey(VisualElement element)
        {
            if (element.GetClasses() is not System.Collections.Generic.List<string> classes) return UnreadableKey;
            var key = classes.Count;
            for (var i = 0; i < classes.Count; i++)
            {
                key = unchecked(key + classes[i].GetHashCode());
            }
            return key == UnreadableKey ? 0 : key;
        }
    }
}
