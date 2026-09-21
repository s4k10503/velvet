using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // Dependency-array cache for MemoNode.
    // Skips the Factory invocation and returns the cached VNode when the dependency array is unchanged, or
    // when the same node instance is expanded again.
    internal sealed class FiberMemoCache
    {
        private sealed class Entry
        {
            internal MemoPosition Position;
            internal VisualElement? Container;
            internal object?[]? Deps;
            // The last two nodes handed in, on a hit as on a recompute. A pass hands in the node the committed tree
            // carries on its old side before its new side hands in the next, so that node is one of the two
            // whether or not the pass commits. PeekInner tells the entries one position holds apart by it.
            internal MemoNode Node = null!;
            internal MemoNode? PreviousNode;
            internal VNode Cached = null!;
        }

        private readonly Dictionary<(MemoPosition Position, VisualElement? Container), Entry> _cache = new();

        // FiberContextSpine looks a committed Memo up here rather than by the key above: its descent restarts at
        // every host container without naming one, so it cannot supply the Container member. A position holds
        // an entry for each container its component writes a memo into there.
        private readonly Dictionary<MemoPosition, List<Entry>> _entriesByPosition = new();

        // An entry leaves when Velvet tears down any fiber or element its key names: one naming a pooled
        // element would otherwise answer for that element's next holder, and one naming a discarded element
        // would keep it.
        private readonly Dictionary<object, HashSet<Entry>> _entriesByReferent = new();

        public (VNode result, VNode? previousCached) GetOrCompute(
            MemoPosition position, VisualElement? container, MemoNode memo)
        {
            VNode? previousCached = null;
            if (_cache.TryGetValue((position, container), out var entry))
            {
                // Two hit conditions, because a null dependency array is "no dependency array" rather than "an
                // unchanged one": AreEqualDeps answers true for two nulls, which would cache such a node as an
                // empty array does. Once the deps arm stops answering for it, identity is what keeps the old-side
                // expansion off the Factory — GeneralPathReconciler.ExpandMemoInline reaches this method once
                // for the old tree and once for the new, and the old side carries the node the entry was last
                // handed wherever the render that handed it committed. Drop the identity arm and the factory-call
                // count in ReconcilerMemoTests' omitted-versus-empty case goes up by one per reconcile.
                if (ReferenceEquals(entry.Node, memo)
                    || (memo.Dependencies != null && ObjectIs.AreEqualDeps(entry.Deps, memo.Dependencies)))
                {
                    HandIn(entry, memo);
                    return (entry.Cached, null);
                }
                previousCached = entry.Cached;
            }

            var result = memo.Factory();
            if (result == null)
            {
                throw new InvalidOperationException("MemoNode.Factory returned null.");
            }
            if (entry == null)
            {
                entry = new Entry { Position = position, Container = container };
                _cache[(position, container)] = entry;
                FileUnderPosition(entry);
                FileUnderReferent(position.Owner, entry);
                FileUnderReferent(position.PortalScope, entry);
                FileUnderReferent(container, entry);
            }
            HandIn(entry, memo);
            entry.Deps = memo.Dependencies;
            entry.Cached = result;
            return (result, previousCached);
        }

        internal void MarkCachedTrees(HashSet<object> live)
        {
            foreach (var entry in _cache.Values)
                FiberTreeReturn.MarkNode(entry.Cached, live);
        }

        private static void HandIn(Entry entry, MemoNode memo)
        {
            entry.PreviousNode = entry.Node;
            entry.Node = memo;
        }

        // Never invokes the Factory: a recompute from FiberContextSpine would run the user Factory and mutate
        // the cache outside a reconcile. The candidates are the entries at position that name node, or every
        // entry there where none does: a Portal's children are walked as they were when it mounted them, which
        // the nodes handed in since have overtaken. Null past the last candidate.
        public VNode? PeekInner(MemoPosition position, MemoNode node, int candidate)
        {
            if (!_entriesByPosition.TryGetValue(position, out var entries)) return null;
            var anyNamesNode = false;
            for (var i = 0; i < entries.Count; i++)
            {
                if (Names(entries[i], node))
                {
                    anyNamesNode = true;
                    break;
                }
            }
            for (var i = 0; i < entries.Count; i++)
            {
                if (anyNamesNode && !Names(entries[i], node)) continue;
                if (candidate-- == 0) return entries[i].Cached;
            }
            return null;
        }

        private static bool Names(Entry entry, MemoNode node)
            => ReferenceEquals(entry.Node, node) || ReferenceEquals(entry.PreviousNode, node);

        public void Forget(ComponentFiber owner) => ForgetReferent(owner);

        public void Forget(VisualElement element) => ForgetReferent(element);

        private void ForgetReferent(object referent)
        {
            if (!_entriesByReferent.Remove(referent, out var entries)) return;
            foreach (var entry in entries)
            {
                _cache.Remove((entry.Position, entry.Container));
                UnfileFromPosition(entry);
                UnfileFromReferent(entry.Position.Owner, entry);
                UnfileFromReferent(entry.Position.PortalScope, entry);
                UnfileFromReferent(entry.Container, entry);
                // Released rather than returned: the teardown that lets the entry go can still hold the cached
                // subtree's elements in the hierarchy when it does, and a returned part is the pool's to hand
                // out again.
                FiberTreeReturn.Release(FiberTreeReturn.NormalizeToArray(entry.Cached));
            }
        }

        private void FileUnderPosition(Entry entry)
        {
            if (!_entriesByPosition.TryGetValue(entry.Position, out var entries))
            {
                entries = new List<Entry>();
                _entriesByPosition[entry.Position] = entries;
            }
            entries.Add(entry);
        }

        private void UnfileFromPosition(Entry entry)
        {
            var entries = _entriesByPosition[entry.Position];
            entries.Remove(entry);
            if (entries.Count == 0) _entriesByPosition.Remove(entry.Position);
        }

        private void FileUnderReferent(object? referent, Entry entry)
        {
            if (referent == null) return;
            if (!_entriesByReferent.TryGetValue(referent, out var entries))
            {
                entries = new HashSet<Entry>();
                _entriesByReferent[referent] = entries;
            }
            entries.Add(entry);
        }

        // The referent ForgetReferent is dropping has already left the index, so it is not found here.
        private void UnfileFromReferent(object? referent, Entry entry)
        {
            if (referent == null) return;
            if (!_entriesByReferent.TryGetValue(referent, out var entries)) return;
            entries.Remove(entry);
            if (entries.Count == 0) _entriesByReferent.Remove(referent);
        }

        // Called at reconciler disposal: the whole mounted tree is torn down with it, so there is no
        // committed successor to spare (a cached inner shared with an already-retired committed tree is
        // a harmless overlap — pool returns are idempotent).
        public void DisposeAndReturnCachedTrees()
        {
            foreach (var entry in _cache.Values)
            {
                FiberTreeReturn.ReturnRetiredTree(FiberTreeReturn.NormalizeToArray(entry.Cached), owner: null);
            }
            _cache.Clear();
            _entriesByPosition.Clear();
            _entriesByReferent.Clear();
        }
    }
}
