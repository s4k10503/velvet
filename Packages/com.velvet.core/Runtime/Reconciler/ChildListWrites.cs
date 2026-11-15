#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Every write Velvet makes to a child list goes through here, so the ContainerRows kept for each container stays
    // equal to the live list. Each op updates the model first and then makes the UI Toolkit call at the index the model
    // gave: a call that attaches the child can run a nested write into the same container (a layer hosted beside its
    // element), and that write has to find the model already holding the child. RowModelRoutingTests fails when a type
    // other than this one calls a child-list writer.
    //
    // A container's model is built from its live children the first time a write reaches it, and again whenever its
    // total no longer matches the live count, which is how a write made outside these ops (a user's own Add on an
    // element Velvet adopted) is noticed at the next write; the end-of-pass verifier reads the rest.
    internal static class ChildListWrites
    {
        private static readonly ConditionalWeakTable<VisualElement, ContainerRows> s_rows = new();
        private static readonly List<VisualElement> s_touched = new();

        internal static RowKind Classify(VisualElement element)
        {
            if (!LogicalChildSlots.IsInvisible(element)) return RowKind.Rendered;
            return LogicalChildSlots.StaysFirst(element) ? RowKind.LeadingInvisible : RowKind.Invisible;
        }

        // The model of a container that has one, as it stands: a read that never builds.
        internal static ContainerRows? Peek(VisualElement container)
            => s_rows.TryGetValue(container, out var rows) ? rows : null;

        // The model of container, built or rebuilt from its live children when it does not match them.
        internal static ContainerRows RowsOf(VisualElement container)
        {
            if (s_rows.TryGetValue(container, out var rows))
            {
                if (rows.TotalCount == container.hierarchy.childCount) return rows;
                rows.Clear();
            }
            else
            {
                rows = new ContainerRows();
                s_rows.Add(container, rows);
            }
            Fill(rows, container);
            return rows;
        }

        private static void Fill(ContainerRows rows, VisualElement container)
        {
            var count = container.hierarchy.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = container.hierarchy[i];
                rows.Insert(i, child, Classify(child));
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

        // Takes child out of whichever model holds it, ahead of a call that makes UI Toolkit detach it first.
        private static void Detach(VisualElement child)
        {
            var old = child.hierarchy.parent;
            if (old != null && s_rows.TryGetValue(old, out var rows)) rows.Remove(child);
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

        private static void InsertCore(VisualElement container, int index, VisualElement child, bool viaHierarchy,
            VisualElement caller)
        {
            var rows = RowsOf(container);
            Touch(container);
            var inside = child.hierarchy.parent != null && ReferenceEquals(child.hierarchy.parent, container);
            var length = container.hierarchy.childCount - (inside ? 1 : 0);
            if (index >= 0 && index <= length)
            {
                Detach(child);
                rows.Insert(index, child, Classify(child));
            }
            if (viaHierarchy) container.hierarchy.Insert(index, child);
            else caller.Insert(index, child);
        }

        internal static void Remove(VisualElement parent, VisualElement child)
        {
            var container = ContentOf(parent);
            if (child.hierarchy.parent != null && ReferenceEquals(child.hierarchy.parent, container))
            {
                RowsOf(container).Remove(child);
                Touch(container);
            }
            parent.Remove(child);
            PruneIfEmpty(container);
        }

        internal static void HierarchyRemove(VisualElement parent, VisualElement child)
        {
            if (child.hierarchy.parent != null && ReferenceEquals(child.hierarchy.parent, parent))
            {
                RowsOf(parent).Remove(child);
                Touch(parent);
            }
            parent.hierarchy.Remove(child);
            PruneIfEmpty(parent);
        }

        internal static void RemoveAt(VisualElement parent, int index)
        {
            var container = ContentOf(parent);
            RemoveAtCore(container, index);
            parent.RemoveAt(index);
            PruneIfEmpty(container);
        }

        internal static void HierarchyRemoveAt(VisualElement parent, int index)
        {
            RemoveAtCore(parent, index);
            parent.hierarchy.RemoveAt(index);
            PruneIfEmpty(parent);
        }

        private static void RemoveAtCore(VisualElement container, int index)
        {
            if (index < 0 || index >= container.hierarchy.childCount) return;
            var rows = RowsOf(container);
            Touch(container);
            rows.Remove(container.hierarchy[index]);
        }

        internal static void RemoveFromHierarchy(VisualElement child)
        {
            var parent = child.hierarchy.parent;
            if (parent != null)
            {
                RowsOf(parent).Remove(child);
                Touch(parent);
            }
            child.RemoveFromHierarchy();
            if (parent != null) PruneIfEmpty(parent);
        }

        internal static void Clear(VisualElement parent)
        {
            var container = ContentOf(parent);
            if (container.hierarchy.childCount > 0)
            {
                RowsOf(container).Clear();
                Touch(container);
            }
            parent.Clear();
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
            parent.hierarchy.Sort(comparison);
            var rows = RowsOf(parent);
            Touch(parent);
            rows.Clear();
            Fill(rows, parent);
        }

        internal static void PlaceBehind(VisualElement element, VisualElement sibling)
        {
            element.PlaceBehind(sibling);
            Resync(element.hierarchy.parent);
        }

        internal static void PlaceInFront(VisualElement element, VisualElement sibling)
        {
            element.PlaceInFront(sibling);
            Resync(element.hierarchy.parent);
        }

        internal static void BringToFront(VisualElement element)
        {
            element.BringToFront();
            Resync(element.hierarchy.parent);
        }

        internal static void SendToBack(VisualElement element)
        {
            element.SendToBack();
            Resync(element.hierarchy.parent);
        }

        // The order a UI Toolkit placement call left, for the calls whose resulting index only UI Toolkit knows.
        private static void Resync(VisualElement? container)
        {
            if (container == null) return;
            var rows = RowsOf(container);
            Touch(container);
            rows.Clear();
            Fill(rows, container);
        }

        [Conditional("UNITY_ASSERTIONS")]
        private static void Touch(VisualElement container)
        {
            if (!s_touched.Contains(container)) s_touched.Add(container);
        }

        // The start of a top-level pass: what was written before it, outside any pass, is not the pass's to verify.
        [Conditional("UNITY_ASSERTIONS")]
        internal static void BeginPass() => s_touched.Clear();

        // The end of a top-level pass: every container the pass wrote is compared with its model, element for
        // element and kind for kind, and a container that differs is reported and rebuilt. Never throws, and never runs
        // per op.
        [Conditional("UNITY_ASSERTIONS")]
        internal static void VerifyTouched()
        {
            for (var i = 0; i < s_touched.Count; i++)
            {
                var container = s_touched[i];
                if (!s_rows.TryGetValue(container, out var rows)) continue;
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
            => $"{element.GetType().Name} '{element.name}' [{string.Join(" ", element.GetClasses())}]";

        private static string? Compare(ContainerRows rows, VisualElement container)
        {
            var count = container.hierarchy.childCount;
            if (rows.TotalCount != count) return $"model holds {rows.TotalCount} children, live list {count}";
            for (var i = 0; i < count; i++)
            {
                var live = container.hierarchy[i];
                if (!ReferenceEquals(rows.ElementAt(i), live)) return $"index {i} holds {Describe(rows.ElementAt(i))} in the model and {Describe(live)} live";
                if (rows.KindAt(i) != Classify(live)) return $"index {i} is {rows.KindAt(i)}, classifies as {Classify(live)}";
            }
            return null;
        }
    }
}
