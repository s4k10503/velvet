using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A leaf's reconcile key belongs to the position it is emitted at, not to the node instance. One node can
    /// be emitted at several positions — a <c>V.Fragment</c> returned for every item of a <c>V.List</c> shares
    /// its children across the copies each item's key takes — and each position is a row of its own with its own
    /// identity, as React gives one element returned for several list items. And a key is compared only among
    /// the leaves one component emits: two sibling components each returning a leaf under one key are two rows,
    /// as React scopes keys to the component that renders them.
    /// </summary>
    [TestFixture]
    internal sealed class ScopedKeyOccurrenceTests
    {
        private static VNode s_row;
        private static StateUpdater<string[]> s_setItems;
        private static StateUpdater<int> s_setTick;
        private static int s_refSetUps;
        private static int s_refCleanUps;
        private VisualElement _root;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            s_row = null;
            s_setItems = default;
            s_setTick = default;
            s_refSetUps = 0;
            s_refCleanUps = 0;
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            s_row = null;
        }

        private static Action TrackRow(VisualElement _)
        {
            s_refSetUps++;
            return () => s_refCleanUps++;
        }

        [Component(Compiler = false)]
        private static VNode SharedRowList()
        {
            var (items, setItems) = Hooks.UseState(new[] { "a", "b" });
            s_setItems = setItems;
            return V.Div(name: "rows", children: V.List(items, item => item, _ => s_row));
        }

        [Test]
        public void Given_OneFragmentReturnedForEveryListItem_When_TheItemsAreReordered_Then_EachRowKeepsItsElement()
        {
            // Arrange
            s_row = V.Fragment(new VNode[] { V.Label(text: "row", refCallback: TrackRow) });
            _mounted = V.Mount(_root, V.Component(SharedRowList));
            var rows = _root.Q<VisualElement>("rows");
            var first = rows.ElementAt(0);
            var second = rows.ElementAt(1);

            // Act
            s_setItems.Invoke(new[] { "b", "a" });
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (rows.childCount, ReferenceEquals(rows.ElementAt(0), second), ReferenceEquals(rows.ElementAt(1), first),
                    s_refSetUps, s_refCleanUps),
                Is.EqualTo((2, true, true, 2, 0)));
        }

        [Test]
        public void Given_OneFragmentReturnedForEveryListItem_When_AnItemIsAppended_Then_TheRowsBeforeItKeepTheirElements()
        {
            // Arrange
            s_row = V.Fragment(new VNode[] { V.Label(text: "row", refCallback: TrackRow) });
            _mounted = V.Mount(_root, V.Component(SharedRowList));
            var rows = _root.Q<VisualElement>("rows");
            var first = rows.ElementAt(0);
            var second = rows.ElementAt(1);

            // Act
            s_setItems.Invoke(new[] { "a", "b", "c" });
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (rows.childCount, ReferenceEquals(rows.ElementAt(0), first), ReferenceEquals(rows.ElementAt(1), second),
                    s_refSetUps, s_refCleanUps),
                Is.EqualTo((3, true, true, 3, 0)));
        }

        [Test]
        public void Given_AKeyedLeafInsideAKeyedFragment_When_AResumedUpdateMovesItOutOfTheFragment_Then_ItMountsAFreshElement()
        {
            // Arrange — the prefix is the same instance on both sides, so the first slice passes it and parks.
            using var scope = new ReconcilerScope();
            var prefix = V.Label(key: "prefix", text: "prefix");
            var oldTree = new VNode[]
            {
                prefix,
                V.Fragment(new VNode[] { V.Label(key: "row", text: "old", refCallback: TrackRow) }, key: "scope"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var oldRow = scope.Root.FindLabelByText("old");
            var nextTree = new VNode[] { prefix, V.Label(key: "row", text: "new", refCallback: TrackRow) };

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, nextTree, frameBudgetMs: double.Epsilon);
            var parked = scope.Reconciler.HasPendingWork;
            while (scope.Reconciler.HasPendingWork) scope.Reconciler.ContinueReconcile();

            // Assert
            var newRow = scope.Root.FindLabelByText("new");
            Assert.That(
                (parked, scope.Root.childCount, newRow != null, ReferenceEquals(oldRow, newRow), s_refSetUps,
                    s_refCleanUps),
                Is.EqualTo((true, 2, true, false, 2, 1)));
        }

        [TestCase(0d)]
        [TestCase(double.Epsilon)]
        public void Given_ANodeHeldAcrossRenders_When_ItMovesOutOfAKeyedFragment_Then_ItMountsAFreshElement(
            double frameBudgetMs)
        {
            // Arrange — the same instance on both sides, so only its position tells the two renders apart.
            using var scope = new ReconcilerScope();
            var held = V.Label(key: "row", text: "held", refCallback: TrackRow);
            var oldTree = new VNode[] { V.Fragment(new VNode[] { held }, key: "scope") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var oldRow = scope.Root.ElementAt(0);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { held }, frameBudgetMs);
            while (scope.Reconciler.HasPendingWork) scope.Reconciler.ContinueReconcile();

            // Assert
            Assert.That(
                (scope.Root.childCount, ReferenceEquals(scope.Root.ElementAt(0), oldRow), s_refSetUps, s_refCleanUps),
                Is.EqualTo((1, false, 2, 1)));
        }

        [Test]
        public void Given_ANodeHeldAcrossRenders_When_ItMovesFromAKeyedFragmentIntoAnUnkeyedOne_Then_ItMountsAFreshElement()
        {
            // Arrange — a wrapper on the new side too, so the walk commits the node rather than the flat diff.
            using var scope = new ReconcilerScope();
            var held = V.Label(key: "row", text: "held", refCallback: TrackRow);
            var oldTree = new VNode[] { V.Fragment(new VNode[] { held }, key: "scope") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var oldRow = scope.Root.ElementAt(0);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Fragment(new VNode[] { held }) });

            // Assert
            Assert.That(
                (scope.Root.childCount, ReferenceEquals(scope.Root.ElementAt(0), oldRow), s_refSetUps, s_refCleanUps),
                Is.EqualTo((1, false, 2, 1)));
        }

        [Test]
        public void Given_ANodeHeldAcrossRenders_When_ItMovesOutOfAKeyedFragmentAndTheSiblingBeforeItLeaves_Then_ItMountsAFreshElement()
        {
            // Arrange — the leading sibling stops the prefix at once, so the held node is met from the tail.
            using var scope = new ReconcilerScope();
            var held = V.Label(key: "row", text: "held", refCallback: TrackRow);
            var oldTree = new VNode[]
            {
                V.Label(key: "leading", text: "leading"),
                V.Fragment(new VNode[] { held }, key: "scope"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var oldRow = scope.Root.ElementAt(1);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { held });

            // Assert
            Assert.That(
                (scope.Root.childCount, ReferenceEquals(scope.Root.ElementAt(0), oldRow), s_refSetUps, s_refCleanUps),
                Is.EqualTo((1, false, 2, 1)));
        }

        // GREEN_ON_BASE(characterization): the base reads a scoped key for the old leaf, so its flat diff
        // is keyed. The old side's keys now come from the walk rather than the node, and this is what fails
        // when the choice between the keyed and the indexed diff stops reading them.
        [Test]
        public void Given_AnUnkeyedLeafInsideAKeyedFragment_When_TheFragmentGivesWayToAnUnkeyedLeaf_Then_ItMountsAFreshElement()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var oldTree = new VNode[]
            {
                V.Fragment(new VNode[] { V.Label(text: "old", refCallback: TrackRow) }, key: "scope"),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var oldRow = scope.Root.ElementAt(0);

            // Act
            scope.Reconciler.Reconcile(scope.Root, oldTree, new VNode[] { V.Label(text: "new", refCallback: TrackRow) });

            // Assert
            Assert.That(
                (scope.Root.childCount, ReferenceEquals(scope.Root.ElementAt(0), oldRow), s_refSetUps, s_refCleanUps),
                Is.EqualTo((1, false, 2, 1)));
        }

        [Component(Compiler = false)]
        private static VNode FirstTitle() => V.Label(key: "title", text: "first", refCallback: TrackRow);

        [Component(Compiler = false)]
        private static VNode SecondTitle() => V.Label(key: "title", text: "second", refCallback: TrackRow);

        [Component(Compiler = false)]
        private static VNode TwoTitles()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "titles", children: new VNode[]
            {
                V.Component(FirstTitle),
                V.Component(SecondTitle),
                V.Label(text: tick.ToString()),
            });
        }

        [Test]
        public void Given_TwoSiblingComponentsEachReturningALeafUnderOneKey_When_TheirParentRendersAgain_Then_EachLeafKeepsItsElement()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(TwoTitles));
            var first = _root.FindLabelByText("first");
            var second = _root.FindLabelByText("second");

            // Act
            s_setTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (_root.Q<VisualElement>("titles").childCount, ReferenceEquals(_root.FindLabelByText("first"), first),
                    ReferenceEquals(_root.FindLabelByText("second"), second), s_refSetUps, s_refCleanUps),
                Is.EqualTo((3, true, true, 2, 0)));
        }
    }
}
