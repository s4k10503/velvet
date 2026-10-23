#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // The state one drag-and-drop scope publishes to the hooks under it: dnd-kit's public context, its active
    // draggable's transform, and its monitor listeners. V.DndContext provides it to its subtree; a scope declared
    // through the raw props slot gets one of its own on its binding, so the session publishes the same way under
    // either and only the hooks need the provider.
    internal sealed class DndContextStore
    {
        internal static readonly ComponentContext<DndContextStore?> Context = ComponentContext<DndContextStore?>.Create();

        // What a hook reads where no V.DndContext is above it: idle forever, since nothing publishes into it.
        internal static readonly DndContextStore Detached = new();

        internal readonly StateStore State = new();
        internal readonly TransformStore Transform = new();
        private readonly List<DndMonitorListener> _monitors = new();

        internal sealed class StateStore : Store<DndContextState>
        {
            internal StateStore() : base(DndContextState.Idle)
            {
            }

            internal void Publish(DndContextState next) => SetState(_ => next);

            protected override void ResetCore() => Publish(DndContextState.Idle);
        }

        internal sealed class TransformStore : Store<DndActiveTransform>
        {
            internal TransformStore() : base(default)
            {
            }

            internal void Publish(DndActiveTransform next) => SetState(_ => next);

            protected override void ResetCore() => Publish(default);
        }

        internal System.Action Register(DndMonitorListener listener)
        {
            _monitors.Add(listener);
            return () => _monitors.Remove(listener);
        }

        // Over a copy: a listener may unregister itself or another, as a monitor's effect cleanup can while the
        // dispatch's own state writes commit.
        internal void Dispatch<TArgs>(TArgs args, System.Func<DndMonitorListener, System.Action<TArgs>?> pick)
        {
            if (_monitors.Count == 0)
            {
                return;
            }
            foreach (var listener in _monitors.ToArray())
            {
                pick(listener)?.Invoke(args);
            }
        }
    }

    // The active draggable's applied translate, published apart from DndContextState so a move re-renders only a
    // component reading the transform, as dnd-kit's ActiveDraggableContext is apart from its public context.
    internal readonly record struct DndActiveTransform(string? ActiveId, Vector2 Value);
}
