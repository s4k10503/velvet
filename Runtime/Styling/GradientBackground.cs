using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Bakes a GradientSpec into a small Texture2D and applies it as an element's background-image,
    // stretched to fill (UI Toolkit then clips it to the element's border-radius). USS has no
    // linear-gradient, so a gradient has to be painted. A baked texture is chosen over a live custom
    // material NOT to avoid URP, but for the same reason DropShadowBaker bakes its (URP) shader rather
    // than binding it: UI Toolkit freezes a custom-material element's draw-command order at first
    // generation, so a live gradient material would composite in front of its own content under an
    // animating ancestor transform. A texture goes through the normal background-image path and orders
    // correctly.
    //
    // A linear 2-3 stop gradient is trivial to compute, so it is baked on the CPU (SetPixels) — no
    // shader asset to author, and the result is unit-testable off-GPU (sample the baked pixels directly).
    //
    // Cache: keyed by spec (value-equal) and shared across every element that resolves to the same
    // gradient. Unlike DropShadowBaker's silhouette cache — whose key includes the element size AND
    // skew, so it needs an LRU + eviction to bound an unbounded variant space — a gradient texture is
    // SIZE-INDEPENDENT (stretched to fit), so the key space is just the set of distinct gradients a UI
    // declares: small and bounded by the className authoring, not by data. So the cache is a plain
    // unbounded memo with no eviction — which also sidesteps the use-after-evict hazard of destroying a
    // texture still referenced by a mounted element. The editor reset hook drops the cache each play
    // session (textures are HideAndDontSave and would otherwise persist with Reload-Domain off).
    internal static class GradientBackground
    {
        // Resolution of the baked gradient. Stretched to any element size with bilinear filtering, so a
        // modest square is plenty for a smooth 2-3 stop linear gradient (incl. the 45° diagonals).
        private const int Resolution = 128;

        private static readonly Dictionary<GradientSpec, Texture2D> s_cache = new();

#if UNITY_EDITOR
        // Baked textures are HideAndDontSave and persist across play-mode cycles without a Domain Reload;
        // drop them so a fresh run re-bakes rather than serving a stale texture (and so they do not
        // accumulate). Mirrors DropShadowBaker.ResetStaticCaches.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            foreach (var tex in s_cache.Values)
            {
                if (tex != null)
                {
                    Object.DestroyImmediate(tex);
                }
            }
            s_cache.Clear();
        }
#endif

        public static void Apply(VisualElement element, GradientSpec spec)
        {
            var tex = GetOrBake(spec);
            // Through the SceneView ownership gate: a live camera feed keeps the slot and defers
            // the gradient for its release; everywhere else this is a plain style write.
            SceneViewElement.WriteBackground(element, new StyleBackground(tex));
            // Stretch the baked texture to the full element box (no 9-slice); border-radius clips it.
            element.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
        }

        // Full reset: clears the gradient's background-image AND the backgroundSize it set.
        public static void Clear(VisualElement element)
        {
            SceneViewElement.WriteBackground(element, new StyleBackground(StyleKeyword.Null));
            ClearSizeOnly(element);
        }

        // Resets only the backgroundSize the gradient set, leaving background-image untouched — used when
        // a className-driven background image (bg-[addr:…]) owns the image and must not be wiped.
        public static void ClearSizeOnly(VisualElement element)
        {
            element.style.backgroundSize = new StyleBackgroundSize(StyleKeyword.Null);
        }

        private static Texture2D GetOrBake(GradientSpec spec)
        {
            if (s_cache.TryGetValue(spec, out var tex) && tex != null)
            {
                return tex;
            }
            tex = Bake(spec);
            s_cache[spec] = tex;
            return tex;
        }

        // Bakes the spec into an RGBA32 texture. Pixel coordinates use UV with (0,0) at the top-left so
        // the gradient axis matches screen space (y grows downward) — UI Toolkit draws background-image
        // top-left-origin, so a ToBottom gradient runs from-color at the top to to-color at the bottom.
        internal static Texture2D Bake(GradientSpec spec)
        {
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            // Precompute per-type constants once: the linear axis, and the radial farthest-corner distance.
            GetAxis(spec.AngleDeg, out var sx, out var sy, out var ex, out var ey);
            var dx = ex - sx;
            var dy = ey - sy;
            var denom = Mathf.Max((dx * dx) + (dy * dy), 1e-6f);
            var maxRadial = Mathf.Max(FarthestCornerDistance(spec.CenterX, spec.CenterY), 1e-5f);

            var pixels = new Color[Resolution * Resolution];
            for (var row = 0; row < Resolution; row++)
            {
                // Texture2D.SetPixels is bottom-up (row 0 = bottom); flip so row 0 is the TOP of the box.
                var v = 1f - row / (float)(Resolution - 1);
                for (var col = 0; col < Resolution; col++)
                {
                    var u = col / (float)(Resolution - 1);
                    var t = ComputeT(spec, u, v, sx, sy, dx, dy, denom, maxRadial);
                    pixels[row * Resolution + col] = ColorAt(spec, Mathf.Clamp01(t));
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        // The gradient parameter t (0..1) at UV (u, v) for the spec's type: Linear projects onto the axis,
        // Radial is the distance from the centre over the farthest-corner distance, Conic is the clockwise
        // angle from the centre (0° = up, matching CSS conic) minus the start angle, over 360°.
        private static float ComputeT(GradientSpec spec, float u, float v,
            float sx, float sy, float dx, float dy, float denom, float maxRadial)
        {
            switch (spec.Type)
            {
                case GradientType.Radial:
                {
                    var ax = u - spec.CenterX;
                    var ay = v - spec.CenterY;
                    return Mathf.Sqrt((ax * ax) + (ay * ay)) / maxRadial;
                }
                case GradientType.Conic:
                {
                    var ax = u - spec.CenterX;
                    var ay = v - spec.CenterY;
                    // atan2(x, -y): 0° straight up, increasing clockwise in the y-down UV (CSS conic).
                    var ang = Mathf.Atan2(ax, -ay) * Mathf.Rad2Deg;
                    return ((((ang - spec.AngleDeg) % 360f) + 360f) % 360f) / 360f;
                }
                default:
                    return ((u - sx) * dx + (v - sy) * dy) / denom;
            }
        }

        // Distance from a UV centre to the farthest box corner (the radial gradient's `to` edge — CSS
        // farthest-corner sizing, so the whole box is covered).
        private static float FarthestCornerDistance(float cx, float cy)
        {
            var dx = Mathf.Max(cx, 1f - cx);
            var dy = Mathf.Max(cy, 1f - cy);
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        // Colour at axis parameter t (0..1), honoring the stop POSITIONS: flat From before FromPos, flat To
        // after ToPos, and a linear interpolation between the bracketing stops. Defaults (0 / 0.5 / 1)
        // reproduce the original even spacing.
        private static Color ColorAt(GradientSpec spec, float t)
        {
            if (t <= spec.FromPos)
            {
                return spec.From;
            }
            if (t >= spec.ToPos)
            {
                return spec.To;
            }
            if (!spec.HasVia)
            {
                return Lerp(spec.From, spec.To, (t - spec.FromPos) / Mathf.Max(spec.ToPos - spec.FromPos, 1e-5f), spec.Interp);
            }
            return t < spec.ViaPos
                ? Lerp(spec.From, spec.Via, (t - spec.FromPos) / Mathf.Max(spec.ViaPos - spec.FromPos, 1e-5f), spec.Interp)
                : Lerp(spec.Via, spec.To, (t - spec.ViaPos) / Mathf.Max(spec.ToPos - spec.ViaPos, 1e-5f), spec.Interp);
        }

        // Lerps two stops in the gradient's interpolation space: a plain sRGB channel lerp, or the
        // perceptually-uniform OKLab lerp (sRGB → linear → OKLab, lerp, back) when /oklch|/oklab was set.
        private static Color Lerp(Color a, Color b, float t, GradientInterp interp)
            => interp == GradientInterp.Oklab ? LerpOklab(a, b, t) : Color.Lerp(a, b, t);

        private static Color LerpOklab(Color a, Color b, float t)
            => FromOklab(Vector4.Lerp(ToOklab(a), ToOklab(b), t));

        // sRGB Color → OKLab (xyz) + alpha (w), per Björn Ottosson's matrices (operating on LINEAR rgb).
        private static Vector4 ToOklab(Color c)
        {
            float lr = SrgbToLinear(c.r), lg = SrgbToLinear(c.g), lb = SrgbToLinear(c.b);
            var l = (0.4122214708f * lr) + (0.5363325363f * lg) + (0.0514459929f * lb);
            var m = (0.2119034982f * lr) + (0.6806995451f * lg) + (0.1073969566f * lb);
            var s = (0.0883024619f * lr) + (0.2817188376f * lg) + (0.6299787005f * lb);
            float l_ = Cbrt(l), m_ = Cbrt(m), s_ = Cbrt(s);
            return new Vector4(
                (0.2104542553f * l_) + (0.7936177850f * m_) - (0.0040720468f * s_),
                (1.9779984951f * l_) - (2.4285922050f * m_) + (0.4505937099f * s_),
                (0.0259040371f * l_) + (0.7827717662f * m_) - (0.8086757660f * s_),
                c.a);
        }

        // OKLab (xyz) + alpha (w) → sRGB Color.
        private static Color FromOklab(Vector4 lab)
        {
            var l_ = lab.x + (0.3963377774f * lab.y) + (0.2158037573f * lab.z);
            var m_ = lab.x - (0.1055613458f * lab.y) - (0.0638541728f * lab.z);
            var s_ = lab.x - (0.0894841775f * lab.y) - (1.2914855480f * lab.z);
            float l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
            var lr = (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s);
            var lg = (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s);
            var lb = (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s);
            return new Color(
                Mathf.Clamp01(LinearToSrgb(lr)),
                Mathf.Clamp01(LinearToSrgb(lg)),
                Mathf.Clamp01(LinearToSrgb(lb)),
                lab.w);
        }

        // The exact IEC 61966-2-1 sRGB transfer, hand-rolled (NOT Color.linear / Mathf.GammaToLinearSpace)
        // on purpose: OKLab is defined on this specific curve regardless of the project's active color
        // space, and these constants must match the shader's HLSL copy bit-for-bit so the skew and non-skew
        // bakes agree.
        private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        private static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : (1.055f * Mathf.Pow(c, 1f / 2.4f)) - 0.055f;
        private static float Cbrt(float x) => x < 0f ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f);

        // Start/end of the gradient axis in the square UV (origin top-left, x right, y down). t=0 is the
        // `from` end, t=1 is the `to` end. The angle follows CSS (0° = to top, 90° = to right, clockwise);
        // the axis is the CSS "magic corner" endpoints for the unit square, which reproduces the 8 named
        // directions exactly (e.g. 180° → top-centre→bottom-centre, 135° → corner (0,0)→(1,1)) and
        // generalizes to any angle. Shared with the skew silhouette baker so the sheared fill matches.
        internal static void GetAxis(float angleDeg, out float sx, out float sy, out float ex, out float ey)
        {
            var rad = angleDeg * Mathf.Deg2Rad;
            // Direction the gradient flows TOWARD, in UV (y down): 0°→up (0,-1), 90°→right (1,0).
            var dx = Mathf.Sin(rad);
            var dy = -Mathf.Cos(rad);
            // Half the projection of the unit square onto the axis (the corner the gradient line reaches).
            var half = 0.5f * (Mathf.Abs(dx) + Mathf.Abs(dy));
            sx = 0.5f - (half * dx);
            sy = 0.5f - (half * dy);
            ex = 0.5f + (half * dx);
            ey = 0.5f + (half * dy);
        }
    }
}
