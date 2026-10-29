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
    /// <para>
    /// <see cref="LogicalSlotCursor.Insert"/> puts a child where <see cref="LogicalChildSlots.ToPhysical"/> does,
    /// whether or not the slot it remembers still describes the container.
    /// </para>
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

        private static VisualElement Named(string name) => new VisualElement { name = name };

        private static VisualElement Marked(string name, string markerClass)
        {
            var child = Named(name);
            child.AddToClassList(markerClass);
            return child;
        }

        private static string Names(VisualElement container)
        {
            var names = new string[container.childCount];
            for (var i = 0; i < names.Length; i++) names[i] = container[i].name;
            return string.Join(",", names);
        }

        [Test]
        public void Given_ATrailingSpacer_When_RowsAreInsertedAtAscendingSlotsPastTheLastRenderedChild_Then_EachLandsAheadOfTheSpacer()
        {
            // Arrange
            var container = Container(2);
            container.Add(Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));
            var cursor = new LogicalSlotCursor();

            // Act
            cursor.Insert(container, 2, Named("n2"));
            cursor.Insert(container, 3, Named("n3"));
            cursor.Insert(container, 4, Named("n4"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("r0,r1,n2,n3,n4,spacer"));
        }

        [Test]
        public void Given_RenderedChildrenBeyondTheSlots_When_RowsAreInsertedAtAscendingSlotsAmongThem_Then_EachLandsBeforeTheChildHoldingItsSlot()
        {
            // Arrange
            var container = Container(4);
            var cursor = new LogicalSlotCursor();

            // Act
            cursor.Insert(container, 1, Named("n1"));
            cursor.Insert(container, 2, Named("n2"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("r0,n1,n2,r1,r2,r3"));
        }

        [Test]
        public void Given_AnInsetBandHostedAfterTheLastInsertedChild_When_TheNextSlotIsInserted_Then_ItLandsAfterTheBand()
        {
            // Arrange — the band stays ahead of what is appended, as ToPhysical places an append.
            var container = new VisualElement();
            var cursor = new LogicalSlotCursor();
            cursor.Insert(container, 0, Named("n0"));
            container.Add(Marked("band", RingOverlay.InsetMarkerClass));

            // Act
            cursor.Insert(container, 1, Named("n1"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("n0,band,n1"));
        }

        [Test]
        public void Given_AnInvisibleChildInsertedAheadOfTheRememberedSlot_When_ASlotPastTheLastRenderedChildIsInserted_Then_ItLandsAfterTheLastRenderedChild()
        {
            // Arrange — the spacer moves the remembered child one place on, so it is not where the cursor left it.
            var container = Container(2);
            var cursor = new LogicalSlotCursor();
            cursor.Insert(container, 2, Named("n2"));
            container.Insert(0, Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));

            // Act
            cursor.Insert(container, 3, Named("n3"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("spacer,r0,r1,n2,n3"));
        }
    }
}
