using System;
using System.Globalization;
using UnityEngine;

namespace Velvet
{
    // The motion an animate-* utility drives. Gradient and Shimmer pan a baked gradient's
    // background-position (no per-frame texture work — the texture is baked once, only its offset moves);
    // Hue continuously rotates the hue-rotate filter angle (color cycling, works on any element, not just a
    // gradient); Pulse oscillates opacity (the attention/skeleton pulse, works on any element). None is the
    // explicit cancel (animate-none).
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
        // Oscillates opacity between full and half over the loop (a smooth ease) — the attention / skeleton
        // pulse. Geometry-free, so it works on any element; compose with a colour cycle for a glowing pulse.
        Pulse,
    }

    // A resolved animate-* utility: the motion mode and its loop duration (seconds). Value-equal (duration
    // quantized to whole milliseconds) so the patch path skips a redundant restart when an unchanged class
    // list re-resolves to the same animation.
    internal readonly struct AnimateSpec : IEquatable<AnimateSpec>
    {
        public readonly AnimateMode Mode;
        public readonly float DurationSec;

        public AnimateSpec(AnimateMode mode, float durationSec)
        {
            Mode = mode;
            DurationSec = durationSec;
        }

        private static int DurKey(float sec) => Mathf.RoundToInt(sec * 1000f);

        public bool Equals(AnimateSpec other) => Mode == other.Mode && DurKey(DurationSec) == DurKey(other.DurationSec);
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
    // filter angle, or an opacity) — the gradient texture is baked once and never re-baked while animating.
    // Unrecognized animate-* tokens (e.g. a future animate-spin) are not claimed, leaving the namespace open.
    internal static class StyleAnimateClass
    {
        private const string Prefix = "animate-";

        // Per-mode default loop durations (seconds), used when no -[<time>] override is given.
        private const float DefaultGradientSec = 3f;
        private const float DefaultShimmerSec = 1.5f;
        private const float DefaultHueSec = 4f;
        private const float DefaultPulseSec = 2f;

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
            var mode = AnimateMode.None;
            var dur = 0f;
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (TryParseToken(cls, out var m, out var d))
                {
                    mode = m;
                    dur = d;
                    found = true;
                }
            }

            if (!found || mode == AnimateMode.None)
            {
                return false;
            }
            spec = new AnimateSpec(mode, dur);
            return true;
        }

        // Parses one token into (mode, durationSec). False when not a recognized animate-* token (incl. an
        // unparseable -[<time>] suffix).
        private static bool TryParseToken(string cls, out AnimateMode mode, out float durationSec)
        {
            mode = AnimateMode.None;
            durationSec = 0f;
            if (!cls.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            var rest = cls.Substring(Prefix.Length);

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

            switch (rest)
            {
                case "none": mode = AnimateMode.None; durationSec = 0f; return true;
                case "gradient": mode = AnimateMode.Gradient; durationSec = overrideSec ?? DefaultGradientSec; return true;
                case "shimmer": mode = AnimateMode.Shimmer; durationSec = overrideSec ?? DefaultShimmerSec; return true;
                case "hue": mode = AnimateMode.Hue; durationSec = overrideSec ?? DefaultHueSec; return true;
                case "pulse": mode = AnimateMode.Pulse; durationSec = overrideSec ?? DefaultPulseSec; return true;
                default: return false;
            }
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
            if (s.EndsWith("ms", StringComparison.Ordinal))
            {
                num = s.Substring(0, s.Length - 2);
                scale = 0.001f;
            }
            else if (s.EndsWith("s", StringComparison.Ordinal))
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
