using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one shadowed element, keyed in ReconcilerContext.ShadowBindings by
    // the element itself (a drop shadow needs NO structural wrapper — the baked shadow texture is painted by
    // the element's own generateVisualContent, BEHIND its content and bleeding outside the box, matching CSS
    // box-shadow as a non-structural paint). Holds the resolved spec (color/blur/offset/spread), the corner
    // radius and skew the bake follows, and the registered callbacks (so they can be unregistered on detach).
    internal sealed class DropShadowBinding
    {
        public ShadowSpec Spec;
        // The class list this element carries, kept current so the geometry callback can re-resolve the
        // rounded-* corner radius after a class change.
        public string[]? ClassNames;
        // The caster's skew-x angle (degrees; 0 = upright). A skewed caster's shadow follows the slant.
        public float SkewXDeg;
        // Whether the caster is skewed on ANY axis (skew-x or skew-y). A skewed caster's SkewSilhouette paints
        // the sheared face over the whole (sheared) shadow quad; an upright caster's shadow is cut out of its
        // border box instead, so the native background and border paint over nothing of it.
        public bool CasterSkewed;
        // The corner radius the bake rounds to. Resolved from rounded-* off-panel and from the laid-out
        // resolvedStyle.borderTopLeftRadius once on a panel — the same precedence the wrapper path used.
        public float CornerRadius;

        // When the caster carries an inline filter, the shadow bleed (painted outside the box) would be
        // clipped to the layout rect by the filter's offscreen render tree. A transparent last-child spacer
        // sized to the shadow's extent widens boundingBox so the bleed survives. WantSpacer is decided by the
        // reconciler from the class list; BoundsSpacer is the child. See SilhouetteBoundsSpacer.
        public bool WantSpacer;
        public VisualElement? BoundsSpacer;
        // The caster's left/top border, parsed from the class list, to shift the spacer origin into padding-box
        // space (see SilhouetteBoundsSpacer.BorderInset).
        public float BorderLeft;
        public float BorderTop;

        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;
    }

    // The four values the reconciler resolves together for one patch; each means what the like-named
    // DropShadowBinding field means.
    internal readonly struct DropShadowSyncRequest
    {
        public ShadowSpec Spec { get; init; }
        public string[] ClassNames { get; init; }
        public float SkewXDeg { get; init; }
        public bool CasterSkewed { get; init; }
    }

    /// <summary>
    /// Paints a shadowed element's drop shadow — a baked, SDF-antialiased silhouette drawn as a single quad
    /// BEHIND the element's content in its own generateVisualContent. This is the <c>shadow-*</c> /
    /// <c>drop-shadow-*</c> parity mechanism: UI Toolkit (6000.3) has no <c>box-shadow</c>, and a CSS box-shadow
    /// is a non-structural paint (it does not change layout and it follows a transform on the element), so the
    /// shadow is painted in the caster's own content rather than hosted in a structural wrapper (a wrapper
    /// altered flex/grid sizing and did not follow the caster's transform).
    /// </summary>
    /// <remarks>
    /// The baked texture is the FULL soft silhouette, interior opaque. CSS paints a box-shadow only outside the
    /// border box, so for an UPRIGHT caster the quad is drawn with the caster's rounded border box cut out of
    /// it, and the element's native background and border paint as they would without a shadow — a colour a
    /// variant, a gesture class or a play changes shows at once, and a translucent background shows no shadow
    /// through it. A SKEWED caster's <see cref="SkewSilhouette"/> paints the sheared face over the whole quad,
    /// so the quad is drawn uncut and fires BEFORE the skew face. The shadow color is the quad's vertex tint,
    /// so a color change retints with no re-bake. The bake + size-keyed cache live in
    /// <see cref="DropShadowBaker"/>.
    /// </remarks>
    internal static class DropShadowSilhouette
    {
        // Lets the animation scheduler find a shadow paint binding on any element of an animating subtree (the
        // shadow is the caster's own paint, not a separate child element it could query for). Auto-drops
        // entries when an element is GC'd; Detach removes the entry eagerly so a pooled element cannot ghost a
        // prior consumer's shadow. Mirrors StyleArbitraryValueResolver's per-element side-channel.
        private static readonly ConditionalWeakTable<VisualElement, DropShadowBinding> s_byElement = new();

        // Wires the paint callbacks onto the element and returns the binding. casterSkewed: whether a
        // SkewSilhouette owns this element's face (any skew axis) — resolved by the reconciler, which reconciles
        // skew before the shadow. The callback is PREPENDED so the quad fires before a skewed caster's
        // SkewSilhouette face (attached BEFORE this).
        public static DropShadowBinding Attach(VisualElement element, ShadowSpec spec, string[] classNames,
            float skewXDeg, bool casterSkewed = false)
        {
            var binding = new DropShadowBinding
            {
                Spec = spec,
                ClassNames = classNames,
                SkewXDeg = skewXDeg,
                CasterSkewed = casterSkewed,
            };
            ResolveCornerRadius(element, binding);

            binding.OnGenerate = mgc => Draw(mgc, element, binding);
            element.generateVisualContent = binding.OnGenerate + element.generateVisualContent;

            // Re-resolve the radius once layout gives the resolved border radius, and repaint at the real size.
            binding.OnGeometryChanged = _ =>
            {
                ResolveCornerRadius(element, binding);
                // Size the filter bounds-spacer to the now-known shadow extent (no-op when not wanted).
                SyncBoundsSpacer(element, binding);
                element.MarkDirtyRepaint();
            };
            element.RegisterCallback(binding.OnGeometryChanged);

            s_byElement.AddOrUpdate(element, binding);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the callbacks and drops the side-channel entry so a pooled element cannot ghost a
        // prior consumer's shadow. The baked textures are cached process-wide and are NOT destroyed here.
        public static void Detach(VisualElement element, DropShadowBinding binding)
        {
            element.generateVisualContent -= binding.OnGenerate;
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
            }
            SilhouetteBoundsSpacer.Remove(element, ref binding.BoundsSpacer);
            s_byElement.Remove(element);
            element.MarkDirtyRepaint();
        }

        // Records whether the caster carries a filter (so its shadow bleed needs the bounds-spacer to survive
        // the filter's offscreen render tree) and syncs the spacer now. Called from the reconciler on create /
        // patch; the geometry callback re-syncs when the size / radius settles.
        public static void SetWantSpacer(VisualElement element, DropShadowBinding binding, bool want, string[] classNames)
        {
            binding.WantSpacer = want;
            SilhouetteBoundsSpacer.BorderInset(classNames, out binding.BorderLeft, out binding.BorderTop);
            SyncBoundsSpacer(element, binding);
        }

        // Sizes (or removes) the bounds-spacer to the shadow's extent: the baked quad (box grown by
        // blur + ExtraPadding per side, shifted by the offset) unioned with the box, and — for a skewed caster,
        // whose shadow shears with it — grown by the shear overhang. Empty AABB until layout gives a size.
        private static void SyncBoundsSpacer(VisualElement element, DropShadowBinding binding)
        {
            var aabb = default(Rect);
            if (SilhouetteBoundsSpacer.TryGetLayoutSize(element, out var w, out var h))
            {
                var spec = binding.Spec;
                var pad = DropShadowBaker.QuantizePx(spec.Blur) + DropShadowBaker.ExtraPadding
                    + DropShadowBaker.QuantizePx(spec.Spread);
                var quad = new Rect(-pad + spec.OffsetX, -pad + spec.OffsetY, w + (2f * pad), h + (2f * pad));
                aabb = SilhouetteBoundsSpacer.Union(new Rect(0f, 0f, w, h), quad);
                if (binding.CasterSkewed)
                {
                    var tanX = Mathf.Tan(binding.SkewXDeg * Mathf.Deg2Rad);
                    aabb = SilhouetteBoundsSpacer.ExpandForShear(aabb, tanX, 0f);
                }
            }
            aabb = SilhouetteBoundsSpacer.ShiftToPaddingBox(aabb, binding.BorderLeft, binding.BorderTop);
            SilhouetteBoundsSpacer.Sync(element, ref binding.BoundsSpacer, binding.WantSpacer, aabb);
        }

        // Re-applies the spec / skew of an already-attached element after a patch, refreshing the radius from
        // the new class list (and the laid-out border radius when on a panel). Repaints so a preset / color /
        // skew change shows without waiting for a geometry event (a skew angle change fires none).
        public static void Sync(VisualElement element, DropShadowBinding binding, in DropShadowSyncRequest request)
        {
            binding.Spec = request.Spec;
            binding.ClassNames = request.ClassNames;
            binding.SkewXDeg = request.SkewXDeg;
            binding.CasterSkewed = request.CasterSkewed;
            ResolveCornerRadius(element, binding);
            element.MarkDirtyRepaint();
        }

        // Returns the live binding for an element, or null. Used by the animation scheduler to collect the
        // shadow paints under an animating subtree.
        public static DropShadowBinding? TryGet(VisualElement element)
            => s_byElement.TryGetValue(element, out var binding) ? binding : null;

        // Radius prefers the laid-out resolvedStyle.borderTopLeftRadius (handles %, arbitrary, and inline
        // radii) once on a panel, and falls back to the rounded-* class scale off-panel / pre-layout so a
        // value is always set.
        private static void ResolveCornerRadius(VisualElement element, DropShadowBinding binding)
        {
            var resolved = element.resolvedStyle.borderTopLeftRadius;
            if (element.panel != null && !float.IsNaN(resolved))
            {
                binding.CornerRadius = resolved;
            }
            else if (StyleShadowClass.TryResolveCornerRadius(binding.ClassNames, out var classRadius))
            {
                binding.CornerRadius = classRadius;
            }
            else
            {
                binding.CornerRadius = 0f;
            }
        }

        // Paints the drop shadow in the caster's own generateVisualContent: the baked silhouette quad — the
        // caster box grown by pad = blur + ExtraPadding per side and shifted by the shadow offset, so the soft
        // edge bleeds outside the box. Skipped before layout gives a real size.
        private static void Draw(MeshGenerationContext mgc, VisualElement ve, DropShadowBinding binding)
        {
            var w = ve.layout.width;
            var h = ve.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            DrawShadowQuad(mgc, ve, binding, w, h);
        }

        // Draws the baked shadow silhouette quad. Returns early if the shadow color is transparent or the bake
        // is not yet available (no graphics device / shader) — the geometry callback retries.
        //
        // A fading caster needs no correction here, and a multiplier would apply the caster's opacity a SECOND
        // time and land the shadow at opacity squared; one existed and was removed. What makes that so is the
        // renderer behaviour pinned by PaintOpacityParityPlaybackTests, with its consequence on this paint
        // pinned by ShadowFadeOpacityPlaybackTests — so restoring a multiplier means refuting those rather
        // than re-deriving a measurement.
        private static void DrawShadowQuad(MeshGenerationContext mgc, VisualElement ve, DropShadowBinding binding,
            float w, float h)
        {
            var spec = binding.Spec;
            var alpha = spec.Color.a;
            if (alpha <= 0.004f)
            {
                return;
            }

            // Bounded as the face painter bounds a corner. RoundedFullSilhouettePlaybackTests pins the halo an
            // unbounded --radius-full bakes away.
            var corner = Mathf.Min(binding.CornerRadius, SilhouetteFace.MaxRadius(w, h));
            var tex = DropShadowBaker.GetOrBakeSilhouette(corner, spec.Blur, spec.Spread,
                w, h, binding.SkewXDeg);
            if (tex == null)
            {
                return; // no graphics device / shader yet — retry on the next geometry change
            }

            // Quantize the blur to whole pixels for the anchor, exactly as QuadSize / the bake do, so the
            // quad's top-left lines up with the baked texel grid (a non-integer arbitrary shadow-[…] blur would
            // otherwise offset the quad up to ~0.5px from the texture).
            var pad = DropShadowBaker.QuantizePx(spec.Blur) + DropShadowBaker.ExtraPadding;
            DropShadowBaker.QuadSize(w, h, spec.Blur, out var quadW, out var quadH);
            // Anchor the quad so its solid core sits over the box: top-left at (-pad + offsetX, -pad + offsetY)
            // and size (w + 2pad, h + 2pad) — clamped to the baked texel size so one texel maps to one pixel.
            var minX = -pad + spec.OffsetX;
            var minY = -pad + spec.OffsetY;
            var maxX = minX + quadW;
            var maxY = minY + quadH;

            // UVs are raw 0..1 (UI Toolkit remaps them into the atlas slot); the texture's alpha carries the
            // SDF antialiasing. texUV.y is flipped (box top samples v = 1) to match the bake orientation and
            // SkewSilhouette's gradient quad, so a skewed caster's shadow slants the same direction on screen.
            var tint = spec.Color;
            tint.a = alpha;
            var quad = new Rect(minX, minY, maxX - minX, maxY - minY);
            s_mesh.Clear();
            if (binding.CasterSkewed || !TryCutBorderBox(ve, w, h, quad))
            {
                s_mesh.AddPolygon(s_rect.Fill(quad));
            }
            if (s_mesh.Vertices.Count == 0)
            {
                return;
            }
            var mwd = mgc.Allocate(s_mesh.Vertices.Count, s_mesh.Indices.Count, tex);
            foreach (var position in s_mesh.Vertices)
            {
                var uv = new Vector2((position.x - quad.xMin) / quad.width, 1f - ((position.y - quad.yMin) / quad.height));
                mwd.SetNextVertex(new Vertex { position = new Vector3(position.x, position.y, Vertex.nearZ), tint = tint, uv = uv });
            }
            foreach (var index in s_mesh.Indices)
            {
                mwd.SetNextIndex(index);
            }
        }

        // The quad less the caster's rounded border box, as CSS clips a box-shadow: the rect decomposes into
        // the pieces beyond each edge of the (convex) box outline that lie within every edge before it, each
        // convex and so fanned directly. False when the outline is degenerate, which leaves the quad uncut.
        private static bool TryCutBorderBox(VisualElement ve, float w, float h, Rect quad)
        {
            SilhouetteFace.BuildShearedRoundedRectPolyline(s_outline,
                ShearedRoundedRect.ForElement(ve, 0f, w, h, 0f, 0f));
            DropRepeatedPoints(s_outline);
            var area = 0f;
            for (var i = 0; i < s_outline.Count; i++)
            {
                var a = s_outline[i];
                var b = s_outline[(i + 1) % s_outline.Count];
                area += (a.x * b.y) - (b.x * a.y);
            }
            if (s_outline.Count < 3 || Mathf.Abs(area) < 1e-3f)
            {
                return false;
            }
            var inward = Mathf.Sign(area);
            var remaining = s_rect.Fill(quad);
            for (var i = 0; i < s_outline.Count && remaining.Count >= 3; i++)
            {
                var a = s_outline[i];
                var b = s_outline[(i + 1) % s_outline.Count];
                s_mesh.AddPolygon(ConvexClip.Keep(remaining, s_piece, a, b, -inward));
                remaining = ConvexClip.Keep(remaining, s_scratch, a, b, inward);
                (s_scratch, s_remaining) = (s_remaining, s_scratch);
                remaining = s_remaining;
            }
            return true;
        }

        private static void DropRepeatedPoints(List<Vector2> points)
        {
            for (var i = points.Count - 1; i >= 0 && points.Count > 1; i--)
            {
                var next = points[(i + 1) % points.Count];
                if ((points[i] - next).sqrMagnitude < 1e-6f)
                {
                    points.RemoveAt(i);
                }
            }
        }

        // Scratch for the paint, which UI Toolkit runs on the main thread only.
        private static readonly List<Vector2> s_outline = new();
        private static readonly List<Vector2> s_piece = new();
        private static List<Vector2> s_scratch = new();
        private static List<Vector2> s_remaining = new();
        private static readonly RectPolygon s_rect = new();
        private static readonly FanMesh s_mesh = new();

        private sealed class RectPolygon
        {
            private readonly List<Vector2> _points = new(4);

            public List<Vector2> Fill(Rect rect)
            {
                _points.Clear();
                _points.Add(new Vector2(rect.xMin, rect.yMin));
                _points.Add(new Vector2(rect.xMax, rect.yMin));
                _points.Add(new Vector2(rect.xMax, rect.yMax));
                _points.Add(new Vector2(rect.xMin, rect.yMax));
                return _points;
            }
        }

        // Triangles fanned from each convex polygon added, in one vertex and index list.
        private sealed class FanMesh
        {
            public readonly List<Vector2> Vertices = new();
            public readonly List<ushort> Indices = new();

            public void Clear()
            {
                Vertices.Clear();
                Indices.Clear();
            }

            public void AddPolygon(List<Vector2> polygon)
            {
                if (polygon.Count < 3)
                {
                    return;
                }
                var first = Vertices.Count;
                Vertices.AddRange(polygon);
                for (var i = 1; i < polygon.Count - 1; i++)
                {
                    Indices.Add((ushort)first);
                    Indices.Add((ushort)(first + i));
                    Indices.Add((ushort)(first + i + 1));
                }
            }
        }
    }

    // Sutherland-Hodgman clipping of a convex polygon against the line through a and b.
    internal static class ConvexClip
    {
        // Writes into output the part of input on the side of a→b where side × cross(b − a, p − a) ≥ 0, and
        // returns output.
        public static List<Vector2> Keep(List<Vector2> input, List<Vector2> output, Vector2 a, Vector2 b,
            float side)
        {
            output.Clear();
            var n = input.Count;
            for (var i = 0; i < n; i++)
            {
                var current = input[i];
                var previous = input[(i + n - 1) % n];
                var dc = side * Cross(a, b, current);
                var dp = side * Cross(a, b, previous);
                if (dc >= 0f)
                {
                    if (dp < 0f)
                    {
                        output.Add(Intersect(previous, current, dp, dc));
                    }
                    output.Add(current);
                }
                else if (dp >= 0f)
                {
                    output.Add(Intersect(previous, current, dp, dc));
                }
            }
            return output;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p)
            => ((b.x - a.x) * (p.y - a.y)) - ((b.y - a.y) * (p.x - a.x));

        private static Vector2 Intersect(Vector2 from, Vector2 to, float dFrom, float dTo)
            => from + ((to - from) * (dFrom / (dFrom - dTo)));
    }
}
