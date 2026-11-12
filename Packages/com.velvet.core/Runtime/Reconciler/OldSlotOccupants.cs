#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The element each old leaf of one container occupies, read from the live child list, beside the element a patch
    // of it reaches through a wrapper. Entry i is old leaf i, at logical slot slotStart + i; a slot nothing occupied
    // is null. Resolve reads at once; Defer reads at the first entry taken or at EnsureResolved, so a pass that takes
    // none and never parks walks no child. The diff takes the element it patches, reuses or removes from here rather
    // than from the live list — ContainerOldInstanceReadTests fails where a slot read comes back — while inserts stay
    // addressed by slot. An entry it takes has to be read before anything changes what stands at its slot: the step
    // that takes a leaf reads its entry before it writes, a removal reads the entry of a leaf the walk kept no element
    // for, and a pass calls EnsureResolved before it parks, since other work runs before its next slice. A patch does
    // not write its result back. Once read from the list, an entry changes only where a z-layer swap names the slot's
    // new element (ReplaceOccupant) or the list is read again (ResolveAgain). A parked pass owns its instance until
    // the slice that finishes it, or a fresh pass or Dispose that discards it.
    internal sealed class OldSlotOccupants
    {
        private readonly List<VisualElement?> _occupants = new();
        private readonly List<VisualElement?> _resolved = new();
        private readonly List<int> _physical = new();
        // What the last Resolve or Defer was asked, for EnsureResolved and ResolveAgain, and whether the list has been
        // read for it.
        private VisualElement? _parent;
        private int _slotStart;
        private int _count;
        private int _slotLimit;
        private FiberNodePatcher? _patcher;
        private bool _read;

        // The element at the old leaf's slot: a wrapper, a z-layer placeholder or the element itself.
        internal VisualElement? Occupant(int index) => Entry(_occupants, index, null);

        // The element a patch of the old leaf is handed (FiberNodePatcher.ResolveWrapped of the occupant).
        internal VisualElement? Resolved(int index) => Entry(_resolved, index, null);

        // The child index the occupant stood at when the list was read, which a removal of it searches from
        // (HostCommitQueue.Remove); -1 where the slot had none.
        internal int PhysicalAtEntry(int index) => Entry(_physical, index, -1);

        private T Entry<T>(List<T> entries, int index, T none)
        {
            EnsureResolved();
            return (uint)index < (uint)entries.Count ? entries[index] : none;
        }

        internal void Resolve(VisualElement parent, int slotStart, int count, int slotLimit, FiberNodePatcher patcher)
        {
            Defer(parent, slotStart, count, slotLimit, patcher);
            Read();
        }

        internal void Defer(VisualElement parent, int slotStart, int count, int slotLimit, FiberNodePatcher patcher)
        {
            Clear();
            _parent = parent;
            _slotStart = slotStart;
            _count = count;
            _slotLimit = slotLimit;
            _patcher = patcher;
        }

        internal void EnsureResolved()
        {
            if (!_read && _parent != null) Read();
        }

        // One walk over the child list, bounded by slotLimit as the indexed diff's slot-missing guard is: where a
        // caller passes a following tenant's start, that tenant's rows are not taken for this container's.
        private void Read()
        {
            var parent = _parent!;
            var end = Math.Min(_slotStart + _count, _slotLimit);
            var logical = 0;
            var physicalCount = parent.childCount;
            for (var physical = 0; physical < physicalCount && logical < end; physical++)
            {
                var child = parent[physical];
                if (LogicalChildSlots.IsInvisible(child))
                {
                    continue;
                }
                if (logical >= _slotStart)
                {
                    _occupants.Add(child);
                    _resolved.Add(_patcher!.ResolveWrapped(child));
                    _physical.Add(physical);
                }
                logical++;
            }
            // MUTANT_SURVIVES(equivalent): an entry padded past the count reads as an index past the entries does.
            // Entry answers such an index with the same null and -1 written here.
            while (_occupants.Count < _count)
            {
                _occupants.Add(null);
                _resolved.Add(null);
                _physical.Add(-1);
            }
            _read = true;
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
            _read = false;
        }
    }
}
