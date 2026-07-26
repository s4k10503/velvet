using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less PAINT layer for border-dashed / border-dotted utilities: a dashed-outline painter
    // drawn via the element's own generateVisualContent. Defers to skew / shadow (FiberSkewApplier /
    // FiberDropShadowApplier) when either already owns the whole face, since both repaint a solid border
    // themselves and would otherwise fight a dashed outline on the same element.
    internal sealed class FiberBorderStyleApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberBorderStyleApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames resolves to an active border-dashed / border-dotted, wires
        // the dashed-outline painter onto the element (no structural wrapper — the outline is the element's own
        // generateVisualContent). Defers to skew / shadow: either owns the whole face and already repaints a
        // solid border, so a dashed outline on the same element would fight it — border-dashed + skew/shadow
        // keeps a SOLID border (documented v1 limitation). ApplySkewOnCreate / ApplyShadowOnCreate ran before
        // this in the factory order, so their bindings are already present to gate on.
        internal void ApplyBorderStyleOnCreate(VisualElement element, string[] classNames)
        {
            if (_ctx.SkewBindings.ContainsKey(element) || _ctx.ShadowBindings.ContainsKey(element))
            {
                return;
            }
            if (!StyleBorderStyleClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            _ctx.BorderStyleBindings[element] = BorderStyleSilhouette.Attach(element, spec);
        }

        // Patch-time reconciliation of an element's border line-style against its new class list. Mirrors the
        // skew paint layer's four cases (update the spec, attach, detach, or no-op the steady state). Runs AFTER
        // ApplySkewOnPatch and ApplyShadowOnPatch (PatchElement order) so it observes the final post-patch skew
        // / shadow ownership: while either owns the face the layer stands down (defer/detach), so an add/remove
        // of skew/shadow in the same patch resolves without a race.
        internal void ApplyBorderStyleOnPatch(VisualElement element, string[] oldClassNames, string[] newClassNames)
        {
            var bound = _ctx.BorderStyleBindings.TryGetValue(element, out var binding);
            var has = StyleBorderStyleClass.TryGetWinningBorderStyleClass(newClassNames, out var winner);
            // Skew or shadow owning the face repaints a solid border itself, so the dashed layer must stand down.
            var deferred = _ctx.SkewBindings.ContainsKey(element) || _ctx.ShadowBindings.ContainsKey(element);

            // Fast path: no border-style anywhere near this element (and none deferred away that we still track).
            if (!bound && (!has || deferred))
            {
                return;
            }

            var classesChanged = !ReferenceEquals(oldClassNames, newClassNames);

            // Steady state: the winning token is exactly what the live binding was built from and no higher
            // layer took the face — skip the parse, but keep the color stash in sync with this patch's styling.
            if (bound && has && !deferred && binding.Spec.Source == winner)
            {
                BorderStyleSilhouette.SyncStashOnPatch(element, binding, classesChanged);
                return;
            }

            BorderStyleSpec spec = default;
            var want = !deferred && has && StyleBorderStyleClass.TryExtract(newClassNames, out spec);

            if (want && bound)
            {
                binding.Spec = spec;
                BorderStyleSilhouette.SyncStashOnPatch(element, binding, classesChanged: true);
                element.MarkDirtyRepaint();
                return;
            }
            if (want)
            {
                var fresh = BorderStyleSilhouette.Attach(element, spec);
                _ctx.BorderStyleBindings[element] = fresh;
                BorderStyleSilhouette.SyncStashOnPatch(element, fresh, classesChanged: true);
                return;
            }
            if (bound)
            {
                // A skew / shadow layer took the whole face in this same patch (deferred): hand it the captured
                // border color and tear down WITHOUT releasing the suppression — that layer re-applied the same
                // sentinel to the shared inline slot when it attached earlier in this patch, and its own Capture
                // could read back only that sentinel, so it needs this color to repaint the border; releasing here
                // would null the slot and re-expose the native rectangle behind the sheared / shadowed face. With
                // no higher layer (the border-style classes were simply removed) this is a plain, releasing detach.
                if (deferred)
                {
                    HandOffBorderFace(element, binding);
                }
                else
                {
                    BorderStyleSilhouette.Detach(element, binding);
                }
                _ctx.BorderStyleBindings.Remove(element);
            }
        }

        // Hands a deferred dashed layer's captured border color to whichever higher layer (skew or shadow) took the
        // face in this patch, then tears the dashed binding down while preserving the suppression the new owner now
        // relies on. Skew is reconciled before shadow, so at most one of them owns the face.
        private void HandOffBorderFace(VisualElement element, BorderStyleBinding binding)
        {
            if (_ctx.SkewBindings.TryGetValue(element, out var skew))
            {
                skew.Face.AdoptBorderFace(binding.Face);
            }
            else if (_ctx.ShadowBindings.TryGetValue(element, out var shadow))
            {
                shadow.Face.AdoptBorderFace(binding.Face);
            }
            BorderStyleSilhouette.DetachPreservingSuppression(element, binding);
        }
    }
}
