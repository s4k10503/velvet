#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Everything that routes pointer/focus input across the boundary between a Velvet-managed host
    // panel (V.Portal(layer:) / V.WorldSpace) and the panel its content logically belongs to. Two
    // distinct mechanisms live in this one file because they are the two ends of the same pipe —
    // reading either without the other misses half of how a cross-panel event actually travels:
    //
    //   FiberCrossPanelEventDispatcher: an event that ALREADY reached a host panel (native dispatch
    //   bubbled it there, inside that panel) continues OUTWARD toward the logical ancestor chain —
    //   the "exit" side.
    //
    //   FiberCrossPanelPointerRouter: an event arriving at the MAIN panel is redirected INTO a
    //   higher-priority host panel FIRST, before the main panel's own dispatch processes it — the
    //   "entry" side.
    //
    // Both share ReconcilerContext.LayerHosts / WorldSpaceBindings (which panels exist) and
    // FiberEventBindingManager.TryInvokeSynthetic (how a handler is invoked once the right element is
    // found) — see those for the rest of the mechanism this file builds on.

    // Bridges a native UI Toolkit event that finished bubbling within one panel (a V.Portal(layer:) or
    // V.WorldSpace host panel) toward the logical ancestor chain OUTSIDE that panel. A host panel's
    // rootVisualElement has no physical parent for native bubbling to continue into — it is a wholly
    // separate Panel/PanelSettings/UIDocument from whatever panel logically encloses the
    // V.Portal/V.WorldSpace call site.
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

    // Ensures a discrete pointer event that arrives on the MAIN panel is redirected to a
    // higher-sortingOrder Velvet-managed LAYER host panel (V.Portal(layer:)) FIRST, when that panel's
    // own content actually sits at the same screen position — before the main panel's native dispatch
    // is allowed to process the event.
    //
    // Why Velvet arbitrates this itself instead of trusting Unity's own runtime input system: Unity's
    // manual states the runtime event system "dispatches pointer events to their panels based on their
    // sorting order" when multiple PanelSettings coexist, but this has a documented gap (Unity Issue
    // Tracker: "Click event passes through overlapping UIDocument's VisualElement") — not reliable
    // enough to depend on for a framework's own layering primitives. This router does its own explicit
    // arbitration using each candidate panel's OWN IPanel.Pick(), which resolves reliably against that
    // panel's own content independent of any other panel's presence or overlap.
    //
    // Scope: pointer events only (PointerDown/PointerUp), and screen-space LAYER hosts only — NOT
    // V.WorldSpace. RuntimePanelUtils.ScreenToPanel/CameraTransformWorldToPanel (used below via
    // PanelToScreen) are for UI Toolkit's OLDER RenderTexture-on-a-mesh workflow: verified empirically
    // (not just from docs) that both return the input essentially unchanged against a Transform-driven
    // PanelRenderMode.WorldSpace panel, i.e. they silently no-op rather than performing the documented
    // transform. World-space panels instead rely entirely on Unity's own implicit runtime input system
    // picking up their Collider (see PanelHostFactory.AttachWorldSpaceCollider) — Velvet cannot
    // substitute a manual Pick() for them the way it does for screen-space layers, since the coordinate
    // conversion these APIs would need is an internal-only code path (WorldSpaceInput.Pick3D /
    // PickDocument3D — the containing class is `internal`) not reachable from a package assembly.
    // Key events also route by FOCUS, not position — a separate concern from this class.
    internal static class FiberCrossPanelPointerRouter
    {
        // Registered once per main panel (ReconcilerContext.CrossPanelRouterAttached guards against a
        // second V.Mount call onto the same target re-registering). TrickleDown so this runs BEFORE the
        // main panel's own element handlers get a chance — rerouting must preempt them, not race them.
        internal static void AttachToMainPanel(VisualElement mainPanelRoot, ReconcilerContext ctx)
        {
            mainPanelRoot.RegisterCallback<PointerDownEvent>(
                evt => TryReroute(evt, mainPanelRoot, ctx), TrickleDown.TrickleDown);
            mainPanelRoot.RegisterCallback<PointerUpEvent>(
                evt => TryReroute(evt, mainPanelRoot, ctx), TrickleDown.TrickleDown);
        }

        private static void TryReroute<TEvent>(TEvent evt, VisualElement mainPanelRoot, ReconcilerContext ctx)
            where TEvent : PointerEventBase<TEvent>, new()
        {
            var mainPanel = mainPanelRoot.panel;
            if (mainPanel == null) return;

            List<PanelHostRecord>? candidates = null;
            foreach (var kvp in ctx.LayerHosts)
            {
                (candidates ??= new List<PanelHostRecord>()).Add(kvp.Value);
            }
            if (candidates == null) return;

            // Highest sortingOrder (drawn frontmost) wins first pick, matching PanelHostFactory's own
            // draw-order semantics (Background < main < Overlay < Topmost).
            candidates.Sort((a, b) => (b.Settings?.sortingOrder ?? 0f).CompareTo(a.Settings?.sortingOrder ?? 0f));

            var screenPos = PanelToScreen(mainPanel, evt.position);
            foreach (var record in candidates)
            {
                var hostPanel = record.Document != null ? record.Document.rootVisualElement?.panel : null;
                if (hostPanel == null || ReferenceEquals(hostPanel, mainPanel)) continue;

                var localPos = RuntimePanelUtils.ScreenToPanel(hostPanel, screenPos);
                var hit = hostPanel.Pick(localPos);
                if (hit == null) continue;

                // A higher-priority host panel actually has content at this screen position — it wins.
                // The main panel's own dispatch for THIS event must not also process it (StopImmediate,
                // not just Stop, so no sibling TrickleDown listener on this same element runs either).
                ctx.EventManager.TryInvokeSynthetic(hit, evt);
                evt.StopImmediatePropagation();
                return;
            }
        }

        // RuntimePanelUtils exposes ScreenToPanel but no inverse; UI Toolkit runtime panels use a pure
        // 2D scale+translate between screen and panel space (never rotation), so the inverse affine
        // transform is derived from two well-separated screen samples rather than hand-deriving it from
        // PanelSettings' own scale-mode math (which differs per scaleMode and DPI configuration).
        private static Vector2 PanelToScreen(IPanel panel, Vector2 panelPosition)
        {
            var screenA = Vector2.zero;
            var screenB = new Vector2(Screen.width, Screen.height);
            var localA = RuntimePanelUtils.ScreenToPanel(panel, screenA);
            var localB = RuntimePanelUtils.ScreenToPanel(panel, screenB);

            var spanX = localB.x - localA.x;
            var spanY = localB.y - localA.y;
            var screenX = Mathf.Approximately(spanX, 0f)
                ? screenA.x
                : screenA.x + (panelPosition.x - localA.x) * (screenB.x - screenA.x) / spanX;
            var screenY = Mathf.Approximately(spanY, 0f)
                ? screenA.y
                : screenA.y + (panelPosition.y - localA.y) * (screenB.y - screenA.y) / spanY;
            return new Vector2(screenX, screenY);
        }
    }
}
