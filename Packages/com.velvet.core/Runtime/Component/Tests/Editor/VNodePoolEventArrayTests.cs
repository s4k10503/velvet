using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// The event-array pool at lengths above one, which an element factory rents when its own callback
    /// binding goes ahead of a caller's <c>events:</c> array. <see cref="VNodePoolTests"/> owns the
    /// one-slot cases and what the pool does with an array it did not rent.
    /// </summary>
    [TestFixture]
    internal sealed class VNodePoolEventArrayTests
    {
        [Test]
        public void Given_AReturnedFiveSlotArrayHoldingBindings_When_FiveSlotsAreRentedAgain_Then_TheSameArrayComesBackWithEverySlotCleared()
        {
            // Arrange — a length no other case rents, so no earlier case can have filled its stack.
            var array = VNodePool.RentEventArray(5);
            for (var i = 0; i < array.Length; i++) array[i] = new ClickedBinding { Handler = () => { } };
            VNodePool.ReturnEventArray(array);

            // Act
            var reused = VNodePool.RentEventArray(5);

            // Assert
            Assert.That((ReferenceEquals(reused, array), reused.All(binding => binding == null)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TenFourSlotArraysReturned_When_TenAreRentedBack_Then_TheTwoTheCapTurnedAwayAreNewArrays()
        {
            // Arrange
            var returned = new FiberEventBinding[10][];
            for (var i = 0; i < returned.Length; i++) returned[i] = VNodePool.RentEventArray(4);
            foreach (var array in returned) VNodePool.ReturnEventArray(array);

            // Act
            var rented = new FiberEventBinding[10][];
            for (var i = 0; i < rented.Length; i++) rented[i] = VNodePool.RentEventArray(4);

            // Assert — eight come back from the pool, which keeps that many arrays of one length.
            Assert.That(rented.Count(array => !returned.Contains(array)), Is.EqualTo(2));
        }

        [Test]
        public void Given_AReturnedThreeSlotArray_When_TwoSlotsAreRented_Then_TheArrayHandedOutHasTwoSlots()
        {
            // Arrange
            VNodePool.ReturnEventArray(VNodePool.RentEventArray(3));

            // Act
            var rented = VNodePool.RentEventArray(2);

            // Assert
            Assert.That(rented.Length, Is.EqualTo(2));
        }
    }
}
