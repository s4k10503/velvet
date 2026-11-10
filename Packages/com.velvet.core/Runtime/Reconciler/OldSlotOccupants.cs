#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element each old leaf of one container occupies, read from the live child list at the container's
    // entry, beside the element a patch of it reaches through a wrapper. Entry i is old leaf i, at logical slot
    // slotStart + i; a slot nothing occupied is null. After the entry the diff takes the element it patches or
    // reuses from here rather than from the live list — ContainerOldInstanceReadTests fails where a slot read comes
    // back — while removals and inserts stay addressed by slot. An entry changes only where a patch of that leaf
    // hands back its slot's new occupant (Replace), a z-layer swap names the slot's new element
    // (ReplaceOccupant), or the list is read again (ResolveAgain). A parked pass owns its instance until the slice
    // that finishes it, or a fresh pass or Dispose that discards it.
    internal sealed class OldSlotOccupants
    {
        private readonly List<VisualElement?> _occupants = new();
        private readonly List<VisualElement?> _resolved = new();
        // What the last Resolve was asked, for ResolveAgain.
        private VisualElement? _parent;
        private int _slotStart;
        private int _count;
        private int _slotLimit;
        private FiberNodePatcher? _patcher;

        internal int Count => _occupants.Count;

        // The element at the old leaf's slot: a wrapper, a z-layer placeholder or the element itself.
        internal VisualElement? Occupant(int index)
            => (uint)index < (uint)_occupants.Count ? _occupants[index] : null;

        // The element a patch of the old leaf is handed (FiberNodePatcher.ResolveWrapped of the occupant).
        internal VisualElement? Resolved(int index)
            => (uint)index < (uint)_resolved.Count ? _resolved[index] : null;

        // One walk over the child list, bounded by slotLimit as the indexed diff's slot-missing guard is: where a
        // caller passes a following tenant's start, that tenant's rows are not taken for this container's.
        internal void Resolve(VisualElement parent, int slotStart, int count, int slotLimit, FiberNodePatcher patcher)
        {
            Clear();
            _parent = parent;
            _slotStart = slotStart;
            _count = count;
            _slotLimit = slotLimit;
            _patcher = patcher;
            var end = Math.Min(slotStart + count, slotLimit);
            var logical = 0;
            var physicalCount = parent.childCount;
            for (var physical = 0; physical < physicalCount && logical < end; physical++)
            {
                var child = parent[physical];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    continue;
                }
                if (logical >= slotStart)
                {
                    _occupants.Add(child);
                    _resolved.Add(patcher.ResolveWrapped(child));
                }
                logical++;
            }
            while (_occupants.Count < count)
            {
                _occupants.Add(null);
                _resolved.Add(null);
            }
        }

        // The same resolve over the child list as it stands now. A walk whose rows another pass wrote while it ran
        // (FiberCommitWork.DrainPendingWork finishing a parked fiber the walk renders) takes them from here.
        internal void ResolveAgain()
        {
            if (_parent != null) Resolve(_parent, _slotStart, _count, _slotLimit, _patcher!);
        }

        // What a patch of the old leaf left at its slot (FiberNodePatcher.PatchNode's result).
        internal void Replace(int index, VisualElement occupant, FiberNodePatcher patcher)
        {
            _occupants[index] = occupant;
            _resolved[index] = patcher.ResolveWrapped(occupant);
        }

        // ReconcilerContext.NoteSlotOccupantChanged: the slot `from` held under parent now holds `to`.
        internal void ReplaceOccupant(VisualElement parent, VisualElement from, VisualElement to)
        {
            if (!ReferenceEquals(_parent, parent)) return;
            for (var i = 0; i < _occupants.Count; i++)
            {
                if (ReferenceEquals(_occupants[i], from)) Replace(i, to, _patcher!);
            }
        }

        internal void Clear()
        {
            _occupants.Clear();
            _resolved.Clear();
            _parent = null;
            _slotStart = 0;
            _count = 0;
            _slotLimit = 0;
            _patcher = null;
        }
    }
}
