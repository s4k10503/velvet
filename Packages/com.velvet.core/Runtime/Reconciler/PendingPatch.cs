#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // What one half of a node's patch needs to write the element once the pass applies its recorded ops: the patcher
    // that commits it, the nodes the diff ran between, and the cleanup count when it was recorded, which an op reads to
    // tell the element was cleaned since. A tail carries what CommitTail reads instead of the nodes: the class arrays and
    // the clip verdict of the post-children passes, or the layout id and timing of the layoutId driver's call. Kept in
    // ChildListWrites' list beside the op it belongs to, so recording one allocates nothing.
    internal readonly struct PendingPatch
    {
        internal PendingPatch(FiberNodePatcher patcher, VisualElement element, VNode? oldNode, VNode? newNode, int serial)
        {
            Patcher = patcher;
            Element = element;
            OldNode = oldNode;
            NewNode = newNode;
            Serial = serial;
            OldClasses = null;
            NewClasses = null;
            // MUTANT_SURVIVES(equivalent, literal): only a tail reads the clip verdict, and a tail is built by the other constructors.
            ClipActive = false;
            LayoutId = null;
            Timing = default;
        }

        internal PendingPatch(FiberNodePatcher patcher, VisualElement element, string[] oldClasses, string[] newClasses,
            bool clipActive, int serial)
        {
            Patcher = patcher;
            Element = element;
            OldNode = null;
            NewNode = null;
            Serial = serial;
            OldClasses = oldClasses;
            NewClasses = newClasses;
            ClipActive = clipActive;
            LayoutId = null;
            Timing = default;
        }

        internal PendingPatch(FiberNodePatcher patcher, VisualElement element, string layoutId, LayoutIdTiming timing, int serial)
        {
            Patcher = patcher;
            Element = element;
            OldNode = null;
            NewNode = null;
            Serial = serial;
            OldClasses = null;
            NewClasses = null;
            // MUTANT_SURVIVES(equivalent, literal): CommitTail returns in its LayoutId branch, ahead of the one read of the
            // clip verdict.
            ClipActive = false;
            LayoutId = layoutId;
            Timing = timing;
        }

        internal FiberNodePatcher Patcher { get; }
        internal VisualElement Element { get; }
        internal VNode? OldNode { get; }
        internal VNode? NewNode { get; }
        internal int Serial { get; }
        internal string[]? OldClasses { get; }
        internal string[]? NewClasses { get; }
        internal bool ClipActive { get; }
        internal string? LayoutId { get; }
        internal LayoutIdTiming Timing { get; }
    }
}
