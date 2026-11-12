using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The children of containers, each container read once while the cache is held, so a pass resolving many slots or
    // positions in one container reads it once rather than once per question. The slot-th entry of a container's list
    // is the child LogicalChildSlots.TryGetPhysical finds at that slot, and a child's index and slot are what
    // VisualElement.IndexOf and LogicalChildSlots.ToLogical give for it. Every answer is the container as the cache
    // first read it: a change made to its children while the cache is held, by a callback the pass runs, is not seen.
    internal sealed class RenderedChildrenCache
    {
        // An instance per holder on the stack, so a pass nested in another takes one of its own.
        private static readonly Stack<RenderedChildrenCache> s_pool = new();

        private readonly Dictionary<VisualElement, List<VisualElement>> _of = new();
        private readonly Dictionary<(VisualElement Container, VisualElement Child), (int Index, int Slot)> _positionOf = new();
        private readonly Stack<List<VisualElement>> _spare = new();

        internal static RenderedChildrenCache Rent() => s_pool.Count > 0 ? s_pool.Pop() : new RenderedChildrenCache();

        internal void Return()
        {
            Clear();
            s_pool.Push(this);
        }

        internal void Clear()
        {
            foreach (var list in _of.Values)
            {
                list.Clear();
                _spare.Push(list);
            }
            _of.Clear();
            _positionOf.Clear();
        }

        // The children that hold a logical slot, in slot order.
        internal List<VisualElement> Of(VisualElement container)
        {
            if (_of.TryGetValue(container, out var rendered)) return rendered;
            rendered = _spare.Count > 0 ? _spare.Pop() : new List<VisualElement>();
            var count = container.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container[i];
                _positionOf[(container, child)] = (i, rendered.Count);
                if (!LogicalChildSlots.IsInvisible(child)) rendered.Add(child);
            }
            _of[container] = rendered;
            return rendered;
        }

        // container.IndexOf(child): -1 where container does not list the child.
        internal int IndexIn(VisualElement container, VisualElement child)
        {
            Of(container);
            return _positionOf.TryGetValue((container, child), out var position) ? position.Index : -1;
        }

        // LogicalChildSlots.ToLogical(container, container.IndexOf(child)): 0 where container does not list the child.
        internal int SlotIn(VisualElement container, VisualElement child)
        {
            Of(container);
            return _positionOf.TryGetValue((container, child), out var position) ? position.Slot : 0;
        }

        // Where the fiber's output starts: its first row, or the row a fiber holding none sits in front of. Null for
        // a fiber holding none behind the last row of a container, which endOf names, and for a fiber mounted
        // nowhere.
        internal VisualElement? StartOf(ComponentFiber fiber, out VisualElement? endOf)
        {
            endOf = null;
            var mount = fiber.MountPoint;
            if (mount == null) return null;
            var rendered = Of(mount);
            var slot = fiber.MountSlotStart;
            if (slot >= 0 && slot < rendered.Count) return rendered[slot];
            endOf = mount;
            return null;
        }
    }
}
