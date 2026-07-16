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

        public DndScopeBinding(DndContextSettings settings)
        {
            Settings = settings;
        }
    }

    // Bookkeeping for one draggable element: the latest settings, the parsed while-dragging classes, the
    // registered pointer-down armer (binding-lifetime, NOT a FiberEventBinding — it must survive event-prop
    // diffs untouched), and the one-shot no-scope warning flag.
    internal sealed class DndDraggableBinding
    {
        public DraggableSettings Settings;
        public string[] DraggingClasses;
        public EventCallback<PointerDownEvent>? OnPointerDown;
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

        public DndDroppableBinding(DroppableSettings settings)
        {
            Settings = settings;
            OverClasses = V.ParseClassNames(settings.WhileOverClass);
            ActiveClasses = V.ParseClassNames(settings.WhileDragActiveClass);
        }

        public void Reparse()
        {
            OverClasses = V.ParseClassNames(Settings.WhileOverClass);
            ActiveClasses = V.ParseClassNames(Settings.WhileDragActiveClass);
        }
    }

    // Bookkeeping for one V.DragOverlay positioner: only the one-shot unsupported-panel warning — the
    // positioner element itself is the registry key, and all session state lives on DndActiveDrag.
    internal sealed class DndOverlayBinding
    {
        public bool WarnedUnsupportedPanel;
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
            binding.OnPointerDown = evt => DndActiveDrag.Arm(element, binding, ctx, evt);
            element.RegisterCallback(binding.OnPointerDown, TrickleDown.TrickleDown);
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
        }
    }

    internal static class DndDroppableDriver
    {
        public static DndDroppableBinding Attach(VisualElement element, DroppableSettings settings)
            => new(settings);

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

    // The V.DragOverlay positioner: a framework-positioned, picking-ignored container on the Overlay
    // layer panel that tracks the pointer while a drag is active and is display:none otherwise. Session
    // begin/end and per-move positioning are driven by DndActiveDrag; this driver owns only the
    // element's binding lifecycle and the panel-space conversion.
    internal static class DndOverlayDriver
    {
        public static DndOverlayBinding Attach(VisualElement positioner, ReconcilerContext ctx)
        {
            // Dictionary enumeration order is unspecified once entries have churned, so which of several
            // overlays a session picks is not deterministic — surface that at mount instead of letting
            // the ghost jump between containers across drags.
            if (ctx.DragOverlayBindings.Count > 0)
            {
                FiberLogger.LogWarning("Dnd",
                    "More than one V.DragOverlay is mounted in this tree; which one shows the drag "
                    + "preview is unspecified. Keep a single overlay per mounted tree.");
            }
            // Forced inline for the same reason AnchoredDriver forces absolute: dynamic left/top has no
            // other way to work. PickingMode.Ignore keeps the ghost from intercepting the drop or waking
            // the cross-panel pointer router.
            positioner.pickingMode = PickingMode.Ignore;
            positioner.style.position = Position.Absolute;
            positioner.style.display = DisplayStyle.None;
            return new DndOverlayBinding();
        }

        public static void Detach(VisualElement positioner, ReconcilerContext ctx)
        {
            ctx.ActiveDrag?.OnOverlayInvalidated(positioner);
            positioner.pickingMode = PickingMode.Position;
            positioner.style.position = StyleKeyword.Null;
            positioner.style.display = StyleKeyword.Null;
            positioner.style.left = StyleKeyword.Null;
            positioner.style.top = StyleKeyword.Null;
            positioner.style.width = StyleKeyword.Null;
            positioner.style.height = StyleKeyword.Null;
        }

        // One overlay per mounted tree: the first registered positioner wins (dnd-kit renders one
        // DragOverlay per DndContext; several in one Velvet tree would fight over one pointer).
        public static DndOverlayBinding? FindOverlay(ReconcilerContext ctx, out VisualElement? positioner)
        {
            foreach (var (element, binding) in ctx.DragOverlayBindings)
            {
                positioner = element;
                return binding;
            }
            positioner = null;
            return null;
        }

        public static void BeginSession(VisualElement positioner, Vector2 sourceSize)
        {
            positioner.style.display = DisplayStyle.Flex;
            positioner.style.width = sourceSize.x;
            positioner.style.height = sourceSize.y;
        }

        public static void EndSession(VisualElement positioner)
        {
            positioner.style.display = DisplayStyle.None;
        }

        // Converts the source panel's pointer position into the overlay panel's space through screen
        // space (PanelToScreen's derived inverse affine + ScreenToPanel), and pins the positioner so the
        // grab point stays under the pointer. Editor-context panels have no defined screen mapping for
        // this conversion — degrade to hidden with a one-shot warning (the layer host itself already
        // degrades in editor mounts; this guard covers a positioner that got created anyway).
        public static void SyncPosition(
            VisualElement positioner, DndOverlayBinding binding,
            IPanel? sourcePanel, Vector2 pointerPanelPosition, Vector2 grabOffset)
        {
            var overlayPanel = positioner.panel;
            if (overlayPanel == null || sourcePanel == null)
            {
                return;
            }
            Vector2 overlayLocal;
            if (ReferenceEquals(overlayPanel, sourcePanel))
            {
                overlayLocal = pointerPanelPosition;
            }
            else
            {
                if (sourcePanel.contextType != ContextType.Player || overlayPanel.contextType != ContextType.Player)
                {
                    if (!binding.WarnedUnsupportedPanel)
                    {
                        binding.WarnedUnsupportedPanel = true;
                        FiberLogger.LogWarning("Dnd",
                            "V.DragOverlay needs runtime (Player-context) panels on both ends of its "
                            + "panel-space conversion; hiding the overlay in this editor-context mount.");
                    }
                    positioner.style.display = DisplayStyle.None;
                    return;
                }
                var screen = FiberCrossPanelPointerRouter.PanelToScreen(sourcePanel, pointerPanelPosition);
                overlayLocal = RuntimePanelUtils.ScreenToPanel(overlayPanel, screen);
            }
            var topLeft = overlayLocal - grabOffset;
            positioner.style.left = topLeft.x;
            positioner.style.top = topLeft.y;
        }
    }

    // Settles the press-derived styling state after the session swallowed the real PointerUp
    // (StopImmediatePropagation runs before the bubble-phase signal callbacks, and captured delivery
    // hides it from ancestors entirely): every manipulator holding an ElementLocalVariantSignals on the
    // source OR any of its ancestors is told to observe a synthetic release — the press's own
    // PointerDown BUBBLED, so ancestors' whileTap / active: (and stacked forms) lit too and would
    // otherwise stick on.
    internal static class DndPressVariantSettler
    {
        public static void Settle(VisualElement element, ReconcilerContext ctx)
        {
            for (var current = element; current != null; current = current.parent)
            {
                SettleOn(current, ctx);
            }
        }

        private static void SettleOn(VisualElement element, ReconcilerContext ctx)
        {
            if (ctx.GestureManipulators.TryGetValue(element, out var gesture))
            {
                gesture.SettleRelease();
            }
            if (ctx.VariantManipulators.TryGetValue(element, out var variant))
            {
                variant.SettleRelease();
            }
            if (ctx.StackedVariantManipulators.Count > 0)
            {
                foreach (var kv in ctx.StackedVariantManipulators)
                {
                    if (kv.Key.target == element)
                    {
                        kv.Value.SettleRelease();
                    }
                }
            }
        }
    }
}
