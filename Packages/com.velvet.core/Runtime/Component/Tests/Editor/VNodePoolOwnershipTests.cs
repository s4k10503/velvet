using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins ownership tracking for the props-bag and single-event-array pools, mirroring the
    /// identity set the VNode?[] pool already keeps. The V.* factories accept caller-supplied
    /// <see cref="FiberElementProps"/> / event arrays that a component may cache and reuse across
    /// renders; retiring an old tree unconditionally returned them, wiping every field of an object
    /// the caller still holds and pushing it into the shared pool where an unrelated mount could
    /// rent and overwrite it. Only instances the pool itself created may be cleared and recycled.
    /// </summary>
    [TestFixture]
    internal sealed class VNodePoolOwnershipTests
    {
        [Test]
        public void Given_UserPropsNotRentedFromPool_When_ReturnProps_Then_ItIsNotCleared()
        {
            // Arrange — a caller-owned props bag, as a component caching props across renders holds.
            var props = new FiberElementProps { Tooltip = "Save" };

            // Act
            VNodePool.ReturnProps(props);

            // Assert — the caller's instance is left untouched.
            Assert.That(props.Tooltip, Is.EqualTo("Save"));
        }

        [Test]
        public void Given_UserPropsWasPassedToReturn_When_AnotherRentFollows_Then_TheUserInstanceIsNotHandedOut()
        {
            // Arrange
            var props = new FiberElementProps { Tooltip = "Save" };
            VNodePool.ReturnProps(props);

            // Act — an unrelated factory call rents from the pool.
            var rented = VNodePool.RentProps();

            // Assert — the caller-owned instance never entered the shared pool.
            Assert.That(ReferenceEquals(rented, props), Is.False);
        }

        [Test]
        public void Given_RentedProps_When_ReturnedAndRentedAgain_Then_ThePoolStillRecyclesIt()
        {
            // Arrange — a genuinely pool-owned props bag with a stale field.
            var rented = VNodePool.RentProps();
            rented.Tooltip = "stale";
            VNodePool.ReturnProps(rented);

            // Act
            var again = VNodePool.RentProps();

            // Assert — the pooled instance round-trips and comes back scrubbed.
            Assert.That((ReferenceEquals(again, rented), again.Tooltip),
                Is.EqualTo((true, (string)null)));
        }

        [Test]
        public void Given_ARentedPropsReturnedTwice_When_TwoRentsFollow_Then_TheyAreDistinctInstances()
        {
            // Arrange — the recycle sweep can reach one bag through several retired trees (a baseline
            // retired by its owner and again by a parent expansion), so a double return must recycle once.
            var bag = VNodePool.RentProps();
            VNodePool.ReturnProps(bag);
            VNodePool.ReturnProps(bag);

            // Act — two rents; a double-pooled bag would come back for both.
            var first = VNodePool.RentProps();
            var second = VNodePool.RentProps();

            // Assert — the pool never handed the same instance out twice.
            Assert.That(ReferenceEquals(first, second), Is.False);
        }

        [Test]
        public void Given_ARentedNodeArrayReturnedTwice_When_TwoRentsFollow_Then_TheyAreDistinctInstances()
        {
            // Arrange — same double-return discipline for the children-array pool.
            var array = VNodePool.RentNodeArray(3);
            VNodePool.ReturnNodeArray(array);
            VNodePool.ReturnNodeArray(array);

            // Act
            var first = VNodePool.RentNodeArray(3);
            var second = VNodePool.RentNodeArray(3);

            // Assert
            Assert.That(ReferenceEquals(first, second), Is.False);
        }

        [Test]
        public void Given_UserEventArrayNotRentedFromPool_When_ReturnEventArray_Then_ItIsNotCleared()
        {
            // Arrange — a caller-owned single-event array, as a component caching events would hold.
            var events = new FiberEventBinding[] { new ClickedBinding { Handler = () => { } } };

            // Act
            VNodePool.ReturnEventArray(events);

            // Assert — the caller's binding survives.
            Assert.That(events[0], Is.Not.Null);
        }

        [Test]
        public void Given_UserEventArrayWasPassedToReturn_When_AnotherRentFollows_Then_TheUserArrayIsNotHandedOut()
        {
            // Arrange
            var events = new FiberEventBinding[] { new ClickedBinding { Handler = () => { } } };
            VNodePool.ReturnEventArray(events);

            // Act
            var rented = VNodePool.RentSingleEventArray();

            // Assert — the caller-owned array never entered the shared pool.
            Assert.That(ReferenceEquals(rented, events), Is.False);
        }
    }
}
