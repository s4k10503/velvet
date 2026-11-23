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
        internal enum OpKind : byte { Insert, RemoveAt, RemoveChild, PatchPre, PatchPost, PatchTail, Teardown, FiberBegin, FiberEnd, RefDetach }

        internal struct Op
        {
            internal VisualElement Container;
            internal VisualElement Caller;
            internal VisualElement Child;
            // The deletion a Teardown runs.
            internal DeletionCommit? Deletion;
            // The turn a FiberBegin marks, where the teardowns of the deletions that run at it are attached.
            internal TurnAnchor? Anchor;
            // The fiber whose turn a FiberEnd ends; for a RefDetach, the component whose walk queued the ref that replaces
            // Child's.
            internal ComponentFiber? Fiber;
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

        // The root MountedTree.Dispose unmounts, set as the unmount begins: ChildListWrites.FileFailure files a failed op
        // of a pass no wave owns under it.
        internal ComponentFiber? DisposingRoot;

        // The component that rendered each element created while a wave records into the pass, with the Portal whose
        // children it was created among, which a failed insert of the element is filed under in place of the root of its
        // run, and the place the element's walk ended at (PlaceBehindRecorded), which the failure takes. Apply clears it
        // with the ops.
        internal readonly Dictionary<VisualElement, (ComponentFiber Owner, VisualElement? Portal, long Place)> Owners
            = new();

        // What the ops, z-layer mounts and Portal mounts of a wave's pass threw, for the wave to route (FiberWave): where
        // the search for its boundary starts, the component it belongs to, the element whose panel decides whether it
        // is reported, kept only where that element was on a panel as it threw, and the place the wave's captures take
        // it at.
        internal readonly List<(ComponentFiber? Start, ComponentFiber Owner, System.Exception Error, VisualElement? OnPanel,
            long Place)> Failures = new();

        // The places a wave makes its captures in the order of (CommitErrors.TakeHeld), g counting the ops of its passes
        // ahead of a point. An op the apply runs is at 2g + 1, and what its turn end, ref detach or teardown throws takes
        // that place. An element the walk creates is at 2g where its walk ends, behind the ops of what it holds and ahead
        // of the turns of the rows after it, since the general walk records its insert where the container's walk ends
        // (FiberNodeFactory.CreateElement), behind those turns. Applied counts the ops the pass
        // has applied, over every wave, since only their order within a wave is read. Meets is the place of the op Apply
        // runs or of the failure the wave routes, and NoPlace otherwise, where what is thrown takes the place behind
        // every op applied.
        internal const long NoPlace = -1;
        internal long Applied;
        internal long Meets = NoPlace;

        internal long PlaceBehindRecorded => 2 * (Applied + Ops.Count);

        internal long PlaceNow => Meets != NoPlace ? Meets : 2 * Applied;

        // The captures a wave takes from its opening until its failures are routed, which it then makes in the order of
        // their places (CommitErrors.TakeHeld).
        internal bool HoldsCaptures;
        internal readonly List<(ComponentFiber Boundary, System.Exception Error, ErrorInfo Info, long Place)> HeldCaptures
            = new();

        internal void BeginSegment(ComponentFiber? root) => Segments.Add((Ops.Count, root));

        // Whether Apply runs the FiberEnd and RefDetach ops, which only a wave ending its pass sets
        // (Reconciler.FinishWavePass); Apply clears it as it ends. Where it is clear the layout commit runs that work.
        internal bool RunsTurnEnds;

        internal void Fail(ComponentFiber? start, ComponentFiber owner, System.Exception error, VisualElement? element,
            long place)
            => Failures.Add((start, owner, error, element?.panel != null ? element : null, place));

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
