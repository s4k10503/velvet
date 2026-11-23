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
    // FiberRenderer owns the journal's two ends: the render that opens it completes the pass, or drops it when a
    // suspend that nothing in the pass catches abandons it.
    internal sealed class ReconcilePass
    {
        internal enum OpKind : byte { Insert, RemoveAt, RemoveChild, PatchPre, PatchPost, Teardown }

        internal struct Op
        {
            internal VisualElement Container;
            internal VisualElement Caller;
            internal VisualElement Child;
            // The deletion a Teardown runs.
            internal DeletionCommit? Deletion;
            // The index into Patches of a PatchPre or a PatchPost.
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

        // The renders of the pass on the stack, which a suspend nothing catches takes back.
        internal FiberPassJournal Journal { get; } = new();

        internal bool Recording;
        internal bool Applying;
        // Raised by HostCommitQueue around the writes it makes, which are the ones recorded.
        internal int ViaQueue;

        // The pass that was current when this one began, which becomes current again where this one ends
        // (ChildListWrites.EndRecording).
        internal ReconcilePass? Enclosing;
        // How many times BeginRecording has begun this pass without a matching end.
        internal int Depth;

        // Returns whether this call opened the journal, which makes the caller the render that completes or drops the pass.
        internal bool TryOwn(ComponentFiber owner) => Journal.TryOpen(owner);

        internal void Complete() => Journal.Close();

        internal void Drop() => Journal.Abandon();
    }
}
