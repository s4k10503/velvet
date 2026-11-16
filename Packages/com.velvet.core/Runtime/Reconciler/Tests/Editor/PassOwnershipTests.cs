using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass a root asks for while another is open waits for it, and that a pass that begins while
    /// the pass of its own context applies begins nothing.
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

        private MountedTree? _mounted;
        private static int s_heldRenders;

        [Component]
        private static VNode CountsItsRenders()
        {
            s_heldRenders++;
            return V.Div(name: "b");
        }

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

        // GREEN_ON_BASE(characterization): the base rendered the root where it was asked for, once.
        [Test]
        public void Given_AHeldMountThatWasMade_When_AnotherPassEnds_Then_TheRootIsNotRenderedAgain()
        {
            // Arrange
            var host = new VisualElement();
            s_heldRenders = 0;
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[]
            {
                V.ScrollView(onCreated: _ => _mounted = V.Mount(host, V.Component(CountsItsRenders))),
            });
            var rendered = s_heldRenders;

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[] { V.Div() });
            var again = s_heldRenders;
            _mounted?.Dispose();

            // Assert
            Assert.That(again, Is.EqualTo(rendered));
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
