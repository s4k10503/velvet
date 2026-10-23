#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Reconciler-side bookkeeping for one DndContext element, keyed in ReconcilerContext.DndScopeBindings
    // by the scope element. Holds only the latest settings — the scope carries no listeners of its own;
    // draggables resolve their innermost scope at pointer-down time by walking the parent chain (the
    // FiberFocusNavigator pattern), so a scope that re-renders with fresh delegates is picked up with no
    // re-registration.
    internal sealed class DndScopeBinding
    {
        public DndContextSettings Settings;
        // Where the session publishes: the one V.DndContext provides, else the binding's own.
        public DndContextStore Store;

        public DndScopeBinding(DndContextSettings settings)
        {
            Settings = settings;
            Store = settings.Store ?? new DndContextStore();
        }
    }

    // Bookkeeping for one draggable element: the latest settings, the parsed while-dragging classes, the
    // registered pointer-down and key-down sensor offers (binding-lifetime, NOT FiberEventBindings — they must
    // survive event-prop diffs untouched), and the one-shot no-scope warning flag.
    internal sealed class DndDraggableBinding
    {
        public DraggableSettings Settings;
        public string[] DraggingClasses;
        public EventCallback<PointerDownEvent>? OnPointerDown;
        public EventCallback<KeyDownEvent>? OnKeyDown;
        public bool WarnedNoScope;

        public DndDraggableBinding(DraggableSettings settings)
        {
            Settings = settings;
            DraggingClasses = V.ParseClassNames(settings.WhileDraggingClass);
        }
    }

    // Bookkeeping for one droppable element: the latest settings plus the parsed over/active class arrays.
    internal sealed class DndDroppableBinding
    {
        public DroppableSettings Settings;
        public string[] OverClasses;
        public string[] ActiveClasses;
        // Position in attach order, kept across settings updates: the order a collision strategy sees the
        // candidates in, where a droppable that unmounts and mounts again goes last.
        public readonly long Order;

        public DndDroppableBinding(DroppableSettings settings, long order)
        {
            Settings = settings;
            Order = order;
            OverClasses = V.ParseClassNames(settings.WhileOverClass);
            ActiveClasses = V.ParseClassNames(settings.WhileDragActiveClass);
        }

        public void Reparse()
        {
            OverClasses = V.ParseClassNames(Settings.WhileOverClass);
            ActiveClasses = V.ParseClassNames(Settings.WhileDragActiveClass);
        }
    }

    // Bookkeeping for one V.DragOverlay positioner: its latest settings, the one-shot unsupported-panel warning
    // and the drop animation it runs after a drag — the positioner element itself is the registry key, and all
    // session state lives on DndActiveDrag.
    internal sealed class DndOverlayBinding
    {
        public DragOverlaySettings? Settings;
        public bool WarnedUnsupportedPanel;
        public DndDropAnimator.Run? Drop;
    }

    // Attach/Update/Detach for the DndContext scope slot, hand-inlined in FiberWrapperElementAppliers like
    // ApplyFocusScope (the drivers need the ReconcilerContext).
    internal static class DndScopeDriver
    {
        public static DndScopeBinding Attach(VisualElement element, DndContextSettings settings)
            => new(settings);

        // In-place refresh: fresh-but-equal records never re-attach (record equality gates the diff), and
        // delegate-bearing records are refreshed on every inequality — cheap, and pointer-down resolves
        // the binding at event time so nothing else needs rewiring.
        public static void Update(DndScopeBinding binding, DndContextSettings settings)
        {
            binding.Settings = settings;
            if (settings.Store != null)
            {
                binding.Store = settings.Store;
            }
        }

        public static void Detach(VisualElement element, ReconcilerContext ctx)
        {
            // A scope disappearing mid-drag takes its session with it (its delegates and collision
            // strategy are gone); teardown-flavored so the user cancel callback lands after the flush.
            if (ctx.ActiveDrag != null && ReferenceEquals(ctx.ActiveDrag.ScopeElement, element))
            {
                ctx.ActiveDrag.CancelForTeardown();
            }
        }
    }

    internal static class DndDraggableDriver
    {
        public static DndDraggableBinding Attach(VisualElement element, DraggableSettings settings, ReconcilerContext ctx)
        {
            var binding = new DndDraggableBinding(settings);
            // Binding-lifetime armer, TrickleDown: a Clickable on the same element (a draggable
            // V.Button) stops the down's immediate propagation from its own bubble-phase handler, which
            // would silence a later-registered bubble armer — the trickle phase runs deterministically
            // first. Arming itself neither captures nor stops propagation: a press that never crosses
            // the activation constraint must stay a plain click (see DndActiveDrag.Arm).
            binding.OnPointerDown = evt => DndActiveDrag.Offer(element, binding, ctx, evt);
            binding.OnKeyDown = evt => DndActiveDrag.Offer(element, binding, ctx, evt);
            element.RegisterCallback(binding.OnPointerDown, TrickleDown.TrickleDown);
            element.RegisterCallback(binding.OnKeyDown, TrickleDown.TrickleDown);
            return binding;
        }

        public static void Update(VisualElement element, DndDraggableBinding binding, DraggableSettings settings, ReconcilerContext ctx)
        {
            var oldClass = binding.Settings.WhileDraggingClass;
            binding.Settings = settings;
            if (!string.Equals(oldClass, settings.WhileDraggingClass, System.StringComparison.Ordinal))
            {
                binding.DraggingClasses = V.ParseClassNames(settings.WhileDraggingClass);
            }
            // Disabling the active source mid-drag cancels its session; the Update runs mid-flush, so the
            // teardown-flavored path (deferred user callback) is the safe one.
            if (settings.Disabled && ctx.ActiveDrag != null && ReferenceEquals(ctx.ActiveDrag.Source, element))
            {
                ctx.ActiveDrag.CancelForTeardown();
            }
        }

        public static void Detach(VisualElement element, DndDraggableBinding binding, ReconcilerContext ctx)
        {
            if (ctx.ActiveDrag != null && ReferenceEquals(ctx.ActiveDrag.Source, element))
            {
                ctx.ActiveDrag.CancelForTeardown();
            }
            if (binding.OnPointerDown != null)
            {
                element.UnregisterCallback(binding.OnPointerDown, TrickleDown.TrickleDown);
                binding.OnPointerDown = null;
            }
            if (binding.OnKeyDown != null)
            {
                element.UnregisterCallback(binding.OnKeyDown, TrickleDown.TrickleDown);
                binding.OnKeyDown = null;
            }
            ctx.ActiveDrag?.OnDraggableDetached(element);
            DndDropAnimator.ForgetSource(element, ctx);
        }
    }

    internal static class DndDroppableDriver
    {
        public static DndDroppableBinding Attach(VisualElement element, DroppableSettings settings, ReconcilerContext ctx)
            => new(settings, ctx.NextDroppableOrder++);

        public static void Update(VisualElement element, DndDroppableBinding binding, DroppableSettings settings, ReconcilerContext ctx)
        {
            binding.Settings = settings;
            binding.Reparse();
            if (settings.Disabled)
            {
                ctx.ActiveDrag?.OnDroppableInvalidated(element);
            }
        }

        public static void Detach(VisualElement element, ReconcilerContext ctx)
        {
            ctx.ActiveDrag?.OnDroppableInvalidated(element);
        }
    }

    // The V.DragOverlay positioner: a framework-positioned, picking-ignored container that tracks the drag while
    // one is active and is display:none otherwise. Session begin/end and per-move positioning are driven by
    // DndActiveDrag; this driver owns only the element's binding lifecycle and the panel-space conversion.
    internal static class DndOverlayDriver
    {
        public static DndOverlayBinding Attach(VisualElement positioner, DragOverlaySettings settings, ReconcilerContext ctx)
        {
            // Forced inline for the same reason AnchoredDriver forces absolute: dynamic left/top has no
            // other way to work. PickingMode.Ignore keeps the ghost from intercepting the drop or waking
            // the cross-panel pointer router.
            positioner.pickingMode = PickingMode.Ignore;
            positioner.style.position = Position.Absolute;
            positioner.style.display = DisplayStyle.None;
            return new DndOverlayBinding { Settings = settings };
        }

        public static void Detach(VisualElement positioner, ReconcilerContext ctx)
        {
            ctx.ActiveDrag?.OnOverlayInvalidated(positioner);
            if (ctx.DragOverlayBindings.TryGetValue(positioner, out var binding))
            {
                DndDropAnimator.Stop(binding);
            }
            positioner.pickingMode = PickingMode.Position;
            positioner.style.position = StyleKeyword.Null;
            positioner.style.display = StyleKeyword.Null;
            positioner.style.left = StyleKeyword.Null;
            positioner.style.top = StyleKeyword.Null;
            positioner.style.width = StyleKeyword.Null;
            positioner.style.height = StyleKeyword.Null;
        }

        public static void BeginSession(VisualElement positioner, DndOverlayBinding binding, Vector2 sourceSize)
        {
            DndDropAnimator.Stop(binding);
            positioner.style.display = DisplayStyle.Flex;
            positioner.style.width = sourceSize.x;
            positioner.style.height = sourceSize.y;
        }

        public static void EndSession(VisualElement positioner, DndOverlayBinding binding)
        {
            positioner.style.display = DisplayStyle.None;
        }

        // Pins the positioner's top-left at topLeft, a point in the source's panel. An overlay that can show
        // nothing there degrades to hidden with a one-shot warning.
        public static void SyncPosition(
            VisualElement positioner, DndOverlayBinding binding, IPanel? sourcePanel, Vector2 topLeft,
            ReconcilerContext ctx)
        {
            if (positioner.panel == null || sourcePanel == null)
            {
                return;
            }
            if (!TryLocalPosition(positioner, sourcePanel, topLeft, ctx, out var local))
            {
                if (!binding.WarnedUnsupportedPanel)
                {
                    binding.WarnedUnsupportedPanel = true;
                    FiberLogger.LogWarning("Dnd",
                        "A drag overlay on another panel than its drag source needs both to be runtime "
                        + "(Player-context) panels, and a Camera.main when either is a world-space panel; "
                        + "hiding the overlay.");
                }
                positioner.style.display = DisplayStyle.None;
                return;
            }
            positioner.style.left = local.x;
            positioner.style.top = local.y;
        }

        // A point in the source's panel, in the positioner's parent's space: through screen space when the two
        // panels differ (DndPanelSpace).
        public static bool TryLocalPosition(
            VisualElement positioner, IPanel sourcePanel, Vector2 point, ReconcilerContext ctx, out Vector2 local)
        {
            if (!DndPanelSpace.TryPoint(sourcePanel, positioner.panel, point, ctx, out var onOverlayPanel))
            {
                local = default;
                return false;
            }
            var parent = positioner.hierarchy.parent;
            local = parent != null ? parent.WorldToLocal(onOverlayPanel) : onOverlayPanel;
            return true;
        }
    }

    // Settles the press-derived styling state after the session swallowed the real PointerUp
    // (StopImmediatePropagation runs before the bubble-phase signal callbacks, and captured delivery
    // hides it from every other element entirely): every manipulator holding an ElementLocalVariantSignals
    // on an element of the press chain — the elements the press's own PointerDown reached, so the ones
    // whose whileTap / active: (and stacked forms) it may have lit — is told to observe a synthetic
    // release. The chain is the session's press-time list rather than a walk from the press target: a
    // descendant unmounted mid-drag no longer has the source above it.
    internal static class DndPressVariantSettler
    {
        public static void Settle(System.Collections.Generic.List<VisualElement> pressChain, ReconcilerContext ctx)
        {
            foreach (var element in pressChain)
            {
                SettleOn(element, ctx);
            }
        }

        private static void SettleOn(VisualElement element, ReconcilerContext ctx)
            => VariantSettleSweep.ForEach(element, ctx, static settler => settler.SettleRelease());
    }
}
