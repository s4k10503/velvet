using System;
using System.Globalization;
using UnityEngine;

namespace Velvet
{
    // The motion an animate-* utility drives. Gradient and Shimmer pan a baked gradient's
    // background-position (no per-frame texture work — the texture is baked once, only its offset moves);
    // Hue continuously rotates the hue-rotate filter angle (color cycling, works on any element, not just a
    // gradient); Pulse oscillates opacity (the attention/skeleton pulse, works on any element); Spin turns
    // the element (the loading spinner); Ping grows the element while it fades out;
    // Bounce lifts it and drops it back. None is the explicit cancel (animate-none).
    internal enum AnimateMode
    {
        None,
        // Pans the gradient back and forth along its axis over an oversized (200%) background, so the box
        // window slides across the full gradient with no transparent edge revealed. The flowing-sheen look.
        Gradient,
        // Sweeps the gradient one-way across the box (the band enters one edge, crosses, exits the other,
        // then repeats). The loading-skeleton shimmer; pair with from-transparent via-{highlight} to-transparent.
        Shimmer,
        // Rotates the hue 0..360 over the loop via the hue-rotate filter — the element's colors cycle in place.
        Hue,
        // Oscillates opacity between full and half over the loop — the attention / skeleton pulse.
        // Geometry-free, so it works on any element; compose with a colour cycle for a glowing pulse.
        Pulse,
        // Rotates a full turn over the loop, linearly — the loading spinner. Owns the rotate slot on the terms
        // Hue owns the filter slot.
        Spin,
        // Scales the element to twice its size while fading it out over the first three quarters of the loop, then
        // holds that for the last quarter.
        Ping,
        // Lifts the element a quarter of its height and drops it back, each half on its own curve.
        Bounce,
        // Loops the keyframes a name registered with VelvetKeyframes holds (animate-wiggle, animate-[wiggle_1s_linear_infinite]).
        Custom,
    }

    // A resolved animate-* utility: the motion mode and its loop duration (seconds). Value-equal (duration
    // quantized to whole milliseconds) so the patch path skips a redundant restart when an unchanged class
    // list re-resolves to the same animation.
    internal readonly struct AnimateSpec : IEquatable<AnimateSpec>
    {
        public readonly AnimateMode Mode;
        public readonly float DurationSec;
        // The registered keyframes and the curve between stops that name none of their own, for Custom.
        public readonly KeyframeSet? Keyframes;
        public readonly BezierCurve Timing;

        public AnimateSpec(AnimateMode mode, float durationSec) : this(mode, durationSec, null, default)
        {
        }

        public AnimateSpec(AnimateMode mode, float durationSec, KeyframeSet? keyframes, BezierCurve timing)
        {
            Mode = mode;
            DurationSec = durationSec;
            Keyframes = keyframes;
            Timing = timing;
        }

        private static int DurKey(float sec) => Mathf.RoundToInt(sec * 1000f);

        public bool Equals(AnimateSpec other)
            => Mode == other.Mode && DurKey(DurationSec) == DurKey(other.DurationSec)
                && ReferenceEquals(Keyframes, other.Keyframes)
                && Timing.X1 == other.Timing.X1 && Timing.Y1 == other.Timing.Y1
                && Timing.X2 == other.Timing.X2 && Timing.Y2 == other.Timing.Y2;
        public override bool Equals(object obj) => obj is AnimateSpec o && Equals(o);
        public override int GetHashCode() => unchecked(((int)Mode * 397) ^ DurKey(DurationSec));
    }

    // Parses the animate-* motion utilities into an AnimateSpec. These compose ORTHOGONALLY with the gradient
    // utilities (bg-gradient-*, from-/via-/to-): the gradient utilities define the colour and shape, an
    // animate-* utility drives the motion over it. An optional -[<time>] suffix (animate-hue-[5s],
    // animate-gradient-[800ms]) overrides the per-mode default loop duration. Cascade-correct: the LAST
    // recognized animate-* token wins, so animate-gradient animate-none resolves to no animation.
    //
    // The per-frame cost is always a cheap inline-style write (a background-position offset, a hue-rotate
    // filter angle, an opacity, a rotation, a scale or a translation) — the gradient texture is baked once and never re-baked while animating.
    // Unrecognized animate-* tokens are not claimed, leaving the namespace open.
    internal static class StyleAnimateClass
    {
        private const string Prefix = "animate-";

        // Per-mode default loop durations (seconds), used when no -[<time>] override is given.
        private const float DefaultGradientSec = 3f;
        private const float DefaultShimmerSec = 1.5f;
        private const float DefaultHueSec = 4f;
        private const float DefaultPulseSec = 2f;
        // Tailwind's animate-spin: one turn a second, linear, forever.
        private const float DefaultSpinSec = 1f;
        // Tailwind's animate-ping and animate-bounce: one second, forever.
        private const float DefaultPingSec = 1f;
        private const float DefaultBounceSec = 1f;

        // True when cls is an animate-* utility this layer owns. Covers the unrecognized names too (a future
        // mode): the namespace is claimed as a whole so a variant payload naming one routes here rather than
        // being judged token by token against the recognized set.
        public static bool IsAnimateClass(string cls)
            => !string.IsNullOrEmpty(cls) && cls.StartsWith(Prefix, StringComparison.Ordinal);

        // Resolves the winning animate-* utility: the LAST recognized token wins (cascade). Returns false
        // when none is present, or when the winner is animate-none (an explicit cancel). This is itself the
        // gate — a no-animation element pays one per-class probe pass, not two — so the reconciler calls it
        // directly with no separate Has* pre-scan.
        public static bool TryExtract(string[] classNames, out AnimateSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls) && TryParseToken(cls, out var parsed))
                {
                    spec = parsed;
                    found = true;
                }
            }

            return found && spec.Mode != AnimateMode.None;
        }

        // Parses one token into (mode, durationSec). False when not a recognized animate-* token (incl. an
        // unparseable -[<time>] suffix).
        private static bool TryParseToken(string cls, out AnimateSpec spec)
        {
            spec = default;
            if (!cls.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            var rest = cls.Substring(Prefix.Length);
            if (rest.StartsWith("[", StringComparison.Ordinal))
            {
                return TryParseArbitrary(rest, out spec);
            }

            // Split off an optional -[<time>] duration override (e.g. animate-gradient-[800ms]).
            float? overrideSec = null;
            var br = rest.IndexOf("-[", StringComparison.Ordinal);
            if (br >= 0 && rest[rest.Length - 1] == ']')
            {
                if (!TryParseTime(rest.Substring(br + 2, rest.Length - br - 3), out var sec))
                {
                    return false;
                }
                overrideSec = sec;
                rest = rest.Substring(0, br);
            }

            if (TryParseBuiltIn(rest, overrideSec, out spec))
            {
                return true;
            }
            if (!VelvetKeyframes.TryGet(rest, out var keyframes))
            {
                return false;
            }
            spec = new AnimateSpec(AnimateMode.Custom, overrideSec ?? keyframes.DefaultDurationSec, keyframes, keyframes.DefaultTiming);
            return true;
        }

        private static bool TryParseBuiltIn(string name, float? overrideSec, out AnimateSpec spec)
        {
            var (mode, defaultSec) = name switch
            {
                "none" => (AnimateMode.None, 0f),
                "gradient" => (AnimateMode.Gradient, DefaultGradientSec),
                "shimmer" => (AnimateMode.Shimmer, DefaultShimmerSec),
                "hue" => (AnimateMode.Hue, DefaultHueSec),
                "pulse" => (AnimateMode.Pulse, DefaultPulseSec),
                "spin" => (AnimateMode.Spin, DefaultSpinSec),
                "ping" => (AnimateMode.Ping, DefaultPingSec),
                "bounce" => (AnimateMode.Bounce, DefaultBounceSec),
                _ => (AnimateMode.Custom, 0f),
            };
            spec = mode == AnimateMode.Custom ? default : new AnimateSpec(mode, mode == AnimateMode.None ? 0f : overrideSec ?? defaultSec);
            return mode != AnimateMode.Custom;
        }

        // animate-[name_1s_ease-in-out_infinite]: a registered name, a time, a timing function and `infinite`, in any
        // order. A loop with another count or a delay is not claimed.
        private static bool TryParseArbitrary(string bracketed, out AnimateSpec spec)
        {
            spec = default;
            if (!bracketed.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }
            KeyframeSet? keyframes = null;
            float? duration = null;
            BezierCurve? timing = null;
            var infinite = false;
            foreach (var token in bracketed.Substring(1, bracketed.Length - 2).Split('_'))
            {
                if (token.Length == 0)
                {
                    continue;
                }
                if (token == "infinite")
                {
                    infinite = true;
                }
                else if (!TryTakeToken(token, ref keyframes, ref duration, ref timing))
                {
                    return false;
                }
            }
            if (keyframes == null || !infinite)
            {
                return false;
            }
            spec = new AnimateSpec(AnimateMode.Custom, duration ?? 0f, keyframes, timing ?? KeyframeSet.Ease);
            return duration.HasValue;
        }

        // Records what a token of animate-[...] names: the first time is the duration, a second would be a delay.
        private static bool TryTakeToken(string token, ref KeyframeSet? keyframes, ref float? duration, ref BezierCurve? timing)
        {
            if (TryParseTime(token, out var seconds))
            {
                var first = duration == null;
                duration ??= seconds;
                return first;
            }
            if (KeyframeSet.TryParseTiming(token, out var curve))
            {
                var first = timing == null;
                timing ??= curve;
                return first;
            }
            if (keyframes == null && VelvetKeyframes.TryGet(token, out var found))
            {
                keyframes = found;
                return true;
            }
            return false;
        }

        // "<n>s" or "<n>ms" → seconds (must be > 0). False otherwise.
        private static bool TryParseTime(string s, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            float scale;
            string num;
            if (s.EndsWith("ms", StringComparison.OrdinalIgnoreCase))
            {
                num = s.Substring(0, s.Length - 2);
                scale = 0.001f;
            }
            else if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                num = s.Substring(0, s.Length - 1);
                scale = 1f;
            }
            else
            {
                return false;
            }
            if (!float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                || float.IsNaN(v) || float.IsInfinity(v) || v <= 0f)
            {
                return false;
            }
            seconds = v * scale;
            return true;
        }
    }
}
