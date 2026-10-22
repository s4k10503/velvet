using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One layer of a CSS box-shadow list. Corner radius is deliberately NOT part of the spec — it follows the
    /// target's own <c>rounded-*</c> radius (resolved at layout time, or from the class scale off-panel), so a
    /// card and a pill get the right shadow silhouette without restating the radius on the shadow.
    /// </summary>
    internal readonly struct ShadowSpec
    {
        public readonly Color Color;
        public readonly float Blur;
        public readonly float OffsetY;
        public readonly float Spread;
        // Horizontal offset (px). The presets are centered (0); only an arbitrary shadow-[x y …] sets it.
        public readonly float OffsetX;
        // An inner shadow (CSS's `inset`), drawn inside the padding box rather than outside the border box.
        public readonly bool Inset;

        public ShadowSpec(Color color, float blur, float offsetY, float spread)
            : this(color, blur, offsetY, spread, 0f)
        {
        }

        public ShadowSpec(Color color, float blur, float offsetY, float spread, float offsetX, bool inset = false)
        {
            Color = color;
            Blur = blur;
            OffsetY = offsetY;
            Spread = spread;
            OffsetX = offsetX;
            Inset = inset;
        }
    }

    // Parses Velvet's shadow-* utility classes into the layers of a box-shadow list, and resolves the companion
    // rounded-* corner radius the shadow follows. Same shape as StyleGapClass / StyleVariantClass: a cheap
    // prefix gate (HasShadowClass) plus a cascade-correct extractor (TryExtract, last matching class wins).
    // The presets are Tailwind v4's --shadow-* theme values. shadow-none is a recognized utility that resolves
    // to "no shadow" (so it can override an earlier shadow-lg in the cascade). Arbitrary values
    // (shadow-[x_y_blur_spread_color], underscores for spaces, commas between layers) are parsed per layer
    // positionally as CSS box-shadow lengths plus an optional color.
    internal static class StyleShadowClass
    {
        private static Color Black(float alpha) => new(0f, 0f, 0f, alpha);

        // The colour an arbitrary layer without one takes.
        private static readonly Color ArbitraryDefaultColor = new(4f / 255f, 12f / 255f, 24f / 255f, 0.20f);

        // suffix → layers, first on top, as theme.css declares --shadow-* (and --shadow for the bare `shadow`).
        private static readonly Dictionary<string, ShadowSpec[]> Presets = new()
        {
            ["2xs"] = new[] { new ShadowSpec(Black(0.05f), 0f, 1f, 0f) },
            ["xs"] = new[] { new ShadowSpec(Black(0.05f), 2f, 1f, 0f) },
            [""] = new[] { new ShadowSpec(Black(0.1f), 3f, 1f, 0f), new ShadowSpec(Black(0.1f), 2f, 1f, -1f) },
            ["sm"] = new[] { new ShadowSpec(Black(0.1f), 3f, 1f, 0f), new ShadowSpec(Black(0.1f), 2f, 1f, -1f) },
            ["md"] = new[] { new ShadowSpec(Black(0.1f), 6f, 4f, -1f), new ShadowSpec(Black(0.1f), 4f, 2f, -2f) },
            ["lg"] = new[] { new ShadowSpec(Black(0.1f), 15f, 10f, -3f), new ShadowSpec(Black(0.1f), 6f, 4f, -4f) },
            ["xl"] = new[] { new ShadowSpec(Black(0.1f), 25f, 20f, -5f), new ShadowSpec(Black(0.1f), 10f, 8f, -6f) },
            ["2xl"] = new[] { new ShadowSpec(Black(0.25f), 50f, 25f, -12f) },
            ["inner"] = new[] { new ShadowSpec(Black(0.05f), 4f, 2f, 0f, 0f, inset: true) },
        };

        // Mirrors --radius-* in _tokens.uss. rounded-full / arbitrary radii are left to the
        // geometry-driven resolvedStyle path (TryResolveCornerRadius returns false for them).
        private static readonly Dictionary<string, float> RadiusScale = new()
        {
            ["none"] = 0f,
            [""] = 4f,      // bare `rounded` == --radius-default
            ["xs"] = 2f,
            ["sm"] = 4f,
            ["md"] = 6f,
            ["lg"] = 8f,
            ["xl"] = 12f,
            ["2xl"] = 16f,
            ["3xl"] = 24f,
            ["4xl"] = 32f,
        };

        // Single source for the --radius-* scale, keyed by the suffix after a rounded-* prefix (the empty
        // string being bare `rounded`) — shared so a per-class radius recognizer (MotionPropertyClassParser)
        // resolves the SAME magnitudes instead of holding a second copy that could drift, mirroring
        // StyleArbitraryValueResolver.TryGetSpacingPx's precedent. `full` is deliberately absent: it is a
        // saturating radius sentinel (--radius-full is deliberately oversized), not a magnitude a caller
        // can interpolate through.
        internal static bool TryGetRadiusPx(string suffix, out float px) => RadiusScale.TryGetValue(suffix, out px);

        // True when cls belongs to the shadow family this layer owns (recognized or not-yet-valid).
        // Both the array gate below and the variant-payload gate answer through this one predicate, so
        // which tokens count as "a shadow utility" is defined in exactly one place. drop-shadow-* is a
        // filter (StyleFilterValueParser.TryParseDropShadow), composed apart from this paint as CSS composes
        // filter apart from box-shadow.
        public static bool IsShadowClass(string cls)
            => !string.IsNullOrEmpty(cls) && (cls == "shadow" || cls.StartsWith("shadow-", StringComparison.Ordinal));

        // Cheap early-out gate: true when ANY class is shadow or begins with shadow-. Used
        // to skip the full parse on the ~99% of elements that carry no shadow class and no binding.
        public static bool HasShadowClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsShadowClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Returns the layers of the last recognized shadow-* utility in classNames (CSS cascade: later classes
        // win), first on top. Returns false when no shadow is wanted — either no shadow class at all, or the
        // winning class is shadow-none.
        public static bool TryExtract(string[] classNames, out ShadowSpec[] spec)
        {
            spec = Array.Empty<ShadowSpec>();
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            // A box-shadow the browser discards takes every shadow-* with it.
            var discarded = StyleRingClass.DiscardsBoxShadow(classNames);
            foreach (var cls in classNames)
            {
                if (discarded && IsShadowClass(cls))
                {
                    continue;
                }
                if (TryParse(cls!, out var parsed, out var wantShadow))
                {
                    // A recognized utility overrides earlier ones; shadow-none flips found back to false.
                    found = wantShadow;
                    if (wantShadow)
                    {
                        spec = parsed;
                    }
                }
            }
            return found;
        }

        private static bool TryParse(string cls, out ShadowSpec[] spec, out bool wantShadow)
        {
            spec = Array.Empty<ShadowSpec>();
            wantShadow = false;
            if (string.IsNullOrEmpty(cls) || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
            {
                return false;
            }

            string suffix;
            if (cls == "shadow")
            {
                suffix = "";
            }
            else if (cls.StartsWith("shadow-", StringComparison.Ordinal))
            {
                suffix = cls.Substring("shadow-".Length);
            }
            else
            {
                return false;
            }

            if (suffix == "none")
            {
                wantShadow = false;
                return true;
            }
            if (Presets.TryGetValue(suffix, out var preset))
            {
                spec = preset;
                wantShadow = true;
                return true;
            }
            // Arbitrary value: shadow-[x_y_blur_spread_#color] (underscores are spaces). Sits AFTER the
            // preset lookup so a named preset never reaches it.
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                && TryParseArbitraryList(inner.ToString(), out spec))
            {
                wantShadow = true;
                return true;
            }
            // Unrecognized shadow-* utility: not a known value.
            return false;
        }

        // Whether cls is a utility Tailwind writes a box-shadow for: shadow, a preset, shadow-none, or a bracket it does
        // not type as a colour, which it writes into --tw-shadow-color alone.
        internal static bool DeclaresBoxShadow(string cls)
        {
            if (cls == "shadow" || cls == "shadow-none"
                || (cls.StartsWith("shadow-", StringComparison.Ordinal) && Presets.ContainsKey(cls.Substring("shadow-".Length))))
            {
                return true;
            }
            return TryReadBracket(cls, out var hint, out var value)
                && (hint != null ? hint != "color" : !TailwindDataTypes.IsColor(value.AsSpan()));
        }

        // Whether cls is a shadow-[…] whose value the browser rejects as a box-shadow, for a class DeclaresBoxShadow
        // takes. Each layer is two to four lengths in a row, the third not negative, with at most one colour and one
        // inset around them; a layer holding a var() is not judged, nor is a function or a word as a colour
        // (StyleColorValueParser.RejectsAsColour).
        internal static bool HoldsRejectedValue(string cls)
        {
            if (!TryReadBracket(cls, out _, out var value))
            {
                return false;
            }
            foreach (var layer in StyleGradientClass.SplitTopLevel(value.Replace('_', ' '), ','))
            {
                if (layer.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0 && RejectsAsLayer(layer))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool RejectsAsLayer(string layer)
        {
            var lengths = 0;
            var lengthsEnded = false;
            var colours = 0;
            var insets = 0;
            foreach (var token in StyleGradientClass.SplitTopLevel(layer, ' '))
            {
                if (token.Length == 0)
                {
                    continue;
                }
                if (!TailwindDataTypes.IsColor(token.AsSpan()) && (token == "0" || StyleCompositeRejection.IsLength(token)
                    || TailwindMathSpacing.HasMathFunction(token)))
                {
                    if (lengthsEnded || (lengths == 2 && token[0] == '-'))
                    {
                        return true;
                    }
                    lengths++;
                    continue;
                }
                lengthsEnded = lengths > 0;
                if (string.Equals(token, "inset", StringComparison.OrdinalIgnoreCase))
                {
                    insets++;
                }
                else if (StyleColorValueParser.RejectsAsColour(token))
                {
                    return true;
                }
                else
                {
                    colours++;
                }
            }
            return lengths < 2 || lengths > 4 || colours > 1 || insets > 1;
        }

        // The bracket of a shadow-[…] class with its type hint split off, whatever /modifier follows it.
        private static bool TryReadBracket(string cls, out string? hint, out string value)
        {
            hint = null;
            value = string.Empty;
            const string open = "shadow-[";
            var close = cls.LastIndexOf(']');
            if (!cls.StartsWith(open, StringComparison.Ordinal) || close <= open.Length
                || !StyleArbitraryValueResolver.TrySplitTypeHint(cls.AsSpan(open.Length, close - open.Length), out hint, out var span))
            {
                return false;
            }
            value = span.ToString();
            return true;
        }

        // Parses the inner body of a shadow-[…] arbitrary value: comma-separated layers, first on top. One
        // malformed layer rejects the whole list, as the browser drops a box-shadow value it cannot read.
        private static bool TryParseArbitraryList(string body, out ShadowSpec[] spec)
        {
            spec = Array.Empty<ShadowSpec>();
            var layers = StyleGradientClass.SplitTopLevel(body, ',');
            var parsed = new ShadowSpec[layers.Count];
            for (var i = 0; i < layers.Count; i++)
            {
                if (!TryParseArbitrary(layers[i], out parsed[i]))
                {
                    return false;
                }
            }
            spec = parsed;
            return parsed.Length > 0;
        }

        // Parses one layer. Tokens are whitespace-separated (the className uses '_' for spaces); each is
        // classified as a length (CSS box-shadow offset/blur/spread, assigned positionally as x, y, blur, spread)
        // or a color (the shared StyleColorValueParser grammar). At least the x+y offsets are required (matching
        // CSS); blur/spread default to 0 and the color defaults to ArbitraryDefaultColor. Percent lengths are
        // rejected, as CSS rejects them in a box-shadow.
        private static bool TryParseArbitrary(string body, out ShadowSpec spec)
        {
            spec = default;
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            // Split outside parentheses, so a colour function's own spaces (rgb(0_0_0_/_25%)) stay in its token.
            var tokens = StyleGradientClass.SplitTopLevel(body.Replace('_', ' '), ' ');
            tokens.RemoveAll(t => t.Length == 0);
            var lengths = new float[4]; // x, y, blur, spread
            var lengthCount = 0;
            var color = ArbitraryDefaultColor;
            var hasColor = false;
            var inset = false;

            foreach (var token in tokens)
            {
                if (token == "inset" && !inset)
                {
                    inset = true;
                    continue;
                }
                if (StyleArbitraryValueResolver.TryParseValue(token.AsSpan(), out var value, out var unit)
                    && unit == LengthUnit.Pixel)
                {
                    if (lengthCount >= 4)
                    {
                        return false; // more than four box-shadow lengths is malformed
                    }
                    lengths[lengthCount++] = value;
                }
                else if (!hasColor && StyleColorValueParser.TryParseColor(token.AsSpan(), out var parsedColor))
                {
                    color = parsedColor;
                    hasColor = true;
                }
                else
                {
                    return false; // an unclassifiable token (e.g. rgba(), a percent length) is not recognized
                }
            }

            if (lengthCount < 2)
            {
                return false; // a box-shadow needs at least x and y offsets
            }

            if (lengths[2] < 0f)
            {
                return false; // CSS rejects a negative blur radius
            }
            spec = new ShadowSpec(color, lengths[2], lengths[1], lengths[3], lengths[0], inset);
            return true;
        }

        // Resolves the top-left corner radius (px) from the rounded-* / rounded-t-* /
        // rounded-l-* / rounded-tl-* classes that affect the top-left corner, mirroring the
        // --radius-* token scale. BoxShadowSilhouette uses it as the pre-layout stand-in for all four corners;
        // once laid out, each corner's own resolved radius replaces it. Of the matching classes the one Tailwind emits last wins, as
        // StyleSpacingPrecedence ranks them and StyleRuleOrder.CompareUtilities orders two that set the same corners. Returns false for
        // rounded-full / arbitrary radii (those resolve via resolvedStyle.borderTopLeftRadius
        // once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
        {
            radius = 0f;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            var heldRank = int.MinValue;
            var heldClass = string.Empty;
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                string suffix;
                ArbitraryProperty property;
                // Longest prefix first so rounded-tl-/rounded-t-/rounded-l- are not swallowed by rounded-.
                if (cls.StartsWith("rounded-tl-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-tl-".Length);
                    property = ArbitraryProperty.BorderTopLeftRadius;
                }
                else if (cls.StartsWith("rounded-ss-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-ss-".Length);
                    property = ArbitraryProperty.BorderStartStartRadius;
                }
                else if (cls.StartsWith("rounded-s-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-s-".Length);
                    property = ArbitraryProperty.BorderStartRadius;
                }
                else if (cls.StartsWith("rounded-t-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-t-".Length);
                    property = ArbitraryProperty.BorderTopRadius;
                }
                else if (cls.StartsWith("rounded-l-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-l-".Length);
                    property = ArbitraryProperty.BorderLeftRadius;
                }
                else if (cls.StartsWith("rounded-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-".Length);
                    property = ArbitraryProperty.BorderRadius;
                }
                else if (cls == "rounded")
                {
                    suffix = ""; // bare `rounded` == --radius-default
                    property = ArbitraryProperty.BorderRadius;
                }
                else
                {
                    continue;
                }

                if (!RadiusScale.TryGetValue(suffix, out var r))
                {
                    continue;
                }
                var rank = StyleSpacingPrecedence.RankOf(property);
                if (rank > heldRank || (rank == heldRank && StyleRuleOrder.CompareUtilities(heldClass, cls) <= 0))
                {
                    radius = r;
                    heldRank = rank;
                    heldClass = cls;
                    found = true;
                }
            }
            return found;
        }
    }
}
