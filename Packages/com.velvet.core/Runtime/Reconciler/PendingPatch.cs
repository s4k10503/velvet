#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // What one half of a node's patch needs to write the element once the pass applies its recorded ops: the patcher
    // that commits it and the nodes the diff ran between. Kept in ChildListWrites' list beside the op it belongs to,
    // so recording one allocates nothing.
    internal struct PendingPatch
    {
        internal PendingPatch(FiberNodePatcher patcher, VisualElement element, VNode? oldNode, VNode? newNode)
        {
            Patcher = patcher;
            Element = element;
            OldNode = oldNode;
            NewNode = newNode;
        }

        internal FiberNodePatcher Patcher { get; }
        internal VisualElement Element { get; }
        internal VNode? OldNode { get; }
        internal VNode? NewNode { get; }
    }
}
