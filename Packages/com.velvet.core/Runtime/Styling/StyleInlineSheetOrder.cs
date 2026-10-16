#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    // Of an inline token and a stylesheet class that write one longhand at one rank, Tailwind emits one later
    // (StyleRuleOrder.CompareUtilities) and that one holds it, while an inline value outranks the class whatever
    // the order (StyleSpacingPrecedence). So an inline token that the classes emitted after it write in full is left
    // out of the list, and those classes hold what they declare. A token they write only in part stays:
    // StyleSpacingPrecedence writes the class inline for the spacing and radius families, and the other families
    // keep the token whole.
    internal static class StyleInlineSheetOrder
    {
        // tokens without each inline token that the stylesheet classes beside it emit after it cover in full;
        // tokens itself when there is none. An important or variant token does not take part: its own layer ranks it.
        internal static string[] WithoutOutrankedInline(string[] tokens)
        {
            List<(int Index, StyleLonghandSet Written)>? classes = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (TryReadClass(tokens[i], out var written))
                {
                    (classes ??= new List<(int, StyleLonghandSet)>()).Add((i, written));
                }
            }
            if (classes == null)
            {
                return tokens;
            }
            List<int>? dropped = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!TryReadInline(tokens[i], out var written))
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
                if (covered.Union(written) == covered)
                {
                    (dropped ??= new List<int>()).Add(i);
                }
            }
            if (dropped == null)
            {
                return tokens;
            }
            var kept = new List<string>(tokens.Length - dropped.Count);
            for (var i = 0; i < tokens.Length; i++)
            {
                if (!dropped.Contains(i))
                {
                    kept.Add(tokens[i]);
                }
            }
            return kept.ToArray();
        }

        // A bundled class that holds its longhands unconditionally.
        private static bool TryReadClass(string token, out StyleLonghandSet written)
        {
            written = StyleLonghandSet.Empty;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (important || StyleArbitraryValueResolver.IsInlineResolved(core)
                || !StyleUtilityProperties.TryGet(core, out var rule) || rule.Gate != StyleUtilityGate.None)
            {
                return false;
            }
            written = rule.Properties;
            return true;
        }

        // A token that resolves inline, writes longhands the cascade model compares, and is none of the families
        // another pass orders: a logical spacing or radius token is placed against its physical side by
        // StyleSpacingPrecedence.
        private static bool TryReadInline(string token, out StyleLonghandSet written)
        {
            written = StyleLonghandSet.Empty;
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (important || StyleSpacingPrecedence.VariantsLength(core) != 0
                || !StyleArbitraryValueResolver.IsInlineResolved(core)
                || !StyleArbitraryValueResolver.TryParseLayer(core, out var style)
                || StyleLogicalUtilities.SortPropertiesOf(core) != null
                    && StyleArbitraryValueResolver.IsSpacingOrRadiusProperty(style.Property))
            {
                return false;
            }
            written = StyleArbitraryLonghands.Of(style.Property);
            return !written.IsEmpty;
        }
    }
}
