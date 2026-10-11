#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Velvet
{
    // Tailwind emits a shorthand ahead of the longhands it contains, so pt-2 beats p-13 whichever is written
    // first: padding, margin, inset and size/width/height sort that way in property-order.ts. Two utilities
    // of one family at one priority are therefore ordered here by that property order, in the place a border
    // side colour already is (StyleBorderSideColor.CompareCascade), instead of by which arrived last.
    //
    // A stylesheet class cannot take part: an inline value outranks every class on the element it sits on, so
    // `p-13 pt-2` would paint 52px at the top with pt-2 a class. Normalize rewrites such a class into its
    // bracket form (pt-2 becomes pt-[8px]) when an inline utility of an overlapping family stands beside it, so
    // both are inline layers and the order above decides.
    internal static class StyleSpacingPrecedence
    {
        private static readonly int[] s_rank = BuildRanks();

        private static int[] BuildRanks()
        {
            var ranks = new int[Enum.GetValues(typeof(ArbitraryProperty)).Length];
            Array.Fill(ranks, -1);
            Set(ranks, ArbitraryProperty.Padding, "padding");
            Set(ranks, ArbitraryProperty.PaddingX, "padding-inline");
            Set(ranks, ArbitraryProperty.PaddingY, "padding-block");
            Set(ranks, ArbitraryProperty.PaddingTop, "padding-top");
            Set(ranks, ArbitraryProperty.PaddingRight, "padding-right");
            Set(ranks, ArbitraryProperty.PaddingBottom, "padding-bottom");
            Set(ranks, ArbitraryProperty.PaddingLeft, "padding-left");
            Set(ranks, ArbitraryProperty.Margin, "margin");
            Set(ranks, ArbitraryProperty.MarginX, "margin-inline");
            Set(ranks, ArbitraryProperty.MarginY, "margin-block");
            Set(ranks, ArbitraryProperty.MarginTop, "margin-top");
            Set(ranks, ArbitraryProperty.MarginRight, "margin-right");
            Set(ranks, ArbitraryProperty.MarginBottom, "margin-bottom");
            Set(ranks, ArbitraryProperty.MarginLeft, "margin-left");
            Set(ranks, ArbitraryProperty.Inset, "inset");
            Set(ranks, ArbitraryProperty.InsetX, "inset-inline");
            Set(ranks, ArbitraryProperty.InsetY, "inset-block");
            Set(ranks, ArbitraryProperty.Top, "top");
            Set(ranks, ArbitraryProperty.Right, "right");
            Set(ranks, ArbitraryProperty.Bottom, "bottom");
            Set(ranks, ArbitraryProperty.Left, "left");
            Set(ranks, ArbitraryProperty.Height, "height");
            Set(ranks, ArbitraryProperty.Width, "width");
            // size-* declares width and height, and property-order.ts sorts height first, so it ahead of both w-*
            // and h-*: the first property it differs from w-* on is height, and from h-* the extra width.
            ranks[(int)ArbitraryProperty.Size] = ranks[(int)ArbitraryProperty.Height] - 1;
            return ranks;
        }

        // Twice the property's index in property-order.ts, which leaves the odd rank before it free for size.
        private static void Set(int[] ranks, ArbitraryProperty property, string tailwindProperty)
            => ranks[(int)property] = 2 * StyleRuleOrder.TailwindIndexOf(tailwindProperty);

        // The place of property's utility in Tailwind's order, or -1 for one outside these families.
        internal static int RankOf(ArbitraryProperty property) => s_rank[(int)property];

        // Two writers of one slot at one priority, ordered as Tailwind emits them; the border colour families
        // where either is one, and 0 where either is neither, which leaves them to arrival.
        internal static int CompareCascade(ArbitraryProperty a, ArbitraryProperty z)
        {
            var aRank = s_rank[(int)a];
            var zRank = s_rank[(int)z];
            return aRank < 0 || zRank < 0 ? StyleBorderSideColor.CompareCascade(a, z) : aRank.CompareTo(zRank);
        }

        // tokens with each stylesheet-backed spacing class that an inline utility of an overlapping family stands
        // beside rewritten to its bracket form; tokens itself when none is. Variant tokens are grouped by their
        // variants, so `sm:p-13 sm:pt-2` is judged apart from `p-13 pt-2`.
        internal static string[] Normalize(string[] tokens)
        {
            if (tokens.Length < 2)
            {
                return tokens;
            }
            // Almost every class list that reaches here has no stylesheet-backed spacing class beside an inline
            // one, so the cheap reading of the backed ones goes first.
            if (!HasStylesheetStep(tokens))
            {
                return tokens;
            }
            var readings = new List<Reading>();
            var inline = false;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryRead(tokens[i], i, out var reading))
                {
                    readings.Add(reading);
                    inline |= !reading.Backed;
                }
            }
            if (!inline)
            {
                return tokens;
            }
            string[]? rewritten = null;
            foreach (var candidate in readings)
            {
                if (candidate.Backed && IsNarrowerThanAnInlineNeighbour(candidate, readings))
                {
                    rewritten ??= (string[])tokens.Clone();
                    rewritten[candidate.Index] = Bracketed(candidate);
                }
            }
            return rewritten ?? tokens;
        }

        private static bool HasStylesheetStep(string[] tokens) => Array.Exists(tokens, IsStylesheetStep);

        private static bool IsStylesheetStep(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var variantsLength = VariantsLength(core);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            return StyleArbitraryValueResolver.TryReadSpacingStep(payload, out _, out var backed) && backed;
        }

        private readonly struct Reading
        {
            public int Index { get; init; }
            public string Variants { get; init; }
            public bool Important { get; init; }
            public bool TrailingBang { get; init; }
            public ArbitraryProperty Property { get; init; }
            public float Pixels { get; init; }
            public bool Backed { get; init; }
        }

        private static bool TryRead(string token, int index, out Reading reading)
        {
            reading = default;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (core.Length < 3)
            {
                return false;
            }
            var variantsLength = VariantsLength(core);
            var variants = core.Substring(0, variantsLength);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            var trailingBang = important && token[token.Length - 1] == '!';
            if (StyleArbitraryValueResolver.TryReadSpacingStep(payload, out var step, out var backed))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, Important = important, TrailingBang = trailingBang,
                    Property = step.Property, Pixels = step.Value, Backed = backed,
                };
                return true;
            }
            if (StyleArbitraryValueResolver.IsInlineResolved(payload)
                && StyleArbitraryValueResolver.TryParse(payload, out var style)
                && StyleArbitraryValueResolver.IsSpacingProperty(style.Property))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, Important = important, TrailingBang = trailingBang,
                    Property = style.Property, Pixels = style.Value, Backed = false,
                };
                return true;
            }
            return false;
        }

        // The length of the variants in front of a payload: up to and including the last ':' outside brackets.
        private static int VariantsLength(string core)
        {
            var depth = 0;
            var length = 0;
            for (var i = 0; i < core.Length; i++)
            {
                if (core[i] == '[')
                {
                    depth++;
                }
                else if (core[i] == ']')
                {
                    depth--;
                }
                else if (core[i] == ':' && depth == 0)
                {
                    length = i + 1;
                }
            }
            return length;
        }

        // Whether an inline reading in the same variants and importance writes some of what candidate does
        // without candidate containing all of it: where candidate contains it the inline one is the narrower
        // utility and an inline value beating a class is already the order Tailwind gives.
        private static bool IsNarrowerThanAnInlineNeighbour(Reading candidate, List<Reading> readings)
            => readings.Exists(other => !other.Backed && Narrows(candidate, other));

        private static bool Narrows(Reading candidate, Reading other)
        {
            if (other.Property == candidate.Property || other.Important != candidate.Important
                || other.Variants != candidate.Variants)
            {
                return false;
            }
            var written = StyleArbitraryLonghands.Of(candidate.Property);
            var otherWritten = StyleArbitraryLonghands.Of(other.Property);
            return written.Overlaps(otherWritten) && written.Union(otherWritten) != written;
        }

        private static string Bracketed(Reading candidate)
        {
            StyleArbitraryValueResolver.TryGetSpacingPrefix(candidate.Property, out var prefix);
            var pixels = candidate.Pixels.ToString("R", CultureInfo.InvariantCulture);
            var payload = prefix + "[" + pixels + "px]";
            var core = candidate.Variants + payload;
            if (!candidate.Important)
            {
                return core;
            }
            return candidate.TrailingBang ? core + "!" : "!" + core;
        }
    }
}
