#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a layoutId crossfade draws an element's opacity at, over the element's own: Lerp(From, own, Mix) * Scale.
    // Framer's mixValues (mix-values.ts) gives a lead fading in `(0, easeIn, 1)`, a member behind it
    // `(the previous lead's, 0, 1 - easeOut)` and a lead alone under its id `(the previous holder's, progress, 1)`.
    internal readonly struct LayoutIdFade
    {
        public static readonly LayoutIdFade None = new(0f, 1f, 1f);

        public LayoutIdFade(float from, float mix, float scale)
        {
            From = from;
            Mix = mix;
            Scale = scale;
        }

        public float From { get; }
        public float Mix { get; }
        public float Scale { get; }
    }

    // The inline opacity slot, which the writers of an element's own opacity (MotionSpringDriver, BezierTweenDriver,
    // StyleAnimateDriver's pulse, an opacity-[x] class) and a layoutId crossfade share. While a crossfade draws an
    // element, each writes through here, and the slot holds the crossfade applied to the element's own opacity.
    //
    // An own opacity the element's classes give it — a static opacity-*, or a change of class a transition carries,
    // as a variant swap or a transition-opacity class does — is read off the cascade
    // (StyleCascade) while a crossfade holds the slot, and its transition is run here, the engine's being suspended for
    // the crossfade through MotionNativeTransitionGuard as for the per-frame drivers. A transition still running as
    // the crossfade ends runs on here until it lands, and only then is the slot handed back.
    internal static class MotionOpacity
    {
        private sealed class Drawing
        {
            public LayoutIdFade Fade;
            // The element's own inline opacity, or Null where its classes give it.
            public StyleFloat OwnInline;
            public StyleFloat Written;
            // The element's own opacity, carried by the transition it declares for opacity.
            public readonly LayoutIdCarry Own = new();
            // Steps the element's own opacity once the crossfade has ended, until it lands.
            public IVisualElementScheduledItem? Tail;
            // What the element's rules give its opacity beneath the inline values, NaN where that cannot be read, and
            // how long they transition it, as last read (ReadCascade).
            public float CascadeOpacity = float.NaN;
            public float CascadeDurationSec;
        }

        private static readonly ConditionalWeakTable<VisualElement, Drawing> s_drawing = new();
        // The owner of the transition suspension an element holds while a crossfade draws it.
        private static readonly object s_owner = new CarryingOwner();

        private sealed class CarryingOwner : ICarryingOwner
        {
        }

        // Writes the element's own inline opacity for a driver that writes it every frame, Null handing it back to its
        // classes. Under a crossfade it lands at once, and the slot holds the crossfade applied to it.
        public static void Write(VisualElement element, StyleFloat own)
        {
            if (!s_drawing.TryGetValue(element, out var drawing))
            {
                element.style.opacity = own;
                return;
            }
            drawing.OwnInline = own;
            Land(drawing);
            Apply(element, drawing);
        }

        // Writes the element's own inline opacity for a class that gives it, which under a crossfade is carried on the
        // element's opacity transition, as the engine carries it otherwise.
        public static void WriteTransitioned(VisualElement element, StyleFloat own)
        {
            if (!s_drawing.TryGetValue(element, out var drawing))
            {
                element.style.opacity = own;
                return;
            }
            drawing.OwnInline = own;
            ReadCascade(element, drawing);
            Carry(element, drawing, 0f);
            Apply(element, drawing);
        }

        // The element's own opacity as it is now: its inline value, or what its classes give it, as a crossfade has
        // carried it while one draws the element.
        public static float Own(VisualElement element)
        {
            if (s_drawing.TryGetValue(element, out var drawing)) return drawing.Own.Value;
            var inline = element.style.opacity;
            return inline.keyword == StyleKeyword.Undefined ? inline.value : element.resolvedStyle.opacity;
        }

        // Draws the element at the crossfade, its own opacity carried on by dtSec. The first draw starts from the
        // opacity the element is resolved at.
        public static void Draw(VisualElement element, LayoutIdFade fade, float dtSec)
        {
            if (!s_drawing.TryGetValue(element, out var drawing))
            {
                drawing = new Drawing { OwnInline = element.style.opacity, Written = element.style.opacity };
                drawing.Own.Land(element.resolvedStyle.opacity);
                RunningStyleTransition.TakeOverOpacity(element, drawing.Own);
                s_drawing.Add(element, drawing);
            }
            drawing.Tail?.Pause();
            drawing.Tail = null;
            drawing.Fade = fade;
            Step(element, drawing, dtSec);
        }

        // Stops the crossfade. The slot is handed back to the element's own inline opacity, or to its classes, and
        // the transitions with it, once the element's own opacity has landed.
        public static void End(VisualElement element)
        {
            if (!s_drawing.TryGetValue(element, out var drawing)) return;
            drawing.Fade = LayoutIdFade.None;
            if (drawing.Own.Landed)
            {
                Finish(element, drawing);
                return;
            }
            Apply(element, drawing);
            drawing.Tail ??= element.schedule.Execute(state => StepTail(element, drawing, state.deltaTime / 1000f))
                .Every(StyleAnimateDriver.TickMs);
        }

        // Drops an element torn down mid-crossfade, so that nothing writing it after the pool hands it on finds it drawn.
        public static void Forget(VisualElement element)
        {
            if (!s_drawing.TryGetValue(element, out var drawing)) return;
            drawing.Tail?.Pause();
            s_drawing.Remove(element);
        }

        private static void StepTail(VisualElement element, Drawing drawing, float dtSec)
        {
            Step(element, drawing, dtSec);
            if (drawing.Own.Landed) Finish(element, drawing);
        }

        private static void Finish(VisualElement element, Drawing drawing)
        {
            drawing.Tail?.Pause();
            s_drawing.Remove(element);
            element.style.opacity = drawing.OwnInline;
            MotionNativeTransitionGuard.Release(element, s_owner);
        }

        private static void Step(VisualElement element, Drawing drawing, float dtSec)
        {
            // A value something else wrote into the slot is the element's own from then on, carried as the engine
            // would carry it.
            if (element.style.opacity != drawing.Written) drawing.OwnInline = element.style.opacity;
            // The timing is read out of a held list before the suspension takes opacity out of it.
            HoldTiming(element, drawing);
            ReadCascade(element, drawing);
            var intercepted = drawing.CascadeDurationSec > 0f
                || (MotionNativeTransitionGuard.DeclaredSlots(element) & MotionTransitionSlots.Opacity) != MotionTransitionSlots.None;
            MotionNativeTransitionGuard.Narrow(element, s_owner, MotionTransitionSlots.Opacity, intercepted);
            Carry(element, drawing, dtSec);
            Apply(element, drawing);
        }

        private static float OwnTarget(Drawing drawing) =>
            drawing.OwnInline.keyword == StyleKeyword.Undefined ? drawing.OwnInline.value : drawing.CascadeOpacity;

        private static void Land(Drawing drawing)
        {
            var target = OwnTarget(drawing);
            if (!float.IsNaN(target)) drawing.Own.Land(target);
        }

        private static void Apply(VisualElement element, Drawing drawing)
        {
            var fade = drawing.Fade;
            element.style.opacity = Mathf.Clamp01(Mathf.LerpUnclamped(fade.From, drawing.Own.Value, fade.Mix) * fade.Scale);
            drawing.Written = element.style.opacity;
        }

        // A list a variant swap holds inline names the timing the engine would have run opacity by. It is read as the
        // swap writes it, before the narrowing takes opacity out of it (Step).
        private static void HoldTiming(VisualElement element, Drawing drawing)
        {
            var style = element.style;
            var lists = new TransitionLists(style.transitionProperty.value, style.transitionDuration.value,
                style.transitionDelay.value, style.transitionTimingFunction.value);
            drawing.Own.Hold(lists, MotionNativeTransitionGuard.HoldsAForeignValue(element), "opacity", null);
        }

        private static void ReadCascade(VisualElement element, Drawing drawing) =>
            (drawing.CascadeOpacity, drawing.CascadeDurationSec, _, _) = StyleCascade.Opacity(element);

        // Carries the element's own opacity towards what it is given now; where the classes' value cannot be read, towards
        // the one it was carried to.
        private static void Carry(VisualElement element, Drawing drawing, float dtSec)
        {
            var target = OwnTarget(drawing);
            drawing.Own.Step(float.IsNaN(target) ? drawing.Own.Target : target, dtSec, element, "opacity", null);
        }
    }
}
