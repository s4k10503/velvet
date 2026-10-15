using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind's text-<size> scale: each size is paired with the line height its --text-<size>--line-height
    // declares. The font size itself is _tokens.uss's and the .text-<size> rules write it; the pixel sizes
    // here serve the text-<size>/<leading> form, which has no rule to carry them, and
    // NamedTextSizeSheetTests fails when they stop matching the sheet.
    internal static class StyleNamedTextSize
    {
        private const string Prefix = "text-";

        // Line heights are the em multipliers Tailwind spells as calc(<rem> / <font-size rem>).
        private static readonly Dictionary<string, (float FontSizePx, float LineHeightEm)> s_sizes =
            new(StringComparer.Ordinal)
            {
                ["xs"] = (12f, 1f / 0.75f),
                ["sm"] = (14f, 1.25f / 0.875f),
                ["base"] = (16f, 1.5f / 1f),
                ["lg"] = (18f, 1.75f / 1.125f),
                ["xl"] = (20f, 1.75f / 1.25f),
                ["2xl"] = (24f, 2f / 1.5f),
                ["3xl"] = (30f, 2.25f / 1.875f),
                ["4xl"] = (36f, 2.5f / 2.25f),
                ["5xl"] = (48f, 1f),
                ["6xl"] = (60f, 1f),
                ["7xl"] = (72f, 1f),
                ["8xl"] = (96f, 1f),
                ["9xl"] = (128f, 1f),
            };

        internal static IEnumerable<KeyValuePair<string, float>> FontSizesPx()
        {
            foreach (var pair in s_sizes)
            {
                yield return new KeyValuePair<string, float>(pair.Key, pair.Value.FontSizePx);
            }
        }

        // The line height a text-<size> or text-<size>/<leading> token sets. The slash form's leading reads
        // as the leading-* utility of the same suffix (text-sm/6 == leading-6), and a suffix that is no
        // leading-* value leaves the token unrecognised.
        public static bool TryParseLineHeight(string core, out LeadingValue lineHeight)
        {
            lineHeight = default;
            if (!TrySplit(core, out var size, out var suffix))
            {
                return false;
            }
            if (suffix == null)
            {
                lineHeight = new LeadingValue(LeadingUnit.Em, s_sizes[size].LineHeightEm);
                return true;
            }
            return StyleTextEffectClass.TryParseLeading("leading-" + suffix, out lineHeight);
        }

        // The font size of a text-<size>/<leading> token, the one form no USS rule writes.
        public static bool TryParseSlashFontSize(string core, out float fontSizePx)
        {
            fontSizePx = 0f;
            if (!TrySplit(core, out var size, out var suffix) || suffix == null
                || !StyleTextEffectClass.TryParseLeading("leading-" + suffix, out _))
            {
                return false;
            }
            fontSizePx = s_sizes[size].FontSizePx;
            return true;
        }

        private static bool TrySplit(string core, out string size, out string? suffix)
        {
            size = string.Empty;
            suffix = null;
            if (core == null || !core.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            var slash = core.IndexOf('/');
            var end = slash == -1 ? core.Length : slash;
            var name = core.Substring(Prefix.Length, end - Prefix.Length);
            if (!s_sizes.ContainsKey(name))
            {
                return false;
            }
            size = name;
            suffix = slash == -1 ? null : core.Substring(slash + 1);
            return true;
        }
    }
}
