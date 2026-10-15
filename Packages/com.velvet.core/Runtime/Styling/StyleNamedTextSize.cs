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

        // The line height a text-<size>, text-<size>/<leading> or text-[<length>]/<leading> token sets. The
        // slash form's leading reads as the leading-* utility of the same suffix (text-sm/6 == leading-6), and
        // a suffix that is no leading-* value leaves the token unrecognised. A bracket size alone pairs nothing.
        public static bool TryParseLineHeight(string core, out LeadingValue lineHeight)
        {
            lineHeight = default;
            if (!TryRead(core, out _, out var pairedEm, out var suffix))
            {
                return false;
            }
            if (suffix == null)
            {
                lineHeight = new LeadingValue(LeadingUnit.Em, pairedEm);
                return true;
            }
            return StyleTextEffectClass.TryParseLeading("leading-" + suffix, out lineHeight);
        }

        // The font size of a slash token, the form no USS rule writes.
        public static bool TryParseSlashFontSize(string core, out ArbitraryStyle fontSize)
        {
            fontSize = default;
            if (!TryRead(core, out var size, out _, out var suffix) || suffix == null)
            {
                return false;
            }
            if (!StyleTextEffectClass.TryParseLeading("leading-" + suffix, out _))
            {
                return false;
            }
            fontSize = size;
            return true;
        }

        // text-[length:<value>] is text-[<value>], Tailwind's type hint for a length.
        public static bool TryStripLengthHint(string className, out string stripped)
        {
            const string hinted = "text-[length:";
            stripped = string.Empty;
            if (!className.StartsWith(hinted, StringComparison.Ordinal))
            {
                return false;
            }
            stripped = "text-[" + className.Substring(hinted.Length);
            return true;
        }

        // Reads the size part of a token: a named size (its font size and paired line height, with an optional
        // /<suffix>) or a bracket size, which only counts with a suffix.
        private static bool TryRead(string core, out ArbitraryStyle size, out float pairedEm, out string? suffix)
        {
            size = default;
            pairedEm = 0f;
            suffix = null;
            if (core == null || !core.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }
            if (core.StartsWith("text-[", StringComparison.Ordinal))
            {
                var close = core.IndexOf("]/", StringComparison.Ordinal);
                if (close == -1)
                {
                    return false;
                }
                if (!StyleArbitraryValueResolver.TryParse(core.Substring(0, close + 1), out size))
                {
                    return false;
                }
                if (size.Property != ArbitraryProperty.FontSize)
                {
                    return false;
                }
                suffix = core.Substring(close + 2);
                return true;
            }
            var slash = core.IndexOf('/');
            var end = slash == -1 ? core.Length : slash;
            if (!s_sizes.TryGetValue(core.Substring(Prefix.Length, end - Prefix.Length), out var entry))
            {
                return false;
            }
            size = new ArbitraryStyle(ArbitraryProperty.FontSize, entry.FontSizePx, LengthUnit.Pixel);
            pairedEm = entry.LineHeightEm;
            suffix = slash == -1 ? null : core.Substring(slash + 1);
            return true;
        }
    }
}
