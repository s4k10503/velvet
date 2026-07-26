using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for skew-x-*/skew-y-* utilities: a sheared silhouette drawn via the
    // element's own generateVisualContent, no structural element added. Also owns any bg-gradient-* on
    // the same element (SyncSkewGradient), since a skewed face must paint the gradient on its sheared
    // mesh rather than as an un-sheared background-image rectangle.
    internal sealed class FiberSkewApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberSkewApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames resolves to an active skew-x-*/skew-y-* pair,
        // wires the sheared-silhouette painter onto the element (no structural wrapper — the
        // silhouette is the element's own generateVisualContent). The first color stash is
        // event-driven (the element is not attached / style-resolved yet at create time).
        internal void ApplySkewOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleSkewClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            var binding = SkewSilhouette.Attach(element, spec);
            _ctx.SkewBindings[element] = binding;
            // A skewed element OWNS any bg-gradient-* on it: the silhouette paints the gradient on a
            // sheared mesh (the rectangular background-image the non-skew path would set cannot follow the
            // shear). ApplyGradientOnCreate runs next and defers to this binding.
            SyncSkewGradient(element, binding, classNames);
            SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(classNames), classNames);
        }

        // Feeds the gradient resolved from the element's class list into a skew binding (or clears it), so
        // a skewed element paints bg-gradient-* on its sheared mesh. The non-skew gradient path defers to
        // the binding whenever the element is skewed, so the gradient renders exactly once.
        private static void SyncSkewGradient(VisualElement element, SkewBinding binding, string[] classNames)
        {
            GradientSpec gradient = default;
            var has = StyleGradientClass.HasGradientClass(classNames)
                && StyleGradientClass.TryExtract(classNames, out gradient);
            SkewSilhouette.SetGradient(element, binding, has, gradient);
        }

        // Patch-time reconciliation of an element's skew state against its new class list. Four
        // cases mirroring the clip/shadow layers: update the existing binding's spec, attach a
        // newly-skewed element, detach one whose skew classes were removed, or do nothing. Runs
        // AFTER SyncClassDrivenStyling so the stash sync observes this patch's freshly-applied
        // styling (the inline slot is shared with the arbitrary-value resolver). Returns the
        // resolved X angle (0 = no skew) — PatchElement forwards it to ApplyShadowOnPatch so the
        // shadow follows the sheared silhouette without re-parsing the skew classes.
        internal float ApplySkewOnPatch(VisualElement element, string[] oldClassNames, string[] newClassNames)
        {
            var bound = _ctx.SkewBindings.TryGetValue(element, out var binding);
            var has = StyleSkewClass.TryGetWinningSkewClasses(newClassNames, out var winnerX, out var winnerY);
            // Fast path: no skew anywhere near this element.
            if (!bound && !has)
            {
                return 0f;
            }

            var classesChanged = !ReferenceEquals(oldClassNames, newClassNames);

            // Steady state: the winning tokens are exactly what the live binding was built from —
            // skip the parse, but keep the color stash in sync with this patch's styling.
            if (bound && has && binding.Spec.SourceX == winnerX && binding.Spec.SourceY == winnerY)
            {
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged);
                // Re-seat the descendant-shear child translate unconditionally (not gated on classesChanged): a
                // child add / remove / reorder leaves the caster's own skew tokens untouched, so the children
                // must still re-seat even when this element's class list did not change.
                SkewSilhouette.SyncChildTranslate(binding);
                // Re-resolve the gradient only when the class list changed (an unchanged list cannot
                // have changed the gradient); SetGradient is itself a no-op when the spec is unchanged.
                if (classesChanged)
                {
                    SyncSkewGradient(element, binding, newClassNames);
                    // A filter utility / animate-hue can appear or vanish without the skew tokens changing.
                    SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(newClassNames), newClassNames);
                }
                return binding.Spec.XDeg;
            }

            SkewSpec spec = default;
            var want = has && StyleSkewClass.TryExtract(newClassNames, out spec);

            if (want && bound)
            {
                binding.Spec = spec;
                SkewSilhouette.SyncStashOnPatch(element, binding, classesChanged: true);
                // The manipulator reads Spec live, so the just-changed angle re-seats the children here.
                SkewSilhouette.SyncChildTranslate(binding);
                SyncSkewGradient(element, binding, newClassNames);
                SkewSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(newClassNames), newClassNames);
                element.MarkDirtyRepaint();
                return spec.XDeg;
            }
            if (want)
            {
                var fresh = SkewSilhouette.Attach(element, spec);
                _ctx.SkewBindings[element] = fresh;
                SkewSilhouette.SyncStashOnPatch(element, fresh, classesChanged: true);
                SyncSkewGradient(element, fresh, newClassNames);
                SkewSilhouette.SetWantSpacer(element, fresh, WrapperInfrastructure.CarriesFilter(newClassNames), newClassNames);
                return spec.XDeg;
            }
            if (bound)
            {
                SkewSilhouette.Detach(element, binding);
                _ctx.SkewBindings.Remove(element);
            }
            return 0f;
        }
    }
}
