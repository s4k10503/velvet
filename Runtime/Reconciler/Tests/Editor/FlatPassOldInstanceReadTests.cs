// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a flat pass over labels reads its container's old instances (<see cref="OldSlotOccupants"/>). Handed
    /// back the nodes it reconciled last, a pass that does not park reads none, so its lookups do not grow with the
    /// rows; one that parks before it takes a row has read them by then, so a row inserted ahead of the list between
    /// slices does not move which element a later slice patches.
    /// </summary>
    [TestFixture]
    internal sealed class FlatPassOldInstanceReadTests
    {
        private const int Rows = 100;

        // A budget none of these passes reaches: the keyed diff takes its time-sliced path and never parks.
        private const double UnreachedBudgetMs = 1e9;

        // The budget ReconcilerBenchmarks yields per element with.
        private const double YieldPerRowBudgetMs = 1e-6;

        // What WrapperLookupsOfAPass returns where the table it counts through was not installed, above any count.
        private const int LookupNotCounted = int.MaxValue;

        private const string ChangedText = "changed";

        // Counts the keys the dictionary hashes.
        private sealed class CountingComparer : IEqualityComparer<VisualElement>
        {
            internal int Lookups;

            public bool Equals(VisualElement x, VisualElement y) => ReferenceEquals(x, y);

            public int GetHashCode(VisualElement element)
            {
                Lookups++;
                return RuntimeHelpers.GetHashCode(element);
            }
        }

        [Test]
        public void Given_AnUnkeyedListHandedItsOwnNodes_When_ItReconciles_Then_ItsWrapperLookupsDoNotGrowWithTheRows()
        {
            // Arrange
            var fewer = WrapperLookupsOfAPass(Rows, keyed: false, frameBudgetMs: 0);

            // Act
            var more = WrapperLookupsOfAPass(2 * Rows, keyed: false, frameBudgetMs: 0);

            // Assert
            Assert.That(Growth(fewer, more), Is.Zero);
        }

        [Test]
        public void Given_AKeyedListHandedItsOwnNodes_When_ItReconciles_Then_ItsWrapperLookupsDoNotGrowWithTheRows()
        {
            // Arrange
            var fewer = WrapperLookupsOfAPass(Rows, keyed: true, frameBudgetMs: 0);

            // Act
            var more = WrapperLookupsOfAPass(2 * Rows, keyed: true, frameBudgetMs: 0);

            // Assert
            Assert.That(Growth(fewer, more), Is.Zero);
        }

        [Test]
        public void Given_AKeyedListHandedItsOwnNodesUnderABudget_When_ItReconcilesWithoutParking_Then_ItsWrapperLookupsDoNotGrowWithTheRows()
        {
            // Arrange
            var fewer = WrapperLookupsOfAPass(Rows, keyed: true, UnreachedBudgetMs);

            // Act
            var more = WrapperLookupsOfAPass(2 * Rows, keyed: true, UnreachedBudgetMs);

            // Assert
            Assert.That(Growth(fewer, more), Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base reads every old row of a pass that removes one, at its entry.
        // This is the case that shows the counter sees that read, which the three zero growths above rely on.
        [Test]
        public void Given_AnUnkeyedListLosingItsLastRow_When_ItReconciles_Then_ItsWrapperLookupsGrowWithTheRows()
        {
            // Arrange
            var fewer = WrapperLookupsOfAPass(Rows, keyed: false, frameBudgetMs: 0, dropLastRow: true);

            // Act
            var more = WrapperLookupsOfAPass(2 * Rows, keyed: false, frameBudgetMs: 0, dropLastRow: true);

            // Assert
            Assert.That(Growth(fewer, more), Is.GreaterThanOrEqualTo(Rows));
        }

        // GREEN_ON_BASE(characterization): the base reads a flat pass's old rows at its entry, ahead of every slice.
        // What this pins is that a pass taking no row before it parks still reads them before the slice ends.
        [Test]
        public void Given_AnIndexedPassParkedAheadOfItsChangedRow_When_ARowIsInsertedAheadOfTheList_Then_TheChangedRowsOwnElementTakesThePatch()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var lastRow = ParkAheadOfTheLastRow(scope, keyed: false);
            scope.Root.Insert(0, new Label("inserted"));

            // Act
            while (scope.Reconciler.HasPendingWork) scope.Reconciler.ContinueReconcile(YieldPerRowBudgetMs);

            // Assert — null where the pass did not park ahead of the last row, which the expected text cannot equal
            Assert.That(lastRow?.text, Is.EqualTo(ChangedText));
        }

        // GREEN_ON_BASE(characterization): the base reads a flat pass's old rows at its entry, ahead of every slice.
        // What this pins is that a keyed pass taking no row before it parks still reads them before the slice ends.
        [Test]
        public void Given_AKeyedPassParkedAheadOfItsChangedRow_When_ARowIsInsertedAheadOfTheList_Then_TheChangedRowsOwnElementTakesThePatch()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var lastRow = ParkAheadOfTheLastRow(scope, keyed: true);
            scope.Root.Insert(0, new Label("inserted"));

            // Act
            while (scope.Reconciler.HasPendingWork) scope.Reconciler.ContinueReconcile(YieldPerRowBudgetMs);

            // Assert — null where the pass did not park ahead of the last row, which the expected text cannot equal
            Assert.That(lastRow?.text, Is.EqualTo(ChangedText));
        }

        // How many more lookups the pass over more rows took, or LookupNotCounted where either went uncounted.
        private static int Growth(int fewer, int more)
            => fewer == LookupNotCounted || more == LookupNotCounted ? LookupNotCounted : more - fewer;

        // The keys the wrapper table hashes while mounted labels reconcile against the same nodes, less the last one
        // where dropLastRow says so; LookupNotCounted where the counting table did not take a lookup. Reading the old
        // instances looks each row's wrapper up (OldSlotOccupants), so a pass that reads them takes one per row.
        private static int WrapperLookupsOfAPass(int rows, bool keyed, double frameBudgetMs, bool dropLastRow = false)
        {
            using var scope = new ReconcilerScope();
            var ctx = scope.Reconciler.Context;
            var counter = new CountingComparer();
            // Sized, so that a lookup is hashed before anything has been written to the table; the count checked below
            // fails where it is not.
            typeof(ReconcilerContext)
                .GetField("<WrapperToInnerMap>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(ctx, new Dictionary<VisualElement, VisualElement>(1, counter));
            var mounted = Labels(rows, keyed);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), mounted);
            var next = dropLastRow ? mounted.Take(rows - 1).ToArray() : mounted;
            counter.Lookups = 0;
            ctx.WrapperToInnerMap.TryGetValue(scope.Root, out _);
            if (counter.Lookups != 1) return LookupNotCounted;
            counter.Lookups = 0;

            scope.Reconciler.Reconcile(scope.Root, mounted, next, frameBudgetMs);
            return counter.Lookups;
        }

        // Mounts the rows and starts a pass that changes only the last of them, under YieldPerRowBudgetMs. Returns the
        // last row's element, or null where the pass did not park before reaching it.
        private static Label? ParkAheadOfTheLastRow(ReconcilerScope scope, bool keyed)
        {
            var mounted = Labels(Rows, keyed);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), mounted);
            var lastRow = (Label)scope.Root[Rows - 1];
            var changed = (VNode[])mounted.Clone();
            changed[Rows - 1] = V.Label(text: ChangedText, key: keyed ? Key(Rows - 1) : null);
            scope.Reconciler.Reconcile(scope.Root, mounted, changed, YieldPerRowBudgetMs);
            return scope.Reconciler.HasPendingWork && lastRow.text != ChangedText ? lastRow : null;
        }

        private static VNode[] Labels(int count, bool keyed)
        {
            var nodes = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                nodes[i] = V.Label(text: "row-" + i, key: keyed ? Key(i) : null);
            }
            return nodes;
        }

        private static string Key(int row) => "k" + row;
    }
}
