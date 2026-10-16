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

    internal enum SkewAxis
    {
        Both,
        X,
        Y,
    }

    // Parses the skew utilities as Tailwind writes them: skew-x-6 / -skew-x-6 (the numeric suffix is DEGREES),
    // the arbitrary form skew-x-[8deg], the same for skew-y-*, and skew-6 / -skew-6 / skew-[8deg], which write
    // both axes. skew-x-0 is a recognized reset (it overrides an earlier skew-x-6 in the cascade). Same shape as
    // StyleClipPathClass: a cheap gate plus a cascade-correct extractor.
    //
    // A per-axis utility outranks skew-<n> on its axis wherever either is written, since Tailwind emits skew-<n>
    // ahead of skew-x-* and skew-y-* in its sheet. Within one of the three families the extractors take the last
    // token, which InTailwindOrder makes the one Tailwind emits last.
    //
    // PARITY NOTE: CSS transform: skewX() shears the element AND its descendants. UI Toolkit's
    // transform supports only translate / rotate / scale — shear is unrepresentable — so Velvet's
    // skew is a documented deviation: it shears the element's own box silhouette (background +
    // border, painted by SkewSilhouette). The descendant shear is APPROXIMATED, not exact:
    // StyleSkewChildTranslateManipulator seats each direct in-flow child's centroid where the shear would
    // carry it via an inline translate, applied automatically (Velvet does the counter-translate a CSS author
    // would otherwise hand-write per row). The seat is piecewise-constant — exact at each child's centroid,
    // slightly off at the far corners of a child large relative to the frame, and it does not rotate the child.
    internal static class StyleSkewClass
    {
        private const string XPrefix = "skew-x-";
        private const string YPrefix = "skew-y-";
        private const string BothPrefix = "skew-";

        // True when cls is a skew utility this layer owns (recognized or not-yet-valid).
        public static bool IsSkewClass(string? cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var body = cls[0] == '-' ? cls.Substring(1) : cls;
            return body.StartsWith(BothPrefix, StringComparison.Ordinal);
        }

        // tokens with their variantless skew utilities put in Tailwind's order, in the positions they already hold, so
        // the last of a family is the one Tailwind emits last: skew-x-6 skew-x-12 and skew-x-12 skew-x-6 both end on
        // skew-x-12, and -skew-6 sorts ahead of skew-6. A variant payload is appended after these by the variant gate
        // and keeps its place there. tokens itself when nothing moves.
        internal static string[] InTailwindOrder(string[] tokens)
        {
            System.Collections.Generic.List<int>? members = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (IsSkewClass(tokens[i]))
                {
                    (members ??= new System.Collections.Generic.List<int>()).Add(i);
                }
            }
            if (members == null || members.Count < 2)
            {
                return tokens;
            }
            var ordered = new System.Collections.Generic.List<string>(members.Count);
            foreach (var member in members)
            {
                ordered.Add(tokens[member]);
            }
            // List.Sort is not stable; two equal candidates are the same string, so their order cannot show.
            ordered.Sort(StyleCandidateOrder.Compare);
            string[]? placed = null;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (!string.Equals(ordered[i], tokens[members[i]], StringComparison.Ordinal))
                {
                    (placed ??= (string[])tokens.Clone())[members[i]] = ordered[i];
                }
            }
            return placed ?? tokens;
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
            string? winnerBoth = null;
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var axis, out _))
                {
                    continue;
                }
                switch (axis)
                {
                    case SkewAxis.X:
                        winnerX = cls;
                        break;
                    case SkewAxis.Y:
                        winnerY = cls;
                        break;
                    default:
                        winnerBoth = cls;
                        break;
                }
            }
            winnerX ??= winnerBoth;
            winnerY ??= winnerBoth;
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

            float x = 0f, y = 0f, both = 0f;
            string? sourceX = null, sourceY = null, sourceBoth = null;
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var axis, out var deg))
                {
                    continue;
                }
                switch (axis)
                {
                    case SkewAxis.X:
                        x = deg;
                        sourceX = cls;
                        break;
                    case SkewAxis.Y:
                        y = deg;
                        sourceY = cls;
                        break;
                    default:
                        both = deg;
                        sourceBoth = cls;
                        break;
                }
            }
            if (sourceX == null && sourceBoth != null)
            {
                x = both;
                sourceX = sourceBoth;
            }
            if (sourceY == null && sourceBoth != null)
            {
                y = both;
                sourceY = sourceBoth;
            }

            spec = new SkewSpec(x, y, sourceX, sourceY);
            return spec.Active;
        }

        // Parses one class: skew-x-<int> / -skew-x-<int> / skew-x-[<angle>] (+ the y and both-axes forms).
        // Returns false for anything else (the cascade ignores unrecognized tokens).
        internal static bool TryParse(string? cls, out SkewAxis axis, out float deg)
        {
            axis = SkewAxis.Both;
            deg = 0f;
            if (string.IsNullOrEmpty(cls) || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
            {
                return false;
            }

            var negative = cls[0] == '-';
            var body = negative ? cls.Substring(1) : cls;
            string suffix;
            if (body.StartsWith(XPrefix, StringComparison.Ordinal))
            {
                axis = SkewAxis.X;
                suffix = body.Substring(XPrefix.Length);
            }
            else if (body.StartsWith(YPrefix, StringComparison.Ordinal))
            {
                axis = SkewAxis.Y;
                suffix = body.Substring(YPrefix.Length);
            }
            else if (body.StartsWith(BothPrefix, StringComparison.Ordinal))
            {
                suffix = body.Substring(BothPrefix.Length);
            }
            else
            {
                return false;
            }

            if (suffix.Length == 0)
            {
                return false;
            }

            // Arbitrary form: an angle as skewX() takes one, a bare number only as 0. A malformed bracket (or
            // one TryStripBrackets rejects as empty) falls through to int.TryParse below, which rejects it just
            // the same (a bracket character is never a valid digit).
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                if (!StyleArbitraryValueResolver.TryParseAngleDegrees(inner, out var value)
                    || (value != 0f && StyleArbitraryValueResolver.IsUnitlessAngle(inner)))
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
