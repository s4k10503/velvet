using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a <see cref="ChildKey"/> the general path owns by a fiber matches only a key owned by
    /// that same fiber, for a positional key and an explicit one alike. The key maps are hashed, and two
    /// owners' keys mostly land in different buckets, so a reconcile reading one only rarely reaches the
    /// comparison this pins; asking it directly is what makes the owner term's absence visible.
    /// </summary>
    [TestFixture]
    internal sealed class ChildKeyOwnerTests
    {
        [Test]
        public void Given_TwoPositionalKeysAtOneIndex_When_TheirOwnersDiffer_Then_TheyDoNotMatch()
        {
            // Arrange
            var owner = new ComponentFiber();
            var other = new ComponentFiber();
            var key = ChildKey.Positional(0).OwnedBy(owner);

            // Act
            var sameOwner = key.Equals(ChildKey.Positional(0).OwnedBy(owner));
            var otherOwner = key.Equals(ChildKey.Positional(0).OwnedBy(other));

            // Assert — the same-owner reading is the control that the comparison can answer true at all.
            Assert.That((sameOwner, otherOwner), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoExplicitKeysSpelledAlike_When_TheirOwnersDiffer_Then_TheyDoNotMatch()
        {
            // Arrange
            var owner = new ComponentFiber();
            var other = new ComponentFiber();
            var key = ChildKey.Explicit("title").OwnedBy(owner);

            // Act
            var sameOwner = key.Equals(ChildKey.Explicit("title").OwnedBy(owner));
            var otherOwner = key.Equals(ChildKey.Explicit("title").OwnedBy(other));

            // Assert
            Assert.That((sameOwner, otherOwner), Is.EqualTo((true, false)));
        }
    }
}
