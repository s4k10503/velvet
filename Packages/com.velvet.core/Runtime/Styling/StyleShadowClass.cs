using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// A resolved drop-shadow preset: the SDF shader inputs a <c>shadow-*</c> utility maps to. Corner radius is
    /// deliberately NOT part of the spec — it follows the target's own <c>rounded-*</c> radius (resolved at
    /// layout time, or from the class scale off-panel), so a card and a pill get the right shadow silhouette
    /// without restating the radius on the shadow.
    /// </summary>
    internal readonly struct ShadowSpec
    {
        // The shadow's colour, or, where CurrentColor is set, the factor the text colour's alpha is scaled by.
        public readonly Color Color;
        public readonly float Blur;
        public readonly float OffsetY;
        public readonly float Spread;
        // Horizontal offset (px). The presets are centered (0); only an arbitrary shadow-[x y …] sets it.
        public readonly float OffsetX;
        // The text colour: a shadow-[…] naming no colour is var(--tw-shadow-color, currentcolor), and
        // shadow-current names it.
        public readonly bool CurrentColor;
        // shadow-inherit: the nearest ancestor's shadow colour (ShadowColorInheritance), the colour above where no
        // ancestor has one.
        public readonly bool InheritColor;
        // An inset-shadow-* layer, whose inherited colour is --tw-inset-shadow-color rather than --tw-shadow-color.
        public readonly bool Inset;

        public ShadowSpec(Color color, float blur, float offsetY, float spread)
            : this(color, blur, offsetY, spread, 0f)
        {
        }

        public ShadowSpec(Color color, float blur, float offsetY, float spread, float offsetX,
            bool currentColor = false, bool inheritColor = false, bool inset = false)
        {
            Color = color;
            Blur = blur;
            OffsetY = offsetY;
            Spread = spread;
            OffsetX = offsetX;
            CurrentColor = currentColor;
            InheritColor = inheritColor;
            Inset = inset;
        }

        public ShadowSpec AsInset() => new(Color, Blur, OffsetY, Spread, OffsetX, CurrentColor, InheritColor, inset: true);

        // This shadow in the colour a shadow colour class of kind names: color itself, the text colour scaled by
        // color.a, or the ancestor's, the shadow keeping its own colour to fall back on.
        public ShadowSpec WithColor(ShadowColorSource kind, Color color) => kind switch
        {
            ShadowColorSource.Inherit => new(Color, Blur, OffsetY, Spread, OffsetX, CurrentColor, inheritColor: true, Inset),
            _ => new(color, Blur, OffsetY, Spread, OffsetX, currentColor: kind == ShadowColorSource.Current, inset: Inset),
        };

        // The colour the shadow is painted in on element.
        public Color TintFor(VisualElement element)
        {
            if (InheritColor && ShadowColorInheritance.TryInherited(element, Inset, out var inherited))
            {
                return inherited;
            }
            if (!CurrentColor)
            {
                return Color;
            }
            var text = element.resolvedStyle.color;
            text.a *= Color.a;
            return text;
        }
    }

    // What a shadow colour class names: a colour, the text colour (shadow-current) or the ancestor's
    // (shadow-inherit).
    internal enum ShadowColorSource
    {
        Explicit,
        Current,
        Inherit,
    }

    // Parses Velvet's shadow-* AND drop-shadow-* utility classes into a ShadowSpec
    // preset, and resolves the companion rounded-* corner radius the shadow follows. Same shape as
    // StyleGapClass / StyleVariantClass: a cheap prefix gate
    // (HasShadowClass) plus a cascade-correct extractor (TryExtract, last
    // matching class wins).
    // The scale is graduated for the soft, iOS/macOS-style SDF shadow Velvet renders (a glow, not a CSS
    // hard edge), so the blur values are larger than a literal CSS box-shadow. Alpha derives from
    // the --color-shadow token (rgba(4, 12, 24, …) in _tokens.uss), stepped per
    // preset. shadow-none is a recognized utility that resolves to "no shadow" (so it can override
    // an earlier shadow-lg in the cascade). Arbitrary values (shadow-[x_y_blur_spread_color], underscores
    // for spaces) are parsed positionally as CSS box-shadow lengths plus an optional color; the
    // SDF shadow has a single uniform softness, so the four-channel CSS shadow collapses to one layer.
    internal static class StyleShadowClass
    {
        // Base RGB from --color-shadow (rgba(4, 12, 24, *)); alpha graduated per preset.
        private static Color ShadowColor(float alpha) => new(4f / 255f, 12f / 255f, 24f / 255f, alpha);

        // suffix → (color, blur, offsetY, spread). "" is the bare `shadow` (the DEFAULT).
        private static readonly Dictionary<string, ShadowSpec> Presets = new()
        {
            ["sm"] = new ShadowSpec(ShadowColor(0.16f), 8f, 2f, 0f),
            [""] = new ShadowSpec(ShadowColor(0.20f), 14f, 3f, 0f),
            ["md"] = new ShadowSpec(ShadowColor(0.24f), 22f, 5f, 0f),
            ["lg"] = new ShadowSpec(ShadowColor(0.28f), 34f, 8f, 0f),
            ["xl"] = new ShadowSpec(ShadowColor(0.32f), 48f, 12f, 0f),
            ["2xl"] = new ShadowSpec(ShadowColor(0.40f), 64f, 18f, 0f),
        };

        // drop-shadow-* presets: a single-shadow approximation of the filter: drop-shadow()
        // scale (whose presets stack two shadows — Velvet's one SDF pass keeps the dominant layer).
        // Tighter and fainter than the shadow-* scale. In Velvet both families render
        // through the same silhouette-following mechanism (the painted shadow already follows a
        // skewed caster), so the practical difference is the preset scale.
        private static readonly Dictionary<string, ShadowSpec> DropPresets = new()
        {
            ["sm"] = new ShadowSpec(ShadowColor(0.05f), 2f, 1f, 0f),
            [""] = new ShadowSpec(ShadowColor(0.10f), 4f, 1f, 0f),
            ["md"] = new ShadowSpec(ShadowColor(0.12f), 6f, 4f, 0f),
            ["lg"] = new ShadowSpec(ShadowColor(0.10f), 16f, 10f, 0f),
            ["xl"] = new ShadowSpec(ShadowColor(0.11f), 26f, 20f, 0f),
            ["2xl"] = new ShadowSpec(ShadowColor(0.15f), 50f, 25f, 0f),
        };

        // Mirrors --radius-* in _tokens.uss. rounded-full / arbitrary radii are left to the
        // geometry-driven resolvedStyle path (TryResolveCornerRadius returns false for them).
        private static readonly Dictionary<string, float> RadiusScale = new()
        {
            ["none"] = 0f,
            [""] = 4f,      // bare `rounded` == --radius-default
            ["sm"] = 2f,
            ["md"] = 6f,
            ["lg"] = 8f,
            ["xl"] = 12f,
            ["2xl"] = 16f,
            ["3xl"] = 24f,
        };

        // Single source for the --radius-* scale, keyed by the suffix after a rounded-* prefix (the empty
        // string being bare `rounded`) — shared so a per-class radius recognizer (MotionPropertyClassParser)
        // resolves the SAME magnitudes instead of holding a second copy that could drift, mirroring
        // StyleArbitraryValueResolver.TryGetSpacingPx's precedent. `full` is deliberately absent: it is a
        // saturating radius sentinel (--radius-full is deliberately oversized), not a magnitude a caller
        // can interpolate through.
        internal static bool TryGetRadiusPx(string suffix, out float px) => RadiusScale.TryGetValue(suffix, out px);

        // True when cls belongs to either shadow family this layer owns (recognized or not-yet-valid).
        // Both the array gate below and the variant-payload gate answer through this one predicate, so
        // which tokens count as "a shadow utility" is defined in exactly one place.
        public static bool IsShadowClass(string cls)
            => !string.IsNullOrEmpty(cls)
                && (IsBoxShadowClass(cls)
                    || cls == "drop-shadow" || cls.StartsWith("drop-shadow-", StringComparison.Ordinal));

        // shadow, shadow-*, and shadow/<n>, the bare size with an opacity modifier.
        internal static bool IsBoxShadowClass(string cls)
            => cls == "shadow" || cls.StartsWith("shadow-", StringComparison.Ordinal)
                || cls.StartsWith("shadow/", StringComparison.Ordinal);

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

        // Returns the resolved spec for the last recognized shadow-* utility in
        // classNames (CSS cascade: later classes win). Returns false when no shadow is
        // wanted — either no shadow class at all, or the winning class is shadow-none.
        public static bool TryExtract(string[] classNames, out ShadowSpec spec)
            => TryExtract(classNames, classNames?.Length ?? 0, out spec);

        // baseCount as StyleRingClass.TryExtract takes it: the box-shadow utilities among the element's own classes
        // are read in the order Tailwind emits them (StyleRingClass.InEmitOrder), so `shadow-xl shadow-none` keeps
        // its shadow.
        public static bool TryExtract(string[] classNames, int baseCount, out ShadowSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }
            classNames = StyleRingClass.InEmitOrder(classNames, baseCount);

            var found = false;
            var foundBoxShadow = false;
            var sizeAlpha = 1f;
            // A box-shadow the browser discards takes every shadow-* with it; a drop-shadow is a filter and stays.
            var discarded = StyleRingClass.DiscardsBoxShadow(classNames);
            foreach (var cls in classNames)
            {
                if (discarded && cls != null && IsBoxShadowClass(cls))
                {
                    continue;
                }
                // A shadow colour is --tw-shadow-color, its own property, so it neither replaces nor cancels the
                // shadow beside it, whichever comes first.
                if (TryParseShadowColor(cls!, out _, out _))
                {
                    continue;
                }
                if (TryParse(cls!, out var parsed, out var wantShadow, out var modifier))
                {
                    // A recognized utility overrides earlier ones; shadow-none flips found back to false.
                    found = wantShadow;
                    if (wantShadow)
                    {
                        spec = parsed;
                        foundBoxShadow = IsBoxShadowClass(cls!);
                        sizeAlpha = modifier ?? 1f;
                    }
                }
            }
            // A box-shadow size writes its colour as var(--tw-shadow-color, <its own>), so a shadow colour
            // replaces the size's colour, the alpha included. A drop-shadow reads its own colour property.
            if (found && foundBoxShadow
                && TryResolveShadowColor(classNames, discarded, sizeAlpha, out var source, out var shadowColor))
            {
                spec = spec.WithColor(source, shadowColor);
            }
            return found;
        }

        // The --tw-shadow-color the shadow colour classes write, scaled by --tw-shadow-alpha, the size's opacity
        // modifier (sizeAlpha), as Tailwind's color-mix(in oklab, <colour> var(--tw-shadow-alpha), transparent)
        // does. classNames is in the order InEmitOrder gives; the last colour class wins.
        private static bool TryResolveShadowColor(string[] classNames, bool discarded, float sizeAlpha,
            out ShadowColorSource source, out Color color)
        {
            source = ShadowColorSource.Explicit;
            color = default;
            var set = false;
            foreach (var cls in classNames)
            {
                if (cls == null || (discarded && IsBoxShadowClass(cls)) || !TryParseShadowColor(cls, out var kind, out var parsed))
                {
                    continue;
                }
                set = true;
                source = kind;
                color = parsed;
            }
            if (set && source != ShadowColorSource.Inherit)
            {
                color.a *= sizeAlpha;
            }
            return set;
        }

        // Records element's own --tw-shadow-color for a descendant's shadow-inherit (ShadowColorInheritance).
        internal static void RecordOwnColor(VisualElement element, string[] classNames, int baseCount)
        {
            classNames = StyleRingClass.InEmitOrder(classNames, baseCount);
            var discarded = StyleRingClass.DiscardsBoxShadow(classNames);
            var sizeAlpha = 1f;
            foreach (var cls in classNames)
            {
                if (cls != null && !TryParseShadowColor(cls, out _, out _) && IsBoxShadowClass(cls)
                    && TryParse(cls, out _, out var want, out var modifier) && want)
                {
                    sizeAlpha = modifier ?? 1f;
                }
            }
            var set = TryResolveShadowColor(classNames, discarded, sizeAlpha, out var source, out var color);
            ShadowColorInheritance.Record(element, inset: false, set, source, color);
        }

        internal static void ForgetOwnColor(VisualElement element) => ShadowColorInheritance.Forget(element, inset: false);

        // shadow-<colour> and shadow-[<colour>], Tailwind's --tw-shadow-color branch, with an optional opacity
        // modifier. A bracket is a colour where its hint is `color`, or where it has none and holds a colour.
        // shadow-current is the text colour, its modifier kept as the factor in color.a; shadow-inherit takes the
        // ancestor's (TryInheritedColor).
        internal static bool TryParseShadowColor(string cls, out ShadowColorSource kind, out Color color)
            => TryParseShadowColor(cls, "shadow-", out kind, out color);

        // The same colour branch under another prefix: inset-shadow- writes --tw-inset-shadow-color the same way.
        internal static bool TryParseShadowColor(string cls, string prefix, out ShadowColorSource kind, out Color color)
        {
            color = default;
            kind = ShadowColorSource.Explicit;
            var start = prefix.Length;
            if (string.IsNullOrEmpty(cls) || !cls.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            var rest = cls.AsSpan(start);
            if (rest.SequenceEqual("inherit".AsSpan()))
            {
                kind = ShadowColorSource.Inherit;
                return true;
            }
            if (rest.StartsWith("current".AsSpan(), StringComparison.Ordinal))
            {
                var end = start + "current".Length;
                kind = ShadowColorSource.Current;
                color = Color.white;
                return cls.Length == end
                    || (cls[end] == '/' && StyleColorValueParser.TryScaleAlpha(cls.AsSpan(end + 1), ref color));
            }
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, start, out var inner))
            {
                return StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner)
                    && (hint == null || hint == "color")
                    && StyleColorValueParser.TryParseColor(inner, out color);
            }
            return StyleColorValueParser.TryParseColorValue(cls, start, out color);
        }

        // A size's opacity modifier: the part after the '/' outside any bracket, which Tailwind writes as the alpha
        // of the size's own colour and as --tw-shadow-alpha. size is cls without it.
        internal static bool TrySplitSizeModifier(string cls, out string size, out float? alpha)
        {
            size = cls;
            alpha = null;
            var slash = StyleColorValueParser.ColorModifierSlashIndex(cls, 0);
            if (slash < 0)
            {
                return true;
            }
            size = cls.Substring(0, slash);
            if (!StyleColorValueParser.TryParseAlphaModifier(cls.AsSpan(slash + 1), out var parsed))
            {
                return false;
            }
            alpha = parsed;
            return true;
        }

        // modifier: the size's opacity modifier, null where it has none.
        private static bool TryParse(string cls, out ShadowSpec spec, out bool wantShadow, out float? modifier)
        {
            spec = default;
            wantShadow = false;
            modifier = null;
            if (string.IsNullOrEmpty(cls) || !TrySplitSizeModifier(cls, out cls, out modifier)
                || !StyleArbitraryValueResolver.TryDropTypeHint(cls, out cls))
            {
                return false;
            }

            // CSS has two shadow channels (box-shadow / filter: drop-shadow); Velvet renders both
            // through its single shadow element, so the two families share ONE cascade slot — the
            // last recognized utility of EITHER family wins (each family's -none included).
            string suffix;
            var table = Presets;
            if (cls == "shadow")
            {
                suffix = "";
            }
            else if (cls.StartsWith("shadow-", StringComparison.Ordinal))
            {
                suffix = cls.Substring("shadow-".Length);
            }
            else if (cls == "drop-shadow")
            {
                suffix = "";
                table = DropPresets;
            }
            else if (cls.StartsWith("drop-shadow-", StringComparison.Ordinal))
            {
                suffix = cls.Substring("drop-shadow-".Length);
                table = DropPresets;
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
            if (table.TryGetValue(suffix, out spec))
            {
                // A preset's colour is written oklab(from <colour> l a b / <modifier>): the modifier replaces its alpha.
                if (modifier is { } presetAlpha)
                {
                    var tint = spec.Color;
                    tint.a = presetAlpha;
                    spec = new ShadowSpec(tint, spec.Blur, spec.OffsetY, spec.Spread, spec.OffsetX);
                }
                wantShadow = true;
                return true;
            }
            // Arbitrary value: shadow-[x_y_blur_spread_#color] (underscores are spaces). Sits AFTER the
            // preset lookup so a named preset never reaches it; both families share this one parse.
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                && TryParseArbitrary(inner.ToString(), modifier, out spec))
            {
                wantShadow = true;
                return true;
            }
            // Unrecognized shadow-* utility: not a known value.
            return false;
        }

        // Whether cls is a box-shadow utility Tailwind generates: shadow, a preset, shadow-none or a bracket that is
        // not a shadow colour.
        internal static bool DeclaresBoxShadow(string cls)
        {
            if (!IsBoxShadowClass(cls) || TryParseShadowColor(cls, out _, out _)
                || !TrySplitSizeModifier(cls, out var size, out _))
            {
                return false;
            }
            return size == "shadow" || size == "shadow-none" || Presets.ContainsKey(size.Substring("shadow-".Length))
                || size["shadow-".Length] == '[';
        }

        // Whether cls is a shadow-[…] Tailwind writes as a box-shadow value that the browser rejects because it
        // holds a bare non-zero number (`shadow-[0_4_8_#000]`).
        internal static bool HoldsRejectedValue(string cls)
        {
            if (!TrySplitSizeModifier(cls, out cls, out _)
                || !cls.StartsWith("shadow-[", StringComparison.Ordinal)
                || !StyleArbitraryValueResolver.TryStripBrackets(cls, "shadow-".Length, out var inner)
                || !StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner)
                || hint == "color" || (hint == null && StyleColorValueParser.TryParseColor(inner, out _)))
            {
                return false;
            }
            foreach (var token in StyleGradientClass.SplitTopLevel(inner.ToString().Replace('_', ' '), ' '))
            {
                if (float.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)
                    && n != 0f)
                {
                    return true;
                }
            }
            return false;
        }

        // Parses the inner body of a shadow-[…] arbitrary value. Tokens are whitespace-separated (the
        // className uses '_' for spaces); each is classified as a length (CSS box-shadow offset/blur/spread,
        // assigned positionally as x, y, blur, spread) or a color (the shared StyleColorValueParser grammar). At least the x+y offsets are required (matching CSS); blur/spread
        // default to 0 and the color defaults to the DEFAULT preset tint. Percent lengths are rejected (the SDF
        // bake is pixel-based).
        // alpha: the class's opacity modifier, which replaces a written colour's alpha and scales the text colour's.
        internal static bool TryParseArbitrary(string body, float? alpha, out ShadowSpec spec)
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
            var color = default(Color);
            var hasColor = false;

            foreach (var token in tokens)
            {
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

            if (!hasColor)
            {
                // var(--tw-shadow-color, currentcolor), the modifier mixing it with transparent.
                spec = new ShadowSpec(new Color(1f, 1f, 1f, alpha ?? 1f), lengths[2], lengths[1], lengths[3], lengths[0],
                    currentColor: true);
                return true;
            }
            if (alpha is { } written)
            {
                color.a = written;
            }
            // x, y, blur, spread — collapse onto the single-softness SDF shadow.
            spec = new ShadowSpec(color, lengths[2], lengths[1], lengths[3], lengths[0]);
            return true;
        }

        // Resolves the top-left corner radius (px) from the rounded-* / rounded-t-* /
        // rounded-l-* / rounded-tl-* classes that affect the top-left corner, mirroring the
        // --radius-* token scale. The shader has a single uniform corner radius, so the top-left
        // corner is taken as representative. Of the matching classes the one Tailwind emits last wins, as
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
