using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Facade interface used by Reconciler subsystems (FiberNodeFactory,
    // FiberElementCleaner, FiberNodePatcher) instead of referencing one another directly.
    // Centralizes the host-platform element operations (create / remove / patch) behind one
    // injection point so the reconciler core stays decoupled from concrete VisualElement handling.
    internal interface IReconcilerHost
    {
        // Operations provided by FiberNodeFactory
        VisualElement CreateElement(VNode node);                                           // Used by Patcher
        List<(string key, VNode node)> BuildKeyedMapCopy(VNode?[] children);               // Used by ChildReconciler

        // Operations provided by FiberElementCleaner
        void RemoveElement(VisualElement parent, int index);                               // Used by Patcher only
        void RemoveElementDirect(VisualElement parent, VisualElement element);

        // Operations provided by ChildReconciler (used by Patcher)
        // Refreshes context snapshots and force-renders consumers under FiberStack.Current
        // for the given Provider's ContextProviderNode.ContextKey. Must be invoked while
        // the new value is already pushed on the live ComponentContextStack so the
        // propagated snapshot reflects it. Used by FiberNodePatcher's
        // ContextProviderNode case when the keyed Provider entry's value changes
        // between renders (e.g. inside an AnimatePresence subtree, where the Provider stays in the
        // keyed map rather than being inline-expanded by ChildReconciler.ExpandInlineRecursive).
        void NotifyContextValueChange(ContextProviderNode newProvider);

        // Implementations must always invoke ChildReconciler.Reconcile with
        // frameBudgetMs=0 (the default argument). Passing frameBudgetMs > 0
        // causes the internal _stopwatch.Restart() to be invoked twice, which
        // distorts the outer budget measurement.
        // ComponentNode children are always inline-mounted (no wrapper VE) — the body's expanded
        // VEs become direct children of parent. Function components emit no
        // element of their own, so this inline-mount is the single canonical expansion strategy
        // across initial mount and patch.
        // slotStart:
        // First child index in parent that this reconcile owns. Default 0
        // is used when the caller owns the entire children list. Pass parent.childCount on
        // initial mount to append a new range without disturbing children already placed by
        // earlier reconcile passes — required when multiple fibers (e.g. several Portals targeting
        // the same DOM node) share parent.
        void ReconcileChildren(VisualElement parent, VNode?[] oldChildren, VNode?[] newChildren, int slotStart = 0);
    }
}
