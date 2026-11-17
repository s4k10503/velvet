#nullable enable
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What one context's pass owns while it runs: the child-list writes it has recorded and not applied, the model
    // state those writes leave pending, and the journal of the renders it makes tentative. A context holds one
    // (ReconcilerContext.Pass) and reuses it for every top-level pass it runs.
    //
    // ChildListWrites owns what the recorded writes mean and how they apply; the fields here are its state.
    // FiberRenderer owns the journal's ends: the render that opens it completes the pass, or drops it when a suspend
    // that nothing in the pass catches abandons it. A wave holds the journal open across the renders it records into
    // the pass (FiberWave), and a render that would have opened it completes or drops a segment of it instead.
    internal sealed class ReconcilePass
    {
        internal enum OpKind : byte { Insert, RemoveAt, RemoveChild, PatchPre, PatchPost, PatchTail, Teardown, FiberBegin, FiberEnd }

        internal struct Op
        {
            internal VisualElement Container;
            internal VisualElement Caller;
            internal VisualElement Child;
            // The deletion a Teardown runs.
            internal DeletionCommit? Deletion;
            // The turn a FiberBegin or a FiberEnd marks, and where the teardowns of the deletions that run at it are
            // attached.
            internal TurnAnchor? Anchor;
            // The index into Patches of a PatchPre, a PatchPost or a PatchTail.
            internal int Index;
            internal OpKind Kind;
            internal bool ViaHierarchy;
        }

        internal readonly List<Op> Ops = new();
        internal readonly List<PendingPatch> Patches = new();
        // Where the model holds each child a recorded write touched; null where the write took it out.
        internal readonly Dictionary<VisualElement, VisualElement?> PendingParent = new();
        // Each container a recorded write touched, with the count this pass added to its model's Pending, which is
        // what the pass takes off again where it applies.
        internal readonly Dictionary<VisualElement, int> PendingContainers = new();
        internal readonly HashSet<VisualElement> Created = new();

        // Where a wave records several roots' renders into the pass: the index of the first op each run belongs to, and
        // the fiber whose render recorded it. Apply files a failed op under the root of its run where Owners names no
        // component for it, and clears it with the ops.
        internal readonly List<(int FirstOp, ComponentFiber? Root)> Segments = new();

        // The root of the op Apply is applying, whose run an op recorded behind it belongs to.
        internal ComponentFiber? ApplyingRoot;

        // The component that rendered each element created while a wave records into the pass, which a failed insert
        // of the element is filed under in place of the root of its run. Apply clears it with the ops.
        internal readonly Dictionary<VisualElement, ComponentFiber> Owners = new();

        // What the ops, z-layer mounts and Portal mounts of a wave's pass threw, for the wave to route (FiberWave): the
        // component each is filed under, the element whose panel decides whether it is reported, kept only where that
        // element was on a panel as it threw, and whether it is an element's placement, whose search for a boundary
        // starts above the fibers the wave mounted.
        internal readonly List<(ComponentFiber Owner, System.Exception Error, VisualElement? OnPanel, bool Placement)>
            Failures = new();

        // The fibers whose first render the open wave made, which a placement's search passes over.
        internal readonly HashSet<ComponentFiber> MountedInWave = new();

        internal void BeginSegment(ComponentFiber? root) => Segments.Add((Ops.Count, root));

        internal void Fail(ComponentFiber owner, System.Exception error, VisualElement? element, bool placement)
            => Failures.Add((owner, error, element?.panel != null ? element : null, placement));

        // The renders of the pass on the stack, which a suspend nothing catches takes back.
        internal FiberPassJournal Journal { get; } = new();

        private readonly Stack<TurnAnchor> _freeAnchors = new();
        private readonly List<TurnAnchor> _anchorsInUse = new();

        internal TurnAnchor RentAnchor()
        {
            var anchor = _freeAnchors.Count > 0 ? _freeAnchors.Pop() : new TurnAnchor();
            _anchorsInUse.Add(anchor);
            return anchor;
        }

        // Where the pass ends: nothing holds an anchor any longer, since the walks that kept them in a table have returned.
        internal void ReleaseAnchors()
        {
            foreach (var anchor in _anchorsInUse)
            {
                anchor.Reset();
                _freeAnchors.Push(anchor);
            }
            _anchorsInUse.Clear();
        }

        internal bool Recording;
        internal bool Applying;
        // Raised by HostCommitQueue around the writes it makes, which are the ones recorded.
        internal int ViaQueue;

        // Returns whether this call opened the journal, which makes the caller the render that completes or drops the pass.
        internal bool TryOwn(ComponentFiber owner) => Journal.TryOpen(owner);

        internal void Complete()
        {
            if (Journal.IsHeld) Journal.EndSegment();
            else Journal.Close();
        }

        internal void Drop() => Journal.Abandon();
    }
}
