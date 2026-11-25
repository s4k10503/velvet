#nullable enable
namespace Velvet
{
    // Where the rows of the anchor a V.VirtualList row stands in (FiberNodeFactory.CreateItemMountContainer) stood among
    // the list's rows at the list's last structural pass: the index of the first, how many there were and how many the
    // list held, Offset -1 before any, and the range placement that pass followed. The anchor carries it as its
    // userData, which is how a structural pass tells the anchor from a row (FiberNodePatcher.ApplyStructuralVariants);
    // the anchor itself stays a plain VisualElement.
    internal sealed class ItemMountPlace
    {
        // How many range placements the lists have ended (IReconcilerBridge.EndRowPlacementForController). A placement
        // moves rows, and the pass over the list that follows it records no place in a context holding no structural
        // rule, so a place recorded before the latest placement is not read.
        internal static int Placements;

        internal int Offset = -1;
        internal int Count;
        internal int Total;
        internal int Placement;
    }
}
