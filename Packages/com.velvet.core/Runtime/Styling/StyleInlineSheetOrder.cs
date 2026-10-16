#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Of an inline token and a stylesheet class that write one longhand at one rank, Tailwind emits one later
    // (StyleRuleOrder.CompareUtilities) and that one holds it, while an inline value outranks the class whatever
    // the order (StyleSpacingPrecedence). So the longhands of an inline token that the classes emitted after it
    // write are taken out of the list: a token they write in full is dropped, and one they write in part is
    // replaced by a token per longhand they leave, as StyleSpacingPrecedence.CornersLeftBy cuts a pose's radius
    // down to the corners it leaves. A spacing, size, inset or radius token is not cut here:
    // StyleSpacingPrecedence writes the class inline instead, and InlineSheetOrderTests holds that it can write every
    // bundled class that covers part of one. The same fixture holds that no other multi-longhand family has a
    // bundled class that writes part of it.
    //
    // Two inline tokens of one property in one layer (important or not) share a layer slot, so the one applied last
    // would hold it and clearing either would clear both. Tailwind's order decides instead, and the earlier token
    // is left out: removing the later one brings it back through the class diff, with no per-element state. An
    // inline border width that later inline tokens write in part is cut down to the sides they leave, as it is for
    // a class.
    internal static class StyleInlineSheetOrder
    {
        // The families cut down to a token per longhand, with the prefix their bracket value follows and the token
        // each longhand is written by.
        private static readonly (ArbitraryProperty Property, string Prefix, (StyleLonghand Longhand, string Side)[] Sides)[] s_splits =
        {
            (ArbitraryProperty.BorderWidth, "border-", new[]
            {
                (StyleLonghand.BorderTopWidth, "border-t-"),
                (StyleLonghand.BorderRightWidth, "border-r-"),
                (StyleLonghand.BorderBottomWidth, "border-b-"),
                (StyleLonghand.BorderLeftWidth, "border-l-"),
            }),
        };

        // A variantless token that resolves inline: what it writes, and what decides which tokens contend for it.
        private readonly struct InlineReading
        {
            public int Index { get; init; }
            // The token without its important modifier, and how that was spelled.
            public string Core { get; init; }
            public bool Important { get; init; }
            public bool TrailingBang { get; init; }
            public ArbitraryProperty Property { get; init; }
            // A registered custom filter is one property with a layer stack per name; null for every other token.
            public string? CustomName { get; init; }
            public StyleLonghandSet Written { get; init; }
            // Whether a stylesheet class can outrank it. An important token is ranked by its own layer, and a
            // logical spacing or radius token is placed against its physical side by StyleSpacingPrecedence.
            public bool ClassesOutrank { get; init; }
            // text-<size>/<leading> also sets a line height, which is read off the token.
            public bool SlashSize { get; init; }
        }

        // tokens with each inline token that the stylesheet classes beside it emit after it write in full left
        // out, and each one they write in part cut down to what they leave; and with each inline token that a
        // later inline token of its property writes left out; tokens itself when there is none. A variant token
        // does not take part: its own layer ranks it.
        internal static string[] WithoutOutrankedInline(string[] tokens)
        {
            List<(int Index, StyleLonghandSet Written)>? classes = null;
            List<InlineReading>? inlines = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryReadClass(tokens[i], out var classWritten))
                {
                    (classes ??= new List<(int, StyleLonghandSet)>()).Add((i, classWritten));
                }
                else if (TryReadInline(tokens[i], i, out var reading))
                {
                    (inlines ??= new List<InlineReading>()).Add(reading);
                }
            }
            if (inlines == null)
            {
                return tokens;
            }
            string[]?[]? replaced = null;
            foreach (var inline in inlines)
            {
                var left = LeftBy(tokens, inline, classes, inlines);
                if (left != null)
                {
                    (replaced ??= new string[]?[tokens.Length])[inline.Index] = left;
                }
            }
            return replaced == null ? tokens : Replaced(tokens, replaced);
        }

        private static string[] Replaced(string[] tokens, string[]?[] replaced)
        {
            var kept = new List<string>(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (replaced[i] is { } with)
                {
                    kept.AddRange(with);
                }
                else
                {
                    kept.Add(tokens[i]);
                }
            }
            return kept.ToArray();
        }

        // What replaces tokens[inline.Index]: no token when a later token writes all of it, a token per longhand
        // they leave for a family cut down above, and null when the token stays as it is.
        private static string[]? LeftBy(string[] tokens, in InlineReading inline,
            List<(int Index, StyleLonghandSet Written)>? classes, List<InlineReading> inlines)
        {
            var token = tokens[inline.Index];
            var covered = StyleLonghandSet.Empty;
            if (inline.ClassesOutrank && classes != null)
            {
                foreach (var (index, classWritten) in classes)
                {
                    if (StyleRuleOrder.CompareUtilities(tokens[index], token) > 0)
                    {
                        covered = covered.Union(classWritten);
                    }
                }
            }
            foreach (var other in inlines)
            {
                if (!Contends(inline, other) || StyleRuleOrder.CompareUtilities(tokens[other.Index], token) <= 0)
                {
                    continue;
                }
                if (other.Property != inline.Property)
                {
                    covered = covered.Union(other.Written);
                }
                else if (!inline.SlashSize)
                {
                    return Array.Empty<string>();
                }
            }
            return Remainder(inline, covered);
        }

        // Whether other, in the layer of inline, can take the longhands of inline from it: a token of its own
        // property, or for a family cut down above one of another property.
        private static bool Contends(in InlineReading inline, in InlineReading other)
        {
            if (other.Important != inline.Important)
            {
                return false;
            }
            if (other.Property == inline.Property)
            {
                return other.CustomName == inline.CustomName;
            }
            return SplitOf(inline.Property) != null;
        }

        private static (string Prefix, (StyleLonghand Longhand, string Side)[] Sides)? SplitOf(ArbitraryProperty property)
        {
            foreach (var (splitProperty, prefix, sides) in s_splits)
            {
                if (splitProperty == property)
                {
                    return (prefix, sides);
                }
            }
            return null;
        }

        // What replaces a token writing inline.Written once the later tokens write `covered`: no token when they
        // write all of it, a token per longhand they leave for a family cut down above, and null when the token
        // stays as it is. A token that writes no longhand (a filter) is left to its own property's contest.
        private static string[]? Remainder(in InlineReading inline, StyleLonghandSet covered)
        {
            if (inline.Written.IsEmpty)
            {
                return null;
            }
            if (covered.Union(inline.Written) == covered)
            {
                return Array.Empty<string>();
            }
            var split = SplitOf(inline.Property);
            // A negated bracket (-border-[3px]) parses to the same property and keeps its sign in front of the
            // prefix, so a token not starting with the prefix is left whole.
            if (!covered.Overlaps(inline.Written) || split == null
                || !inline.Core.StartsWith(split.Value.Prefix, StringComparison.Ordinal))
            {
                return null;
            }
            var (prefix, sides) = split.Value;
            var value = inline.Core.Substring(prefix.Length);
            var left = new List<string>();
            foreach (var (longhand, side) in sides)
            {
                if (!covered.Contains(longhand))
                {
                    left.Add(Spelled(inline, string.Concat(side, value)));
                }
            }
            return left.ToArray();
        }

        private static string Spelled(in InlineReading inline, string core)
        {
            if (!inline.Important)
            {
                return core;
            }
            return inline.TrailingBang ? core + "!" : "!" + core;
        }

        // A bundled class that holds its longhands unconditionally. No bundled class name is claimed by
        // IsInlineResolved, which InlineSheetOrderTests holds, so the class half needs no dispatch check.
        private static bool TryReadClass(string token, out StyleLonghandSet written)
        {
            written = StyleLonghandSet.Empty;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (important || !StyleUtilityProperties.TryGet(core, out var rule) || rule.Gate != StyleUtilityGate.None)
            {
                return false;
            }
            written = rule.Properties;
            return true;
        }

        // A variantless token that resolves inline to a layer.
        private static bool TryReadInline(string token, int index, out InlineReading reading)
        {
            reading = default;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (StyleSpacingPrecedence.VariantsLength(core) != 0
                || !StyleArbitraryValueResolver.IsInlineResolved(core)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style))
            {
                return false;
            }
            var logicalSpacingOrRadius = StyleLogicalUtilities.SortPropertiesOf(core) != null
                && StyleArbitraryValueResolver.IsSpacingOrRadiusProperty(style.Property);
            var written = StyleArbitraryLonghands.Of(style.Property);
            reading = new InlineReading
            {
                Index = index,
                Core = core,
                Important = important,
                TrailingBang = important && token[token.Length - 1] == '!',
                Property = style.Property,
                CustomName = style.Custom?.Name,
                Written = written,
                ClassesOutrank = !important && !logicalSpacingOrRadius,
                SlashSize = StyleNamedTextSize.TryParseSlashFontSize(core, out _),
            };
            return true;
        }
    }
}
