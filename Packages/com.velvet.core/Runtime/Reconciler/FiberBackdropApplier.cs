using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less layer for backdrop-* utilities: an immediate-mode sibling placed before the element
    // (BackdropFilterLayer). Like the ring band, it adds no wrapper, so a variant re-sync can attach and detach it.
    internal sealed class FiberBackdropApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberBackdropApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point; the element is not in the hierarchy yet, so the layer is placed at the drain.
        // A clip-path-* on the element suppresses the layer, as it suppresses the ring (FiberRingApplier).
        internal void ApplyBackdropOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleBackdropClass.HasBackdropClass(classNames) || StyleClipPathClass.WantsClipWrapper(classNames)
                || !StyleBackdropClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            _ctx.BackdropBindings[element] = BackdropFilterLayer.Attach(element, spec!);
            BackdropFilterLayer.RequestPlacement(_ctx, element);
        }

        // clipActive: whether the class list resolves to an active clip-path-*, resolved once by the caller.
        internal void ApplyBackdropOnPatch(VisualElement element, string[] classNames, bool clipActive)
        {
            var bound = _ctx.BackdropBindings.TryGetValue(element, out var binding);
            if (!bound && !StyleBackdropClass.HasBackdropClass(classNames))
            {
                return;
            }
            BackdropSpec? spec = null;
            var want = !clipActive && StyleBackdropClass.TryExtract(classNames, out spec);
            if (want && bound)
            {
                BackdropFilterLayer.Sync(element, binding, spec!);
            }
            else if (want)
            {
                _ctx.BackdropBindings[element] = BackdropFilterLayer.Attach(element, spec!);
                BackdropFilterLayer.RequestPlacement(_ctx, element);
            }
            else if (bound)
            {
                BackdropFilterLayer.Detach(element, binding);
                _ctx.BackdropBindings.Remove(element);
            }
        }
    }
}
