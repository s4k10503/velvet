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
    //   FiberCrossPanelEventDispatcher: an event that native dispatch carries through a portal/world-space
    //   boundary also reaches the logical ancestor chain outside it — the "exit" side. This is the ONLY
    //   mechanism V.Portal(targetId:) needs — there is no "entry" side for it, since the event started in
    //   the same panel to begin with.
    //
    //   FiberCrossPanelPointerRouter: an event arriving at the MAIN panel is redirected INTO a
    //   higher-priority host panel FIRST, before the main panel's own dispatch processes it — the
    //   "entry" side. Screen-space layer hosts only (see its own comment); irrelevant to
    //   V.Portal(targetId:), which never creates a separate panel to route into.
    //
    // Both share ReconcilerContext.LayerHosts / WorldSpaceBindings (which panels exist) and
    // FiberEventBindingManager.TryInvokeSynthetic (how a handler is invoked once the right element is
    // found) — see those for the rest of the mechanism this file builds on.

    // Bridges a native UI Toolkit event crossing a Portal/WorldSpace boundary to the logical ancestor
    // chain OUTSIDE that boundary. AttachBridge is called from two different kinds of place,
    // distinguished by whether the returned unbind Action matters:
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
    // return path — a portal's child answers to the position its placeholder holds (see LogicalParent) —
    // in capture as the event trickles through the anchor, and in bubble as it comes back up.
    internal static class FiberCrossPanelEventDispatcher
    {
        // Each event the bridge carries, registered on bridgeAnchor TrickleDown and, where it bubbles,
        // BubbleUp. Matches the event set FiberEventBindingManager.TryInvokeSynthetic supports.
        // FocusEvent/BlurEvent trickle down and do not bubble up, per Unity's own UIElements API docs;
        // GeometryChangedEvent neither trickles nor bubbles, reaching only the element whose geometry
        // changed, and is not carried.
        private static readonly System.Action<VisualElement, Bridge, Registrations>[] Listeners =
        {
            TrickleAndBubble<PointerDownEvent>,
            TrickleAndBubble<PointerUpEvent>,
            TrickleAndBubble<PointerMoveEvent>,
            TrickleAndBubble<PointerEnterEvent>,
            TrickleAndBubble<PointerLeaveEvent>,
            TrickleAndBubble<WheelEvent>,
            TrickleAndBubble<KeyDownEvent>,
            TrickleAndBubble<KeyUpEvent>,
            TrickleAndBubble<FocusInEvent>,
            TrickleAndBubble<FocusOutEvent>,
            TrickleAndBubble<ClickEvent>,
            TrickleAndBubble<ChangeEvent<float>>,
            TrickleAndBubble<ChangeEvent<bool>>,
            TrickleAndBubble<ChangeEvent<string>>,
            TrickleAndBubble<ChangeEvent<int>>,
            Trickle<FocusEvent>,
            Trickle<BlurEvent>,
        };

        private sealed class Registrations
        {
            public readonly List<System.Action> Undo = new();
            public readonly List<System.Action> Retrickle = new();
        }

        private static void Trickle<T>(VisualElement anchor, Bridge bridge, Registrations registrations)
            where T : EventBase<T>, new()
        {
            EventCallback<T> capture = bridge.Capture;
            anchor.RegisterCallback(capture, TrickleDown.TrickleDown);
            registrations.Undo.Add(() => anchor.UnregisterCallback(capture, TrickleDown.TrickleDown));
            registrations.Retrickle.Add(() =>
            {
                anchor.UnregisterCallback(capture, TrickleDown.TrickleDown);
                anchor.RegisterCallback(capture, TrickleDown.TrickleDown);
            });
        }

        private static void TrickleAndBubble<T>(VisualElement anchor, Bridge bridge, Registrations registrations)
            where T : EventBase<T>, new()
        {
            Trickle<T>(anchor, bridge, registrations);
            EventCallback<T> bubble = bridge.Bubble;
            anchor.RegisterCallback(bubble);
            registrations.Undo.Add(() => anchor.UnregisterCallback(bubble));
        }

        // Registers the listeners above on bridgeAnchor — either a newly created host panel's root (called
        // once, from PanelHostFactory) or a resolved same-panel target (once per target, from
        // ReconcilerContext.BindPortalTarget, which owns the attach-once guard and which element it listens
        // on). Returns the delegate that undoes every registration, for a caller that needs to detach it
        // later (see the class comment above); a caller that never needs to (a framework-owned host root,
        // destroyed wholesale) is free to discard it.
        internal static System.Action AttachBridge(VisualElement bridgeAnchor, ReconcilerContext ctx)
        {
            var bridge = new Bridge(bridgeAnchor, ctx);
            var registrations = new Registrations();
            foreach (var listen in Listeners) listen(bridgeAnchor, bridge, registrations);
            // The capture segment runs after the anchor's own capture bindings because the listener is
            // registered after them; a rebind of those bindings registers them again, and the listeners are
            // moved back behind them then.
            ctx.EventManager.SetBridge(bridgeAnchor, bridge.RunBubble, () =>
            {
                foreach (var retrickle in registrations.Retrickle) retrickle();
            });

            return () =>
            {
                ctx.EventManager.ClearBridge(bridgeAnchor);
                foreach (var unregister in registrations.Undo) unregister();
            };
        }

        // One anchor's share of a dispatch. Each anchor on the target's physical path carries the logical
        // ancestors that sit between the portal content below it and the point where the logical chain
        // rejoins the physical path, so every anchor on the path adds its own segment at the place native
        // dispatch reaches it, and the segments of nested portals interleave with the physical elements the
        // way the logical chain does.
        private sealed class Bridge
        {
            private readonly VisualElement _anchor;
            private readonly ReconcilerContext _ctx;

            // The dispatch whose bubble segment already ran, and whether a handler in it stopped
            // propagation. The anchor's own bubble bindings run that segment first through the prelude the
            // manager calls, and the anchor's BubbleUp listener after them must not run it again; Capture
            // clears it as each dispatch trickles through.
            private EventBase? _bubbled;
            private bool _stopped;

            public Bridge(VisualElement anchor, ReconcilerContext ctx)
            {
                _anchor = anchor;
                _ctx = ctx;
            }

            // Outermost first, after the anchor's own capture bindings and before anything below the anchor.
            public void Capture(EventBase evt)
            {
                _bubbled = null;
                var target = evt.target as VisualElement;
                var start = SegmentStart(target);
                if (start != null) CaptureChain(start, target, evt, _ctx);
            }

            public void Bubble(EventBase evt) => RunBubble(evt);

            // Innermost first, after everything below the anchor and before the anchor's own bubble bindings.
            public bool RunBubble(EventBase evt)
            {
                if (ReferenceEquals(_bubbled, evt)) return _stopped;
                _bubbled = evt;
                var target = evt.target as VisualElement;
                _stopped = BubbleChain(SegmentStart(target), target, evt, _ctx);
                return _stopped;
            }

            // The innermost logical ancestor this anchor carries: the logical parent, off the target's physical
            // path, of the element nearest the anchor whose logical parent leaves that path. What lies below
            // another anchor on the path is that anchor's to carry.
            private VisualElement? SegmentStart(VisualElement? target)
            {
                VisualElement? start = null;
                for (var element = target; !ReferenceEquals(element, _anchor); element = element.hierarchy.parent)
                {
                    // MUTANT_SURVIVES(unreachable, guard removed): a listener on the anchor, and the prelude its
                    // own bindings call, run only for a target at or below the anchor.
                    if (element == null) return null;
                    if (IsOtherAnchor(element)) start = null;
                    var logical = LogicalParent(element, _ctx);
                    if (OffPath(logical, target)) start = logical;
                }
                return start;
            }

            private bool IsOtherAnchor(VisualElement element)
            {
                foreach (var bridge in _ctx.SamePanelPortalBridges.Values)
                {
                    if (ReferenceEquals(bridge.Anchor, element)) return !ReferenceEquals(element, _anchor);
                }
                return false;
            }
        }

        // A pointer event the layer router took from the main panel reaches no native dispatch in the host
        // panel, so every logical ancestor of the element it picked is run here: capture outermost first, the
        // element itself, then bubble innermost first.
        internal static void DispatchRerouted(VisualElement hit, EventBase evt, ReconcilerContext ctx)
        {
            var parent = LogicalParent(hit, ctx);
            if (parent != null) CaptureChain(parent, null, evt, ctx);
            if (evt.isPropagationStopped) return;
            ctx.EventManager.TryInvokeSynthetic(hit, evt);
            if (evt.isPropagationStopped) return;
            BubbleChain(parent, null, evt, ctx);
        }

        // Whether a logical ancestor is one native dispatch does not reach: there is one, and it is not on
        // the physical path of target, where a null target is a dispatch with no physical path at all.
        private static bool OffPath(VisualElement? logical, VisualElement? target) =>
            logical != null && !IsPhysicalAncestorOrSelf(logical, target);

        // The chain from start up to where it rejoins target's physical path, outermost first. It is counted
        // and then walked from start for each depth, which keeps a dispatch through an anchor from
        // allocating.
        private static void CaptureChain(VisualElement start, VisualElement? target, EventBase evt, ReconcilerContext ctx)
        {
            var length = 0;
            for (var current = start; OffPath(current, target); current = LogicalParent(current!, ctx)) length++;
            for (var depth = length - 1; depth >= 0; depth--)
            {
                if (evt.isPropagationStopped) return;
                var ancestor = start;
                for (var step = 0; step < depth; step++) ancestor = LogicalParent(ancestor, ctx)!;
                ctx.EventManager.TryInvokeSynthetic(ancestor, evt, capture: true);
            }
        }

        // The same chain innermost first; answers whether one of its handlers stopped propagation.
        private static bool BubbleChain(VisualElement? start, VisualElement? target, EventBase evt, ReconcilerContext ctx)
        {
            for (var current = start; OffPath(current, target); current = LogicalParent(current!, ctx))
            {
                ctx.EventManager.TryInvokeSynthetic(current!, evt, capture: false);
                if (evt.isPropagationStopped) return true;
            }
            return false;
        }

        private static bool IsPhysicalAncestorOrSelf(VisualElement candidate, VisualElement? target)
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
            var placeholder = ctx.PortalHoldingRow(slot, parent);
            return placeholder != null ? LogicalParent(placeholder, ctx) : parent;
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
                FiberCrossPanelEventDispatcher.DispatchRerouted(hit, evt, ctx);
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
