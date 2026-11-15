using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a container's rendered slots sit among its invisible children: that a slot past the last
    /// rendered child is empty at the append position ahead of a trailing invisible child, that a slot below another is
    /// found across an invisible child and a negative one is empty, and that <see cref="HostCommitQueue.InsertAtSlot"/>
    /// puts a child where <see cref="LogicalChildSlots.ToPhysical"/> does when the container was changed without
    /// going through the row model.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="ContainerRowsTests"/>, which compares the model with a walk of its own over random
    /// sequences of writes made through <c>ChildListWrites</c>: these cases change the container directly, so each
    /// answer is taken from a model that has to notice the change first.
    /// </remarks>
    [TestFixture]
    internal sealed class SlotAddressingTests
    {
        private Reconciler _reconciler = null!;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private HostCommitQueue Host => _reconciler.Context.HostCommit;

        private static VisualElement Named(string name) => new VisualElement { name = name };

        private static VisualElement Marked(string name, string markerClass)
        {
            var child = Named(name);
            child.AddToClassList(markerClass);
            return child;
        }

        private static VisualElement Container(int rendered)
        {
            var container = new VisualElement();
            for (var i = 0; i < rendered; i++) container.Add(Named("r" + i));
            return container;
        }

        private static string Names(VisualElement container)
        {
            var names = new string[container.childCount];
            for (var i = 0; i < names.Length; i++) names[i] = container[i].name;
            return string.Join(",", names);
        }

        // GREEN_ON_BASE(characterization): the base answers the slot past the last rendered child by its own walk. What
        // this pins is that the model's answer stops after the last rendered child rather than at the end of the list.
        [Test]
        public void Given_ATrailingSpacer_When_ASlotPastTheLastRenderedChildIsAsked_Then_ItIsEmptyAtTheAppendPositionAheadOfTheSpacer()
        {
            // Arrange — the spacer puts the append position (3) short of childCount (4).
            var container = Container(3);
            container.Add(Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));
            LogicalChildSlots.TryGetPhysical(container, 1, out _);

            // Act
            var occupied = LogicalChildSlots.TryGetPhysical(container, 5, out var physical);

            // Assert — NaN stands where the slot was reported occupied, which no physical index equals.
            Assert.That(occupied ? float.NaN : physical, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base answers a slot across an invisible child by its own walk.
        [Test]
        public void Given_ASpacerBetweenRenderedChildren_When_ASlotBelowAnotherIsAsked_Then_ItIsFoundAcrossTheSpacer()
        {
            // Arrange — r0 r1 spacer r2 r3: slot 1 is physical 1.
            var container = Container(4);
            container.Insert(2, Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));
            LogicalChildSlots.TryGetPhysical(container, 3, out _);

            // Act
            var found = LogicalChildSlots.TryGetPhysical(container, 1, out var physical);

            // Assert — NaN stands where the slot was reported empty.
            Assert.That(found ? physical : float.NaN, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base answers a negative slot as empty by its own walk.
        [Test]
        public void Given_ThreeRenderedChildren_When_ANegativeSlotIsAsked_Then_ItIsEmpty()
        {
            // Arrange
            var container = Container(3);
            LogicalChildSlots.TryGetPhysical(container, 2, out _);

            // Act
            var occupied = LogicalChildSlots.TryGetPhysical(container, -1, out _);

            // Assert
            Assert.That(occupied, Is.False);
        }

        [Test]
        public void Given_ATrailingSpacer_When_RowsAreInsertedAtAscendingSlotsPastTheLastRenderedChild_Then_EachLandsAheadOfTheSpacer()
        {
            // Arrange
            var container = Container(2);
            container.Add(Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));

            // Act
            Host.InsertAtSlot(container, 2, Named("n2"));
            Host.InsertAtSlot(container, 3, Named("n3"));
            Host.InsertAtSlot(container, 4, Named("n4"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("r0,r1,n2,n3,n4,spacer"));
        }

        [Test]
        public void Given_RenderedChildrenBeyondTheSlots_When_RowsAreInsertedAtAscendingSlotsAmongThem_Then_EachLandsBeforeTheChildHoldingItsSlot()
        {
            // Arrange
            var container = Container(4);

            // Act
            Host.InsertAtSlot(container, 1, Named("n1"));
            Host.InsertAtSlot(container, 2, Named("n2"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("r0,n1,n2,r1,r2,r3"));
        }

        [Test]
        public void Given_AnInsetBandHostedAfterTheLastInsertedChild_When_TheNextSlotIsInserted_Then_ItLandsAfterTheBand()
        {
            // Arrange — the band stays ahead of what is appended, as ToPhysical places an append.
            var container = new VisualElement();
            Host.InsertAtSlot(container, 0, Named("n0"));
            container.Add(Marked("band", RingOverlay.InsetMarkerClass));

            // Act
            Host.InsertAtSlot(container, 1, Named("n1"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("n0,band,n1"));
        }

        [Test]
        public void Given_ASpacerInsertedAheadOfTheInsertedRows_When_ASlotPastTheLastRenderedChildIsInserted_Then_ItLandsAfterTheLastRenderedChild()
        {
            // Arrange — the spacer moves every row one place on.
            var container = Container(2);
            Host.InsertAtSlot(container, 2, Named("n2"));
            container.Insert(0, Marked("spacer", SilhouetteBoundsSpacer.MarkerClass));

            // Act
            Host.InsertAtSlot(container, 3, Named("n3"));

            // Assert
            Assert.That(Names(container), Is.EqualTo("spacer,r0,r1,n2,n3"));
        }
    }
}
