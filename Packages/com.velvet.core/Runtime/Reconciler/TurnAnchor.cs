#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Where a fiber's turn, a construct's, or a container's own begins among a pass's recorded ops: the op that carries
    // it applies the teardowns attached to it, in the order they were attached (ChildListWrites.RecordTeardown), so a
    // deletion runs at its owner's turn without any index into the ops being held. A pass rents them as it records
    // them and takes them all back where it ends (ReconcilePass.ReleaseAnchors).
    internal sealed class TurnAnchor
    {
        internal readonly List<DeletionCommit> Teardowns = new();
        // Set where the anchor's op applies, after which a teardown can no longer be attached to it and is run ahead
        // of the ops left instead (ReconcilePass.Late), in the order the anchors were recorded.
        internal bool Applied;
        internal int Serial;

        internal void Reset()
        {
            Teardowns.Clear();
            Applied = false;
            Serial = 0;
        }
    }

    // A fiber, or a construct of its tree, within one container's walk: the construct is named by the position its
    // nodes were walked at (FiberKeying.WalkPosition.SlotPath), which restarts at the walk's root for each container,
    // so the key means something only beside the walk that recorded it. Turn is zero for the fiber itself.
    internal readonly struct TurnKey : IEquatable<TurnKey>
    {
        internal readonly ComponentFiber? Fiber;
        internal readonly long Turn;

        internal TurnKey(ComponentFiber? fiber, long turn)
        {
            Fiber = fiber;
            Turn = turn;
        }

        public bool Equals(TurnKey other) => ReferenceEquals(Fiber, other.Fiber) && Turn == other.Turn;

        public override int GetHashCode()
            => (Fiber == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Fiber)) * 31 + Turn.GetHashCode();
    }
}
