using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the registry reads off its own tables of mounted fibers: the order it gives the components in
    /// a container's reordered rows, and which fibers a teardown under an element and a discarded suspended mount
    /// select. Each case writes the tables directly, so the fibers it arranges are the only ones registered.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentRegistryTableTests
    {
        private static IDictionary Table(ComponentRegistry registry, string name)
            => (IDictionary)typeof(ComponentRegistry).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(registry);

        private static void RegisterWrapper(ComponentRegistry registry, ComponentFiber fiber, VisualElement wrapper)
        {
            Table(registry, "_wrapperIndex")[wrapper] = fiber;
            (VisualElement, object) info = (wrapper, new object());
            Table(registry, "_wrapperFiberInfo")[fiber] = info;
        }

        private static void RegisterInline(ComponentRegistry registry, ComponentFiber fiber, VisualElement container)
        {
            (ComponentFiber, VisualElement, VisualElement, object, object) key =
                (fiber.Parent, null, container, new object(), new object());
            Table(registry, "_inlineFiberToKey")[fiber] = key;
        }

        private static (VisualElement Container, VisualElement RowA, VisualElement RowB) TwoRows()
        {
            var container = new VisualElement();
            var rowA = new VisualElement();
            var rowB = new VisualElement();
            container.Add(rowA);
            container.Add(rowB);
            return (container, rowA, rowB);
        }

        // GREEN_ON_BASE(characterization): the base already gives this selection or order; no case asserted it before.
        [Test]
        public void Given_AWrapperMountedComponentInALaterRow_When_TheRowsCommitTheirOrder_Then_ItFollowsTheComponentPlacedInAnEarlierRow()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var (container, rowA, rowB) = TwoRows();
            var wrapper = new VisualElement();
            rowB.Add(wrapper);
            var parent = new ComponentFiber();
            var wrapped = new ComponentFiber();
            var placed = new ComponentFiber();
            parent.AppendChild(wrapped);
            parent.AppendChild(placed);
            wrapped.MountPoint = wrapper;
            RegisterWrapper(registry, wrapped, wrapper);
            var rows = new List<(VisualElement element, bool isExisting)> { (rowA, true), (rowB, true) };
            var placements = new List<(ComponentFiber Fiber, int FirstRow, int Rows)> { (placed, 0, 1) };

            // Act
            registry.CommitChildOrderToRows(container, rows, placements);

            // Assert
            Assert.That(parent.Child, Is.SameAs(placed));
        }

        // GREEN_ON_BASE(characterization): the base already gives this selection or order; no case asserted it before.
        [Test]
        public void Given_AComponentPlacedAtARowAndOneMountedInsideIt_When_TheRowsCommitTheirOrder_Then_ThePlacedOneComesFirst()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var (container, rowA, rowB) = TwoRows();
            var inner = new VisualElement();
            rowA.Add(inner);
            var parent = new ComponentFiber();
            var nested = new ComponentFiber();
            var placed = new ComponentFiber();
            parent.AppendChild(nested);
            parent.AppendChild(placed);
            nested.MountPoint = inner;
            RegisterInline(registry, nested, inner);
            var rows = new List<(VisualElement element, bool isExisting)> { (rowA, true), (rowB, true) };
            var placements = new List<(ComponentFiber Fiber, int FirstRow, int Rows)> { (placed, 0, 0) };

            // Act
            registry.CommitChildOrderToRows(container, rows, placements);

            // Assert
            Assert.That(parent.Child, Is.SameAs(placed));
        }

        // GREEN_ON_BASE(characterization): the base already gives this selection or order; no case asserted it before.
        [Test]
        public void Given_AWrapperFiberIndexedUnderAnElementItNoLongerMountsOn_When_ThatElementsSubtreeIsDisposed_Then_TheFiberIsNot()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var root = new VisualElement();
            var element = new VisualElement();
            root.Add(element);
            var stale = new ComponentFiber();
            RegisterWrapper(registry, stale, element);

            // Act
            registry.DisposeFibersUnder(root);

            // Assert
            Assert.That(stale.IsDisposed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already gives this selection or order; no case asserted it before.
        [Test]
        public void Given_ASuspendedMountBesideAFiberAnEarlierDiscardFlagged_When_TheMountIsDiscarded_Then_TheFlaggedFiberIsNotDisposed()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var root = new VisualElement();
            var readerElement = new VisualElement();
            var flaggedElement = new VisualElement();
            root.Add(readerElement);
            root.Add(flaggedElement);
            var reader = new ComponentFiber { MountPoint = readerElement };
            var flagged = new ComponentFiber { MountPoint = flaggedElement, MountDiscarded = true };
            RegisterWrapper(registry, reader, readerElement);
            RegisterWrapper(registry, flagged, flaggedElement);

            // Act
            registry.DiscardSuspendedMountUnder(root, reader);

            // Assert
            Assert.That(flagged.IsDisposed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base already gives this selection or order; no case asserted it before.
        [Test]
        public void Given_ASuspendedMountBesideAFiberOutsideTheReadersChain_When_TheMountIsDiscarded_Then_ThatFiberIsDisposed()
        {
            // Arrange
            var registry = new ComponentRegistry(new ReconcilerContext());
            var root = new VisualElement();
            var readerElement = new VisualElement();
            var otherElement = new VisualElement();
            root.Add(readerElement);
            root.Add(otherElement);
            var reader = new ComponentFiber { MountPoint = readerElement };
            var other = new ComponentFiber { MountPoint = otherElement };
            RegisterWrapper(registry, reader, readerElement);
            RegisterWrapper(registry, other, otherElement);

            // Act
            registry.DiscardSuspendedMountUnder(root, reader);

            // Assert
            Assert.That(other.IsDisposed, Is.True);
        }
    }
}
