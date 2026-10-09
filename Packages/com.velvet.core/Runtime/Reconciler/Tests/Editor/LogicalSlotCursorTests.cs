using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="LogicalSlotCursor"/> answers as <see cref="LogicalChildSlots.TryGetPhysical"/>
    /// does when the container changed under the slot it remembers, and when the slot asked for lies past
    /// the last rendered child.
    /// <list type="bullet">
    /// <item>A child inserted ahead of that slot shifts every later slot's physical index.</item>
    /// <item>A container shrunk to that slot's physical index reports the slot empty.</item>
    /// <item>A slot past the last rendered child is reported empty, at the static walk's append position, which
    /// sits ahead of a trailing invisible child rather than at the end of the child list.</item>
    /// </list>
    /// <see cref="GeneralPathSlotLookupScalingTests"/> holds that the cursor walks on rather than from the start.
    /// </summary>
    [TestFixture]
    internal sealed class LogicalSlotCursorTests
    {
        private static VisualElement Container(int rendered)
        {
            var container = new VisualElement();
            for (var i = 0; i < rendered; i++) container.Add(new VisualElement { name = "r" + i });
            return container;
        }

        [Test]
        public void Given_AChildInsertedAheadOfTheRememberedSlot_When_ALaterSlotIsAsked_Then_ItIsFoundPastTheInsert()
        {
            // Arrange — slot 3 is "r3", physical 4 once an invisible child leads the list.
            var container = Container(4);
            var cursor = new LogicalSlotCursor();
            cursor.TryGetPhysical(container, 2, out _);
            var invisible = new VisualElement();
            invisible.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Insert(0, invisible);

            // Act
            var occupied = cursor.TryGetPhysical(container, 3, out var physical);

            // Assert
            Assert.That((occupied, physical), Is.EqualTo((true, 4)));
        }

        [Test]
        public void Given_AContainerShrunkToTheRememberedSlot_When_ThatSlotIsAsked_Then_ItIsEmpty()
        {
            // Arrange — removing the first child leaves three, so physical 3 is past the end.
            var container = Container(4);
            var cursor = new LogicalSlotCursor();
            cursor.TryGetPhysical(container, 3, out _);
            container.RemoveAt(0);

            // Act
            var occupied = cursor.TryGetPhysical(container, 3, out var physical);

            // Assert
            Assert.That((occupied, physical), Is.EqualTo((false, 3)));
        }

        [Test]
        public void Given_ARememberedSlot_When_ASlotPastTheLastRenderedChildIsAsked_Then_ItIsEmptyAtTheAppendPositionAheadOfATrailingSpacer()
        {
            // Arrange — a trailing invisible child puts the append position (3) short of childCount (4).
            var container = Container(3);
            var trailing = new VisualElement();
            trailing.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
            container.Add(trailing);
            var cursor = new LogicalSlotCursor();
            cursor.TryGetPhysical(container, 1, out _);

            // Act
            var occupied = cursor.TryGetPhysical(container, 5, out var physical);

            // Assert
            Assert.That((occupied, physical), Is.EqualTo((false, 3)));
        }
    }
}
