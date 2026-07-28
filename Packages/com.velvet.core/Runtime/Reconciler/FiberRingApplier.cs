using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less layer for ring-*/outline-* utilities: a native-border overlay hosted as a
    // reconciler-invisible SIBLING of the ringed element (see RingOverlay for why a sibling rather than a
    // wrapper around it or a paint inside it). Adding no wrapper is what lets a ring be attached and detached
    // from a variant re-sync — which runs outside a reconcile pass, where parent surgery on a real slot is
    // forbidden — so focus:ring-* renders instead of toggling an inert class.
    internal sealed class FiberRingApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberRingApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point. The element is NOT yet in the hierarchy here; RingOverlay places the
        // overlay on the element's attach instead.
        internal void ApplyRingOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleRingClass.HasRingClass(classNames) || !StyleRingClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            // A clip-path-* on the same element clips the ring too (CSS semantics), and the clip is a
            // structural wrapper the ring no longer competes with — so this is a plain suppression gate,
            // exactly as ApplyShadowOnCreate does it. WantsClipWrapper (not just an active base clip) mirrors
            // the patch path's clipActive gate: variant activation state is not resolved at this call site, so
            // a clip VARIANT on a base-ring element suppresses the ring unconditionally. Pure base clip and
            // pure ring are unaffected.
            if (StyleClipPathClass.WantsClipWrapper(classNames))
            {
                return;
            }
            _ctx.RingBindings[element] = RingOverlay.Attach(element, spec, classNames);
            // The element is not in the hierarchy yet, so the overlay has no host to sit beside; the drain at
            // the reconcile boundary places it once the caller has inserted the element.
            RingOverlay.RequestPlacement(_ctx, element);
        }

        // Patch-time reconciliation of an element's ring state against its new class list. Mirrors the other
        // wrapper-less layers' four cases: update the existing overlay's spec, attach a newly-ringed element,
        // detach one whose ring was removed, or do nothing.
        // clipActive: whether the class list resolves to an active clip-path-* — resolved ONCE by the caller.
        internal void ApplyRingOnPatch(VisualElement element, string[] classNames, bool clipActive)
        {
            var bound = _ctx.RingBindings.TryGetValue(element, out var binding);
            // Fast path: no ring anywhere near this element.
            if (!bound && !StyleRingClass.HasRingClass(classNames))
            {
                return;
            }

            var spec = default(RingSpec);
            var want = !clipActive && StyleRingClass.TryExtract(classNames, out spec);

            if (want && bound)
            {
                RingOverlay.Sync(element, binding, spec, classNames);
            }
            else if (want)
            {
                _ctx.RingBindings[element] = RingOverlay.Attach(element, spec, classNames);
                // A patch-time attach usually has its parent already, and Attach places the overlay itself;
                // queue a retry anyway for the one case that does not — an element created earlier in this
                // same pass whose ring arrived through a variant payload before the caller inserted it.
                RingOverlay.RequestPlacement(_ctx, element);
            }
            else if (bound)
            {
                RingOverlay.Detach(element, binding);
                _ctx.RingBindings.Remove(element);
            }
        }
    }
}
