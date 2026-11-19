using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests.Editor
{
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
            // Arrange
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
