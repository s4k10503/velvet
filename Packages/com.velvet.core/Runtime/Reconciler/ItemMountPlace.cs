#nullable enable
namespace Velvet
{
    // Where the rows of the anchor a V.VirtualList row stands in (FiberNodeFactory.CreateItemMountContainer) stood among
    // the list's rows at the list's last structural pass: the index of the first, how many there were and how many the
    // list held, Offset -1 before any. The anchor carries it as its userData, which is how a structural pass tells the
    // anchor from a row (FiberNodePatcher.ApplyStructuralVariants); the anchor itself stays a plain VisualElement.
    internal sealed class ItemMountPlace
    {
        internal int Offset = -1;
        internal int Count;
        internal int Total;
    }
}
