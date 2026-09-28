using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how an unkeyed <see cref="ChildKey"/> spreads across a hash table and how it prints.
    /// <list type="bullet">
    /// <item>Keys at one index under a thousand sibling unkeyed Fragments hash to a thousand values, and so
    /// do keys at a thousand indices of one array, so neither shape chains a keyed diff's table into one
    /// bucket.</item>
    /// <item>Two keys at one index under sibling unkeyed Fragments print differently.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ChildKeyTests
    {
        private static ChildKey FirstChildOfFragmentAt(int fragmentIndex)
            => ChildKey.Positional(FiberKeying.FragmentChild(FiberKeying.WalkRoot, null, fragmentIndex).SlotPath, 0);

        [Test]
        public void Given_TheFirstChildrenOfAThousandSiblingUnkeyedFragments_When_Hashed_Then_EachHashIsDistinct()
        {
            // Arrange
            var keys = Enumerable.Range(0, 1000).Select(FirstChildOfFragmentAt).ToArray();

            // Act
            var distinct = keys.Select(key => key.GetHashCode()).Distinct().Count();

            // Assert — the hash is a fixed function of the paths FiberKeying composes, so the count is exact.
            Assert.That(distinct, Is.EqualTo(1000));
        }

        // GREEN_ON_BASE(characterization): the base hashes an unkeyed key by its index alone, which spreads
        // one array's children already. Drop `unchecked(_index * 397) ^` from the hash and this reddens.
        [Test]
        public void Given_AThousandChildrenOfOneArray_When_Hashed_Then_EachHashIsDistinct()
        {
            // Arrange
            var keys = Enumerable.Range(0, 1000).Select(index => ChildKey.Positional(index)).ToArray();

            // Act
            var distinct = keys.Select(key => key.GetHashCode()).Distinct().Count();

            // Assert
            Assert.That(distinct, Is.EqualTo(1000));
        }

        [Test]
        public void Given_TwoKeysAtOneIndexUnderSiblingUnkeyedFragments_When_Printed_Then_TheyDiffer()
        {
            // Arrange
            var first = FirstChildOfFragmentAt(0);
            var second = FirstChildOfFragmentAt(1);

            // Act
            var printed = (first.ToString(), second.ToString());

            // Assert
            Assert.That(printed.Item1, Is.Not.EqualTo(printed.Item2));
        }
    }
}
