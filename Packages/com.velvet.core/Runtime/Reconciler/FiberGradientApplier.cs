using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for bg-gradient-* utilities: bakes the gradient to a texture and
    // applies it as the element's own background-image (UI Toolkit clips it to the element's
    // border-radius).
    internal sealed class FiberGradientApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberGradientApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames resolves to an active gradient (bg-gradient-to-* plus
        // at least one from/to stop), bakes it to a texture and applies it as the element's own
        // background-image (UI Toolkit clips it to the element's border-radius). No structural wrapper.
        internal void ApplyGradientOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleGradientClass.HasGradientClass(classNames)
                || !StyleGradientClass.TryExtract(classNames, element, out var spec, out var measured))
            {
                return;
            }
            var binding = GradientBackground.Bind(element, spec);
            _ctx.GradientBackgrounds[element] = binding;
            SyncMeasuredPoll(element, binding, classNames, measured);
        }

        // Reads the classes again on the element once a length they hold measures has moved (its font, the
        // panel), and binds what they come to there.
        private static void SyncMeasuredPoll(VisualElement element, GradientBinding binding, string[] classNames, bool measured)
            => StyleRelativeLengths.SyncPoll(ref binding.Poll, element, measured, () =>
            {
                if (StyleGradientClass.TryExtract(classNames, element, out var spec, out _) && !binding.Spec.Equals(spec))
                {
                    GradientBackground.Rebind(element, binding, spec);
                }
            });

        // Patch-time reconciliation of an element's gradient against its new class list: re-apply on a changed spec, attach a newly-gradiented element, clear one
        // whose gradient classes were removed, or the steady state (spec unchanged), which skips the re-bake
        // and only re-syncs the sizing to whether a StyleOverrides.BackgroundImage shows over the bake (the
        // image itself is ranked by StyleArbitraryValueResolver, so DiffStyles cannot leave it stale).
        internal void ApplyGradientOnPatch(VisualElement element, string[] classNames)
        {
            var bound = _ctx.GradientBackgrounds.TryGetValue(element, out var current);
            GradientSpec spec = default;
            var measured = false;
            var want = StyleGradientClass.HasGradientClass(classNames)
                && StyleGradientClass.TryExtract(classNames, element, out spec, out measured);

            if (!want)
            {
                if (bound)
                {
                    ClearStraightGradient(element, classNames);
                    GradientBackground.Detach(element, current!);
                    _ctx.GradientBackgrounds.Remove(element);
                }
                return;
            }
            if (!bound)
            {
                current = GradientBackground.Bind(element, spec);
                _ctx.GradientBackgrounds[element] = current;
            }
            else if (!current!.Spec.Equals(spec))
            {
                GradientBackground.Rebind(element, current, spec);
            }
            else
            {
                GradientBackground.SyncSize(element, current);
            }
            // A new poll for this pass's classes: the one before held the classes it was started with.
            StyleRelativeLengths.SyncPoll(ref current.Poll, element, false, () => { });
            SyncMeasuredPoll(element, current, classNames, measured);
        }

        // Clears the straight gradient background-image, but only nulls the image when no className-driven
        // image (bg-[addr:…]) owns it — that resolver writes the SAME inline property, so an unconditional
        // clear would wipe an image it applied earlier in this same patch (or one it left from a prior
        // render and did not re-apply). The backgroundSize the gradient set is reset either way.
        private static void ClearStraightGradient(VisualElement element, string[] classNames)
        {
            if (StyleBackgroundImageResolver.HasBackgroundImageClass(classNames))
            {
                GradientBackground.ClearSizeOnly(element);
            }
            else
            {
                GradientBackground.Clear(element);
            }
        }
    }
}
