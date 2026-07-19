using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one border-dashed / border-dotted element, keyed in
    // ReconcilerContext.BorderStyleBindings by the element itself (no structural wrapper — the dashed outline
    // is painted by the element's own generateVisualContent). Holds the resolved spec, the registered
    // callbacks (so they can be unregistered on detach), and a BORDER-ONLY face stash: the native border color
    // is suppressed with a sentinel and re-painted as a dashed / dotted stroke, so its effective color must be
    // captured before suppression. The background is left untouched (includeBackground:false) — only the
    // border is restyled, so a dashed outline composes with any fill.
    internal sealed class BorderStyleBinding
    {
        public BorderStyleSpec Spec;
        public Action<MeshGenerationContext>? OnGenerate;
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;
        public EventCallback<GeometryChangedEvent>? OnGeometryChanged;

        // Suppresses only the native border color (never the background) and captures its effective color,
        // reusing the shared sentinel machinery. Border uses the LEFT side (a single uniform stroke).
        public readonly SilhouetteFaceStash Face = new(includeBackground: false);

        // Pass-throughs onto Face so the reconciler / tests read the stash state directly off the binding.
        public Color BorderColor => Face.BorderColor;
        public bool HasStash => Face.HasStash;
        public bool SuppressionApplied => Face.SuppressionApplied;

        // Reusable polyline buffer for the outline (rebuilt each Draw from the live layout), so a steady-state
        // repaint allocates nothing.
        public readonly List<Vector2> Polyline = new();
    }

    /// <summary>
    /// Paints a <c>border-dashed</c> / <c>border-dotted</c> element's outline — a dashed or dotted rounded-rect
    /// stroke drawn via Painter2D in the element's own <c>generateVisualContent</c>. UI Toolkit has no CSS
    /// border-style, so a non-solid border is unrepresentable natively; the arc-length marcher
    /// (<see cref="DashedBorderPainter"/>) walks the box outline emitting dash / dot runs.
    /// </summary>
    /// <remarks>
    /// The native border color is suppressed with a near-invisible sentinel (the inline slot is shared with
    /// <see cref="StyleArbitraryValueResolver"/>'s <c>border-[…]</c> writes, so suppression re-syncs on every
    /// patch — the same contract <see cref="SkewSilhouette"/> documents), while the border WIDTH is left
    /// untouched so the box keeps reserving the same layout space a solid border would. Only the box's own
    /// outline is repainted; the background and children are unaffected, so <c>border-2 border-red-500
    /// border-dashed</c> composes exactly as CSS width + style + color (width from the untouched resolved
    /// style, color from the captured stash).
    /// <para>
    /// Limitation: when the element is also skewed or shadowed, that layer owns the whole face (it repaints a
    /// solid border as part of its silhouette), so the border stays SOLID — the reconciler defers this layer
    /// while a skew / shadow binding is present, the same tier as the clip + shadow / clip + ring mutual
    /// exclusions.
    /// </para>
    /// </remarks>
    internal static class BorderStyleSilhouette
    {
        // Wires the paint + stash callbacks onto the element and returns the binding. The first stash (capture
        // the border color, then suppress the native border color) is driven, in order of reliability: a
        // synchronous attempt here, then a GeometryChangedEvent, then CustomStyleResolvedEvent — mirroring
        // SkewSilhouette.Attach so a colored border never shows a solid ghost through the dashes before the
        // first patch.
        public static BorderStyleBinding Attach(VisualElement element, BorderStyleSpec spec)
        {
            var binding = new BorderStyleBinding { Spec = spec };
            binding.OnGenerate = mgc => Draw(mgc, element, binding);
            // TextElement (Button, Label) registers its text-rendering callback at construction, before this
            // Attach call. Prepending keeps the outline from being appended AFTER (over) the text — the same
            // ordering rule SkewSilhouette keeps for a skewed button's sheared background.
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
            };
            element.RegisterCallback(binding.OnGeometryChanged);
            binding.Face.TryStash(element);
            element.MarkDirtyRepaint();
            return binding;
        }

        // Unregisters the callbacks and releases the border-color suppression so the native border renders
        // again (pool reset also nulls inline style, but a detach without pooling must restore too).
        public static void Detach(VisualElement element, BorderStyleBinding binding)
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
            element.MarkDirtyRepaint();
        }

        // Tears the binding down for a face HANDOFF to a skew / shadow layer that took over the whole face in the
        // same patch: unregisters the paint + stash callbacks but LEAVES the border-color suppression in place, so
        // the incoming layer's own suppression of the shared inline slot is not nulled out from under it (which
        // would re-expose the native rectangular border behind the sheared / shadowed face). The plain Detach
        // releases instead, for when the border-style classes were genuinely removed with no higher layer taking over.
        public static void DetachPreservingSuppression(VisualElement element, BorderStyleBinding binding)
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
            element.MarkDirtyRepaint();
        }

        // Patch-time stash sync, delegated to the shared border-only face stash. Runs AFTER the per-patch
        // styling is applied (the inline border slot is shared with the arbitrary-value resolver).
        public static void SyncStashOnPatch(VisualElement element, BorderStyleBinding binding, bool classesChanged)
            => binding.Face.SyncOnPatch(element, classesChanged);

        private static void Draw(MeshGenerationContext mgc, VisualElement ve, BorderStyleBinding binding)
        {
            var w = ve.layout.width;
            var h = ve.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            // The width is never suppressed — only the color — so it reads real off the live resolved style,
            // exactly like the skew silhouette's border stroke.
            var borderWidth = ve.resolvedStyle.borderLeftWidth;
            if (borderWidth <= 0.01f)
            {
                return;
            }

            // Pre-stash frame (the first paint racing the stash): read the live color directly — the native
            // border is still visible this frame, so the overdraw is identical-colored.
            var color = binding.HasStash ? binding.BorderColor : ve.resolvedStyle.borderLeftColor;
            if (color.a <= 0.004f)
            {
                return;
            }

            // Stroke centered on a half-width-inset outline ≈ CSS's inside border, matching the skew
            // silhouette's border stroke inset. A border stroke never bleeds past the box, so (unlike skew /
            // shadow) no bounds-spacer is needed.
            SilhouetteFace.BuildShearedRoundedRectPolyline(binding.Polyline, ve, borderWidth * 0.5f, w, h, 0f, 0f);
            DashedBorderPainter.StrokeDashed(mgc.painter2D, binding.Polyline, closed: true, borderWidth, color, binding.Spec.Style);
        }
    }
}
