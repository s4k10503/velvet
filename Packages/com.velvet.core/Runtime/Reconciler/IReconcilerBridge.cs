using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Bridge interface that decouples FiberVirtualListController from Reconciler.
    internal interface IReconcilerBridge
    {
        VisualElement CreateElementForController(VNode node);
        void CleanupElementForController(VisualElement element);

        // A row the item loop creates or patches attaches its refs and commits its layout work once the controller
        // has placed it in the list, rather than where the row was created, for the reason
        // Reconciler.FinishTopLevelPass gives — and this is the seam that reaches it, because the item loop also
        // runs outside any reconcile pass (BeginDetachedItemScope names when). Hold opens a list of its own for one
        // range render, which Commit closes, running the held commits, each attaching the refs of the elements its
        // row owns; a row the range discarded, because the range threw or the list was disposed while it rendered,
        // is unmounted by then and runs nothing. Which queued ref entries a commit takes, and when none, is
        // ReconcilerContext.TakeRefAttaches's own, and inside a drain the rows' layout work is the drain's, which
        // commits it at the drain's end.
        void HoldRowLayoutEffects();
        void CommitHeldRowLayoutEffects();

        // A range render outside a pass is a render of its own: react-window's scroll and resize renders are state
        // updates, and React flushes pending passive effects before it renders one.
        void FlushPendingPassiveEffectsForController();

        // A range rendered outside a reconcile pass has no enclosing entry to end with
        // FiberEffects.CommitStrandedLayoutWork, so the controller calls this where a range render returns.
        void CommitStrandedLayoutWorkForController();

        // Patches an item element previously returned by CreateElementForController or by this
        // method. element may be a structural WRAPPER (shadow-* / clip-path-* item roots —
        // CreateElement returns the wrapper) or the placeholder of an element FiberZLayerCoordinator
        // relocated: the implementation patches what ChildReconciler would. Returns the slot's CURRENT
        // occupant — a class-driven wrap/unwrap or relocation during the patch swaps which element must
        // be (re)mounted, so the controller must store this return value, not its stale pre-patch reference.
        VisualElement PatchNodeForController(VisualElement element, VNode oldNode, VNode newNode);

        // Bracket the controller's rewrite of the container's rows, which leaves the elements
        // FiberZLayerCoordinator relocated out of the rows' slots standing: Begin before the first row is
        // removed, End once every row is placed.
        void BeginRowPlacementForController(VisualElement container);
        void EndRowPlacementForController();

        // The element that is the row, given the occupant of its slot: the one a placeholder stands for.
        VisualElement RowBoxForController(VisualElement occupant);

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
