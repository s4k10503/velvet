#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // V.SortableContext's and V.Sortable's components. Not beside their factories in Component/V.cs: same
    // reason Navigate is not — these bodies call hooks.
    internal static class SortableContextHost
    {
        private static int s_nextId;

        internal sealed class Props
        {
            internal IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
            internal DndSortingStrategy? Strategy { get; init; }
            internal string? Id { get; init; }
            internal VNode?[]? Children { get; init; }
        }

        [Component(Compiler = false)]
        public static VNode Render(Props props)
        {
            var store = Hooks.UseRef(() => new DndSortableContextStore { Id = "sortable-" + s_nextId++ }).Current!;
            store.Items = props.Items;
            store.Strategy = props.Strategy ?? DndSortingStrategies.Rect;
            if (props.Id != null)
            {
                store.Id = props.Id;
            }
            var value = new DndSortableContextValue(store, store.Items, store.Strategy, store.Id);
            return V.Provider(DndSortableContextValue.Context, value, children: props.Children ?? Array.Empty<VNode?>());
        }
    }

    internal static class SortableHost
    {
        internal sealed class Props
        {
            internal string Id { get; init; } = string.Empty;
            internal object? SortableData { get; init; }
            internal bool Disabled { get; init; }
            internal DragMovement Movement { get; init; }
            internal DragActivation? Activation { get; init; }
            internal DndSortingStrategy? Strategy { get; init; }
            internal string? WhileDraggingClass { get; init; }
            internal string? WhileOverClass { get; init; }
            internal string? ClassName { get; init; }
            internal string? Name { get; init; }
            internal FiberElementProps? ElementProps { get; init; }
            internal StyleOverrides? Styles { get; init; }
            internal Func<VisualElement, Action>? RefCallback { get; init; }
            internal VNode?[]? Children { get; init; }
            internal string? WhileHoverClass { get; init; }
            internal string? WhileTapClass { get; init; }
            internal string? WhileFocusClass { get; init; }
            internal IReadOnlyDictionary<string, string>? Data { get; init; }
            internal IReadOnlyDictionary<string, string>? Aria { get; init; }
            internal FiberEventBinding[]? Events { get; init; }
        }

        // One element that is both the draggable and the droppable under the item's id, carrying the context it
        // sorts in, as dnd-kit's useSortable combines useDraggable and useDroppable on one node.
        [Component(Compiler = false)]
        public static VNode Render(Props props)
        {
            var context = Hooks.UseContext(DndSortableContextValue.Context);
            var node = V.Draggable(props.Id, props.SortableData, props.Disabled, props.Movement, props.Activation,
                props.WhileDraggingClass, props.ClassName, name: props.Name, props: props.ElementProps,
                styles: props.Styles, refCallback: props.RefCallback, children: props.Children,
                whileHoverClass: props.WhileHoverClass, whileTapClass: props.WhileTapClass,
                whileFocusClass: props.WhileFocusClass, data: props.Data, aria: props.Aria, events: props.Events);
            var elementProps = node.Props!;
            if (context != null)
            {
                elementProps.Draggable = elementProps.Draggable! with
                {
                    Sortable = new DndSortableMembership(context.Store, props.Strategy),
                };
            }
            elementProps.Droppable = new DroppableSettings(
                props.Id, props.SortableData, props.Disabled, props.WhileOverClass);
            return node;
        }
    }
}
