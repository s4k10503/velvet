using System.Collections.Generic;
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
    // LogicalElementTree's tree. A setup whose owner is not in the batch (a row a range render committed in the
    // middle of the pass) precedes its nearest ancestor that is, and follows every fiber when none is.
    internal static class FiberRefPlacement
    {
        internal static List<LayoutStep> Interleave(
            ReconcilerContext ctx, List<(ComponentFiber Fiber, bool IsMount)> ordered, List<PendingRefAttach> refs)
        {
            var tree = LogicalElementTree.Rent(ctx);
            try
            {
                return Interleave(ordered, refs, tree);
            }
            finally
            {
                tree.Return();
            }
        }

        private static List<LayoutStep> Interleave(
            List<(ComponentFiber Fiber, bool IsMount)> ordered, List<PendingRefAttach> refs, LogicalElementTree tree)
        {
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
                    if (tree.HasPortalRows) SortByTreeOrder(gap, tree);
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
                for (; at > 0 && tree.Precedes(entry.Element, gap[at - 1].Element); at--)
                {
                    gap[at] = gap[at - 1];
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
                if (!_indexOf.TryGetValue(owner, out var slot)) return SlotBeforeNearestAncestor(owner);
                while (slot > 0 && LiesAfter(owner, _ordered[slot - 1].Fiber, entry.Element)) slot--;
                return slot;
            }

            private int SlotBeforeNearestAncestor(ComponentFiber owner)
            {
                for (var ancestor = owner.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (_indexOf.TryGetValue(ancestor, out var slot)) return slot;
                }
                return _ordered.Count;
            }

            // Whether the fiber is below the owner, under a child fiber of it that does not lie before the element.
            // The child fibers that do are a prefix of them, in document order.
            private bool LiesAfter(ComponentFiber owner, ComponentFiber fiber, VisualElement element)
            {
                for (var branch = fiber; branch.Parent != null; branch = branch.Parent)
                {
                    if (ReferenceEquals(branch.Parent, owner)) return !LiesBefore(branch, element);
                }
                return false;
            }

            // Whether everything the child fiber rendered completes before the element does: the element holds it,
            // or it comes earlier in the tree. A fiber that holds no place in the tree reads as not before.
            private bool LiesBefore(ComponentFiber child, VisualElement element)
            {
                var mount = child.MountPoint;
                if (mount == null) return false;
                if (!child.IsInlineMounted) return _tree.Precedes(mount, element);
                if (child.MountSlotCount < 0) return false;
                return _tree.RowsBefore(_tree.RowParentOf(child), child.MountSlotStart + child.MountSlotCount, element);
            }
        }
    }

    // Reads the order of elements in the tree the components describe rather than the one the panel holds: a
    // Portal's rows are children of its placeholder, and a z-* element is the placeholder left at its slot. The
    // maps are built when the tree is filled, and each container's children read once while it is held
    // (RenderedChildrenCache), which its holders do over work that changes no element.
    internal sealed class LogicalElementTree
    {
        // An instance per holder on the stack, so a commit nested in another takes one of its own.
        private static readonly Stack<LogicalElementTree> s_pool = new();

        private ReconcilerContext _ctx = null!;
        private readonly Dictionary<VisualElement, VisualElement> _standInOf = new();
        private readonly Dictionary<VisualElement, List<(VisualElement Placeholder, int Start, int Length)>> _portalsOf
            = new();
        private readonly Stack<List<(VisualElement Placeholder, int Start, int Length)>> _spareRanges = new();
        private readonly List<VisualElement> _chainA = new();
        private readonly List<VisualElement> _chainB = new();
        private readonly RenderedChildrenCache _rendered = new();

        internal LogicalElementTree(ReconcilerContext ctx) => Fill(ctx);

        private LogicalElementTree()
        {
        }

        internal static LogicalElementTree Rent(ReconcilerContext ctx)
        {
            var tree = s_pool.Count > 0 ? s_pool.Pop() : new LogicalElementTree();
            tree.Fill(ctx);
            return tree;
        }

        internal void Return()
        {
            _ctx = null!;
            _standInOf.Clear();
            foreach (var ranges in _portalsOf.Values)
            {
                ranges.Clear();
                _spareRanges.Push(ranges);
            }
            _portalsOf.Clear();
            _chainA.Clear();
            _chainB.Clear();
            _rendered.Clear();
            s_pool.Push(this);
        }

        private void Fill(ReconcilerContext ctx)
        {
            _ctx = ctx;
            foreach (var pair in ctx.ZLayerPlaceholders) _standInOf[pair.Value] = pair.Key;
            foreach (var pair in ctx.PortalState)
            {
                var target = pair.Value.Target;
                if (target == null) continue;
                if (!_portalsOf.TryGetValue(target, out var ranges))
                {
                    ranges = _spareRanges.Count > 0
                        ? _spareRanges.Pop()
                        : new List<(VisualElement Placeholder, int Start, int Length)>();
                    _portalsOf[target] = ranges;
                }
                ranges.Add((pair.Key, pair.Value.SlotStart, pair.Value.SlotLength));
            }
        }

        // Whether a Portal has rows in a target, which mount after the pass that rendered the Portal: the late
        // arrival SortByTreeOrder places.
        internal bool HasPortalRows => _portalsOf.Count != 0;

        // The element an inline fiber's rows are children of in this tree: the Portal placeholder whose range on the
        // target holds them, a fiber nested in a Portal child included, and otherwise its mount point. The rows'
        // slots are the target's either way, which is what RowsBefore reads under a placeholder.
        internal VisualElement RowParentOf(ComponentFiber fiber)
        {
            var mount = fiber.MountPoint!;
            var placeholder = fiber.OwningPortalPlaceholder;
            if (placeholder == null || !_ctx.PortalState.TryGetValue(placeholder, out var info)) return mount;
            return ReferenceEquals(info.Target, mount) ? placeholder : mount;
        }

        internal bool IsAtOrUnder(VisualElement element, VisualElement subtree)
        {
            for (VisualElement? e = element; e != null; e = ParentOf(e))
            {
                if (ReferenceEquals(e, subtree)) return true;
            }
            return false;
        }

        // Whether the rows of parent that end at the logical slot end all lie before element: the element is parent
        // or holds it, or sits in a row at or past end, or comes after parent in the tree.
        internal bool RowsBefore(VisualElement parent, int end, VisualElement element)
        {
            var chain = ChainOf(element, _chainA);
            var at = chain.IndexOf(parent);
            if (at > 0) return end <= LogicalSlotOf(chain[at - 1]);
            if (at == 0) return true;
            return Precedes(parent, element);
        }

        // RenderedChildrenCache.StartOf, each container read once while the tree is held.
        internal VisualElement? StartOf(ComponentFiber fiber, out VisualElement? endOf) => _rendered.StartOf(fiber, out endOf);

        // Whether element, or with atEnd the place behind its last child, comes after everything inside subtree.
        internal bool ComesAfter(VisualElement subtree, VisualElement element, bool atEnd)
        {
            var chainA = ChainOf(subtree, _chainA);
            var chainB = ChainOf(element, _chainB);
            var ia = chainA.Count - 1;
            var ib = chainB.Count - 1;
            while (ia > 0 && ib > 0 && ReferenceEquals(chainA[ia - 1], chainB[ib - 1]))
            {
                ia--;
                ib--;
            }
            if (ia == 0) return false;
            if (ib == 0) return atEnd;
            return SiblingPrecedes(chainA[ia - 1], chainB[ib - 1]);
        }

        // Whether the walk completes a before b: a is inside b, or comes earlier in the tree.
        internal bool Precedes(VisualElement a, VisualElement b)
        {
            var chainA = ChainOf(a, _chainA);
            var chainB = ChainOf(b, _chainB);
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
        private bool SiblingPrecedes(VisualElement a, VisualElement b)
        {
            var parent = a.hierarchy.parent;
            // MUTANT_SURVIVES(equivalent, boundary): a and b are two distinct branches under their lowest common
            // ancestor, where the loop in Precedes stops once the chains differ, so their indices never tie.
            return _rendered.IndexIn(parent, a) < _rendered.IndexIn(parent, b);
        }

        // The element and each parent it has in this tree, up to the root.
        private List<VisualElement> ChainOf(VisualElement element, List<VisualElement> chain)
        {
            chain.Clear();
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

        private int LogicalSlotOf(VisualElement element) => _rendered.SlotIn(element.hierarchy.parent, element);
    }
}
