using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// The event-array pool at lengths above one, which an element factory rents when its own callback
    /// binding goes ahead of a caller's <c>events:</c> array. <see cref="VNodePoolTests"/> owns the
    /// one-slot cases and what the pool does with an array it did not rent. Each case hands back what it
    /// rents, so the rented-out set does not grow across fixtures.
    /// </summary>
    [TestFixture]
    internal sealed class VNodePoolEventArrayTests
    {
        private const string MaxPoolSizeFieldName = "MaxPoolSize";

        private static int MaxPoolSize()
        {
            var field = typeof(VNodePool).GetField(MaxPoolSizeFieldName, BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(VNodePool).FullName, MaxPoolSizeFieldName);
            }
            return (int)field.GetValue(null)!;
        }

        [Test]
        public void Given_AReturnedFiveSlotArrayHoldingBindings_When_FiveSlotsAreRentedAgain_Then_TheSameArrayComesBackWithEverySlotCleared()
        {
            // Arrange — a length no other case rents, so no earlier case can have filled its stack.
            var array = VNodePool.RentEventArray(5);
            for (var i = 0; i < array.Length; i++) array[i] = new ClickedBinding { Handler = () => { } };
            VNodePool.ReturnEventArray(array);

            // Act
            var reused = VNodePool.RentEventArray(5);
            var observed = (ReferenceEquals(reused, array), reused.All(binding => binding == null));
            VNodePool.ReturnEventArray(reused);

            // Assert
            Assert.That(observed, Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_MoreFourSlotArraysReturnedThanThePoolKeeps_When_AsManyAreRentedBack_Then_TheOnesItTurnedAwayAreNewArrays()
        {
            // Arrange — two over the cap, so the boundary is read on both sides of it.
            var count = MaxPoolSize() + 2;
            var returned = new FiberEventBinding[count][];
            for (var i = 0; i < count; i++) returned[i] = VNodePool.RentEventArray(4);
            foreach (var array in returned) VNodePool.ReturnEventArray(array);

            // Act
            var rented = new FiberEventBinding[count][];
            for (var i = 0; i < count; i++) rented[i] = VNodePool.RentEventArray(4);
            var fresh = rented.Count(array => !returned.Contains(array));
            foreach (var array in rented) VNodePool.ReturnEventArray(array);

            // Assert
            Assert.That(fresh, Is.EqualTo(2));
        }

        [Test]
        public void Given_AReturnedThreeSlotArray_When_TwoSlotsAreRented_Then_TheArrayHandedOutHasTwoSlots()
        {
            // Arrange
            VNodePool.ReturnEventArray(VNodePool.RentEventArray(3));

            // Act
            var rented = VNodePool.RentEventArray(2);
            var length = rented.Length;
            VNodePool.ReturnEventArray(rented);

            // Assert
            Assert.That(length, Is.EqualTo(2));
        }
    }
}
