using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Shared plumbing for the className-driven structural wrapper layers (shadow-*, ring-*,
    // clip-path-*). Both the patcher (wrapper<->inner resolution exposed to the reconciler) and the
    // wrapper element appliers (the wrap/unwrap surgery) depend on these, so the pieces below are the
    // parts whose two copies must never drift: the passthrough style block, the slot-preserving unwrap
    // surgery (which ChildReconciler's keyed-move re-fetch depends on), and the flex-forwarding contract.
    internal sealed class WrapperInfrastructure
    {
        private readonly ReconcilerContext _ctx;

        public WrapperInfrastructure(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        internal VisualElement ResolveWrapped(VisualElement domElement)
            => _ctx.WrapperToInnerMap.GetValueOrDefault(domElement, domElement);

        // The inverse of ResolveWrapped: the element's current top-level DOM node — its
        // wrapper when it is the inner of one, else itself. Callers that hold a pre-patch element
        // reference (the VirtualList bridge) use this after a patch, because a class-driven
        // wrap/unwrap during the patch swaps which element occupies the slot.
        internal VisualElement ResolveOuter(VisualElement element)
        {
            var parent = element.parent;
            return parent != null
                && _ctx.WrapperToInnerMap.TryGetValue(parent, out var inner)
                && ReferenceEquals(inner, element)
                ? parent
                : element;
        }

        // True when element is already the inner of a wrapper (its direct parent maps to
        // it in ReconcilerContext.WrapperToInnerMap) — e.g. a user wrapElement wrapper.
        // Used to avoid stacking a className clip/ring wrapper on top of an existing wrapper.
        // Same predicate as ResolveOuter (which returns the wrapper instead of a bool), so it is
        // expressed in those terms to keep the wrapper-identity rule defined in exactly one place.
        internal bool IsAlreadyWrapped(VisualElement element)
            => !ReferenceEquals(ResolveOuter(element), element);

        // A layout-passthrough wrapper: a positioning context whose centered inner stays on-origin
        // when a forwarded flex-grow enlarges the wrapper. KNOWN LIMITATION (CSS clip-path/shadow
        // are paint-only; this wrapper is not): only flexGrow/flexShrink are forwarded — an inner
        // with a percentage width in a row parent, or one relying on the parent's default
        // cross-axis stretch, sizes against the wrapper instead of the real parent and can
        // shrink-wrap. Both wrapper layers share this limitation, so fixing it for one must fix both.
        internal static VisualElement CreatePassthroughWrapper(string ussClass)
        {
            var wrapper = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Relative,
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                }
            };
            wrapper.AddToClassList(ussClass);
            return wrapper;
        }

        // Removes wrapper from the tree and restores element (its inner) at the wrapper's
        // slot. Binding-specific resource disposal and dictionary removal are the caller's job,
        // done BEFORE this. Keeping the element at the SAME index is what the child reconciler's
        // post-patch re-fetch tolerates (see ChildReconciler's keyed-move re-fetch).
        internal static void RemoveWrapperRestoreInner(VisualElement element, VisualElement wrapper)
        {
            var parent = wrapper.parent;
            if (parent == null)
            {
                element.RemoveFromHierarchy();
                return;
            }
            var index = parent.IndexOf(wrapper);
            element.RemoveFromHierarchy();
            wrapper.RemoveFromHierarchy();
            parent.Insert(index, element);
        }

        // Forwards the inner's resolved flex participation onto its passthrough wrapper so a
        // flex-grow/shrink declared on the inner acts on the wrapper (the element the parent
        // actually lays out). Shared by both wrapper layers' geometry syncs.
        internal static void ForwardInnerFlexToWrapper(VisualElement element, VisualElement wrapper)
        {
            var flexGrow = element.resolvedStyle.flexGrow;
            if (!float.IsNaN(flexGrow))
            {
                wrapper.style.flexGrow = flexGrow;
            }
            var flexShrink = element.resolvedStyle.flexShrink;
            if (!float.IsNaN(flexShrink))
            {
                wrapper.style.flexShrink = flexShrink;
            }
        }

        // True when the class list carries an inline filter — a static filter-* utility or the animate-hue
        // motion (which drives style.filter every frame) — in the base classes OR any state variant. A filter
        // promotes the element to an offscreen render tree sized to its layout boundingBox, which clips a
        // sheared silhouette / shadow bleed; the paint layers answer with a bounds-spacer (SilhouetteBoundsSpacer)
        // that widens boundingBox. The spacer must exist whenever a filter COULD apply (a variant applies its
        // payload at state time, outside this reconcile pass), so both checks peel variant layers to the leaf.
        // Shared by the skew, drop-shadow and particles spacer gates above, so it lives here rather than on any
        // one of those subsystems.
        internal static bool CarriesFilter(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            if (StyleFilterValueParser.HasFilterClass(classNames))
            {
                return true;
            }
            foreach (var cls in classNames)
            {
                var leaf = cls;
                while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                {
                    leaf = payload;
                }
                if (leaf != null && leaf.StartsWith("animate-hue", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Removes the ring wrapper, restoring element at the wrapper's slot. Shared by the ring layer's own
        // unwrap case and the clip-path layer's wrapper swap (a clip added to an already ring-wrapped
        // element steals the wrapper slot), so it lives here rather than on either one, taking the owning
        // ReconcilerContext explicitly since it has no instance of its own to read it from.
        internal static void UnwrapRingInPlace(ReconcilerContext ctx, VisualElement element, RingBinding binding)
        {
            if (binding.OnGeometry != null)
            {
                element.UnregisterCallback(binding.OnGeometry);
            }
            ctx.RingBindings.Remove(element);
            ctx.WrapperToInnerMap.Remove(binding.Wrapper);
            WrapperInfrastructure.RemoveWrapperRestoreInner(element, binding.Wrapper);
        }
    }
}
