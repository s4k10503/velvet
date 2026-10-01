using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-child bookkeeping for a dashed / dotted divider drawn on ONE divided child's divider edge. A solid
    // divider is a plain inline border the StyleDivideManipulator writes; a dashed / dotted divider has no UI
    // Toolkit border-style, so it is painted by the CHILD's own generateVisualContent. The child still
    // reserves the SAME layout gutter (the border width stays on the child, the divider's or one of the child's
    // own, and only the color is masked with the sentinel), so switching a divider between solid and dashed is
    // layout-identical — only the paint differs. The binding stores the physical EDGE rather than the axis: on a
    // reversed container the divider is on the axis's trailing edge, and the stroke has to be drawn where the
    // solid border of the same divider would paint.
    internal sealed class DivideDashChildBinding
    {
        public Action<MeshGenerationContext>? OnGenerate;
        public VisualElement Child = null!;
        public DivideEdge Edge;
        public BorderLineStyle Style;

        // The divide-{color}, where the divider has one.
        public Color? Divider;

        // A color written inline on the child, kept once the mask replaced it.
        public Color? Inline;

        // Read when the child paints, so a pseudo-state, a class or a variant layer that moves the child's own
        // color after the divider last ran is still the color drawn.
        public Color Color => DivideDashPainter.PaintColor(this);

        // The width the child's border resolves to on Edge: the divider's own, or a width of the child's own that
        // the divider gave way to, so the dash is drawn as wide as the border it stands in for.
        public float Width => DivideDashPainter.StrokeWidth(Child, Edge);

        // Reusable 2-point buffer for the divider segment (rebuilt each Draw from the live layout).
        public readonly Vector2[] Segment = new Vector2[2];
    }

    // Attaches / updates / detaches a divided child's dashed / dotted divider paint. The binding must register
    // on each DIVIDED CHILD's own generateVisualContent, not the container's: a container's generateVisualContent
    // always paints BEHIND its children, so a divider drawn from the container would be hidden under any child
    // with an opaque background — the same reason SkewSilhouette paints on the skewed element itself. Keyed per
    // child in ReconcilerContext.DivideDashBindings; torn down by FiberElementCleaner (so a keyed-list reorder
    // recycling one child independently of its container is still caught) and swept by Reconciler.Dispose.
    internal static class DivideDashPainter
    {
        public static DivideDashChildBinding Attach(VisualElement child, DivideEdge edge, BorderLineStyle style)
        {
            var binding = new DivideDashChildBinding { Child = child, Edge = edge, Style = style };
            binding.OnGenerate = mgc => Draw(mgc, child, binding);
            // Appended (not prepended): the divider sits ON the child's divider edge over its own content, the
            // same place a solid inline border paints (over the child's background edge).
            child.generateVisualContent += binding.OnGenerate;
            child.MarkDirtyRepaint();
            return binding;
        }

        public static void Update(VisualElement child, DivideDashChildBinding binding, DivideEdge edge, BorderLineStyle style)
        {
            binding.Edge = edge;
            binding.Style = style;
            child.MarkDirtyRepaint();
        }

        public static void Detach(VisualElement child, DivideDashChildBinding binding)
        {
            child.generateVisualContent -= binding.OnGenerate;
            child.MarkDirtyRepaint();
        }

        // A layer of the child's own wins, then the divide-{color} where no class of the child's own sets the edge's
        // color, as a solid divider's hold does; then a color written inline on the child; then the color the
        // child's own style rules give the edge.
        internal static Color PaintColor(DivideDashChildBinding binding)
        {
            var slot = StyleDivideManipulator.ColorSlot(binding.Edge);
            if (StyleArbitraryValueResolver.ResolveLayered(binding.Child, slot) is { } layered)
            {
                return layered.Color;
            }
            if (binding.Divider.HasValue && !StyleArbitraryValueResolver.DeclaresOwn(binding.Child, slot))
            {
                return binding.Divider.Value;
            }
            return binding.Inline ?? RulesColor(binding.Child, binding.Edge);
        }

        // The child's own inline slot on the edge holds the mask, so the color its style rules give the edge —
        // classes, selectors, pseudo-states and theme — is read where the engine keeps it for the child's matching
        // rules: the value removing the inline color would apply. Clear when that cannot be reached, which paints
        // nothing. DividerEdgePanelTests pins what this returns.
        internal static Color RulesColor(VisualElement child, DivideEdge edge)
        {
            var access = s_rules;
            if (access == null)
            {
                return Color.clear;
            }
            var style = access.Style.GetValue(child);
            var hash = (long)access.Hash.GetValue(style);
            var lookup = new object?[] { hash, null };
            if (hash == 0 || !(bool)access.TryGet.Invoke(null, lookup)!)
            {
                return Color.clear;
            }
            return (Color)access.Edge(edge).GetValue(lookup[1])!;
        }

        private sealed class RulesAccess
        {
            public FieldInfo Style = null!;
            public FieldInfo Hash = null!;
            public MethodInfo TryGet = null!;
            public PropertyInfo Left = null!;
            public PropertyInfo Right = null!;
            public PropertyInfo Top = null!;
            public PropertyInfo Bottom = null!;

#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
            public PropertyInfo Edge(DivideEdge edge) => edge switch
            {
                DivideEdge.Left => Left,
                DivideEdge.Right => Right,
                DivideEdge.Top => Top,
                DivideEdge.Bottom => Bottom,
            };
#pragma warning restore CS8524
        }

        private static readonly RulesAccess? s_rules = BuildRulesAccess();

        private static RulesAccess? BuildRulesAccess()
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var style = typeof(VisualElement).GetField("m_Style", instance);
            var styleType = style?.FieldType;
            var hash = styleType?.GetField("matchingRulesHash", instance);
            var cache = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.StyleCache");
            var tryGet = cache?.GetMethod("TryGetValue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(long), styleType?.MakeByRefType() ?? typeof(object) }, null);
            var left = styleType?.GetProperty("borderLeftColor", instance);
            var right = styleType?.GetProperty("borderRightColor", instance);
            var top = styleType?.GetProperty("borderTopColor", instance);
            var bottom = styleType?.GetProperty("borderBottomColor", instance);
            if (style == null || hash == null || tryGet == null || left == null || right == null || top == null
                || bottom == null)
            {
                return null;
            }
            return new RulesAccess
            {
                Style = style, Hash = hash, TryGet = tryGet, Left = left, Right = right, Top = top, Bottom = bottom,
            };
        }


#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
        internal static float StrokeWidth(VisualElement child, DivideEdge edge) => edge switch
        {
            DivideEdge.Left => child.resolvedStyle.borderLeftWidth,
            DivideEdge.Right => child.resolvedStyle.borderRightWidth,
            DivideEdge.Top => child.resolvedStyle.borderTopWidth,
            DivideEdge.Bottom => child.resolvedStyle.borderBottomWidth,
        };
#pragma warning restore CS8524

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

            // The divider edge in the child's own local space, inset by the border's half-width so the dashed
            // line straddles the edge exactly where a solid inline border of the same width would sit.
            var half = binding.Width * 0.5f;
            switch (binding.Edge)
            {
                case DivideEdge.Left:
                    binding.Segment[0] = new Vector2(half, 0f);
                    binding.Segment[1] = new Vector2(half, h);
                    break;
                case DivideEdge.Right:
                    binding.Segment[0] = new Vector2(w - half, 0f);
                    binding.Segment[1] = new Vector2(w - half, h);
                    break;
                case DivideEdge.Top:
                    binding.Segment[0] = new Vector2(0f, half);
                    binding.Segment[1] = new Vector2(w, half);
                    break;
                case DivideEdge.Bottom:
                    binding.Segment[0] = new Vector2(0f, h - half);
                    binding.Segment[1] = new Vector2(w, h - half);
                    break;
            }
            DashedBorderPainter.StrokeDashed(mgc.painter2D, binding.Segment, closed: false, binding.Width, binding.Color, binding.Style);
        }
    }
}
