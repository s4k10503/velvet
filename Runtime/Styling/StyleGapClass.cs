using System;

namespace Velvet
{
    // Parses Velvet's gap-* / gap-x-* / gap-y-* utility classes (and the space-x-* /
    // space-y-* aliases, and the gap-[..] / gap-x-[..] JIT arbitrary form) into a pixel gap value
    // and the axis they space along, for StyleGapManipulator. The numeric scale mirrors the
    // --space-* tokens in _tokens.uss (1 unit = 4px), keeping gap-* spacing visually consistent
    // with the padding/margin scale elsewhere in the utility set.
    internal static class StyleGapClass
    {
        // Returns true and the parsed gap / axis when
        // cls is a recognized gap utility. gap-x-* → horizontal,
        // gap-y-* → vertical, plain gap-* → GapAxis.Auto (follows
        // flex-direction).
        public static bool TryParse(string cls, out float gap, out GapAxis axis)
        {
            gap = 0f;
            axis = GapAxis.Auto;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            string suffix;
            // space-x-*/space-y-* alias onto the gap axes: Velvet realizes inter-child
            // spacing as a leading margin on every child but the first (the gap manipulator), which
            // is exactly what the `space-* > * + *` margin rule produces for a flex container.
            // (Deviation: takes effect only inside a flex container.) The space-x-reverse /
            // space-y-reverse markers carry no pixel value of their own, so they decline here the same as
            // any other unrecognized suffix ("reverse" matches neither the arbitrary-pixel nor the preset
            // scale parse below) — StyleGapManipulator reads them separately via ExtractReverseMarkers,
            // since they flip which edge the gap lands on rather than contributing a gap value themselves.
            if (cls.StartsWith("space-x-", StringComparison.Ordinal))
            {
                axis = GapAxis.Horizontal;
                suffix = cls.Substring("space-x-".Length);
            }
            else if (cls.StartsWith("space-y-", StringComparison.Ordinal))
            {
                axis = GapAxis.Vertical;
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
                if (!string.IsNullOrEmpty(cls)
                    && (cls.StartsWith("gap-", StringComparison.Ordinal)
                        || cls.StartsWith("space-x-", StringComparison.Ordinal)
                        || cls.StartsWith("space-y-", StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            return false;
        }

        // Scans classNames for the space-x-reverse / space-y-reverse markers. Tailwind's markers are
        // absolute ("put the margin on the trailing edge") and never consult flex-direction themselves —
        // StyleGapManipulator is the one that OR's a marker with a detected row-reverse/column-reverse, so
        // the idiomatic flex-row-reverse space-x-4 space-x-reverse combination still lands trailing rather
        // than cancelling back to leading.
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
        public static bool TryExtract(string[] classNames, out float gap, out GapAxis axis)
        {
            gap = 0f;
            axis = GapAxis.Auto;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var g, out var a))
                {
                    gap = g;
                    axis = a;
                    found = true;
                }
            }
            return found;
        }
    }
}
