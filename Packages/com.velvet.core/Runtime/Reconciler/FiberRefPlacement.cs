using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Velvet
{
    // A host ref setup a layout commit took from the context's queue (ReconcilerContext.TakeRefAttaches).
    internal readonly record struct PendingRefAttach(VisualElement Element, ComponentFiber? Owner);

    // One step of a layout commit's setup pass: the fiber at FiberIndex of the commit's ordered batch, or a ref
    // setup.
    internal readonly struct LayoutStep
    {
        internal LayoutStep(int fiberIndex)
        {
            FiberIndex = fiberIndex;
            Ref = default;
            IsRef = false;
        }

        internal LayoutStep(PendingRefAttach reference)
        {
            FiberIndex = -1;
            Ref = reference;
            IsRef = true;
        }

        internal int FiberIndex { get; }

        internal PendingRefAttach Ref { get; }

        internal bool IsRef { get; }
    }

    // Places a layout commit's ref setups among its fibers' setups in the order of React's layout phase: one
    // post-order walk of the tree, in which an element attaches its ref after everything inside it and a component
    // runs its layout effects after the elements it rendered. The fibers keep the order the batch gave them
    // (FiberEffects.TakeBatch); a setup is placed against them by where its element sits.
    //
    // A setup belongs to the component that rendered its element. It follows the fibers below that component
    // which lie before the element and precedes the rest, together with the component itself and everything after
    // it in the batch. Where the element sits among the owner's child fibers is read off the elements, in
    // LogicalElementTree's tree. A setup whose owner is not in the batch follows every fiber of it.
    internal static class FiberRefPlacement
    {
        internal static List<LayoutStep> Interleave(
            ReconcilerContext ctx, List<(ComponentFiber Fiber, bool IsMount)> ordered, List<PendingRefAttach> refs)
        {
            var tree = new LogicalElementTree(ctx);
            var placer = new Placer(ordered, tree);
            var gaps = new Dictionary<int, List<PendingRefAttach>>();
            foreach (var entry in refs)
            {
                var slot = placer.SlotOf(entry);
                if (!gaps.TryGetValue(slot, out var gap))
                {
                    gap = new List<PendingRefAttach>();
                    gaps[slot] = gap;
                }
                gap.Add(entry);
            }
            var steps = new List<LayoutStep>(ordered.Count + refs.Count);
            for (var slot = 0; slot <= ordered.Count; slot++)
            {
                if (gaps.TryGetValue(slot, out var gap))
                {
                    SortByTreeOrder(gap, tree);
                    foreach (var entry in gap) steps.Add(new LayoutStep(entry));
                }
                if (slot < ordered.Count) steps.Add(new LayoutStep(slot));
            }
            return steps;
        }

        // The queue lists elements as the walk reached them, which is the order wanted except for what the walk
        // reaches late: a Portal's rows mount after the pass that rendered the Portal. An element moves ahead of
        // its predecessor only where the tree says it comes first.
        private static void SortByTreeOrder(List<PendingRefAttach> gap, LogicalElementTree tree)
        {
            for (var i = 1; i < gap.Count; i++)
            {
                var entry = gap[i];
                var at = i;
                while (at > 0 && tree.Precedes(entry.Element, gap[at - 1].Element))
                {
                    gap[at] = gap[at - 1];
                    at--;
                }
                gap[at] = entry;
            }
        }

        private sealed class Placer
        {
            private readonly List<(ComponentFiber Fiber, bool IsMount)> _ordered;
            private readonly LogicalElementTree _tree;
            private readonly Dictionary<ComponentFiber, int> _indexOf = new();

            internal Placer(List<(ComponentFiber Fiber, bool IsMount)> ordered, LogicalElementTree tree)
            {
                _ordered = ordered;
                _tree = tree;
                for (var i = 0; i < ordered.Count; i++) _indexOf[ordered[i].Fiber] = i;
            }

            // The index of the first fiber of the batch the setup precedes, which is the owner's own unless the
            // owner's descendants that lie after the element sit right behind it: the batch is a post-order walk,
            // so they end where the owner begins.
            internal int SlotOf(PendingRefAttach entry)
            {
                var owner = entry.Owner;
                if (owner == null) return _ordered.Count;
                if (!_indexOf.TryGetValue(owner, out var slot)) return _ordered.Count;
                var leading = LeadingChildren(owner, entry.Element);
                while (slot > 0 && BranchUnder(owner, _ordered[slot - 1].Fiber) >= leading) slot--;
                return slot;
            }

            // How many of the owner's child fibers lie before the element. They are in document order, so those
            // that do are a prefix of them.
            private int LeadingChildren(ComponentFiber owner, VisualElement element)
            {
                var children = ChildrenOf(owner);
                var low = 0;
                var high = children.Count;
                while (low < high)
                {
                    var middle = (low + high) >> 1;
                    if (LiesBefore(children[middle], element)) low = middle + 1;
                    else high = middle;
                }
                return low;
            }

            // The index, among the owner's children, of the child that holds the fiber; -1 for a fiber not below the
            // owner.
            private static int BranchUnder(ComponentFiber owner, ComponentFiber fiber)
            {
                for (var f = fiber; f.Parent != null; f = f.Parent)
                {
                    if (ReferenceEquals(f.Parent, owner)) return ChildrenOf(owner).IndexOf(f);
                }
                return -1;
            }

            private static List<ComponentFiber> ChildrenOf(ComponentFiber fiber)
            {
                var children = new List<ComponentFiber>();
                for (var child = fiber.Child; child != null; child = child.Sibling) children.Add(child);
                return children;
            }

            // Whether everything the child fiber rendered completes before the element does: the element holds it,
            // or it comes earlier in the tree. A fiber that holds no place in the tree reads as not before.
            private bool LiesBefore(ComponentFiber child, VisualElement element)
            {
                var mount = child.MountPoint;
                if (mount == null) return false;
                var portal = _tree.PortalOf(child);
                if (portal != null) return _tree.Precedes(portal, element);
                if (!child.IsInlineMounted) return _tree.Precedes(mount, element);
                if (child.MountSlotCount < 0) return false;
                return _tree.RowsBefore(mount, child.MountSlotStart + child.MountSlotCount, element);
            }
        }
    }

    // Reads the order of elements in the tree the components describe rather than the one the panel holds: a
    // Portal's rows are children of its placeholder, and a z-* element is the placeholder left at its slot. The
    // maps are built once for a commit, and the tree does not change while the commit places setups.
    internal sealed class LogicalElementTree
    {
        private readonly ReconcilerContext _ctx;
        private readonly Dictionary<VisualElement, VisualElement> _standInOf = new();
        private readonly Dictionary<VisualElement, List<(VisualElement Placeholder, int Start, int Length)>> _portalsOf
            = new();

        internal LogicalElementTree(ReconcilerContext ctx)
        {
            _ctx = ctx;
            foreach (var pair in ctx.ZLayerPlaceholders) _standInOf[pair.Value] = pair.Key;
            foreach (var pair in ctx.PortalState)
            {
                var target = pair.Value.Target;
                if (target == null) continue;
                if (!_portalsOf.TryGetValue(target, out var ranges))
                {
                    ranges = new List<(VisualElement Placeholder, int Start, int Length)>();
                    _portalsOf[target] = ranges;
                }
                ranges.Add((pair.Key, pair.Value.SlotStart, pair.Value.SlotLength));
            }
        }

        // The Portal placeholder whose rows the fiber is the top level of, or null for a fiber that is not.
        internal VisualElement? PortalOf(ComponentFiber fiber)
        {
            var placeholder = fiber.OwningPortalPlaceholder;
            if (placeholder == null) return null;
            if (!_ctx.PortalState.TryGetValue(placeholder, out var info)) return null;
            return ReferenceEquals(info.Target, fiber.MountPoint) ? placeholder : null;
        }

        // Whether the rows of parent that end at the logical slot end all lie before element: the element is parent
        // or holds it, or sits in a row at or past end, or comes after parent in the tree.
        internal bool RowsBefore(VisualElement parent, int end, VisualElement element)
        {
            var chain = ChainOf(element);
            var at = chain.IndexOf(parent);
            if (at > 0) return end <= LogicalSlotOf(chain[at - 1]);
            if (at == 0) return true;
            return Precedes(parent, element);
        }

        // Whether the walk completes a before b: a is inside b, or comes earlier in the tree.
        internal bool Precedes(VisualElement a, VisualElement b)
        {
            var chainA = ChainOf(a);
            var chainB = ChainOf(b);
            var ia = chainA.Count - 1;
            var ib = chainB.Count - 1;
            while (ia > 0 && ib > 0 && ReferenceEquals(chainA[ia - 1], chainB[ib - 1]))
            {
                ia--;
                ib--;
            }
            if (ia == 0) return false;
            if (ib == 0) return true;
            return SiblingPrecedes(chainA[ia - 1], chainB[ib - 1]);
        }

        // Whether a comes first among the children of its parent, which is b's as well: a and b are the two
        // branches under their lowest common ancestor, and a branch is a physical child of it, or a row of the one
        // Portal target it stands for.
        private static bool SiblingPrecedes(VisualElement a, VisualElement b)
            => ReferenceEquals(
                a.hierarchy.parent.Children().First(child => ReferenceEquals(child, a) || ReferenceEquals(child, b)), a);

        // The element and each parent it has in this tree, up to the root.
        private List<VisualElement> ChainOf(VisualElement element)
        {
            var chain = new List<VisualElement>();
            for (var e = element; e != null; e = ParentOf(e)) chain.Add(e);
            return chain;
        }

        private VisualElement? ParentOf(VisualElement element)
        {
            var physical = element.hierarchy.parent;
            if (physical == null) return null;
            if (_standInOf.TryGetValue(element, out var standIn)) return standIn;
            if (!_portalsOf.TryGetValue(physical, out var ranges)) return physical;
            var slot = LogicalSlotOf(element);
            foreach (var range in ranges)
            {
                if (slot >= range.Start && slot < range.Start + range.Length) return range.Placeholder;
            }
            return physical;
        }

        private static int LogicalSlotOf(VisualElement element)
            => LogicalChildSlots.ToLogical(element.hierarchy.parent, element.hierarchy.parent.IndexOf(element));
    }
}
