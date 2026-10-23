using UnityEngine.UIElements;

namespace Velvet
{
    // The wrapper-less layer for backdrop-* utilities: an immediate-mode sibling placed before the element
    // (BackdropFilterLayer). Like the ring band, it adds no wrapper, so a variant re-sync can attach and detach it.
    // Unlike the band it stays under a clip-path-*: the layer sits inside the clip wrapper beside the element, so the
    // wrapper's mask clips it as CSS clips a backdrop-filter by the element's clip-path.
    internal sealed class FiberBackdropApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberBackdropApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Create-time entry point; the element is not in the hierarchy yet, so the layer is placed at the drain
        // (BackdropFilterLayer.DrainPendingPlacements).
        internal void ApplyBackdropOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleBackdropClass.HasBackdropClass(classNames) || !StyleBackdropClass.TryExtract(classNames, out var spec))
            {
                return;
            }
            _ctx.BackdropBindings[element] = BackdropFilterLayer.Attach(element, spec!);
        }

        internal void ApplyBackdropOnPatch(VisualElement element, string[] classNames)
        {
            var bound = _ctx.BackdropBindings.TryGetValue(element, out var binding);
            if (!bound && !StyleBackdropClass.HasBackdropClass(classNames))
            {
                return;
            }
            if (StyleBackdropClass.TryExtract(classNames, out var spec))
            {
                Want(element, binding, spec!);
            }
            else if (bound)
            {
                Release(element, binding);
            }
        }

        private void Want(VisualElement element, BackdropBinding? binding, BackdropSpec spec)
        {
            if (binding != null)
            {
                BackdropFilterLayer.Sync(element, binding, spec);
                return;
            }
            _ctx.BackdropBindings[element] = BackdropFilterLayer.Attach(element, spec);
        }

        // No class declares a backdrop-filter any more, which leaves it at none: a transition to none plays out
        // before the layer goes.
        private void Release(VisualElement element, BackdropBinding binding)
        {
            BackdropFilterLayer.Sync(element, binding, BackdropSpec.None);
            if (binding.Layer.Transitioning)
            {
                binding.OnSettledToNone = () => Detach(element, binding);
                return;
            }
            Detach(element, binding);
        }

        private void Detach(VisualElement element, BackdropBinding binding)
        {
            BackdropFilterLayer.Detach(element, binding);
            if (_ctx.BackdropBindings.TryGetValue(element, out var held) && held == binding)
            {
                _ctx.BackdropBindings.Remove(element);
            }
        }
    }
}
