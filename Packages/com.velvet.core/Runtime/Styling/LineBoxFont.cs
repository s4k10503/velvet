#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet
{
    // A copy of a font asset whose ascent and descent are spread evenly about the font's own until they are one line
    // height apart, for text whose line height cannot be written as a <line-height> tag: a multiline field's input
    // text and a label or button with rich text off, none of which parses tags. Both text generators then lay each line
    // out one line height tall and one line height below the last (LineBoxFontTests pins both).
    //
    // A copy of a dynamic asset is created from the same source font with an atlas of its own: one atlas filled by
    // two assets would be packed twice over. A static asset adds no glyph, so its copy shares its atlas. Copies are
    // kept for the domain's lifetime, one per font and line height.
    internal static class LineBoxFont
    {
        private static readonly Dictionary<(FontAsset Source, int Units), FontAsset?> s_variants = new();

        // Keyed in 1/256 font units, a step under the line height, since a height a hair over a device pixel is laid
        // out a whole device pixel taller (LineBoxFontTests' Standard cases read 24.5 for a 24px line otherwise).
        private const float UnitsPerKey = 256f;

        static LineBoxFont()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
#endif
        }

        // source with its lines lineHeight pixels apart at fontSize, or null where no copy can be made.
        public static FontAsset? For(FontAsset source, float lineHeight, float fontSize)
        {
            var face = source.faceInfo;
            if (!(fontSize > 0f) || !(lineHeight >= 0f) || !(face.pointSize > 0f) || !(face.scale > 0f))
            {
                return null;
            }
            var units = lineHeight / (fontSize / face.pointSize * face.scale);
            return Cached(source, Mathf.FloorToInt(units * UnitsPerKey) - 1);
        }

        // A copy a face of the weight table being copied names again is that face itself, so the copying ends.
        private static FontAsset? Cached(FontAsset source, int key)
        {
            if (s_variants.TryGetValue((source, key), out var variant) && (variant is null || variant != null))
            {
                return variant;
            }
            s_variants[(source, key)] = null;
            variant = Create(source, key / UnitsPerKey);
            s_variants[(source, key)] = variant;
            return variant;
        }

        private static FontAsset? Create(FontAsset source, float units)
        {
            var copy = Copy(source);
            if (copy == null)
            {
                return null;
            }
            copy.hideFlags = HideFlags.DontSave;
            var face = copy.faceInfo;
            var spread = (units - (face.ascentLine - face.descentLine)) * 0.5f;
            face.ascentLine += spread;
            face.descentLine -= spread;
            face.lineHeight = face.ascentLine - face.descentLine;
            copy.faceInfo = face;
            copy.fallbackFontAssetTable = source.fallbackFontAssetTable != null
                ? new List<FontAsset>(source.fallbackFontAssetTable)
                : new List<FontAsset>();
            var weights = source.fontWeightTable;
            var copied = copy.fontWeightTable;
            for (var i = 0; weights != null && copied != null && i < weights.Length && i < copied.Length; i++)
            {
                copied[i].regularTypeface = weights[i].regularTypeface == source ? copy : WeightOf(weights[i].regularTypeface, source, units);
                copied[i].italicTypeface = weights[i].italicTypeface == source ? copy : WeightOf(weights[i].italicTypeface, source, units);
            }
            copy.ReadFontAssetDefinition();
            return copy;
        }

        // A face of the weight table in the same line height, which is taken in the source's units.
        private static FontAsset? WeightOf(FontAsset? face, FontAsset source, float units)
        {
            if (face == null)
            {
                return null;
            }
            var ratio = face.faceInfo.pointSize * source.faceInfo.scale / (source.faceInfo.pointSize * face.faceInfo.scale);
            return Cached(face, Mathf.FloorToInt(units * ratio * UnitsPerKey)) ?? face;
        }

        private static FontAsset? Copy(FontAsset source)
        {
            switch (source.atlasPopulationMode)
            {
                case AtlasPopulationMode.Static:
                    return UnityEngine.Object.Instantiate(source);
                case AtlasPopulationMode.DynamicOS:
                    return FontAsset.CreateFontAsset(source.faceInfo.familyName, source.faceInfo.styleName,
                        (int)source.faceInfo.pointSize);
                default:
                    return source.sourceFontFile == null
                        ? null
                        : FontAsset.CreateFontAsset(source.sourceFontFile, (int)source.faceInfo.pointSize,
                            source.atlasPadding, source.atlasRenderMode, source.atlasWidth, source.atlasHeight);
            }
        }

        private static void DestroyAll()
        {
            foreach (var pair in s_variants)
            {
                var variant = pair.Value;
                if (variant == null)
                {
                    continue;
                }
                if (pair.Key.Source.atlasPopulationMode == AtlasPopulationMode.Static)
                {
                    // The atlas and material are the source's, which destroying the copy would destroy with it.
                    variant.atlasTextures = Array.Empty<Texture2D>();
                    variant.material = null;
                }
                UnityEngine.Object.DestroyImmediate(variant);
            }
            s_variants.Clear();
        }
    }

    // An element's inline font written as LineBoxFont's copy of the font it would otherwise take, and given back. The
    // font it would take is its inline one where somebody else wrote it, and else its parent's, which it inherits;
    // an inline legacy Font is not a font asset and is left alone.
    internal sealed class LineBoxFontOwner
    {
        private FontAsset? _written;
        private StyleFontDefinition _underlying;

        public void Settle(VisualElement element, float lineHeight)
        {
            var inline = element.style.unityFontDefinition;
            if (_written == null || inline.value.fontAsset != _written)
            {
                _written = null;
                _underlying = inline;
            }
            var source = _underlying.keyword == StyleKeyword.Undefined
                ? _underlying.value.fontAsset
                : element.hierarchy.parent is { } parent ? StyleFontUnits.AssetOf(parent) : null;
            var variant = source != null && !float.IsNaN(lineHeight)
                ? LineBoxFont.For(source, lineHeight, element.resolvedStyle.fontSize)
                : null;
            if (variant == null)
            {
                Release(element);
                return;
            }
            if (variant == _written)
            {
                return;
            }
            _written = variant;
            element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(variant));
        }

        public void Release(VisualElement element)
        {
            if (_written == null)
            {
                return;
            }
            if (element.style.unityFontDefinition.value.fontAsset == _written)
            {
                element.style.unityFontDefinition = _underlying;
            }
            _written = null;
            _underlying = default;
        }
    }
}
