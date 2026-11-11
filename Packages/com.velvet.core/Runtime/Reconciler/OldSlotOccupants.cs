#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element each old leaf of one container occupies, read from the live child list at the container's
    // entry, beside the element a patch of it reaches through a wrapper. Entry i is old leaf i, at logical slot
    // slotStart + i; a slot nothing occupied is null. After the entry the diff takes the element it patches, reuses
    // or removes from here rather than from the live list — ContainerOldInstanceReadTests fails where a slot read
    // comes back — while inserts stay addressed by slot. The step that takes a leaf reads its entry, and a removal
    // reads the entry of a leaf the walk kept no element for; a patch does not write its result back. Once read from
    // the list, an entry changes only where a z-layer swap names the slot's new element (ReplaceOccupant) or the list
    // is read again (ResolveAgain). A parked pass owns its instance until the slice that finishes it, or a fresh pass
    // or Dispose that discards it.
    internal sealed class OldSlotOccupants
    {
        private readonly List<VisualElement?> _occupants = new();
        private readonly List<VisualElement?> _resolved = new();
        private readonly List<int> _physical = new();
        // What the last Resolve was asked, for ResolveAgain.
        private VisualElement? _parent;
        private int _slotStart;
        private int _count;
        private int _slotLimit;
        private FiberNodePatcher? _patcher;

        // The element at the old leaf's slot: a wrapper, a z-layer placeholder or the element itself.
        internal VisualElement? Occupant(int index)
            => (uint)index < (uint)_occupants.Count ? _occupants[index] : null;

        // The element a patch of the old leaf is handed (FiberNodePatcher.ResolveWrapped of the occupant).
        internal VisualElement? Resolved(int index)
            => (uint)index < (uint)_resolved.Count ? _resolved[index] : null;

        // The child index the occupant stood at when the list was read, which a removal of it searches from
        // (HostCommitQueue.Remove); -1 where the slot had none.
        internal int PhysicalAtEntry(int index)
            => (uint)index < (uint)_physical.Count ? _physical[index] : -1;

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
                    _physical.Add(physical);
                }
                logical++;
            }
            while (_occupants.Count < count)
            {
                _occupants.Add(null);
                _resolved.Add(null);
                _physical.Add(-1);
            }
        }

        // The same resolve over the child list as it stands now. A walk whose rows another pass wrote while it ran
        // (FiberCommitWork.DrainPendingWork finishing a parked fiber the walk renders) takes them from here.
        internal void ResolveAgain()
        {
            if (_parent != null) Resolve(_parent, _slotStart, _count, _slotLimit, _patcher!);
        }

        // ReconcilerContext.NoteSlotOccupantChanged: the slot `from` held now holds `to`. No parent is compared. `from`
        // stands under the container the swap is in, so an entry naming it in another container's instance names an
        // element that container's diff removed; a diff removes an old leaf only once the step that takes it has run,
        // or where no step takes it, so that entry is not read again.
        internal void ReplaceOccupant(VisualElement from, VisualElement to)
        {
            for (var i = 0; i < _occupants.Count; i++)
            {
                if (!ReferenceEquals(_occupants[i], from)) continue;
                _occupants[i] = to;
                _resolved[i] = _patcher!.ResolveWrapped(to);
            }
        }

        internal void Clear()
        {
            _occupants.Clear();
            _resolved.Clear();
            _physical.Clear();
            _parent = null;
            _slotStart = 0;
            _count = 0;
            _slotLimit = 0;
            _patcher = null;
        }
    }
}
