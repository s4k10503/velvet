using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Takes the fiber-side state of a context before an act, lets the act run its passes under a journal the
    /// harness owns, then puts back what the journal recorded and reports every entry that is not what it was.
    /// An empty report says the journal restored what the pass wrote on the fibers and tenancies registered before
    /// the act. Fibers and tenancies the act created are outside the snapshot, and what a real drop runs
    /// (<c>Abandon</c>) is not what this calls (<c>Rollback</c>).
    /// <para>
    /// The act's passes commit as they always do: the harness holds the journal open, so the render that would
    /// complete the pass does not, and rolls it back once the act returns. What it can compare is therefore what
    /// the journal restores without undoing a commit. Once it has compared, it writes back what the pass had left on
    /// each entry the rollback touched, so a test can still unmount the tree.
    /// </para>
    /// </summary>
    internal sealed class PassDropHarness
    {
        private readonly FiberPassJournal _journal;
        private readonly List<(string Label, Func<object?> Read, object? Before)> _entries = new();

        private PassDropHarness(ReconcilerContext context, ComponentFiber owner)
        {
            _journal = context.Pass.Journal;
            if (!_journal.TryOpen(owner))
            {
                throw new InvalidOperationException("The context's pass journal is already open.");
            }
            Capture(context.ComponentRegistry);
        }

        /// <summary>Takes the snapshot and opens the journal, with the tree's root fiber as the owner.</summary>
        internal static PassDropHarness Open(MountedTree mounted)
            => new(mounted.Root.Reconciler.Context, mounted.Root);

        /// <summary>Rolls the journal back, closes it, and returns one line per entry that differs from the snapshot.</summary>
        internal List<string> Drop(JournalFamilies expect)
        {
            if (_entries.Count == 0) throw new InvalidOperationException("The snapshot holds no fiber.");
            FiberPassJournalProbe.RequireRecorded(_journal, expect);
            var redo = FiberPassJournalProbe.CaptureRedo(_journal);
            _journal.Rollback();
            _journal.Close();
            var differences = new List<string>();
            foreach (var (label, read, before) in _entries)
            {
                var after = read();
                if (!Same(before, after)) differences.Add($"{label}: {Describe(before)} -> {Describe(after)}");
            }
            foreach (var write in redo) write();
            return differences;
        }

        private void Capture(ComponentRegistry registry)
        {
            var ordinal = 0;
            var fibers = new List<ComponentFiber>();
            fibers.AddRange(ComponentRegistryProbe.InlineFibers(registry));
            fibers.AddRange(ComponentRegistryProbe.WrapperFibers(registry));
            if (fibers.Count == 0) throw new InvalidOperationException("The registry's indexes held no fiber to snapshot.");
            foreach (var fiber in fibers)
            {
                var label = $"{fiber.Body?.Method.Name ?? "fiber"}#{ordinal++}";
                Add($"{label} slots", () => $"{fiber.MountSlotStart},{fiber.MountSlotCount}");
                Add($"{label} previous tree", () => fiber.PreviousTree);
                Add($"{label} pending old tree", () => fiber.PendingOldTree);
                Add($"{label} owning portal", () => fiber.OwningPortalPlaceholder);
                Add($"{label} boundary state", () => FiberBoundaryState.Of(fiber));
            }
            foreach (var (container, tenancy) in ComponentRegistryProbe.Tenancies(registry))
            {
                Add($"tenancy {container.name} shifted", () => tenancy.ShiftedRows.ToString());
            }
        }

        private void Add(string label, Func<object?> read) => _entries.Add((label, read, read()));

        // A string and a boundary state are compared by value and every other reading by reference: the trees and
        // the Portal stamp are arrays and elements, whose identity is what a restore has to give back.
        private static bool Same(object? before, object? after)
            => before is string or FiberBoundaryState ? Equals(before, after) : ReferenceEquals(before, after);

        private static string Describe(object? reading)
            => reading switch
            {
                null => "null",
                string text => text,
                Array array => $"{reading.GetType().Name}[{array.Length}]@{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(array)}",
                VisualElement element => $"{element.GetType().Name} '{element.name}'",
                _ => reading.ToString(),
            };
    }

    /// <summary>The families of entries a case expects the act to have written into the journal.</summary>
    [Flags]
    internal enum JournalFamilies
    {
        Slots = 1,
        Trees = 2,
        Boundary = 4,
    }

    /// <summary>Reads the entries a <see cref="FiberPassJournal"/> holds, which are private.</summary>
    internal static class FiberPassJournalProbe
    {
        /// <summary>Throws where the journal holds no entry in a family the case names, so an act that changed nothing reads red.</summary>
        internal static void RequireRecorded(FiberPassJournal journal, JournalFamilies expect)
        {
            RequireFamily(journal, expect, JournalFamilies.Slots, "_slots");
            RequireFamily(journal, expect, JournalFamilies.Trees, "_trees");
            RequireFamily(journal, expect, JournalFamilies.Boundary, "_boundaries");
        }

        private static void RequireFamily(FiberPassJournal journal, JournalFamilies expect, JournalFamilies family, string table)
        {
            if ((expect & family) != 0 && Table(journal, table).Count == 0)
            {
                throw new InvalidOperationException($"The act wrote nothing into the journal's {family} entries.");
            }
        }

        private static IDictionary Table(FiberPassJournal journal, string name)
            => (IDictionary)(typeof(FiberPassJournal).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(journal) ?? throw new MissingFieldException(typeof(FiberPassJournal).FullName, name));

        /// <summary>One write per journaled entry that puts back what the entry holds now.</summary>
        internal static List<Action> CaptureRedo(FiberPassJournal journal)
        {
            var redo = new List<Action>();
            foreach (DictionaryEntry entry in Table(journal, "_slots"))
            {
                var fiber = (ComponentFiber)entry.Key;
                var (start, count) = (fiber.MountSlotStart, fiber.MountSlotCount);
                redo.Add(() =>
                {
                    fiber.MountSlotStart = start;
                    fiber.MountSlotCount = count;
                });
            }
            foreach (DictionaryEntry entry in Table(journal, "_shiftedRows"))
            {
                var tenancy = (InlineTenancy)entry.Key;
                var shifted = tenancy.ShiftedRows;
                redo.Add(() => tenancy.ShiftedRows = shifted);
            }
            foreach (DictionaryEntry entry in Table(journal, "_boundaries"))
            {
                var fiber = (ComponentFiber)entry.Key;
                var state = FiberBoundaryState.Of(fiber);
                redo.Add(() => state.WriteTo(fiber));
            }
            foreach (DictionaryEntry entry in Table(journal, "_trees"))
            {
                var fiber = (ComponentFiber)entry.Key;
                var (previous, pendingOld, owning) = (fiber.PreviousTree, fiber.PendingOldTree, fiber.OwningPortalPlaceholder);
                redo.Add(() =>
                {
                    fiber.PreviousTree = previous;
                    fiber.PendingOldTree = pendingOld;
                    fiber.OwningPortalPlaceholder = owning;
                });
            }
            return redo;
        }
    }

    /// <summary>Reaches the indexes of a <see cref="ComponentRegistry"/>, which are private.</summary>
    internal static class ComponentRegistryProbe
    {
        private static object Field(ComponentRegistry registry, string name)
            => typeof(ComponentRegistry).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(registry) ?? throw new MissingFieldException(typeof(ComponentRegistry).FullName, name);

        internal static List<ComponentFiber> InlineFibers(ComponentRegistry registry)
        {
            var fibers = new List<ComponentFiber>();
            foreach (DictionaryEntry entry in (IDictionary)Field(registry, "_inlineFiberToKey")) fibers.Add((ComponentFiber)entry.Key);
            return fibers;
        }

        internal static List<ComponentFiber> WrapperFibers(ComponentRegistry registry)
        {
            var fibers = new List<ComponentFiber>();
            foreach (DictionaryEntry entry in (IDictionary)Field(registry, "_wrapperFiberInfo")) fibers.Add((ComponentFiber)entry.Key);
            return fibers;
        }

        internal static List<(VisualElement Container, InlineTenancy Tenancy)> Tenancies(ComponentRegistry registry)
        {
            var tenancies = new List<(VisualElement, InlineTenancy)>();
            foreach (DictionaryEntry entry in (IDictionary)Field(registry, "_tenancies"))
            {
                tenancies.Add(((VisualElement)entry.Key, (InlineTenancy)entry.Value!));
            }
            return tenancies;
        }
    }
}
