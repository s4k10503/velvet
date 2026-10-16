using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Bakes the Velvet/BoxShadow shader into a full-size texture per box-shadow layer and caches the result.
    // UI Toolkit (6000.3) has no native box-shadow, so a shadow-* utility paints a baked
    // shadow texture behind its caster (BoxShadowSilhouette draws each layer as a single quad in the caster's
    // own generateVisualContent). The shader is BAKED once (Graphics.Blit) rather than applied as a live
    // per-element material: UITK freezes a custom-material element's draw-command order at first generation,
    // so a shadow first generated under an animating ancestor transform would composite in FRONT of its
    // caster. The bake produces a WHITE texture with the coverage in alpha — the layer's color is applied
    // as the quad's vertex tint at draw time, so a color change retints without re-baking.
    //
    // The texture covers ShadowRect at one texel per pixel, upright OR sheared (skewDeg follows a skewed
    // caster, x and y in degrees); the LRU cache below bounds the per-shape bakes.
    internal static class BoxShadowBaker
    {
        // Margin past three standard deviations, so the quad's edge texels hold no coverage worth drawing.
        internal const float ExtraPadding = 1f;

        private static Shader? s_shader;

        // The single bake Material, lazily created from the shader. It is only a bake tool (the baked textures
        // are cached below), so one instance serves every shadow and is disposed once on reconciler teardown.
        private static Material? s_material;

        // Baked layers shared across all shadows, keyed by (corner/blur/spread/y skew, target size, x skew,
        // offset). A baked shadow cannot be size-independent (a full-size quad is drawn), so a card that animates
        // through many pixel sizes would otherwise leak one full-quad Texture2D per distinct size. The LRU
        // below caps the cache and evicts the least-recently-used bake; a re-needed size re-bakes.
        private static readonly Dictionary<(long shape, int w, int h, int skewCenti, long offset), Texture2D>
            s_silhouetteCache = new();

        // Most-recently-used at the TAIL of s_silhouetteLru, least at the HEAD; a node-by-key map keeps
        // touch/evict O(1) and keeps the list in lockstep with the cache (no duplicate or orphaned nodes).
        private const int MaxSilhouetteCacheEntries = 64;
        private static readonly LinkedList<(long shape, int w, int h, int skewCenti, long offset)> s_silhouetteLru = new();
        private static readonly Dictionary<(long shape, int w, int h, int skewCenti, long offset),
            LinkedListNode<(long shape, int w, int h, int skewCenti, long offset)>> s_silhouetteLruNodes = new();

        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");
        private static readonly int ShapeRadiusId = Shader.PropertyToID("_ShapeRadius");
        private static readonly int SpreadId = Shader.PropertyToID("_Spread");
        private static readonly int OffsetId = Shader.PropertyToID("_Offset");
        private static readonly int ElementSizeId = Shader.PropertyToID("_ElementSize");
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

        // Returns the cached bake of one layer for a caster of (targetWidth, targetHeight) with the given corner
        // radius and skew, baking it if absent. Keeps the returned key warm in the LRU (touched on hit, re-baked
        // if it was evicted) so a caster re-fetching its own bake never serves a destroyed texture. NB this only
        // protects the key being fetched: were more than MaxSilhouetteCacheEntries distinct layers painted in a
        // SINGLE frame, an earlier caster's already-handed-off texture could be evicted by a later bake that
        // frame. Returns null (the caller paints nothing) when the shader is unavailable / headless.
        internal static Texture2D? GetOrBakeSilhouette(float corner, in ShadowSpec layer,
            float targetWidth, float targetHeight, Vector2 skewDeg)
        {
            if (targetWidth <= 0f || targetHeight <= 0f)
            {
                return null;
            }
            if (!EnsureMaterial(out var mat))
            {
                return null;
            }

            var key = (CacheKey(corner, layer.Blur, layer.Spread, skewDeg.y),
                Mathf.RoundToInt(targetWidth), Mathf.RoundToInt(targetHeight),
                Mathf.RoundToInt(skewDeg.x * 100f), OffsetKey(layer.OffsetX, layer.OffsetY));
            if (s_silhouetteCache.TryGetValue(key, out var tex) && tex != null)
            {
                TouchSilhouette(key);
                return tex;
            }

            tex = BakeSilhouetteTexture(mat, corner, layer, new Vector2(targetWidth, targetHeight), skewDeg);
            StoreSilhouette(key, tex);
            return tex;
        }

        // The rect one layer's quad covers in the caster's local space: the box grown by the spread, padded by
        // three standard deviations of the blur (half the blur radius) plus ExtraPadding per side, grown by the
        // shear's overhang about the box centre, and moved by the offset carried through the same shear, as CSS
        // transforms a shadow with its element, offset included. Upright, it is that padded shape moved by the
        // offset.
        internal static Rect ShadowRect(float targetWidth, float targetHeight, in ShadowSpec layer, Vector2 skewDeg)
        {
            var spread = QuantizeSpread(layer.Spread);
            var pad = Mathf.Ceil(1.5f * QuantizePx(layer.Blur)) + ExtraPadding;
            var shapeW = Mathf.Max(targetWidth + (2f * spread), 0f);
            var shapeH = Mathf.Max(targetHeight + (2f * spread), 0f);
            var tanX = Mathf.Tan(skewDeg.x * Mathf.Deg2Rad);
            var tanY = Mathf.Tan(skewDeg.y * Mathf.Deg2Rad);
            var quad = SilhouetteBoundsSpacer.ExpandForShear(
                new Rect(((targetWidth - shapeW) * 0.5f) - pad, ((targetHeight - shapeH) * 0.5f) - pad,
                    shapeW + (2f * pad), shapeH + (2f * pad)), tanX, tanY);
            var sheared = new Vector2(((1f + (tanX * tanY)) * layer.OffsetX) + (tanX * layer.OffsetY),
                (tanY * layer.OffsetX) + layer.OffsetY);
            return new Rect(quad.position + sheared, quad.size);
        }

        // CSS Backgrounds 3's spread rule for the shadow shape's corner radius: the spread is added to the
        // caster's radius, floored at zero, and where a positive spread exceeds the radius it is first scaled by
        // 1 + (r - 1)^3, r being the radius over the spread, so a square corner stays square.
        internal static float SpreadRadius(float radius, float spread)
        {
            if (spread > radius && spread > 0f)
            {
                var ratio = (radius / spread) - 1f;
                return radius + (spread * (1f + (ratio * ratio * ratio)));
            }
            return Mathf.Max(radius + spread, 0f);
        }

        // The pixel size the silhouette is baked at for a shadow rect of that size. The paint binding sizes its
        // draw quad to this so one texel maps to one pixel.
        internal static void QuadSize(Vector2 rectSize, out int quadW, out int quadH)
        {
            quadW = Mathf.Clamp(Mathf.CeilToInt(rectSize.x), 8, 2048);
            quadH = Mathf.Clamp(Mathf.CeilToInt(rectSize.y), 8, 2048);
        }

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

        // Bakes one layer at the exact quad pixel size, so the stretched quad is texel-accurate. The shader
        // unshears the sample coordinate, so the shape evaluates upright while the rendered alpha follows the
        // slant. White RGB; alpha is the coverage.
        private static Texture2D BakeSilhouetteTexture(Material mat, float corner, in ShadowSpec layer,
            Vector2 target, Vector2 skewDeg)
        {
            var radius = QuantizePx(corner);
            var spread = QuantizeSpread(layer.Spread);
            var rect = ShadowRect(target.x, target.y, layer, skewDeg);
            QuadSize(rect.size, out var qw, out var qh);

            mat.SetFloat(BlurRadiusId, QuantizePx(layer.Blur));
            mat.SetFloat(CornerRadiusId, radius);
            mat.SetFloat(ShapeRadiusId, SpreadRadius(radius, spread));
            mat.SetFloat(SpreadId, spread);
            mat.SetVector(OffsetId, new Vector4(OffsetPx(layer.OffsetX), OffsetPx(layer.OffsetY), 0f, 0f));
            mat.SetVector(ElementSizeId, new Vector4(target.x, target.y, rect.width, rect.height));
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

        // Inserts a freshly baked silhouette and evicts the least-recently-used bake (destroying its texture)
        // when the cache would exceed its cap. A null bake (shader unresolved) is not cached so it re-attempts.
        // Re-storing an already-present key replaces its texture and just refreshes recency (no duplicate node).
        private static void StoreSilhouette((long shape, int w, int h, int skewCenti, long offset) key, Texture2D tex)
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
        private static void TouchSilhouette((long shape, int w, int h, int skewCenti, long offset) key)
        {
            if (s_silhouetteLruNodes.TryGetValue(key, out var node))
            {
                s_silhouetteLru.Remove(node);
                s_silhouetteLru.AddLast(node);
            }
        }

        // Round to whole pixels and pack (corner, blur, spread) into one key, with the y skew in hundredths of a
        // degree in the top 16 bits. Uses the SAME quantizers as the bake so the key never disagrees with the
        // baked texture.
        private static long CacheKey(float corner, float blur, float spread, float skewYDeg)
        {
            long c = (long)QuantizePx(corner);
            long b = (long)QuantizePx(blur);
            long s = (ushort)(short)QuantizeSpread(spread);
            long y = (ushort)(short)Mathf.Clamp(Mathf.RoundToInt(skewYDeg * 100f), short.MinValue, short.MaxValue);
            return (y << 48) | (c << 32) | (b << 16) | s;
        }

        // The offset in hundredths of a pixel, as the bake reads it, since it places the caster's cut-out.
        private static float OffsetPx(float v) => Mathf.RoundToInt(v * 100f) / 100f;

        private static long OffsetKey(float x, float y)
            => ((long)Mathf.RoundToInt(x * 100f) << 32) | (uint)Mathf.RoundToInt(y * 100f);
    }
}
