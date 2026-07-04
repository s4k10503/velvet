#nullable enable
using System;
using System.Globalization;

namespace Velvet
{
    /// <summary>
    /// A resolved skew utility pair: per-axis shear angles in degrees plus the winning class token per axis
    /// (the allocation-free steady-state probe compares tokens, mirroring <see cref="StyleClipPathClass"/>'s
    /// Source). Positive X matches CSS <c>skewX</c>: with y growing downward, the bottom edge shifts right;
    /// the negative form <c>-skew-x-6</c> leans the top edge right.
    /// </summary>
    internal readonly struct SkewSpec
    {
        public readonly float XDeg;
        public readonly float YDeg;
        public readonly string? SourceX;
        public readonly string? SourceY;

        public SkewSpec(float xDeg, float yDeg, string? sourceX, string? sourceY)
        {
            XDeg = xDeg;
            YDeg = yDeg;
            SourceX = sourceX;
            SourceY = sourceY;
        }

        public bool Active => XDeg != 0f || YDeg != 0f;
    }

    // Parses Velvet's skew utility classes: skew-x-6 / -skew-x-6 (the numeric
    // suffix is DEGREES) and the arbitrary form skew-x-[8deg] / -skew-x-[8deg],
    // same for skew-y-*. skew-x-0 is a recognized reset (it overrides an earlier skew-x-6 in the
    // cascade). Same shape as StyleClipPathClass: a cheap gate plus a cascade-correct extractor
    // (last recognized class per axis wins).
    //
    // PARITY NOTE: CSS transform: skewX() shears the element AND its descendants. UI Toolkit's
    // transform supports only translate / rotate / scale — shear is unrepresentable — so Velvet's
    // skew is a documented deviation: it shears the element's own box silhouette (background +
    // border, painted by SkewSilhouette) while children stay upright. This matches the dominant
    // CSS usage (skew the card, counter-skew the content) with the counter-skew built in.
    internal static class StyleSkewClass
    {
        private const string XPrefix = "skew-x-";
        private const string YPrefix = "skew-y-";

        // True when cls is a skew utility this layer owns (recognized or not-yet-valid).
        public static bool IsSkewClass(string? cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var body = cls[0] == '-' ? cls.Substring(1) : cls;
            return body.StartsWith(XPrefix, StringComparison.Ordinal)
                || body.StartsWith(YPrefix, StringComparison.Ordinal);
        }

        // Cheap early-out gate: true when ANY class is a skew utility.
        public static bool HasSkewClass(string[]? classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsSkewClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Allocation-free cascade probe: the LAST PARSEABLE skew token per axis (mirroring the token
        // TryExtract stores in SkewSpec.SourceX / SourceY), or false when neither axis has one. The patch
        // path compares these against the live binding's SourceX / SourceY to skip the parse in the steady
        // state — gating on the same TryParse predicate keeps the two probes from diverging on a trailing
        // unparseable token (e.g. ["skew-x-6", "skew-x-junk"]), which otherwise defeats the fast path.
        public static bool TryGetWinningSkewClasses(string[]? classNames, out string? winnerX, out string? winnerY)
        {
            winnerX = null;
            winnerY = null;
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var isX, out _))
                {
                    continue;
                }
                if (isX)
                {
                    winnerX = cls;
                }
                else
                {
                    winnerY = cls;
                }
            }
            return winnerX != null || winnerY != null;
        }

        // Resolves the cascade (last recognized class per axis wins) into a SkewSpec. Returns true
        // only when the result is ACTIVE (a non-zero angle on at least one axis) — skew-x-0 resets
        // its axis, so a list ending in resets extracts to false.
        public static bool TryExtract(string[]? classNames, out SkewSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            float x = 0f, y = 0f;
            string? sourceX = null, sourceY = null;
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var isX, out var deg))
                {
                    continue;
                }
                if (isX)
                {
                    x = deg;
                    sourceX = cls;
                }
                else
                {
                    y = deg;
                    sourceY = cls;
                }
            }

            spec = new SkewSpec(x, y, sourceX, sourceY);
            return spec.Active;
        }

        // Parses one class: skew-x-<int> / -skew-x-<int> / skew-x-[<float>deg] (+ y variants).
        // Returns false for anything else (the cascade ignores unrecognized tokens).
        internal static bool TryParse(string? cls, out bool isX, out float deg)
        {
            isX = false;
            deg = 0f;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            var negative = cls[0] == '-';
            var body = negative ? cls.Substring(1) : cls;
            string suffix;
            if (body.StartsWith(XPrefix, StringComparison.Ordinal))
            {
                isX = true;
                suffix = body.Substring(XPrefix.Length);
            }
            else if (body.StartsWith(YPrefix, StringComparison.Ordinal))
            {
                suffix = body.Substring(YPrefix.Length);
            }
            else
            {
                return false;
            }

            if (suffix.Length == 0)
            {
                return false;
            }

            // Arbitrary form: [<float>deg] (the unit is required in the bracket).
            if (suffix[0] == '[')
            {
                if (!suffix.EndsWith("deg]", StringComparison.Ordinal) || suffix.Length < 6)
                {
                    return false;
                }
                var number = suffix.Substring(1, suffix.Length - 5);
                if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
                deg = negative ? -value : value;
                return true;
            }

            // Numeric form: an integer number of degrees (the preset scale is a subset).
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
            {
                return false;
            }
            deg = negative ? -whole : whole;
            return true;
        }
    }
}
