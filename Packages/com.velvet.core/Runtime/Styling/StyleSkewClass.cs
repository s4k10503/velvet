#nullable enable
using System;

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
    // An axis takes the last class that writes it among those of the highest importance, a skew-<n> writing both.
    // Normalize has put the base classes in Tailwind's order (StyleFamilyOrder), and a variant payload is appended
    // after them, so the last one is the one Tailwind emits last: every skew-<n> beneath every skew-x-* and
    // skew-y-*, whichever is written first and whatever its sign, and a payload over the base.
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
            var core = StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out _);
            if (core.Length == 0)
            {
                return false;
            }
            var body = core[0] == '-' ? core.Substring(1) : core;
            return body.StartsWith(BothPrefix, StringComparison.Ordinal);
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

        // The skew token holding each axis (the token TryExtract stores in SkewSpec.SourceX / SourceY), or false when
        // neither axis has one. The patch path compares these against the live binding's SourceX / SourceY to skip
        // the parse in the steady state; both read Resolve, so a trailing unparseable token (e.g. ["skew-x-6",
        // "skew-x-junk"]) cannot make them disagree and defeat the fast path.
        public static bool TryGetWinningSkewClasses(string[]? classNames, out string? winnerX, out string? winnerY)
        {
            winnerX = null;
            winnerY = null;
            if (classNames == null)
            {
                return false;
            }
            Resolve(classNames, out var x, out var y);
            winnerX = x.Source;
            winnerY = y.Source;
            return winnerX != null || winnerY != null;
        }

        // Resolves the cascade into a SkewSpec. Returns true only when the result is ACTIVE (a non-zero angle on at
        // least one axis) — skew-x-0 resets its axis, so a list ending in resets extracts to false.
        public static bool TryExtract(string[]? classNames, out SkewSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }
            Resolve(classNames, out var x, out var y);
            // Both axes are written by one transform declaration, which a rejected angle holding either one makes
            // invalid (StyleCompositeRejection), leaving no skew.
            spec = x.Rejected || y.Rejected
                ? new SkewSpec(0f, 0f, x.Source, y.Source)
                : new SkewSpec(x.Deg, y.Deg, x.Source, y.Source);
            return spec.Active;
        }

        private struct Pick
        {
            public float Deg;
            public string? Source;
            public bool Important;
            public bool Rejected;
        }

        private static void Resolve(string[] classNames, out Pick x, out Pick y)
        {
            x = default;
            y = default;
            foreach (var cls in classNames)
            {
                if (!TryRead(cls, out var axis, out var deg, out var important, out var rejected))
                {
                    continue;
                }
                var pick = new Pick { Deg = deg, Source = cls, Important = important, Rejected = rejected };
                if (axis != SkewAxis.Y)
                {
                    Offer(ref x, pick);
                }
                if (axis != SkewAxis.X)
                {
                    Offer(ref y, pick);
                }
            }
        }

        // An important token takes an axis from a plain one, and between two of one importance the later does.
        private static void Offer(ref Pick held, Pick offered)
        {
            if (offered.Important || !held.Important)
            {
                held = offered;
            }
        }

        // TryParse, or a skew bracket whose angle the browser rejects, which holds its axes as any other skew class
        // does.
        private static bool TryRead(string? cls, out SkewAxis axis, out float deg, out bool important, out bool rejected)
        {
            rejected = false;
            if (TryParse(cls, out axis, out deg, out important))
            {
                return true;
            }
            var core = StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out important);
            rejected = StyleCompositeRejection.RejectsSkew(core, out axis);
            return rejected;
        }

        // The axes core, a skew utility without its important modifier, writes.
        internal static SkewAxis AxisOf(string core)
        {
            var at = core.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
            return string.CompareOrdinal(core, at, XPrefix, 0, XPrefix.Length) == 0 ? SkewAxis.X
                : string.CompareOrdinal(core, at, YPrefix, 0, YPrefix.Length) == 0 ? SkewAxis.Y
                : SkewAxis.Both;
        }

        // Parses one class: skew-x-<int> / -skew-x-<int> / skew-x-[<angle>] (+ the y and both-axes forms), with or
        // without the important modifier. Returns false for anything else (the cascade ignores unrecognized tokens).
        internal static bool TryParse(string? cls, out SkewAxis axis, out float deg, out bool important)
        {
            axis = SkewAxis.Both;
            deg = 0f;
            cls = StyleArbitraryValueResolver.StripImportant(cls ?? string.Empty, out important);
            if (cls.Length == 0 || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
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
            // one TryStripBrackets rejects as empty) falls through to TryParseWhole below, which rejects it just
            // the same (a bracket character is never a digit).
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

            // Numeric form: a whole number of degrees without a leading zero (the preset scale is a subset).
            if (!StyleArbitraryValueResolver.TryParseWhole(suffix.AsSpan(), out var whole))
            {
                return false;
            }
            deg = negative ? -whole : whole;
            return true;
        }
    }
}
