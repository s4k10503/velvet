#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
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
