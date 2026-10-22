using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for shadow-* utilities: a baked box-shadow texture drawn behind the
    // element's content and bleeding outside its box via the element's own generateVisualContent. Composes
    // like skew and gradient (a paint, not a wrapper), and follows a skewed caster's sheared silhouette
    // (see FiberSkewApplier) rather than painting an upright rectangle.
    internal sealed class FiberBoxShadowApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberBoxShadowApplier(ReconcilerContext ctx) => _ctx = ctx;

        // Create-time entry point: when classNames carries a shadow-* utility, attaches the box-shadow
        // paint onto the element (no structural wrapper — the baked shadow texture is the element's own
        // generateVisualContent, drawn behind its content and bleeding outside the box). Composes like skew
        // and gradient: a paint, not a wrapper, so it works alongside a clip wrapper and a user
        // wrapElement.
        internal void ApplyShadowOnCreate(VisualElement element, string[] classNames)
        {
            BoxShadowSilhouette.RecordDeclaredColours(element, classNames);
            if (!StyleShadowClass.HasShadowClass(classNames) || !StyleShadowClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            // A clip-path-* on the same element clips the box-shadow too (CSS semantics): skip the paint when
            // a clip can apply. WantsClipWrapper (not just an active base clip) mirrors the patch path's
            // clipActive gate, so a clip VARIANT (hover:clip-path-[…]) on a base-shadow element suppresses the
            // shadow unconditionally — clip and shadow share this one suppression gate because variant
            // activation state is not resolved at this call site, so the shadow must conservatively assume
            // the clip may apply; pure base clip and pure shadow are unaffected.
            if (StyleClipPathClass.WantsClipWrapper(classNames))
            {
                return;
            }
            // A skewed caster's shadow follows the sheared silhouette (the box-shadow behavior);
            // create-time resolves the skew here, the patch path forwards it from ApplySkewOnPatch.
            var skew = StyleSkewClass.TryExtract(classNames, out var extracted) ? extracted : default;
            // ApplySkewOnCreate ran before this (the factory order), so a skewed caster already has a
            // SkewSilhouette owning its face. When skewed, the shadow paints ONLY the shadow quad (the skew
            // layer repaints the sheared fill/border); when upright, the shadow paint owns the face itself,
            // suppressing the native chrome and repainting an upright fill over the shadow quad. skew-y casters
            // have an X angle of 0 yet are skewed, so this gate is the SkewBindings presence, not the X angle.
            var casterSkewed = _ctx.SkewBindings.ContainsKey(element);
            var shadowBinding = BoxShadowSilhouette.Attach(element, spec, classNames, skew.XDeg, skew.YDeg,
                casterSkewed);
            _ctx.ShadowBindings[element] = shadowBinding;
            BoxShadowSilhouette.SetWantSpacer(element, shadowBinding, WrapperInfrastructure.CarriesFilter(_ctx, element, classNames), classNames);
        }

        // Patch-time reconciliation of an element's shadow state against its new class list. Mirrors the
        // skew / gradient paint layers' four cases: update the existing paint's spec, attach a newly-shadowed
        // element, detach one whose shadow was removed, or do nothing.
        // clipActive: whether the class list resolves to an active clip-path-* — resolved ONCE by the caller
        // (PatchElement forwards ApplyClipPathOnPatch's result; PatchMotion passes false). An active clip
        // suppresses the shadow (CSS clip-path clips the box-shadow too).
        // The shadow is a paint, not a wrapper, so a Motion can carry a shadow paint without becoming an
        // AnimatePresence anchor (nothing structural is added).
        internal void ApplyShadowOnPatch(VisualElement element, string[] classNames, bool clipActive,
            SkewSpec skew)
        {
            // Before the fast path: an element declaring only a colour paints nothing, and its children's -inherit
            // reads what it declares.
            BoxShadowSilhouette.RecordDeclaredColours(element, classNames);
            var bound = _ctx.ShadowBindings.TryGetValue(element, out var binding);
            // Fast path: no shadow anywhere near this element.
            if (!bound && !StyleShadowClass.HasShadowClass(classNames))
            {
                return;
            }

            var spec = System.Array.Empty<ShadowSpec>();
            var want = !clipActive && StyleShadowClass.TryExtract(classNames, out spec);

            // ApplySkewOnPatch ran before this (the shared pass order), so the SkewBindings entry is in its
            // post-patch state: a skewed caster's face is owned by its SkewSilhouette, an upright caster's by
            // this shadow paint. Tracks skew-y too (an X-angle-0 yet skewed caster keeps a SkewBinding).
            var casterSkewed = _ctx.SkewBindings.ContainsKey(element);

            if (want && bound)
            {
                BoxShadowSilhouette.Sync(element, binding, new BoxShadowSyncRequest
                {
                    Layers = spec,
                    ClassNames = classNames,
                    SkewXDeg = skew.XDeg,
                    SkewYDeg = skew.YDeg,
                    CasterSkewed = casterSkewed,
                });
                BoxShadowSilhouette.SetWantSpacer(element, binding, WrapperInfrastructure.CarriesFilter(_ctx, element, classNames), classNames);
            }
            else if (want)
            {
                var fresh = BoxShadowSilhouette.Attach(element, spec, classNames, skew.XDeg, skew.YDeg, casterSkewed);
                _ctx.ShadowBindings[element] = fresh;
                BoxShadowSilhouette.SetWantSpacer(element, fresh, WrapperInfrastructure.CarriesFilter(_ctx, element, classNames), classNames);
            }
            else if (bound)
            {
                BoxShadowSilhouette.Detach(element, binding);
                _ctx.ShadowBindings.Remove(element);
            }
        }
    }
}
