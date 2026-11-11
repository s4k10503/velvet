using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The timing one channel of a driven Tween runs on where a PropertyOverrides entry names its property, or one a
    // gesture state hands back to a bezier play (BezierTweenDriver.ReturnChannels). The delay counts from the
    // tick's start. Curve: eased on the play's own cubic-bezier rather than Easing. Bezier: eased on that curve
    // instead. Spring: run on that spring until it rests, DurationSec being where it does. Accelerated: Easing eased as
    // for a value Framer hands the Web Animations API (MotionEasing).
    internal readonly struct TweenChannelTiming
    {
        public TweenChannelTiming(float delaySec, float durationSec, EasingMode easing, bool curve = false)
        {
            DelaySec = delaySec;
            DurationSec = durationSec;
            Easing = easing;
            Curve = curve;
            Bezier = null;
            Spring = null;
            RestSpeed = null;
        }

        public float DelaySec { get; init; }
        public float DurationSec { get; init; }
        public EasingMode Easing { get; }
        public bool Curve { get; }
        public BezierCurve? Bezier { get; init; }
        public (float Stiffness, float Damping, float Mass)? Spring { get; init; }
        public float? RestSpeed { get; init; }
        public bool Accelerated { get; init; }

        // The share of the way from the channel's start to its end activeSec after its delay, for a channel that
        // travels `travel` in its own units, which a spring is measured over as Framer measures a value's.
        public float SpringOrBezierFraction(double activeSec, float travel)
        {
            if (activeSec <= 0.0)
            {
                return 0f;
            }
            if (Spring is { } spring)
            {
                return activeSec >= DurationSec || travel == 0f
                    ? 1f
                    : MotionSpringDriver.SampleSpring(0f, 1f, Math.Abs(travel), 0f, activeSec, spring, null, RestSpeed);
            }
            var progress = DurationSec > 0f ? Math.Min(1f, (float)(activeSec / DurationSec)) : 1f;
            return Bezier is { } curve
                ? CubicBezierEvaluator.Evaluate(curve.X1, curve.Y1, curve.X2, curve.Y2, progress)
                : MotionEasing.Evaluate(Easing, progress);
        }

        // This timing over a travel of `travel`, where a spring rests elsewhere than the travel it was measured on.
        public TweenChannelTiming OverTravel(float travel)
            => Spring is { } spring
                ? this with { DurationSec = MotionSpringDriver.SpringRestSec(spring, RestSpeed, Math.Abs(travel)) }
                : this;
    }

    // The timing of a Tween a mount's clock drives (StyleAnimationScheduler.StartDrivenTween), read off the list
    // ApplyPropertyOverrideTransitionStyles writes for the native transition the way UI Toolkit reads that list: a
    // channel runs on the last PropertyOverrides entry naming its property, a shorthand of it or `all`, and on the
    // top-level timing where none does. The tick waits for the earliest delay among them (StartDelaySec), so each
    // delay here counts from the tick's start.
    internal sealed class DrivenTweenTiming
    {
        private readonly IReadOnlyList<StylePropertyTransition>? _overrides;
        private readonly float _orchestrationSec;

        public DrivenTweenTiming(float delaySec, float durationSec, EasingMode easing,
            IReadOnlyList<StylePropertyTransition>? overrides, float orchestrationSec = 0f)
        {
            _overrides = overrides;
            _orchestrationSec = orchestrationSec;
            DurationSec = durationSec;
            Easing = easing;
            var earliest = delaySec;
            // The top-level entry's delay stands in for an end where there is no entry, which EndSec outlasts.
            var slowestEnd = delaySec;
            foreach (var entry in overrides ?? Array.Empty<StylePropertyTransition>())
            {
                var (entryDelaySec, entryDurationSec) = Entry(entry);
                earliest = Math.Min(earliest, entryDelaySec);
                slowestEnd = Math.Max(slowestEnd, entryDelaySec + entryDurationSec);
            }
            StartDelaySec = earliest;
            DelaySec = delaySec - earliest;
            SlowestEntryEndSec = slowestEnd - earliest;
        }

        public float StartDelaySec { get; }
        // Where the slowest PropertyOverrides entry ends, whether or not a channel plays its property, as the native
        // transition's completion waits for every entry it wrote (StyleAnimationScheduler.SlowestPropertyTimeoutMs).
        public float SlowestEntryEndSec { get; }
        public float DelaySec { get; }
        public float DurationSec { get; }
        public EasingMode Easing { get; }

        // Null where no entry names any of the longhands, which leaves the channel on the top-level timing. kind,
        // from and to: the default an entry naming no timing takes, and the channel's travel a spring is measured on.
        public TweenChannelTiming? For(StyleLonghandSet longhands, ValueDefault kind, float from, float to)
            => LastNaming(_overrides, longhands) is { } entry
                ? entry.TimingFor(Entry(entry).delaySec - StartDelaySec, kind, from, to, Accelerated(longhands))
                : null;

        // Whether Framer runs the value these longhands make up on the Web Animations API (accelerated-values.ts):
        // opacity alone among the channels a driven play writes, its transforms going through its TransformAnimation,
        // which samples the keyframes generator.
        internal static bool Accelerated(StyleLonghandSet longhands) => longhands.Contains(StyleLonghand.Opacity);

        // The last entry naming any of the longhands, which UI Toolkit's transition and Framer both let win.
        internal static StylePropertyTransition? LastNaming(IReadOnlyList<StylePropertyTransition>? overrides,
            StyleLonghandSet longhands)
        {
            for (var i = (overrides?.Count ?? 0) - 1; i >= 0; i--)
            {
                if (Named(overrides![i].Property).Overlaps(longhands))
                {
                    return overrides[i];
                }
            }
            return null;
        }

        // A negative duration runs as none, as UI Toolkit floors it (StyleFilterTransitionDriver.TryFindTransition).
        private (float delaySec, float durationSec) Entry(StylePropertyTransition entry)
            => (entry.OwnDelaySec(_orchestrationSec), Math.Max(0f, entry.DurationOrDefaultSec));

        // The channels of a spring or bezier play a PropertyOverrides entry can name, each with the longhands that name
        // it and the default transition Framer gives its value.
        internal static IEnumerable<(T Channel, StyleLonghandSet Longhands, ValueDefault Kind)> OverridableChannels<T>(
            IGestureMaskableState state) where T : class, IGestureMaskable
        {
            var (opacity, translateX, translateY, scale, rotate) = state.Scalars;
            if (opacity is T o) yield return (o, StyleLonghandSet.Of(StyleLonghand.Opacity), ValueDefault.Ease);
            if (translateX is T x) yield return (x, StyleLonghandSet.Of(StyleLonghand.Translate), ValueDefault.Transform);
            if (translateY is T y) yield return (y, StyleLonghandSet.Of(StyleLonghand.Translate), ValueDefault.Transform);
            if (scale is T s) yield return (s, StyleLonghandSet.Of(StyleLonghand.Scale), ValueDefault.Scale);
            if (state.ScaleY is T sy) yield return (sy, StyleLonghandSet.Of(StyleLonghand.Scale), ValueDefault.Scale);
            if (rotate is T r) yield return (r, StyleLonghandSet.Of(StyleLonghand.Rotate), ValueDefault.Transform);
            foreach (var property in state.ColorChannels ?? Array.Empty<IPropertyChannel>())
            {
                yield return ((T)property.Channel, StyleArbitraryLonghands.Of(property.Property), ValueDefault.Ease);
            }
            foreach (var property in state.LengthChannels ?? Array.Empty<IPropertyChannel>())
            {
                yield return ((T)property.Channel, StyleArbitraryLonghands.Of(property.Property), ValueDefault.Ease);
            }
        }

        internal static StyleLonghandSet Named(string property)
            => s_named.TryGetValue(property, out var named) ? named : StyleLonghandSet.Empty;

        // The shorthands naming a longhand a driven channel writes. DrivenTweenShorthandTests pins each against the
        // longhands UI Toolkit's own transition runs under that name.
        internal static readonly IReadOnlyDictionary<string, StyleLonghandSet> Shorthands =
            new Dictionary<string, StyleLonghandSet>(StringComparer.Ordinal)
            {
                ["padding"] = Of(StyleLonghand.PaddingTop, StyleLonghand.PaddingRight, StyleLonghand.PaddingBottom, StyleLonghand.PaddingLeft),
                ["margin"] = Of(StyleLonghand.MarginTop, StyleLonghand.MarginRight, StyleLonghand.MarginBottom, StyleLonghand.MarginLeft),
                ["border-width"] = Of(StyleLonghand.BorderTopWidth, StyleLonghand.BorderRightWidth, StyleLonghand.BorderBottomWidth, StyleLonghand.BorderLeftWidth),
                ["border-color"] = Of(StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor, StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor),
                ["border-radius"] = Of(StyleLonghand.BorderTopLeftRadius, StyleLonghand.BorderTopRightRadius, StyleLonghand.BorderBottomRightRadius, StyleLonghand.BorderBottomLeftRadius),
                ["flex"] = Of(StyleLonghand.FlexBasis),
            };

        // Every name a transition-property entry can give a driven channel: each longhand's, the shorthands above,
        // and `all`. Built after Shorthands, which it reads.
        private static readonly Dictionary<string, StyleLonghandSet> s_named = BuildNamed();

        private static Dictionary<string, StyleLonghandSet> BuildNamed()
        {
            var named = new Dictionary<string, StyleLonghandSet>(StringComparer.Ordinal);
            var all = StyleLonghandSet.Empty;
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                named[StyleUtilityProperties.UssName(longhand)] = StyleLonghandSet.Of(longhand);
                all = all.Union(StyleLonghandSet.Of(longhand));
            }
            foreach (var shorthand in Shorthands)
            {
                named[shorthand.Key] = shorthand.Value;
            }
            named["all"] = all;
            return named;
        }

        private static StyleLonghandSet Of(params StyleLonghand[] longhands) => MotionNativeTransitionGuard.SetOf(longhands);
    }
}
