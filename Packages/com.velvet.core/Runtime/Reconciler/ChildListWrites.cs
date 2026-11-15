#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Every write the runtime assembly makes to a child list goes through here (RowModelRoutingTests), and each op
    // updates the ContainerRows of the container beside the write: the model first and then the UI Toolkit call, so a
    // write nested inside that call (LogicalSlotCursor.Insert names one) finds the child already in the model; only
    // HierarchySort makes its call first and rebuilds after.
    //
    // A model is checked against the live list when an op starts: its count, its first and last child and the child at
    // the index the op reads, plus whether a change was announced that no op made (NoteHierarchyChange). A model that
    // fails is rebuilt from the live list. An op nested in an open op's UI Toolkit call rebuilds nothing, since the
    // outer op's child is in neither list yet; the containers both wrote are compared child for child when the
    // outermost op ends.
    // The probes do not read every child, so VerifyTouched compares whole lists for the containers writes reached.
    internal static class ChildListWrites
    {
        private static readonly ConditionalWeakTable<VisualElement, ContainerRows> s_rows = new();
        private static readonly HashSet<VisualElement> s_touched = new();
        private static readonly HashSet<VisualElement> s_nested = new();
        private static readonly List<VisualElement> s_flight = new();
        private static int s_open;

        internal static RowKind Classify(VisualElement element)
        {
            if (!LogicalChildSlots.IsInvisible(element)) return RowKind.Rendered;
            return LogicalChildSlots.StaysFirst(element) ? RowKind.LeadingInvisible : RowKind.Invisible;
        }

        // The model of a container that has one, as it stands: a read that never builds or checks. Private until a
        // reader moves onto the model; the tests reach it by reflection (RowModelAccess).
        private static ContainerRows? Peek(VisualElement container)
            => s_rows.TryGetValue(container, out var rows) ? rows : null;

        private static ContainerRows Synced(VisualElement container, int readAt)
        {
            if (!s_rows.TryGetValue(container, out var rows))
            {
                rows = new ContainerRows();
                s_rows.Add(container, rows);
                Fill(rows, container);
            }
            else if (s_open == 0 && !Agrees(rows, container, readAt))
            {
                rows.Clear();
                Fill(rows, container);
            }
            return rows;
        }

        // The cheap probes: stale mark, count, first and last child, and the child at readAt.
        private static bool Agrees(ContainerRows rows, VisualElement container, int readAt)
        {
            var count = container.hierarchy.childCount;
            if (rows.Stale || rows.TotalCount != count) return false;
            if (count == 0) return true;
            if (!ReferenceEquals(rows.FirstElement, container.hierarchy[0])
                || !ReferenceEquals(rows.LastElement, container.hierarchy[count - 1]))
            {
                return false;
            }
            return readAt < 0 || readAt >= count || ReferenceEquals(rows.ElementAt(readAt), container.hierarchy[readAt]);
        }

        private static void Fill(ContainerRows rows, VisualElement container)
        {
            var count = container.hierarchy.childCount;
            rows.Reserve(count);
            for (var i = 0; i < count; i++)
            {
                var child = container.hierarchy[i];
                rows.Insert(i, child, Classify(child));
            }
        }

        // An announced change to a hierarchy (RelationalTreeWatch hears them from the panel) that none of these ops is
        // making marks the models of the element and its parent stale. One made inside an op's UI Toolkit call is that
        // op's own, except for a container the op is not writing (NoteDuringOp).
        internal static void NoteHierarchyChange(VisualElement element)
        {
            if (s_open > 0)
            {
                NoteDuringOp(element);
                NoteDuringOp(element.hierarchy.parent);
                return;
            }
            if (s_rows.TryGetValue(element, out var own)) own.Stale = true;
            var parent = element.hierarchy.parent;
            if (parent != null && s_rows.TryGetValue(parent, out var rows)) rows.Stale = true;
        }

        // A change announced inside an op's UI Toolkit call to a container the op is not writing is UI Toolkit's own
        // reaction to the op (a composite field moving its input between containers). The op ends with that container
        // compared whole; the op's own containers are settled anyway.
        private static void NoteDuringOp(VisualElement? container)
        {
            if (container != null && s_rows.TryGetValue(container, out _) && !s_flight.Contains(container))
            {
                s_nested.Add(container);
            }
        }

        // The element UI Toolkit's own child-list calls on parent write into: parent itself, or the content container
        // it forwards to.
        private static VisualElement ContentOf(VisualElement parent)
        {
            var content = parent;
            for (var hops = 0; hops < 8 && content.contentContainer is { } next && !ReferenceEquals(next, content); hops++)
            {
                content = next;
            }
            return content;
        }

        private static ContainerRows Enter(VisualElement container, int readAt)
        {
            if (s_open > 0) s_nested.Add(container);
            var rows = Synced(container, readAt);
            s_open++;
            s_flight.Add(container);
            Touch(container);
            return rows;
        }

        private static void Exit(VisualElement container)
        {
            s_flight.RemoveAt(s_flight.Count - 1);
            if (--s_open > 0) return;
            var nestedRan = s_nested.Count > 0;
            Settle(container, nestedRan);
            foreach (var nested in s_nested) Settle(nested, full: true);
            s_nested.Clear();
        }

        // After the outermost op: a model that no longer matches the live list is rebuilt. Where a nested op ran, the
        // lists are compared child for child, since two writes that each moved a child can leave the probes agreeing.
        private static void Settle(VisualElement container, bool full)
        {
            if (!s_rows.TryGetValue(container, out var rows)) return;
            if (full ? Compare(rows, container) == null : Agrees(rows, container, -1)) return;
            rows.Clear();
            Fill(rows, container);
        }

        // For a role or class change that writes no child list, which flips what Classify answers for a child that
        // stays where it is: FiberZLayerCoordinator's Hold and Release, and RingOverlay's mode switch.
        internal static void Reclassify(VisualElement element)
        {
            var parent = element.hierarchy.parent;
            if (parent != null && s_rows.TryGetValue(parent, out var rows) && rows.Contains(element))
            {
                rows.Reclassify(element, Classify(element));
            }
        }

        // The model goes with the element: a container returned to a pool or released keeps no children's rows.
        internal static void Drop(VisualElement container) => s_rows.Remove(container);

        // Every model under root goes: for a control that rebuilds its own containers in a way that writes no child
        // list through here (a TextField changing multiline), where the models would otherwise outlive the structure.
        internal static void DropSubtree(VisualElement root)
        {
            Drop(root);
            foreach (var child in root.hierarchy.Children()) DropSubtree(child);
        }

        internal static void Insert(VisualElement parent, int index, VisualElement child)
            => InsertCore(ContentOf(parent), index, child, viaHierarchy: false, parent);

        internal static void Add(VisualElement parent, VisualElement child)
        {
            var container = ContentOf(parent);
            InsertCore(container, container.hierarchy.childCount, child, viaHierarchy: false, parent);
        }

        internal static void HierarchyInsert(VisualElement parent, int index, VisualElement child)
            => InsertCore(parent, index, child, viaHierarchy: true, parent);

        internal static void HierarchyAdd(VisualElement parent, VisualElement child)
            => InsertCore(parent, parent.hierarchy.childCount, child, viaHierarchy: true, parent);

        // UI Toolkit refuses an index past the child count as it stood before it detaches the child, then detaches it
        // and puts it at the index, clamped to the list it has left. The model takes the same two steps.
        private static void InsertCore(VisualElement container, int index, VisualElement child, bool viaHierarchy,
            VisualElement caller)
        {
            var left = child.hierarchy.parent;
            var rows = Enter(container, Math.Min(index, container.hierarchy.childCount - 1));
            try
            {
                if (index >= 0 && index <= container.hierarchy.childCount)
                {
                    if (left != null && s_rows.TryGetValue(left, out var leftRows))
                    {
                        leftRows.Remove(child);
                        Touch(left);
                    }
                    rows.Insert(Math.Min(index, rows.TotalCount), child, Classify(child));
                }
                if (viaHierarchy) container.hierarchy.Insert(index, child);
                else caller.Insert(index, child);
            }
            finally
            {
                Exit(container);
            }
            if (left != null && !ReferenceEquals(left, container)) PruneIfEmpty(left);
        }

        internal static void Remove(VisualElement parent, VisualElement child)
        {
            var container = ContentOf(parent);
            var rows = Enter(container, -1);
            try
            {
                if (ReferenceEquals(child.hierarchy.parent, container)) rows.Remove(child);
                parent.Remove(child);
            }
            finally
            {
                Exit(container);
            }
            PruneIfEmpty(container);
        }

        internal static void HierarchyRemove(VisualElement parent, VisualElement child)
        {
            var rows = Enter(parent, -1);
            try
            {
                if (ReferenceEquals(child.hierarchy.parent, parent)) rows.Remove(child);
                parent.hierarchy.Remove(child);
            }
            finally
            {
                Exit(parent);
            }
            PruneIfEmpty(parent);
        }

        internal static void RemoveAt(VisualElement parent, int index)
        {
            var container = ContentOf(parent);
            var rows = Enter(container, index);
            try
            {
                if (index >= 0 && index < container.hierarchy.childCount) rows.Remove(container.hierarchy[index]);
                parent.RemoveAt(index);
            }
            finally
            {
                Exit(container);
            }
            PruneIfEmpty(container);
        }

        internal static void RemoveFromHierarchy(VisualElement child)
        {
            var parent = child.hierarchy.parent;
            if (parent == null)
            {
                child.RemoveFromHierarchy();
                return;
            }
            var rows = Enter(parent, -1);
            try
            {
                rows.Remove(child);
                child.RemoveFromHierarchy();
            }
            finally
            {
                Exit(parent);
            }
            PruneIfEmpty(parent);
        }

        internal static void Clear(VisualElement parent)
        {
            var container = ContentOf(parent);
            if (container.hierarchy.childCount == 0)
            {
                parent.Clear();
                return;
            }
            var rows = Enter(container, -1);
            try
            {
                rows.Clear();
                parent.Clear();
            }
            finally
            {
                Exit(container);
            }
            PruneIfEmpty(container);
        }

        // A container with no children keeps no model.
        private static void PruneIfEmpty(VisualElement container)
        {
            if (container.hierarchy.childCount == 0) s_rows.Remove(container);
        }

        // Sorts the live list and takes the order it came out in; the comparison runs in UI Toolkit's own sort, so the
        // model never has to reproduce it.
        internal static void HierarchySort(VisualElement parent, Comparison<VisualElement> comparison)
        {
            var rows = Enter(parent, -1);
            try
            {
                parent.hierarchy.Sort(comparison);
                rows.Clear();
                Fill(rows, parent);
            }
            finally
            {
                Exit(parent);
            }
        }

        [Conditional("UNITY_ASSERTIONS")]
        private static void Touch(VisualElement container) => s_touched.Add(container);

        // The start of a top-level pass: what was written since the last verification, outside any pass, is checked
        // before the pass's own writes start the set over. For those, a different child count is read as a foreign
        // change and rebuilt silently, since nothing tells a write Velvet made to a panel's root from the engine
        // clearing it when the panel goes.
        [Conditional("UNITY_ASSERTIONS")]
        internal static void BeginPass() => Verify(carried: true);

        // The end of a top-level pass: every container its writes reached, as a write's source container or its
        // destination, is compared with its model, child for child and kind for kind. One a foreign change was
        // announced for is rebuilt silently. A difference is reported and the model rebuilt. Never throws, and never
        // runs per op.
        [Conditional("UNITY_ASSERTIONS")]
        internal static void VerifyTouched() => Verify(carried: false);

        private static void Verify(bool carried)
        {
            if (s_open > 0) return;
            foreach (var container in s_touched)
            {
                if (!s_rows.TryGetValue(container, out var rows)) continue;
                if (rows.Stale || (carried && rows.TotalCount != container.hierarchy.childCount))
                {
                    rows.Clear();
                    Fill(rows, container);
                    continue;
                }
                var difference = Compare(rows, container);
                if (difference == null) continue;
                UnityEngine.Debug.LogAssertion(
                    $"ContainerRows drifted from the live child list of '{container.name}' ({container.GetType().Name}): {difference}");
                rows.Clear();
                Fill(rows, container);
            }
            s_touched.Clear();
        }

        private static string Describe(VisualElement element)
            => $"{element.GetType().Name} '{element.name}'";

        private static string? Compare(ContainerRows rows, VisualElement container)
        {
            var count = container.hierarchy.childCount;
            if (rows.TotalCount != count) return $"model holds {rows.TotalCount} children, live list {count}";
            for (var i = 0; i < count; i++)
            {
                var live = container.hierarchy[i];
                if (!ReferenceEquals(rows.ElementAt(i), live))
                {
                    return $"index {i} holds {Describe(rows.ElementAt(i))} in the model and {Describe(live)} live";
                }
                if (rows.KindAt(i) != Classify(live)) return $"index {i} is {rows.KindAt(i)}, classifies as {Classify(live)}";
            }
            return null;
        }
    }
}
