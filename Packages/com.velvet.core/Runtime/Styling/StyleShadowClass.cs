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
        public readonly Color Color;
        public readonly float Blur;
        public readonly float OffsetY;
        public readonly float Spread;
        // Horizontal offset (px). The presets are centered (0); only an arbitrary shadow-[x y …] sets it.
        public readonly float OffsetX;

        public ShadowSpec(Color color, float blur, float offsetY, float spread)
            : this(color, blur, offsetY, spread, 0f)
        {
        }

        public ShadowSpec(Color color, float blur, float offsetY, float spread, float offsetX)
        {
            Color = color;
            Blur = blur;
            OffsetY = offsetY;
            Spread = spread;
            OffsetX = offsetX;
        }
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
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (cls == "shadow" || cls.StartsWith("shadow-", StringComparison.Ordinal)
                    || cls == "drop-shadow" || cls.StartsWith("drop-shadow-", StringComparison.Ordinal))
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
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var parsed, out var wantShadow))
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

        private static bool TryParse(string cls, out ShadowSpec spec, out bool wantShadow)
        {
            spec = default;
            wantShadow = false;
            if (string.IsNullOrEmpty(cls))
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
                wantShadow = true;
                return true;
            }
            // Arbitrary value: shadow-[x_y_blur_spread_#color] (underscores are spaces). Sits AFTER the
            // preset lookup so a named preset never reaches it; both families share this one parse.
            if (suffix.Length >= 2 && suffix[0] == '[' && suffix[suffix.Length - 1] == ']'
                && TryParseArbitrary(suffix.Substring(1, suffix.Length - 2), out spec))
            {
                wantShadow = true;
                return true;
            }
            // Unrecognized shadow-* utility: not a known value.
            return false;
        }

        // Parses the inner body of a shadow-[…] arbitrary value. Tokens are whitespace-separated (the
        // className uses '_' for spaces); each is classified as a length (CSS box-shadow offset/blur/spread,
        // assigned positionally as x, y, blur, spread) or a color (#hex / named / rgb() / rgba(), via the shared
        // StyleArbitraryValueResolver grammar). At least the x+y offsets are required (matching CSS); blur/spread
        // default to 0 and the color defaults to the DEFAULT preset tint. Percent lengths are rejected (the SDF
        // bake is pixel-based).
        private static bool TryParseArbitrary(string body, out ShadowSpec spec)
        {
            spec = default;
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            var tokens = body.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var lengths = new float[4]; // x, y, blur, spread
            var lengthCount = 0;
            var color = ShadowColor(0.20f); // the bare-shadow DEFAULT tint
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

            // x, y, blur, spread — collapse onto the single-softness SDF shadow.
            spec = new ShadowSpec(color, lengths[2], lengths[1], lengths[3], lengths[0]);
            return true;
        }

        // Resolves the top-left corner radius (px) from the rounded-* / rounded-t-* /
        // rounded-l-* / rounded-tl-* classes that affect the top-left corner, mirroring the
        // --radius-* token scale. The shader has a single uniform corner radius, so the top-left
        // corner is taken as representative. Last matching class wins. Returns false for
        // rounded-full / arbitrary radii (those resolve via resolvedStyle.borderTopLeftRadius
        // once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[] classNames, out float radius)
        {
            radius = 0f;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                string suffix;
                // Longest prefix first so rounded-tl-/rounded-t-/rounded-l- are not swallowed by rounded-.
                if (cls.StartsWith("rounded-tl-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-tl-".Length);
                }
                else if (cls.StartsWith("rounded-t-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-t-".Length);
                }
                else if (cls.StartsWith("rounded-l-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-l-".Length);
                }
                else if (cls.StartsWith("rounded-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-".Length);
                }
                else if (cls == "rounded")
                {
                    suffix = ""; // bare `rounded` == --radius-default
                }
                else
                {
                    continue;
                }

                if (RadiusScale.TryGetValue(suffix, out var r))
                {
                    radius = r;
                    found = true;
                }
            }
            return found;
        }
    }
}
