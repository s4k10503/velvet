using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a pass takes a hierarchy write at once into each link of the chain from an element it creates to
    /// the container its children go into, where it records one into an element it did not create behind the pass
    /// (<see cref="ChildListWrites"/> owns which writes are recorded).
    /// The writes come from the constructor of a child created under the element, which runs after the element is
    /// marked and before any child is inserted, and the queue's flag is raised around them as
    /// <c>HostCommitQueue.Insert</c> raises it around its own.
    /// </summary>
    [TestFixture]
    internal sealed class CreatedContainerHierarchyWriteTests
    {
        private class Forwarder : VisualElement
        {
            public Forwarder(VisualElement to)
            {
                To = to;
                hierarchy.Add(to);
            }

            public VisualElement To { get; }

            public override VisualElement contentContainer => To;
        }

        // One link between the element and the container the children go into.
        private sealed class OneHop : Forwarder
        {
            public OneHop() : base(new VisualElement()) => s_target = this;
        }

        // Two links: the element forwards to a Forwarder, which forwards on.
        private sealed class TwoHops : Forwarder
        {
            public TwoHops() : base(new Forwarder(new VisualElement())) => s_target = To;
        }

        private sealed class Probe : VisualElement
        {
            public Probe()
            {
                if (s_target == null) return;
                var written = new VisualElement();
                ChildListWrites.ViaQueue++;
                try { ChildListWrites.HierarchyInsert(s_target, 0, written); }
                finally { ChildListWrites.ViaQueue--; }
                s_parentAtWrite = written.hierarchy.parent;
                ChildListWrites.HierarchyRemove(s_target, written);
            }
        }

        private static VisualElement s_target;
        private static VisualElement s_parentAtWrite;

        [SetUp]
        public void SetUp()
        {
            s_target = null;
            s_parentAtWrite = null;
        }

        // GREEN_ON_BASE(characterization): the base makes at once a write that names an element the pass created while its children are created.
        // Drop `ChildListWrites.MarkCreated(element);` from ReconcileChildrenOfNewElement and the write is recorded
        // behind the pass, which leaves the parent unset.
        [Test]
        public void Given_AnElementForwardingToItsContent_When_AWriteNamesTheElementWhileItsChildrenAreCreated_Then_TheWriteIsMadeAtOnce()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Custom<OneHop>(children: new VNode[] { V.Custom<Probe>() }));

            // Assert
            Assert.That(s_parentAtWrite, Is.SameAs(s_target));
        }

        // GREEN_ON_BASE(characterization): the base makes at once a write that names the first link of the chain an element created by the pass forwards through.
        // Drop `ChildListWrites.MarkCreated(childContainer);` from ReconcileChildrenOfNewElement and the first link
        // is in neither marked set, so the write is recorded behind the pass.
        [Test]
        public void Given_AnElementForwardingThroughTwoLinks_When_AWriteNamesTheFirstLinkWhileItsChildrenAreCreated_Then_TheWriteIsMadeAtOnce()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Custom<TwoHops>(children: new VNode[] { V.Custom<Probe>() }));

            // Assert
            Assert.That(s_parentAtWrite, Is.SameAs(s_target));
        }

        // GREEN_ON_BASE(characterization): the instrument for the two cases above, which read the same write into an element the pass did not create.
        [Test]
        public void Given_AnElementTheMountDidNotCreate_When_AWriteNamesItWhileChildrenAreCreated_Then_TheWriteIsRecordedBehindThePass()
        {
            // Arrange
            var root = new VisualElement();
            s_target = root;

            // Act
            using var mounted = V.Mount(root, V.Custom<VisualElement>(children: new VNode[] { V.Custom<Probe>() }));

            // Assert — a write made at once leaves the parent set, which the recorded one does not until the pass ends
            Assert.That(s_parentAtWrite, Is.Null);
        }
    }
}
