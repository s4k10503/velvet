using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
    /// <summary>
    /// Specifies the contract of <see cref="StoreShallowEqualityComparer.Sequence{T}"/>, the comparer used to
    /// decide whether two consecutive <see cref="IReadOnlyList{T}"/> selector slices are equal.
    /// <list type="bullet">
    /// <item>Two sequences are equal only when both are null, or when they share the same reference, or when
    /// their lengths match and every element pair is identity-equal.</item>
    /// <item>One null and one non-null sequence are never equal, regardless of the non-null one being empty.</item>
    /// <item>Sequences of differing length are never equal.</item>
    /// <item>Value-type elements compare by value; reference-type elements compare by identity, so distinct-but
    /// value-equal instances at matching positions count as unequal.</item>
    /// <item>The comparer for a given element type is a cached singleton, so repeated requests return the same
    /// instance.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StoreShallowEqualityComparerTests
    {
        private sealed record Item(int Id);

        [Test]
        public void Given_TwoNullSequences_When_Compared_Then_AreEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(null, null);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_NullAndEmptySequence_When_Compared_Then_AreNotEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(null, Array.Empty<int>());

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_EmptyAndNullSequence_When_Compared_Then_AreNotEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(Array.Empty<int>(), null);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_SameSequenceReference_When_Compared_Then_AreEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();
            IReadOnlyList<int> list = new[] { 1, 2, 3 };

            // Act
            var result = cmp.Equals(list, list);

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_SequencesOfDifferentLength_When_Compared_Then_AreNotEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(new[] { 1, 2 }, new[] { 1, 2, 3 });

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_EqualValueTypeElements_When_Compared_Then_AreEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(new[] { 1, 2, 3 }, new[] { 1, 2, 3 });

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_DifferingValueTypeElements_When_Compared_Then_AreNotEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<int>();

            // Act
            var result = cmp.Equals(new[] { 1, 2, 3 }, new[] { 1, 2, 4 });

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_DistinctButValueEqualReferenceTypeElements_When_Compared_Then_AreNotEqual()
        {
            // Arrange — reference-type elements compare by identity, so distinct instances are not equal
            var cmp = StoreShallowEqualityComparer.Sequence<Item>();

            // Act
            var result = cmp.Equals(new[] { new Item(1), new Item(2) }, new[] { new Item(1), new Item(2) });

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_SameReferenceTypeElementInstances_When_Compared_Then_AreEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<Item>();
            var a = new Item(1);
            var b = new Item(2);

            // Act
            var result = cmp.Equals(new[] { a, b }, new[] { a, b });

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void Given_DifferentReferenceTypeElements_When_Compared_Then_AreNotEqual()
        {
            // Arrange
            var cmp = StoreShallowEqualityComparer.Sequence<Item>();

            // Act
            var result = cmp.Equals(new[] { new Item(1), new Item(2) }, new[] { new Item(1), new Item(99) });

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void Given_SameElementType_When_RequestedTwice_Then_ReturnsSameCachedComparer()
        {
            // Act
            var first = StoreShallowEqualityComparer.Sequence<int>();
            var second = StoreShallowEqualityComparer.Sequence<int>();

            // Assert
            Assert.That(first, Is.SameAs(second));
        }
    }
}
