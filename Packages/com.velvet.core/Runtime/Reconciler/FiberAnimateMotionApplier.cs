using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for animate-* motion utilities. Gradient/Shimmer pan an existing
    // bg-gradient-* (so they defer to FiberGradientApplier's baked spec); the non-pan modes drive their own
    // shared inline slot (style.filter / style.opacity / style.rotate / style.scale / style.translate) directly,
    // independent of any gradient.
    internal sealed class FiberAnimateMotionApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberAnimateMotionApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames resolves to an active animate-* motion, attaches the
        // driver. Runs AFTER ApplyGradientOnCreate so a pan mode (gradient/shimmer) sees the baked gradient
        // already on the element; a pan mode with no gradient is inert (nothing to pan). The non-pan
        // modes need no gradient and attach on any element.
        internal void ApplyAnimateOnCreate(VisualElement element, string[] classNames)
        {
            // TryExtract is itself the cheap gate (its per-class probe costs the same as a separate scan),
            // so no-animation elements pay one pass, not two.
            if (!StyleAnimateClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            if (IsPanMode(spec.Mode) && !HasGradientShowing(element))
            {
                // A pan utility with no gradient to pan is a no-op (parity with a lone gradient stop).
                return;
            }
            AttachMotion(element, spec);
        }

        // Patch-time reconciliation of an element's animate-* motion against its new class list. Mirrors the
        // gradient layer's four cases: restart on a changed spec, attach a newly-animated element, detach one
        // whose animate-* classes were removed, or no-op the steady state. A pan mode also detaches if its
        // gradient was removed (nothing left to pan). Runs AFTER ApplyGradientOnPatch for the same reason as
        // create (the pan reads the live gradient).
        internal void ApplyAnimateOnPatch(VisualElement element, string[] classNames)
        {
            var bound = _ctx.AnimationBindings.TryGetValue(element, out var binding);
            var want = StyleAnimateClass.TryExtract(classNames, out var spec);
            // A pan mode needs a gradient showing; if it is gone or covered, the motion cannot run.
            if (want && IsPanMode(spec.Mode) && !HasGradientShowing(element))
            {
                want = false;
            }

            if (!bound && !want)
            {
                return;
            }
            if (want)
            {
                if (!bound || !binding.Spec.Equals(spec))
                {
                    if (bound)
                    {
                        var detachedMode = binding.Spec.Mode;
                        StyleAnimateDriver.Detach(element, binding);
                        RestoreSharedInlineSlot(element, detachedMode, classNames);
                    }
                    AttachMotion(element, spec);
                }
                else
                {
                    // Steady state: a gradient re-bake under a pan (ApplyGradientOnPatch ran just before this
                    // and may have reset backgroundSize to 100% stretch) would drag the pan's clamped edge into
                    // view — re-assert the pan oversize. No-op for the non-pan modes.
                    StyleAnimateDriver.ReapplyPanSizing(element, binding);
                    // The spec is unchanged but the class list around it may not be: a transition utility
                    // added or removed beside a running motion changes whether it should be suspended.
                    StyleAnimateDriver.SyncTransitionSuspension(element, binding);
                }
                return;
            }
            // Bound but the animate-* classes (or the gradient a pan needs) were removed: tear down.
            var teardownMode = binding.Spec.Mode;
            StyleAnimateDriver.Detach(element, binding);
            _ctx.AnimationBindings.Remove(element);
            SyncGradientBoxScale(element, AnimateMode.None, false);
            RestoreSharedInlineSlot(element, teardownMode, classNames);
        }

        // A gradient under a StyleOverrides.BackgroundImage is not the image showing, so there is nothing to pan.
        private bool HasGradientShowing(VisualElement element)
            => _ctx.GradientBackgrounds.ContainsKey(element) && !StyleArbitraryValueResolver.OverrideBackgroundWins(element);

        private void AttachMotion(VisualElement element, AnimateSpec spec)
        {
            var binding = StyleAnimateDriver.Attach(element, spec, ResolvePanVertical(element, spec));
            _ctx.AnimationBindings[element] = binding;
            SyncGradientBoxScale(element, spec.Mode, binding.PanVertical);
        }

        // The Gradient pan paints the background twice as large along its axis, so the gradient is laid out
        // over that box; every other state paints it over the element's own.
        private void SyncGradientBoxScale(VisualElement element, AnimateMode mode, bool panVertical)
        {
            if (!_ctx.GradientBackgrounds.TryGetValue(element, out var gradient))
            {
                return;
            }
            var scale = mode != AnimateMode.Gradient ? Vector2.one : panVertical ? new Vector2(1f, 2f) : new Vector2(2f, 1f);
            GradientBackground.SetBoxScale(element, gradient, scale);
        }

        // The non-pan modes own a shared inline slot while active — style.filter / style.opacity / style.rotate /
        // style.scale / style.translate — that an inline-resolved utility also writes (the arbitrary filter-[..] / opacity-[.x] forms, and the
        // filter presets blur-sm etc.). Detach nulls that slot to return to the no-motion state: a NAMED USS
        // class (opacity-50) then re-resolves on its own, but a surviving inline-resolved value is lost —
        // DiffClassList does not re-apply a token that did not change across the patch. So after Detach, re-assert
        // the new class list's inline-resolved values to restore the element's class-driven appearance. Pan modes
        // own no shared inline slot (they drive background-size/position), so they skip this.
        private void RestoreSharedInlineSlot(VisualElement element, AnimateMode detachedMode, string[] classNames)
        {
            if (!IsPanMode(detachedMode))
            {
                FiberNodePatcher.ReapplyArbitraryValues(element, classNames);
            }
        }

        private static bool IsPanMode(AnimateMode mode) => mode == AnimateMode.Gradient || mode == AnimateMode.Shimmer;

        // Pan axis from the element's bound gradient angle. Defaults to horizontal when the element has no
        // gradient (a non-pan motion, or a pan that was already filtered out above).
        private bool ResolvePanVertical(VisualElement element, AnimateSpec spec)
        {
            if (IsPanMode(spec.Mode) && _ctx.GradientBackgrounds.TryGetValue(element, out var gradient))
            {
                return StyleAnimateDriver.PanVerticalForAngle(gradient.Spec.AngleDeg);
            }
            return false;
        }
    }
}
