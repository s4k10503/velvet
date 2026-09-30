using System.Collections.Generic;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the pool's rental journal, which an auto-memoized component's cache hit disowns: it records only
    /// what is rented inside a journal window, and it keeps a bounded capacity once the outermost window closes.
    /// <c>FiberBeginWork.Render</c> opens the window around a component body; these cases open it directly.
    /// </summary>
    [TestFixture]
    internal sealed class VNodePoolRentalJournalTests
    {
        private const int RentalsPastTheCap = 300;

        [Test]
        public void Given_AWindowThatHasClosed_When_ALabelRentsItsProps_Then_TheJournalDoesNotGrow()
        {
            // Arrange — a window that has opened and closed, as a finished render leaves the journal.
            VNodePool.BeginRentalJournal();
            VNodePool.EndRentalJournal();
            var before = VNodePoolTestAccess.RentalJournalCountForTest;

            // Act
            var outside = V.Label(text: "outside");
            var grown = VNodePoolTestAccess.RentalJournalCountForTest - before;
            VNodePool.ReturnProps(outside.Props);

            // Assert
            Assert.That(grown, Is.EqualTo(0),
                "A rental outside any window is not journaled, so a later hit cannot disown it");
        }

        [Test]
        public void Given_AWindowRentingPastTheJournalCap_When_ItCloses_Then_TheJournalKeepsNoMoreThanTheCap()
        {
            // Arrange
            var labels = new List<ElementNode>();

            // Act
            VNodePool.BeginRentalJournal();
            try
            {
                for (var i = 0; i < RentalsPastTheCap; i++) labels.Add(V.Label(text: "row"));
            }
            finally
            {
                VNodePool.EndRentalJournal();
            }
            var capacity = VNodePoolTestAccess.RentalJournalCapacityForTest;
            foreach (var label in labels) VNodePool.ReturnProps(label.Props);

            // Assert
            Assert.That(capacity, Is.AtMost(256),
                "The window journaled 300 rentals, and its close trims the journal back to the cap");
        }
    }
}
