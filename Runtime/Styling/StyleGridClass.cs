using System;

namespace Velvet
{
    // Parses Velvet's grid / grid-cols-* utilities into a column count for StyleGridManipulator, and extracts
    // the row / column gaps a grid container owns. UI Toolkit has no CSS Grid (its layout is a Flexbox subset),
    // so `grid` is realized as a flex-wrap row (_layout.uss) and the manipulator sizes each child to a 1/N
    // column. A bare `grid` with no grid-cols-N is a single column (CSS grid-template-columns: none).
    // Supports grid-cols-1..12 and the integer grid-cols-[N] form.
    internal static class StyleGridClass
    {
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
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (cls == "grid" || cls.StartsWith("grid-cols-", StringComparison.Ordinal))
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

        // grid-cols-3 → 3; grid-cols-[5] → 5. Returns false for a non-grid-cols class or a non-positive count.
        public static bool TryParse(string cls, out int columns)
        {
            columns = 0;
            if (string.IsNullOrEmpty(cls) || !cls.StartsWith("grid-cols-", StringComparison.Ordinal))
            {
                return false;
            }

            var suffix = cls.Substring("grid-cols-".Length);
            // Arbitrary grid-cols-[N] (JIT arbitrary value) — only an integer column COUNT is supported; a CSS track
            // list (grid-cols-[1fr_200px] / repeat(...)) has no Flexbox equivalent and no-ops here.
            if (suffix.Length >= 2 && suffix[0] == '[' && suffix[suffix.Length - 1] == ']')
            {
                suffix = suffix.Substring(1, suffix.Length - 2);
            }

            if (int.TryParse(suffix, out var parsed) && parsed > 0)
            {
                columns = parsed;
                return true;
            }
            return false;
        }

        // Extracts the column gap and row gap (px) a grid owns from the gap-*/gap-x-*/gap-y-* classes (last
        // wins per axis, matching CSS cascade): gap-N sets both, gap-x-N the column gap, gap-y-N the row gap.
        // A grid routes its gap through StyleGridManipulator, so the values are read here rather than left to
        // the (suppressed) gap manipulator. Defaults to 0 when no gap class is present.
        public static void ExtractGaps(string[] classNames, out float columnGap, out float rowGap)
        {
            columnGap = 0f;
            rowGap = 0f;
            if (classNames == null)
            {
                return;
            }

            foreach (var cls in classNames)
            {
                if (!StyleGapClass.TryParse(cls, out var g, out var axis))
                {
                    continue;
                }
                switch (axis)
                {
                    case GapAxis.Horizontal:
                        columnGap = g;
                        break;
                    case GapAxis.Vertical:
                        rowGap = g;
                        break;
                    default:
                        columnGap = g;
                        rowGap = g;
                        break;
                }
            }
        }
    }
}
