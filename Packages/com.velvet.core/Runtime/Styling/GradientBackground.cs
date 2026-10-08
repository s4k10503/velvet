using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A gradient element's bound spec, with the aspect its texture was baked for and the geometry watch
    // that re-bakes it when the box's proportions change. OnGeometryChanged is set only while the spec's
    // geometry depends on the aspect (GradientBackground.DependsOnAspect).
    internal sealed class GradientBinding
    {
        public GradientSpec Spec;
        public int AspectKey;
        public Texture2D? Texture;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
    }

    // Bakes a GradientSpec into a small Texture2D and applies it as an element's background-image,
    // stretched to fill (UI Toolkit then clips it to the element's border-radius). USS has no
    // linear-gradient, so a gradient has to be painted. A baked texture is chosen over a live custom
    // material NOT to avoid URP, but for the same reason DropShadowBaker bakes its (URP) shader rather
    // than binding it: UI Toolkit freezes a custom-material element's draw-command order at first
    // generation, so a live gradient material would composite in front of its own content under an
    // animating ancestor transform. A texture goes through the normal background-image path and orders
    // correctly.
    //
    // A gradient is trivial to compute, so it is baked on the CPU (SetPixels) — no
    // shader asset to author, and the result is unit-testable off-GPU (sample the baked pixels directly).
    //
    // Cache: keyed by spec (value-equal) and the box's aspect, and shared across every element that
    // resolves to the same pair. Only a gradient whose geometry depends on the box's proportions — a
    // diagonal angle, a conic — carries an aspect in its key, quantized to
    // AspectStepsPerOctave steps per doubling and held within +-MaxAspectSteps, so those add at most
    // 2 * MaxAspectSteps + 1 textures per distinct spec. Every other gradient is SIZE-INDEPENDENT
    // (stretched to fit) and keys on its spec alone, so unlike DropShadowBaker's silhouette cache — whose
    // key includes the element size AND skew, so it needs an LRU + eviction — the key space is the set of
    // distinct gradients a UI declares, bounded by the className authoring and not by data. So the cache
    // is a plain memo with no eviction — which also sidesteps the use-after-evict hazard of
    // destroying a texture still referenced by a mounted element. The editor reset hook drops the cache
    // each play session (textures are HideAndDontSave and would otherwise persist with Reload-Domain off).
    internal static class GradientBackground
    {
        // Resolution of the baked gradient, stretched to any element size with bilinear filtering: a stop
        // list's detail, a hard stop included, lands at 1/128 of the box along each axis.
        private const int Resolution = 128;

        // The aspect (width over height) a baked texture is laid out for is held as a step count of this
        // many per doubling, so a box that grows a pixel does not bake a new texture, and within
        // +-MaxAspectSteps (aspects from 1:16 to 16:1).
        private const int AspectStepsPerOctave = 32;
        private const int MaxAspectSteps = 4 * AspectStepsPerOctave;

        // The largest parameter a colour is read at. A stop at 100% lies above it, so a hard stop there
        // paints its earlier colour across the box and its later one never, as CSS does.
        private const float MaxParameter = 1f - 1e-6f;

        private static readonly Dictionary<(GradientSpec, int), Texture2D> s_cache = new();

#if UNITY_EDITOR
        // Baked textures are HideAndDontSave and persist across play-mode cycles without a Domain
        // Reload; drop them so a fresh run re-bakes rather than serving a stale texture (and so they do not
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

        // True when the gradient's geometry depends on the box's proportions: CSS lays a diagonal angle's
        // gradient line out over the physical box, and a conic sweeps physical angles. An angle along an
        // axis, a corner direction and a radial come out the same in every box.
        internal static bool DependsOnAspect(in GradientSpec spec)
        {
            switch (spec.Type)
            {
                case GradientType.Conic:
                    return true;
                case GradientType.Radial:
                    return false;
                default:
                    var offAxis = Mathf.Repeat(spec.AngleDeg, 90f);
                    return !spec.ToCorner && offAxis > 0.01f && offAxis < 89.99f;
            }
        }

        // The quantized aspect of a box; 0 (a square) for a box with no size yet.
        internal static int AspectKey(float width, float height)
        {
            if (!(width > 0f) || !(height > 0f))
            {
                return 0;
            }
            var steps = Mathf.RoundToInt(Mathf.Log(width / height, 2f) * AspectStepsPerOctave);
            return Mathf.Clamp(steps, -MaxAspectSteps, MaxAspectSteps);
        }

        private static float AspectOf(int aspectKey) => Mathf.Pow(2f, aspectKey / (float)AspectStepsPerOctave);

        // Applies the gradient and starts watching the element's geometry when the gradient needs it.
        public static GradientBinding Bind(VisualElement element, GradientSpec spec)
        {
            var binding = new GradientBinding();
            Rebind(element, binding, spec);
            return binding;
        }

        // Applies a new spec to an element already bound, re-reading the box it is baked for.
        public static void Rebind(VisualElement element, GradientBinding binding, GradientSpec spec)
        {
            binding.Spec = spec;
            var size = element.layout.size;
            binding.AspectKey = DependsOnAspect(spec) ? AspectKey(size.x, size.y) : 0;
            binding.Texture = Apply(element, spec, binding.AspectKey);
            SyncGeometryWatch(element, binding);
        }

        // Stops watching the element's geometry. Pairs with Clear, which the caller runs when it also
        // wants the background gone.
        public static void Unwatch(VisualElement element, GradientBinding binding)
        {
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
                binding.OnGeometryChanged = null;
            }
        }

        private static void SyncGeometryWatch(VisualElement element, GradientBinding binding)
        {
            if (!DependsOnAspect(binding.Spec))
            {
                Unwatch(element, binding);
                return;
            }
            if (binding.OnGeometryChanged != null)
            {
                return;
            }
            binding.OnGeometryChanged = evt =>
            {
                var key = AspectKey(evt.newRect.width, evt.newRect.height);
                if (key == binding.AspectKey)
                {
                    return;
                }
                binding.AspectKey = key;
                // Only while the image is still the one this binding wrote: a className-driven image written
                // since (bg-[addr:…]) owns the slot, and the patch that wrote it did not touch this binding.
                // A SceneViewElement's slot may be held by its camera, so it is always written.
                if (element is not SceneViewElement && element.style.backgroundImage.value.texture != binding.Texture)
                {
                    return;
                }
                // The image alone: backgroundSize is whatever Apply or a pan mode last set.
                binding.Texture = GetOrBake(binding.Spec, key);
                SceneViewElement.WriteBackground(element, new StyleBackground(binding.Texture));
            };
            element.RegisterCallback(binding.OnGeometryChanged);
        }

        public static Texture2D Apply(VisualElement element, GradientSpec spec, int aspectKey = 0)
        {
            var tex = GetOrBake(spec, aspectKey);
            // Through the SceneView ownership gate: a live camera feed keeps the slot and defers
            // the gradient for its release; everywhere else this is a plain style write.
            SceneViewElement.WriteBackground(element, new StyleBackground(tex));
            // Stretch the baked texture to the full element box (no 9-slice); border-radius clips it.
            element.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
            return tex;
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

        private static Texture2D GetOrBake(GradientSpec spec, int aspectKey)
        {
            var key = (spec, DependsOnAspect(spec) ? aspectKey : 0);
            if (s_cache.TryGetValue(key, out var tex) && tex != null)
            {
                return tex;
            }
            tex = Bake(spec, AspectOf(key.Item2));
            s_cache[key] = tex;
            return tex;
        }

        // Bakes the spec into an RGBA32 texture for a box of the given width over height. Pixel coordinates
        // use UV with (0,0) at the top-left so the gradient axis matches screen space (y grows downward) —
        // UI Toolkit draws background-image top-left-origin, so a ToBottom gradient runs from-color at the
        // top to to-color at the bottom.
        internal static Texture2D Bake(GradientSpec spec, float aspect)
        {
            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color[Resolution * Resolution];
            var direction = LinearDirection(spec.AngleDeg);
            // A corner direction's lines run parallel to the box's diagonal, which the stretched texture
            // already keeps in any box, so it is laid out over a square.
            var lineAspect = spec.ToCorner ? 1f : aspect;
            for (var row = 0; row < Resolution; row++)
            {
                // Texture2D.SetPixels is bottom-up (row 0 = bottom); flip so row 0 is the TOP of the box.
                var v = 1f - row / (float)(Resolution - 1);
                for (var col = 0; col < Resolution; col++)
                {
                    var u = col / (float)(Resolution - 1);
                    var t = ComputeT(spec, u, v, aspect, lineAspect, direction);
                    pixels[row * Resolution + col] = ColorAt(spec, t);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        // The gradient parameter t at UV (u, v) of a box whose width over height is aspect, for the spec's
        // type: Linear projects onto the gradient line, Radial is the elliptical distance from the centre
        // over the farthest-corner ellipse, Conic is the clockwise angle from the centre (0° = up, matching
        // CSS conic) minus the start angle, over 360°.
        private static float ComputeT(GradientSpec spec, float u, float v, float aspect, float lineAspect, Vector2 direction)
        {
            switch (spec.Type)
            {
                case GradientType.Radial:
                    return RadialParameter(u - spec.CenterX, v - spec.CenterY, spec.CenterX, spec.CenterY);
                case GradientType.Conic:
                {
                    // atan2(x, -y): 0° straight up, increasing clockwise in the y-down UV (CSS conic).
                    var ang = Mathf.Atan2((u - spec.CenterX) * aspect, -(v - spec.CenterY)) * Mathf.Rad2Deg;
                    return ((((ang - spec.AngleDeg) % 360f) + 360f) % 360f) / 360f;
                }
                default:
                    return (((u - 0.5f) * lineAspect * direction.x) + ((v - 0.5f) * direction.y))
                        / ((Mathf.Abs(direction.x) * lineAspect) + Mathf.Abs(direction.y)) + 0.5f;
            }
        }

        // The radial parameter of a point offset (dx, dy) in UV from a centre (cx, cy): 1 on the ellipse that
        // CSS's default farthest-corner sizing draws, which passes through the farthest box corner with the
        // proportions of the farthest-side ellipse, so it is the same in every box.
        private static float RadialParameter(float dx, float dy, float cx, float cy)
        {
            var rx = Mathf.Max(cx, 1f - cx);
            var ry = Mathf.Max(cy, 1f - cy);
            var nx = dx / rx;
            var ny = dy / ry;
            return Mathf.Sqrt((nx * nx) + (ny * ny)) * 0.70710678f;
        }

        // The unit direction a linear gradient runs in, for an angle in CSS degrees (0 = to top, clockwise),
        // in a box whose x points right and y down. Shared with the skew silhouette bake.
        internal static Vector2 LinearDirection(float angleDeg)
        {
            var rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
        }

        // Colour at axis parameter t, honoring the stop POSITIONS: the first colour before the first stop,
        // otherwise a linear interpolation from the last stop at or before t to the next one, and the last
        // colour once none follows. t is held inside [0, MaxParameter], which settles a stop at the very
        // start or end of the line the way CSS does: where stops share a position the later one starts there,
        // and a hard stop at 100% never shows its later colour. The skew silhouette shader evaluates the
        // same walk.
        private static Color ColorAt(GradientSpec spec, float t)
        {
            t = Mathf.Clamp(t, 0f, MaxParameter);
            var stops = spec.Stops;
            if (t < stops[0].Position)
            {
                return stops[0].Color;
            }
            var i = 1;
            while (i < stops.Length && t >= stops[i].Position)
            {
                i++;
            }
            if (i == stops.Length)
            {
                return stops[i - 1].Color;
            }
            var a = stops[i - 1];
            var b = stops[i];
            return Lerp(a.Color, b.Color, (t - a.Position) / (b.Position - a.Position), spec.Interp);
        }

        // Lerps two stops in the gradient's interpolation space — plain sRGB channels, or the
        // perceptually-uniform OKLab (sRGB → linear → OKLab, lerp, back) when /oklch|/oklab was set — with
        // the colour components weighted by alpha, as CSS interpolates, so a stop fading to transparent
        // keeps its colour instead of darkening toward the transparent colour's channels. Where the
        // result is fully transparent its colour is unweighted, which nothing paints.
        private static Color Lerp(Color a, Color b, float t, GradientInterp interp)
        {
            var oklab = interp == GradientInterp.Oklab;
            Vector3 ca = oklab ? (Vector3)ToOklab(a) : new Vector3(a.r, a.g, a.b);
            Vector3 cb = oklab ? (Vector3)ToOklab(b) : new Vector3(b.r, b.g, b.b);
            var alpha = Mathf.Lerp(a.a, b.a, t);
            var mixed = alpha > 0f
                ? Vector3.Lerp(ca * a.a, cb * b.a, t) / alpha
                : Vector3.Lerp(ca, cb, t);
            return oklab
                ? FromOklab(new Vector4(mixed.x, mixed.y, mixed.z, alpha))
                : new Color(mixed.x, mixed.y, mixed.z, alpha);
        }

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
    }
}
