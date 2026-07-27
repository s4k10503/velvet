using UnityEngine.UIElements;

namespace Velvet
{
    // Owns the lifecycle of the per-element binding that lets a later inline filter change (a hover variant,
    // a reconcile-diff value swap) animate instead of snapping. Wrapper-less: the filter itself is applied by
    // the arbitrary-value resolver, which the tween's write hook sits inside.
    internal sealed class FiberFilterTransitionApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberFilterTransitionApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point: when classNames carries transition-filter, registers the tween binding so a
        // later filter change (a hover variant, a reconcile-diff value swap) animates instead of snapping.
        // Wrapper-less — the tween drives the element's own inline filter. The binding is only the driver's
        // handle on the element; the filter itself is applied by the arbitrary-value resolver, which the
        // tween's write hook (StyleFilterTransitionDriver.TryStartOrRedirect) sits inside.
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

        // Patch-time reconciliation of the binding against the new class list: attach it when transition-filter
        // first appears, keep it while present, tear it down when removed. NOTE: on the very patch that ADDS
        // the class, a filter value that changes in the same diff still applies
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

        // True when the class list carries transition-filter in the base classes OR any state variant (peeling
        // variant layers to the leaf, mirroring CarriesFilter). Scoped to that one token because it is the only
        // utility that can resolve transition-property to a list naming filter, which is the only value
        // StyleFilterTransitionDriver's probe accepts: .transition-all is declared later in the bundled sheet
        // at equal specificity, so it always wins the cascade over this class, and every other transition-*
        // names unrelated properties. Registering wider would put a binding on the element-creation path for a
        // tween the probe would reject on every write. The probe still has the final say — this only decides
        // where a binding exists for it to consult.
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
