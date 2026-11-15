#nullable enable
using System.Collections.Generic;
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a physical child of a container is to slot counting. Mirrors LogicalChildSlots.IsInvisible and StaysFirst:
    // a child is Rendered when it holds a logical slot, LeadingInvisible when it holds none and an append steps over
    // it, and Invisible when it holds none and an append stops before it.
    internal enum RowKind : byte
    {
        Rendered,
        LeadingInvisible,
        Invisible,
    }

    // The physical children of one container in order, each with its RowKind, so that the counts and conversions
    // LogicalChildSlots answers by scanning the child list are answered from per-block counts instead. A block holds
    // up to BlockMax children; a container that fits one block has no element table, and a larger one keeps an
    // element-to-block table, so a lookup by element scans one block at most. ChildListWrites owns every instance and
    // keeps it equal to the live child list.
    internal sealed class ContainerRows
    {
        internal const int BlockMax = 128;

        private sealed class Block
        {
            internal VisualElement[] Elements;
            internal RowKind[] Kinds;
            internal int Length;
            internal int Rendered;
            internal int Anchors;

            internal Block(int capacity)
            {
                Elements = new VisualElement[capacity];
                Kinds = new RowKind[capacity];
            }

            internal int IndexOf(VisualElement element)
            {
                for (var i = 0; i < Length; i++)
                {
                    if (ReferenceEquals(Elements[i], element)) return i;
                }
                return -1;
            }

            internal void Insert(int at, VisualElement element, RowKind kind)
            {
                if (Length == Elements.Length)
                {
                    var capacity = Math.Min(Math.Max(Length * 2, 4), BlockMax + 1);
                    Array.Resize(ref Elements, capacity);
                    Array.Resize(ref Kinds, capacity);
                }
                if (at < Length)
                {
                    Array.Copy(Elements, at, Elements, at + 1, Length - at);
                    Array.Copy(Kinds, at, Kinds, at + 1, Length - at);
                }
                Elements[at] = element;
                Kinds[at] = kind;
                Length++;
                Tally(kind, 1);
            }

            internal void RemoveAt(int at)
            {
                Tally(Kinds[at], -1);
                Length--;
                if (at < Length)
                {
                    Array.Copy(Elements, at + 1, Elements, at, Length - at);
                    Array.Copy(Kinds, at + 1, Kinds, at, Length - at);
                }
                Elements[Length] = null!;
            }

            internal void Empty()
            {
                Array.Clear(Elements, 0, Length);
                Length = 0;
                Rendered = 0;
                Anchors = 0;
            }

            internal void Tally(RowKind kind, int delta)
            {
                if (kind == RowKind.Rendered) Rendered += delta;
                if (kind != RowKind.Invisible) Anchors += delta;
            }
        }

        private readonly List<Block> _blocks = new();
        private Dictionary<VisualElement, Block>? _owner;
        private int _total;
        private int _rendered;

        internal int TotalCount => _total;

        // The exclusive bound of the slot range: what LogicalChildSlots.Count returns.
        internal int RenderedCount => _rendered;

        internal int SkippedCount => _total - _rendered;

        internal VisualElement ElementAt(int physical)
        {
            var block = BlockAtPhysical(physical, out var inBlock);
            return block.Elements[inBlock];
        }

        internal RowKind KindAt(int physical)
        {
            var block = BlockAtPhysical(physical, out var inBlock);
            return block.Kinds[inBlock];
        }

        internal bool Contains(VisualElement element) => TryFind(element, out _, out _);

        // Rendered for an element the model does not hold.
        internal RowKind KindOf(VisualElement element)
            => TryFind(element, out var block, out var at) ? block!.Kinds[at] : RowKind.Rendered;

        // LogicalChildSlots.ToPhysical, over the model.
        internal int ToPhysical(int logical)
        {
            if (logical < 0) logical = 0;
            var start = 0;
            for (var b = 0; b < _blocks.Count; b++)
            {
                var block = _blocks[b];
                if (logical < block.Rendered)
                {
                    for (var i = 0; i < block.Length; i++)
                    {
                        if (block.Kinds[i] != RowKind.Rendered) continue;
                        if (logical-- == 0) return start + i;
                    }
                }
                logical -= block.Rendered;
                start += block.Length;
            }
            // Past the last rendered child: just after the last child an append steps over.
            for (var b = _blocks.Count - 1; b >= 0; b--)
            {
                var block = _blocks[b];
                start -= block.Length;
                if (block.Anchors == 0) continue;
                for (var i = block.Length - 1; i >= 0; i--)
                {
                    if (block.Kinds[i] != RowKind.Invisible) return start + i + 1;
                }
            }
            return 0;
        }

        // LogicalChildSlots.TryGetPhysical, over the model.
        internal bool TryGetPhysical(int logical, out int physical)
        {
            physical = ToPhysical(logical);
            return logical >= 0 && physical < _total && KindAt(physical) == RowKind.Rendered;
        }

        // LogicalChildSlots.ToLogical, over the model.
        internal int ToLogical(int physical)
        {
            if (physical <= 0) return 0;
            var limit = Math.Min(physical, _total);
            var rendered = 0;
            for (var b = 0; b < _blocks.Count && limit > 0; b++)
            {
                var block = _blocks[b];
                if (limit >= block.Length)
                {
                    rendered += block.Rendered;
                    limit -= block.Length;
                    continue;
                }
                for (var i = 0; i < limit; i++)
                {
                    if (block.Kinds[i] == RowKind.Rendered) rendered++;
                }
                break;
            }
            return rendered;
        }

        internal int PhysicalIndexOf(VisualElement element)
        {
            if (!TryFind(element, out var block, out var at)) return -1;
            var start = 0;
            for (var b = 0; b < _blocks.Count && !ReferenceEquals(_blocks[b], block); b++) start += _blocks[b].Length;
            return start + at;
        }

        // The slot a Rendered element holds, or -1.
        internal int SlotOf(VisualElement element)
        {
            if (!TryFind(element, out var block, out var at) || block!.Kinds[at] != RowKind.Rendered) return -1;
            var rendered = 0;
            for (var b = 0; b < _blocks.Count && !ReferenceEquals(_blocks[b], block); b++) rendered += _blocks[b].Rendered;
            for (var i = 0; i < at; i++)
            {
                if (block.Kinds[i] == RowKind.Rendered) rendered++;
            }
            return rendered;
        }

        internal void Insert(int physical, VisualElement element, RowKind kind)
        {
            Block block;
            int at;
            if (_blocks.Count == 0)
            {
                block = new Block(4);
                _blocks.Add(block);
                at = 0;
            }
            else if (physical >= _total)
            {
                block = _blocks[^1];
                at = block.Length;
            }
            else
            {
                block = BlockAtPhysical(Math.Max(physical, 0), out at);
            }
            block.Insert(at, element, kind);
            _total++;
            if (kind == RowKind.Rendered) _rendered++;
            _owner?.Add(element, block);
            if (block.Length > BlockMax) Split(block);
        }

        internal void Remove(VisualElement element)
        {
            if (!TryFind(element, out var block, out var at)) return;
            if (block!.Kinds[at] == RowKind.Rendered) _rendered--;
            block.RemoveAt(at);
            _total--;
            _owner?.Remove(element);
            if (block.Length == 0 && _blocks.Count > 1) _blocks.Remove(block);
            if (_blocks.Count == 1) _owner = null;
        }

        internal void Reclassify(VisualElement element, RowKind kind)
        {
            if (!TryFind(element, out var block, out var at)) return;
            var old = block!.Kinds[at];
            if (old == kind) return;
            if (old == RowKind.Rendered) _rendered--;
            if (kind == RowKind.Rendered) _rendered++;
            block.Tally(old, -1);
            block.Tally(kind, 1);
            block.Kinds[at] = kind;
        }

        // Keeps the first block's arrays, so a model rebuilt from the live list (ChildListWrites.RowsOf) allocates
        // nothing for a container no larger than it was.
        internal void Clear()
        {
            if (_blocks.Count > 1) _blocks.RemoveRange(1, _blocks.Count - 1);
            if (_blocks.Count == 1) _blocks[0].Empty();
            _owner = null;
            _total = 0;
            _rendered = 0;
        }

        private void Split(Block block)
        {
            var index = _blocks.IndexOf(block);
            var half = block.Length / 2;
            var tail = new Block(BlockMax + 1);
            for (var i = half; i < block.Length; i++) tail.Insert(tail.Length, block.Elements[i], block.Kinds[i]);
            for (var i = block.Length - 1; i >= half; i--) block.RemoveAt(i);
            _blocks.Insert(index + 1, tail);
            if (_owner == null)
            {
                _owner = new Dictionary<VisualElement, Block>(_total * 2);
                foreach (var each in _blocks)
                {
                    for (var i = 0; i < each.Length; i++) _owner[each.Elements[i]] = each;
                }
                return;
            }
            for (var i = 0; i < tail.Length; i++) _owner[tail.Elements[i]] = tail;
        }

        private Block BlockAtPhysical(int physical, out int inBlock)
        {
            for (var b = 0; b < _blocks.Count; b++)
            {
                var block = _blocks[b];
                if (physical < block.Length)
                {
                    inBlock = physical;
                    return block;
                }
                physical -= block.Length;
            }
            throw new ArgumentOutOfRangeException(nameof(physical));
        }

        private bool TryFind(VisualElement element, out Block? block, out int at)
        {
            block = null;
            at = -1;
            if (_blocks.Count == 0) return false;
            if (_owner == null)
            {
                block = _blocks[0];
            }
            else if (!_owner.TryGetValue(element, out block))
            {
                return false;
            }
            at = block.IndexOf(element);
            return at >= 0;
        }
    }
}
