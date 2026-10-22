using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    // The class tokens StyleCustomProperties resolves on an element: a custom property declaration (`[--gap:12px]`),
    // an arbitrary property (`[width:var(--w)]`), and a utility reading a custom property, through var() in its
    // bracket (`w-[calc(var(--w)*2)]`) or through Tailwind v4's parenthesised shorthand (`bg-(--brand)`,
    // `text-(length:--size)`).
    internal static class StyleCustomPropertyClass
    {
        // Tailwind's arbitrary properties written as the utility that writes the same longhand, with the type
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
            ["border-radius"] = "rounded-",
            ["border-width"] = "border-[length:", ["border-color"] = "border-[color:",
            ["background-color"] = "bg-[color:", ["color"] = "text-[color:", ["font-size"] = "text-[length:",
            ["letter-spacing"] = "tracking-", ["opacity"] = "opacity-", ["rotate"] = "rotate-",
            ["aspect-ratio"] = "aspect-", ["transition-duration"] = "duration-",
        };

        private static readonly Dictionary<string, string> s_normalized = new(StringComparer.Ordinal);

        // A token StyleCustomProperties owns rather than the class list or the arbitrary-value layers. The caller
        // has stripped the important bang and ruled out a variant.
        public static bool IsCustomPropertyToken(string core)
            => !string.IsNullOrEmpty(core) && (core[0] == '[' || core.IndexOf('(') >= 0)
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

        // The utility a consumer token is read as, with var() in its bracket: the token itself, the bracket form of
        // a parenthesised one, or the utility an arbitrary property maps to. Null for any other token, and for an
        // arbitrary property Velvet has no utility for.
        public static string? Consumer(string core)
        {
            if (string.IsNullOrEmpty(core) || (core[0] != '[' && core.IndexOf('(') < 0))
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
            var open = core.IndexOf('[');
            if (open >= 0)
            {
                return open >= 2 && core[open - 1] == '-' && core[core.Length - 1] == ']'
                    && core.IndexOf("var(", open, StringComparison.OrdinalIgnoreCase) > open
                    ? core
                    : null;
            }
            return Shorthand(core);
        }

        // `prefix-(--x)` and `prefix-(hint:--x)`, which Tailwind reads as `prefix-[var(--x)]` and
        // `prefix-[hint:var(--x)]`.
        private static string? Shorthand(string core)
        {
            var open = core.IndexOf('(');
            if (open < 2 || core[open - 1] != '-' || core[core.Length - 1] != ')')
            {
                return null;
            }
            for (var i = 0; i < open; i++)
            {
                var c = core[i];
                if (c != '-' && (c < 'a' || c > 'z') && (c < '0' || c > '9'))
                {
                    return null;
                }
            }
            var inner = core.Substring(open + 1, core.Length - open - 2);
            var hintEnd = 0;
            while (hintEnd < inner.Length && (inner[hintEnd] == '-' || (inner[hintEnd] >= 'a' && inner[hintEnd] <= 'z')))
            {
                hintEnd++;
            }
            var hint = hintEnd > 0 && hintEnd < inner.Length && inner[hintEnd] == ':' ? inner.Substring(0, hintEnd) : null;
            var reference = hint == null ? inner : inner.Substring(hintEnd + 1);
            if (!reference.StartsWith("--", StringComparison.Ordinal) || reference.Length < 3)
            {
                return null;
            }
            return core.Substring(0, open) + "[" + (hint == null ? string.Empty : hint + ":") + "var(" + reference + ")]";
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
}
