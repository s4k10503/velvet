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
    //   - ChildReconciler's registry-portal drain branch, ONCE per resolved V.Portal(targetId:) target
    //     element (see ReconcilerContext.SamePanelPortalBridges). A same-panel target DOES have a
    //     physical parent chain that keeps bubbling on its own, but that chain reflects the target's
    //     OWN position, not the Portal's LOGICAL one, so this bridge still needs to run to reach the
    //     latter. The unbind Action is retained and invoked at Reconciler.Dispose: a registry target
    //     is an ordinary, already-live user element that normally outlives this reconciler, unlike a
    //     framework-owned host root.
    //
    // Mirrors React's own root-level event delegation (a single listener per event type at the DOM
    // root, walking Fiber.return instead of the DOM to build the dispatch order — see
    // DOMPluginEventSystem.js's accumulateSinglePhaseListeners) with one deliberate adaptation: Velvet
    // does NOT move every event to root-level delegation. Ordinary same-panel bubbling stays exactly as
    // UI Toolkit's own native dispatch already does it (FiberEventBindingManager.Bind's direct
    // RegisterCallback<T> registrations on each element), since that already agrees with the logical
    // tree everywhere except at a portal/world-space boundary. This dispatcher only takes over at the
    // seams where physical bubbling structurally cannot reach the logical chain: a host panel's root
    // (nothing physical above it at all), and a same-panel registry target (something physical above
    // it, but not the right thing — see Continue's truncation check for how a same-panel target avoids
    // double-invoking whatever the two chains share).
    //
    // Known limitation: resolving "which ComponentFiber logically owns this event" depends on
    // FiberNodeFactory stamping element.userData with _ctx.FiberStack.Current at CreateElement time —
    // which is only meaningful while a component's Body is actually rendering. A Portal/WorldSpace
    // child that is a bare host element (e.g. V.Portal(children: [V.Div(...)])) with no enclosing
    // V.Component gets userData stamped from whatever fiber happens to be current during the deferred
    // drain (see ChildReconciler.DrainPendingPortalMounts), which is not reliably the logical
    // enclosing component. Wrap portal/world-space children in a component to get correct synthetic
    // bubbling; a bare element's own events: handlers still fire normally (native bubbling is
    // unaffected everywhere), only its FURTHER bubbling past the portal boundary is affected.
    internal static class FiberCrossPanelEventDispatcher
    {
        // Registers one BubbleUp listener per synthetic-bubbling-eligible event type on bridgeAnchor —
        // either a newly created host panel's root (called once, from PanelHostFactory) or a resolved
        // V.Portal(targetId:) target element (called once per target, from ChildReconciler's registry-
        // portal drain branch — see ReconcilerContext.SamePanelPortalBridges for the attach-once guard).
        // Each listener fires only after UI Toolkit's own native dispatch has already bubbled the event
        // through every element AT OR BELOW bridgeAnchor (BubbleUp is the last phase to run on a given
        // element), so nothing here duplicates a handler UI Toolkit's own dispatcher already invoked at
        // or below that point. Matches the event set FiberEventBindingManager.TryInvokeSynthetic
        // supports — see its own comment for why ClickedBinding/ChangeEventBinding<T> are excluded.
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
            };
        }

        private static void Continue(EventBase evt, VisualElement? target, ReconcilerContext ctx, VisualElement bridgeAnchor)
        {
            if (target == null) return;

            // Walk from the native target up to (and including) bridgeAnchor, looking for the element
            // whose owning fiber carries a DetachedMountContext — stamped only on the top-level
            // child(ren) a Portal/WorldSpace drain produced (DrainPendingPortalMounts). This is a
            // metadata scan only (no handler invocation along the way), so it cannot double-fire
            // anything even though it retraces ground the native dispatch already covered.
            ComponentFiber? logicalParent = null;
            for (var current = target; current != null; current = current.parent)
            {
                if (current.userData is ComponentFiber { DetachedMountContext: { } dmc })
                {
                    logicalParent = ResolveOutermostLogicalAncestor(dmc.LogicalParent);
                    break;
                }
            }

            if (logicalParent?.MountPoint == null) return;

            // From here on, walk outward from the logical ancestor's OWN physical location, invoking
            // each element's own synthetic handler directly.
            for (var current = logicalParent.MountPoint; current != null; current = NextLogicalAncestor(current))
            {
                // A synthetic handler invoked on an earlier hop may itself call StopPropagation() —
                // honor it exactly like the native BubbleUp phase this walk continues would (this read
                // was previously missing, so a synthetic StopPropagation() had no effect on the rest of
                // the walk — a real bug, since every OTHER stage of dispatch in this codebase respects
                // the flag).
                if (evt.isPropagationStopped) break;
                // Same-panel targets sit in the SAME physical tree as their own remaining ancestors:
                // the native dispatch that is STILL bubbling this exact evt (this callback fired
                // mid-bubble, at bridgeAnchor, not after the whole dispatch finished) will visit
                // bridgeAnchor's own ancestors on its own once this callback returns, provided
                // propagation was not already stopped (checked above). Stopping the synthetic walk the
                // moment it reaches that same ground avoids invoking a shared handler twice — the two
                // chains always eventually reconverge, at minimum at the panel root itself. For a
                // cross-panel host (bridgeAnchor = a separate panel's root), this can never match
                // anything in the synthetic chain (a completely different Panel), so the check is a
                // harmless no-op there and the full walk always runs exactly as it did before same-panel
                // support existed.
                if (IsCoveredByNativeBubbling(current, bridgeAnchor)) break;
                ctx.EventManager.TryInvokeSynthetic(current, evt);
            }
        }

        // True when candidate is bridgeAnchor itself, or one of ITS OWN physical ancestors — elements
        // the SAME native bubble dispatch that produced evt is guaranteed to visit on its own once the
        // BubbleUp callback registered on bridgeAnchor returns (nothing between here and there calls
        // StopPropagation). Walked inline on every call rather than precomputed once into a
        // HashSet<VisualElement>: these chains are short (a handful of levels), and this runs on every
        // hop of a walk that itself runs on every PointerMove/Enter/Leave — trading a few extra
        // reference comparisons for zero per-event allocation.
        private static bool IsCoveredByNativeBubbling(VisualElement candidate, VisualElement bridgeAnchor)
        {
            for (var ancestor = bridgeAnchor; ancestor != null; ancestor = ancestor.parent)
            {
                if (ReferenceEquals(ancestor, candidate)) return true;
            }
            return false;
        }

        // Chases DetachedMountContext.LogicalParent while fiber is ITSELF a detached-mount top-level
        // child — a Portal/WorldSpace nested inside another Portal's/WorldSpace's content — instead of
        // stopping at the immediate LogicalParent. A nested detached mount's own MountPoint is the
        // INNER boundary's target/host root: a physical location, not that fiber's true logical
        // position (a registry target is not a logical-tree ancestor of anything logically inside it; a
        // host panel root's own physical parent is UI-Toolkit-internal and belongs to a different Panel
        // than the outer logical chain). Escaping every nested boundary before reading MountPoint
        // mirrors FiberContextSpine.Push, which treats a nested DetachedMountContext as its own spine
        // edge rather than trusting an intermediate MountPoint — without this, a doubly-nested portal's
        // outward walk would invoke a meaningless element once and then dead-end on ITS unrelated
        // ancestors, never reaching the outer Portal's true logical chain.
        private static ComponentFiber? ResolveOutermostLogicalAncestor(ComponentFiber? fiber)
        {
            while (fiber?.DetachedMountContext is { LogicalParent: { } outer })
            {
                fiber = outer;
            }
            return fiber;
        }

        // Advances one step further up the synthetic chain: the ordinary physical parent, UNLESS
        // current is itself another portal/world-space boundary's top-level child (a nested portal),
        // in which case the walk must hop through ITS OWN (fully resolved) logical ancestor the same
        // way — a plain VisualElement.parent walk would silently dead-end there, same as the outer walk
        // in Continue above.
        private static VisualElement? NextLogicalAncestor(VisualElement current)
        {
            if (current.userData is ComponentFiber { DetachedMountContext: { } dmc })
            {
                return ResolveOutermostLogicalAncestor(dmc.LogicalParent)?.MountPoint;
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
