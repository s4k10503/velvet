using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one skewed element, keyed in ReconcilerContext.SkewBindings
    // by the element itself (skew needs NO structural wrapper — the silhouette is painted by the
    // element's own generateVisualContent). Holds the resolved spec, the registered callbacks (so
    // they can be unregistered on detach), and the stashed face colors: the native rectangular
    // background / border are suppressed with a sentinel color and re-painted sheared, so their
    // effective colors must be captured before suppression.
    internal sealed class SkewBinding
    {
        public SkewSpec Spec;
        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;

        // The native-face stash + sentinel suppression shared with the drop-shadow layer: the rectangular
        // background / border are suppressed with a sentinel color and re-painted sheared, so their effective
        // colors must be captured before suppression. Border uses the LEFT side (the sheared outline is a
        // single uniform stroke, so per-side colors/widths collapse to one).
        public readonly SilhouetteFaceStash Face = new();

        // Pass-throughs onto Face so the reconciler / tests read the stash state directly off the binding.
        public Color BgColor => Face.BgColor;
        public Color BorderColor => Face.BorderColor;
        public bool HasStash => Face.HasStash;
        public bool SuppressionApplied => Face.SuppressionApplied;

        // The gradient fill, when a bg-gradient-* class is present alongside skew-*. When HasGradient, Draw
        // paints the sheared shape with this gradient instead of the solid BgColor — the skew + gradient
        // composition a rectangular background-image cannot achieve (it cannot follow the shear). The
        // reconciler feeds the spec from the SAME class list the non-skew gradient path reads, and that path
        // defers here whenever the element is skewed, so the gradient renders exactly once.
        public GradientSpec Gradient;
        public bool HasGradient;

        // The baked silhouette: the sheared, rounded, SDF-antialiased gradient fill rendered by the
        // Velvet/GradientSilhouette shader at the element's sheared bounding-box size. Owned by this binding
        // — re-baked when the size / skew / spec change, destroyed on Detach. Draw samples it on a
        // bounding-box quad. Null until the first bake (pre-layout, or no graphics device / shader).
        public Texture2D? GradientTex;
        // Quantized (w, h, skewX·100, skewY·100, tl, tr, br, bl radii) the current GradientTex was baked at,
        // to skip a redundant re-bake; GradBaked gates it (SetGradient clears it so a spec change re-bakes).
        public (int w, int h, int sx, int sy, int tl, int tr, int br, int bl) GradKey;
        public bool GradBaked;
    }

    /// <summary>
    /// Paints a skewed element's face — the sheared rounded-rect silhouette (fill + an outline that FOLLOWS
    /// the shear) drawn via Painter2D in the element's own generateVisualContent. This is the
    /// <c>skew-x-*</c> / <c>skew-y-*</c> parity mechanism: UI Toolkit's transform has no shear and its stencil
    /// mask (the clip-path path) is binary — jagged diagonals, borders cut off — while painted vector geometry
    /// antialiases and keeps its outline.
    /// </summary>
    /// <remarks>
    /// The native rectangular background/border colors are suppressed with a near-invisible sentinel (the
    /// inline slot is shared with <see cref="StyleArbitraryValueResolver"/>'s <c>bg-[…]</c> / <c>border-[…]</c>
    /// writes, so suppression re-syncs on every patch: a fresh resolver value re-stashes directly; a USS-driven
    /// stash releases the sentinel on a class change and re-stashes on the next style resolution, while an
    /// inline-driven stash is kept — an inline color only changes through a resolver write, which the re-stash
    /// branch already catches).
    /// <para>
    /// Caveat — this paints ONLY the element's own silhouette; it is NOT a real transform, so the element's
    /// layout box and its children stay axis-aligned and do NOT follow the lean (unlike CSS
    /// <c>transform: skewX()</c>, which shears descendants too). UI Toolkit has no shear transform — only
    /// position/rotation/scale — so children must be offset manually (e.g. a per-row <c>translate-x</c> by the
    /// frame's centre-based shear) to seat them inside a slanted frame.
    /// </para>
    /// </remarks>
    internal static class SkewSilhouette
    {
        // The suppression sentinel + bit-exact tests live in SilhouetteFace (shared with the drop-shadow layer);
        // forwarded here so existing call-sites / tests keep reading them off SkewSilhouette.
        internal static Color SuppressedColor => SilhouetteFace.SuppressedColor;

        internal static bool IsSentinel(Color c) => SilhouetteFace.IsSentinel(c);

        // Wires the paint + stash callbacks onto the element and returns the binding. The first stash
        // (capture the face colors, then suppress the native rect chrome) MUST land before the first paint:
        // otherwise the un-suppressed rectangle shows through the sheared silhouette as a DOUBLE image — a
        // straight rect ghost behind the slant — until the first patch happens to run SyncStashOnPatch (e.g.
        // a click). So the stash is driven, in order of reliability: a synchronous attempt here, then a
        // GeometryChangedEvent (fires in EVERY host once layout settles), then CustomStyleResolvedEvent (only
        // fires when the element has --var custom props, so unreliable alone), then SyncStashOnPatch on patch.
        public static SkewBinding Attach(VisualElement element, SkewSpec spec)
        {
            var binding = new SkewBinding { Spec = spec };
            binding.OnGenerate = mgc => Draw(mgc, element, binding);
            // TextElement (Button, Label) registers its text-rendering callback at construction, before
            // this Attach call. In UITK's content phase, generateVisualContent callbacks fire in
            // registration order — later callbacks paint over earlier ones. Appending (+=) would place
            // the silhouette fill AFTER the text, covering it. Prepending keeps the sheared background
            // behind the text so the label remains readable on skewed buttons.
            if (element is TextElement)
            {
                element.generateVisualContent = binding.OnGenerate + element.generateVisualContent;
            }
            else
            {
                element.generateVisualContent += binding.OnGenerate;
            }
            binding.OnStyleResolved = _ =>
            {
                if (!binding.HasStash)
                {
                    binding.Face.TryStash(element);
                }
            };
            element.RegisterCallback(binding.OnStyleResolved);
            binding.OnGeometryChanged = _ =>
            {
                if (!binding.HasStash)
                {
                    binding.Face.TryStash(element);
                }
                // Bake the gradient silhouette once layout gives a real size (the bake needs the element
                // pixel size + radius). Graphics.Blit here is safe — a geometry callback, not mesh generation.
                SyncGradientBake(element, binding);
            };
            element.RegisterCallback(binding.OnGeometryChanged);
            // Synchronous attempt: if the resolver's inline bg-[…]/border-[…] are already on the element,
            // suppress now so there is not even a one-frame ghost. Off-panel with no inline value it bails
            // and the events above pick it up.
            binding.Face.TryStash(element);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the callbacks and releases the suppression so the native rect chrome renders
        // again (pool reset also nulls inline style, but a detach without pooling must restore too).
        public static void Detach(VisualElement element, SkewBinding binding)
        {
            element.generateVisualContent -= binding.OnGenerate;
            if (binding.OnStyleResolved != null)
            {
                element.UnregisterCallback(binding.OnStyleResolved);
            }
            if (binding.OnGeometryChanged != null)
            {
                element.UnregisterCallback(binding.OnGeometryChanged);
            }
            if (binding.SuppressionApplied)
            {
                binding.Face.Release(element);
            }
            DestroyGradientTex(binding);
            element.MarkDirtyRepaint();
        }

        // Sets or clears the gradient fill for a skewed element, fed by the reconciler from the same class
        // list the non-skew gradient path reads. A no-op when unchanged; otherwise invalidates the baked
        // silhouette so it re-bakes at the (possibly new) spec, and repaints. Clearing destroys the bake.
        public static void SetGradient(VisualElement element, SkewBinding binding, bool hasGradient, GradientSpec gradient)
        {
            var specChanged = binding.HasGradient != hasGradient
                || (hasGradient && !binding.Gradient.Equals(gradient));
            if (specChanged)
            {
                binding.HasGradient = hasGradient;
                binding.Gradient = gradient;
                binding.GradBaked = false; // force a re-bake at the new spec
                if (!hasGradient)
                {
                    DestroyGradientTex(binding);
                }
                element.MarkDirtyRepaint();
            }
            if (hasGradient)
            {
                // ALWAYS re-sync (not only on a spec change): skew is painted, not a UITK transform, so a
                // skew-angle / border-radius change fires no GeometryChangedEvent. SyncGradientBake's key
                // (size + skew + radii) catches those and re-bakes, and is a cheap no-op when nothing moved.
                SyncGradientBake(element, binding);
            }
        }

        // Patch-time stash sync, delegated to the shared face stash. Runs AFTER SyncClassDrivenStyling (so the
        // resolver's inline writes for this patch are already on the element); see SilhouetteFaceStash.SyncOnPatch
        // for the three cases (resolver overwrote us / USS color may have moved beneath the sentinel / current).
        public static void SyncStashOnPatch(VisualElement element, SkewBinding binding, bool classesChanged)
            => binding.Face.SyncOnPatch(element, classesChanged);

        private static void Draw(MeshGenerationContext mgc, VisualElement ve, SkewBinding binding)
        {
            var w = ve.layout.width;
            var h = ve.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            var tanX = Mathf.Tan(binding.Spec.XDeg * Mathf.Deg2Rad);
            var tanY = Mathf.Tan(binding.Spec.YDeg * Mathf.Deg2Rad);
            var p = mgc.painter2D;

            if (binding.HasGradient)
            {
                // Gradient fill: the baked, SDF-antialiased sheared silhouette drawn as a bounding-box quad
                // (it pokes beyond the box, which a clipped background-image cannot). Baked in the geometry
                // callback, so on the first frame before layout it is simply not yet present.
                DrawGradientQuad(mgc, binding, w, h);
            }
            else
            {
                // Pre-stash frame (first paint racing the stash): read the live styles directly — the
                // native chrome is still visible this frame, so the overdraw is identical-colored.
                var fill = binding.HasStash ? binding.BgColor : ve.resolvedStyle.backgroundColor;
                if (fill.a > 0.004f)
                {
                    SilhouetteFace.BuildShearedRoundedRect(p, ve, 0f, w, h, tanX, tanY);
                    p.fillColor = fill;
                    p.Fill();
                }
            }

            var borderColor = binding.HasStash ? binding.BorderColor : ve.resolvedStyle.borderLeftColor;
            var borderWidth = ve.resolvedStyle.borderLeftWidth;
            if (borderWidth > 0.01f && borderColor.a > 0.004f)
            {
                // Stroke centered on a half-width-inset path ≈ CSS's inside border. Drawn after the fill
                // so the outline sits on top and follows the shear, keeping the slanted edge crisp.
                SilhouetteFace.BuildShearedRoundedRect(p, ve, borderWidth * 0.5f, w, h, tanX, tanY);
                p.strokeColor = borderColor;
                p.lineWidth = borderWidth;
                p.lineJoin = LineJoin.Miter;
                p.Stroke();
            }
        }

        // Re-bakes the gradient silhouette when the element's size / skew / spec changed since the last bake
        // and the layout is ready. Self-guards: clears the texture when the gradient is gone, and defers
        // when there is no panel / size yet (the geometry callback retries). The bake runs the
        // Velvet/GradientSilhouette shader at the sheared bounding-box size; the result is owned by the
        // binding and sampled by DrawGradientQuad. NB run from a geometry callback or a patch (never from
        // Draw), so the Graphics.Blit inside the baker is not issued during UITK mesh generation.
        private static void SyncGradientBake(VisualElement element, SkewBinding binding)
        {
            if (!binding.HasGradient)
            {
                DestroyGradientTex(binding);
                return;
            }

            var w = element.layout.width;
            var h = element.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h) || element.panel == null)
            {
                return;
            }

            var tanX = Mathf.Tan(binding.Spec.XDeg * Mathf.Deg2Rad);
            var tanY = Mathf.Tan(binding.Spec.YDeg * Mathf.Deg2Rad);
            var rs = element.resolvedStyle;
            float tl = rs.borderTopLeftRadius, tr = rs.borderTopRightRadius;
            float br = rs.borderBottomRightRadius, bl = rs.borderBottomLeftRadius;
            var key = (Mathf.RoundToInt(w), Mathf.RoundToInt(h),
                Mathf.RoundToInt(tanX * 100f), Mathf.RoundToInt(tanY * 100f),
                Mathf.RoundToInt(tl), Mathf.RoundToInt(tr), Mathf.RoundToInt(br), Mathf.RoundToInt(bl));
            if (binding.GradBaked && binding.GradientTex != null && key == binding.GradKey)
            {
                return;
            }

            var tex = GradientSilhouetteBaker.Bake(binding.Gradient, w, h, tanX, tanY, new Vector4(tl, tr, br, bl));
            if (tex == null)
            {
                return; // no graphics device / shader yet — retry on the next geometry change
            }
            DestroyGradientTex(binding);
            binding.GradientTex = tex;
            binding.GradKey = key;
            binding.GradBaked = true;
            element.MarkDirtyRepaint();
        }

        // Draws the baked silhouette as a quad covering the element's sheared bounding box (centred on the
        // box centre, matching the bake) in the element's own generateVisualContent, so the slant pokes
        // beyond the box (a clipped background-image could not). UVs are raw 0..1 (UI Toolkit remaps them
        // into the atlas slot); the texture's alpha carries the SDF antialiasing. texUV.y is flipped so the
        // box top samples the `from` stop, matching the bake and the non-skew background orientation.
        private static void DrawGradientQuad(MeshGenerationContext mgc, SkewBinding binding, float w, float h)
        {
            var tex = binding.GradientTex;
            if (tex == null)
            {
                return;
            }
            float qw = tex.width;
            float qh = tex.height;
            var minX = (w * 0.5f) - (qw * 0.5f);
            var maxX = (w * 0.5f) + (qw * 0.5f);
            var minY = (h * 0.5f) - (qh * 0.5f);
            var maxY = (h * 0.5f) + (qh * 0.5f);

            var mwd = mgc.Allocate(4, 6, tex);
            mwd.SetNextVertex(new Vertex { position = new Vector3(minX, minY, Vertex.nearZ), tint = Color.white, uv = new Vector2(0f, 1f) }); // top-left
            mwd.SetNextVertex(new Vertex { position = new Vector3(maxX, minY, Vertex.nearZ), tint = Color.white, uv = new Vector2(1f, 1f) }); // top-right
            mwd.SetNextVertex(new Vertex { position = new Vector3(maxX, maxY, Vertex.nearZ), tint = Color.white, uv = new Vector2(1f, 0f) }); // bottom-right
            mwd.SetNextVertex(new Vertex { position = new Vector3(minX, maxY, Vertex.nearZ), tint = Color.white, uv = new Vector2(0f, 0f) }); // bottom-left
            mwd.SetNextIndex(0);
            mwd.SetNextIndex(1);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(0);
            mwd.SetNextIndex(2);
            mwd.SetNextIndex(3);
        }

        private static void DestroyGradientTex(SkewBinding binding)
        {
            GradientSilhouetteBaker.Release(binding.GradientTex);
            binding.GradientTex = null;
            binding.GradBaked = false;
        }

    }
}
