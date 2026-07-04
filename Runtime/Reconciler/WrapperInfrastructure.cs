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

        // Returns the inner real element when the input is a wrapper container.
        // Otherwise returns the input unchanged.
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
        // shrink-wrap. Documented in the velvet-ui skill; fixing it for one layer must fix both.
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
            element.RemoveFromHierarchy(); // out of the wrapper
            wrapper.RemoveFromHierarchy(); // wrapper leaves the parent
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
    }
}
