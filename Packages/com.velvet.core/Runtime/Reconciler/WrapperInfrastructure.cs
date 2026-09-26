using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Shared plumbing for the structural wrapper layers — the className-driven clip-path-* wrapper and the
    // user wrapElement opt-in. Both the patcher (wrapper<->inner resolution exposed to the reconciler) and the
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
        // Used to avoid stacking a className clip wrapper on top of an existing wrapper.
        // Same predicate as ResolveOuter (which returns the wrapper instead of a bool), so it is
        // expressed in those terms to keep the wrapper-identity rule defined in exactly one place.
        internal bool IsAlreadyWrapped(VisualElement element)
            => !ReferenceEquals(ResolveOuter(element), element);

        // A layout-passthrough wrapper, laid out by ForwardLayoutContextToWrapper.
        internal static VisualElement CreatePassthroughWrapper(string ussClass)
        {
            var wrapper = new VisualElement { pickingMode = PickingMode.Ignore };
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
        // actually lays out).
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

        // CSS clip-path is paint-only, so the wrapper has to leave the inner where the parent would have put it.
        // In flow the parent lays out the wrapper in the inner's place: the wrapper takes the parent's
        // flex-direction, so the inner's flex-grow acts along the same axis inside it, and the inner's
        // align-self, so the parent aligns the wrapper as it would have aligned the inner. Out of flow the inner
        // resolves its edge offsets against the wrapper, so the wrapper leaves the flow, spans the real parent
        // (inset 0) and takes the parent's justify-content and align-items, which place an inner with no offset
        // on an axis. Run on every geometry sync rather than once at the wrap, because a patch can change the
        // inner's position after it. ClipPathWrapperFlowParityPanelTests compares the result against an
        // unclipped twin.
        // KNOWN LIMITATION: in flow the wrapper takes its size from the inner along the parent's main axis, and
        // along the cross axis unless the parent stretches it, so a percentage width, height or flex-basis on
        // such an axis resolves against the wrapper rather than the parent, and auto margins along the main axis
        // do not centre the inner. The justify-content and align-items an out-of-flow inner's wrapper took are
        // read again only at the next geometry sync, so a parent that changes nothing but its alignment leaves
        // them behind.
        internal static void ForwardLayoutContextToWrapper(VisualElement element, VisualElement wrapper)
        {
            var ws = wrapper.style;
            var parent = wrapper.parent;
            var parentStyle = parent != null && parent.panel != null ? parent.resolvedStyle : null;
            ws.flexDirection = parentStyle != null ? parentStyle.flexDirection : StyleKeyword.Null;
            if (StyleOutOfFlowChild.IsOutOfFlow(element))
            {
                ws.position = Position.Absolute;
                ws.left = 0f;
                ws.top = 0f;
                ws.right = 0f;
                ws.bottom = 0f;
                ws.alignSelf = StyleKeyword.Null;
                ws.justifyContent = parentStyle != null ? parentStyle.justifyContent : StyleKeyword.Null;
                ws.alignItems = parentStyle != null ? parentStyle.alignItems : StyleKeyword.Null;
                return;
            }
            ws.position = Position.Relative;
            ws.left = StyleKeyword.Null;
            ws.top = StyleKeyword.Null;
            ws.right = StyleKeyword.Null;
            ws.bottom = StyleKeyword.Null;
            ws.justifyContent = StyleKeyword.Null;
            ws.alignItems = StyleKeyword.Null;
            ws.alignSelf = element.panel != null ? element.resolvedStyle.alignSelf : StyleKeyword.Null;
        }

        // True when the class list carries an inline filter — a static filter-* utility or the animate-hue
        // motion (which drives style.filter every frame) — in the base classes OR any state variant. A filter
        // promotes the element to an offscreen render tree sized to its layout boundingBox, which clips a
        // sheared silhouette / shadow bleed; the paint layers answer with a bounds-spacer (SilhouetteBoundsSpacer)
        // that widens boundingBox. The spacer must exist whenever a filter COULD apply (a variant applies its
        // payload at state time, outside this reconcile pass), so both checks peel variant layers to the leaf.
        // Shared by the skew, drop-shadow and particles spacer gates above, so it lives here rather than on any
        // one of those subsystems. The ring is NOT one of them: its band is hosted beside the element rather
        // than inside the element's own offscreen filter tree, so no filter pass can clip it.
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
    }
}
