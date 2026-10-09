using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The timing one channel of a driven Tween runs on where a PropertyOverrides entry names its property. The
    // delay counts from the tick's start.
    internal readonly struct TweenChannelTiming
    {
        public TweenChannelTiming(float delaySec, float durationSec, EasingMode easing)
        {
            DelaySec = delaySec;
            DurationSec = durationSec;
            Easing = easing;
        }

        public float DelaySec { get; }
        public float DurationSec { get; }
        public EasingMode Easing { get; }
    }

    // The timing of a Tween a mount's clock drives (StyleAnimationScheduler.StartDrivenTween), read off the list
    // ApplyPropertyOverrideTransitionStyles writes for the native transition the way UI Toolkit reads that list: a
    // channel runs on the last PropertyOverrides entry naming its property, a shorthand of it or `all`, and on the
    // top-level timing where none does. The tick waits for the earliest delay among them (StartDelaySec), so each
    // delay here counts from the tick's start.
    internal sealed class DrivenTweenTiming
    {
        private readonly IReadOnlyList<StylePropertyTransition>? _overrides;
        private readonly float _topDelaySec;

        public DrivenTweenTiming(float delaySec, float durationSec, EasingMode easing,
            IReadOnlyList<StylePropertyTransition>? overrides)
        {
            _overrides = overrides;
            _topDelaySec = delaySec;
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

        // Null where no entry names any of the longhands, which leaves the channel on the top-level timing.
        public TweenChannelTiming? For(StyleLonghandSet longhands)
        {
            var count = _overrides?.Count ?? 0;
            for (var i = count - 1; i >= 0; i--)
            {
                var entry = _overrides![i];
                if (Named(entry.Property).Overlaps(longhands))
                {
                    var (delaySec, durationSec) = Entry(entry);
                    return new TweenChannelTiming(delaySec - StartDelaySec, durationSec, entry.Easing ?? Easing);
                }
            }
            return null;
        }

        // A negative duration runs as none, as UI Toolkit floors it (StyleFilterTransitionDriver.TryFindTransition).
        private (float delaySec, float durationSec) Entry(StylePropertyTransition entry)
            => (entry.DelaySec ?? _topDelaySec, Math.Max(0f, entry.DurationSec ?? DurationSec));

        private static StyleLonghandSet Named(string property)
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
