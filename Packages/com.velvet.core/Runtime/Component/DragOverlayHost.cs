#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a drop animation (DndDropAnimator) tells V.DragOverlay's component: while it runs, the overlay keeps
    // showing the last children it showed, as dnd-kit's DragOverlay keeps its last children through the drop
    // animation after the activeId recipe has cleared them.
    internal sealed class DragOverlayHandle
    {
        internal bool Dropping;
        internal Action? Rerender;

        internal void BeginDrop() => Dropping = true;

        internal void EndDrop()
        {
            if (!Dropping)
            {
                return;
            }
            Dropping = false;
            Rerender?.Invoke();
        }
    }

    // V.DragOverlay's component. Not beside its factory in Component/V.cs: same reason Navigate is not — this
    // body calls hooks.
    internal static class DragOverlayHost
    {
        internal sealed class Props
        {
            internal VNode?[]? Children { get; init; }
            internal DndDropAnimation? DropAnimation { get; init; }
            internal IReadOnlyList<DndModifier>? Modifiers { get; init; }
        }

        [Component(Compiler = false)]
        public static VNode Render(Props props)
        {
            var handle = Hooks.UseRef(() => new DragOverlayHandle()).Current!;
            // Held in a ref so a retirement of the tree that showed them leaves their pooled parts alone while the
            // drop animation shows them again.
            var retained = Hooks.UseRef<VNode?[]>();
            var (_, setVersion) = Hooks.UseState(0);
            handle.Rerender = () => setVersion.Invoke(version => version + 1);

            var children = props.Children ?? Array.Empty<VNode?>();
            VNode?[] shown;
            if (children.Length > 0)
            {
                retained.Set(children);
                shown = children;
            }
            else if (handle.Dropping && retained.Current != null)
            {
                shown = retained.Current;
            }
            else
            {
                retained.Set(null);
                shown = children;
            }

            var positionerProps = VNodePool.RentProps();
            positionerProps.DragOverlay = new DragOverlaySettings
            {
                DropAnimation = props.DropAnimation,
                Modifiers = props.Modifiers,
                Handle = handle,
            };
            var positioner = new ElementNode
            {
                Key = "drag-overlay-positioner",
                ElementType = typeof(VisualElement),
                Props = positionerProps,
                Children = shown,
                Events = Array.Empty<FiberEventBinding>(),
            };
            // An editor panel has no runtime layer to portal into that shares its pointer's space, so there the
            // positioner stays where it is declared, on the source's own panel. A drag preview is not a place Tab
            // goes.
            return InEditorPanel()
                ? positioner
                : V.Portal(UILayer.Overlay, focusOrder: PanelFocusOrder.Isolated, children: new VNode?[] { positioner });
        }

        // Read off the mount point of the tree's root fiber: the context's MainPanelRoot is written after the first
        // render, which is the render that decides whether to portal.
        private static bool InEditorPanel()
        {
            var fiber = FiberAmbientStack.Current;
            while (fiber?.Parent != null)
            {
                fiber = fiber.Parent;
            }
            return fiber?.MountPoint?.panel?.contextType == ContextType.Editor;
        }
    }
}
