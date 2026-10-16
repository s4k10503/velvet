using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // Bakes an inset box-shadow into a white texture with the shadow in alpha, the size of the padding box it is
    // painted over, and caches the bakes. The shadow is what CSS Backgrounds 3 draws for `inset`: the padding box
    // with a hole cut by the box moved by the offset and shrunk by the spread, blurred by a Gaussian whose standard
    // deviation is half the blur radius, and clipped to the padding box. Baked on the CPU, as the inset shadows
    // Tailwind ships blur by at most 4px; the colour is the quad's tint, so a colour change needs no bake.
    internal static class InsetShadowBaker
    {
        private const int MaxEntries = 32;

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int _w, _h, _ox, _oy, _blur, _spread, _tl, _tr, _br, _bl;

            public Key(int w, int h, Vector4 radii, ShadowSpec s)
            {
                _w = w;
                _h = h;
                _ox = Q(s.OffsetX);
                _oy = Q(s.OffsetY);
                _blur = Q(s.Blur);
                _spread = Q(s.Spread);
                _tl = Q(radii.x);
                _tr = Q(radii.y);
                _br = Q(radii.z);
                _bl = Q(radii.w);
            }

            private static int Q(float v) => Mathf.RoundToInt(v * 16f);

            public bool Equals(Key o) => _w == o._w && _h == o._h && _ox == o._ox && _oy == o._oy && _blur == o._blur
                && _spread == o._spread && _tl == o._tl && _tr == o._tr && _br == o._br && _bl == o._bl;

            public override bool Equals(object? obj) => obj is Key k && Equals(k);

            public override int GetHashCode()
                => HashCode.Combine(HashCode.Combine(_w, _h, _ox, _oy, _blur), HashCode.Combine(_spread, _tl, _tr, _br, _bl));
        }

        private static readonly Dictionary<Key, LinkedListNode<(Key Key, Texture2D Texture)>> s_cache = new();
        private static readonly LinkedList<(Key Key, Texture2D Texture)> s_lru = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            foreach (var (_, texture) in s_lru)
            {
                VelvetObjectUtil.Destroy(texture);
            }
            s_lru.Clear();
            s_cache.Clear();
        }
#endif

        // The bake for a padding box of width x height with these corner radii, or null for an empty box.
        internal static Texture2D? Get(float width, float height, Vector4 radii, in ShadowSpec shadow)
        {
            var w = Mathf.CeilToInt(width);
            var h = Mathf.CeilToInt(height);
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
            {
                return null;
            }
            var key = new Key(w, h, radii, shadow);
            if (s_cache.TryGetValue(key, out var node) && node.Value.Texture != null)
            {
                s_lru.Remove(node);
                s_lru.AddLast(node);
                return node.Value.Texture;
            }
            var texture = Bake(w, h, radii, shadow);
            if (s_cache.Count >= MaxEntries && s_lru.First is { } oldest)
            {
                s_lru.RemoveFirst();
                s_cache.Remove(oldest.Value.Key);
                VelvetObjectUtil.Destroy(oldest.Value.Texture);
            }
            s_cache[key] = s_lru.AddLast((key, texture));
            return texture;
        }

        // The alpha at each pixel centre: coverage of the padding box times the blurred coverage of what lies outside
        // the hole. Row 0 is the bottom of the box, so the quad samples the top at v = 1.
        private static Texture2D Bake(int w, int h, Vector4 radii, in ShadowSpec s)
        {
            var pixels = new Color32[w * h];
            var half = new Vector2(w * 0.5f, h * 0.5f);
            var holeHalf = new Vector2(Mathf.Max(0f, half.x - s.Spread), Mathf.Max(0f, half.y - s.Spread));
            var holeRadii = RingBandShape.SpreadRadii(radii, -s.Spread);
            var sigma = s.Blur * 0.5f;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f - half.x, y + 0.5f - half.y);
                    var clip = Mathf.Clamp01(0.5f - RoundedBoxDistance(p, half, radii));
                    var d = RoundedBoxDistance(p - new Vector2(s.OffsetX, s.OffsetY), holeHalf, holeRadii);
                    var outside = sigma > 0f ? 0.5f * Erfc(-d / (sigma * 1.41421356f)) : Mathf.Clamp01(0.5f + d);
                    pixels[((h - 1 - y) * w) + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(clip * outside * 255f));
                }
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // Signed distance from p to a box of half-size half centred on the origin, rounded by the radius of the
        // quadrant p lies in; negative inside.
        internal static float RoundedBoxDistance(Vector2 p, Vector2 half, Vector4 radii)
        {
            var r = p.x < 0f ? (p.y < 0f ? radii.x : radii.w) : (p.y < 0f ? radii.y : radii.z);
            r = Mathf.Min(r, Mathf.Min(half.x, half.y));
            var q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        // The complementary error function, by Numerical Recipes' erfcc approximation.
        internal static float Erfc(float x)
        {
            var z = Mathf.Abs(x);
            var t = 1f / (1f + (0.5f * z));
            var poly = -0.82215223f + (t * 0.17087277f);
            poly = 1.48851587f + (t * poly);
            poly = -1.13520398f + (t * poly);
            poly = 0.27886807f + (t * poly);
            poly = -0.18628806f + (t * poly);
            poly = 0.09678418f + (t * poly);
            poly = 0.37409196f + (t * poly);
            poly = 1.00002368f + (t * poly);
            poly = -1.26551223f + (t * poly);
            var r = t * Mathf.Exp((-z * z) + poly);
            return x >= 0f ? r : 2f - r;
        }
    }
}
