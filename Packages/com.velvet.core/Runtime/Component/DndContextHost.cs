#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // V.DndContext's component, as dnd-kit's DndContext is one: it keeps the scope's store for the life of the
    // instance and provides it to the subtree, so the drag hooks read the scope the way dnd-kit's read its context.
    // Not beside its factory in Component/V.cs: same reason Navigate is not — this body calls hooks.
    internal static class DndContextHost
    {
        internal sealed class Props
        {
            internal DndContextSettings Settings { get; init; } = new();
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

        // The element is built here rather than in the factory: its settings carry the store, which exists only
        // once an instance does.
        [Component(Compiler = false)]
        public static VNode Render(Props props)
        {
            var store = Hooks.UseRef(() => new DndContextStore()).Current!;
            var scope = V.DndScopeElement(props, props.Settings with { Store = store });
            return V.Provider(DndContextStore.Context, store, children: new VNode[] { scope });
        }
    }
}
