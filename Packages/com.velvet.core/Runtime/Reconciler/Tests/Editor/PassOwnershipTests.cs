using System;
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

        private MountedTree? _mounted;

        private VNode[] TreeMountingARootInsideTheWalk(VisualElement host, Action<VisualElement>? andThen = null)
            => new VNode[]
            {
                V.ScrollView(onCreated: scroll =>
                {
                    _mounted = V.Mount(host, V.Div(name: "b"));
                    andThen?.Invoke(scroll);
                }),
            };

        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheWalkEnds_Then_TheRootHasNotRenderedInsideIt()
        {
            // Arrange
            var host = new VisualElement();
            var seen = -1;
            var tree = TreeMountingARootInsideTheWalk(host, _ => seen = host.childCount);

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            _mounted?.Dispose();

            // Assert
            Assert.That(seen, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root and unmounted it, which leaves the host as empty.
        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheRootIsDisposedBeforeThePassEnds_Then_ItNeverRenders()
        {
            // Arrange
            var host = new VisualElement();
            var tree = TreeMountingARootInsideTheWalk(host, _ => _mounted!.Dispose());

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(host.childCount, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where it was asked for, so it holds its row by the
        // time control returns; the case pins that holding the render does not leave it for a frame.
        [Test]
        public void Given_AMountAskedForInsideAWalk_When_ThePassEnds_Then_TheRootHasRenderedBeforeControlReturns()
        {
            // Arrange
            var host = new VisualElement();
            var tree = TreeMountingARootInsideTheWalk(host);

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            var rendered = host.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): as for the pass that ends without a throw.
        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheApplyOfThePassThrows_Then_TheRootHasStillRendered()
        {
            // Arrange
            var host = new VisualElement();
            var patcher = new FiberNodePatcher(_first.Context);
            var tree = TreeMountingARootInsideTheWalk(host, _ => ChildListWrites.RecordPatchPre(
                new PendingPatch(patcher, new Label(), null, new ElementNode(), 0)));

            // Act
            try
            {
                _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);
            }
            catch (NullReferenceException)
            {
            }
            var rendered = host.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
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
