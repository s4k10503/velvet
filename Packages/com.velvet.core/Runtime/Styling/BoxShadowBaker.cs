using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // The caster box a shadow layer is baked against: its border-box size, its corner radii (top-left, top-right,
    // bottom-right, bottom-left), horizontal in Radii and vertical in RadiiY, and its border widths (left, top, right,
    // bottom), which place the padding box an inset layer draws inside.
    internal readonly struct ShadowBox
    {
        public readonly float Width;
        public readonly float Height;
        public readonly Vector4 Radii;
        public readonly Vector4 RadiiY;
        public readonly Vector4 Borders;

        // Each corner as round on both axes.
        public ShadowBox(float width, float height, Vector4 radii, Vector4 borders)
            : this(width, height, radii, radii, borders)
        {
        }

        public ShadowBox(float width, float height, Vector4 radii, Vector4 radiiY, Vector4 borders)
        {
            Width = width;
            Height = height;
            Radii = radii;
            RadiiY = radiiY;
            Borders = borders;
        }

        public Vector2 Center => new(Width * 0.5f, Height * 0.5f);
    }

    // Bakes the Velvet/BoxShadow shader into a full-size texture per box-shadow layer and caches the result.
    // UI Toolkit (6000.3) has no native box-shadow, so a shadow-* utility paints a baked
    // shadow texture behind its caster (BoxShadowSilhouette draws each layer as a single quad in the caster's
    // own generateVisualContent). The shader is BAKED once (Graphics.Blit) rather than applied as a live
    // per-element material: UITK freezes a custom-material element's draw-command order at first generation,
    // so a shadow first generated under an animating ancestor transform would composite in FRONT of its
    // caster. The bake produces a WHITE texture with the coverage in alpha — the layer's color is applied
    // as the quad's vertex tint at draw time, so a color change retints without re-baking.
    //
    // The texture covers ShadowRect at one texel per pixel (until QuadSize's clamp), upright OR sheared (skewDeg follows a skewed
    // caster, x and y in degrees); the LRU cache below bounds the per-shape bakes.
    internal static class BoxShadowBaker
    {
        // Margin past three standard deviations, so the quad's edge texels hold no coverage worth drawing.
        internal const float ExtraPadding = 1f;

        private static Shader? s_shader;

        // The single bake Material, lazily created from the shader. It is only a bake tool (the baked textures
        // are cached below), so one instance serves every shadow and is disposed once on reconciler teardown.
        private static Material? s_material;

        // Baked layers shared across all shadows, keyed by (blur/spread/y skew/inset, radii, borders, target
        // size, x skew, offset). A baked shadow cannot be size-independent (a full-size quad is drawn), so a card
        // that animates through many pixel sizes would otherwise leak one full-quad Texture2D per distinct size.
        // The LRU below caps the cache and evicts the least-recently-used bake; a re-needed size re-bakes.
        private static readonly Dictionary<(long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset),
            Texture2D> s_silhouetteCache = new();

        // Most-recently-used at the TAIL of s_silhouetteLru, least at the HEAD; a node-by-key map keeps
        // touch/evict O(1) and keeps the list in lockstep with the cache (no duplicate or orphaned nodes).
        // Each layer of a shadow is its own entry and a preset has up to two, so this doubles the 64 entries
        // that held one entry per shadow.
        private const int MaxSilhouetteCacheEntries = 128;
        private static readonly LinkedList<(long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset)>
            s_silhouetteLru = new();
        private static readonly Dictionary<(long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset),
            LinkedListNode<(long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset)>>
            s_silhouetteLruNodes = new();

        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int ShapeRadiiId = Shader.PropertyToID("_ShapeRadii");
        private static readonly int ClipId = Shader.PropertyToID("_Clip");
        private static readonly int ClipRadiiId = Shader.PropertyToID("_ClipRadii");
        private static readonly int ShapeRadiiYId = Shader.PropertyToID("_ShapeRadiiY");
        private static readonly int ClipRadiiYId = Shader.PropertyToID("_ClipRadiiY");
        private static readonly int ElementSizeId = Shader.PropertyToID("_ElementSize");
        private static readonly int InsetId = Shader.PropertyToID("_Inset");
        private static readonly int SkewXId = Shader.PropertyToID("_SkewX");
        private static readonly int SkewYId = Shader.PropertyToID("_SkewY");

#if UNITY_EDITOR
        // Baked textures (and the bake Material) persist across play-mode cycles without a Domain Reload; drop
        // them so a shader edit or a fresh run re-bakes rather than serving a stale texture (and so they do not
        // accumulate).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            foreach (var tex in s_silhouetteCache.Values)
            {
                if (tex != null) Object.DestroyImmediate(tex);
            }
            s_silhouetteCache.Clear();
            s_silhouetteLru.Clear();
            s_silhouetteLruNodes.Clear();
            if (s_material != null)
            {
                Object.DestroyImmediate(s_material);
                s_material = null;
            }
        }
#endif

        // Returns the cached bake of one layer against a caster box, baking it if absent. Keeps the returned key
        // warm in the LRU (touched on hit, re-baked if it was evicted) so a caster re-fetching its own bake never
        // serves a destroyed texture. NB this only protects the key being fetched: were more than
        // MaxSilhouetteCacheEntries distinct layers painted in a SINGLE frame, an earlier caster's
        // already-handed-off texture could be evicted by a later bake that frame. Returns null (the caller paints
        // nothing) when the shader is unavailable / headless.
        internal static Texture2D? GetOrBakeSilhouette(in ShadowBox box, in ShadowSpec layer, Vector2 skewDeg)
        {
            if (box.Width <= 0f || box.Height <= 0f)
            {
                return null;
            }
            if (!EnsureMaterial(out var mat))
            {
                return null;
            }

            var key = (ShapeKey(layer, skewDeg.y), PackQuad(box.Radii), PackQuad(box.RadiiY),
                PackQuad(layer.Inset ? box.Borders : Vector4.zero),
                Mathf.RoundToInt(box.Width), Mathf.RoundToInt(box.Height),
                Mathf.RoundToInt(skewDeg.x * 100f), OffsetKey(layer.OffsetX, layer.OffsetY));
            if (s_silhouetteCache.TryGetValue(key, out var tex) && tex != null)
            {
                TouchSilhouette(key);
                return tex;
            }

            tex = BakeSilhouetteTexture(mat, box, layer, skewDeg);
            StoreSilhouette(key, tex);
            return tex;
        }

        // The rect one layer's quad covers in the caster's local space. An outer layer covers its shape — the
        // border box moved by the offset and grown by the spread — padded by three standard deviations of the
        // blur (half the blur radius) plus ExtraPadding per side; an inset layer covers the padding box it is
        // drawn inside. Either is grown by the shear's overhang and placed where the shear about the box centre
        // carries it, as CSS transforms a shadow with its element, offset included.
        internal static Rect ShadowRect(in ShadowBox box, in ShadowSpec layer, Vector2 skewDeg)
        {
            var tanX = Mathf.Tan(skewDeg.x * Mathf.Deg2Rad);
            var tanY = Mathf.Tan(skewDeg.y * Mathf.Deg2Rad);
            Rect upright;
            if (layer.Inset)
            {
                upright = PaddingRect(box);
            }
            else
            {
                var spread = QuantizeSpread(layer.Spread);
                var pad = Mathf.Ceil(1.5f * QuantizePx(layer.Blur)) + ExtraPadding;
                var shapeW = Mathf.Max(box.Width + (2f * spread), 0f);
                var shapeH = Mathf.Max(box.Height + (2f * spread), 0f);
                upright = new Rect(((box.Width - shapeW) * 0.5f) - pad + layer.OffsetX,
                    ((box.Height - shapeH) * 0.5f) - pad + layer.OffsetY, shapeW + (2f * pad), shapeH + (2f * pad));
            }
            var quad = SilhouetteBoundsSpacer.ExpandForShear(upright, tanX, tanY);
            var shift = Shear(upright.center - box.Center, tanX, tanY) - (upright.center - box.Center);
            return new Rect(quad.position + shift, quad.size);
        }

        // CSS Backgrounds 3's spread rule for the shadow shape's corner radius: the spread is added to the
        // caster's radius, floored at zero, and where a positive spread exceeds the radius it is first scaled by
        // 1 + (r - 1)^3, r being the radius over the spread, so a square corner stays square. An inset layer's
        // hole takes the negated spread.
        internal static float SpreadRadius(float radius, float spread)
        {
            if (spread > radius && spread > 0f)
            {
                var ratio = (radius / spread) - 1f;
                return radius + (spread * (1f + (ratio * ratio * ratio)));
            }
            return Mathf.Max(radius + spread, 0f);
        }

        // The pixel size the silhouette texture is baked at for a shadow rect of that size, clamped to what a
        // texture holds. The shader spans the whole texture over the rect, so the draw quad is the rect itself
        // and this size sets only the resolution.
        internal static void QuadSize(Vector2 rectSize, out int quadW, out int quadH)
        {
            quadW = Mathf.Clamp(Mathf.CeilToInt(rectSize.x), 8, 2048);
            quadH = Mathf.Clamp(Mathf.CeilToInt(rectSize.y), 8, 2048);
        }

        // The padding box, in the caster's local space.
        internal static Rect PaddingRect(in ShadowBox box)
            => new(box.Borders.x, box.Borders.y,
                Mathf.Max(box.Width - box.Borders.x - box.Borders.z, 0f),
                Mathf.Max(box.Height - box.Borders.y - box.Borders.w, 0f));

        // The padding box's corner radii: each border-box radius less the wider of the two borders meeting there.
        internal static Vector4 PaddingRadii(in ShadowBox box) => PaddingRadii(box.Radii, box.Borders);

        internal static Vector4 PaddingRadiiY(in ShadowBox box) => PaddingRadii(box.RadiiY, box.Borders);

        private static Vector4 PaddingRadii(Vector4 radii, Vector4 borders)
            => new(Mathf.Max(radii.x - Mathf.Max(borders.x, borders.y), 0f),
                Mathf.Max(radii.y - Mathf.Max(borders.z, borders.y), 0f),
                Mathf.Max(radii.z - Mathf.Max(borders.z, borders.w), 0f),
                Mathf.Max(radii.w - Mathf.Max(borders.x, borders.w), 0f));

        // Disposes the bake Material. Invoked once on reconciler teardown — the baked textures are cached
        // process-wide and are NOT touched here (the editor reset above destroys them). Idempotent.
        internal static void DisposeMaterial()
        {
            VelvetObjectUtil.Destroy(s_material);
            s_material = null;
        }

        private static bool EnsureMaterial(out Material material)
        {
            if (s_material != null)
            {
                material = s_material;
                return true;
            }
            if (s_shader == null)
            {
                s_shader = VelvetShaders.Find(VelvetShaders.BoxShadow, "BoxShadow", "shadow");
            }
            if (s_shader == null)
            {
                material = null!;
                return false;
            }
            s_material = new Material(s_shader);
            material = s_material;
            return true;
        }

        // Quantize a radius to whole pixels (bakes are pixel-resolution anyway). The cache key and the baked
        // texture must BOTH derive from these quantized values, or two presets whose raw floats round to the
        // same key would share whichever raw-float texture baked first, giving a subpixel-off shadow.
        internal static float QuantizePx(float v) => Mathf.Clamp(Mathf.RoundToInt(v), 0, 0xFFFF);

        // As QuantizePx for the spread, which may be negative.
        internal static float QuantizeSpread(float v) => Mathf.Clamp(Mathf.RoundToInt(v), short.MinValue, short.MaxValue);

        private static Vector4 Quantize(Vector4 v) => new(QuantizePx(v.x), QuantizePx(v.y), QuantizePx(v.z), QuantizePx(v.w));

        // Skew x then skew y about the origin: Tailwind's skewX() skewY().
        private static Vector2 Shear(Vector2 v, float tanX, float tanY)
            => new(((1f + (tanX * tanY)) * v.x) + (tanX * v.y), (tanY * v.x) + v.y);

        // Bakes one layer at the exact quad pixel size, so the stretched quad is texel-accurate. The shader
        // unshears the sample coordinate, so the boxes evaluate upright while the rendered alpha follows the
        // slant. White RGB; alpha is the coverage.
        private static Texture2D BakeSilhouetteTexture(Material mat, in ShadowBox box, in ShadowSpec layer,
            Vector2 skewDeg)
        {
            var quantized = new ShadowBox(box.Width, box.Height, Quantize(box.Radii), Quantize(box.RadiiY),
                Quantize(box.Borders));
            var spread = QuantizeSpread(layer.Spread);
            var offset = new Vector2(OffsetPx(layer.OffsetX), OffsetPx(layer.OffsetY));
            var rect = ShadowRect(quantized, layer, skewDeg);
            QuadSize(rect.size, out var qw, out var qh);

            // Centres relative to the quad's (the shape's for an outer layer, the padding box's for an inset one),
            // in the unsheared frame the shader evaluates in.
            Vector4 shape, shapeRadii, shapeRadiiY, clip, clipRadii, clipRadiiY;
            if (layer.Inset)
            {
                var padding = PaddingRect(quantized);
                var paddingRadii = PaddingRadii(quantized);
                var paddingRadiiY = PaddingRadiiY(quantized);
                clip = new Vector4(padding.width * 0.5f, padding.height * 0.5f, 0f, 0f);
                clipRadii = paddingRadii;
                clipRadiiY = paddingRadiiY;
                shape = new Vector4(Mathf.Max((padding.width * 0.5f) - spread, 0f),
                    Mathf.Max((padding.height * 0.5f) - spread, 0f), offset.x, offset.y);
                shapeRadii = SpreadRadii(paddingRadii, -spread);
                shapeRadiiY = SpreadRadii(paddingRadiiY, -spread);
            }
            else
            {
                clip = new Vector4(box.Width * 0.5f, box.Height * 0.5f, -offset.x, -offset.y);
                clipRadii = quantized.Radii;
                clipRadiiY = quantized.RadiiY;
                shape = new Vector4(Mathf.Max((box.Width * 0.5f) + spread, 0f),
                    Mathf.Max((box.Height * 0.5f) + spread, 0f), 0f, 0f);
                shapeRadii = SpreadRadii(quantized.Radii, spread);
                shapeRadiiY = SpreadRadii(quantized.RadiiY, spread);
            }

            mat.SetFloat(BlurRadiusId, QuantizePx(layer.Blur));
            mat.SetVector(ShapeId, shape);
            mat.SetVector(ShapeRadiiId, shapeRadii);
            mat.SetVector(ShapeRadiiYId, shapeRadiiY);
            mat.SetVector(ClipId, clip);
            mat.SetVector(ClipRadiiId, clipRadii);
            mat.SetVector(ClipRadiiYId, clipRadiiY);
            mat.SetVector(ElementSizeId, new Vector4(rect.width, rect.height, 0f, 0f));
            mat.SetFloat(InsetId, layer.Inset ? 1f : 0f);
            mat.SetFloat(SkewXId, Mathf.Tan(skewDeg.x * Mathf.Deg2Rad));
            mat.SetFloat(SkewYId, Mathf.Tan(skewDeg.y * Mathf.Deg2Rad));

            var tex = SilhouetteBakeReadback.Bake(mat, qw, qh, new SilhouetteBakeSettings
            {
                RtFormat = RenderTextureFormat.ARGB32,
                RtReadWrite = RenderTextureReadWrite.Default,
                TextureFormat = TextureFormat.RGBA32,
                Linear = false,
                Name = "VelvetBoxShadowSilhouette",
                WrapMode = TextureWrapMode.Clamp,
                FilterMode = FilterMode.Bilinear,
                HideFlags = HideFlags.None,
            });
            mat.SetFloat(SkewXId, 0f); // defensive: never leave a stale shear on the shared Material
            mat.SetFloat(SkewYId, 0f);
            return tex;
        }

        private static Vector4 SpreadRadii(Vector4 radii, float spread)
            => new(SpreadRadius(radii.x, spread), SpreadRadius(radii.y, spread),
                SpreadRadius(radii.z, spread), SpreadRadius(radii.w, spread));

        // Inserts a freshly baked silhouette and evicts the least-recently-used bake (destroying its texture)
        // when the cache would exceed its cap. A null bake (shader unresolved) is not cached so it re-attempts.
        // Re-storing an already-present key replaces its texture and just refreshes recency (no duplicate node).
        private static void StoreSilhouette(
            (long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset) key, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            if (s_silhouetteCache.TryGetValue(key, out var existing))
            {
                if (existing != null && existing != tex)
                {
                    VelvetObjectUtil.Destroy(existing);
                }
                s_silhouetteCache[key] = tex;
                TouchSilhouette(key);
                return;
            }
            s_silhouetteCache[key] = tex;
            s_silhouetteLruNodes[key] = s_silhouetteLru.AddLast(key);
            while (s_silhouetteCache.Count > MaxSilhouetteCacheEntries && s_silhouetteLru.First != null)
            {
                var oldest = s_silhouetteLru.First.Value;
                s_silhouetteLru.RemoveFirst();
                s_silhouetteLruNodes.Remove(oldest);
                if (s_silhouetteCache.TryGetValue(oldest, out var evicted))
                {
                    s_silhouetteCache.Remove(oldest);
                    if (evicted != null)
                    {
                        VelvetObjectUtil.Destroy(evicted);
                    }
                }
            }
        }

        // Marks an existing key most-recently-used (moves its node to the tail) on a cache hit — O(1) via the
        // node map, so a frequently re-hit silhouette pays no linear scan.
        private static void TouchSilhouette(
            (long shape, long radii, long radiiY, long borders, int w, int h, int skewCenti, long offset) key)
        {
            if (s_silhouetteLruNodes.TryGetValue(key, out var node))
            {
                s_silhouetteLru.Remove(node);
                s_silhouetteLru.AddLast(node);
            }
        }

        // Pack (blur, spread, inset) into one key, with the y skew in hundredths of a degree in the top 16 bits.
        // Uses the SAME quantizers as the bake so the key never disagrees with the baked texture.
        private static long ShapeKey(in ShadowSpec layer, float skewYDeg)
        {
            long b = (long)QuantizePx(layer.Blur);
            long s = (ushort)(short)QuantizeSpread(layer.Spread);
            long i = layer.Inset ? 1L : 0L;
            long y = (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(skewYDeg * 100f), short.MinValue, short.MaxValue);
            return (y << 48) | (i << 32) | (b << 16) | s;
        }

        // Four whole-pixel quantities in 16 bits each.
        private static long PackQuad(Vector4 v)
            => ((long)QuantizePx(v.x) << 48) | ((long)QuantizePx(v.y) << 32) | ((long)QuantizePx(v.z) << 16)
               | (long)QuantizePx(v.w);

        // The offset in hundredths of a pixel, as the bake reads it.
        private static float OffsetPx(float v) => Mathf.RoundToInt(v * 100f) / 100f;

        private static long OffsetKey(float x, float y)
            => ((long)Mathf.RoundToInt(x * 100f) << 32) | (uint)Mathf.RoundToInt(y * 100f);
    }
}
