using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The structural-WRAPPER layer for ring-*/outline-* utilities. UI Toolkit has no CSS box-shadow /
    // outline, so the outset (or inset) HARD border these utilities describe is drawn as a native
    // rounded-border OVERLAY element — hardware-rendered, follows rounded-* corners, with no custom
    // material / draw-order hazard (unlike the soft, blurred drop shadow, which needs an SDF shader).
    // Lower precedence than clip-path (FiberClipPathApplier): a clipped element carries no ring wrapper,
    // since the two structural wrappers are mutually exclusive (one per element). The drop shadow is a
    // wrapper-less paint, so a ring composes with a shadow (it does not compete).
    internal sealed class FiberRingApplier
    {
        private readonly ReconcilerContext _ctx;
        private readonly WrapperInfrastructure _wrappers;

        public FiberRingApplier(ReconcilerContext ctx, WrapperInfrastructure wrappers)
        {
            _ctx = ctx;
            _wrappers = wrappers;
        }

        // Create-time entry point: when classNames resolves to a ring/outline (and the element was not already
        // wrapped), wraps element in a ring container and returns the wrapper; else returns element unchanged.
        // Mirrors ApplyShadowOnCreate. The factory calls this AFTER clip-path and shadow (lowest precedence).
        internal VisualElement ApplyRingOnCreate(VisualElement element, string[] classNames)
        {
            if (!StyleRingClass.HasRingClass(classNames) || !StyleRingClass.TryExtract(classNames, out var spec))
            {
                return element;
            }
            return BuildRingWrapper(element, spec, classNames);
        }

        // Patch-time reconciliation of an element's ring state. element is the resolved INNER. Mirrors
        // ApplyShadowOnPatch's four cases (update / wrap / unwrap / nothing). suppress is true when a
        // higher-precedence layer (clip-path or shadow) owns the wrapper, so the ring must not also wrap
        // (mutual exclusion) — a suppressed element with an existing ring binding is unwrapped. allowWrap is
        // false on the Motion patch path (a structural wrapper would become the AnimatePresence enter/exit
        // anchor while the transition stays on the inner Motion, breaking it — same rule the shadow layer keeps).
        internal void ApplyRingOnPatch(VisualElement element, string[] classNames, bool suppress, bool allowWrap)
        {
            var wrapped = _ctx.RingBindings.TryGetValue(element, out var binding);
            if (!wrapped && !StyleRingClass.HasRingClass(classNames))
            {
                return;
            }

            var spec = default(RingSpec);
            var want = !suppress && StyleRingClass.TryExtract(classNames, out spec);

            if (want && wrapped)
            {
                binding.ClassNames = classNames;
                binding.Spec = spec;
                ApplyRingSpec(binding.Overlay, spec);
                SyncRingGeometry(element, binding, classNames);
            }
            else if (want)
            {
                if (!allowWrap || _wrappers.IsAlreadyWrapped(element))
                {
                    return;
                }
                WrapRingInPlace(element, spec, classNames);
            }
            else if (wrapped)
            {
                WrapperInfrastructure.UnwrapRingInPlace(_ctx, element, binding);
            }
        }

        // Builds the ring wrapper around element: a layout-passthrough container holding element plus an
        // absolutely-positioned native-border overlay as its LAST child (so an inset band paints over the
        // inner edge; an outset band never overlaps the inner anyway). Does NOT touch any parent — the caller
        // inserts the returned wrapper.
        private VisualElement BuildRingWrapper(VisualElement element, RingSpec spec, string[] classNames)
        {
            var wrapper = WrapperInfrastructure.CreatePassthroughWrapper(FiberWrapperElementAppliers.RingWrapperClass);

            var overlay = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { position = Position.Absolute, backgroundColor = Color.clear },
            };
            ApplyRingSpec(overlay, spec);

            wrapper.Add(element); // reparents element from its current parent (if any) into the wrapper
            wrapper.Add(overlay);

            var binding = new RingBinding(wrapper, overlay) { ClassNames = classNames, Spec = spec };
            binding.OnGeometry = _ => SyncRingGeometry(element, binding, binding.ClassNames);
            element.RegisterCallback(binding.OnGeometry);

            _ctx.RingBindings[element] = binding;
            _ctx.WrapperToInnerMap[wrapper] = element;

            // Resolve geometry now so EditMode / pre-layout reads a sensible band without a tick.
            SyncRingGeometry(element, binding, classNames);
            return wrapper;
        }

        private void WrapRingInPlace(VisualElement element, RingSpec spec, string[] classNames)
        {
            var parent = element.parent;
            if (parent == null)
            {
                BuildRingWrapper(element, spec, classNames);
                return;
            }
            var index = parent.IndexOf(element);
            var wrapper = BuildRingWrapper(element, spec, classNames); // removes element from parent
            parent.Insert(index, wrapper);
        }

        // Paints the spec onto the overlay (native border width + color, all four sides). The band's geometry
        // (size / position / corner radius) is set by SyncRingGeometry once the inner is laid out.
        private static void ApplyRingSpec(VisualElement overlay, RingSpec spec)
        {
            overlay.style.borderTopWidth = spec.Width;
            overlay.style.borderRightWidth = spec.Width;
            overlay.style.borderBottomWidth = spec.Width;
            overlay.style.borderLeftWidth = spec.Width;
            overlay.style.borderTopColor = spec.Color;
            overlay.style.borderRightColor = spec.Color;
            overlay.style.borderBottomColor = spec.Color;
            overlay.style.borderLeftColor = spec.Color;
        }

        // Keeps the ring overlay tracking its target: forwards the inner's flex to the wrapper, then sizes and
        // positions the overlay to the inner's resolved box. Outset (default): the band sits OUTSIDE the inner
        // edge by Offset, so the overlay inflates by (Offset + Width) per side and its outer corner radius is
        // innerRadius + Offset + Width. Inset (ring-inset): the band sits inside, so the overlay matches the
        // inner box exactly at the inner radius. Radius prefers the laid-out resolvedStyle.borderTopLeftRadius
        // (handles %, arbitrary, inline radii), falling back to the rounded-* class scale pre-layout. Pre-layout
        // (no resolved size) it defers to the geometry callback.
        private static void SyncRingGeometry(VisualElement element, RingBinding binding, string[] classNames)
        {
            if (binding == null)
            {
                return;
            }
            var overlay = binding.Overlay;
            var spec = binding.Spec;

            float innerRadius;
            var resolvedRadius = element.resolvedStyle.borderTopLeftRadius;
            // Prefer a NON-ZERO laid-out radius (handles %, arbitrary, inline radii). The `> 0f` is deliberate:
            // a USS rounded-* class does not always reflect into
            // resolvedStyle.borderTopLeftRadius off-screen / pre-layout (it reads 0 there), so a resolved 0
            // must fall back to the rounded-* class scale rather than being trusted as "no rounding" — else a
            // rounded card would get a square ring. A genuine no-rounding element resolves 0 here and also
            // misses the class scale, landing at 0 correctly.
            if (element.panel != null && !float.IsNaN(resolvedRadius) && resolvedRadius > 0f)
            {
                innerRadius = resolvedRadius;
            }
            else if (StyleRingClass.TryResolveCornerRadius(classNames, out var classRadius))
            {
                innerRadius = classRadius;
            }
            else
            {
                innerRadius = 0f;
            }

            WrapperInfrastructure.ForwardInnerFlexToWrapper(element, binding.Wrapper);

            var width = element.resolvedStyle.width;
            var height = element.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0)
            {
                return;
            }
            var originX = element.layout.x;
            var originY = element.layout.y;
            if (float.IsNaN(originX)) originX = 0f;
            if (float.IsNaN(originY)) originY = 0f;

            var grow = spec.Inset ? 0f : spec.Offset + spec.Width;
            overlay.style.left = originX - grow;
            overlay.style.top = originY - grow;
            overlay.style.width = width + (grow * 2f);
            overlay.style.height = height + (grow * 2f);

            var outerRadius = spec.Inset ? innerRadius : innerRadius + spec.Offset + spec.Width;
            overlay.style.borderTopLeftRadius = outerRadius;
            overlay.style.borderTopRightRadius = outerRadius;
            overlay.style.borderBottomLeftRadius = outerRadius;
            overlay.style.borderBottomRightRadius = outerRadius;
        }
    }
}
