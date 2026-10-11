#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind emits a shorthand ahead of the longhands it contains, so pt-2 beats p-13 whichever is written
    // first: padding, margin, inset and size/width/height sort that way in property-order.ts. Two utilities
    // of one family at one priority are therefore ordered here by that property order, in the place a border
    // side colour already is (StyleBorderSideColor.CompareCascade), instead of by which arrived last.
    //
    // A stylesheet class cannot take part: an inline value outranks every class on the element it sits on, so
    // `p-13 pt-2` would paint 52px at the top with pt-2 a class. Normalize rewrites such a class into an inline
    // form (pt-2 becomes pt-[8px]) when an inline utility of an overlapping family stands beside it, so both
    // are inline layers and the order above decides.
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
        // beside rewritten to its inline form (pt-2 as pt-[8px], mx-auto as mx-[auto], w-full as w-[100%]), and
        // each logical utility dropped for the physical one of its own side (ps-13 beside pl-2: Tailwind sorts
        // padding-inline-start ahead of padding-left, so the physical one holds the side whichever is written
        // first); tokens itself when neither applies. Tokens are grouped by importance and by the set of their
        // variants, so `sm:p-13 sm:pt-2` is judged apart from `p-13 pt-2`, and `hover:focus:` is `focus:hover:`.
        internal static string[] Normalize(string[] tokens)
        {
            if (tokens.Length < 2)
            {
                return tokens;
            }
            // Almost every class list that reaches here has no stylesheet-backed spacing class and no logical
            // utility, so the cheap reading of those goes first.
            if (!Array.Exists(tokens, IsStylesheetClassOrLogical))
            {
                return tokens;
            }
            var readings = ReadAll(tokens);
            string[]? rewritten = null;
            List<int>? dropped = null;
            foreach (var reading in readings)
            {
                if (reading.Logical && readings.Exists(other => !other.Logical && SameSlot(reading, other)))
                {
                    (dropped ??= new List<int>()).Add(reading.Index);
                }
            }
            foreach (var candidate in readings)
            {
                if (candidate.Backed && IsNarrowerThanAnInlineNeighbour(candidate, readings, dropped))
                {
                    rewritten ??= (string[])tokens.Clone();
                    rewritten[candidate.Index] = Inlined(candidate);
                }
            }
            if (dropped == null)
            {
                return rewritten ?? tokens;
            }
            var source = rewritten ?? tokens;
            var kept = new List<string>(tokens.Length);
            for (var i = 0; i < source.Length; i++)
            {
                if (!dropped.Contains(i))
                {
                    kept.Add(source[i]);
                }
            }
            return kept.ToArray();
        }

        // baseClasses without the inline spacing tokens a pose of the same Motion overrides: a pose is Framer
        // Motion's animate, which overrides the node's own style, so a base pt-[8px] must not outrank a pose's
        // p-[52px] on the top edge as it would in one class list. A base token goes when the pose token's property
        // contains everything it writes; baseClasses itself when none does.
        internal static string[] WithoutOverriddenBase(string[] baseClasses, string[] pose)
        {
            if (baseClasses.Length == 0 || pose.Length == 0)
            {
                return baseClasses;
            }
            var poseReadings = ReadAll(pose);
            if (poseReadings.Count == 0)
            {
                return baseClasses;
            }
            var overridden = new HashSet<int>();
            foreach (var own in ReadAll(baseClasses))
            {
                if (!own.Backed && poseReadings.Exists(posed => Overrides(posed, own)))
                {
                    overridden.Add(own.Index);
                }
            }
            if (overridden.Count == 0)
            {
                return baseClasses;
            }
            var kept = new List<string>(baseClasses.Length);
            for (var i = 0; i < baseClasses.Length; i++)
            {
                if (!overridden.Contains(i))
                {
                    kept.Add(baseClasses[i]);
                }
            }
            return kept.ToArray();
        }

        private static bool Overrides(Reading posed, Reading own)
        {
            if (posed.Property == own.Property || posed.Important != own.Important || posed.VariantSet != own.VariantSet)
            {
                return false;
            }
            var posedWritten = StyleArbitraryLonghands.Of(posed.Property);
            return posedWritten.Union(StyleArbitraryLonghands.Of(own.Property)) == posedWritten;
        }

        private static List<Reading> ReadAll(string[] tokens)
        {
            var readings = new List<Reading>();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryRead(tokens[i], i, out var reading))
                {
                    readings.Add(reading);
                }
            }
            return readings;
        }

        private static bool IsStylesheetClassOrLogical(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var variantsLength = VariantsLength(core);
            var payload = variantsLength == 0 ? core : core.Substring(variantsLength);
            return StyleArbitraryValueResolver.TryReadStylesheetClass(payload, out _, out _)
                || StyleLogicalUtilities.SortPropertiesOf(payload) != null;
        }

        private readonly struct Reading
        {
            public int Index { get; init; }
            public string Variants { get; init; }
            // The variants as a sorted set, so stacking order does not tell two tokens apart.
            public string VariantSet { get; init; }
            public bool Important { get; init; }
            public bool TrailingBang { get; init; }
            public ArbitraryProperty Property { get; init; }
            // The inline token a stylesheet-backed class resolves to when it is rewritten.
            public string InlineForm { get; init; }
            public bool Backed { get; init; }
            public bool Logical { get; init; }
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
            if (StyleArbitraryValueResolver.TryReadStylesheetClass(payload, out var property, out var form))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, VariantSet = VariantSetOf(variants), Important = important,
                    TrailingBang = trailingBang, Property = property, InlineForm = form, Backed = true,
                };
                return true;
            }
            if (StyleArbitraryValueResolver.IsInlineResolved(payload)
                && StyleArbitraryValueResolver.TryParse(payload, out var style)
                && StyleArbitraryValueResolver.IsSpacingProperty(style.Property))
            {
                reading = new Reading
                {
                    Index = index, Variants = variants, VariantSet = VariantSetOf(variants), Important = important,
                    TrailingBang = trailingBang, Property = style.Property, InlineForm = string.Empty,
                    Logical = StyleLogicalUtilities.SortPropertiesOf(payload) != null,
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

        // `hover:focus:` and `focus:hover:` name one rule's variants, so they compare as the same set.
        private static string VariantSetOf(string variants)
        {
            if (variants.Length == 0)
            {
                return variants;
            }
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < variants.Length; i++)
            {
                if (variants[i] == '[')
                {
                    depth++;
                }
                else if (variants[i] == ']')
                {
                    depth--;
                }
                else if (variants[i] == ':' && depth == 0)
                {
                    parts.Add(variants.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join(":", parts);
        }

        private static bool SameSlot(Reading a, Reading z)
            => a.Property == z.Property && a.Important == z.Important && a.VariantSet == z.VariantSet;

        // Whether an inline reading in the same variants and importance writes some of what candidate does
        // without candidate containing all of it: where candidate contains it the inline one is the narrower
        // utility and an inline value beating a class is already the order Tailwind gives.
        private static bool IsNarrowerThanAnInlineNeighbour(Reading candidate, List<Reading> readings, List<int>? dropped)
            => readings.Exists(other => !other.Backed && Narrows(candidate, other)
                && (dropped == null || !dropped.Contains(other.Index)));

        private static bool Narrows(Reading candidate, Reading other)
        {
            if (other.Property == candidate.Property || other.Important != candidate.Important
                || other.VariantSet != candidate.VariantSet)
            {
                return false;
            }
            var written = StyleArbitraryLonghands.Of(candidate.Property);
            var otherWritten = StyleArbitraryLonghands.Of(other.Property);
            return written.Overlaps(otherWritten) && written.Union(otherWritten) != written;
        }

        private static string Inlined(Reading candidate)
        {
            var core = candidate.Variants + candidate.InlineForm;
            if (!candidate.Important)
            {
                return core;
            }
            return candidate.TrailingBang ? core + "!" : "!" + core;
        }
    }
}
