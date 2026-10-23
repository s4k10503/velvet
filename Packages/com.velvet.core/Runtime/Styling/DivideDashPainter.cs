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

        // The panel whose per-frame check holds this binding (DivideDashPainter.EnsureCheck).
        public IPanel? CheckedBy;

        // Whether a settling of the sibling overlay (FaceBorderOverlay) is waiting to run, and whether the binding
        // has been torn down.
        public bool OverlayPending;
        public bool Detached;

        // The width the child's border resolves to on Edge: the divider's own, or a width of the child's own that
        // the divider gave way to, so the dash is drawn as wide as the border it stands in for.
        public float Width => DivideDashPainter.StrokeWidth(Child, Edge);
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
            binding.Detached = true;
            FaceBorderOverlay.Remove(child);
            child.generateVisualContent -= binding.OnGenerate;
            // MUTANT_SURVIVES(equivalent, line removed): a callback left registered only asks the element for a
            // repaint.
            child.UnregisterCallback(binding.OnStyleResolved);
            child.RemoveFromClassList(MarkerClass);
            // MUTANT_SURVIVES(equivalent, line removed): a binding left in the table only takes a driver's color for
            // a dash nothing paints.
            s_bound.Remove(child);
            Unregister(binding);
            binding.Tick.Pause();
            child.MarkDirtyRepaint();
        }

        // The color the dash on child's edge paints, where a dashed divider masks that edge, or null.
        internal static Color? ColorUnderMask(VisualElement child, DivideEdge edge)
            => s_bound.TryGetValue(child, out var binding) && binding.Edge == edge ? PaintColor(binding) : null;

        // The edge a dashed divider paints on child, which the child's own border-dashed outline leaves to it.
        internal static DivideEdge? BoundEdge(VisualElement child)
            => s_bound.TryGetValue(child, out var binding) ? binding.Edge : null;

        private static void OnStyleResolved(DivideDashChildBinding binding)
        {
            if (binding.Target != PaintColor(binding))
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

        // The color the dash on child's edge for longhand is painted in, short of a transition toward it (Advance).
        internal static bool TryReadPainted(VisualElement child, StyleLonghand longhand, out Color color)
        {
            if (!s_bound.TryGetValue(child, out var binding)
                || HeldSlotGroups.LonghandOf(StyleDivideManipulator.ColorSlot(binding.Edge)) != longhand)
            {
                color = default;
                return false;
            }
            color = PaintColor(binding);
            return true;
        }

        // Whether child's dash masks slot over a color written inline by the child's own code (TakeOver).
        internal static bool MasksOwnColor(VisualElement child, HeldSlot slot)
            => s_bound.TryGetValue(child, out var binding) && StyleDivideManipulator.ColorSlot(binding.Edge) == slot
                && binding.Inline.HasValue;

        internal static bool IsMask(StyleColor color)
            => color.keyword == StyleKeyword.Undefined && SilhouetteFace.IsSentinel(color.value);

        private sealed class PanelCheck
        {
            public readonly System.Collections.Generic.List<DivideDashChildBinding> Bindings = new();
            public IVisualElementScheduledItem Item = null!;
            // The panel scheduler's time at the check's last run, which a dash transition is timed by: it stands in
            // for a transition the child's own classes declare, which motion.md's Clocks section leaves on the panel's
            // time on any MotionClock.
            public long NowMs;
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
                created.Item = panel.visualTree.schedule.Execute((TimerState ts) =>
                {
                    created.NowMs = ts.now;
                    Check(panel, created);
                }).Every(0);
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
        // the child's own the write gives way to — as on the slot (StyleArbitraryValueResolver.ApplyDriven). Then a
        // color the child's own code wrote inline, unless an important declaration holds the edge, as an inline
        // style outranks every declaration but an important one. Then the divide-{color} where no class of the
        // child's own outranks it, giving way to a layer of the child's own as a solid divider's hold does
        // (StyleDivideManipulator.WriteColors); then a layer of the child's own; then the color the child's own
        // style rules give the edge.
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
            if (binding.Inline is { } inline && !(divides && binding.DividerImportant)
                && !StyleArbitraryValueResolver.DeclaresImportantOwn(binding.Child, slot)
                && StyleArbitraryValueResolver.ResolveLayered(binding.Child, slot, importantOnly: true) == null)
            {
                return inline;
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
            return RulesColor(binding.Child, binding.Edge);
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
            if (binding.Target is not { } previous)
            {
                binding.Target = target;
                binding.Tick.Pause();
                return target;
            }
            if (previous != target)
            {
                var runs = StyleFilterTransitionDriver.TryFindTransition(binding.Child, ColorProperty(binding.Edge),
                    "border-color", out var durationMs, out var delayMs, out var easing);
                var running = binding.Tick.isActive;
                var eased = running ? Eased(binding, now, out _) : 1f;
                var shown = running ? Color.LerpUnclamped(binding.From, previous, eased) : binding.Color;
                if (runs)
                {
                    var reverses = target == binding.ReversingStart;
                    binding.Shortening = reverses
                        ? Mathf.Clamp01(Mathf.Abs(1f - (1f - eased) * binding.Shortening))
                        : 1f;
                    binding.ReversingStart = reverses ? previous : shown;
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
            EnsureCheck(binding, child.panel);
            TakeOver(binding);
            var now = s_checks.TryGetValue(child.panel, out var check) ? check.NowMs / 1000.0 : 0.0;
            binding.Color = Advance(binding, PaintColor(binding), now);
            // A child that clips its content cuts this dash at its padding box, which the divider's border band lies
            // outside (styling-variants.md's hidden-overflow table), and CSS clips neither, so such a child's dash
            // is painted on a sibling over it (FaceBorderOverlay), settled outside the paint as it writes the
            // hierarchy.
            if (!FaceBorderOverlay.ClipsAndSettle(child, ref binding.OverlayPending, binding, SettleOverlay))
            {
                PaintEdge(mgc, child, binding);
            }
        }

        private static void SettleOverlay(VisualElement child, DivideDashChildBinding binding)
        {
            binding.OverlayPending = false;
            if (binding.Detached || child.panel == null)
            {
                return;
            }
            FaceBorderOverlay.Sync(child, StyleCascade.ClipsOverflow(child), (mgc, _, _) => PaintEdge(mgc, child, binding));
        }

        private static void PaintEdge(MeshGenerationContext mgc, VisualElement child, DivideDashChildBinding binding)
        {
            if (binding.Width <= 0.01f || binding.Color.a <= 0.004f)
            {
                return;
            }
            if (BorderStyleSilhouette.BoxOf(child) is { } box)
            {
                // The divider is painted where a solid inline border of the same width would sit, as a side of
                // the child's own border.
                DashedBorderPainter.PaintSide(mgc.painter2D, binding.Edge, box, binding.Color, binding.Style);
            }
        }
    }
}
