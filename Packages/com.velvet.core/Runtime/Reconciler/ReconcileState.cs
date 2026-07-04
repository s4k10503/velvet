#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    internal enum IndexedReconcilePhase
    {
        Common,
        Remove,
        Add,
    }

    internal readonly struct IndexedReconcileState
    {
        public readonly VisualElement? Parent;
        public readonly VNode?[] OldNodes;
        public readonly VNode?[] NewNodes;
        public readonly IndexedReconcilePhase ResumePhase;
        public readonly int ResumeIndex;
        public readonly int SlotStart;
        // Exclusive end of this fiber's slot range (the next inline sibling's MountSlotStart, or int.MaxValue when
        // last/only tenant). Bounds the Common-phase desync recovery so it cannot patch a following sibling's row.
        public readonly int SlotLimit;

        public IndexedReconcileState(
            VisualElement? parent,
            VNode?[] oldNodes,
            VNode?[] newNodes,
            IndexedReconcilePhase resumePhase,
            int resumeIndex,
            int slotStart = 0,
            int slotLimit = int.MaxValue)
        {
            Parent = parent;
            OldNodes = oldNodes;
            NewNodes = newNodes;
            ResumePhase = resumePhase;
            ResumeIndex = resumeIndex;
            SlotStart = slotStart;
            SlotLimit = slotLimit;
        }
    }

    // Suspension phase of keyed reconciliation.
    // A keyed-children diff could run to completion synchronously, but Velvet yields per VNode
    // at the ChildReconciler layer, so Pass 2 is also suspendable per VNode.
    internal enum KeyedReconcilePhase
    {
        // Pass 1 linear scan (patch while oldKey == newKey).
        Pass1Linear,
        // Pass 1 consumed all old nodes; only tail appends remain.
        TailAdd,
        // Pass 1 consumed all new nodes; only tail removals remain.
        TailRemove,
        // Pass 2: building the oldKeyMap.
        Pass2BuildMap,
        // Pass 2: walking newNodes (patch / create / replace).
        Pass2Process,
        // Pass 2: removing unused old entries.
        Pass2Remove,
        // Pass 2: re-placing elements after LIS computation.
        Pass2Reorder,
        // All phases complete. Terminates the dispatch loop.
        Done,
    }

    // Suspended state of keyed reconciliation.
    // The intermediate buffers used by Pass 2 (OldKeyMap / UsedKeys / ReplacedKeys / NewElements /
    // OrphanedOldIndices / LisIndices) are rented from ReconcilerBufferPool and owned by this state,
    // retained while suspended. Returned to the Pool via
    // ChildReconciler.ReleaseKeyedBuffers on completion or when a new Reconcile begins.
    internal sealed class KeyedReconcileState
    {
        #region Inputs — fixed at construction

        // Parent VE the reconciled children are placed into.
        public VisualElement? Parent { get; init; }
        // Child VNode array from the previous render (already FlattenAndFilter'd).
        public VNode?[] OldNodes { get; init; } = null!;
        // Child VNode array requested by the current render (already FlattenAndFilter'd).
        public VNode?[] NewNodes { get; init; } = null!;
        // Zero-based slot offset into Parent.children at which this keyed reconcile operates.
        // Default 0 (Reconcile owns the full children list). Non-zero for wrapper-less fibers that
        // share a parent VE with sibling slots. Settable so a sibling shift that re-bases this
        // parked fiber's slot range can update the offset in place (see
        // ChildReconciler.RebasePendingSlotStart) without re-renting the held buffers.
        public int SlotStart { get; set; }

        // Exclusive end of this fiber's slot range — the next inline-mount sibling's MountSlotStart, or
        // int.MaxValue when this fiber is the last/only tenant. Bounds the desync-recovery rebuild so it cannot
        // delete a following sibling's rows. Captured once at the fresh keyed start (the recovery only runs
        // there), so it does not need re-basing across resume slices.
        public int SlotLimit { get; set; }

        #endregion

        #region Cursor — updated as state-machine advances

        public KeyedReconcilePhase Phase;
        // Exclusive end of the prefix-match range fixed by Pass 1. Also serves as the Pass 2 start index.
        public int LinearEnd;
        // Resume index within the current phase. Meaning differs per phase (Pass1: linear scan position, Pass2Remove: tail-remove position, etc.).
        public int ResumeIndex;

        #endregion

        #region Pass 2 Buffers — rented from ReconcilerBufferPool, released by ReleaseKeyedBuffers

        // Map from key to (domIndex, VNode) for old nodes at indices ≥ linearEnd. Built by Pass2BuildMap.
        public Dictionary<ChildKey, (int index, VNode? node)>? OldKeyMap;
        // Set of old keys consumed by new nodes during Pass2Process. Used to decide which unused keys to remove.
        public HashSet<ChildKey>? UsedKeys;
        // Set of old keys replaced by type swap (CanPatch=false). Identifies elements to remove from the DOM during the removal phase.
        public HashSet<ChildKey>? ReplacedKeys;
        // List of (element, isExisting) in the new placement order, built by Pass2Process. Input to the Reorder phase.
        public List<(VisualElement? element, bool isExisting)>? NewElements;
        // Set of DOM indices of old nodes orphaned by duplicate-key overwrites. Usually empty.
        public HashSet<int>? OrphanedOldIndices;
        // Set of LIS anchor positions (in NewElements indices) computed by ComputeLis. Consulted in Reorder to skip anchors.
        public HashSet<int>? LisIndices;

        #endregion
    }

    // Identity key for a child in the keyed reconcile path. A child is identified either by an
    // explicit key (its own VNode.Key or a Fragment-scoped key) or, when unkeyed, by
    // its full sibling index. Keeping the two kinds in one type but tagged by kind separates
    // explicit string keys from numeric implicit indices: an explicit key and a positional
    // index can never compare equal, so a user key value — including one a Fragment scope composes
    // — cannot collide with an unkeyed sibling's slot.
    internal readonly struct ChildKey : IEquatable<ChildKey>
    {
        private readonly string? _key;
        private readonly int _index;
        private readonly bool _isPositional;

        private ChildKey(string? key, int index, bool isPositional)
        {
            _key = key;
            _index = index;
            _isPositional = isPositional;
        }

        public static ChildKey Explicit(string key) => new(key, 0, false);

        public static ChildKey Positional(int index) => new(null, index, true);

        public bool Equals(ChildKey other)
            => _isPositional
                ? other._isPositional && _index == other._index
                : !other._isPositional && _key == other._key;

        public override bool Equals(object obj) => obj is ChildKey other && Equals(other);

        public override int GetHashCode()
            => _isPositional ? _index : (_key?.GetHashCode() ?? 0);

        public override string ToString()
            => _isPositional
                ? $"position {_index.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : $"\"{_key}\"";
    }
}
