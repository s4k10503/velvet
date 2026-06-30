using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Bakes the Velvet/DropShadow SDF shader into a full-size shadow texture and caches the result.
    // UI Toolkit (6000.3) has no native box-shadow, so a shadow-*/drop-shadow-* utility paints a baked
    // shadow texture behind its caster (DropShadowSilhouette draws it as a single quad in the caster's own
    // generateVisualContent). The shader is BAKED once (Graphics.Blit) rather than applied as a live
    // per-element material: UITK freezes a custom-material element's draw-command order at first generation,
    // so a shadow first generated under an animating ancestor transform would composite in FRONT of its
    // caster. The bake produces a WHITE texture with the soft falloff in alpha — the shadow color is applied
    // as the quad's vertex tint at draw time, so a color change retints without re-baking.
    //
    // The texture is the FULL silhouette at the caster's pixel size (target + padding per side), upright OR
    // sheared (skewXDeg follows a skew-x-* caster). A full-size bake is used for every case because the
    // in-element paint draws ONE quad; the size-keyed LRU cache below bounds the per-size bakes.
    internal static class DropShadowBaker
    {
        private const string ShadowShaderPath = "Velvet/DropShadow";

        // Bleed margin added around the blur so the soft edge is not clipped by the quad. Public so the paint
        // binding can size and offset the draw quad to match the bake.
        internal const float ExtraPadding = 5f;

        // Cached across all shadows: Shader.Find is a project-wide lookup, identical for every shadow.
        private static Shader s_shader;

        // The single bake Material, lazily created from the shader. It is only a bake tool (the baked textures
        // are cached below), so one instance serves every shadow and is disposed once on reconciler teardown.
        private static Material s_material;

        // Baked SDF silhouettes shared across all shadows, keyed by (corner/blur/spread, target size, skew).
        // A baked shadow cannot be size-independent (a full-size quad is drawn), so a card that animates
        // through many pixel sizes would otherwise leak one full-quad Texture2D per distinct size. The LRU
        // below caps the cache and evicts the least-recently-used bake; a re-needed size re-bakes.
        private static readonly Dictionary<(long key, int w, int h, int skewCenti), Texture2D> s_silhouetteCache = new();

        // Most-recently-used at the TAIL of s_silhouetteLru, least at the HEAD; a node-by-key map keeps
        // touch/evict O(1) and keeps the list in lockstep with the cache (no duplicate or orphaned nodes).
        private const int MaxSilhouetteCacheEntries = 64;
        private static readonly LinkedList<(long key, int w, int h, int skewCenti)> s_silhouetteLru = new();
        private static readonly Dictionary<(long key, int w, int h, int skewCenti),
            LinkedListNode<(long key, int w, int h, int skewCenti)>> s_silhouetteLruNodes = new();

        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
        private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
        private static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");
        private static readonly int SpreadId = Shader.PropertyToID("_Spread");
        private static readonly int ElementSizeId = Shader.PropertyToID("_ElementSize");
        private static readonly int SkewXId = Shader.PropertyToID("_SkewX");

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

        // Returns the cached silhouette for (corner, blur, spread, target size, skew), baking it if absent.
        // Keeps the returned key warm in the LRU (touched on hit, re-baked if it was evicted) so a caster
        // re-fetching its own bake never serves a destroyed texture. NB this only protects the key being
        // fetched: were more than MaxSilhouetteCacheEntries distinct-sized shadows painted in a SINGLE frame,
        // an earlier caster's already-handed-off texture could be evicted by a later bake that frame — well
        // beyond any realistic screen, but not an absolute guarantee. Returns null (the caller paints nothing)
        // when the shader is unavailable / headless.
        internal static Texture2D GetOrBakeSilhouette(float corner, float blur, float spread,
            float targetWidth, float targetHeight, float skewXDeg)
        {
            if (targetWidth <= 0f || targetHeight <= 0f)
            {
                return null;
            }
            if (!EnsureMaterial())
            {
                return null;
            }

            var key = (CacheKey(corner, blur, spread),
                Mathf.RoundToInt(targetWidth), Mathf.RoundToInt(targetHeight),
                Mathf.RoundToInt(skewXDeg * 100f));
            if (s_silhouetteCache.TryGetValue(key, out var tex) && tex != null)
            {
                TouchSilhouette(key); // bump recency on a cache hit
                return tex;
            }

            tex = BakeSilhouetteTexture(s_material, corner, blur, spread, targetWidth, targetHeight, skewXDeg);
            StoreSilhouette(key, tex); // insert + evict-if-over-cap (a null bake is not cached)
            return tex;
        }

        // The quad pixel size (per side padded by blur + ExtraPadding) the silhouette is baked at, for a caster
        // of (targetWidth, targetHeight). The paint binding sizes its draw quad to this so one texel maps to
        // one pixel. Mirrors BakeSilhouetteTexture's clamp exactly.
        internal static void QuadSize(float targetWidth, float targetHeight, float blur,
            out int quadW, out int quadH)
        {
            var pad = QuantizePx(blur) + ExtraPadding;
            quadW = Mathf.Clamp(Mathf.CeilToInt(targetWidth + (2f * pad)), 8, 2048);
            quadH = Mathf.Clamp(Mathf.CeilToInt(targetHeight + (2f * pad)), 8, 2048);
        }

        // Disposes the bake Material. Invoked once on reconciler teardown — the baked textures are cached
        // process-wide and are NOT touched here (the editor reset above destroys them). Idempotent.
        internal static void DisposeMaterial()
        {
            VelvetObjectUtil.Destroy(s_material);
            s_material = null;
        }

        private static bool EnsureMaterial()
        {
            if (s_material != null)
            {
                return true;
            }
            if (s_shader == null)
            {
                s_shader = Shader.Find(ShadowShaderPath);
            }
            if (s_shader == null)
            {
                FiberLogger.LogWarning("DropShadow", $"Shader not found: {ShadowShaderPath}. " +
                    "Ensure the project uses URP and the shader is included in the build.");
                return false;
            }
            s_material = new Material(s_shader);
            return true;
        }

        // Quantize a radius to whole pixels (bakes are pixel-resolution anyway). The cache key and the baked
        // texture must BOTH derive from these quantized values, or two presets whose raw floats round to the
        // same key would share whichever raw-float texture baked first, giving a subpixel-off shadow.
        internal static float QuantizePx(float v) => Mathf.Clamp(Mathf.RoundToInt(v), 0, 0xFFFF);

        // Bakes the silhouette at the exact quad pixel size (target + padding per side), so the stretched
        // quad is texel-accurate. The shader unshears the sample coordinate, so the SDF evaluates the upright
        // rounded box while the rendered alpha follows the slant. White RGB; alpha is the soft falloff.
        private static Texture2D BakeSilhouetteTexture(Material mat, float corner, float blur, float spread,
            float targetWidth, float targetHeight, float skewXDeg)
        {
            corner = QuantizePx(corner);
            blur = QuantizePx(blur);
            spread = QuantizePx(spread);
            var pad = blur + ExtraPadding;
            var qw = Mathf.Clamp(Mathf.CeilToInt(targetWidth + (2f * pad)), 8, 2048);
            var qh = Mathf.Clamp(Mathf.CeilToInt(targetHeight + (2f * pad)), 8, 2048);

            mat.SetColor(ShadowColorId, Color.white);
            mat.SetFloat(BlurRadiusId, blur);
            mat.SetFloat(CornerRadiusId, corner);
            mat.SetFloat(SpreadId, spread);
            mat.SetVector(ElementSizeId, new Vector4(targetWidth, targetHeight, 0f, 0f));
            mat.SetFloat(SkewXId, Mathf.Tan(skewXDeg * Mathf.Deg2Rad));

            var rt = RenderTexture.GetTemporary(qw, qh, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(null, rt, mat);
            RenderTexture.active = rt;
            var tex = new Texture2D(qw, qh, TextureFormat.RGBA32, false)
            {
                name = "VelvetDropShadowSilhouette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.ReadPixels(new Rect(0, 0, qw, qh), 0, 0);
            tex.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            mat.SetFloat(SkewXId, 0f); // defensive: never leave a stale shear on the shared Material
            return tex;
        }

        // Inserts a freshly baked silhouette and evicts the least-recently-used bake (destroying its texture)
        // when the cache would exceed its cap. A null bake (shader unresolved) is not cached so it re-attempts.
        // Re-storing an already-present key replaces its texture and just refreshes recency (no duplicate node).
        private static void StoreSilhouette((long key, int w, int h, int skewCenti) key, Texture2D tex)
        {
            if (tex == null)
            {
                return;
            }
            if (s_silhouetteCache.TryGetValue(key, out var existing))
            {
                if (existing != null && existing != tex)
                {
                    Object.DestroyImmediate(existing);
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
                        Object.DestroyImmediate(evicted);
                    }
                }
            }
        }

        // Marks an existing key most-recently-used (moves its node to the tail) on a cache hit — O(1) via the
        // node map, so a frequently re-hit silhouette pays no linear scan.
        private static void TouchSilhouette((long key, int w, int h, int skewCenti) key)
        {
            if (s_silhouetteLruNodes.TryGetValue(key, out var node))
            {
                s_silhouetteLru.Remove(node);
                s_silhouetteLru.AddLast(node);
            }
        }

        // Round to whole pixels and pack (corner, blur, spread) into one key. Uses the SAME QuantizePx as the
        // bake so the key never disagrees with the baked texture.
        private static long CacheKey(float corner, float blur, float spread)
        {
            long c = (long)QuantizePx(corner);
            long b = (long)QuantizePx(blur);
            long s = (long)QuantizePx(spread);
            return (c << 32) | (b << 16) | s;
        }
    }
}
