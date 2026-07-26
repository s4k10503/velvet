using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less opt-in that makes a later inline filter change (a hover variant, a reconcile-diff
    // value swap) animate instead of snapping. The binding only ENABLES the tween; the filter itself is
    // applied by the arbitrary-value resolver, which the tween's write hook sits inside.
    internal sealed class FiberFilterTransitionApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberFilterTransitionApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames carries the transition-filter opt-in, registers the tween
        // binding so a later filter change (a hover variant, a reconcile-diff value swap) animates instead of
        // snapping. Wrapper-less — the tween drives the element's own inline filter. The binding only ENABLES
        // the tween; the filter itself is applied by the arbitrary-value resolver, which the tween's write hook
        // (StyleFilterTransitionDriver.TryStartOrRedirect) sits inside.
        internal void ApplyFilterTransitionOnCreate(VisualElement element, string[] classNames)
        {
            if (!HasFilterTransitionClass(classNames))
            {
                return;
            }
            var binding = new StyleFilterTransitionBinding();
            _ctx.FilterTransitionBindings[element] = binding;
            StyleFilterTransitionDriver.Register(element, binding);
        }

        // Patch-time reconciliation of the transition-filter opt-in against the new class list: attach the
        // binding when the class first appears, keep it while present, tear it down when removed. NOTE: on the
        // very patch that ADDS transition-filter, a filter value that changes in the same diff still applies
        // INSTANTLY — SyncClassDrivenStyling (which resolves and writes the filter) runs before this pass, so
        // the tween binding is not enabled yet when the write happens. This matches CSS, which does not
        // retroactively animate a value that changed in the same paint the transition first became active;
        // every SUBSEQUENT change transitions.
        internal void ApplyFilterTransitionOnPatch(VisualElement element, string[] classNames)
        {
            var bound = _ctx.FilterTransitionBindings.TryGetValue(element, out var binding);
            var want = HasFilterTransitionClass(classNames);
            if (!bound && !want)
            {
                return;
            }
            if (want)
            {
                if (!bound)
                {
                    var fresh = new StyleFilterTransitionBinding();
                    _ctx.FilterTransitionBindings[element] = fresh;
                    StyleFilterTransitionDriver.Register(element, fresh);
                }
                return;
            }
            StyleFilterTransitionDriver.Detach(element, binding);
            _ctx.FilterTransitionBindings.Remove(element);
        }

        // True when the class list carries the transition-filter opt-in in the base classes OR any state
        // variant (peeling variant layers to the leaf, mirroring CarriesFilter). Scoped to the literal
        // transition-filter token only — transition-all sets a real transition-property and rides UI Toolkit's
        // native transition system, which cannot repaint an inline filter, so it is deliberately NOT an opt-in.
        private static bool HasFilterTransitionClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                var leaf = cls;
                while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                {
                    leaf = payload;
                }
                if (leaf == "transition-filter")
                {
                    return true;
                }
            }
            return false;
        }
    }
}
