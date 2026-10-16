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

        // tokens with each inline token that the stylesheet classes beside it emit after it write in full left
        // out, and each one they write in part cut down to what they leave; tokens itself when there is none. An
        // important or variant token does not take part: its own layer ranks it.
        internal static string[] WithoutOutrankedInline(string[] tokens)
        {
            List<(int Index, StyleLonghandSet Written)>? classes = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryReadClass(tokens[i], out var classWritten))
                {
                    (classes ??= new List<(int, StyleLonghandSet)>()).Add((i, classWritten));
                }
            }
            if (classes == null)
            {
                return tokens;
            }
            string[]?[]? replaced = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!TryReadInline(tokens[i], out var core, out var property, out var written))
                {
                    continue;
                }
                var covered = StyleLonghandSet.Empty;
                foreach (var (index, classWritten) in classes)
                {
                    if (StyleRuleOrder.CompareUtilities(tokens[index], tokens[i]) > 0)
                    {
                        covered = covered.Union(classWritten);
                    }
                }
                var left = LeftBy(core, property, written, covered);
                if (left != null)
                {
                    (replaced ??= new string[]?[tokens.Length])[i] = left;
                }
            }
            if (replaced == null)
            {
                return tokens;
            }
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

        // What replaces a token writing `written` once the classes after it write `covered`: no token when they
        // write all of it, a token per longhand they leave for a family cut down above, and null when the token
        // stays as it is.
        private static string[]? LeftBy(string core, ArbitraryProperty property, StyleLonghandSet written,
            StyleLonghandSet covered)
        {
            if (covered.Union(written) == covered)
            {
                return Array.Empty<string>();
            }
            if (!covered.Overlaps(written))
            {
                return null;
            }
            foreach (var (splitProperty, prefix, sides) in s_splits)
            {
                // A negated bracket (-border-[3px]) parses to the same property and keeps its sign in front of the
                // prefix, so a token not starting with the prefix is left whole.
                if (splitProperty != property || !core.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var value = core.Substring(prefix.Length);
                var left = new List<string>();
                foreach (var (longhand, side) in sides)
                {
                    if (!covered.Contains(longhand))
                    {
                        left.Add(string.Concat(side, value));
                    }
                }
                return left.ToArray();
            }
            return null;
        }

        // A bundled class that holds its longhands unconditionally. No bundled class name is claimed by
        // IsInlineResolved, which InlineSheetOrderTests holds, so the class half needs no dispatch check.
        private static bool TryReadClass(string token, out StyleLonghandSet written)
        {
            written = StyleLonghandSet.Empty;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (important || !StyleUtilityProperties.TryGet(core, out var rule) || rule.Gate != StyleUtilityGate.None)
            {
                // MUTANT_SURVIVES(equivalent, literal): written is assigned only below this return, so a class read here would hold no longhand and cover nothing.
                return false;
            }
            written = rule.Properties;
            return true;
        }

        // A token that resolves inline, writes longhands the cascade model compares, and is none of the families
        // another pass orders: a logical spacing or radius token is placed against its physical side by
        // StyleSpacingPrecedence.
        private static bool TryReadInline(string token, out string core, out ArbitraryProperty property,
            out StyleLonghandSet written)
        {
            property = default;
            written = StyleLonghandSet.Empty;
            core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (important)
            {
                return false;
            }
            if (StyleSpacingPrecedence.VariantsLength(core) != 0)
            {
                return false;
            }
            if (!StyleArbitraryValueResolver.IsInlineResolved(core)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style))
            {
                return false;
            }
            property = style.Property;
            if (StyleLogicalUtilities.SortPropertiesOf(core) != null
                && StyleArbitraryValueResolver.IsSpacingOrRadiusProperty(property))
            {
                return false;
            }
            written = StyleArbitraryLonghands.Of(property);
            return !written.IsEmpty;
        }
    }
}
