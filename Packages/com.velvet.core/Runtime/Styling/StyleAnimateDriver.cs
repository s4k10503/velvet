using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Per-element state for a running animate-* motion. Holds the recurring scheduled tick (paused on
    // teardown), the loop start time, and the pan-axis decision (pan modes only).
    internal sealed class StyleAnimateBinding
    {
        public AnimateSpec Spec;
        // Wall-clock start of the loop (Time.realtimeSinceStartupAsDouble). The phase is derived from elapsed
        // time, so a dropped frame never accumulates drift (unlike a per-tick increment).
        public double StartTime;
        // The recurring tick. Scheduled on the PANEL ROOT (not the element) so a keyed reorder — which
        // briefly detaches the element and would make UI Toolkit silently drop a per-element scheduled item —
        // does not stall the animation. Paused on teardown.
        public IVisualElementScheduledItem Scheduled;
        // Pan axis for Gradient/Shimmer, derived from the gradient's angle at attach (vertical when the
        // gradient flows more up/down than left/right). Unused by Hue.
        public bool PanVertical;
        // For an attach that happened off-panel: the deferred-scheduling callback, unregistered on teardown
        // if it never fired (so it does not linger on the element across pool reuse).
        public EventCallback<AttachToPanelEvent> PendingAttach;
    }

    // Drives the animate-* motions. The texture is baked ONCE (the static gradient path); this only writes a
    // cheap inline style per frame — a background-position offset (Gradient/Shimmer), a hue-rotate filter
    // angle (Hue), or an opacity (Pulse) — so a continuously-animating gradient costs no per-frame texture
    // work. The phase math is pure (PanOffsetPx / HueAngleDeg / PulseOpacity / Phase) and unit-tested
    // directly; the scheduler wiring is exercised at runtime (the EditMode PlayerLoop does not tick, so tests
    // drive ApplyFrame at explicit phases instead).
    internal static class StyleAnimateDriver
    {
        // Tick interval (~60fps). The phase is time-derived, so the exact cadence only affects smoothness.
        private const long TickMs = 16;
        // Oversize factor for the Gradient pan: the background is twice the box along the pan axis, so the
        // box window slides across the full gradient (offset range [-box, 0]) without revealing an edge.
        private const float GradientOversize = 200f;
        // Pulse opacity bounds: oscillates between full and half (matches the conventional attention pulse).
        private const float PulseMinOpacity = 0.5f;
        private const float PulseMaxOpacity = 1f;

        // Attaches a motion to an element whose gradient (Gradient/Shimmer) or any background (Hue) is already
        // applied. Sets the once-per-attach background sizing for pan modes, then schedules the recurring tick
        // on the panel root (deferred to attach when the element is off-panel). Returns the binding to store.
        public static StyleAnimateBinding Attach(VisualElement element, AnimateSpec spec, bool panVertical)
        {
            var binding = new StyleAnimateBinding
            {
                Spec = spec,
                StartTime = Time.realtimeSinceStartupAsDouble,
                PanVertical = panVertical,
            };

            // Pan modes need their background sizing established once (the per-frame write only moves the
            // position). Gradient oversizes the pan axis; Shimmer keeps the stretched box so its
            // transparent-ended band can sweep fully in and out. Both disable repeat so off-box is empty.
            if (spec.Mode == AnimateMode.Gradient || spec.Mode == AnimateMode.Shimmer)
            {
                ApplyPanSizing(element, spec.Mode, panVertical);
            }

            ScheduleOrDefer(element, binding);
            return binding;
        }

        // Tears down a running motion: pauses the tick, removes any deferred-attach callback, and restores the
        // styles the motion drove. Pan modes restore the gradient's stretch-to-fill (the gradient itself may
        // still be bound) and clear the panned position; Hue clears the filter it owned; Pulse clears the
        // inline opacity it drove.
        public static void Detach(VisualElement element, StyleAnimateBinding binding)
        {
            binding.Scheduled?.Pause();
            binding.Scheduled = null;
            if (binding.PendingAttach != null)
            {
                element.UnregisterCallback(binding.PendingAttach);
                binding.PendingAttach = null;
            }

            if (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer)
            {
                // Restore the gradient's stretch-to-fill (matches GradientBackground.Apply) and drop the pan.
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
                element.style.backgroundPositionX = StyleKeyword.Null;
                element.style.backgroundPositionY = StyleKeyword.Null;
                element.style.backgroundRepeat = StyleKeyword.Null;
            }
            else if (binding.Spec.Mode == AnimateMode.Hue)
            {
                // Hue owns the filter slot while active (a static filter-* is an unsupported combo — Hue wins).
                // Null returns it to no-filter; a surviving class-driven filter is re-asserted by the reconciler
                // right after Detach (a NAMED USS filter re-resolves, an inline-resolved one is re-applied).
                element.style.filter = StyleKeyword.Null;
            }
            else if (binding.Spec.Mode == AnimateMode.Pulse)
            {
                // Pulse owns the opacity slot while active (a static opacity-* is shadowed — Pulse wins). Null
                // returns it to no-inline-opacity; a surviving class-driven opacity is re-asserted by the
                // reconciler right after Detach (a NAMED opacity-* re-resolves, an opacity-[.x] is re-applied).
                element.style.opacity = StyleKeyword.Null;
            }
        }

        // Re-asserts a pan mode's background sizing. A steady-state patch (the animate spec is unchanged) may
        // follow a gradient re-bake — GradientBackground.Apply resets backgroundSize to 100% stretch-to-fill —
        // which would drag the Gradient pan's clamped edge into the box. Re-applying the oversize (and the
        // NoRepeat) keeps the pan correct. No-op for Hue (it owns no background sizing).
        public static void ReapplyPanSizing(VisualElement element, StyleAnimateBinding binding)
        {
            if (binding.Spec.Mode == AnimateMode.Gradient || binding.Spec.Mode == AnimateMode.Shimmer)
            {
                ApplyPanSizing(element, binding.Spec.Mode, binding.PanVertical);
            }
        }

        // Decides the pan axis from a gradient's angle: vertical when the gradient flows more up/down than
        // left/right. 0/180 (to top / to bottom) → vertical; 90/270 (to right / to left) → horizontal.
        public static bool PanVerticalForAngle(float angleDeg)
        {
            var rad = angleDeg * Mathf.Deg2Rad;
            return Mathf.Abs(Mathf.Cos(rad)) > Mathf.Abs(Mathf.Sin(rad));
        }

        // Normalized loop position in [0,1), time-derived so a dropped tick never accumulates drift.
        public static float Phase(double elapsedSec, float durationSec)
        {
            if (durationSec <= 0f)
            {
                return 0f;
            }
            var frac = (float)(elapsedSec / durationSec);
            frac -= Mathf.Floor(frac);
            return frac < 0f ? frac + 1f : frac;
        }

        // The background-position offset (pixels) along the pan axis at loop position t, for a box of the given
        // extent. Gradient ping-pongs across the oversized background (range [-box, 0] via a triangle wave);
        // Shimmer sweeps one-way fully across the box (range [-box, +box] via a sawtooth).
        public static float PanOffsetPx(AnimateMode mode, float t, float box)
        {
            if (mode == AnimateMode.Shimmer)
            {
                return ((2f * t) - 1f) * box;
            }
            // Gradient: triangle wave 0→1→0 over the loop, panned leftward/upward by up to one box extent.
            var tri = 1f - Mathf.Abs((2f * t) - 1f);
            return -tri * box;
        }

        // The hue-rotate angle (degrees) at loop position t — a full 0..360 rotation per loop.
        public static float HueAngleDeg(float t) => 360f * t;

        // The opacity at loop position t: a smooth cosine ease between full (t=0,1) and half (t=0.5), so the
        // pulse fades out and back in once per loop with no hard turn at the extremes. The cosine is a faithful
        // approximation of the conventional cubic-bezier(0.4,0,0.6,1) pulse easing — it shares the (1,0.5,1)
        // keyframe vertices and the same smooth in-out feel; the in-between curve differs imperceptibly.
        public static float PulseOpacity(float t)
        {
            var mid = (PulseMaxOpacity + PulseMinOpacity) * 0.5f;
            var amp = (PulseMaxOpacity - PulseMinOpacity) * 0.5f;
            return mid + (amp * Mathf.Cos(2f * Mathf.PI * t));
        }

        // Applies one frame at loop position t. Pan modes read the element's resolved box (so they need a
        // laid-out element); Hue is geometry-independent. Public so tests drive specific phases without the
        // runtime scheduler (which the EditMode PlayerLoop does not tick).
        public static void ApplyFrame(VisualElement element, StyleAnimateBinding binding, float t)
        {
            switch (binding.Spec.Mode)
            {
                case AnimateMode.Gradient:
                case AnimateMode.Shimmer:
                {
                    var box = binding.PanVertical ? element.resolvedStyle.height : element.resolvedStyle.width;
                    // Pre-layout (or off-panel) the resolved box is NaN / 0; skip the write so a NaN offset is
                    // never applied — the next tick after layout resolves writes a valid offset. Mirrors the
                    // geometry guards on the clip-path / skew paths.
                    if (float.IsNaN(box) || box <= 0f)
                    {
                        break;
                    }
                    var offset = PanOffsetPx(binding.Spec.Mode, t, box);
                    if (binding.PanVertical)
                    {
                        element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, offset);
                    }
                    else
                    {
                        element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, offset);
                    }
                    break;
                }
                case AnimateMode.Hue:
                {
                    var fn = new FilterFunction(FilterFunctionType.HueRotate);
                    fn.AddParameter(new FilterParameter(HueAngleDeg(t)));
                    // A FRESH list every frame is REQUIRED, not wasteful: UI Toolkit's inline-filter setter
                    // dirties the element only when the backing list REFERENCE changes (it ref-compares, not
                    // content-compares), so reusing one mutated list would repaint frame 1 then freeze the hue.
                    // The only cost is a 1-element list of a struct.
                    element.style.filter = new List<FilterFunction> { fn };
                    break;
                }
                case AnimateMode.Pulse:
                {
                    // Geometry-free: opacity is a value-compared float, so writing it each frame dirties the
                    // element correctly (no reference-list pitfall like the filter slot above).
                    element.style.opacity = PulseOpacity(t);
                    break;
                }
            }
        }

        private static void ApplyPanSizing(VisualElement element, AnimateMode mode, bool panVertical)
        {
            if (mode == AnimateMode.Gradient)
            {
                var pan = Length.Percent(GradientOversize);
                var cross = Length.Percent(100f);
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(panVertical ? cross : pan, panVertical ? pan : cross));
            }
            else
            {
                // Shimmer keeps the gradient's stretch-to-fill; the transparent-ended band sweeps fully across.
                element.style.backgroundSize = new StyleBackgroundSize(
                    new BackgroundSize(Length.Percent(100f), Length.Percent(100f)));
            }
            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        }

        // Schedules the recurring tick on the panel root, or defers to AttachToPanelEvent when the element is
        // off-panel at attach (mirrors the exit scheduler: a host only exists once attached).
        private static void ScheduleOrDefer(VisualElement element, StyleAnimateBinding binding)
        {
            if (element.panel != null)
            {
                StartTick(element, binding);
                return;
            }
            EventCallback<AttachToPanelEvent> onAttach = null;
            onAttach = _ =>
            {
                element.UnregisterCallback(onAttach);
                binding.PendingAttach = null;
                // Only start if this binding is still the live one (a cancel-before-attach clears Scheduled
                // and unregisters this; but guard the StartTime baseline against a long off-panel delay).
                binding.StartTime = Time.realtimeSinceStartupAsDouble;
                StartTick(element, binding);
            };
            element.RegisterCallback(onAttach);
            binding.PendingAttach = onAttach;
        }

        private static void StartTick(VisualElement element, StyleAnimateBinding binding)
        {
            var host = element.panel.visualTree;
            binding.Scheduled = host.schedule.Execute(() =>
            {
                var elapsed = Time.realtimeSinceStartupAsDouble - binding.StartTime;
                ApplyFrame(element, binding, Phase(elapsed, binding.Spec.DurationSec));
            }).Every(TickMs);
        }
    }
}
