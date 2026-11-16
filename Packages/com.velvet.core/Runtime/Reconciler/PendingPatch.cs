#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // What one half of a node's patch needs to write the element once the pass applies its recorded ops: the patcher
    // that commits it, the nodes the diff ran between, and the cleanup count when it was recorded, which an op reads to
    // tell the element was cleaned since. Kept in ChildListWrites' list beside the op it belongs to, so recording one
    // allocates nothing.
    internal readonly struct PendingPatch
    {
        internal PendingPatch(FiberNodePatcher patcher, VisualElement element, VNode? oldNode, VNode? newNode, int serial)
        {
            Patcher = patcher;
            Element = element;
            OldNode = oldNode;
            NewNode = newNode;
            Serial = serial;
        }

        internal FiberNodePatcher Patcher { get; }
        internal VisualElement Element { get; }
        internal VNode? OldNode { get; }
        internal VNode? NewNode { get; }
        internal int Serial { get; }
    }
}
