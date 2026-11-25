#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // The anchor a V.VirtualList row stands in where its renderer returned a component or a Provider, neither of which
    // emits an element of its own (FiberNodeFactory.CreateItemMountContainer). A type of its own so a structural pass
    // tells it from a row at the cost of a type test (FiberNodePatcher.ApplyStructuralVariants).
    internal sealed class ItemMountContainer : VisualElement
    {
    }
}
