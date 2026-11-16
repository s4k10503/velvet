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
    // up to BlockMax children. A container that fits one block keeps it in a field and has no element table; a larger
    // one keeps a list of blocks and an element-to-block table, so a lookup by element scans one block at most.
    // ChildListWrites updates every instance beside each write it makes and compares it with the live list.
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
                Elements = capacity == 0 ? Array.Empty<VisualElement>() : new VisualElement[capacity];
                Kinds = capacity == 0 ? Array.Empty<RowKind>() : new RowKind[capacity];
            }

            internal int IndexOf(VisualElement element)
            {
                for (var i = 0; i < Length; i++)
                {
                    if (ReferenceEquals(Elements[i], element)) return i;
                }
                return -1;
            }

            internal void Reserve(int capacity)
            {
                // MUTANT_SURVIVES(equivalent, boundary): at equal length Array.Resize leaves both arrays as they are.
                if (Elements.Length >= capacity) return;
                Array.Resize(ref Elements, capacity);
                Array.Resize(ref Kinds, capacity);
            }

            internal void Insert(int at, VisualElement element, RowKind kind)
            {
                if (Length == Elements.Length) Reserve(Math.Min(Math.Max(Length * 2, 4), BlockMax + 1));
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

        private Block _first = new(0);
        private List<Block>? _blocks;
        private Dictionary<VisualElement, Block>? _owner;
        private int _total;
        private int _rendered;

        // Set when a change to the live list is announced that no ChildListWrites op made; the next sync rebuilds.
        internal bool Stale;

        private int BlockCount => _blocks?.Count ?? 1;

        private Block BlockAt(int index) => _blocks == null ? _first : _blocks[index];

        internal int TotalCount => _total;

        // The exclusive bound of the slot range: what LogicalChildSlots.Count returns.
        internal int RenderedCount => _rendered;

        internal int SkippedCount => _total - _rendered;

        internal VisualElement? FirstElement => _total == 0 ? null : BlockAt(0).Elements[0];

        internal VisualElement? LastElement
        {
            get
            {
                if (_total == 0) return null;
                var block = BlockAt(BlockCount - 1);
                return block.Elements[block.Length - 1];
            }
        }

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

        // LogicalChildSlots.ToPhysical, over the model.
        internal int ToPhysical(int logical)
        {
            // MUTANT_SURVIVES(equivalent, boundary): at zero the assignment writes the zero it already holds.
            if (logical < 0) logical = 0;
            var start = 0;
            for (var b = 0; b < BlockCount; b++)
            {
                var block = BlockAt(b);
                if (logical < block.Rendered)
                {
                    // MUTANT_SURVIVES(equivalent, boundary): the loop returns at the logical-th Rendered kind, which exists since logical < block.Rendered, before i reaches block.Length.
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
            for (var b = BlockCount - 1; b >= 0; b--)
            {
                var block = BlockAt(b);
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

        // Appends the rendered children at slots [slotStart, end) to into, fewer where the container holds fewer.
        internal void Rows(int slotStart, int end, List<VisualElement?> into)
        {
            // MUTANT_SURVIVES(equivalent, line removed): the copy below stops where the rows run out, and a slotStart at or past _rendered leaves the skip loop with no block, so the clamp changes no row appended.
            end = Math.Min(end, _rendered);
            // MUTANT_SURVIVES(equivalent, boundary): at slotStart == end want is 0 and the copy loops append nothing.
            if (slotStart >= end) return;
            var skip = slotStart;
            var b = 0;
            // MUTANT_SURVIVES(unreachable, boundary): b reaches BlockCount only if skip covers the Rendered rows of every block, which slotStart < end <= _rendered rules out.
            while (b < BlockCount && skip >= BlockAt(b).Rendered)
            {
                skip -= BlockAt(b).Rendered;
                b++;
            }
            var want = end - slotStart;
            for (; b < BlockCount && want > 0; b++)
            {
                var block = BlockAt(b);
                for (var i = 0; i < block.Length && want > 0; i++)
                {
                    if (block.Kinds[i] != RowKind.Rendered) continue;
                    if (skip > 0)
                    {
                        skip--;
                        continue;
                    }
                    into.Add(block.Elements[i]);
                    want--;
                }
            }
        }

        // The rendered child holding the slot, or null where none does.
        internal VisualElement? RowAt(int logical)
            => TryGetPhysical(logical, out var physical) ? ElementAt(physical) : null;

        // Appends the children at physical indices [start, end) to into, fewer where the container holds fewer.
        internal void PhysicalRange(int start, int end, List<VisualElement> into)
        {
            start = Math.Max(start, 0);
            end = Math.Min(end, _total);
            if (start >= end) return;
            var block = BlockAtPhysical(start, out var at);
            var b = 0;
            while (!ReferenceEquals(BlockAt(b), block)) b++;
            for (var want = end - start; want > 0; b++, at = 0)
            {
                block = BlockAt(b);
                for (; at < block.Length && want > 0; at++, want--) into.Add(block.Elements[at]);
            }
        }

        // LogicalChildSlots.ToLogical, over the model.
        internal int ToLogical(int physical)
        {
            if (physical <= 0) return 0;
            var limit = Math.Min(physical, _total);
            var rendered = 0;
            for (var b = 0; b < BlockCount && limit > 0; b++)
            {
                var block = BlockAt(b);
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
            for (var b = 0; b < BlockCount && !ReferenceEquals(BlockAt(b), block); b++) start += BlockAt(b).Length;
            return start + at;
        }

        // The slot a Rendered element holds, or -1.
        private int SlotOf(VisualElement element)
        {
            if (!TryFind(element, out var block, out var at) || block!.Kinds[at] != RowKind.Rendered) return -1;
            var rendered = 0;
            // MUTANT_SURVIVES(equivalent, boundary): block was found among the blocks, so the walk stops at its index, below BlockCount.
            for (var b = 0; b < BlockCount && !ReferenceEquals(BlockAt(b), block); b++) rendered += BlockAt(b).Rendered;
            for (var i = 0; i < at; i++)
            {
                if (block.Kinds[i] == RowKind.Rendered) rendered++;
            }
            return rendered;
        }

        // Sizes the first block for count children, so filling a model from a live list grows nothing.
        internal void Reserve(int count)
        {
            if (_blocks == null) _first.Reserve(Math.Min(count, BlockMax + 1));
        }

        internal void Insert(int physical, VisualElement element, RowKind kind)
        {
            Block block;
            int at;
            if (physical >= _total)
            {
                block = BlockAt(BlockCount - 1);
                at = block.Length;
            }
            else
            {
                block = BlockAtPhysical(Math.Max(physical, 0), out at);
            }
            block.Insert(at, element, kind);
            _total++;
            if (kind == RowKind.Rendered) _rendered++;
            if (_owner != null) _owner[element] = block;
            if (block.Length > BlockMax) Split(block);
        }

        internal void Remove(VisualElement element)
        {
            if (!TryFind(element, out var block, out var at)) return;
            if (block!.Kinds[at] == RowKind.Rendered) _rendered--;
            block.RemoveAt(at);
            _total--;
            _owner?.Remove(element);
            if (_blocks == null) return;
            if (block.Length == 0) _blocks.Remove(block);
            if (_blocks.Count == 1)
            {
                _first = _blocks[0];
                _blocks = null;
                _owner = null;
            }
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

        // Keeps the first block's arrays, so a model rebuilt from the live list allocates nothing for a container no
        // larger than it was.
        internal void Clear()
        {
            if (_blocks != null) _first = _blocks[0];
            _first.Empty();
            _blocks = null;
            _owner = null;
            _total = 0;
            _rendered = 0;
            Stale = false;
        }

        private void Split(Block block)
        {
            if (_blocks == null) _blocks = new List<Block> { _first };
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
            for (var b = 0; b < BlockCount; b++)
            {
                var block = BlockAt(b);
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
            if (_total == 0) return false;
            if (_owner == null)
            {
                block = _first;
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
