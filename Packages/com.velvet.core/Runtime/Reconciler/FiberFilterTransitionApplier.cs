using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Owns the tween binding of an element carrying transition-filter, so teardown and root disposal pause its tick.
    // Wrapper-less: the filter itself is applied by the arbitrary-value resolver, whose write hook
    // (StyleFilterTransitionDriver.TryStartOrRedirect) decides whether a change tweens and binds any element it
    // tweens on.
    internal sealed class FiberFilterTransitionApplier
    {
        private const string FilterTransitionClass = "transition-filter";

        private readonly ReconcilerContext _ctx;

        public FiberFilterTransitionApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        internal void ApplyFilterTransitionOnCreate(VisualElement element, string[] classNames)
        {
            // Whatever its classes, since a tween starts wherever the resolved lists run filter, not only under this one.
            MotionClock.Record(element, _ctx.StyleAnimationScheduler.Clock);
            if (!HasFilterTransitionClass(classNames))
            {
                return;
            }
            _ctx.FilterTransitionBindings[element] = StyleFilterTransitionDriver.Bind(element);
        }

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
                    _ctx.FilterTransitionBindings[element] = StyleFilterTransitionDriver.Bind(element);
                }
                return;
            }
            StyleFilterTransitionDriver.Detach(element, binding);
            _ctx.FilterTransitionBindings.Remove(element);
        }

        // True when the class list carries transition-filter in the base classes OR any state variant (peeling
        // variant layers to the leaf, mirroring CarriesFilter).
        private static bool HasFilterTransitionClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                // Same pass-over as StyleEaseCurve.NoteClasses: a peel leaves a suffix of its class.
                if (cls != null && cls.EndsWith(FilterTransitionClass, StringComparison.Ordinal)
                    && StyleVariantClass.LeafOf(cls) == FilterTransitionClass)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
