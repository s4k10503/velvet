#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Every write the runtime assembly makes to a child list goes through here (RowModelRoutingTests), and each op
    // updates the ContainerRows of the container beside the write (a recorded write, below, is applied to the live list
    // later): the model first and then the UI Toolkit call, so a
    // write nested inside that call finds the child already in the model; only
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
            if (LogicalChildSlots.StaysFirst(element)) return RowKind.LeadingInvisible;
            return LogicalChildSlots.IsHostedAfterItsRow(element) ? RowKind.Hosted : RowKind.Invisible;
        }

        // The child count of a container: the live list's, or the model's where a write is recorded and not yet applied.
        private static int CountOf(VisualElement content)
            => s_rows.TryGetValue(content, out var rows) && rows.Pending > 0 ? rows.TotalCount : content.hierarchy.childCount;

        // The model of a container that has one, as it stands: a read that never builds or checks. Private until a
        // caller needs one that does not build; the tests reach it by reflection (RowModelAccess).
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
            else if (s_open == 0 && rows.Pending == 0 && !Agrees(rows, container, readAt))
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
            // MUTANT_SURVIVES(equivalent, boundary): at readAt 0 the comparison is the first-child one made above, so skipping it changes no answer.
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
            if (!s_rows.TryGetValue(container, out var rows) || rows.Pending > 0) return;
            if (full ? Compare(rows, container) == null : Agrees(rows, container, -1)) return;
            rows.Clear();
            Fill(rows, container);
        }

        // What LogicalChildSlots.Count and ToLogical answer, from the model of the container's content element, which is
        // built here when the container has children and no model.
        internal static int RenderedCount(VisualElement container)
        {
            var content = ContentOf(container);
            return CountOf(content) == 0 ? 0 : Synced(content, -1).RenderedCount;
        }

        internal static int ToLogical(VisualElement container, int physical)
        {
            var content = ContentOf(container);
            var count = CountOf(content);
            return count == 0 ? 0 : Synced(content, Math.Min(physical, count) - 1).ToLogical(physical);
        }

        // What LogicalChildSlots.ToPhysical and TryGetPhysical answer. The slot's element is compared with the live
        // child at the index the model gave, so a model that drifted where the cheap probes do not look is rebuilt
        // before the answer is returned.
        internal static int ToPhysical(VisualElement container, int logical)
        {
            var content = ContentOf(container);
            if (CountOf(content) == 0) return 0;
            var rows = Synced(content, -1);
            var physical = rows.ToPhysical(logical);
            if (!AgreesAt(rows, content, physical))
            {
                rows = Rebuilt(rows, content);
                physical = rows.ToPhysical(logical);
            }
            return physical;
        }

        internal static bool TryGetPhysical(VisualElement container, int logical, out int physical)
        {
            var content = ContentOf(container);
            if (CountOf(content) == 0)
            {
                physical = 0;
                return false;
            }
            var rows = Synced(content, -1);
            var found = rows.TryGetPhysical(logical, out physical);
            if (!AgreesAt(rows, content, physical))
            {
                rows = Rebuilt(rows, content);
                found = rows.TryGetPhysical(logical, out physical);
            }
            return found;
        }

        private static bool AgreesAt(ContainerRows rows, VisualElement content, int physical)
            => s_open > 0 || rows.Pending > 0 || physical >= CountOf(content)
                || ReferenceEquals(rows.ElementAt(physical), content.hierarchy[physical]);

        private static ContainerRows Rebuilt(ContainerRows rows, VisualElement content)
        {
            rows.Clear();
            Fill(rows, content);
            return rows;
        }

        // The index of the child in the container's list, or -1 where it is not one of its children.
        internal static int PhysicalIndexOf(VisualElement container, VisualElement element)
        {
            var content = ContentOf(container);
            return CountOf(content) == 0 ? -1 : Synced(content, -1).PhysicalIndexOf(element);
        }

        // The index of the child in container's own hierarchy list, for a container written through HierarchyInsert,
        // whose model is keyed on container itself where PhysicalIndexOf reads the one its content element holds.
        internal static int HierarchyIndexOf(VisualElement container, VisualElement element)
            => CountOf(container) == 0 ? -1 : Synced(container, -1).PhysicalIndexOf(element);

        // Appends the rendered children at slots [slotStart, end) to into, as OldSlotOccupants reads them.
        internal static void Rows(VisualElement container, int slotStart, int end, List<VisualElement?> into)
        {
            var content = ContentOf(container);
            if (CountOf(content) == 0) return;
            Synced(content, -1).Rows(slotStart, end, into);
        }

        // The rendered child holding the slot, or null where none does: what TryGetPhysical finds, read as the element.
        internal static VisualElement? RowAt(VisualElement container, int logical)
        {
            var content = ContentOf(container);
            if (CountOf(content) == 0) return null;
            var rows = Synced(content, -1);
            var row = rows.RowAt(logical);
            if (!AgreesAt(rows, content, rows.ToPhysical(logical)))
            {
                row = Rebuilt(rows, content).RowAt(logical);
            }
            return row;
        }

        // Appends the children at physical indices [start, end) to into, in order.
        internal static void PhysicalRange(VisualElement container, int start, int end, List<VisualElement> into)
        {
            var content = ContentOf(container);
            if (CountOf(content) == 0) return;
            // MUTANT_SURVIVES(equivalent, arithmetic): readAt reaches only the probe in Agrees, and past the last child that probe repeats the last-child comparison made before it.
            Synced(content, Math.Min(start, CountOf(content) - 1)).PhysicalRange(start, end, into);
        }

        // Inserts a rendered child where LogicalChildSlots.ToPhysical puts a child at the slot.
        internal static void InsertAtSlot(VisualElement parent, int slot, VisualElement child)
            => Insert(parent, ToPhysical(parent, slot), child);

        // For a role or class change that writes no child list, which flips what Classify answers for a child that
        // stays where it is: FiberZLayerCoordinator's Hold and Release, and RingOverlay's mode switch.
        internal static void Reclassify(VisualElement element)
        {
            var parent = ParentOf(element);
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
            if (ShouldRecord(container, child))
            {
                RecordInsert(container, index, child, viaHierarchy, caller);
                return;
            }
            FlushIfPending(container, child);
            var left = child.hierarchy.parent;
            // MUTANT_SURVIVES(equivalent, arithmetic): readAt reaches only the probe in Agrees, and past the last child that probe repeats the last-child comparison made before it.
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
            if (s_applying)
            {
                RecordRemoveChild(child);
                return;
            }
            var container = ContentOf(parent);
            FlushIfPending(container, child);
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
            FlushIfPending(parent, child);
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
            if (s_applying)
            {
                RecordRemoveAt(container, parent, index);
                return;
            }
            FlushIfPending(container, null);
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
            if (s_applying)
            {
                RecordRemoveChild(child);
                return;
            }
            FlushIfPending(null, child);
            var parent = child.hierarchy.parent;
            if (parent == null)
            {
                // MUTANT_SURVIVES(equivalent, line removed): this branch runs only where child.hierarchy.parent is null, and RemoveFromHierarchy does nothing without one.
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
            FlushIfPending(container, null);
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

        // ---- Recorded writes ----
        //
        // Inside a pass, an insert or a move the walk makes through HostCommitQueue into a container the pass did not
        // create updates the model and is recorded; the live list takes it where FinishPass starts (ApplyRecorded), in
        // the order recorded. A node's patch halves are recorded in the same list (PatchPre and PatchPost, the
        // children's ops between them); they name no container, so a reader of the element's parent does not apply them. The readers the walk has moved to the model (ParentOf,
        // ChildCount, ElementAtPhysical and the reads above) answer from it meanwhile, and a container with a recorded
        // write is skipped by the checks that compare the model with the live list. Any other write into such a
        // container applies the recorded ones first, except one made while they apply, which is recorded behind them.
        private enum OpKind : byte { Insert, RemoveAt, RemoveChild, PatchPre, PatchPost }

        private struct Op
        {
            internal VisualElement Container;
            internal VisualElement Caller;
            internal VisualElement Child;
            // The index into s_patches of a PatchPre or a PatchPost.
            internal int Index;
            internal OpKind Kind;
            internal bool ViaHierarchy;
        }

        private static readonly List<Op> s_ops = new();
        private static readonly List<PendingPatch> s_patches = new();
        // Where the model holds each child a recorded write touched; null where the write took it out.
        private static readonly Dictionary<VisualElement, VisualElement?> s_pendingParent = new();
        private static readonly HashSet<VisualElement> s_pendingContainers = new();
        private static readonly HashSet<VisualElement> s_created = new();
        private static bool s_recording;
        private static bool s_applying;

        // Raised by HostCommitQueue around the writes it makes, which are the ones recorded.
        internal static int ViaQueue;

        // A pass starting where another is still recording applies the other's writes first.
        internal static void BeginRecording()
        {
            if (s_recording) ApplyRecorded();
            s_created.Clear();
            s_recording = true;
        }

        // A container the pass creates takes its children at once.
        internal static void MarkCreated(VisualElement element)
        {
            if (!s_recording) return;
            s_created.Add(element);
            s_created.Add(ContentOf(element));
        }

        // A write made while the recorded ones apply (a child attaching to the panel places the layer it hosts beside
        // itself) is recorded behind them, since the model already holds the rows still to apply and the live list
        // does not.
        private static bool ShouldRecord(VisualElement container, VisualElement child)
            => s_applying
               || (s_recording && ViaQueue > 0 && (!s_created.Contains(container) || s_pendingParent.ContainsKey(child)));

        internal static int ChildCount(VisualElement container) => CountOf(ContentOf(container));

        // What element.hierarchy.childCount and element.hierarchy[index] answer where nothing is recorded, and what the
        // model holds where a write is: a read of a descendant that builds no model.
        internal static int HierarchyCount(VisualElement? element)
            => element == null ? 0 : s_rows.TryGetValue(element, out var rows) && rows.Pending > 0 ? rows.TotalCount : element.hierarchy.childCount;

        internal static VisualElement HierarchyChild(VisualElement element, int index)
            => s_rows.TryGetValue(element, out var rows) && rows.Pending > 0 ? rows.ElementAt(index) : element.hierarchy[index];

        internal static VisualElement? ParentOf(VisualElement element)
            => s_pendingParent.TryGetValue(element, out var parent) ? parent : element.hierarchy.parent;

        // The child at the physical index of the container's model, which is the live list where nothing is recorded.
        internal static VisualElement? ElementAtPhysical(VisualElement container, int physical)
        {
            var content = ContentOf(container);
            return physical < 0 || physical >= CountOf(content) ? null : Synced(content, physical).ElementAt(physical);
        }

        private static void Mark(VisualElement container)
        {
            if (s_rows.TryGetValue(container, out var rows)) rows.Pending++;
            s_pendingContainers.Add(container);
        }

        private static void RecordInsert(VisualElement container, int index, VisualElement child, bool viaHierarchy,
            VisualElement caller)
        {
            var left = ParentOf(child);
            var rows = Synced(container, -1);
            var held = false;
            if (index >= 0 && index <= rows.TotalCount)
            {
                if (left != null)
                {
                    var leftRows = ReferenceEquals(left, container) ? rows
                        : left.hierarchy.childCount > 0 || s_rows.TryGetValue(left, out _) ? Synced(left, -1) : null;
                    if (leftRows != null && leftRows.Contains(child))
                    {
                        leftRows.Remove(child);
                        Mark(left);
                    }
                }
                rows.Insert(Math.Min(index, rows.TotalCount), child, Classify(child));
                held = true;
            }
            Mark(container);
            if (held) s_pendingParent[child] = container;
            s_ops.Add(new Op
            {
                Container = container, Caller = caller, Child = child, Index = index, Kind = OpKind.Insert,
                ViaHierarchy = viaHierarchy,
            });
            Touch(container);
        }

        // The removal half of a move HostCommitQueue.Reorder makes.
        internal static void RemoveAtForMove(VisualElement parent, int index)
        {
            var container = ContentOf(parent);
            if (s_applying || (s_recording && ViaQueue > 0 && !s_created.Contains(container)))
            {
                RecordRemoveAt(container, parent, index);
                return;
            }
            RemoveAt(parent, index);
        }

        private static void RecordRemoveAt(VisualElement container, VisualElement caller, int index)
        {
            var rows = Synced(container, -1);
            if (index >= 0 && index < rows.TotalCount)
            {
                var element = rows.ElementAt(index);
                rows.Remove(element);
                s_pendingParent[element] = null;
            }
            Mark(container);
            s_ops.Add(new Op { Container = container, Caller = caller, Index = index, Kind = OpKind.RemoveAt });
            Touch(container);
        }

        private static void RecordRemoveChild(VisualElement child)
        {
            var container = ParentOf(child);
            if (container == null) return;
            if (s_rows.TryGetValue(container, out var rows) && rows.Contains(child))
            {
                rows.Remove(child);
                Mark(container);
                Touch(container);
            }
            s_pendingParent[child] = null;
            s_ops.Add(new Op { Container = container, Child = child, Kind = OpKind.RemoveChild });
        }

        // Whether a pass is recording writes it has not applied yet.
        internal static bool IsRecording => s_recording && !s_applying;

        // Whether a patch of element waits for the pass to end: one made where a pass records, to an element that has a
        // live or a recorded parent. An element with neither is written at once.
        internal static bool StagesPatch(VisualElement element)
            => s_recording && !s_applying && ParentOf(element) != null;

        // Records the first half of a patch behind every op recorded so far, to commit where the recorded ops apply.
        internal static void RecordPatchPre(in PendingPatch patch) => RecordPatch(OpKind.PatchPre, patch);

        // Records the second half, behind the ops of the children the walk reconciled between the two.
        internal static void RecordPatchPost(in PendingPatch patch) => RecordPatch(OpKind.PatchPost, patch);

        private static void RecordPatch(OpKind kind, in PendingPatch patch)
        {
            s_patches.Add(patch);
            s_ops.Add(new Op { Index = s_patches.Count - 1, Kind = kind });
        }

        // For a reader of container's live child list: the recorded writes of the container apply first.
        internal static void SyncLive(VisualElement container) => FlushIfPending(ContentOf(container), null);


        private static void FlushIfPending(VisualElement? container, VisualElement? child)
        {
            if (s_ops.Count == 0 || s_applying) return;
            if ((container != null && s_pendingContainers.Contains(container))
                || (child != null && (s_pendingParent.ContainsKey(child)
                    || (child.hierarchy.parent is { } live && s_pendingContainers.Contains(live)))))
            {
                ApplyRecorded();
            }
        }

        // Puts the recorded writes on the live lists in the order they were made and ends recording.
        internal static void ApplyRecorded()
        {
            if (s_applying) return;
            s_applying = true;
            try
            {
                for (var i = 0; i < s_ops.Count; i++)
                {
                    var op = s_ops[i];
                    if (op.Kind >= OpKind.PatchPre)
                    {
                        var patch = s_patches[op.Index];
                        if (op.Kind == OpKind.PatchPre) patch.Patcher.CommitPre(patch);
                        else patch.Patcher.CommitPost(patch);
                        continue;
                    }
                    s_open++;
                    s_flight.Add(op.Container);
                    try
                    {
                        if (op.Kind == OpKind.RemoveChild) op.Child.RemoveFromHierarchy();
                        else if (op.Kind == OpKind.RemoveAt) op.Caller.RemoveAt(op.Index);
                        else if (op.ViaHierarchy) op.Container.hierarchy.Insert(op.Index, op.Child);
                        else op.Caller.Insert(op.Index, op.Child);
                    }
                    finally
                    {
                        s_flight.RemoveAt(s_flight.Count - 1);
                        s_open--;
                    }
                }
            }
            finally
            {
                s_applying = false;
                s_ops.Clear();
                s_patches.Clear();
                s_pendingParent.Clear();
                foreach (var container in s_pendingContainers)
                {
                    if (s_rows.TryGetValue(container, out var rows)) rows.Pending = 0;
                }
                foreach (var container in s_pendingContainers)
                {
                    Touch(container);
                    PruneIfEmpty(container);
                }
                s_pendingContainers.Clear();
                foreach (var nested in s_nested) Settle(nested, full: true);
                s_nested.Clear();
            }
        }

        // Ends recording and applies what it recorded.
        internal static void EndRecording()
        {
            s_recording = false;
            ApplyRecorded();
            s_created.Clear();
        }

        // A container with no children keeps no model.
        private static void PruneIfEmpty(VisualElement container)
        {
            if (container.hierarchy.childCount == 0 && !(s_rows.TryGetValue(container, out var rows) && rows.Pending > 0)) s_rows.Remove(container);
        }

        // Sorts the live list and takes the order it came out in; the comparison runs in UI Toolkit's own sort, so the
        // model never has to reproduce it.
        internal static void HierarchySort(VisualElement parent, Comparison<VisualElement> comparison)
        {
            FlushIfPending(parent, null);
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
                if (!s_rows.TryGetValue(container, out var rows) || rows.Pending > 0) continue;
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
