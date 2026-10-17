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
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;
        public VisualElement Child = null!;
        public DivideEdge Edge;
        public BorderLineStyle Style;

        // The divide-{color}, where the divider has one, and whether it is important.
        public Color? Divider;
        public bool DividerImportant;

        // A color written inline on the child, kept once the mask replaced it, and the one a motion driver writes
        // while it drives the edge.
        public Color? Inline;
        public Color? Driven;

        // The color the last paint drew.
        public Color Color;

        // The panel whose per-frame check holds this binding (DivideDashPainter.EnsureCheck).
        public IPanel? CheckedBy;

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
    //
    // The child's own slot on the edge holds the mask, so a restyle that moves the color the dash takes can leave
    // every value the engine paints the child by where it was, and nothing repaints the child then
    // (Given_ADashedDivideRow_When_AGestureClassGivesADividedChildAColor_Then_TheDashIsPaintedInIt). MarkerClass
    // gives the child the custom property that makes UI Toolkit send it CustomStyleResolvedEvent on a restyle, as
    // LeadingLengthProbe's does, and the child repaints when the color differs from the one the last paint drew.
    internal static class DivideDashPainter
    {
        internal const string MarkerClass = "velvet-divide-dash";

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, DivideDashChildBinding>
            s_bound = new();

        public static DivideDashChildBinding Attach(VisualElement child, DivideEdge edge, BorderLineStyle style)
        {
            var binding = new DivideDashChildBinding { Child = child, Edge = edge, Style = style };
            binding.OnGenerate = mgc => Draw(mgc, child, binding);
            binding.OnStyleResolved = _ => OnStyleResolved(binding);
            // Appended (not prepended): the divider sits ON the child's divider edge over its own content, the
            // same place a solid inline border paints (over the child's background edge).
            child.generateVisualContent += binding.OnGenerate;
            child.RegisterCallback(binding.OnStyleResolved);
            child.AddToClassList(MarkerClass);
            s_bound.AddOrUpdate(child, binding);
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
            // MUTANT_SURVIVES(equivalent, line removed): a callback left registered only asks the element for a
            // repaint.
            child.UnregisterCallback(binding.OnStyleResolved);
            child.RemoveFromClassList(MarkerClass);
            // MUTANT_SURVIVES(equivalent, line removed): a binding left in the table only takes a driver's color for
            // a dash nothing paints.
            s_bound.Remove(child);
            Unregister(binding);
            child.MarkDirtyRepaint();
        }

        private static void OnStyleResolved(DivideDashChildBinding binding)
        {
            if (binding.Color != PaintColor(binding))
            {
                binding.Child.MarkDirtyRepaint();
            }
        }

        // The color a motion driver writes over the edge while it drives it (StyleArbitraryValueResolver.ApplyDriven,
        // which keeps the mask on the slot), or null once it hands the edge back.
        internal static void Drive(VisualElement element, int slots, Color? color)
        {
            if (!s_bound.TryGetValue(element, out var binding))
            {
                return;
            }
            if ((slots & StyleHeldSlots.Bit(StyleDivideManipulator.ColorSlot(binding.Edge))) != 0)
            {
                binding.Driven = color;
                element.MarkDirtyRepaint();
            }
        }


        internal static bool IsMask(StyleColor color)
            => color.keyword == StyleKeyword.Undefined && SilhouetteFace.IsSentinel(color.value);

        private sealed class PanelCheck
        {
            public readonly System.Collections.Generic.List<DivideDashChildBinding> Bindings = new();
            public IVisualElementScheduledItem Item = null!;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPanel, PanelCheck> s_checks = new();

        // A write of other code over a mask is found by one check per panel, run each frame among the panel's
        // scheduled items, so one made before it is masked before the edge paints
        // (Given_ADashedDivideRow_When_TheChildsCodeWritesItsEdgeColor_Then_ItIsMaskedBeforeItPaints).
        private static void EnsureCheck(DivideDashChildBinding binding, IPanel panel)
        {
            if (!s_checks.TryGetValue(panel, out var check))
            {
                var created = new PanelCheck();
                created.Item = panel.visualTree.schedule.Execute(() => Check(panel, created)).Every(0);
                s_checks.Add(panel, created);
                check = created;
            }
            if (binding.CheckedBy != panel)
            {
                Unregister(binding);
                binding.CheckedBy = panel;
                check.Bindings.Add(binding);
            }
            check.Item.Resume();
        }

        private static void Unregister(DivideDashChildBinding binding)
        {
            if (binding.CheckedBy != null && s_checks.TryGetValue(binding.CheckedBy, out var check))
            {
                check.Bindings.Remove(binding);
            }
            binding.CheckedBy = null;
        }

        private static void Check(IPanel panel, PanelCheck check)
        {
            for (var i = check.Bindings.Count - 1; i >= 0; i--)
            {
                var binding = check.Bindings[i];
                if (binding.Child.panel != panel)
                {
                    binding.CheckedBy = null;
                    check.Bindings.RemoveAt(i);
                    continue;
                }
                if (TakeOver(binding))
                {
                    StyleArbitraryValueResolver.Mask(binding.Child, StyleDivideManipulator.ColorSlot(binding.Edge));
                    binding.Child.MarkDirtyRepaint();
                }
            }
            if (check.Bindings.Count == 0)
            {
                check.Item.Pause();
            }
        }

        // A color written over the mask since it was held is taken as the child's own, and a write clearing the slot
        // drops the color taken; true when the mask has to be written back. StyleDivideManipulator.InlineOwnColor
        // takes one at an Apply.
        private static bool TakeOver(DivideDashChildBinding binding)
        {
            var written = StyleDivideManipulator.InlineColor(binding.Child, binding.Edge);
            if (IsMask(written))
            {
                return false;
            }
            binding.Inline = written.keyword == StyleKeyword.Undefined ? written.value : null;
            return true;
        }

        // A motion driver's color wins unless an important declaration does — an important divide-{color}, or one of
        // the child's own the write gives way to — as on the slot (StyleArbitraryValueResolver.ApplyDriven). Then the
        // divide-{color} where no class of the child's own outranks it, giving way to a layer of the child's own as
        // a solid divider's hold does (StyleDivideManipulator.WriteColors); then a layer of the child's own; then a
        // color written inline on the child; then the color the child's own style rules give the edge.
        internal static Color PaintColor(DivideDashChildBinding binding)
        {
            var slot = StyleDivideManipulator.ColorSlot(binding.Edge);
            var divides = binding.Divider.HasValue
                && !StyleDivideManipulator.ChildOutranks(binding.Child, slot, binding.DividerImportant);
            if (binding.Driven is { } driven && !(divides && binding.DividerImportant)
                && !StyleArbitraryValueResolver.ImportantWinsOverDriven(binding.Child, slot))
            {
                return driven;
            }
            if (binding.Divider is { } divider && divides)
            {
                return StyleArbitraryValueResolver.ResolveLayered(binding.Child, slot, binding.DividerImportant) is { } over
                    ? over.Color
                    : divider;
            }
            if (StyleArbitraryValueResolver.ResolveLayered(binding.Child, slot) is { } layered)
            {
                return layered.Color;
            }
            return binding.Inline ?? RulesColor(binding.Child, binding.Edge);
        }

        // The child's own inline slot on the edge holds the mask, so the color its style rules give the edge —
        // classes, selectors, pseudo-states and theme — is read where the engine keeps it for the child's matching
        // rules: the value removing the inline color would apply. Clear when that cannot be reached, which paints
        // nothing. DividerEdgePanelTests pins what this returns.
        internal static Color RulesColor(VisualElement child, DivideEdge edge)
            => StyleCascade.Of(child) is { } style && StyleCascade.TryReadBorderColor(style, edge, out var color)
                ? color
                : Color.clear;

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
            EnsureCheck(binding, child.panel);
            TakeOver(binding);
            binding.Color = PaintColor(binding);
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
