using System;
using System.Runtime.CompilerServices;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // The font-relative units on an element, measured from the font asset UI Toolkit draws its text with (the
    // asset's face metrics and glyph advances, scaled to a font size as UI Toolkit's text generator scales them).
    internal static class StyleFontUnits
    {
        private const char Zero = '0';
        private const char Water = '水';

        private static readonly Func<VisualElement, FontAsset?>? s_assetOf = BindAssetOf();

        // Each asset's metrics as fractions of its em, read once per asset: a glyph lookup may add the glyph to a
        // dynamic asset, which is not something to repeat on every poll tick.
        private static readonly ConditionalWeakTable<FontAsset, Ratios> s_ratios = new();

        private static readonly ConditionalWeakTable<FontAsset, Ratios>.CreateValueCallback s_measure = Measure;

        // The units on element at fontSize, whose line height is lineHeight, or the font's own where that is NaN.
        // Without a font asset, or on no panel, each unit takes FontUnitPixels.WithoutMetrics' value.
        public static FontUnitPixels Of(VisualElement element, float fontSize, float lineHeight)
        {
            var asset = element.panel != null ? s_assetOf?.Invoke(element) : null;
            var ratios = asset != null ? s_ratios.GetValue(asset, s_measure) : null;
            var fallback = FontUnitPixels.WithoutMetrics(fontSize);
            return new FontUnitPixels(fontSize,
                Scale(ratios?.Ex, fontSize, fallback.Ex),
                Scale(ratios?.Ch, fontSize, fallback.Ch),
                Scale(ratios?.Cap, fontSize, fallback.Cap),
                Scale(ratios?.Ic, fontSize, fallback.Ic),
                float.IsNaN(lineHeight) ? Scale(ratios?.Lh, fontSize, fallback.Lh) : lineHeight);
        }

        // The font asset element's text is drawn in, which the units are read from; null off a panel or without one.
        public static FontAsset? AssetOf(VisualElement element) => element.panel != null ? s_assetOf?.Invoke(element) : null;

        private static float Scale(float? ratio, float fontSize, float fallback)
            => ratio is { } r && !float.IsNaN(r) ? r * fontSize : fallback;

        private static Func<VisualElement, FontAsset?>? BindAssetOf()
            => EngineMember.ElementFontAsset.ResolveMethod() is { } method
                ? (Func<VisualElement, FontAsset?>)Delegate.CreateDelegate(typeof(Func<VisualElement, FontAsset?>), method)
                : null;

        // A metric the font does not carry is NaN, for Of to replace with CSS's stand-in. The cap height's stand-in
        // is the ascent, as CSS gives it, before the em.
        private static Ratios Measure(FontAsset asset)
        {
            var face = asset.faceInfo;
            var perEm = face.pointSize > 0f ? face.scale / face.pointSize : float.NaN;
            return new Ratios
            {
                Ex = Positive(face.meanLine * perEm),
                Cap = Positive(face.capLine * perEm) is var cap && !float.IsNaN(cap) ? cap : Positive(face.ascentLine * perEm),
                Lh = Positive(face.lineHeight * perEm),
                Ch = Advance(asset, Zero),
                Ic = Advance(asset, Water),
            };
        }

        // The advance of c in the asset's em, from the asset or the first of its fallbacks holding it.
        private static float Advance(FontAsset asset, char c)
        {
            if (AdvanceIn(asset, c) is { } own)
            {
                return own;
            }
            foreach (var fallback in asset.fallbackFontAssetTable ?? new System.Collections.Generic.List<FontAsset>())
            {
                if (fallback != null && AdvanceIn(fallback, c) is { } found)
                {
                    return found;
                }
            }
            return float.NaN;
        }

        private static float? AdvanceIn(FontAsset asset, char c)
        {
            var face = asset.faceInfo;
            if (face.pointSize <= 0f || !asset.HasCharacter(c, searchFallbacks: false, tryAddCharacter: true)
                || !asset.characterLookupTable.TryGetValue(c, out var character) || character.glyph == null)
            {
                return null;
            }
            return character.glyph.metrics.horizontalAdvance * character.scale * character.glyph.scale * face.scale
                / face.pointSize;
        }

        private static float Positive(float value) => value > 0f ? value : float.NaN;

        private sealed class Ratios
        {
            public float Ex;
            public float Ch;
            public float Cap;
            public float Ic;
            public float Lh;
        }
    }
}
