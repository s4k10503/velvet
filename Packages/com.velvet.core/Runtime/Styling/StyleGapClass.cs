using System;

namespace Velvet
{
    // Parses Velvet's gap-* / gap-x-* / gap-y-* utility classes (and the space-x-* /
    // space-y-* family, and the gap-[..] / gap-x-[..] JIT arbitrary form) into a pixel gap value
    // and the axis they space along, for StyleGapManipulator. The numeric scale mirrors the
    // --space-* tokens in _tokens.uss (1 unit = 4px), keeping gap-* spacing visually consistent
    // with the padding/margin scale elsewhere in the utility set.
    // A gap or space utility as StyleGapManipulator applies it: the pixel value, the axis, whether it is a
    // space-x-* / space-y-* token rather than a gap-* one (see StyleGapManipulator.ResolveEdge), and the
    // space-x-reverse / space-y-reverse markers.
    internal readonly struct GapSpec
    {
        public readonly float Gap;
        public readonly GapAxis Axis;
        public readonly bool Space;
        public readonly bool XReverse;
        public readonly bool YReverse;

        public GapSpec(float gap, GapAxis axis, bool space, bool xReverse, bool yReverse)
        {
            Gap = gap;
            Axis = axis;
            Space = space;
            XReverse = xReverse;
            YReverse = yReverse;
        }
    }

    internal static class StyleGapClass
    {
        // Returns true and the parsed gap / axis when
        // cls is a recognized gap utility. gap-x-* → horizontal,
        // gap-y-* → vertical, plain gap-* → GapAxis.Auto (follows
        // flex-direction).
        public static bool TryParse(string cls, out float gap, out GapAxis axis)
            => TryParse(cls, out gap, out axis, out _);

        // space is true for the space-x-* / space-y-* family: Tailwind's margin rule rather than CSS gap, so
        // StyleGapManipulator takes its edge from the reverse marker alone and never switches to the wrap path.
        public static bool TryParse(string cls, out float gap, out GapAxis axis, out bool space)
        {
            gap = 0f;
            axis = GapAxis.Auto;
            space = false;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            string suffix;
            // The space-x-reverse / space-y-reverse markers carry no pixel value of their own, so they
            // decline here the same as any other unrecognized suffix — StyleGapManipulator reads them
            // separately via ExtractReverseMarkers.
            if (cls.StartsWith("space-x-", StringComparison.Ordinal))
            {
                axis = GapAxis.Horizontal;
                space = true;
                suffix = cls.Substring("space-x-".Length);
            }
            else if (cls.StartsWith("space-y-", StringComparison.Ordinal))
            {
                axis = GapAxis.Vertical;
                space = true;
                suffix = cls.Substring("space-y-".Length);
            }
            else if (cls.StartsWith("gap-x-", StringComparison.Ordinal))
            {
                axis = GapAxis.Horizontal;
                suffix = cls.Substring("gap-x-".Length);
            }
            else if (cls.StartsWith("gap-y-", StringComparison.Ordinal))
            {
                axis = GapAxis.Vertical;
                suffix = cls.Substring("gap-y-".Length);
            }
            else if (cls.StartsWith("gap-", StringComparison.Ordinal))
            {
                axis = GapAxis.Auto;
                suffix = cls.Substring("gap-".Length);
            }
            else
            {
                return false;
            }

            // Arbitrary value: gap-[20px] / gap-x-[12px] (JIT arbitrary value). Gap is realized as a pixel
            // inter-child margin, so a percentage value is rejected (only px / unitless is meaningful).
            // TryParseArbitraryPixels already verifies the bracket shape itself, so a non-bracket suffix falls
            // straight through to the preset scale below without needing its own duplicate guard here.
            if (StyleArbitraryValueResolver.TryParseArbitraryPixels(suffix.AsSpan(), out gap))
            {
                return true;
            }

            // The numeric scale is the shared --space-* preset table (1 unit = 4px), so gap-* resolves the same
            // values as mt-* / p-*; single-sourced in StyleArbitraryValueResolver to avoid a divergent copy.
            return StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out gap);
        }

        // Single-token half of HasGapClass: true when cls belongs to the gap / space family the gap
        // manipulator is gated on. Its own predicate so the prefix set has ONE definition — both the array
        // scan below and the variant-payload gate (StyleVariantPayload) resolve the family through here.
        public static bool IsGapToken(string cls)
            => !string.IsNullOrEmpty(cls)
                && (cls.StartsWith("gap-", StringComparison.Ordinal)
                    || cls.StartsWith("space-x-", StringComparison.Ordinal)
                    || cls.StartsWith("space-y-", StringComparison.Ordinal));

        // Cheap early-out gate: true when ANY class begins with the gap- prefix. No dictionary
        // lookup and no substring allocation — used to skip the full TryExtract scan on the
        // ~99% of elements that carry no gap class at all.
        public static bool HasGapClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsGapToken(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Scans classNames for the space-x-reverse / space-y-reverse markers.
        public static void ExtractReverseMarkers(string[] classNames, out bool xReverse, out bool yReverse)
        {
            xReverse = false;
            yReverse = false;
            if (classNames == null)
            {
                return;
            }
            foreach (var cls in classNames)
            {
                if (cls == "space-x-reverse")
                {
                    xReverse = true;
                }
                else if (cls == "space-y-reverse")
                {
                    yReverse = true;
                }
            }
        }

        // Scans classNames for the last gap utility (later classes win, matching CSS
        // cascade order) and returns it. Returns false when no gap utility is present.
        public static bool TryExtract(string[] classNames, out float gap, out GapAxis axis, out bool space)
        {
            gap = 0f;
            axis = GapAxis.Auto;
            space = default;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var g, out var a, out var s))
                {
                    gap = g;
                    axis = a;
                    space = s;
                    found = true;
                }
            }
            return found;
        }
    }
}
