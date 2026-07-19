using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-child bookkeeping for a dashed / dotted divider drawn on ONE divided child's leading edge
    // (border-left for divide-x, border-top for divide-y). A solid divider is a plain inline border the
    // StyleDivideManipulator writes; a dashed / dotted divider has no UI Toolkit border-style, so it is
    // painted by the CHILD's own generateVisualContent. The child still reserves the SAME layout gutter (the
    // manipulator writes the real border width inline and masks only the color with the sentinel), so
    // switching a divider between solid and dashed is layout-identical — only the paint differs.
    internal sealed class DivideDashChildBinding
    {
        public Action<MeshGenerationContext>? OnGenerate;
        public DivideAxis Axis;
        public float Width;
        public Color Color;
        public BorderLineStyle Style;

        // Reusable 2-point buffer for the leading-edge segment (rebuilt each Draw from the live layout).
        public readonly Vector2[] Edge = new Vector2[2];
    }

    // Attaches / updates / detaches a divided child's dashed / dotted divider paint. The binding must register
    // on each DIVIDED CHILD's own generateVisualContent, not the container's: a container's generateVisualContent
    // always paints BEHIND its children, so a divider drawn from the container would be hidden under any child
    // with an opaque background — the same reason SkewSilhouette paints on the skewed element itself. Keyed per
    // child in ReconcilerContext.DivideDashBindings; torn down by FiberElementCleaner (so a keyed-list reorder
    // recycling one child independently of its container is still caught) and swept by Reconciler.Dispose.
    internal static class DivideDashPainter
    {
        public static DivideDashChildBinding Attach(VisualElement child, DivideAxis axis, float width, Color color, BorderLineStyle style)
        {
            var binding = new DivideDashChildBinding { Axis = axis, Width = width, Color = color, Style = style };
            binding.OnGenerate = mgc => Draw(mgc, child, binding);
            // Appended (not prepended): the divider sits ON the child's leading edge over its own content, the
            // same place a solid inline border paints (over the child's background edge).
            child.generateVisualContent += binding.OnGenerate;
            child.MarkDirtyRepaint();
            return binding;
        }

        public static void Update(VisualElement child, DivideDashChildBinding binding, DivideAxis axis, float width, Color color, BorderLineStyle style)
        {
            binding.Axis = axis;
            binding.Width = width;
            binding.Color = color;
            binding.Style = style;
            child.MarkDirtyRepaint();
        }

        public static void Detach(VisualElement child, DivideDashChildBinding binding)
        {
            child.generateVisualContent -= binding.OnGenerate;
            child.MarkDirtyRepaint();
        }

        private static void Draw(MeshGenerationContext mgc, VisualElement child, DivideDashChildBinding binding)
        {
            if (binding.Width <= 0.01f || binding.Color.a <= 0.004f)
            {
                return;
            }
            var w = child.layout.width;
            var h = child.layout.height;
            if (w <= 0f || h <= 0f || float.IsNaN(w) || float.IsNaN(h))
            {
                return;
            }

            // The leading edge in the child's own local space, centered on the border's half-width so the
            // dashed line straddles the edge exactly where a solid inline border of the same width would sit.
            var half = binding.Width * 0.5f;
            if (binding.Axis == DivideAxis.Horizontal)
            {
                // border-left: a vertical line down the left edge.
                binding.Edge[0] = new Vector2(half, 0f);
                binding.Edge[1] = new Vector2(half, h);
            }
            else
            {
                // border-top: a horizontal line across the top edge.
                binding.Edge[0] = new Vector2(0f, half);
                binding.Edge[1] = new Vector2(w, half);
            }
            DashedBorderPainter.StrokeDashed(mgc.painter2D, binding.Edge, closed: false, binding.Width, binding.Color, binding.Style);
        }
    }
}
