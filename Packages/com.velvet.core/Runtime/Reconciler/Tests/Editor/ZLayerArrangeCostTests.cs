using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what arranging a parent's stacking costs a time-sliced pass over rows none of which is tracked: the
    /// placeholder lookups of a pass patching the rows one per slice grow with the rows rather than with their square,
    /// with a <c>relative</c> element in another parent as without one.
    /// </summary>
    [TestFixture]
    internal sealed class ZLayerArrangeCostTests
    {
        private const int Rows = 100;

        // The budget ReconcilerBenchmarks yields per element with.
        private const double YieldPerRowBudgetMs = 1e-6;

        private enum Shape
        {
            NothingStacked,
            GhostPinnedAndUnpinned,
            RelativeElementInAnotherParent,
        }

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
        public void Given_TwiceTheRowsAndNothingStacked_When_ATimeSlicedPassPatchesEveryRow_Then_ItsPlaceholderLookupsGrowNoFasterThanTheRows()
        {
            // Arrange
            var fewer = PlaceholderLookupsOfASlicedPass(Rows, Shape.NothingStacked);

            // Act
            var more = PlaceholderLookupsOfASlicedPass(2 * Rows, Shape.NothingStacked);

            // Assert — what grows with the rows cancels here, and looking every row up again at the end of every slice
            // would leave a remainder growing with the square of their count. Where a count could not be taken, the
            // excess is one no bound admits.
            Assert.That(fewer < 0 || more < 0 ? int.MaxValue : more - 2 * fewer, Is.LessThan(Rows));
        }

        [Test]
        public void Given_AGhostPinnedAndUnpinned_When_ATimeSlicedPassPatchesTwiceTheRows_Then_ItsPlaceholderLookupsGrowNoFasterThanTheRows()
        {
            // Arrange
            var fewer = PlaceholderLookupsOfASlicedPass(Rows, Shape.GhostPinnedAndUnpinned);

            // Act
            var more = PlaceholderLookupsOfASlicedPass(2 * Rows, Shape.GhostPinnedAndUnpinned);

            // Assert — as above: the rows' parent is looked through once after the unpin, and not again
            Assert.That(fewer < 0 || more < 0 ? int.MaxValue : more - 2 * fewer, Is.LessThan(Rows));
        }

        [Test]
        public void Given_ARelativeElementInAnotherParent_When_ATimeSlicedPassPatchesTwiceTheRows_Then_ItsPlaceholderLookupsGrowNoFasterThanTheRows()
        {
            // Arrange
            var fewer = PlaceholderLookupsOfASlicedPass(Rows, Shape.RelativeElementInAnotherParent);

            // Act
            var more = PlaceholderLookupsOfASlicedPass(2 * Rows, Shape.RelativeElementInAnotherParent);

            // Assert — as above: an element tracked elsewhere leaves nothing for the slices to look for here
            Assert.That(fewer < 0 || more < 0 ? int.MaxValue : more - 2 * fewer, Is.LessThan(Rows));
        }

        // The keys the placeholder table hashes during a pass that patches rows labels under the budget above, or -1
        // where a lookup is not counted once, the shape's relative element went untracked, or the pass yielded fewer
        // times than once per two rows.
        private static int PlaceholderLookupsOfASlicedPass(int rows, Shape shape)
        {
            using var scope = new ReconcilerScope();
            var ctx = scope.Reconciler.Context;
            var counter = new CountingComparer();
            typeof(ReconcilerContext)
                .GetField("<ZLayerPlaceholders>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(ctx, new Dictionary<VisualElement, VisualElement>(counter));
            var parent = scope.Root;
            if (shape == Shape.RelativeElementInAnotherParent)
            {
                scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                    new VNode[] { V.Div(className: "relative", name: "elsewhere"), V.Div(name: "rows") });
                if (!ctx.StackingFacts.ContainsKey(scope.Root.Q("elsewhere"))) return -1;
                parent = scope.Root.Q("rows");
            }
            var before = Labels(rows, "old-");
            var after = Labels(rows, "new-");
            scope.Reconciler.Reconcile(parent, Array.Empty<VNode>(), before);
            if (shape == Shape.GhostPinnedAndUnpinned)
            {
                FiberZLayerCoordinator.PinGhost(ctx, parent[0]);
                FiberZLayerCoordinator.UnpinGhost(ctx, parent[0]);
            }
            counter.Lookups = 0;
            ctx.ZLayerPlaceholders.TryGetValue(parent, out _);
            if (counter.Lookups != 1) return -1;
            counter.Lookups = 0;

            scope.Reconciler.Reconcile(parent, before, after, YieldPerRowBudgetMs);
            var resumes = 0;
            while (scope.Reconciler.HasPendingWork)
            {
                scope.Reconciler.ContinueReconcile(YieldPerRowBudgetMs);
                resumes++;
            }
            return 2 * resumes < rows ? -1 : counter.Lookups;
        }

        private static VNode[] Labels(int count, string prefix)
        {
            var nodes = new VNode[count];
            for (var i = 0; i < count; i++)
            {
                nodes[i] = V.Label(text: prefix + i);
            }
            return nodes;
        }
    }
}
