using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Tailwind's text-<size> scale: each size is paired with the line height its --text-<size>--line-height
    // declares. The font size itself is _tokens.uss's and the .text-<size> rules write it; the pixel sizes
    // here serve the text-<size>/<leading> form, which has no rule to carry them. NamedTextSizeSheetTests
    // fails when the sizes or their order stop matching the sheets.
    internal static class StyleNamedTextSize
    {
        private const string Prefix = "text-";

        // The rank of a slash token, above every named size: its font size is inline, which outranks the
        // .text-<size> rules.
        public const int SlashRank = 1000;

        // Rank is the place of the size's rule in _typography.uss, where the later rule wins the font size.
        // Line heights are the em multipliers Tailwind spells as calc(<rem> / <font-size rem>).
        private readonly struct SizeEntry
        {
            public readonly string Name;
            public readonly float FontSizePx;
            public readonly float LineHeightEm;

            public SizeEntry(string name, float fontSizePx, float lineHeightEm)
            {
                Name = name;
                FontSizePx = fontSizePx;
                LineHeightEm = lineHeightEm;
            }
        }

        private static readonly SizeEntry[] s_sizes =
        {
            new("xs", 12f, 1f / 0.75f),
            new("sm", 14f, 1.25f / 0.875f),
            new("base", 16f, 1.5f / 1f),
            new("lg", 18f, 1.75f / 1.125f),
            new("xl", 20f, 1.75f / 1.25f),
            new("2xl", 24f, 2f / 1.5f),
            new("3xl", 30f, 2.25f / 1.875f),
            new("4xl", 36f, 2.5f / 2.25f),
            new("5xl", 48f, 1f),
            new("6xl", 60f, 1f),
            new("7xl", 72f, 1f),
            new("8xl", 96f, 1f),
            new("9xl", 128f, 1f),
        };

        // The pair of every named size, for the cache of their formatted tags.
        public static IEnumerable<LeadingValue> PairedLineHeights()
        {
            foreach (var entry in s_sizes)
            {
                yield return new LeadingValue(LeadingUnit.Em, entry.LineHeightEm);
            }
        }

        // The line height a text-<size>, text-<size>/<leading> or text-[<length>]/<leading> token sets, and its
        // rank among the size tokens of one element (the highest rank is the one whose font size shows). The
        // slash form's leading reads as the leading-* utility of the same suffix (text-sm/6 == leading-6), and
        // a suffix that is no leading-* value leaves the token unrecognised. A bracket size alone pairs nothing.
        public static bool TryParseLineHeight(string core, out LeadingValue lineHeight, out int rank)
        {
            lineHeight = default;
            if (!TryRead(core, out _, out var pairedEm, out rank, out var suffixAt))
            {
                return false;
            }
            if (suffixAt == -1)
            {
                lineHeight = new LeadingValue(LeadingUnit.Em, pairedEm);
                return true;
            }
            rank = SlashRank;
            return StyleTextEffectClass.TryParseLeading("leading-" + core.Substring(suffixAt), out lineHeight);
        }

        // The font size of a slash token, the form no USS rule writes.
        public static bool TryParseSlashFontSize(string core, out ArbitraryStyle fontSize)
        {
            fontSize = default;
            if (!TryRead(core, out var size, out _, out _, out var suffixAt) || suffixAt == -1)
            {
                return false;
            }
            if (!StyleTextEffectClass.TryParseLeading("leading-" + core.Substring(suffixAt), out _))
            {
                return false;
            }
            fontSize = size;
            return true;
        }

        // Reads the size part of a token: a named size (its font size, paired line height and rank, with an
        // optional /<suffix>) or a bracket size, which only counts with a suffix. suffixAt is where the suffix
        // starts, or -1 when the token has none.
        private static bool TryRead(string core, out ArbitraryStyle size, out float pairedEm, out int rank,
            out int suffixAt)
        {
            size = default;
            pairedEm = 0f;
            rank = -1;
            suffixAt = -1;
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
                var sizeText = core.Substring(0, close + 1);
                if (!StyleArbitraryValueResolver.TryParse(sizeText, out size) || size.Property != ArbitraryProperty.FontSize)
                {
                    return false;
                }
                suffixAt = close + 2;
                return true;
            }
            var slash = core.IndexOf('/');
            var nameLength = (slash == -1 ? core.Length : slash) - Prefix.Length;
            for (var i = 0; i < s_sizes.Length; i++)
            {
                var entry = s_sizes[i];
                if (entry.Name.Length != nameLength
                    || string.CompareOrdinal(core, Prefix.Length, entry.Name, 0, nameLength) != 0)
                {
                    continue;
                }
                size = new ArbitraryStyle(ArbitraryProperty.FontSize, entry.FontSizePx, LengthUnit.Pixel);
                pairedEm = entry.LineHeightEm;
                rank = i;
                suffixAt = slash == -1 ? -1 : slash + 1;
                return true;
            }
            return false;
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
    }
}
