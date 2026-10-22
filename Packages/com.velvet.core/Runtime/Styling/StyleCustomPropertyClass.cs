using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    // The class tokens StyleCustomProperties resolves on an element: a custom property declaration (`[--gap:12px]`),
    // an arbitrary property (`[width:var(--w)]`), a utility reading a custom property, through var() in a bracket
    // (`w-[calc(var(--w)*2)]`, `bg-red-500/[var(--a)]`) or through Tailwind v4's parenthesised shorthand
    // (`bg-(--brand)`, `text-(length:--size)`, `bg-red-500/(--a)`), and a shorthand utility holding more than one
    // value (`p-[1px_2px]`), which only the element can split into its sides.
    internal static class StyleCustomPropertyClass
    {
        // Tailwind's arbitrary properties written as the utility that writes the same property, with the type
        // hint that pins the utility to it: `[color:…]` is `text-[color:…]`, never the font size `text-` also
        // writes. A property absent here is not one Velvet can write from a class.
        private static readonly Dictionary<string, string> s_propertyUtilities = new(StringComparer.Ordinal)
        {
            ["width"] = "w-", ["height"] = "h-",
            ["min-width"] = "min-w-", ["min-height"] = "min-h-", ["max-width"] = "max-w-", ["max-height"] = "max-h-",
            ["flex-basis"] = "basis-", ["flex-grow"] = "grow-", ["flex-shrink"] = "shrink-",
            ["top"] = "top-", ["right"] = "right-", ["bottom"] = "bottom-", ["left"] = "left-", ["inset"] = "inset-",
            ["padding"] = "p-", ["padding-top"] = "pt-", ["padding-right"] = "pr-", ["padding-bottom"] = "pb-",
            ["padding-left"] = "pl-",
            ["margin"] = "m-", ["margin-top"] = "mt-", ["margin-right"] = "mr-", ["margin-bottom"] = "mb-",
            ["margin-left"] = "ml-",
            ["gap"] = "gap-", ["row-gap"] = "gap-y-", ["column-gap"] = "gap-x-",
            ["border-radius"] = "rounded-",
            ["border-width"] = "border-[length:", ["border-color"] = "border-[color:",
            ["background-color"] = "bg-[color:", ["color"] = "text-[color:", ["font-size"] = "text-[length:",
            ["font-weight"] = "font-[weight:", ["font-family"] = "font-[family-name:", ["line-height"] = "leading-",
            ["outline-color"] = "outline-[color:",
            ["letter-spacing"] = "tracking-", ["opacity"] = "opacity-", ["rotate"] = "rotate-",
            ["aspect-ratio"] = "aspect-", ["transition-duration"] = "duration-",
        };

        // The shorthand utilities whose bracket may hold one value per side, with the utilities of those sides in
        // CSS's top, right, bottom, left order (corners from the top left for a radius; row then column for gap).
        // A border's are keyed with the hint that picks its width or its colour.
        private static readonly Dictionary<string, string[]> s_sides = new(StringComparer.Ordinal)
        {
            ["p-"] = new[] { "pt-", "pr-", "pb-", "pl-" },
            ["m-"] = new[] { "mt-", "mr-", "mb-", "ml-" },
            ["inset-"] = new[] { "top-", "right-", "bottom-", "left-" },
            ["rounded-"] = new[] { "rounded-tl-", "rounded-tr-", "rounded-br-", "rounded-bl-" },
            ["border-length"] = new[] { "border-t-", "border-r-", "border-b-", "border-l-" },
            ["border-color"] = new[] { "border-t-", "border-r-", "border-b-", "border-l-" },
            ["gap-"] = new[] { "gap-y-", "gap-x-" },
        };

        private static readonly Dictionary<string, string> s_normalized = new(StringComparer.Ordinal);

        // A token StyleCustomProperties owns rather than the class list or the arbitrary-value layers. The caller
        // has stripped the important bang and ruled out a variant.
        public static bool IsCustomPropertyToken(string core)
            => !string.IsNullOrEmpty(core) && (core[0] == '[' || core.IndexOf('(') >= 0 || core.IndexOf('_') >= 0)
                && (IsArbitraryProperty(core, out _, out _) || Consumer(core) != null);

        // `[--name:value]`: the name as written and the value as Tailwind writes it into the declaration, with `_`
        // read as a space outside a var() name and a url().
        public static bool TryDeclaration(string core, out string name, out string value)
        {
            name = value = string.Empty;
            if (!IsArbitraryProperty(core, out var property, out var raw) || !property.StartsWith("--", StringComparison.Ordinal)
                || property.Length < 3)
            {
                return false;
            }
            name = property;
            value = Decode(raw);
            return true;
        }

        // The utility a consumer token is read as: the token with each parenthesised var() shorthand written as
        // the bracket it stands for, or the utility an arbitrary property maps to. Null for any other token, and
        // for an arbitrary property Velvet has no utility for.
        public static string? Consumer(string core)
        {
            if (string.IsNullOrEmpty(core) || (core[0] != '[' && core.IndexOf('(') < 0 && core.IndexOf('_') < 0))
            {
                return null;
            }
            if (s_normalized.TryGetValue(core, out var cached))
            {
                return cached;
            }
            // Only a consumer is kept, so a tree writing ever-new bracket values that read nothing grows nothing.
            var normalized = Normalize(core);
            if (normalized != null)
            {
                s_normalized[core] = normalized;
            }
            return normalized;
        }

        private static string? Normalize(string core)
        {
            if (IsArbitraryProperty(core, out var property, out var raw))
            {
                if (property.StartsWith("--", StringComparison.Ordinal)
                    || !s_propertyUtilities.TryGetValue(property, out var utility))
                {
                    return null;
                }
                return utility.EndsWith(":", StringComparison.Ordinal) ? utility + raw + "]" : utility + "[" + raw + "]";
            }
            var expanded = WithShorthandsExpanded(core);
            if (expanded == null)
            {
                return null;
            }
            var open = expanded.IndexOf('[');
            if (open < 2 || (expanded[open - 1] != '-' && expanded[open - 1] != '/') || expanded[expanded.Length - 1] != ']')
            {
                return null;
            }
            var shape = UtilityShape.Of(expanded);
            return expanded.IndexOf("var(", open, StringComparison.OrdinalIgnoreCase) > open
                || (SidesOf(expanded, shape) != null && HoldsSeveralValues(expanded, shape))
                ? expanded
                : null;
        }

        // `prefix-(--x)`, `prefix-(hint:--x)` and a modifier's `/(--x)`, each written as the bracket Tailwind reads
        // it as (`[var(--x)]`, `[hint:var(--x)]`). The token as it is when it holds none; null when a parenthesised
        // part outside a bracket is not one.
        private static string? WithShorthandsExpanded(string core)
        {
            StringBuilder? rewritten = null;
            var copied = 0;
            var brackets = 0;
            for (var i = 0; i < core.Length; i++)
            {
                var c = core[i];
                brackets += c == '[' ? 1 : c == ']' ? -1 : 0;
                if (c != '(' || brackets != 0)
                {
                    continue;
                }
                if (!TryShorthandGroup(core, i, out var close, out var hint, out var reference))
                {
                    return null;
                }
                rewritten ??= new StringBuilder(core.Length + 8);
                rewritten.Append(core, copied, i - copied).Append('[');
                if (hint != null)
                {
                    rewritten.Append(hint).Append(':');
                }
                rewritten.Append("var(").Append(reference).Append(")]");
                copied = close + 1;
                i = close;
            }
            return rewritten == null ? core : rewritten.Append(core, copied, core.Length - copied).ToString();
        }

        // The parenthesised group opening at open: `(--x)` or `(hint:--x)` after a `-`, and `(--x)` after a
        // modifier's `/`, which Tailwind reads without a hint. A fallback may hold parentheses of its own
        // (`(--x,calc(1px*2))`), so the group closes at the parenthesis matching open.
        private static bool TryShorthandGroup(string core, int open, out int close, out string? hint, out string reference)
        {
            hint = null;
            reference = string.Empty;
            close = StyleCustomProperties.MatchingClose(core, open, core.Length);
            if (open < 2 || (core[open - 1] != '-' && core[open - 1] != '/') || close < 0)
            {
                return false;
            }
            var inner = core.Substring(open + 1, close - open - 1);
            var hintEnd = 0;
            while (hintEnd < inner.Length && (inner[hintEnd] == '-' || (inner[hintEnd] >= 'a' && inner[hintEnd] <= 'z')))
            {
                hintEnd++;
            }
            hint = hintEnd > 0 && hintEnd < inner.Length && inner[hintEnd] == ':' ? inner.Substring(0, hintEnd) : null;
            reference = hint == null ? inner : inner.Substring(hintEnd + 1);
            return (hint == null || core[open - 1] == '-') && reference.StartsWith("--", StringComparison.Ordinal)
                && reference.Length > 2;
        }

        // The side utilities a shorthand utility splits into, or null for any other utility.
        internal static string[]? SidesOf(string utility, in UtilityShape shape)
        {
            if (shape.MainOpen < 0 || shape.HasModifier)
            {
                return null;
            }
            var prefix = utility.Substring(0, shape.MainOpen);
            if (prefix == "border-")
            {
                prefix = shape.Hint is "length" or "line-width" ? "border-length" : shape.Hint == "color" ? "border-color" : prefix;
            }
            return s_sides.TryGetValue(prefix, out var sides) ? sides : null;
        }

        // Whether the main bracket holds a space outside any parenthesis.
        private static bool HoldsSeveralValues(string utility, in UtilityShape shape)
        {
            var depth = 0;
            var several = false;
            for (var i = shape.BodyStart; i < shape.MainClose && !several; i++)
            {
                depth += utility[i] == '(' ? 1 : utility[i] == ')' ? -1 : 0;
                several = depth == 0 && utility[i] == '_' && utility[i - 1] != '\\';
            }
            return several;
        }

        // `[property:value]`, the property a run of lower-case letters and hyphens or a custom property name, and
        // a value left after it.
        private static bool IsArbitraryProperty(string core, out string property, out string value)
        {
            property = value = string.Empty;
            if (core.Length < 5 || core[0] != '[' || core[core.Length - 1] != ']')
            {
                return false;
            }
            var colon = core.IndexOf(':');
            if (colon < 2 || colon > core.Length - 3)
            {
                return false;
            }
            var custom = core.Length > 3 && core[1] == '-' && core[2] == '-';
            for (var i = 1; i < colon; i++)
            {
                var c = core[i];
                var allowed = c == '-' || (c >= 'a' && c <= 'z')
                    || (custom && ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_'));
                if (!allowed)
                {
                    return false;
                }
            }
            property = core.Substring(1, colon - 1);
            value = core.Substring(colon + 1, core.Length - colon - 2);
            return value.Trim('_', ' ').Length > 0;
        }

        // Tailwind's reading of a bracket: `\_` is an underscore and `_` a space, except in a var() name and inside
        // a url(), which keep theirs.
        internal static string Decode(string encoded)
        {
            if (encoded.IndexOf('_') < 0)
            {
                return encoded;
            }
            var decoded = new StringBuilder(encoded.Length);
            var keepUntil = -1;
            for (var i = 0; i < encoded.Length; i++)
            {
                if (i > keepUntil && OpensAt(encoded, i, "var(--"))
                {
                    keepUntil = IndexOfAny(encoded, i + 4, ",)");
                }
                else if (i > keepUntil && OpensAt(encoded, i, "url("))
                {
                    keepUntil = IndexOfAny(encoded, i + 4, ")");
                }
                var c = encoded[i];
                if (c == '\\' && i + 1 < encoded.Length && encoded[i + 1] == '_')
                {
                    decoded.Append('_');
                    i++;
                }
                else
                {
                    decoded.Append(c == '_' && i > keepUntil ? ' ' : c);
                }
            }
            return decoded.ToString();
        }

        private static bool OpensAt(string text, int at, string opening)
            => string.CompareOrdinal(text, at, opening, 0, opening.Length) == 0;

        private static int IndexOfAny(string text, int from, string set)
        {
            var at = text.IndexOfAny(set.ToCharArray(), from);
            return at < 0 ? text.Length : at;
        }
    }

    // Where a bracketed utility's parts sit: the bracket holding its value, the type hint opening that bracket, and
    // whether a modifier follows the value (`bg-[…]/50`) or is the only bracket (`bg-red-500/[…]`).
    internal readonly struct UtilityShape
    {
        // The value's bracket, opened after the utility's `-`, and the bracket closing it; -1 when the value is
        // not bracketed.
        public readonly int MainOpen;
        public readonly int MainClose;
        public readonly string? Hint;
        // Where the value starts, after the hint.
        public readonly int BodyStart;
        public readonly bool HasModifier;

        private UtilityShape(int mainOpen, int mainClose, string? hint, int bodyStart, bool hasModifier)
        {
            MainOpen = mainOpen;
            MainClose = mainClose;
            Hint = hint;
            BodyStart = bodyStart;
            HasModifier = hasModifier;
        }

        public static UtilityShape Of(string utility)
        {
            var open = utility.IndexOf('[');
            if (open < 1 || utility[open - 1] != '-')
            {
                return new UtilityShape(-1, -1, null, -1, true);
            }
            var depth = 0;
            var close = open;
            for (; close < utility.Length; close++)
            {
                depth += utility[close] == '[' ? 1 : utility[close] == ']' ? -1 : 0;
                if (depth == 0)
                {
                    break;
                }
            }
            if (close >= utility.Length)
            {
                return new UtilityShape(-1, -1, null, -1, true);
            }
            StyleArbitraryValueResolver.TrySplitTypeHint(utility.AsSpan(open + 1, close - open - 1), out var hint, out _);
            return new UtilityShape(open, close, hint, open + 1 + (hint == null ? 0 : hint.Length + 1),
                close < utility.Length - 1);
        }
    }
}
