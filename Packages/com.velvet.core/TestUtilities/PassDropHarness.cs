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
    /// An empty report is the claim a pass dropped at its end would leave the fiber side as it found it.
    /// <para>
    /// The act's passes commit as they always do: the harness holds the journal open, so the render that would
    /// complete the pass does not, and rolls it back once the act returns. What it can compare is therefore what
    /// the journal restores without undoing a commit.
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
        internal List<string> Drop()
        {
            _journal.Rollback();
            _journal.Close();
            var differences = new List<string>();
            foreach (var (label, read, before) in _entries)
            {
                var after = read();
                if (!Same(before, after)) differences.Add($"{label}: {Describe(before)} -> {Describe(after)}");
            }
            return differences;
        }

        private void Capture(ComponentRegistry registry)
        {
            var ordinal = 0;
            var fibers = new List<ComponentFiber>();
            fibers.AddRange(ComponentRegistryProbe.InlineFibers(registry));
            fibers.AddRange(ComponentRegistryProbe.WrapperFibers(registry));
            foreach (var fiber in fibers)
            {
                var label = $"{fiber.Body?.Method.Name ?? "fiber"}#{ordinal++}";
                Add($"{label} slots", () => $"{fiber.MountSlotStart},{fiber.MountSlotCount}");
                Add($"{label} previous tree", () => fiber.PreviousTree);
                Add($"{label} pending old tree", () => fiber.PendingOldTree);
                Add($"{label} owning portal", () => fiber.OwningPortalPlaceholder);
            }
            foreach (var (container, tenancy) in ComponentRegistryProbe.Tenancies(registry))
            {
                Add($"tenancy {container.name} shifted", () => tenancy.ShiftedRows.ToString());
            }
        }

        private void Add(string label, Func<object?> read) => _entries.Add((label, read, read()));

        // A string is compared by value and every other reading by reference: the trees and the Portal stamp are
        // arrays and elements, whose identity is what a restore has to give back.
        private static bool Same(object? before, object? after)
            => before is string ? Equals(before, after) : ReferenceEquals(before, after);

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
