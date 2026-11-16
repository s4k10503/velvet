using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the recorded child-list writes belong to the pass that made them: a pass that starts while
    /// another's are unapplied leaves them where they are, and the one that was recording takes over again where the
    /// other ends.
    /// </summary>
    [TestFixture]
    internal sealed class PassOwnershipTests
    {
        private Reconciler _first = null!;
        private Reconciler _second = null!;

        [SetUp]
        public void SetUp()
        {
            _first = new Reconciler();
            _second = new Reconciler();
        }

        [TearDown]
        public void TearDown()
        {
            ChildListWrites.EndRecording(_second.Context.Pass);
            ChildListWrites.EndRecording(_first.Context.Pass);
            _first.Dispose();
            _second.Dispose();
        }

        [Test]
        public void Given_APassWithARecordedInsert_When_AnotherPassBegins_Then_TheInsertIsNotOnTheLiveListYet()
        {
            // Arrange
            var parent = new VisualElement();
            parent.Add(new VisualElement());
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 1, child);

            // Act
            ChildListWrites.BeginRecording(_second.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base applied the first pass's insert where the second began, so the live
        // list holds the child after the first ends as well.
        [Test]
        public void Given_APassWithARecordedInsert_When_AnotherPassBeginsAndTheFirstEnds_Then_TheInsertIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            parent.Add(new VisualElement());
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 1, child);
            ChildListWrites.BeginRecording(_second.Context.Pass);
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Act
            ChildListWrites.EndRecording(_first.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base applied the insert where the second pass began, so the live parent is
        // the container; here it is the model's, which the enclosing pass holds.
        [Test]
        public void Given_APassWithARecordedInsert_When_AnotherPassReadsTheParentOfTheChild_Then_ItIsTheContainer()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 0, child);
            ChildListWrites.BeginRecording(_second.Context.Pass);

            // Act
            var seen = ChildListWrites.ParentOf(child);

            // Assert
            Assert.That(seen, Is.SameAs(parent));
        }

        [Test]
        public void Given_APassHoldingAContainer_When_AnotherPassInsertsIntoIt_Then_TheInsertWaitsForTheHoldersApply()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            var other = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 0, child);
            ChildListWrites.BeginRecording(_second.Context.Pass);
            _second.Context.HostCommit.Insert(parent, 1, other);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.Zero);
        }

        private static (VisualElement Parent, VisualElement Y) FourRows()
        {
            var parent = new VisualElement();
            var y = new VisualElement();
            parent.Add(new VisualElement());
            parent.Add(new VisualElement());
            parent.Add(y);
            parent.Add(new VisualElement());
            return (parent, y);
        }

        // GREEN_ON_BASE(characterization): the base applied the first pass's insert where the second began, so the
        // second's index named the live row as well.
        [Test]
        public void Given_APassHoldingAnInsertIntoAContainer_When_AnotherPassRemovesARowFromIt_Then_TheRowRemovedIsTheOneNamed()
        {
            // Arrange
            var (parent, y) = FourRows();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 1, new VisualElement());
            ChildListWrites.BeginRecording(_second.Context.Pass);
            _second.Context.HostCommit.RemoveRow(parent, y);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);
            ChildListWrites.EndRecording(_first.Context.Pass);

            // Assert
            Assert.That(y.hierarchy.parent, Is.Null);
        }

        // GREEN_ON_BASE(characterization): as for the removal of a named row.
        [Test]
        public void Given_APassHoldingAnInsertIntoAContainer_When_AnotherPassTakesARowOutToMoveIt_Then_TheRowTakenOutIsTheOneAtTheIndex()
        {
            // Arrange
            var (parent, y) = FourRows();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            _first.Context.HostCommit.Insert(parent, 1, new VisualElement());
            ChildListWrites.BeginRecording(_second.Context.Pass);
            using (ChildListWrites.QueueWrite())
            {
                ChildListWrites.RemoveAtForMove(parent, 3);
            }

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);
            ChildListWrites.EndRecording(_first.Context.Pass);

            // Assert
            Assert.That(y.hierarchy.parent, Is.Null);
        }

        [Test]
        public void Given_APassThatBeginsAgainWhileItApplies_When_BothEnds_Then_NoPassIsRecording()
        {
            // Arrange
            var pass = _first.Context.Pass;
            ChildListWrites.BeginRecording(pass);
            pass.Applying = true;
            ChildListWrites.BeginRecording(pass);
            ChildListWrites.EndRecording(pass);
            pass.Applying = false;

            // Act
            ChildListWrites.EndRecording(pass);

            // Assert
            Assert.That(ChildListWrites.IsRecording, Is.False);
        }

        private static VNode[] ZRow()
            => new VNode[]
            {
                V.Div(name: "parent", className: "relative", children: new VNode[]
                {
                    V.Div(name: "z", className: "absolute z-10"),
                    V.Div(name: "after"),
                }),
            };

        [Test]
        public void Given_APassOfAnotherContextBeginningInsideAWalk_When_ItEnds_Then_ItsRelocationWaitsForTheWalk()
        {
            // Arrange
            var rootB = new VisualElement();
            bool? held = null;
            var treeA = new VNode[]
            {
                V.ScrollView(onCreated: _ =>
                {
                    _second.Reconcile(rootB, System.Array.Empty<VNode>(), ZRow());
                    held = FiberZLayerCoordinator.PlaceholderOf(_second.Context, rootB.Q<VisualElement>("z")) == null;
                }),
            };

            // Act
            _first.Reconcile(new VisualElement(), System.Array.Empty<VNode>(), treeA);

            // Assert
            Assert.That(held, Is.EqualTo(true));
        }

        // GREEN_ON_BASE(characterization): the base settled the nested pass where it ended, ahead of the walk's end.
        [Test]
        public void Given_APassOfAnotherContextBeginningInsideAWalk_When_TheWalksPassEnds_Then_ItsRelocationIsMade()
        {
            // Arrange
            var rootB = new VisualElement();
            var treeA = new VNode[]
            {
                V.ScrollView(onCreated: _ => _second.Reconcile(rootB, System.Array.Empty<VNode>(), ZRow())),
            };

            // Act
            _first.Reconcile(new VisualElement(), System.Array.Empty<VNode>(), treeA);

            // Assert
            Assert.That(FiberZLayerCoordinator.PlaceholderOf(_second.Context, rootB.Q<VisualElement>("z")), Is.Not.Null);
        }

        [Test]
        public void Given_APassOpenInOneContext_When_AnotherContextWithNoPassRestacksAnElement_Then_ItStaysRelocatedUntilThePassEnds()
        {
            // Arrange
            var rootB = new VisualElement();
            _second.Reconcile(rootB, System.Array.Empty<VNode>(), ZRow());
            var z = rootB.Q<VisualElement>("z");
            ChildListWrites.BeginRecording(_first.Context.Pass);

            // Act
            FiberZLayerCoordinator.Restack(_second.Context, z, null);

            // Assert
            Assert.That(FiberZLayerCoordinator.PlaceholderOf(_second.Context, z), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base restacked at once, with no pass open, as it does where the open one ends.
        [Test]
        public void Given_ARestackLeftWhileAPassWasOpen_When_ThePassEnds_Then_TheElementIsBackInItsSlot()
        {
            // Arrange
            var rootB = new VisualElement();
            _second.Reconcile(rootB, System.Array.Empty<VNode>(), ZRow());
            var z = rootB.Q<VisualElement>("z");
            ChildListWrites.BeginRecording(_first.Context.Pass);
            FiberZLayerCoordinator.Restack(_second.Context, z, null);

            // Act
            ChildListWrites.EndRecording(_first.Context.Pass);
            FiberZLayerCoordinator.RunDeferred();

            // Assert
            Assert.That(FiberZLayerCoordinator.PlaceholderOf(_second.Context, z), Is.Null);
        }

        [Test]
        public void Given_AnotherPassBeganOverARecordingOne_When_ItEnds_Then_TheOuterPassIsRecordingAgain()
        {
            // Arrange
            ChildListWrites.BeginRecording(_first.Context.Pass);
            ChildListWrites.BeginRecording(_second.Context.Pass);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Assert
            Assert.That(ChildListWrites.IsRecording, Is.True);
        }

        [Test]
        public void Given_AnotherPassBeganOverARecordingOne_When_ItEnds_Then_ItsInsertIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.BeginRecording(_first.Context.Pass);
            ChildListWrites.BeginRecording(_second.Context.Pass);
            _second.Context.HostCommit.Insert(parent, 0, child);

            // Act
            ChildListWrites.EndRecording(_second.Context.Pass);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base writes at once outside a pass as well; the case pins that the
        // pass-owned state leaves that answer where it was.
        [Test]
        public void Given_NoPassIsOpen_When_AnInsertIsMadeThroughTheQueue_Then_ItIsOnTheLiveList()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();

            // Act
            _first.Context.HostCommit.Insert(parent, 0, child);

            // Assert
            Assert.That(parent.childCount, Is.EqualTo(1));
        }
    }
}
