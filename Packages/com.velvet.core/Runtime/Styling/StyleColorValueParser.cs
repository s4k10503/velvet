using System;
using System.Globalization;
using UnityEngine;

namespace Velvet
{
    // Color value parsers for the arbitrary-value dispatch (StyleArbitraryValueResolver): the color-capable
    // bracket prefixes (text-/bg-/border-[..]), the color-opacity modifier ({bg|text|border}-<color>/<N>),
    // and the shared color grammar (#hex / named / rgb()/rgba()). The dispatch calls in; this group calls
    // only the color palette and its own private helpers, never back into the dispatch or another parser.
    internal static class StyleColorValueParser
    {
        // Color-capable bracket prefixes. true (result set) on a color match; false to reject the class
        // outright (bg- with a non-color value, so the caller falls through to the background image
        // resolver); null when not a color match and the caller should fall through to the length-based
        // path (text-/border- with a non-color value, or the negated form — '-' never applies to a color).
        internal static bool? TryParseColorPrefix(string prefix, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return null;
            }

            // text-[...] is overloaded: a color value wins, otherwise it falls through to font-size.
            if (prefix == "text-")
            {
                if (TryParseColor(valueSpan, out var textColor))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.TextColor, textColor);
                    return true;
                }
                return null;
            }

            // bg-[...] is color-only here; a non-color value (e.g. bg-[addr:...]) rejects.
            if (prefix == "bg-")
            {
                if (TryParseColor(valueSpan, out var bgColor))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.BackgroundColor, bgColor);
                    return true;
                }
                return false;
            }

            // border-[...] is overloaded: a color value sets all four border colors, otherwise it falls
            // through to border-width (a length).
            if (prefix == "border-")
            {
                if (TryParseColor(valueSpan, out var borderColor))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.BorderColor, borderColor);
                    return true;
                }
                return null;
            }

            return null;
        }

        // Parses an arbitrary color value. Accepts CSS rgb()/rgba() functional notation in addition to the
        // #hex / named colors ColorUtility handles, so text-/bg-/border-[..] and shadow-[..] all take
        // rgb(255,0,0) / rgba(0,0,0,0.3). Internal so StyleShadowClass shares the one color grammar.
        internal static bool TryParseColor(ReadOnlySpan<char> valueStr, out Color color)
        {
            if (TryParseRgbFunction(valueStr, out color))
            {
                return true;
            }
            // ColorUtility.TryParseHtmlString accepts #rgb / #rgba / #rrggbb / #rrggbbaa and named colors.
            // It requires a string; the allocation is incurred only for the rare arbitrary color class.
            return ColorUtility.TryParseHtmlString(valueStr.ToString(), out color);
        }

        // Index of the color-opacity-modifier '/' — the first '/' at bracket depth 0 (i.e. not inside a
        // [...] arbitrary value), scanning from start. Returns -1 when there is no such separator, so an
        // in-bracket '/' (aspect-[4/3], bg-[rgb(1/2)]) is never mistaken for the modifier separator.
        private static int ColorModifierSlashIndex(string s, int start)
        {
            var depth = 0;
            for (var i = start; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '[')
                {
                    depth++;
                }
                else if (c == ']')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (c == '/' && depth == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        // True when className is a color utility carrying an opacity modifier ({bg|text|border}-<color>/<N>),
        // e.g. bg-red-500/50, text-black/75, border-white/10, bg-[#fff]/50. The palette form has no '[', so the
        // reconciler's '['-only fast path would route it to the USS class list (where a '/' selector matches
        // nothing); this predicate lets the dispatch sites send it to the resolver instead. Single source of
        // truth for the AddClass / RemoveClass / ApplyClassNames / variant-payload checks.
        public static bool HasColorOpacityModifier(string className)
        {
            if (string.IsNullOrEmpty(className) || className[0] == '-' || className.IndexOf('/') < 0)
            {
                return false;
            }
            if (!className.StartsWith("bg-", StringComparison.Ordinal)
                && !className.StartsWith("text-", StringComparison.Ordinal)
                && !className.StartsWith("border-", StringComparison.Ordinal))
            {
                return false;
            }
            // The modifier '/' is the one lying OUTSIDE any [...] bracket — handles a bracketed base
            // (bg-[#fff]/50), a bracketed alpha (bg-blue-500/[0.32]), and an in-bracket '/' (aspect-[4/3]).
            var slash = ColorModifierSlashIndex(className, 0);
            return slash > 0 && slash < className.Length - 1;
        }

        // Parses a color opacity modifier ({bg|text|border}-<color>/<N>). The base <color> is a palette name
        // (red-500/white/black/transparent) or an arbitrary [#hex]/[rgb(...)] value; <N> is an integer percent
        // 0..100 (alpha = N/100) or an arbitrary [0..1] fraction (.../[0.32]). The modifier is authoritative:
        // it OVERWRITES the base color's alpha (an 8-digit-hex base's alpha is replaced, not multiplied).
        // Returns false for an unknown prefix, an unresolvable base, or an out-of-range N.
        internal static bool TryParseColorOpacityModifier(string className, out ArbitraryStyle result)
        {
            result = default;

            ArbitraryProperty property;
            int prefixLen;
            if (className.StartsWith("bg-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.BackgroundColor;
                prefixLen = 3;
            }
            else if (className.StartsWith("text-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.TextColor;
                prefixLen = 5;
            }
            else if (className.StartsWith("border-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.BorderColor;
                prefixLen = 7;
            }
            else
            {
                return false;
            }

            // Split base from modifier on the '/' that lies OUTSIDE any [...] bracket, so a bracketed base
            // (bg-[#fff]/50), a bracketed alpha (bg-blue-500/[0.32]), or an in-bracket '/' all resolve right.
            var slash = ColorModifierSlashIndex(className, prefixLen);
            if (slash <= prefixLen || slash >= className.Length - 1)
            {
                return false;
            }

            var baseToken = className.Substring(prefixLen, slash - prefixLen);
            if (!VelvetPalette.TryResolveColorToken(baseToken, out var color))
            {
                return false;
            }

            if (!TryParseAlphaModifier(className.AsSpan(slash + 1), out var alpha))
            {
                return false;
            }

            color.a = alpha;
            result = new ArbitraryStyle(property, color);
            return true;
        }

        // Parses the alpha portion of a color opacity modifier: an integer percent 0..100 (50 -> 0.5) or an
        // arbitrary bracketed 0..1 fraction ([0.32] -> 0.32). Returns false for any other / out-of-range form.
        private static bool TryParseAlphaModifier(ReadOnlySpan<char> span, out float alpha)
        {
            alpha = 0f;
            if (span.Length == 0)
            {
                return false;
            }
            if (span[0] == '[' && span[span.Length - 1] == ']')
            {
                if (!float.TryParse(span.Slice(1, span.Length - 2), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var frac)
                    || !float.IsFinite(frac) || frac < 0f || frac > 1f)
                {
                    return false;
                }
                alpha = frac;
                return true;
            }
            if (!int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
                || percent < 0 || percent > 100)
            {
                return false;
            }
            alpha = percent / 100f;
            return true;
        }

        // Parses CSS rgb(r,g,b) / rgba(r,g,b,a): r/g/b are 0..255 integers, a is a 0..1 float. Whitespace
        // around the commas is tolerated. Returns false for any other form so the #hex/named path can try.
        private static bool TryParseRgbFunction(ReadOnlySpan<char> span, out Color color)
        {
            color = default;
            var s = span.ToString().Trim();
            var isRgba = s.StartsWith("rgba(", StringComparison.Ordinal);
            var isRgb = !isRgba && s.StartsWith("rgb(", StringComparison.Ordinal);
            if ((!isRgb && !isRgba) || !s.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }
            var open = s.IndexOf('(');
            // Underscores stand in for the spaces a class string cannot carry (the same
            // arbitrary-value convention the shadow and clip-path grammars restore before parsing),
            // so the underscore form of a copy-pasted "rgb(0, 0, 0)" parses like the spaced one.
            var parts = s.Substring(open + 1, s.Length - open - 2).Replace('_', ' ').Split(',');
            if (parts.Length != (isRgba ? 4 : 3))
            {
                return false;
            }
            if (!byte.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
                || !byte.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
                || !byte.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            {
                return false;
            }
            var a = 1f;
            if (isRgba && (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                || a < 0f || a > 1f))
            {
                return false;
            }
            color = new Color(r / 255f, g / 255f, b / 255f, a);
            return true;
        }
    }
}
