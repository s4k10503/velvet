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
    // An important token (!skew-x-6, skew-x-6!) outranks every plain one, as its !important declaration does. Between
    // two of one importance, a per-axis utility outranks skew-<n> on its axis wherever either is written, since
    // Tailwind emits skew-<n> ahead of skew-x-* and skew-y-* in its sheet, and within one of the three families the
    // extractors take the last token, which InTailwindOrder makes the one Tailwind emits last.
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
            var body = core.Length > 0 && core[0] == '-' ? core.Substring(1) : core;
            return body.StartsWith(BothPrefix, StringComparison.Ordinal);
        }

        // tokens with their variantless skew utilities put in Tailwind's order, in the positions they already hold, so
        // the last of a family is the one Tailwind emits last: skew-x-6 skew-x-12 and skew-x-12 skew-x-6 both end on
        // skew-x-12, and -skew-6 sorts ahead of skew-6. The important tokens are ordered apart from the plain ones, in
        // the positions the important ones hold, each by the candidate as written. A variant payload is
        // appended after these by the variant gate and keeps its place there. tokens itself when nothing moves.
        internal static string[] InTailwindOrder(string[] tokens)
        {
            var placed = Placed(null, tokens, important: false);
            return Placed(placed, tokens, important: true) ?? placed ?? tokens;
        }

        // into, or a copy of tokens when into is null, with the skew tokens of one importance sorted into the
        // positions they hold; null when they already are in order.
        private static string[]? Placed(string[]? into, string[] tokens, bool important)
        {
            System.Collections.Generic.List<int>? members = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (IsSkewClass(tokens[i]) && IsImportant(tokens[i]) == important)
                {
                    (members ??= new System.Collections.Generic.List<int>()).Add(i);
                }
            }
            if (members == null || members.Count < 2)
            {
                return null;
            }
            var ordered = members.ConvertAll(member => tokens[member]);
            // List.Sort is not stable; two equal candidates are the same string, so their order cannot show.
            ordered.Sort(StyleCandidateOrder.Compare);
            string[]? placed = null;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (!string.Equals(ordered[i], tokens[members[i]], StringComparison.Ordinal))
                {
                    (placed ??= into ?? (string[])tokens.Clone())[members[i]] = ordered[i];
                }
            }
            return placed;
        }

        private static bool IsImportant(string token)
        {
            StyleArbitraryValueResolver.StripImportant(token, out var important);
            return important;
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

        // The winning skew token per axis (the token TryExtract stores in SkewSpec.SourceX / SourceY), or false when
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
            spec = new SkewSpec(x.Deg, y.Deg, x.Source, y.Source);
            return spec.Active;
        }

        private struct Pick
        {
            public float Deg;
            public string? Source;
            public bool Important;
        }

        private static void Resolve(string[] classNames, out Pick x, out Pick y)
        {
            Pick perX = default, perY = default, both = default;
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var axis, out var deg, out var important))
                {
                    continue;
                }
                var pick = new Pick { Deg = deg, Source = cls, Important = important };
                switch (axis)
                {
                    case SkewAxis.X:
                        Offer(ref perX, pick);
                        break;
                    case SkewAxis.Y:
                        Offer(ref perY, pick);
                        break;
                    default:
                        Offer(ref both, pick);
                        break;
                }
            }
            x = OnAxis(perX, both);
            y = OnAxis(perY, both);
        }

        // An important token takes a family from a plain one, and between two of one importance the later does.
        private static void Offer(ref Pick held, Pick offered)
        {
            if (held.Source == null || offered.Important || !held.Important)
            {
                held = offered;
            }
        }

        // The per-axis pick holds its axis unless only the both-axes one is important.
        private static Pick OnAxis(Pick perAxis, Pick both)
            => perAxis.Source != null && (perAxis.Important || !both.Important) ? perAxis : both;

        // Parses one class: skew-x-<int> / -skew-x-<int> / skew-x-[<angle>] (+ the y and both-axes forms), with or
        // without the important modifier. Returns false for anything else (the cascade ignores unrecognized tokens).
        internal static bool TryParse(string? cls, out SkewAxis axis, out float deg)
            => TryParse(cls, out axis, out deg, out _);

        private static bool TryParse(string? cls, out SkewAxis axis, out float deg, out bool important)
        {
            axis = SkewAxis.Both;
            deg = 0f;
            important = false;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            cls = StyleArbitraryValueResolver.StripImportant(cls, out important);
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
