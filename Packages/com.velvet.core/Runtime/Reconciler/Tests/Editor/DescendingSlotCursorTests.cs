using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="DescendingSlotCursor"/> answers as <see cref="LogicalChildSlots.ToPhysical"/>
    /// does for the slots a removal loop asks for, whether or not it can walk on from the slot it resolved last.
    /// <see cref="KeyedListClearScalingTests"/> holds that it walks on rather than from the start.
    /// </summary>
    [TestFixture]
    internal sealed class DescendingSlotCursorTests
    {
        private static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            return spacer;
        }

        // r0 r1 spacer r2 r3: slots 0..3 sit at physical 0, 1, 3, 4.
        private static VisualElement ContainerWithAnInterleavedSpacer()
        {
            var container = new VisualElement();
            container.Add(new VisualElement { name = "r0" });
            container.Add(new VisualElement { name = "r1" });
            container.Add(Spacer());
            container.Add(new VisualElement { name = "r2" });
            container.Add(new VisualElement { name = "r3" });
            return container;
        }

        [Test]
        public void Given_ASpacerBetweenRenderedChildren_When_SlotsAreAskedDownward_Then_EachIsTheStaticWalksAnswer()
        {
            // Arrange
            var container = ContainerWithAnInterleavedSpacer();
            var cursor = new DescendingSlotCursor();

            // Act
            var answers = new[]
            {
                cursor.ToPhysical(container, 3), cursor.ToPhysical(container, 2),
                cursor.ToPhysical(container, 1), cursor.ToPhysical(container, 0),
            };

            // Assert
            Assert.That(answers, Is.EqualTo(new[] { 4, 3, 1, 0 }));
        }

        [Test]
        public void Given_TheResolvedChildRemoved_When_TheSlotBelowIsAsked_Then_ItIsFoundAcrossTheSpacer()
        {
            // Arrange
            var container = ContainerWithAnInterleavedSpacer();
            var cursor = new DescendingSlotCursor();
            container.RemoveAt(cursor.ToPhysical(container, 2));

            // Act
            var physical = cursor.ToPhysical(container, 1);

            // Assert
            Assert.That(physical, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildInsertedAheadOfTheResolvedSlot_When_TheSlotBelowIsAsked_Then_ItIsFoundWhereItNowSits()
        {
            // Arrange — slot 1 is "r1", at physical 2 once a spacer leads the list.
            var container = ContainerWithAnInterleavedSpacer();
            var cursor = new DescendingSlotCursor();
            cursor.ToPhysical(container, 3);
            container.Insert(0, Spacer());

            // Act
            var physical = cursor.ToPhysical(container, 1);

            // Assert
            Assert.That(physical, Is.EqualTo(2));
        }

        [Test]
        public void Given_ASlotPastTheLastRenderedChild_When_ItIsAsked_Then_ItIsTheAppendPositionAndTheNextSlotIsStillFound()
        {
            // Arrange
            var container = ContainerWithAnInterleavedSpacer();
            var cursor = new DescendingSlotCursor();

            // Act
            var answers = (cursor.ToPhysical(container, 9), cursor.ToPhysical(container, 3));

            // Assert
            Assert.That(answers, Is.EqualTo((5, 4)));
        }
    }
}
