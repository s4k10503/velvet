using System;
using System.Globalization;
using UnityEngine;

namespace Velvet
{
    // Color value parsers for the arbitrary-value dispatch (StyleArbitraryValueResolver): the color-capable
    // bracket prefixes (text-/bg-/border-[..]), the color-opacity modifier ({bg|text|border}-<color>/<N>),
    // and the shared color grammar (TryParseColor). The dispatch and the other colour-bearing utilities call in;
    // outside its own helpers this group calls VelvetPalette, CssColorMath, ColorUtility and the dispatch's
    // TryStripBrackets.
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

        // Internal so the shadow, ring, divide, gradient and filter grammars share the one colour grammar, which
        // TryParseCssColor owns.
        internal static bool TryParseColor(ReadOnlySpan<char> valueStr, out Color color)
        {
            if (TryParseCssColor(valueStr.ToString(), out var css))
            {
                color = CssColorMath.ToDisplay(css);
                return true;
            }
            color = default;
            return false;
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
        // (red-500/white/black/transparent) or an arbitrary [<color>] value; <N> is an integer percent
        // 0..100 (alpha = N/100) or an arbitrary [0..1] fraction (.../[0.32]). The modifier multiplies the base
        // colour's alpha, as Tailwind v4's color-mix(in oklab, <color> N%, transparent) does.
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

            color.a *= alpha;
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
            if (StyleArbitraryValueResolver.TryStripBrackets(span, 0, out var inner))
            {
                if (!float.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out var frac)
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

        // A colour as CSS Color 4 writes it, kept in the space it names: rgb()/rgba() and hsl()/hsla() in the comma
        // syntax or the space syntax with a '/' alpha, hwb(), transparent, and otherwise the #hex and names
        // ColorUtility parses. Underscores stand in for the spaces a class string cannot carry, as the shadow and
        // clip-path grammars read them.
        private static bool TryParseCssColor(string value, out CssColor color)
        {
            color = default;
            var s = value.Replace('_', ' ').Trim();
            if (!s.Contains('('))
            {
                if (!ColorUtility.TryParseHtmlString(s, out var html))
                {
                    return false;
                }
                color = new CssColor(CssColorSpace.Srgb, html.r, html.g, html.b, html.a);
                return true;
            }
            if (s[s.Length - 1] != ')')
            {
                return false;
            }
            var open = s.IndexOf('(');
            var space = SpaceOf(s.Substring(0, open).ToLowerInvariant());
            if (space == null)
            {
                return false;
            }
            var body = s.Substring(open + 1, s.Length - open - 2).ToLowerInvariant();
            switch (space.Value)
            {
                case CssColorSpace.Srgb:
                    return TryParseRgb(body, out color);
                case CssColorSpace.Hsl:
                    return TryParseHsl(body, out color);
                default:
                    return TryParseModern(body, space.Value, out color);
            }
        }

        // The space a colour function's name writes in, read before its body is built so that a url() or a
        // gradient's own brackets pay for no lowercased copy; null for any other name.
        private static CssColorSpace? SpaceOf(string name)
        {
            switch (name)
            {
                case "rgb":
                case "rgba":
                    return CssColorSpace.Srgb;
                case "hsl":
                case "hsla":
                    return CssColorSpace.Hsl;
                case "hwb":
                    return CssColorSpace.Hwb;
                default:
                    return null;
            }
        }

        // The comma syntax takes three numbers or three percentages and an optional alpha, and no none.
        private static bool TryParseRgb(string body, out CssColor color)
        {
            if (!body.Contains(','))
            {
                return TryParseModern(body, CssColorSpace.Srgb, out color);
            }
            color = new CssColor(CssColorSpace.Srgb, 0, 0, 0, 1);
            var parts = body.Split(',');
            if (parts.Length != 3 && parts.Length != 4)
            {
                return false;
            }
            var percent = parts[0].Trim().EndsWith("%", StringComparison.Ordinal);
            for (var i = 0; i < 3; i++)
            {
                var token = parts[i].Trim();
                if (token.EndsWith("%", StringComparison.Ordinal) != percent
                    || !TryParseLegacyToken(token, 255, out var channel))
                {
                    return false;
                }
                color[i] = Math.Clamp(channel / 255, 0, 1);
            }
            return TryParseLegacyAlpha(parts, ref color);
        }

        // The comma syntax takes a hue and two percentages, and no none.
        private static bool TryParseHsl(string body, out CssColor color)
        {
            if (!body.Contains(','))
            {
                return TryParseModern(body, CssColorSpace.Hsl, out color);
            }
            color = new CssColor(CssColorSpace.Hsl, 0, 0, 0, 1);
            var parts = body.Split(',');
            if (parts.Length != 3 && parts.Length != 4)
            {
                return false;
            }
            var hue = parts[0].Trim();
            var saturation = parts[1].Trim();
            var lightness = parts[2].Trim();
            if (hue == "none" || !TryParseHue(hue, out color.C0)
                || !saturation.EndsWith("%", StringComparison.Ordinal) || !TryParseLegacyToken(saturation, 100, out color.C1)
                || !lightness.EndsWith("%", StringComparison.Ordinal) || !TryParseLegacyToken(lightness, 100, out color.C2))
            {
                return false;
            }
            color.C1 = Math.Max(color.C1, 0);
            return TryParseLegacyAlpha(parts, ref color);
        }

        private static bool TryParseLegacyToken(string token, double percentReference, out double value)
        {
            value = 0;
            return token != "none" && TryParseComponent(token, percentReference, out value);
        }

        private static bool TryParseLegacyAlpha(string[] parts, ref CssColor color)
        {
            if (parts.Length == 3)
            {
                return true;
            }
            if (!TryParseLegacyToken(parts[3].Trim(), 1, out var alpha))
            {
                return false;
            }
            color.Alpha = Math.Clamp(alpha, 0, 1);
            return true;
        }

        // The space syntax: three components, each a number, a percentage or none (read as zero), then an
        // optional '/' alpha. The percentage reference and parse-time clamp are each space's CSS Color 4 table's.
        private static bool TryParseModern(string body, CssColorSpace space, out CssColor color)
        {
            color = new CssColor(space, 0, 0, 0, 1);
            if (!TrySplitModern(body, out var tokens, out var alphaToken))
            {
                return false;
            }
            for (var i = 0; i < 3; i++)
            {
                double value;
                if (CssColorMath.IsPolar(space) && i == 0)
                {
                    if (!TryParseHue(tokens[i], out value))
                    {
                        return false;
                    }
                }
                else if (!TryParseComponent(tokens[i], space == CssColorSpace.Srgb ? 255 : 100, out value))
                {
                    return false;
                }
                color[i] = ClampAtParse(space, i, value);
            }
            if (alphaToken == null)
            {
                return true;
            }
            if (!TryParseComponent(alphaToken, 1, out var alpha))
            {
                return false;
            }
            color.Alpha = Math.Clamp(alpha, 0, 1);
            return true;
        }

        private static double ClampAtParse(CssColorSpace space, int index, double value)
        {
            if (space == CssColorSpace.Srgb)
            {
                return Math.Clamp(value / 255, 0, 1);
            }
            return index == 1 && space == CssColorSpace.Hsl ? Math.Max(value, 0) : value;
        }

        // Splits a space-syntax body into its three components and the alpha after a '/', which may sit with or
        // without spaces around it.
        private static bool TrySplitModern(string body, out string[] components, out string? alpha)
        {
            components = new string[3];
            alpha = null;
            var count = 0;
            var afterSlash = false;
            foreach (var part in body.Replace("/", " / ").Split(' '))
            {
                if (part.Length == 0)
                {
                    continue;
                }
                if (part == "/")
                {
                    if (afterSlash)
                    {
                        return false;
                    }
                    afterSlash = true;
                }
                else if (afterSlash)
                {
                    if (alpha != null)
                    {
                        return false;
                    }
                    alpha = part;
                }
                else
                {
                    if (count == 3)
                    {
                        return false;
                    }
                    components[count++] = part;
                }
            }
            return count == 3 && afterSlash == (alpha != null);
        }

        // A number, a percentage of the reference, or none, which reads as zero.
        private static bool TryParseComponent(string token, double percentReference, out double value)
        {
            value = 0;
            if (token == "none")
            {
                return true;
            }
            if (token.EndsWith("%", StringComparison.Ordinal))
            {
                if (!TryParseNumber(token.Substring(0, token.Length - 1), out var percent))
                {
                    return false;
                }
                value = percent / 100 * percentReference;
                return true;
            }
            return TryParseNumber(token, out value);
        }

        // A <hue>: a number of degrees or a deg / grad / rad / turn angle, normalised to [0, 360), or none.
        private static bool TryParseHue(string token, out double degrees)
        {
            degrees = 0;
            if (token == "none")
            {
                return true;
            }
            var scale = 1.0;
            var unitLength = 0;
            if (token.EndsWith("deg", StringComparison.Ordinal))
            {
                unitLength = 3;
            }
            else if (token.EndsWith("grad", StringComparison.Ordinal))
            {
                scale = 0.9;
                unitLength = 4;
            }
            else if (token.EndsWith("rad", StringComparison.Ordinal))
            {
                scale = 180 / Math.PI;
                unitLength = 3;
            }
            else if (token.EndsWith("turn", StringComparison.Ordinal))
            {
                scale = 360;
                unitLength = 4;
            }
            if (!TryParseNumber(token.Substring(0, token.Length - unitLength), out var number))
            {
                return false;
            }
            // A hue at or past double's largest, which an overflowing literal reads as, is 0deg, as Chromium reads it.
            var hue = number * scale;
            degrees = Math.Abs(hue) < double.MaxValue ? CssColorMath.NormalizeHue(hue) : 0;
            return true;
        }

        // A literal past double's range reads as double's largest of its sign, which a channel then clamps, as
        // Chromium reads one: Mono's parser declines such a literal and .NET's returns an infinity, so neither
        // result is kept.
        private static bool TryParseNumber(string token, out double value)
        {
            value = 0;
            if (!IsCssNumber(token))
            {
                return false;
            }
            // MUTANT_SURVIVES(unreachable): Mono's parser returns no infinity for a CSS <number> (measured on
            // Mono 6.8), so on Mono only the decline reaches the largest. On .NET's own parser, which returns one,
            // the hsl(90_1e999%_50%) row of Given_ALiteralPastDoublesRange_When_Parsed_Then_ItReadsAsChromiumReadsIt
            // fails without the infinity clause.
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value))
            {
                value = token[0] == '-' ? -double.MaxValue : double.MaxValue;
            }
            return true;
        }

        // A CSS <number>: an optional sign, then digits with an optional fraction or a fraction alone, then an optional
        // exponent. double.TryParse alone would also take "1.", surrounding spaces and "Infinity", which CSS does not.
        private static bool IsCssNumber(string token)
        {
            var i = 0;
            SkipSign(token, ref i);
            var digits = SkipDigits(token, ref i);
            if (i < token.Length && token[i] == '.')
            {
                i++;
                var fraction = SkipDigits(token, ref i);
                if (fraction == 0)
                {
                    return false;
                }
                digits += fraction;
            }
            if (i < token.Length && token[i] == 'e')
            {
                i++;
                SkipSign(token, ref i);
                if (SkipDigits(token, ref i) == 0)
                {
                    return false;
                }
            }
            return digits > 0 && i == token.Length;
        }

        private static void SkipSign(string token, ref int i)
        {
            if (i < token.Length && (token[i] == '+' || token[i] == '-'))
            {
                i++;
            }
        }

        private static int SkipDigits(string token, ref int i)
        {
            var start = i;
            for (; i < token.Length && token[i] >= '0' && token[i] <= '9'; i++)
            {
            }
            return i - start;
        }
    }
}
