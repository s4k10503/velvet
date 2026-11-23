using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the hash of an explicit <see cref="ChildKey"/> tells keys apart past a NUL delimiter, which is
    /// what <c>FiberKeying.LeafScope</c> joins a scope and a key with: a table of the siblings of one scope would
    /// otherwise put every one of them in one bucket.
    /// </summary>
    [TestFixture]
    internal sealed class ChildKeyHashTests
    {
        [Test]
        public void Given_HundredKeysScopedUnderOneScope_When_TheyAreHashed_Then_EveryHashIsDistinct()
        {
            // Arrange
            var owner = new ComponentFiber();
            var hashes = new HashSet<int>();

            // Act
            for (var i = 0; i < 100; i++) hashes.Add(ChildKey.Explicit("0\0p\0Lp" + i).OwnedBy(owner).GetHashCode());

            // Assert
            Assert.That(hashes.Count, Is.EqualTo(100));
        }

        [Test]
        public void Given_TwoKeysAgreeingUpToADelimiter_When_TheyAreHashed_Then_TheHashesDiffer()
        {
            // Arrange
            var first = ChildKey.Explicit("scope\0a");
            var second = ChildKey.Explicit("scope\0b");

            // Act
            var same = first.GetHashCode() == second.GetHashCode();

            // Assert
            Assert.That(same, Is.False);
        }
    }
}
