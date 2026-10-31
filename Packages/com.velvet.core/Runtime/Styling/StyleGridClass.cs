using System;

namespace Velvet
{
    // Parses Velvet's grid / grid-cols-* utilities into a column count for StyleGridManipulator, and extracts
    // the row / column gaps a grid container owns. UI Toolkit has no CSS Grid (its layout is a Flexbox subset),
    // so `grid` is realized as a flex-wrap row (_layout.uss) and the manipulator sizes each child to a 1/N
    // column. A bare `grid` with no grid-cols-N is a single column (CSS grid-template-columns: none).
    // Supports grid-cols-<n> for any positive n, and the bracket that spells the same columns,
    // grid-cols-[repeat(n,minmax(0,1fr))].
    internal static class StyleGridClass
    {
        // Single-token half of HasGridClass: true when cls is the bare `grid` marker or a grid-cols
        // utility. Its own predicate so the token set has ONE definition — both the array scan below and
        // the variant-payload gate (StyleVariantPayload) resolve the family through here.
        public static bool IsGridToken(string cls)
            => !string.IsNullOrEmpty(cls)
                && (cls == "grid" || cls.StartsWith("grid-cols-", StringComparison.Ordinal));

        // Cheap early-out gate: true when ANY class is the bare `grid` marker or a grid-cols utility. Skips
        // the full scan on the ~99% of elements that carry no grid spec.
        public static bool HasGridClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsGridToken(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Scans classNames for the last grid-cols-* (later classes win, matching CSS cascade) and returns its
        // column count, defaulting a bare `grid` (no grid-cols) to a single column. Returns false when neither
        // a grid-cols utility nor the bare grid marker is present.
        public static bool TryExtract(string[] classNames, out int columns)
        {
            columns = 0;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            var hasBareGrid = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var n))
                {
                    columns = n;
                    found = true;
                }
                else if (cls == "grid")
                {
                    hasBareGrid = true;
                }
            }
            if (found)
            {
                return true;
            }
            // A bare `grid` with no grid-cols-N is a single column (CSS grid-template-columns: none): the
            // manipulator then sizes each child to the full row width so they stack vertically.
            if (hasBareGrid)
            {
                columns = 1;
                return true;
            }
            return false;
        }

        // grid-cols-3 → 3; grid-cols-[repeat(5,minmax(0,1fr))] → 5. Returns false for a non-grid-cols class, a
        // non-positive count, or any other bracket.
        public static bool TryParse(string cls, out int columns)
        {
            columns = 0;
            if (string.IsNullOrEmpty(cls) || !cls.StartsWith("grid-cols-", StringComparison.Ordinal)
                || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
            {
                return false;
            }

            var suffix = cls.Substring("grid-cols-".Length);
            // Tailwind writes a bracket as grid-template-columns itself, so a bare grid-cols-[5] is a declaration the
            // browser rejects, and of the track lists only the one grid-cols-<n> writes has an equivalent in the
            // 1/N columns this emulation lays out. A malformed or empty bracket reaches int.TryParse whole, which
            // rejects it (a bracket character is never a valid digit).
            var numeric = suffix.AsSpan();
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                && !TryReadEqualColumns(inner.ToString(), out numeric))
            {
                return false;
            }

            if (int.TryParse(numeric, out var parsed) && parsed > 0)
            {
                columns = parsed;
                return true;
            }
            return false;
        }

        // The n of repeat(n,minmax(0,1fr)), the track list grid-cols-<n> writes, with spaces anywhere and the
        // function names in any case, as CSS reads them.
        private static bool TryReadEqualColumns(string tracks, out ReadOnlySpan<char> count)
        {
            count = default;
            var compact = tracks.Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
            const string open = "repeat(";
            const string close = ",minmax(0,1fr))";
            if (!compact.StartsWith(open, StringComparison.Ordinal) || !compact.EndsWith(close, StringComparison.Ordinal)
                || compact.Length <= open.Length + close.Length)
            {
                return false;
            }
            count = compact.AsSpan(open.Length, compact.Length - open.Length - close.Length);
            return true;
        }

        // Extracts the column gap and row gap (px) a grid owns from the gap-*/gap-x-*/gap-y-* classes.
        // Important wins over plain, then last wins per axis: gap-N sets both, gap-x-N the column gap,
        // gap-y-N the row gap.
        // space-* is not a gap.
        // A grid routes its gap through StyleGridManipulator, so the values are read here rather than left to
        // the (suppressed) gap manipulator. Defaults to 0 when no gap class is present.
        public static void ExtractGaps(string[] classNames, out float columnGap, out float rowGap)
            => ExtractGaps(classNames, out columnGap, out rowGap, out _, out _);

        // As above, with the gap the container measures where the winning class holds one.
        public static void ExtractGaps(string[] classNames, out float columnGap, out float rowGap,
            out StyleLengthExpression? columnMeasured, out StyleLengthExpression? rowMeasured)
        {
            columnGap = 0f;
            rowGap = 0f;
            columnMeasured = null;
            rowMeasured = null;
            var columnImportant = false;
            var rowImportant = false;
            if (classNames == null)
            {
                return;
            }

            foreach (var cls in classNames)
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                if (!StyleGapClass.TryParse(cls, out var g, out var axis, out var measured))
                {
                    continue;
                }
                // A space-* token is a margin on the children, which the grid adds on its own.
                if (StyleGapClass.IsSpaceToken(core))
                {
                    continue;
                }
                if (axis != GapAxis.Vertical && (important || !columnImportant))
                {
                    (columnGap, columnMeasured) = (g, measured);
                    columnImportant = important;
                }
                if (axis != GapAxis.Horizontal && (important || !rowImportant))
                {
                    (rowGap, rowMeasured) = (g, measured);
                    rowImportant = important;
                }
            }
        }
    }
}
