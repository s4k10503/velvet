using System;

namespace Velvet
{
    // The space-x-* / space-y-* margins (0 when absent) and the space-x-reverse / space-y-reverse markers.
    internal readonly struct SpaceSpec
    {
        public readonly float X;
        public readonly float Y;
        public readonly bool XReverse;
        public readonly bool YReverse;
        // A margin each child measures (space-x-[1em]) in place of X or Y; null where that one is in pixels.
        public readonly StyleLengthExpression? XMeasured;
        public readonly StyleLengthExpression? YMeasured;

        public SpaceSpec(float x, float y, bool xReverse, bool yReverse,
            StyleLengthExpression? xMeasured = null, StyleLengthExpression? yMeasured = null)
        {
            X = x;
            Y = y;
            XReverse = xReverse;
            YReverse = yReverse;
            XMeasured = xMeasured;
            YMeasured = yMeasured;
        }

        public bool IsMeasured => XMeasured != null || YMeasured != null;
    }

    // What StyleGapManipulator applies to one container: CSS's column-gap and row-gap (gap-x-* / gap-y-*, both
    // from gap-*; each Has flag false when no token sets it) and the space margins. The gaps and the space are
    // independent, as they are in Tailwind, where the gap is the container's own property and the space a
    // margin on its children.
    internal readonly struct GapSpec
    {
        public readonly bool HasColumnGap;
        public readonly float ColumnGap;
        public readonly bool HasRowGap;
        public readonly float RowGap;
        public readonly SpaceSpec Space;
        // A gap the container measures (gap-[1em]) in place of ColumnGap or RowGap; null where it is in pixels.
        public readonly StyleLengthExpression? ColumnGapMeasured;
        public readonly StyleLengthExpression? RowGapMeasured;

        public GapSpec(bool hasColumnGap, float columnGap, bool hasRowGap, float rowGap, SpaceSpec space,
            StyleLengthExpression? columnGapMeasured = null, StyleLengthExpression? rowGapMeasured = null)
        {
            HasColumnGap = hasColumnGap;
            ColumnGap = columnGap;
            HasRowGap = hasRowGap;
            RowGap = rowGap;
            Space = space;
            ColumnGapMeasured = columnGapMeasured;
            RowGapMeasured = rowGapMeasured;
        }

        // Whether this spec writes anything: a space-x-0 / space-y-0 writes no margin.
        public bool IsActive => HasColumnGap || HasRowGap || Space.X != 0f || Space.Y != 0f || Space.IsMeasured;

        public bool IsMeasured => ColumnGapMeasured != null || RowGapMeasured != null || Space.IsMeasured;
    }

    // Parses Velvet's gap-* / gap-x-* / gap-y-* utility classes (and the space-x-* /
    // space-y-* family, and the gap-[..] / gap-x-[..] JIT arbitrary form) into a pixel gap value
    // and the axis they space along, for StyleGapManipulator. The numeric scale is the one padding and margin
    // read (StyleArbitraryValueResolver.TryGetSpacingPx), keeping gap-* spacing consistent with it.
    internal static class StyleGapClass
    {
        // Returns true and the parsed gap / axis when
        // cls is a recognized gap or space utility. gap-x-* / space-x-* → horizontal,
        // gap-y-* / space-y-* → vertical, plain gap-* → GapAxis.Auto (follows
        // flex-direction). -space-x-* / -space-y-* are negative, as in Tailwind; a gap has no negative form.
        public static bool TryParse(string cls, out float gap, out GapAxis axis) => TryParse(cls, out gap, out axis, out _);

        // As above, with the length the element measures where the bracket holds one (gap then 0): the container
        // for a gap, each child for a space margin.
        public static bool TryParse(string cls, out float gap, out GapAxis axis, out StyleLengthExpression? measured)
        {
            measured = null;
            gap = 0f;
            axis = GapAxis.Auto;
            if (string.IsNullOrEmpty(cls) || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
            {
                return false;
            }

            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            var negate = cls.StartsWith("-space-", StringComparison.Ordinal);
            var body = negate ? cls.Substring(1) : cls;
            string suffix;
            // The space-x-reverse / space-y-reverse markers carry no pixel value of their own, so they
            // decline here the same as any other unrecognized suffix — StyleGapManipulator reads them
            // separately via ExtractReverseMarkers.
            if (body.StartsWith("space-x-", StringComparison.Ordinal))
            {
                axis = GapAxis.Horizontal;
                suffix = body.Substring("space-x-".Length);
            }
            else if (body.StartsWith("space-y-", StringComparison.Ordinal))
            {
                axis = GapAxis.Vertical;
                suffix = body.Substring("space-y-".Length);
            }
            else if (core.StartsWith("gap-x-", StringComparison.Ordinal))
            {
                axis = GapAxis.Horizontal;
                suffix = core.Substring("gap-x-".Length);
            }
            else if (core.StartsWith("gap-y-", StringComparison.Ordinal))
            {
                axis = GapAxis.Vertical;
                suffix = core.Substring("gap-y-".Length);
            }
            else if (core.StartsWith("gap-", StringComparison.Ordinal))
            {
                axis = GapAxis.Auto;
                suffix = core.Substring("gap-".Length);
            }
            else
            {
                return false;
            }

            // Arbitrary value: gap-[20px] / gap-x-[12px] (JIT arbitrary value). Gap is realized as a pixel
            // inter-child margin, so a percentage value is rejected (only px / unitless is meaningful).
            // TryParseArbitraryPixels already verifies the bracket shape itself, so a non-bracket suffix falls
            // straight through to the preset scale below without needing its own duplicate guard here.
            // The numeric scale is TryGetSpacingPx's, so gap-* resolves the same values as mt-* / p-*.
            if (!StyleArbitraryValueResolver.TryParseArbitraryPixels(suffix.AsSpan(), out gap)
                && !StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out gap))
            {
                // A space margin takes a negative length, a gap none.
                if (!StyleArbitraryValueResolver.TryParseMeasuredPixels(suffix.AsSpan(), out measured,
                        allowNegative: axis != GapAxis.Auto && body.StartsWith("space-", StringComparison.Ordinal)))
                {
                    return false;
                }
                measured = negate ? measured!.Negated() : measured;
                return true;
            }
            if (negate)
            {
                gap = -gap;
            }
            return true;
        }

        // Single-token half of HasGapClass: true when cls belongs to the gap / space family the gap
        // manipulator is gated on. Its own predicate so the prefix set has ONE definition — both the array
        // scan below and the variant-payload gate (StyleVariantPayload) resolve the family through here.
        public static bool IsGapToken(string cls)
            => !string.IsNullOrEmpty(cls)
                && (StyleArbitraryValueResolver.StripImportant(cls, out _).StartsWith("gap-", StringComparison.Ordinal)
                    || IsSpaceToken(cls));

        // True for the space-x-* / space-y-* family, negative forms and reverse markers included.
        public static bool IsSpaceToken(string cls)
            => cls.StartsWith("space-x-", StringComparison.Ordinal)
                || cls.StartsWith("space-y-", StringComparison.Ordinal)
                || cls.StartsWith("-space-", StringComparison.Ordinal);

        // Cheap early-out gate: true when ANY class belongs to the gap / space family, including important
        // gap tokens. No dictionary lookup — used to skip the full TryExtract scan on the
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

        // Which of CSS's column-gap and row-gap the gap-* tokens in classNames set: gap-x-* the first, gap-y-*
        // the second, a plain gap-* both. The values are StyleGridClass.ExtractGaps'; a space-* token is not a
        // gap.
        public static void ExtractGapAxes(string[] classNames, out bool hasColumnGap, out bool hasRowGap)
        {
            hasColumnGap = false;
            hasRowGap = false;
            foreach (var cls in classNames)
            {
                if (IsSpaceToken(cls))
                {
                    continue;
                }
                if (!TryParse(cls, out _, out var axis))
                {
                    continue;
                }
                hasColumnGap |= axis != GapAxis.Vertical;
                hasRowGap |= axis != GapAxis.Horizontal;
            }
        }

        // The last space-x-* and the last space-y-* value in classNames, each 0 when absent.
        public static void ExtractSpace(string[] classNames, out float spaceX, out float spaceY)
            => ExtractSpace(classNames, out spaceX, out spaceY, out _, out _);

        // As above, with the margin each child measures where the last class of an axis holds one.
        private static void ExtractSpace(string[] classNames, out float spaceX, out float spaceY,
            out StyleLengthExpression? measuredX, out StyleLengthExpression? measuredY)
        {
            spaceX = 0f;
            spaceY = 0f;
            measuredX = null;
            measuredY = null;
            foreach (var cls in classNames)
            {
                if (!IsSpaceToken(cls))
                {
                    continue;
                }
                if (!TryParse(cls, out var value, out var axis, out var measured))
                {
                    continue;
                }
                if (axis == GapAxis.Horizontal)
                {
                    (spaceX, measuredX) = (value, measured);
                }
                else
                {
                    (spaceY, measuredY) = (value, measured);
                }
            }
        }

        // The space margins and markers in classNames.
        public static SpaceSpec ExtractSpaceSpec(string[] classNames)
        {
            ExtractSpace(classNames, out var spaceX, out var spaceY, out var measuredX, out var measuredY);
            ExtractReverseMarkers(classNames, out var xReverse, out var yReverse);
            return new SpaceSpec(spaceX, spaceY, xReverse, yReverse, measuredX, measuredY);
        }

        // The whole spec StyleGapManipulator runs on.
        public static GapSpec Extract(string[] classNames)
        {
            ExtractGapAxes(classNames, out var hasColumnGap, out var hasRowGap);
            StyleGridClass.ExtractGaps(classNames, out var columnGap, out var rowGap, out var columnMeasured, out var rowMeasured);
            return new GapSpec(hasColumnGap, columnGap, hasRowGap, rowGap, ExtractSpaceSpec(classNames),
                columnMeasured, rowMeasured);
        }
    }
}
