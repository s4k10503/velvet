#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Everything that routes pointer/focus input across the boundary between a Portal's/WorldSpace's
    // PHYSICAL attachment point and the panel its content LOGICALLY belongs to. That boundary is a
    // separate Velvet-managed host panel for V.Portal(layer:) / V.WorldSpace, or — same panel, no
    // separate Panel object involved — the registered target element itself for V.Portal(targetId:).
    // Two distinct mechanisms live in this one file because they are the two ends of the same pipe —
    // reading either without the other misses half of how a cross-panel event actually travels:
    //
    //   FiberCrossPanelEventDispatcher: an event that ALREADY reached a portal/world-space boundary
    //   (native dispatch bubbled it there) continues OUTWARD toward the logical ancestor chain — the
    //   "exit" side. This is the ONLY mechanism V.Portal(targetId:) needs — there is no "entry" side
    //   for it, since the event started in the same panel to begin with.
    //
    //   FiberCrossPanelPointerRouter: an event arriving at the MAIN panel is redirected INTO a
    //   higher-priority host panel FIRST, before the main panel's own dispatch processes it — the
    //   "entry" side. Screen-space layer hosts only (see its own comment); irrelevant to
    //   V.Portal(targetId:), which never creates a separate panel to route into.
    //
    // Both share ReconcilerContext.LayerHosts / WorldSpaceBindings (which panels exist) and
    // FiberEventBindingManager.TryInvokeSynthetic (how a handler is invoked once the right element is
    // found) — see those for the rest of the mechanism this file builds on.

    // Bridges a native UI Toolkit event that finished bubbling within one panel toward the logical
    // ancestor chain OUTSIDE the Portal/WorldSpace boundary it just bubbled through. AttachBridge is
    // called from two different kinds of place, distinguished by whether the returned unbind Action
    // matters:
    //   - PanelHostFactory.CreateLayerHost/CreateWorldSpaceHost, ONCE per framework-owned host panel,
    //     on that host's rootVisualElement. A host panel has no physical parent for native bubbling to
    //     continue into (a wholly separate Panel/PanelSettings/UIDocument), so this is the only way
    //     further bubbling can happen at all. The unbind Action is discarded here: the host root is
    //     destroyed wholesale with its GameObject (PanelHostFactory.Destroy), taking the callbacks
    //     with it.
    //   - ChildReconciler's same-panel drain branch, ONCE per resolved target of a V.Portal(targetId:)
    //     or V.Portal(target:) (see ReconcilerContext.SamePanelPortalBridges). A same-panel target DOES
    //     have a physical parent chain that keeps bubbling on its own, but that chain reflects the
    //     target's OWN position, not the Portal's LOGICAL one, so this bridge still needs to run to
    //     reach the latter. The unbind Action is retained and invoked when the last Portal on that
    //     target unmounts, or at Reconciler.Dispose: a same-panel target is an ordinary element the
    //     app or the tree owns, not a framework-owned host root destroyed wholesale.
    //
    // Ordinary same-panel bubbling stays UI Toolkit's own native dispatch (FiberEventBindingManager.Bind's
    // direct RegisterCallback<T> registrations on each element), which agrees with the logical tree
    // everywhere except at a portal boundary. The bridge supplies the rest: the logical ancestors of the
    // event's target that are not its physical ancestors, walked the way React walks a portal child's
    // return path — a portal's child answers to the position its placeholder holds (see LogicalParent).
    internal static class FiberCrossPanelEventDispatcher
    {
        // Registers one BubbleUp listener per synthetic-bubbling-eligible event type on bridgeAnchor —
        // either a newly created host panel's root (called once, from PanelHostFactory) or a resolved
        // same-panel target element (called once per target, from ChildReconciler's same-panel drain
        // branch — see ReconcilerContext.SamePanelPortalBridges for the attach-once guard).
        // Each listener fires only after UI Toolkit's own native dispatch has already bubbled the event
        // through every element AT OR BELOW bridgeAnchor (BubbleUp is the last phase to run on a given
        // element), so nothing here duplicates a handler UI Toolkit's own dispatcher already invoked at
        // or below that point. Matches the event set FiberEventBindingManager.TryInvokeSynthetic
        // supports.
        // Returns the delegate that undoes every registration below, for a caller that needs to detach
        // it later (see the class comment above); a caller that never needs to (a framework-owned host
        // root, destroyed wholesale) is free to discard it.
        internal static System.Action AttachBridge(VisualElement bridgeAnchor, ReconcilerContext ctx)
        {
            EventCallback<PointerDownEvent> onPointerDown = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<PointerUpEvent> onPointerUp = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<PointerMoveEvent> onPointerMove = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<PointerEnterEvent> onPointerEnter = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<PointerLeaveEvent> onPointerLeave = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<WheelEvent> onWheel = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<KeyDownEvent> onKeyDown = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<KeyUpEvent> onKeyUp = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<FocusInEvent> onFocusIn = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<FocusOutEvent> onFocusOut = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<ClickEvent> onClick = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<ChangeEvent<float>> onFloatChange = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<ChangeEvent<bool>> onBoolChange = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<ChangeEvent<string>> onStringChange = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            EventCallback<ChangeEvent<int>> onIntChange = evt => Continue(evt, evt.target as VisualElement, ctx, bridgeAnchor);
            // FocusEvent/BlurEvent are deliberately NOT registered here, even though
            // FiberEventBindingManager.TryInvokeSynthetic has a case for both (kept there for symmetry
            // with the other binding kinds, and reachable if some other caller ever synthesizes one).
            // Per Unity's own UIElements API docs, FocusEvent/BlurEvent "trickle down and do not bubble
            // up" — target-only, unlike FocusInEvent/FocusOutEvent which explicitly "trickle down and
            // bubble up". A BubbleUp listener registered HERE, on bridgeAnchor (an ancestor of the
            // actual focused/blurred element in every real case — a host panel root or a registry
            // target container is essentially never itself the focus target), would structurally never
            // receive one raised on a descendant: the event simply never reaches bridgeAnchor. This
            // mirrors FiberFocusNavigator.AttachToRoot's own choice of FocusIn/Out over Focus/Blur for
            // its own panel-root-level tracking.
            // GeometryChangedEvent is excluded for the same target-only reason, even though
            // TryInvokeSynthetic has a case for it too: per Unity's own docs it does not bubble or
            // trickle down, only ever dispatching to the element whose own geometry just changed, so a
            // BubbleUp listener on bridgeAnchor could likewise never receive one raised on a descendant.
            bridgeAnchor.RegisterCallback(onPointerDown);
            bridgeAnchor.RegisterCallback(onPointerUp);
            bridgeAnchor.RegisterCallback(onPointerMove);
            bridgeAnchor.RegisterCallback(onPointerEnter);
            bridgeAnchor.RegisterCallback(onPointerLeave);
            bridgeAnchor.RegisterCallback(onWheel);
            bridgeAnchor.RegisterCallback(onKeyDown);
            bridgeAnchor.RegisterCallback(onKeyUp);
            bridgeAnchor.RegisterCallback(onFocusIn);
            bridgeAnchor.RegisterCallback(onFocusOut);
            bridgeAnchor.RegisterCallback(onClick);
            bridgeAnchor.RegisterCallback(onFloatChange);
            bridgeAnchor.RegisterCallback(onBoolChange);
            bridgeAnchor.RegisterCallback(onStringChange);
            bridgeAnchor.RegisterCallback(onIntChange);

            return () =>
            {
                bridgeAnchor.UnregisterCallback(onPointerDown);
                bridgeAnchor.UnregisterCallback(onPointerUp);
                bridgeAnchor.UnregisterCallback(onPointerMove);
                bridgeAnchor.UnregisterCallback(onPointerEnter);
                bridgeAnchor.UnregisterCallback(onPointerLeave);
                bridgeAnchor.UnregisterCallback(onWheel);
                bridgeAnchor.UnregisterCallback(onKeyDown);
                bridgeAnchor.UnregisterCallback(onKeyUp);
                bridgeAnchor.UnregisterCallback(onFocusIn);
                bridgeAnchor.UnregisterCallback(onFocusOut);
                bridgeAnchor.UnregisterCallback(onClick);
                bridgeAnchor.UnregisterCallback(onFloatChange);
                bridgeAnchor.UnregisterCallback(onBoolChange);
                bridgeAnchor.UnregisterCallback(onStringChange);
                bridgeAnchor.UnregisterCallback(onIntChange);
            };
        }

        private static void Continue(EventBase evt, VisualElement? target, ReconcilerContext ctx, VisualElement bridgeAnchor)
        {
            if (target == null) return;
            // One dispatch reaches every bridge on its target's physical path, innermost first, and the walk
            // below covers every boundary, so an outer bridge leaves the event to the inner one.
            if (HasBridgeBelow(target, bridgeAnchor, ctx)) return;

            for (var current = LogicalParent(target, ctx); current != null; current = LogicalParent(current, ctx))
            {
                // A synthetic handler may stop propagation, which ends this walk as it ends native bubbling.
                if (evt.isPropagationStopped) break;
                // Native dispatch reaches the target's physical ancestors itself.
                if (IsPhysicalAncestorOrSelf(current, target)) continue;
                ctx.EventManager.TryInvokeSynthetic(current, evt);
            }
        }

        // Whether an element from target up to, but not including, bridgeAnchor carries a bridge of its own. A
        // host panel's root, the other kind of anchor, is the root of its panel and so never below another.
        private static bool HasBridgeBelow(VisualElement target, VisualElement bridgeAnchor, ReconcilerContext ctx)
        {
            for (var element = target; !ReferenceEquals(element, bridgeAnchor); element = element.hierarchy.parent!)
            {
                if (ctx.SamePanelPortalBridges.ContainsKey(element)) return true;
            }
            return false;
        }

        private static bool IsPhysicalAncestorOrSelf(VisualElement candidate, VisualElement target)
        {
            for (var element = target; element != null; element = element.hierarchy.parent)
            {
                if (ReferenceEquals(element, candidate)) return true;
            }
            return false;
        }

        // A portal's child answers to the placeholder standing at the portal's call site, whatever element it
        // is mounted under, and a z-managed element to the placeholder at its declared slot; any other element
        // to its parent.
        private static VisualElement? LogicalParent(VisualElement element, ReconcilerContext ctx)
        {
            var slot = ctx.ZLayerMembers.TryGetValue(element, out var member) ? member.Placeholder : element;
            var parent = slot.parent;
            if (parent == null) return null;
            var placeholder = PortalHoldingRow(slot, parent, ctx);
            return placeholder != null ? LogicalParent(placeholder, ctx) : parent;
        }

        // The Portal whose range on parent holds row, or null for a row of parent's own. Where ranges nest, the
        // one starting last is the innermost.
        private static VisualElement? PortalHoldingRow(VisualElement row, VisualElement parent, ReconcilerContext ctx)
        {
            var index = LogicalChildSlots.ToLogical(parent, parent.IndexOf(row));
            VisualElement? holder = null;
            var holderStart = -1;
            foreach (var entry in ctx.PortalState)
            {
                var range = entry.Value;
                if (!ReferenceEquals(range.Target, parent)) continue;
                if (index < range.SlotStart || index >= range.SlotStart + range.SlotLength) continue;
                // MUTANT_SURVIVES(unreachable, boundary): ranges holding one row are a portal nested in another on
                // one target, and the inner opens at the target's row count, behind the outer's row that holds its
                // placeholder, so they never start at one slot.
                if (range.SlotStart <= holderStart) continue;
                holder = entry.Key;
                holderStart = range.SlotStart;
            }
            return holder;
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
        // Internal (not private): the drag-overlay positioner rides the same conversion for its
        // source-panel → screen → overlay-panel hop.
        internal static Vector2 PanelToScreen(IPanel panel, Vector2 panelPosition)
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
