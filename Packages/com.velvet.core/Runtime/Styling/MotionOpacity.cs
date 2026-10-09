#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
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
    // (Cascade) while a crossfade holds the slot, and its transition is run here, the engine's being suspended for
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
            // The element's own opacity, carried from From towards Target by the transition it declares for opacity.
            public float Value;
            public float From;
            public float Target;
            public float ElapsedSec;
            public float DelaySec;
            public float DurationSec;
            public EasingMode Easing;
            // The timing the list a variant swap holds inline gave opacity, before this took opacity out of it; NaN
            // duration until one is read, and again once no tween plays on the element.
            public float HeldDurationSec = float.NaN;
            public float HeldDelaySec;
            public EasingMode HeldEasing;
            // Steps the element's own opacity once the crossfade has ended, until it lands.
            public IVisualElementScheduledItem? Tail;
            // What the element's rules give its opacity beneath the inline values, NaN where that cannot be read, and
            // the transition it runs opacity by, as last read (Cascade.Read).
            public float CascadeOpacity = float.NaN;
            public float CascadeDurationSec;
            public float CascadeDelaySec;
            public EasingMode CascadeEasing;
        }

        private static readonly ConditionalWeakTable<VisualElement, Drawing> s_drawing = new();
        // The owner of the transition suspension an element holds while a crossfade draws it.
        private static readonly object s_owner = new();
        private static readonly StylePropertyName s_opacityName = new("opacity");
        // Stands in for a zero duration, and is added to the time elapsed, so that a change with no transition lands in
        // the call that sees it, as the engine applies it.
        private const float MinDurationSec = 1e-4f;

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
            Cascade.Read(element, drawing);
            Carry(element, drawing, 0f);
            Apply(element, drawing);
        }

        // The element's own opacity as it is now: its inline value, or what its classes give it, as a crossfade has
        // carried it while one draws the element.
        public static float Own(VisualElement element)
        {
            if (s_drawing.TryGetValue(element, out var drawing)) return drawing.Value;
            var inline = element.style.opacity;
            return inline.keyword == StyleKeyword.Undefined ? inline.value : element.resolvedStyle.opacity;
        }

        // Draws the element at the crossfade, its own opacity carried on by dtSec. The first draw starts from the
        // opacity the element is resolved at.
        public static void Draw(VisualElement element, LayoutIdFade fade, float dtSec)
        {
            if (!s_drawing.TryGetValue(element, out var drawing))
            {
                var resolved = element.resolvedStyle.opacity;
                drawing = new Drawing { OwnInline = element.style.opacity, Written = element.style.opacity, Value = resolved, Target = resolved };
                s_drawing.Add(element, drawing);
                element.RegisterCallback(s_onTransitionCancel);
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
            if (Landed(drawing))
            {
                Finish(element, drawing);
                return;
            }
            Apply(element, drawing);
            drawing.Tail ??= element.schedule.Execute(state => StepTail(element, drawing, state.deltaTime / 1000f))
                .Every(StyleAnimateDriver.TickMs);
        }

        // True while a crossfade draws the element, so its inline opacity is the crossfade's rather than anything the
        // element's own classes or a loop gave it.
        public static bool Draws(VisualElement element) => s_drawing.TryGetValue(element, out _);

        // Drops an element torn down mid-crossfade, so that nothing writing it after the pool hands it on finds it drawn.
        public static void Forget(VisualElement element)
        {
            if (!s_drawing.TryGetValue(element, out var drawing)) return;
            element.UnregisterCallback(s_onTransitionCancel);
            drawing.Tail?.Pause();
            s_drawing.Remove(element);
        }

        private static void StepTail(VisualElement element, Drawing drawing, float dtSec)
        {
            Step(element, drawing, dtSec);
            if (Landed(drawing)) Finish(element, drawing);
        }

        // UI Toolkit reports how long it had run a transition it cancels, which the first write here does to one it
        // was running for the element's opacity. The carry that takes that transition over runs what was left of its
        // duration rather than all of it again, and none of its delay
        // (Given_AMemberPartWayThroughADelayedFade_When_ANewLeadTakesTheId_Then_TheFadeEndsWhenItWouldHave).
        private static readonly EventCallback<TransitionCancelEvent> s_onTransitionCancel = OnTransitionCancel;

        private static void OnTransitionCancel(TransitionCancelEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            if (!evt.AffectsProperty(s_opacityName)) return;
            if (!s_drawing.TryGetValue(element, out var drawing)) return;
            element.UnregisterCallback(s_onTransitionCancel);
            drawing.DelaySec = 0f;
            drawing.DurationSec = Mathf.Max(0f, drawing.DurationSec - (float)evt.elapsedTime);
        }

        private static void Finish(VisualElement element, Drawing drawing)
        {
            element.UnregisterCallback(s_onTransitionCancel);
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
            Cascade.Read(element, drawing);
            var intercepted = drawing.CascadeDurationSec > 0f
                || (MotionNativeTransitionGuard.DeclaredSlots(element) & MotionTransitionSlots.Opacity) != MotionTransitionSlots.None;
            MotionNativeTransitionGuard.SyncSuspension(element, s_owner, MotionTransitionSlots.Opacity, intercepted);
            Carry(element, drawing, dtSec);
            Apply(element, drawing);
        }

        private static bool Landed(Drawing drawing) => drawing.ElapsedSec >= drawing.DelaySec + drawing.DurationSec;

        private static float OwnTarget(Drawing drawing) =>
            drawing.OwnInline.keyword == StyleKeyword.Undefined ? drawing.OwnInline.value : drawing.CascadeOpacity;

        private static void Land(Drawing drawing)
        {
            var target = OwnTarget(drawing);
            if (float.IsNaN(target)) return;
            drawing.Value = drawing.From = drawing.Target = target;
            drawing.ElapsedSec = drawing.DelaySec = drawing.DurationSec = 0f;
        }

        private static void Apply(VisualElement element, Drawing drawing)
        {
            var fade = drawing.Fade;
            element.style.opacity = Mathf.Clamp01(Mathf.LerpUnclamped(fade.From, drawing.Value, fade.Mix) * fade.Scale);
            drawing.Written = element.style.opacity;
        }

        // A list a variant swap holds inline names the timing the engine would have run opacity by. It is read as the
        // swap writes it, before the suspension takes opacity out of it (Step), and dropped once no tween plays on the
        // element, whose slots then hold its own timing (MotionTweenTiming).
        private static void HoldTiming(VisualElement element, Drawing drawing)
        {
            var lists = new TransitionLists(element.style.transitionProperty.value, element.style.transitionDuration.value,
                element.style.transitionDelay.value, element.style.transitionTimingFunction.value);
            if (!MotionTweenTiming.Playing(element))
            {
                drawing.HeldDurationSec = float.NaN;
            }
            else if (StyleFilterTransitionDriver.TryFindTransition(lists, "opacity", null, out var durationMs, out var delayMs, out var easing))
            {
                drawing.HeldDurationSec = durationMs / 1000f;
                drawing.HeldDelaySec = delayMs / 1000f;
                drawing.HeldEasing = easing;
            }
        }

        // Carries the element's own opacity towards what it is given now, starting over from where it stands whenever
        // that changes, on the timing a held list gave opacity or else the one UI Toolkit runs it by (Cascade.Read).
        private static void Carry(VisualElement element, Drawing drawing, float dtSec)
        {
            var target = OwnTarget(drawing);
            if (!float.IsNaN(target) && target != drawing.Target)
            {
                drawing.From = drawing.Value;
                drawing.Target = target;
                drawing.ElapsedSec = 0f;
                if (!float.IsNaN(drawing.HeldDurationSec))
                {
                    (drawing.DurationSec, drawing.DelaySec, drawing.Easing) = (drawing.HeldDurationSec, drawing.HeldDelaySec, drawing.HeldEasing);
                }
                else
                {
                    (drawing.DurationSec, drawing.DelaySec, drawing.Easing) = (drawing.CascadeDurationSec, drawing.CascadeDelaySec, drawing.CascadeEasing);
                }
            }
            drawing.ElapsedSec += dtSec;
            var t = Mathf.Clamp01((drawing.ElapsedSec - drawing.DelaySec + MinDurationSec) / Mathf.Max(drawing.DurationSec, MinDurationSec));
            drawing.Value = Mathf.LerpUnclamped(drawing.From, drawing.Target, UssEasing.Evaluate(drawing.Easing, t));
        }

        // The style an element's classes cascade to, read through StyleCascade. Where that cannot be read, the classes'
        // value stays the one the crossfade started from. Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_
        // Then_ItsOwnIsCarriedOnThatTransition fails when the read stops giving the cascaded value.
        private static class Cascade
        {
            private static readonly PropertyInfo? s_opacity = EngineMember.ComputedStyleOpacity.ResolveProperty();
            private static readonly PropertyInfo? s_property = EngineMember.ComputedStyleTransitionProperty.ResolveProperty();
            private static readonly PropertyInfo? s_duration = EngineMember.ComputedStyleTransitionDuration.ResolveProperty();
            private static readonly PropertyInfo? s_delay = EngineMember.ComputedStyleTransitionDelay.ResolveProperty();
            private static readonly PropertyInfo? s_curve = EngineMember.ComputedStyleTransitionTimingFunction.ResolveProperty();
            private static readonly bool s_readable = Array.TrueForAll(
                new MemberInfo?[] { s_opacity, s_property, s_duration, s_delay, s_curve }, m => m != null);

            // Takes the element's cascaded opacity and the transition it runs opacity by into the drawing: the rules'
            // transition-property with the inline duration, delay and curve lists wherever the element holds them, as
            // UI Toolkit combines the two; the inline transition-property is the suspension's. NaN and no transition
            // where the cached style cannot be read.
            public static void Read(VisualElement element, Drawing drawing)
            {
                (drawing.CascadeOpacity, drawing.CascadeDurationSec, drawing.CascadeDelaySec, drawing.CascadeEasing) =
                    (float.NaN, 0f, 0f, EasingMode.Ease);
                if (Style(element) is not { } style) return;
                drawing.CascadeOpacity = (float)s_opacity!.GetValue(style);
                var inline = element.style;
                var lists = new TransitionLists(s_property!.GetValue(style) as List<StylePropertyName>,
                    inline.transitionDuration.keyword == StyleKeyword.Undefined ? inline.transitionDuration.value : s_duration!.GetValue(style) as List<TimeValue>,
                    inline.transitionDelay.keyword == StyleKeyword.Undefined ? inline.transitionDelay.value : s_delay!.GetValue(style) as List<TimeValue>,
                    inline.transitionTimingFunction.keyword == StyleKeyword.Undefined
                        ? inline.transitionTimingFunction.value
                        : s_curve!.GetValue(style) as List<EasingFunction>);
                if (!StyleFilterTransitionDriver.TryFindTransition(lists, "opacity", null, out var durationMs, out var delayMs, out var easing))
                {
                    return;
                }
                (drawing.CascadeDurationSec, drawing.CascadeDelaySec, drawing.CascadeEasing) = (durationMs / 1000f, delayMs / 1000f, easing);
            }

            private static object? Style(VisualElement element)
            {
                // MUTANT_SURVIVES(equivalent): on the editor this package declares, every member resolves and this returns nothing.
                // Given_ALeadCrossfadingIn_When_AClassTakesItsOpacityToZeroOnATransition_Then_ItsOwnIsCarriedOnThatTransition
                // fails where one does not.
                if (!s_readable) return null;
                return StyleCascade.Of(element);
            }
        }
    }
}
