using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for bg-gradient-* utilities: bakes the gradient to a texture and
    // applies it as the element's own background-image (UI Toolkit clips it to the element's
    // border-radius). Defers to a skew binding when one owns the element's face (see FiberSkewApplier),
    // since a skewed caster paints its gradient on the sheared mesh instead.
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
                || !StyleGradientClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            // A skewed element owns its gradient (ApplySkewOnCreate ran first and fed the spec into the
            // skew binding, which paints it on the sheared mesh). Defer — a straight background-image here
            // would render a second, un-sheared rectangle behind the slant.
            if (_ctx.SkewBindings.ContainsKey(element))
            {
                return;
            }
            _ctx.GradientBackgrounds[element] = GradientBackground.Bind(element, spec);
        }

        // Patch-time reconciliation of an element's gradient against its new class list. Mirrors the
        // skew layer's four cases: re-apply on a changed spec, attach a newly-gradiented element, clear one
        // whose gradient classes were removed, or the steady state (spec unchanged), which skips the re-bake
        // and only re-syncs the sizing to whether a StyleOverrides.BackgroundImage shows over the bake (the
        // image itself is ranked by StyleArbitraryValueResolver, so DiffStyles cannot leave it stale).
        internal void ApplyGradientOnPatch(VisualElement element, string[] classNames, bool skewable)
        {
            var bound = _ctx.GradientBackgrounds.TryGetValue(element, out var current);
            GradientSpec spec = default;
            var want = StyleGradientClass.HasGradientClass(classNames)
                && StyleGradientClass.TryExtract(classNames, out spec);

            // A skewed element paints its gradient on the sheared mesh (ApplySkewOnPatch runs after this and
            // feeds it the spec), so the straight background-image path must stand down. Only an element node
            // is skewable — a Motion never attaches a sheared silhouette, so its gradient stays on the
            // straight path even with skew classes present. Drop any straight gradient left from a prior
            // non-skew state so the un-sheared rectangle does not linger behind the slant; the same drop
            // clears one whose gradient classes were removed.
            var skewed = skewable && StyleSkewClass.TryExtract(classNames, out _);
            if (skewed || !want)
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
                _ctx.GradientBackgrounds[element] = GradientBackground.Bind(element, spec);
            }
            else if (!current!.Spec.Equals(spec))
            {
                GradientBackground.Rebind(element, current, spec);
            }
            else
            {
                GradientBackground.SyncSize(element, current);
            }
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
