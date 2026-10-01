using System;
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

        // Set when Color came from a value written inline on the child, which the mask then replaced.
        public bool FromInline;

        // An element carrying the child's classes and none of its inline style, present while the dash takes
        // the color those classes resolve to: the child's own slot holds the mask, so it cannot be read there.
        public VisualElement? Probe;

        private Color _color;

        public Color Color
        {
            get => Probe != null ? DivideDashPainter.ResolvedColor(Probe, Edge) : _color;
            set => _color = value;
        }

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
        public static DivideDashChildBinding Attach(VisualElement child, DivideEdge edge, Color color, BorderLineStyle style)
        {
            var binding = new DivideDashChildBinding { Child = child, Edge = edge, Color = color, Style = style };
            binding.OnGenerate = mgc => Draw(mgc, child, binding);
            // Appended (not prepended): the divider sits ON the child's divider edge over its own content, the
            // same place a solid inline border paints (over the child's background edge).
            child.generateVisualContent += binding.OnGenerate;
            child.MarkDirtyRepaint();
            return binding;
        }

        public static void Update(VisualElement child, DivideDashChildBinding binding, DivideEdge edge, Color color, BorderLineStyle style)
        {
            binding.Edge = edge;
            binding.Color = color;
            binding.Style = style;
            child.MarkDirtyRepaint();
        }

        public static void Detach(VisualElement child, DivideDashChildBinding binding)
        {
            child.generateVisualContent -= binding.OnGenerate;
            RemoveProbe(binding);
            child.MarkDirtyRepaint();
        }

        internal const string ProbeClass = "velvet-divide-dash-probe";

        // Places the binding's probe inside the child, or removes it. Not beside it: the divide manipulator walks
        // the child's container by index while it adds and removes probes.
        public static void SyncProbe(VisualElement child, DivideDashChildBinding binding, bool wanted)
        {
            if (!wanted)
            {
                RemoveProbe(binding);
                return;
            }
            binding.Probe ??= new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    width = 0f,
                    height = 0f,
                    visibility = Visibility.Hidden,
                },
            };
            if (binding.Probe.hierarchy.parent != child)
            {
                child.hierarchy.Add(binding.Probe);
            }
            SyncProbeClasses(child, binding.Probe);
        }

        internal static void SyncProbeClasses(VisualElement child, VisualElement probe)
        {
            probe.ClearClassList();
            probe.AddToClassList(ProbeClass);
            foreach (var cls in child.GetClasses())
            {
                probe.AddToClassList(cls);
            }
        }

        private static void RemoveProbe(DivideDashChildBinding binding)
        {
            binding.Probe?.RemoveFromHierarchy();
            binding.Probe = null;
        }

#pragma warning disable CS8524 // no discard arm: a new edge has to name the side it reads
        internal static Color ResolvedColor(VisualElement element, DivideEdge edge) => edge switch
        {
            DivideEdge.Left => element.resolvedStyle.borderLeftColor,
            DivideEdge.Right => element.resolvedStyle.borderRightColor,
            DivideEdge.Top => element.resolvedStyle.borderTopColor,
            DivideEdge.Bottom => element.resolvedStyle.borderBottomColor,
        };
#pragma warning restore CS8524

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
