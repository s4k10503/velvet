using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A gradient element's bound spec, with the box its texture was baked for and the geometry watch that
    // re-bakes it when the box changes. OnGeometryChanged is set only while the spec's geometry depends on
    // the box (GradientBackground.DependsOnAspect). BoxScale is how many times the element's box the
    // texture is painted over, which a pan mode that oversizes the background sets. Box is the box as the
    // cache keys it (GradientBackground.KeysFor), and Held is the cache entry this binding keeps alive.
    internal sealed class GradientBinding
    {
        public GradientSpec Spec;
        public BoxKey Box;
        public Vector2 BoxScale = Vector2.one;
        public Vector2 BoxSize;
        public Texture2D? Texture;
        public (GradientSpec, BoxKey)? Held;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
    }

    // The box a texture is baked for, as the cache keys it. A is the quantized aspect, or the whole-pixel
    // width of a gradient sized in pixels (with B its height); Resolution is the texture's side, 0 for the
    // default.
    internal readonly struct BoxKey : System.IEquatable<BoxKey>
    {
        public BoxKey(int a, int b, int resolution)
        {
            A = a;
            B = b;
            Resolution = resolution;
        }

        public int A { get; }
        public int B { get; }
        public int Resolution { get; }

        public bool IsDefault => A == 0 && B == 0 && Resolution == 0;

        public bool Equals(BoxKey other) => A == other.A && B == other.B && Resolution == other.Resolution;

        public override bool Equals(object obj) => obj is BoxKey o && Equals(o);

        public override int GetHashCode() => System.HashCode.Combine(A, B, Resolution);
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
    // Cache: keyed by spec (value-equal) and the box, and shared across every element that resolves to the
    // same key. A gradient that does not depend on the box keys on its spec alone and stays for the session:
    // it is bounded by the className authoring and not by data. One that does — a diagonal angle, a conic,
    // a radial circle — keys on the box's aspect, quantized to AspectStepsPerOctave steps per doubling and
    // held within +-MaxAspectSteps, and a radial sized in pixels keys on the box's whole-pixel width and
    // height. Those can take as many keys as an element has sizes, so an entry is reference-counted by the
    // bindings that show it, and once none does it joins an LRU of at most MaxIdleBoxed entries whose
    // eldest is destroyed — an entry a mounted element still shows is never evicted, so unlike
    // DropShadowBaker's silhouette cache there is no use-after-evict hazard to accept. The editor reset
    // hook drops the cache each play session (textures are HideAndDontSave and would otherwise persist
    // with Reload-Domain off).
    internal static class GradientBackground
    {
        // Resolution of the baked gradient, stretched to any element size with bilinear filtering: a stop
        // list's detail, a hard stop included, lands at 1/128 of the box along each axis.
        private const int DefaultResolution = 128;

        // The largest side a gradient with a sharp feature is baked at: a line bakes N x 1 up to
        // MaxLineResolution, anything else N x N up to MaxResolution.
        private const int MaxResolution = 512;
        private const int MaxLineResolution = 2048;

        // A gap between neighbouring stops narrower than this is a sharp feature the default texture would blur.
        private const float SharpGap = 2f / DefaultResolution;

        // The aspect (width over height) a baked texture is laid out for is held as a step count of this
        // many per doubling, so a box that grows a pixel does not bake a new texture, and within
        // +-MaxAspectSteps (aspects from 1:16 to 16:1).
        private const int AspectStepsPerOctave = 32;
        private const int MaxAspectSteps = 4 * AspectStepsPerOctave;

        // The largest parameter a colour is read at on a linear or conic gradient, whose line ends at the
        // box's edge. A stop at 100% lies above it, so a hard stop there paints its earlier colour across the
        // box and its later one never, as CSS does. A radial runs free past 100%: its rings go on beyond the
        // radius where the box does not end.
        private const float MaxParameter = 1f - 1e-6f;

        // Idle boxed entries kept for reuse, eldest at the head.
        private const int MaxIdleBoxed = 32;

        private sealed class CacheEntry
        {
            public Texture2D Texture = null!;
            public int Holders;
            public LinkedListNode<(GradientSpec, BoxKey)>? Idle;
        }

        private static readonly Dictionary<(GradientSpec, BoxKey), CacheEntry> s_cache = new();
        private static readonly LinkedList<(GradientSpec, BoxKey)> s_idle = new();

#if UNITY_EDITOR
        // Baked textures are HideAndDontSave and persist across play-mode cycles without a Domain
        // Reload; drop them so a fresh run re-bakes rather than serving a stale texture (and so they do not
        // accumulate). Mirrors DropShadowBaker.ResetStaticCaches.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            foreach (var entry in s_cache.Values)
            {
                if (entry.Texture != null)
                {
                    Object.DestroyImmediate(entry.Texture);
                }
            }
            s_cache.Clear();
            s_idle.Clear();
        }
#endif

        // True when the gradient's geometry depends on the box's proportions: CSS lays a diagonal angle's
        // gradient line out over the physical box, and a conic sweeps physical angles. An angle along an
        // axis, a corner direction and a radial come out the same in every box.
        internal static bool DependsOnAspect(in GradientSpec spec)
        {
            if (spec.HasLengths)
            {
                return true;
            }
            switch (spec.Type)
            {
                case GradientType.Conic:
                    return true;
                case GradientType.Radial:
                    return spec.Radial.Circle || NeedsAbsoluteSize(spec);
                default:
                    return !spec.ToCorner && !IsAxisAngle(spec.AngleDeg);
            }
        }

        // True when a radial's size or a stop's position is written in pixels, so the texture depends on the
        // box's size and not only its proportions.
        internal static bool NeedsAbsoluteSize(in GradientSpec spec)
            => spec.HasLengths || (spec.Type == GradientType.Radial && spec.Radial.Extent == RadialExtent.Explicit
                && (spec.Radial.Circle || !spec.Radial.XPercent || !spec.Radial.YPercent));

        // True when the gradient has a feature the default texture would blur: two stops a hair apart (a hard
        // edge, a narrow band), or a stop written in pixels, whose place is not yet known.
        internal static bool HasSharpFeature(in GradientSpec spec)
        {
            if (spec.HasLengths)
            {
                return true;
            }
            var stops = spec.Stops;
            for (var i = 1; i < stops.Length; i++)
            {
                if (stops[i].Position - stops[i - 1].Position < SharpGap)
                {
                    return true;
                }
            }
            return false;
        }

        // True for a linear gradient along an axis, which varies in one direction only: it bakes as a single
        // row (horizontal) or column.
        internal static bool IsAxisLine(in GradientSpec spec, out bool horizontal)
        {
            horizontal = Mathf.Abs(Mathf.Sin(spec.AngleDeg * Mathf.Deg2Rad)) > 0.5f;
            return spec.Type == GradientType.Linear && !spec.ToCorner && IsAxisAngle(spec.AngleDeg);
        }

        private static bool IsAxisAngle(float angleDeg)
        {
            var offAxis = Mathf.Repeat(angleDeg, 90f);
            return offAxis <= 0.01f || offAxis >= 89.99f;
        }

        // Whether the box has to be watched: its proportions, or the size a sharp feature needs a texture for.
        private static bool WatchesBox(in GradientSpec spec) => DependsOnAspect(spec) || HasSharpFeature(spec);

        // The side to bake a gradient with a sharp feature at in a box of the given size: its length in
        // pixels, up to the cap, in powers of two; 0 (the default texture) when that is no more than it.
        private static int ResolutionFor(in GradientSpec spec, float width, float height)
        {
            if (!HasSharpFeature(spec) || !(width > 0f) || !(height > 0f))
            {
                return 0;
            }
            var line = IsAxisLine(spec, out var horizontal);
            var length = line ? (horizontal ? width : height) : Mathf.Max(width, height);
            var side = Mathf.Min(Mathf.NextPowerOfTwo(Mathf.CeilToInt(length)), line ? MaxLineResolution : MaxResolution);
            return side > DefaultResolution ? side : 0;
        }

        // The box a spec is baked for in a box of the given size painted over scale times that box: the
        // quantized aspect for a gradient that depends on proportions, the whole-pixel width and height for
        // one sized in pixels, and the texture side a sharp feature asks for.
        private static BoxKey KeysFor(in GradientSpec spec, Vector2 size, Vector2 scale)
        {
            var w = size.x * scale.x;
            var h = size.y * scale.y;
            var resolution = ResolutionFor(spec, w, h);
            if (NeedsAbsoluteSize(spec))
            {
                return new BoxKey(Mathf.RoundToInt(Mathf.Max(w, 0f)), Mathf.RoundToInt(Mathf.Max(h, 0f)), resolution);
            }
            return new BoxKey(DependsOnAspect(spec) ? AspectKey(w, h) : 0, 0, resolution);
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
            binding.Box = KeysFor(spec, SizeOf(element, binding), binding.BoxScale);
            binding.Texture = Hold(binding);
            // As a utility's image, ranked below a StyleOverrides.BackgroundImage and written through the SceneView
            // ownership gate, which defers it while a live camera feed keeps the slot.
            WriteImage(element, binding);
            // Stretch the baked texture to the full element box (no 9-slice); border-radius clips it.
            element.style.backgroundSize = new StyleBackgroundSize(
                new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
            SyncGeometryWatch(element, binding);
        }

        // The element's box: its layout once it has one, else the size its last geometry event reported.
        private static Vector2 SizeOf(VisualElement element, GradientBinding binding)
        {
            var size = element.layout.size;
            return size.x > 0f && size.y > 0f ? size : binding.BoxSize;
        }

        // Paints the gradient over scale times the element's box from now on, as a pan mode that oversizes
        // the background does, and bakes it for that box.
        public static void SetBoxScale(VisualElement element, GradientBinding binding, Vector2 scale)
        {
            if (binding.BoxScale == scale)
            {
                return;
            }
            binding.BoxScale = scale;
            Rebake(element, binding, KeysFor(binding.Spec, SizeOf(element, binding), scale));
        }

        // Writes the texture for a new box, unless it is the one already written.
        private static void Rebake(VisualElement element, GradientBinding binding, BoxKey box)
        {
            if (box.Equals(binding.Box))
            {
                return;
            }
            binding.Box = box;
            // Only while the utilities' image is still the one this binding wrote: a className-driven image
            // written since (bg-[addr:…]) owns it, and the patch that wrote it did not touch this binding. A
            // StyleOverrides.BackgroundImage showing over it does not stop the re-bake.
            // A SceneViewElement's slot may be held by its camera, so it is always written.
            if (element is not SceneViewElement
                && StyleArbitraryValueResolver.UtilityBackgroundImage(element).value.texture != binding.Texture)
            {
                return;
            }
            // The image alone: backgroundSize is whatever Rebind or a pan mode last set.
            binding.Texture = Hold(binding);
            WriteImage(element, binding);
        }

        // The bake, as the utilities' background image. A gradient has no important form.
        private static void WriteImage(VisualElement element, GradientBinding binding)
            => StyleArbitraryValueResolver.WriteBackgroundImageUtility(
                element, new StyleBackground(binding.Texture), important: false);

        // Makes the binding hold the cache entry for its spec and box, baking it if absent, and lets go of
        // the entry it held before.
        private static Texture2D Hold(GradientBinding binding)
        {
            var spec = binding.Spec;
            var key = (spec, new BoxKey(
                DependsOnAspect(spec) ? binding.Box.A : 0, NeedsAbsoluteSize(spec) ? binding.Box.B : 0, binding.Box.Resolution));
            if (binding.Held is { } held && held.Equals(key) && s_cache.TryGetValue(key, out var kept) && kept.Texture != null)
            {
                return kept.Texture;
            }
            Release(binding);
            if (!s_cache.TryGetValue(key, out var entry) || entry.Texture == null)
            {
                var box = key.Item2;
                var absolute = NeedsAbsoluteSize(spec);
                var aspect = absolute ? (box.A > 0 && box.B > 0 ? box.A / (float)box.B : 1f) : AspectOf(box.A);
                entry = new CacheEntry { Texture = Bake(spec, aspect, absolute ? Mathf.Max(box.A, 1) : 0f, box.Resolution) };
                s_cache[key] = entry;
            }
            entry.Holders++;
            if (entry.Idle != null)
            {
                s_idle.Remove(entry.Idle);
                entry.Idle = null;
            }
            binding.Held = key;
            return entry.Texture;
        }

        // Lets go of the entry the binding holds. A boxed entry nobody holds waits in the LRU for reuse and is
        // destroyed when it is the eldest past the cap.
        private static void Release(GradientBinding binding)
        {
            if (binding.Held is not { } key)
            {
                return;
            }
            binding.Held = null;
            if (!s_cache.TryGetValue(key, out var entry))
            {
                return;
            }
            entry.Holders--;
            if (entry.Holders > 0)
            {
                return;
            }
            if (entry.Texture == null)
            {
                // Destroyed under the cache (an editor reset): nothing left to keep.
                if (entry.Idle != null)
                {
                    s_idle.Remove(entry.Idle);
                }
                s_cache.Remove(key);
                return;
            }
            if (key.Item2.IsDefault)
            {
                return;
            }
            entry.Idle = s_idle.AddLast(key);
            while (s_idle.Count > MaxIdleBoxed && s_idle.First != null)
            {
                var eldest = s_idle.First.Value;
                s_idle.RemoveFirst();
                if (s_cache.TryGetValue(eldest, out var evicted))
                {
                    s_cache.Remove(eldest);
                    if (evicted.Texture != null)
                    {
                        Object.DestroyImmediate(evicted.Texture);
                    }
                }
            }
        }

        // Drops the binding: stops watching the element's geometry and lets go of its texture. Pairs with
        // Clear, which the caller runs when it also wants the background gone.
        public static void Detach(VisualElement element, GradientBinding binding)
        {
            Unwatch(element, binding);
            Release(binding);
        }

        private static void Unwatch(VisualElement element, GradientBinding binding)
        {
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
                binding.OnGeometryChanged = null;
            }
        }

        private static void SyncGeometryWatch(VisualElement element, GradientBinding binding)
        {
            if (!WatchesBox(binding.Spec))
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
                binding.BoxSize = evt.newRect.size;
                Rebake(element, binding, KeysFor(binding.Spec, binding.BoxSize, binding.BoxScale));
            };
            element.RegisterCallback(binding.OnGeometryChanged);
        }

        // Full reset: clears the gradient's background-image AND the backgroundSize it set.
        public static void Clear(VisualElement element)
        {
            StyleArbitraryValueResolver.ClearBackgroundImageUtility(element);
            ClearSizeOnly(element);
        }

        // Resets only the backgroundSize the gradient set, leaving background-image untouched — used when
        // a className-driven background image (bg-[addr:…]) owns the image and must not be wiped.
        public static void ClearSizeOnly(VisualElement element)
        {
            element.style.backgroundSize = new StyleBackgroundSize(StyleKeyword.Null);
        }

        // Bakes the spec into an RGBA32 texture for a box of the given width over height. Pixel coordinates
        // use UV with (0,0) at the top-left so the gradient axis matches screen space (y grows downward) —
        // UI Toolkit draws background-image top-left-origin, so a ToBottom gradient runs from-color at the
        // top to to-color at the bottom.
        // widthPx is the box's width in pixels when the spec has a size written in pixels, else 0. resolution is
        // the texture's side, 0 for the default; a gradient along an axis bakes as one row or column of that
        // length, and the stretch to the box fills in the other direction.
        internal static Texture2D Bake(GradientSpec spec, float aspect, float widthPx = 0f, int resolution = 0)
        {
            var side = resolution > 0 ? resolution : DefaultResolution;
            var line = IsAxisLine(spec, out var horizontal);
            var width = line && !horizontal ? 1 : side;
            var height = line && horizontal ? 1 : side;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[width * height];
            var direction = LinearDirection(spec.AngleDeg);
            // A corner direction's lines run parallel to the box's diagonal, which the stretched texture
            // already keeps in any box, so it is laid out over a square.
            var lineAspect = spec.ToCorner ? 1f : aspect;
            var box = widthPx > 0f ? new Vector2(widthPx, widthPx / aspect) : new Vector2(aspect, 1f);
            var frame = new BakeFrame(box, lineAspect, direction, RadialRadii(spec, box.x, box.y));
            var stops = ResolveStops(spec, box.x, box.y);
            for (var row = 0; row < height; row++)
            {
                // Texture2D.SetPixels is bottom-up (row 0 = bottom); flip so row 0 is the TOP of the box.
                var v = height > 1 ? 1f - row / (float)(height - 1) : 0.5f;
                for (var col = 0; col < width; col++)
                {
                    var u = width > 1 ? col / (float)(width - 1) : 0.5f;
                    var t = ComputeT(spec, u, v, in frame);
                    pixels[(row * width) + col] = ColorAt(stops, t, spec.Type, spec.Interp, spec.Hue);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        // What a bake needs beyond the spec: the box (to scale, in pixels or in proportion), the aspect a
        // linear line is laid out over, its direction, and a radial's radii in the box's units.
        private readonly struct BakeFrame
        {
            public BakeFrame(Vector2 box, float lineAspect, Vector2 direction, Vector2 radii)
            {
                Box = box;
                LineAspect = lineAspect;
                Direction = direction;
                Radii = radii;
            }

            public Vector2 Box { get; }
            public float LineAspect { get; }
            public Vector2 Direction { get; }
            public Vector2 Radii { get; }
        }

        // The gradient parameter t at UV (u, v), for the spec's type: Linear projects onto the gradient
        // line, Radial is the elliptical distance from the centre over the radial's radii, Conic is the
        // clockwise angle from the centre (0° = up, matching CSS conic) minus the start angle, over 360°.
        private static float ComputeT(GradientSpec spec, float u, float v, in BakeFrame frame)
        {
            switch (spec.Type)
            {
                case GradientType.Radial:
                {
                    var nx = (u - spec.CenterX) * frame.Box.x / frame.Radii.x;
                    var ny = (v - spec.CenterY) * frame.Box.y / frame.Radii.y;
                    return Mathf.Sqrt((nx * nx) + (ny * ny));
                }
                case GradientType.Conic:
                {
                    // atan2(x, -y): 0° straight up, increasing clockwise in the y-down UV (CSS conic).
                    var ang = Mathf.Atan2((u - spec.CenterX) * frame.Box.x, -(v - spec.CenterY) * frame.Box.y) * Mathf.Rad2Deg;
                    return ((((ang - spec.AngleDeg) % 360f) + 360f) % 360f) / 360f;
                }
                default:
                    return (((u - 0.5f) * frame.LineAspect * frame.Direction.x) + ((v - 0.5f) * frame.Direction.y))
                        / ((Mathf.Abs(frame.Direction.x) * frame.LineAspect) + Mathf.Abs(frame.Direction.y)) + 0.5f;
            }
        }

        // The radii of a radial gradient in a width x height box, in the box's units: where CSS puts the
        // ending shape for its size. A circle has one radius and an ellipse two. The extent keywords measure
        // the centre's distance to the sides or corners; an ellipse keeps the proportions of its side
        // measure, and a corner extent scales them to pass through the corner. Shared with the skew
        // silhouette bake.
        internal static Vector2 RadialRadii(in GradientSpec spec, float width, float height)
        {
            var radial = spec.Radial;
            var left = Mathf.Abs(spec.CenterX * width);
            var right = Mathf.Abs((1f - spec.CenterX) * width);
            var top = Mathf.Abs(spec.CenterY * height);
            var bottom = Mathf.Abs((1f - spec.CenterY) * height);
            var nearX = Mathf.Min(left, right);
            var nearY = Mathf.Min(top, bottom);
            var farX = Mathf.Max(left, right);
            var farY = Mathf.Max(top, bottom);
            Vector2 radii;
            switch (radial.Extent)
            {
                case RadialExtent.Explicit:
                    radii = radial.Circle
                        ? new Vector2(radial.X, radial.X)
                        : new Vector2(radial.XPercent ? radial.X * width : radial.X, radial.YPercent ? radial.Y * height : radial.Y);
                    break;
                case RadialExtent.ClosestSide:
                    radii = radial.Circle ? Vector2.one * Mathf.Min(nearX, nearY) : new Vector2(nearX, nearY);
                    break;
                case RadialExtent.FarthestSide:
                    radii = radial.Circle ? Vector2.one * Mathf.Max(farX, farY) : new Vector2(farX, farY);
                    break;
                case RadialExtent.ClosestCorner:
                    radii = radial.Circle
                        ? Vector2.one * Mathf.Sqrt((nearX * nearX) + (nearY * nearY))
                        : new Vector2(nearX, nearY) * Mathf.Sqrt(2f);
                    break;
                default:
                    radii = radial.Circle
                        ? Vector2.one * Mathf.Sqrt((farX * farX) + (farY * farY))
                        : new Vector2(farX, farY) * Mathf.Sqrt(2f);
                    break;
            }
            return new Vector2(Mathf.Max(radii.x, 1e-4f), Mathf.Max(radii.y, 1e-4f));
        }

        // The unit direction a linear gradient runs in, for an angle in CSS degrees (0 = to top, clockwise),
        // in a box whose x points right and y down. Shared with the skew silhouette bake.
        internal static Vector2 LinearDirection(float angleDeg)
        {
            var rad = angleDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
        }

        // The stops of a gradient placed in a box: positions, and the position of a colour hint between a
        // stop and the next (NaN where there is none).
        internal sealed class ResolvedStops
        {
            public Color[] Colors = null!;
            public float[] Positions = null!;
            public float[] Hints = null!;
        }

        // The stops the walk reads for a width x height box. Stops written in pixels are placed by the
        // length of the gradient line (or a radial's ray) and the fix-up is run again over them.
        internal static ResolvedStops ResolveStops(in GradientSpec spec, float width, float height)
        {
            var stops = spec.Stops;
            if (spec.HasLengths)
            {
                var length = Mathf.Max(GradientLength(spec, width, height), 1e-4f);
                var placed = new GradientStop[stops.Length];
                for (var i = 0; i < stops.Length; i++)
                {
                    var stop = stops[i];
                    placed[i] = new GradientStop(stop.Color,
                        float.IsNaN(stop.PositionPx) ? stop.Position : stop.PositionPx / length,
                        float.NaN,
                        float.IsNaN(stop.HintPx) ? stop.Hint : stop.HintPx / length);
                }
                stops = StyleGradientClass.FixUp(placed);
            }
            var resolved = new ResolvedStops
            {
                Colors = new Color[stops.Length],
                Positions = new float[stops.Length],
                Hints = new float[stops.Length],
            };
            for (var i = 0; i < stops.Length; i++)
            {
                resolved.Colors[i] = stops[i].Color;
                resolved.Positions[i] = stops[i].Position;
                resolved.Hints[i] = stops[i].Hint;
            }
            return resolved;
        }

        // The length in pixels of what a stop position of 100% is: a linear gradient's line, a radial's
        // ray along x; a conic has no length.
        private static float GradientLength(in GradientSpec spec, float width, float height)
        {
            switch (spec.Type)
            {
                case GradientType.Radial:
                    return RadialRadii(spec, width, height).x;
                case GradientType.Conic:
                    return 1f;
                default:
                    var direction = LinearDirection(spec.AngleDeg);
                    if (spec.ToCorner)
                    {
                        direction = new Vector2(Mathf.Sign(direction.x) * height, Mathf.Sign(direction.y) * width).normalized;
                    }
                    return (Mathf.Abs(direction.x) * width) + (Mathf.Abs(direction.y) * height);
            }
        }

        // Colour at axis parameter t, honoring the stop POSITIONS: the first colour before the first stop,
        // otherwise a linear interpolation from the last stop at or before t to the next one, and the last
        // colour once none follows. A colour hint between the two moves the midpoint of the interpolation
        // to where it was written. t is held inside [0, MaxParameter] (a radial's has no upper bound), which
        // settles a stop at the very start or end of the line the way CSS does: where stops share a position
        // the later one starts there, and a hard stop at 100% of a box-long line never shows its later
        // colour. The skew silhouette shader evaluates the same walk.
        private static Color ColorAt(ResolvedStops stops, float t, GradientType type, GradientInterp interp, HueMethod method)
        {
            t = type == GradientType.Radial ? Mathf.Max(t, 0f) : Mathf.Clamp(t, 0f, MaxParameter);
            var positions = stops.Positions;
            if (t < positions[0])
            {
                return stops.Colors[0];
            }
            var i = 1;
            while (i < positions.Length && t >= positions[i])
            {
                i++;
            }
            if (i == positions.Length)
            {
                return stops.Colors[i - 1];
            }
            var span = positions[i] - positions[i - 1];
            var weight = ApplyHint((t - positions[i - 1]) / span, stops.Hints[i - 1], positions[i - 1], span);
            return Lerp(stops.Colors[i - 1], stops.Colors[i], weight, interp, method);
        }

        // The interpolation weight with the colour hint written between two stops: the weight is raised to the
        // power that puts the half-way mix at the hint, a hint at the first stop is an instant change and a
        // hint at or past the second stop holds the first colour.
        private static float ApplyHint(float weight, float hint, float start, float span)
        {
            if (float.IsNaN(hint))
            {
                return weight;
            }
            var at = (hint - start) / span;
            if (at <= 0f)
            {
                return 1f;
            }
            return at >= 1f ? 0f : Mathf.Pow(weight, Mathf.Log(0.5f) / Mathf.Log(at));
        }

        // Lerps two stops in the gradient's interpolation space, with the colour components weighted by alpha
        // as CSS interpolates, so a stop fading to transparent keeps its colour instead of darkening toward
        // the transparent colour's channels. A polar space's hue is not weighted and is interpolated along
        // the arc the hue method picks; a colour with no chroma has no hue of its own and takes the other's.
        // Where the result is fully transparent its colour is unweighted, which nothing paints.
        private static Color Lerp(Color a, Color b, float t, GradientInterp interp, HueMethod method)
        {
            var ca = ToSpace(a, interp);
            var cb = ToSpace(b, interp);
            var alpha = Mathf.Lerp(a.a, b.a, t);
            var mixed = alpha > 0f
                ? Vector3.Lerp(ca * a.a, cb * b.a, t) / alpha
                : Vector3.Lerp(ca, cb, t);
            if (GradientSpec.IsPolarSpace(interp))
            {
                // The hue is the third component in every polar space and is interpolated on its own.
                mixed.z = LerpHue(ca, cb, t, interp, method);
            }
            return FromSpace(mixed, alpha, interp);
        }

        // Whether a colour in a polar space has no hue: its chroma (saturation for hsl) is nil.
        private static bool HueMissing(Vector3 c, GradientInterp interp)
        {
            switch (interp)
            {
                case GradientInterp.Hsl: return c.x < 1e-4f;
                case GradientInterp.Lch: return c.y < 0.01f;
                default: return c.y < 2e-4f;
            }
        }

        // Interpolates the hue of two colours along the arc the method names, in degrees in [0, 360).
        private static float LerpHue(Vector3 ca, Vector3 cb, float t, GradientInterp interp, HueMethod method)
        {
            var missingA = HueMissing(ca, interp);
            var missingB = HueMissing(cb, interp);
            var from = missingA ? (missingB ? 0f : cb.z) : ca.z;
            var to = missingB ? (missingA ? 0f : ca.z) : cb.z;
            var delta = to - from;
            switch (method)
            {
                case HueMethod.Longer:
                    if (delta > 0f && delta < 180f)
                    {
                        from += 360f;
                    }
                    else if (delta > -180f && delta <= 0f)
                    {
                        to += 360f;
                    }
                    break;
                case HueMethod.Increasing:
                    if (delta < 0f)
                    {
                        to += 360f;
                    }
                    break;
                case HueMethod.Decreasing:
                    if (delta > 0f)
                    {
                        from += 360f;
                    }
                    break;
                default:
                    if (delta > 180f)
                    {
                        from += 360f;
                    }
                    else if (delta < -180f)
                    {
                        to += 360f;
                    }
                    break;
            }
            return Mathf.Repeat(Mathf.LerpUnclamped(from, to, t), 360f);
        }

        // sRGB Color → the interpolation space's three components. A polar space is (c0, c1, hue in degrees):
        // (lightness, chroma, hue) for lch and oklch, (saturation, lightness, hue) for hsl.
        private static Vector3 ToSpace(Color c, GradientInterp interp)
        {
            switch (interp)
            {
                case GradientInterp.SrgbLinear:
                    return new Vector3(SrgbToLinear(c.r), SrgbToLinear(c.g), SrgbToLinear(c.b));
                case GradientInterp.Oklab:
                    return ToOklab(c);
                case GradientInterp.Oklch:
                    return ToPolar(ToOklab(c));
                case GradientInterp.Lab:
                    return ToLab(c);
                case GradientInterp.Lch:
                    return ToPolar(ToLab(c));
                case GradientInterp.Hsl:
                    return ToHsl(c);
                default:
                    return new Vector3(c.r, c.g, c.b);
            }
        }

        // The interpolation space's components → an sRGB Color, clipped to the gamut.
        private static Color FromSpace(Vector3 v, float alpha, GradientInterp interp)
        {
            Vector3 rgb;
            switch (interp)
            {
                case GradientInterp.SrgbLinear:
                    rgb = new Vector3(LinearToSrgb(v.x), LinearToSrgb(v.y), LinearToSrgb(v.z));
                    break;
                case GradientInterp.Oklab:
                    rgb = FromOklab(v);
                    break;
                case GradientInterp.Oklch:
                    rgb = FromOklab(FromPolar(v));
                    break;
                case GradientInterp.Lab:
                    rgb = FromLab(v);
                    break;
                case GradientInterp.Lch:
                    rgb = FromLab(FromPolar(v));
                    break;
                case GradientInterp.Hsl:
                    rgb = FromHsl(v);
                    break;
                default:
                    rgb = v;
                    break;
            }
            return new Color(Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z), alpha);
        }

        // (a, b) → (chroma, hue in degrees); the first component stays.
        private static Vector3 ToPolar(Vector3 lab)
            => new(lab.x, Mathf.Sqrt((lab.y * lab.y) + (lab.z * lab.z)),
                Mathf.Repeat(Mathf.Atan2(lab.z, lab.y) * Mathf.Rad2Deg, 360f));

        private static Vector3 FromPolar(Vector3 lch)
            => new(lch.x, lch.y * Mathf.Cos(lch.z * Mathf.Deg2Rad), lch.y * Mathf.Sin(lch.z * Mathf.Deg2Rad));

        // sRGB Color → OKLab, per Björn Ottosson's matrices (operating on LINEAR rgb).
        private static Vector3 ToOklab(Color c)
        {
            float lr = SrgbToLinear(c.r), lg = SrgbToLinear(c.g), lb = SrgbToLinear(c.b);
            var l = (0.4122214708f * lr) + (0.5363325363f * lg) + (0.0514459929f * lb);
            var m = (0.2119034982f * lr) + (0.6806995451f * lg) + (0.1073969566f * lb);
            var s = (0.0883024619f * lr) + (0.2817188376f * lg) + (0.6299787005f * lb);
            float l_ = Cbrt(l), m_ = Cbrt(m), s_ = Cbrt(s);
            return new Vector3(
                (0.2104542553f * l_) + (0.7936177850f * m_) - (0.0040720468f * s_),
                (1.9779984951f * l_) - (2.4285922050f * m_) + (0.4505937099f * s_),
                (0.0259040371f * l_) + (0.7827717662f * m_) - (0.8086757660f * s_));
        }

        // OKLab → sRGB components, unclipped.
        private static Vector3 FromOklab(Vector3 lab)
        {
            var l_ = lab.x + (0.3963377774f * lab.y) + (0.2158037573f * lab.z);
            var m_ = lab.x - (0.1055613458f * lab.y) - (0.0638541728f * lab.z);
            var s_ = lab.x - (0.0894841775f * lab.y) - (1.2914855480f * lab.z);
            float l = l_ * l_ * l_, m = m_ * m_ * m_, s = s_ * s_ * s_;
            var lr = (4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s);
            var lg = (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s);
            var lb = (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s);
            return new Vector3(LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
        }

        // CIE Lab with a D50 white, as CSS Color 4 defines lab(): linear sRGB through the Bradford-adapted
        // matrix to XYZ. The constants match the shader's copy.
        private static Vector3 ToLab(Color c)
        {
            float lr = SrgbToLinear(c.r), lg = SrgbToLinear(c.g), lb = SrgbToLinear(c.b);
            var x = ((0.4360747f * lr) + (0.3850649f * lg) + (0.1430804f * lb)) / 0.9642957f;
            var y = (0.2225045f * lr) + (0.7168786f * lg) + (0.0606169f * lb);
            var z = ((0.0139322f * lr) + (0.0971045f * lg) + (0.7141733f * lb)) / 0.8251046f;
            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new Vector3((116f * fy) - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static Vector3 FromLab(Vector3 lab)
        {
            var fy = (lab.x + 16f) / 116f;
            var fx = (lab.y / 500f) + fy;
            var fz = fy - (lab.z / 200f);
            var x = LabFInverse(fx) * 0.9642957f;
            var y = lab.x > 8f ? fy * fy * fy : lab.x / 903.2963f;
            var z = LabFInverse(fz) * 0.8251046f;
            var lr = (3.1338561f * x) - (1.6168667f * y) - (0.4906146f * z);
            var lg = (-0.9787684f * x) + (1.9161415f * y) + (0.0334540f * z);
            var lb = (0.0719453f * x) - (0.2289914f * y) + (1.4052427f * z);
            return new Vector3(LinearToSrgb(lr), LinearToSrgb(lg), LinearToSrgb(lb));
        }

        private static float LabF(float t) => t > 0.008856452f ? Cbrt(t) : ((903.2963f * t) + 16f) / 116f;

        private static float LabFInverse(float f)
        {
            var cubed = f * f * f;
            return cubed > 0.008856452f ? cubed : ((116f * f) - 16f) / 903.2963f;
        }

        // sRGB → (saturation, lightness, hue in degrees).
        private static Vector3 ToHsl(Color c)
        {
            var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            var min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            var d = max - min;
            var l = (max + min) * 0.5f;
            var s = d < 1e-6f ? 0f : d / (1f - Mathf.Abs((2f * l) - 1f));
            float h;
            if (d < 1e-6f)
            {
                h = 0f;
            }
            else if (max == c.r)
            {
                h = Mathf.Repeat((c.g - c.b) / d, 6f) * 60f;
            }
            else if (max == c.g)
            {
                h = (((c.b - c.r) / d) + 2f) * 60f;
            }
            else
            {
                h = (((c.r - c.g) / d) + 4f) * 60f;
            }
            return new Vector3(s, l, h);
        }

        private static Vector3 FromHsl(Vector3 v)
        {
            var chroma = (1f - Mathf.Abs((2f * v.y) - 1f)) * v.x;
            var h = Mathf.Repeat(v.z, 360f) / 60f;
            var x = chroma * (1f - Mathf.Abs(Mathf.Repeat(h, 2f) - 1f));
            var m = v.y - (chroma * 0.5f);
            Vector3 rgb;
            if (h < 1f)
            {
                rgb = new Vector3(chroma, x, 0f);
            }
            else if (h < 2f)
            {
                rgb = new Vector3(x, chroma, 0f);
            }
            else if (h < 3f)
            {
                rgb = new Vector3(0f, chroma, x);
            }
            else if (h < 4f)
            {
                rgb = new Vector3(0f, x, chroma);
            }
            else if (h < 5f)
            {
                rgb = new Vector3(x, 0f, chroma);
            }
            else
            {
                rgb = new Vector3(chroma, 0f, x);
            }
            return rgb + new Vector3(m, m, m);
        }

        // The exact IEC 61966-2-1 sRGB transfer, hand-rolled (NOT Color.linear / Mathf.GammaToLinearSpace)
        // on purpose: the spaces here are defined on this specific curve regardless of the project's active
        // color space, and these constants must match the shader's HLSL copy bit-for-bit so the skew and
        // non-skew bakes agree.
        private static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        private static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : (1.055f * Mathf.Pow(c, 1f / 2.4f)) - 0.055f;
        private static float Cbrt(float x) => x < 0f ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f);
    }
}
