#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element each old leaf of one container occupies, read from the live child list at the container's
    // entry, beside the element a patch of it reaches through a wrapper. Entry i is old leaf i, at logical slot
    // slotStart + i; a slot nothing occupied is null. After the entry the diff names an old instance through this
    // alone — ContainerOldInstanceReadTests fails where a slot read comes back — and an entry changes only where a
    // patch of that leaf hands back its slot's new occupant (Replace) or the list is read again (ResolveAgain). A
    // parked pass owns its instance until the slice that finishes it, or a fresh pass that discards it.
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

        // One walk over the child list. Bounded by slotLimit as the indexed diff's slot-missing guard is, so a
        // following tenant's row is never taken for one of this container's.
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
