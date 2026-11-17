using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass a root asks for while another is open waits for it.
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
            _mounted?.Dispose();
            _mounted = null;
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

        [Test]
        public void Given_AMountAskedForInsideAWalk_When_TheRootIsDisposedBeforeThePassEnds_Then_ItNeverRenders()
        {
            // Arrange
            s_heldRenders = 0;
            var tree = new VNode[]
            {
                V.ScrollView(onCreated: _ =>
                {
                    _mounted = V.Mount(new VisualElement(), V.Component(CountsItsRenders));
                    _mounted.Dispose();
                }),
            };

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_heldRenders, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base rendered the root where it was asked for, inside the walk. The case
        // pins that holding that render still makes it before control returns, rather than leaving it for a frame.
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

        private VNode[] TreeAskingForHeldWork(Action<FiberBatchScheduler> ask)
            => new VNode[] { V.ScrollView(onCreated: _ => ask(_second.Context.BatchScheduler)) };

        private MountedTree? _inner;

        // GREEN_ON_BASE(characterization): the base holds the unmount a ref cleanup asks for in the walk.
        // It runs once the entry that ran the pass has ended, as on the branch.
        [Test]
        public void Given_ARefCleanupThatDisposesAnotherRootInTheWalk_When_ThePassEnds_Then_TheOtherRootIsDisposed()
        {
            // Arrange
            VNode Row() => V.Div(name: "row", refCallback: element =>
            {
                _inner = V.Mount(element, V.Label(text: "inner"));
                return () => _inner.Dispose();
            });
            var parent = new VisualElement();
            var before = new VNode[] { V.Div(name: "list", children: new VNode[] { Row() }) };
            _first.Reconcile(parent, Array.Empty<VNode>(), before);
            var shown = _inner;

            // Act
            _first.Reconcile(parent, before, new VNode[] { V.Div(name: "list") });

            // Assert
            Assert.That(shown!.Root.IsDisposed, Is.True);
        }

        private static StateUpdater<int> s_setCounted;
        private static int s_countedRenders;

        [Component]
        private static VNode CountsItsStateRenders()
        {
            var (_, set) = Hooks.UseState(0);
            s_setCounted = set;
            s_countedRenders++;
            return V.Div();
        }

        // GREEN_ON_BASE(characterization): the base rendered the other root once, inside the walk that asked for it.
        // The case pins that holding the two flushes keeps each, rather than letting the first stand for both.
        [Test]
        public void Given_FlushesOfTwoContextsAskedForInsideAWalk_When_ThePassEnds_Then_TheSecondHasRendered()
        {
            // Arrange
            using var other = V.Mount(new VisualElement(), V.Component(CountsItsStateRenders));
            var otherScheduler = other.Root.Reconciler!.Context.BatchScheduler;
            var before = s_countedRenders;
            var tree = TreeAskingForHeldWork(owner =>
            {
                owner.FlushImmediate();
                s_setCounted.Invoke(1);
                otherScheduler.FlushImmediate();
            });

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(s_countedRenders - before, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base flushed each request at once, with nothing queued to render. The case
        // pins that sixty requests held for one scheduler are held once, below the bound on a root's held work.
        [Test]
        public void Given_AFlushAskedForMoreTimesThanTheNestedUpdateLimitInsideAWalk_When_ThePassEnds_Then_NothingIsThrown()
        {
            // Arrange
            var tree = TreeAskingForHeldWork(owner =>
            {
                for (var i = 0; i < 60; i++) owner.FlushImmediate();
            });

            // Act
            TestDelegate act = () => _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(act, Throws.Nothing);
        }

        // GREEN_ON_BASE(characterization): the base rendered each root where it was asked for, so the second has rendered
        // by the time its pass returns.
        [Test]
        public void Given_TwoPassesThatEachHoldAMount_When_TheSecondEnds_Then_ItsRootHasRendered()
        {
            // Arrange
            var firstHost = new VisualElement();
            var secondHost = new VisualElement();
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), TreeMountingARootInsideTheWalk(firstHost));
            using var firstRoot = _mounted;

            // Act
            _first.Reconcile(new VisualElement(), Array.Empty<VNode>(), TreeMountingARootInsideTheWalk(secondHost));
            var rendered = secondHost.childCount;
            _mounted?.Dispose();

            // Assert
            Assert.That(rendered, Is.EqualTo(1));
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
