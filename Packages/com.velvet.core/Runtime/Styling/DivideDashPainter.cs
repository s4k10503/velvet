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
        public EventCallback<CustomStyleResolvedEvent>? OnStyleResolved;
        public VisualElement Child = null!;
        public DivideEdge Edge;
        public BorderLineStyle Style;

        // The divide-{color}, where the divider has one.
        public Color? Divider;

        // A color written inline on the child, kept once the mask replaced it.
        public Color? Inline;

        // The color the dash heads for, null before the first paint, and the color the last paint drew.
        public Color? Target;
        public Color Color;

        // The transition the child's own transition entry for the edge's color runs from From to Target; Tick
        // repaints the child each frame while it runs, and is paused otherwise.
        public Color From;
        // Where a change back runs to, and how far the running transition was shortened.
        public Color ReversingStart;
        public float Shortening = 1f;
        public double StartTime;
        public float DelaySec;
        public float DurationSec;
        public EasingMode Easing;
        public IVisualElementScheduledItem Tick = null!;

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
    // LeadingLengthProbe's does, and the child repaints when the color differs from the one the dash heads for.
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
            binding.Tick = child.schedule.Execute(child.MarkDirtyRepaint).Every(0);
            // MUTANT_SURVIVES(equivalent, line removed): the first paint pauses the tick, its target being new
            // and starting no transition, so a tick left running repaints only the frames before that paint.
            binding.Tick.Pause();
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
            s_bound.Remove(child);
            binding.Tick.Pause();
            child.MarkDirtyRepaint();
        }

        private static void OnStyleResolved(DivideDashChildBinding binding)
        {
            if (binding.Target != PaintColor(binding))
            {
                binding.Child.MarkDirtyRepaint();
            }
        }

        // The color writes of StyleArbitraryValueResolver.ApplyInline and ClearInline, which a motion driver makes
        // each frame, reach here as they are made, so the mask is back before the engine paints the edge. A write of the child's own code is
        // found when the child paints, and the mask goes back at the next scheduler tick rather than in the paint,
        // so the edge shows the write for that one paint
        // (Given_ADashedDivideRow_When_TheChildsCodeWritesItsEdgeColor_Then_TheEdgeIsMaskedFromTheNextPaint goes red on
        // a mask written back in the paint).
        internal static void TakeOverWrite(VisualElement element)
        {
            if (s_bound.TryGetValue(element, out var binding) && TakeOver(binding))
            {
                StyleArbitraryValueResolver.Hold(element, StyleDivideManipulator.ColorSlot(binding.Edge),
                    new StyleColor(SilhouetteFace.SuppressedColor));
                element.MarkDirtyRepaint();
            }
        }

        // A color written over the mask since it was held is taken as the child's own, and a write clearing the slot
        // drops the color taken; true when the mask has to be written back. StyleDivideManipulator.InlineOwnColor
        // takes one at an Apply.
        private static bool TakeOver(DivideDashChildBinding binding)
        {
            var written = StyleDivideManipulator.InlineColor(binding.Child, binding.Edge);
            if (SilhouetteFace.IsSentinel(written))
            {
                return false;
            }
            binding.Inline = SilhouetteFace.IsUnset(written) ? null : written;
            return true;
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
            var lookup = access.Lookup;
            lookup[0] = access.Hash.GetValue(access.Style.GetValue(child));
            lookup[1] = null;
            if (!(bool)access.TryGet.Invoke(null, lookup)!)
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
            public readonly object?[] Lookup = new object?[2];

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
            // A member of another type than the one read here would throw inside the paint, so it counts as missing.
            if ((hash?.FieldType, tryGet?.ReturnType, left?.PropertyType, right?.PropertyType, top?.PropertyType,
                    bottom?.PropertyType)
                != (typeof(long), typeof(bool), typeof(Color), typeof(Color), typeof(Color), typeof(Color)))
            {
                return null;
            }
            return new RulesAccess
            {
                Style = style!, Hash = hash!, TryGet = tryGet!, Left = left!, Right = right!, Top = top!,
                Bottom = bottom!,
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

#pragma warning disable CS8524 // no discard arm: a new edge has to name the property it transitions
        private static string ColorProperty(DivideEdge edge) => edge switch
        {
            DivideEdge.Left => "border-left-color",
            DivideEdge.Right => "border-right-color",
            DivideEdge.Top => "border-top-color",
            DivideEdge.Bottom => "border-bottom-color",
        };
#pragma warning restore CS8524

        // The color to paint now: the target, or a frame of the transition toward it. A target that differs from
        // the last one starts a transition from the color shown where the child carries an entry running for the
        // edge's color; the first paint starts none. A change back to where the running transition reverses to is
        // shortened by how far that one had got, its duration and a negative delay alike, as CSS Transitions shortens
        // a reversing transition (Given_ARunningDashTransition_When_TheColorChangesBackPartWay_Then_TheWayBackIsShortened).
        internal static Color Advance(DivideDashChildBinding binding, Color target, double now)
        {
            if (binding.Target != target)
            {
                var runs = StyleFilterTransitionDriver.TryFindTransition(binding.Child, ColorProperty(binding.Edge),
                    "border-color", out var durationMs, out var delayMs, out var easing);
                var running = binding.Tick.isActive;
                var eased = running ? Eased(binding, now, out _) : 1f;
                var shown = running ? Color.LerpUnclamped(binding.From, binding.Target!.Value, eased) : binding.Color;
                if (binding.Target != null && runs)
                {
                    var reverses = target == binding.ReversingStart;
                    binding.Shortening = reverses
                        ? Mathf.Clamp01(Mathf.Abs(1f - (1f - eased) * binding.Shortening))
                        : 1f;
                    binding.ReversingStart = reverses ? binding.Target.Value : shown;
                    binding.From = shown;
                    binding.StartTime = now;
                    binding.DurationSec = Mathf.Max(0, durationMs) / 1000f * binding.Shortening;
                    // MUTANT_SURVIVES(equivalent, boundary): a zero delay is zero shortened or not.
                    binding.DelaySec = delayMs / 1000f * (delayMs < 0 ? binding.Shortening : 1f);
                    binding.Easing = easing;
                    binding.Tick.Resume();
                }
                else
                {
                    binding.Tick.Pause();
                }
                binding.Target = target;
            }
            if (!binding.Tick.isActive)
            {
                return target;
            }
            // An entry taken away while the transition runs ends it at the target.
            if (!StyleFilterTransitionDriver.TryFindTransition(binding.Child, ColorProperty(binding.Edge), "border-color",
                    out _, out _, out _))
            {
                binding.Tick.Pause();
                return target;
            }
            var progress = Eased(binding, now, out var ended);
            if (ended)
            {
                binding.Tick.Pause();
            }
            return Color.LerpUnclamped(binding.From, target, progress);
        }

        // The eased progress of the binding's transition at `now`, and whether it has ended: 0 through the delay, and a
        // zero duration jumps to the end once the delay has passed.
        internal static float Eased(DivideDashChildBinding binding, double now, out bool ended)
        {
            var t = Mathf.Clamp01((float)((now - binding.StartTime - binding.DelaySec)
                / Math.Max(binding.DurationSec, 1e-6)));
            ended = t >= 1f;
            return UssEasing.Evaluate(binding.Easing, t);
        }

        private static void Draw(MeshGenerationContext mgc, VisualElement child, DivideDashChildBinding binding)
        {
            if (TakeOver(binding))
            {
                child.schedule.Execute(() => TakeOverWrite(child));
            }
            binding.Color = Advance(binding, PaintColor(binding), Time.realtimeSinceStartupAsDouble);
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
