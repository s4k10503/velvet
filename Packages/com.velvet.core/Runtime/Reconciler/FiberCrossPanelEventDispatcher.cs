#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // Bridges a native UI Toolkit event that finished bubbling within one panel (a V.Portal(layer:) or
    // V.WorldSpace host panel) toward the logical ancestor chain OUTSIDE that panel. A host panel's
    // rootVisualElement has no physical parent for native bubbling to continue into — it is a wholly
    // separate Panel/PanelSettings/UIDocument from whatever panel logically encloses the
    // V.Portal/V.WorldSpace call site (ReconcilerContext.LayerHosts / WorldSpaceBindings).
    //
    // Mirrors React's own root-level event delegation (a single listener per event type at the DOM
    // root, walking Fiber.return instead of the DOM to build the dispatch order — see
    // DOMPluginEventSystem.js's accumulateSinglePhaseListeners) with one deliberate adaptation: Velvet
    // does NOT move every event to root-level delegation. Ordinary same-panel bubbling stays exactly as
    // UI Toolkit's own native dispatch already does it (FiberEventBindingManager.Bind's direct
    // RegisterCallback<T> registrations on each element), since that already agrees with the logical
    // tree everywhere except at a portal/world-space boundary. This dispatcher only takes over at the
    // ONE seam where physical bubbling structurally cannot continue: a host panel's root.
    //
    // Known limitation: resolving "which ComponentFiber logically owns this event" depends on
    // FiberNodeFactory stamping element.userData with _ctx.FiberStack.Current at CreateElement time —
    // which is only meaningful while a component's Body is actually rendering. A Portal/WorldSpace
    // child that is a bare host element (e.g. V.Portal(children: [V.Div(...)])) with no enclosing
    // V.Component gets userData stamped from whatever fiber happens to be current during the deferred
    // drain (see ChildReconciler.DrainPendingPortalMounts), which is not reliably the logical
    // enclosing component. Wrap portal/world-space children in a component to get correct cross-panel
    // bubbling; a bare element's own events: handlers still fire normally (native, same-panel), only
    // its FURTHER bubbling past the panel boundary is affected.
    internal static class FiberCrossPanelEventDispatcher
    {
        // Registers one BubbleUp listener per synthetic-bubbling-eligible event type on a newly created
        // host panel's root — called once, when PanelHostFactory creates the panel (layer or
        // world-space). Each listener fires only after UI Toolkit's own native dispatch has already
        // bubbled the event through every element inside this panel (BubbleUp is the last phase to
        // run), so nothing here duplicates a handler UI Toolkit's own dispatcher already invoked.
        // Matches the event set FiberEventBindingManager.TryInvokeSynthetic supports — see its own
        // comment for why ClickedBinding/ChangeEventBinding<T> are excluded.
        internal static void AttachBridge(VisualElement panelRoot, ReconcilerContext ctx)
        {
            panelRoot.RegisterCallback<PointerDownEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<PointerUpEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<PointerMoveEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<PointerEnterEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<PointerLeaveEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<WheelEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<KeyDownEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<KeyUpEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<FocusInEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
            panelRoot.RegisterCallback<FocusOutEvent>(evt => Continue(evt, evt.target as VisualElement, ctx));
        }

        private static void Continue(EventBase evt, VisualElement? target, ReconcilerContext ctx)
        {
            if (target == null) return;

            // Walk from the native target up to (and including) the panel root, looking for the
            // element whose owning fiber carries a DetachedMountContext — stamped only on the
            // top-level child(ren) a Portal/WorldSpace drain produced (DrainPendingPortalMounts). This
            // is a metadata scan only (no handler invocation along the way), so it cannot double-fire
            // anything even though it retraces ground the native dispatch already covered.
            ComponentFiber? logicalParent = null;
            for (var current = target; current != null; current = current.parent)
            {
                if (current.userData is ComponentFiber { DetachedMountContext: { } dmc })
                {
                    logicalParent = dmc.LogicalParent;
                    break;
                }
            }

            if (logicalParent?.MountPoint == null) return;

            // From here on, walk outward from the logical ancestor's OWN physical location, invoking
            // each element's own synthetic handler directly. This is genuinely new ground the native
            // dispatch never reached — logicalParent.MountPoint lives in a completely different Panel
            // than evt's original native target.
            for (var current = logicalParent.MountPoint; current != null; current = NextLogicalAncestor(current))
            {
                ctx.EventManager.TryInvokeSynthetic(current, evt);
            }
        }

        // Advances one step further up the synthetic chain: the ordinary physical parent, UNLESS
        // current is itself another portal/world-space boundary's top-level child (a nested portal),
        // in which case the walk must hop through ITS OWN LogicalParent.MountPoint the same way — a
        // plain VisualElement.parent walk would silently stop at that panel's root, same as the outer
        // walk in Continue above.
        private static VisualElement? NextLogicalAncestor(VisualElement current)
        {
            if (current.userData is ComponentFiber { DetachedMountContext: { } dmc })
            {
                return dmc.LogicalParent?.MountPoint;
            }
            return current.parent;
        }
    }
}
