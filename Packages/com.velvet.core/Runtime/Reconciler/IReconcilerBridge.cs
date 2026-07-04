using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Bridge interface that decouples FiberVirtualListController from Reconciler.
    internal interface IReconcilerBridge
    {
        VisualElement CreateElementForController(VNode node);
        void CleanupElementForController(VisualElement element);

        // Patches an item element previously returned by CreateElementForController or by this
        // method. element may be a structural WRAPPER (shadow-* / clip-path-* item roots —
        // CreateElement returns the wrapper): the implementation resolves the real inner before
        // PatchNode, mirroring ChildReconciler. Returns the slot's CURRENT top-level element —
        // a class-driven wrap/unwrap during the patch swaps which element must be (re)mounted, so
        // the controller must store this return value, not its stale pre-patch reference.
        VisualElement PatchNodeForController(VisualElement element, VNode oldNode, VNode newNode);

        // Brackets a controller's item create/patch loop so the items inherit the context that enclosed the
        // V.VirtualList. Items mount outside the reconcile pass (scroll / geometry callbacks), where the
        // context cursor is empty and FiberStack.Current is null — which would give each item fiber a null
        // parent and hence its own fresh, empty ReconcilerContext. Begin pushes the host fiber that rendered
        // the list onto FiberStack (so item fibers parent under it and share its context) and restores the
        // captured enclosing-context snapshot onto the live cursor; End unwinds both and stamps the item
        // fibers created within the scope with a DetachedMountContext so an isolated re-render can rebuild
        // the same context via FiberContextSpine. host may be null (no enclosing host captured — e.g. a unit
        // test driving the controller directly); then the scope is context-only and no stamping occurs. The
        // returned token must be passed back to End (carries the pre-scope child set for stamping; supports
        // nested VirtualLists).
        object? BeginDetachedItemScope(ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext);

        // Stamps the item fibers newly added under host since the last call with a DetachedMountContext whose
        // DescendantNodes is THIS item's rendered vnode — so an isolated re-render can rebuild not just the
        // list's enclosing context but also any Provider the renderer placed above the item's consumer (the
        // VirtualList parallel to Portal's drained-children walk). Call once per item, after its create/patch;
        // reused items add no fibers, so the per-item set diff stamps nothing for them. No-op when host is null.
        void StampDetachedItemFibers(
            ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext, VNode itemVnode, object? scopeToken);

        void EndDetachedItemScope(
            ComponentFiber? host, List<KeyValuePair<object, object>>? enclosingContext, object? scopeToken);
    }
}
