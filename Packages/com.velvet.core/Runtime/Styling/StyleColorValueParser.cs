using System;
using System.Collections.Generic;
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
        // A type hint picks the branch Tailwind's tests would have: one HintSelectsNonColour names skips the colour,
        // and any other makes the value a colour or nothing.
        internal static bool? TryParseColorPrefix(
            string prefix, ReadOnlySpan<char> valueSpan, bool negate, string? hint, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return null;
            }
            var root = prefix.Substring(0, prefix.Length - 1);
            if (HintSelectsNonColour(root, hint))
            {
                return prefix == "bg-" ? false : null;
            }
            var colourOnly = hint != null;

            // text-[...] is overloaded: a color value wins, otherwise it falls through to font-size.
            if (prefix == "text-")
            {
                if (TryParseColor(valueSpan, true, out var textColor))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.TextColor, textColor);
                    return true;
                }
                return colourOnly ? false : null;
            }

            // bg-[...] is color-only here; a non-color value (e.g. bg-[addr:...]) rejects.
            if (prefix == "bg-")
            {
                if (TryParseColor(valueSpan, true, out var bgColor))
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
                return colourOnly ? false : null;
            }

            return null;
        }

        // Whether a type hint picks a branch other than the colour for a utility Tailwind types, named by its root:
        // the hints its switch handles before the colour default.
        internal static bool HintSelectsNonColour(string root, string? hint)
            => hint != null && s_nonColourHints.TryGetValue(root, out var hints) && Array.IndexOf(hints, hint) >= 0;

        // True for the utilities HintSelectsNonColour knows, each of which Tailwind writes an untyped value of as a
        // colour when its type tests take it as nothing else; a border side reads the border's hints.
        internal static bool DefaultsToColour(string root)
            => s_nonColourHints.ContainsKey(root) || StyleBorderSideColor.IsSideRoot(root);

        private static readonly Dictionary<string, string[]> s_nonColourHints = new()
        {
            ["text"] = new[] { "size", "length", "percentage", "absolute-size", "relative-size" },
            ["border"] = new[] { "length", "line-width" },
            ["bg"] = new[] { "percentage", "position", "bg-size", "length", "size", "image", "url" },
            ["ring"] = new[] { "length" }, ["ring-offset"] = new[] { "length" }, ["inset-ring"] = new[] { "length" },
            ["outline"] = new[] { "length", "number", "percentage" },
            ["from"] = new[] { "length", "percentage" }, ["via"] = new[] { "length", "percentage" },
            ["to"] = new[] { "length", "percentage" },
        };

        // Whether the browser rejects value where it is written as a <color>: a value TryParseColor does not read,
        // unless it is a function or a bare word, which this does not judge (var(), oklch(), currentcolor, a named
        // colour TryParseColor does not know).
        internal static bool RejectsAsColour(string value)
        {
            if (value.IndexOf('(') >= 0 || TryParseColor(value.AsSpan(), out _))
            {
                return false;
            }
            foreach (var c in value)
            {
                if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z'))
                {
                    return true;
                }
            }
            return false;
        }

        // Internal so the shadow, ring, divide, gradient and filter grammars share the one colour grammar, which
        // TryParseCssColor owns.
        internal static bool TryParseColor(ReadOnlySpan<char> valueStr, out Color color)
            => TryParseColor(valueStr, false, out color);

        // longhand is true for a caller whose CSS equivalent is a colour longhand Chromium's fast path reads
        // (color, background-color, a border side's colour, outline-color, caret-color): only such a colour can
        // have its hsl() saturation clamped, as IsPlainHsl describes. A shorthand, a shadow, a gradient, a filter
        // and a colour mixed with an opacity modifier all read through Chromium's general parser.
        internal static bool TryParseColor(ReadOnlySpan<char> valueStr, bool longhand, out Color color)
        {
            if (TryParseCssColor(valueStr.ToString(), longhand, out var css))
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
        internal static int ColorModifierSlashIndex(string s, int start)
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

            // bg-[addr:key]/N is Velvet's addressable image, not a colour behind a type hint.
            if (property == ArbitraryProperty.BackgroundColor
                && className.AsSpan(prefixLen).StartsWith("[addr:".AsSpan(), StringComparison.Ordinal))
            {
                return false;
            }

            if (ColorModifierSlashIndex(className, prefixLen) == -1
                || HintSelectsNonColour(className.Substring(0, prefixLen - 1), StyleArbitraryValueResolver.TypeHintOf(className)))
            {
                return false;
            }
            // MUTANT_SURVIVES(unreachable, literal): the slash found above means TryParseColorValue never reaches the slashless branch that reads longhand.
            if (!TryParseColorValue(className, prefixLen, false, out var color))
            {
                // A modifier outside 0..100% (TryParseColourKeyword).
                return TryParseColourKeyword(property, className, prefixLen, out result);
            }

            result = new ArbitraryStyle(property, color);
            return true;
        }

        // {text|bg|border}-{current|inherit}, current's opacity modifier, and a colour Tailwind writes through
        // color-mix() with a modifier outside 0..100%, which the browser computes as invalid and so as the property's
        // unset value. Cheap enough for IsInlineResolved's per-class check.
        internal static bool IsColourKeywordUtility(string core)
            => (core.StartsWith("bg-", StringComparison.Ordinal) || core.StartsWith("text-", StringComparison.Ordinal)
                    || core.StartsWith("border-", StringComparison.Ordinal))
                && (core.EndsWith("-current", StringComparison.Ordinal) || core.EndsWith("-inherit", StringComparison.Ordinal)
                    || core.IndexOf("-current/", StringComparison.Ordinal) >= 0);

        internal static bool TryParseColourKeywordUtility(string className, out ArbitraryStyle result)
        {
            result = default;
            if (!IsColourKeywordUtility(className))
            {
                return false;
            }
            if (className.StartsWith("bg-", StringComparison.Ordinal))
            {
                return TryParseColourKeyword(ArbitraryProperty.BackgroundColor, className, 3, out result);
            }
            if (className.StartsWith("text-", StringComparison.Ordinal))
            {
                return TryParseColourKeyword(ArbitraryProperty.TextColor, className, 5, out result);
            }
            return className.StartsWith("border-", StringComparison.Ordinal)
                && TryParseColourKeyword(ArbitraryProperty.BorderColor, className, 7, out result);
        }

        // The colour token from start, for property: `current` and `inherit`, and current's opacity modifier, as
        // Tailwind writes currentcolor and inherit; and a colour whose modifier lies outside 0..100%, which leaves the
        // color-mix() Tailwind writes invalid once computed, so property takes its unset value — a background's
        // transparent, the text colour's inherited one, a border's currentcolor. `inherit` takes no modifier, which
        // color-mix() cannot hold: Tailwind's declaration is dropped.
        internal static bool TryParseColourKeyword(ArbitraryProperty property, string token, int start, out ArbitraryStyle result)
        {
            result = default;
            var rest = token.AsSpan(start);
            if (rest.SequenceEqual("inherit".AsSpan()))
            {
                result = new ArbitraryStyle(property, Color.white, ColorReference.Inherit);
                return true;
            }
            var factor = Color.white;
            if (rest.StartsWith("current".AsSpan(), StringComparison.Ordinal))
            {
                if (rest.Length == "current".Length)
                {
                    result = new ArbitraryStyle(property, factor, ColorReference.Current);
                    return true;
                }
                if (rest[7] != '/' || !TryParseModifierPercent(rest.Slice(8), out var percent))
                {
                    return false;
                }
                if (percent >= 0f && percent <= 100f)
                {
                    factor.a = percent / 100f;
                    result = new ArbitraryStyle(property, factor, ColorReference.Current);
                    return true;
                }
                // Tailwind writes currentcolor first and the color-mix() behind a feature query, whose percentage
                // outside 0..100% is a parse error there, so the property is plain currentcolor.
                result = new ArbitraryStyle(property, factor, ColorReference.Current);
                return true;
            }
            else if (!RejectedAtComputedTime(token, start))
            {
                return false;
            }
            result = property == ArbitraryProperty.BackgroundColor
                ? new ArbitraryStyle(property, Color.clear)
                : new ArbitraryStyle(property, factor,
                    property == ArbitraryProperty.TextColor ? ColorReference.Inherit : ColorReference.Current);
            return true;
        }

        // The value of a color utility from start to the end of token: a palette name or a bracketed color,
        // optionally followed by the opacity modifier, which scales the color's alpha.
        internal static bool TryParseColorValue(string token, int start, bool longhand, out Color color)
        {
            // Split base from modifier on the '/' that lies OUTSIDE any [...] bracket, so a bracketed base
            // (bg-[#fff]/50), a bracketed alpha (bg-blue-500/[0.32]), or an in-bracket '/' all resolve right.
            var slash = ColorModifierSlashIndex(token, start);
            if (slash == -1)
            {
                return VelvetPalette.TryResolveColorToken(token.Substring(start), longhand, out color);
            }

            if (!VelvetPalette.TryResolveColorToken(token.Substring(start, slash - start), out color))
            {
                return false;
            }
            return TryScaleAlpha(token.AsSpan(slash + 1), ref color);
        }

        internal static bool TryScaleAlpha(ReadOnlySpan<char> modifier, ref Color color)
        {
            if (!TryParseAlphaModifier(modifier, out var alpha))
            {
                return false;
            }

            color.a *= alpha;
            return true;
        }

        internal enum ColourVerdict
        {
            None,
            Valid,
            Rejected,
        }

        // Whether the colour utility at start has an opacity modifier outside 0..100% on a colour Tailwind writes
        // through var(): a palette name other than transparent, which Tailwind writes as the keyword, and a bracket
        // holding a var(). Such a color-mix() is invalid only once computed, so the property is unset. Any other
        // colour's out-of-range percentage is a parse error that drops the declaration, leaving an earlier class
        // standing.
        internal static bool RejectedAtComputedTime(string token, int start)
        {
            var slash = ColorModifierSlashIndex(token, start);
            if (start >= token.Length || slash < 0 || !TryParseModifierPercent(token.AsSpan(slash + 1), out var percent)
                || (percent >= 0f && percent <= 100f))
            {
                return false;
            }
            var colour = token.Substring(start, slash - start);
            return colour[0] == '['
                ? colour.IndexOf("var(", StringComparison.OrdinalIgnoreCase) >= 0
                : colour != "transparent" && VelvetPalette.TryResolveColorToken(colour, out _);
        }

        // A colour utility's value from start: a palette name or a bracketed colour, with or without an opacity
        // modifier. Rejected where the modifier's percentage lies outside 0..100, which leaves the color-mix()
        // Tailwind writes invalid; None where the token is no colour Tailwind generates.
        internal static ColourVerdict VerdictOfColourValue(string token, int start)
        {
            var slash = ColorModifierSlashIndex(token, start);
            var colour = slash < 0 ? token.Substring(start) : token.Substring(start, slash - start);
            if (!VelvetPalette.TryResolveColorToken(colour, out _))
            {
                return ColourVerdict.None;
            }
            if (slash < 0)
            {
                return ColourVerdict.Valid;
            }
            if (!TryParseModifierPercent(token.AsSpan(slash + 1), out var percent))
            {
                return ColourVerdict.None;
            }
            return percent < 0f || percent > 100f ? ColourVerdict.Rejected : ColourVerdict.Valid;
        }

        // Parses the alpha portion of a color opacity modifier, a percent from 0 to 100 as TryParseModifierPercent reads
        // it (50 -> 0.5). Returns false for any other form, and for a percent outside that range, which makes the
        // color-mix() Tailwind writes invalid.
        internal static bool TryParseAlphaModifier(ReadOnlySpan<char> span, out float alpha)
        {
            alpha = 0f;
            if (!TryParseModifierPercent(span, out var percent) || percent < 0f || percent > 100f)
            {
                return false;
            }
            alpha = percent / 100f;
            return true;
        }

        // The percent an opacity modifier names, in the forms Tailwind generates: a number in steps of 0.25 written
        // as a number prints (50, 2.25, 0.5 — not 05, .5 or 50.0), or a bracketed number, which is a fraction of
        // 100% ([0.32]), or a bracketed percentage ([33.3%]). A bracket may hold any value, a negative one or one
        // past 100% included; what that does is the reader's to decide.
        internal static bool TryParseModifierPercent(ReadOnlySpan<char> span, out float percent)
        {
            percent = 0f;
            if (span.Length == 0)
            {
                return false;
            }
            if (StyleArbitraryValueResolver.TryStripBrackets(span, 0, out var inner))
            {
                var isPercent = inner.Length > 0 && inner[inner.Length - 1] == '%';
                var number = isPercent ? inner.Slice(0, inner.Length - 1) : inner;
                if (!float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                {
                    return false;
                }
                percent = isPercent ? value : value * 100f;
                return true;
            }
            if (!float.TryParse(span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var named)
                || (named * 4f) % 1f != 0f
                || !span.SequenceEqual(named.ToString(CultureInfo.InvariantCulture).AsSpan()))
            {
                return false;
            }
            percent = named;
            return true;
        }

        // A colour as CSS Color 4 writes it, kept in the space it names: rgb()/rgba() and hsl()/hsla() in the comma
        // syntax or the space syntax with a '/' alpha, hwb(), transparent, and otherwise the #hex and names
        // ColorUtility parses. Underscores stand in for the spaces a class string cannot carry, as the shadow and
        // clip-path grammars read them.
        private static bool TryParseCssColor(string value, bool longhand, out CssColor color)
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
            var name = s.Substring(0, open);
            var space = SpaceOf(name.ToLowerInvariant());
            if (space == null)
            {
                return false;
            }
            var body = s.Substring(open + 1, s.Length - open - 2).ToLowerInvariant();
            var fastName = longhand && (name is "hsl" or "hsla");
            switch (space.Value)
            {
                case CssColorSpace.Srgb:
                    return TryParseRgb(body, out color);
                case CssColorSpace.Hsl:
                    return TryParseHsl(body, fastName, out color);
                default:
                    return TryParseModern(body, space.Value, false, out color);
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
            var parts = StyleGradientClass.SplitTopLevel(body, ',').ToArray();
            if (parts.Length == 1)
            {
                // MUTANT_SURVIVES(equivalent, literal): an rgb() channel is clamped to 1 at parse, so the saturation clamp IsPlainHsl gates leaves it as it is.
                return TryParseModern(body, CssColorSpace.Srgb, false, out color);
            }
            color = new CssColor(CssColorSpace.Srgb, 0, 0, 0, 1);
            if (parts.Length != 3 && parts.Length != 4)
            {
                return false;
            }
            var percent = IsPercentage(parts[0].Trim());
            for (var i = 0; i < 3; i++)
            {
                var token = parts[i].Trim();
                if (IsPercentage(token) != percent
                    || !TryParseLegacyToken(token, 255, out var channel))
                {
                    return false;
                }
                color[i] = Math.Clamp(channel / 255, 0, 1);
            }
            return TryParseLegacyAlpha(parts, ref color);
        }

        // The comma syntax takes a hue and two percentages, and no none.
        private static bool TryParseHsl(string body, bool fastName, out CssColor color)
        {
            var parts = StyleGradientClass.SplitTopLevel(body, ',').ToArray();
            if (parts.Length == 1)
            {
                return TryParseModern(body, CssColorSpace.Hsl, fastName, out color);
            }
            color = new CssColor(CssColorSpace.Hsl, 0, 0, 0, 1);
            if (parts.Length != 3 && parts.Length != 4)
            {
                return false;
            }
            var hue = parts[0].Trim();
            var saturation = parts[1].Trim();
            var lightness = parts[2].Trim();
            if (hue == "none" || !TryParseHue(hue, out color.C0)
                || !IsPercentage(saturation) || !TryParseLegacyToken(saturation, 100, out color.C1)
                || !IsPercentage(lightness) || !TryParseLegacyToken(lightness, 100, out color.C2))
            {
                return false;
            }
            // Both of Chromium's parsers clamp a comma-syntax saturation at each end, whatever its spelling.
            color.C1 = Math.Clamp(color.C1, 0, 100);
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
        private static bool TryParseModern(string body, CssColorSpace space, bool fastName, out CssColor color)
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
            if (alphaToken != null)
            {
                if (!TryParseComponent(alphaToken, 1, out var alpha))
                {
                    return false;
                }
                color.Alpha = Math.Clamp(alpha, 0, 1);
            }
            if (fastName && IsPlainHsl(tokens, alphaToken, body))
            {
                color.C1 = Math.Min(color.C1, 100);
            }
            return true;
        }

        // Chromium clamps a space-syntax saturation to 100% only on the fast path it reads a plain colour with: a
        // decimal hue with an optional angle unit, two decimal percentages and a decimal alpha that nothing separates
        // from the closing bracket, each without an exponent, a '+' or a none. A colour with any component spelled
        // another way reaches its general parser, which leaves the saturation unclamped above. The caller adds the
        // function's name and the property, which the fast path also keys on. Pinned by
        // Given_AnHslSaturationAbove100_When_ParsedAsALonghand_Then_ItClampsWhereChromiumReadsItPlain
        // and Given_AnHslSaturationAbove100_When_ChromiumDeclinesItsFastPath_Then_ItStaysUnclamped.
        private static bool IsPlainHsl(string[] tokens, string? alpha, string body)
        {
            return IsPlainHue(tokens[0])
                && IsPlainPercent(tokens[1])
                && IsPlainPercent(tokens[2])
                && (alpha == null || (IsPlainDecimal(alpha, true) && body.EndsWith(alpha, StringComparison.Ordinal)));
        }

        private static bool IsPlainHue(string token)
        {
            var end = token.Length;
            for (; end > 0 && char.IsLetter(token[end - 1]); end--)
            {
            }
            return IsPlainDecimal(token.Substring(0, end), true);
        }

        private static bool IsPlainPercent(string token)
        {
            return token.EndsWith("%", StringComparison.Ordinal)
                && IsPlainDecimal(token.Substring(0, token.Length - 1), false);
        }

        // An optional '-', digits and an optional fraction; a number with no digits before its point is plain only
        // where bareFraction allows it. The parser has read every component as a number or as none before this
        // looks, so a point is always followed by digits.
        private static bool IsPlainDecimal(string token, bool bareFraction)
        {
            var i = token.Length > 0 && token[0] == '-' ? 1 : 0;
            var whole = SkipDigits(token, ref i);
            var fraction = 0;
            if (i < token.Length && token[i] == '.')
            {
                i++;
                fraction = SkipDigits(token, ref i);
            }
            return i == token.Length && (whole > 0 || (bareFraction && fraction > 0));
        }

        private static double ClampAtParse(CssColorSpace space, int index, double value)
        {
            if (space == CssColorSpace.Srgb)
            {
                return Math.Clamp(value / 255, 0, 1);
            }
            // MUTANT_SURVIVES(equivalent): TryParseHue normalises the hue at index 0, so it is never below 0.
            return index > 0 && space == CssColorSpace.Hsl ? Math.Max(value, 0) : value;
        }

        // Splits a space-syntax body into its three components and the alpha after a '/', which may sit with or
        // without spaces around it.
        private static bool TrySplitModern(string body, out string[] components, out string? alpha)
        {
            components = new string[3];
            alpha = null;
            var count = 0;
            var afterSlash = false;
            foreach (var part in SplitModern(body))
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

        // The words of a space-syntax body, with each '/' a word of its own, outside parentheses: a math function's
        // own spaces and its division stay in its word.
        private static List<string> SplitModern(string body)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i <= body.Length; i++)
            {
                var c = i < body.Length ? body[i] : ' ';
                depth += c == '(' ? 1 : c == ')' ? -1 : 0;
                if (depth != 0 || (c != ' ' && c != '/'))
                {
                    continue;
                }
                parts.Add(body.Substring(start, i - start));
                if (c == '/')
                {
                    parts.Add("/");
                }
                start = i + 1;
            }
            return parts;
        }

        // Whether a channel is a percentage: written as one, or a math function coming to one.
        private static bool IsPercentage(string token)
            => token.EndsWith("%", StringComparison.Ordinal)
                || StyleLengthExpression.TypeOf(token.AsSpan()) == MathType.Percentage;

        // A number, a percentage of the reference, or none, which reads as zero. A math function comes to either.
        private static bool TryParseComponent(string token, double percentReference, out double value)
        {
            value = 0;
            if (token == "none")
            {
                return true;
            }
            if (StyleLengthExpression.OpensMathFunction(token.AsSpan()))
            {
                var parsed = StyleLengthExpression.TryParseNumberOrPercentage(token.AsSpan(), out var math, out var percent);
                value = percent ? math / 100 * percentReference : math;
                return parsed;
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
            if (StyleLengthExpression.OpensMathFunction(token.AsSpan()))
            {
                var parsed = StyleLengthExpression.TryParseAngle(token.AsSpan(), out var angle)
                    || StyleLengthExpression.TryParseNumber(token.AsSpan(), out angle);
                degrees = parsed ? CssColorMath.NormalizeHue(angle) : 0;
                return parsed;
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
            degrees = CssColorMath.NormalizeHue(number * scale);
            return true;
        }

        // A literal past float's range reads as float's largest of its sign, as Chromium's tokenizer reads one,
        // so a hue of 1e300 is the hue of 3.4028235e38 and every component clamps from the same end. A literal
        // past double's range takes its sign from the token, whether the parser declines it or returns an infinity.
        private static bool TryParseNumber(string token, out double value)
        {
            value = 0;
            if (!IsCssNumber(token))
            {
                return false;
            }
            var largest = (double)float.MaxValue;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || double.IsInfinity(parsed))
            {
                value = token[0] == '-' ? -largest : largest;
                return true;
            }
            value = Math.Clamp(parsed, -largest, largest);
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
