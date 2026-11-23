using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a pass takes the rows it removes from a container it did not create: out of the model at once
    /// and out of the live list where the pass ends, with a removed pooled element reset and pooled after that and not
    /// handed to a row of the same pass.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedRemovalTests
    {
        private Reconciler _reconciler = null!;
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            VNodePoolTestAccess.ClearLabelPoolForTest();
            _reconciler = new Reconciler();
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            ChildListWrites.EndRecording();
            _reconciler.Dispose();
            VNodePoolTestAccess.ClearLabelPoolForTest();
        }

        private static VNode Branch(string name, params VNode[] children)
            => V.Div(key: name, name: name, children: children);

        private static string NamesOf(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        // Two siblings reconciled one after the other: the first loses rows, and the second creates the row whose
        // callback reads what the first holds while the pass goes on.
        private (VNode[] Old, VisualElement Left) MountTwoBranches(params VNode[] leftRows)
        {
            var oldTree = new[] { Branch("left", leftRows), Branch("right") };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            return (oldTree, _root.Q<VisualElement>("left"));
        }

        [Test]
        public void Given_ARowRemovedFromAContainer_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsTheRow()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Div(name: "a"), V.Div(name: "b"));
            var seen = -1;
            var newTree = new[]
            {
                Branch("left", V.Div(name: "a")),
                Branch("right", V.ScrollView(onCreated: _ => seen = left.childCount)),
            };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(seen, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base has taken the row out of the model by then, and so does the change.
        [Test]
        public void Given_ARowRemovedFromAContainer_When_ALaterRowIsCreatedInThePass_Then_TheModelHoldsOnlyTheRemainingRow()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Div(name: "a"), V.Div(name: "b"));
            var seen = -1;
            var newTree = new[]
            {
                Branch("left", V.Div(name: "a")),
                Branch("right", V.ScrollView(onCreated: _ => seen = ChildListWrites.ChildCount(left))),
            };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(seen, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base has removed the row as the walk reached it; what the case pins is
        // that the live list has lost it once the reconcile returns, where the removal waits for the end of the pass.
        [Test]
        public void Given_ARowRemovedFromAContainer_When_TheReconcileReturns_Then_TheLiveListHoldsOnlyTheRemainingRow()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Div(name: "a"), V.Div(name: "b"));

            // Act
            _reconciler.Reconcile(_root, oldTree, new[] { Branch("left", V.Div(name: "a")), Branch("right") });

            // Assert
            Assert.That(NamesOf(left), Is.EqualTo("a"));
        }

        // A replaced row is removed, its replacement is recorded, and the row behind it is removed: the removal behind
        // the recorded insert applies nothing early.
        [Test]
        public void Given_AReplacedRowAndARemovedRowBehindIt_When_ALaterRowIsCreatedInThePass_Then_TheLiveListStillHoldsAllThreeOldRows()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Div(name: "a"), V.Div(name: "b"), V.Div(name: "c"));
            var seen = -1;
            var newTree = new[]
            {
                Branch("left", V.Div(name: "a"), V.Text("x")),
                Branch("right", V.ScrollView(onCreated: _ => seen = left.childCount)),
            };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(seen, Is.EqualTo(3));
        }

        [Test]
        public void Given_ARemovedLabel_When_ALaterRowIsCreatedInThePass_Then_TheLabelIsNotResetYet()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Text("kept text"));
            var removed = (Label)left[0];
            var seen = "unset";
            var newTree = new[]
            {
                Branch("left"),
                Branch("right", V.ScrollView(onCreated: _ => seen = removed.text)),
            };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(seen, Is.EqualTo("kept text"));
        }

        [Test]
        public void Given_ARemovedLabel_When_ALaterRowOfThePassRentsALabel_Then_ItIsNotTheRemovedOne()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Text("old"));
            var removed = (Label)left[0];
            var newTree = new[] { Branch("left"), Branch("right", V.Text("new")) };

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(ReferenceEquals(_root.Q<VisualElement>("right")[0], removed), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base pools the label as it removes it; what the case pins is that the
        // change still pools it, once the pass has landed the removal.
        [Test]
        public void Given_ARemovedLabel_When_TheReconcileReturns_Then_ItIsTheNextOneRented()
        {
            // Arrange
            var (oldTree, left) = MountTwoBranches(V.Text("old"));
            var removed = (Label)left[0];
            _reconciler.Reconcile(_root, oldTree, new[] { Branch("left"), Branch("right") });

            // Act
            var rented = VNodePool.RentLabel("next");

            // Assert
            Assert.That(ReferenceEquals(rented, removed), Is.True);
        }

        [Test]
        public void Given_ARemovedRowWaitingForThePassToEnd_When_ItsReconcilerIsDisposed_Then_NoReturnIsLeftQueued()
        {
            // Arrange
            var owner = new Reconciler();
            var parent = new VisualElement();
            var label = new Label("x");
            parent.Add(label);
            ChildListWrites.BeginRecording();
            owner.Context.HostCommit.Remove(parent, label);

            // Act
            owner.Dispose();

            // Assert
            Assert.That(owner.Context.PendingPoolReturns.Count, Is.Zero);
        }
    }
}
