#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryKey"/> equality, which decides which cache entry a query reads, and its
    /// prefix match, which decides which entries an invalidation reaches.
    /// <list type="bullet">
    /// <item>A key rebuilt with equal parts finds the entry an earlier key stored; a key differing in any part
    /// does not, a part of another type included, and neither does a key one part longer, even where the hash
    /// codes collide.</item>
    /// <item>Editing the array a key was built from does not move the key, and a null array is refused.</item>
    /// <item>No key equals null or a key under the object overload that differs from it.</item>
    /// <item>A key starts with each of its leading runs and not with a longer key.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class QueryKeyTests
    {
        [Test]
        public void Given_AKeyRebuiltWithEqualParts_When_LookedUpInAHashSet_Then_ItIsFound()
        {
            // Arrange
            var stored = new HashSet<QueryKey> { new QueryKey("todos", 1) };

            // Act
            var found = stored.Contains(new QueryKey("todos", 1));

            // Assert
            Assert.That(found, Is.True, "Parts compare by content, so a rebuilt key names the same entry");
        }

        [Test]
        public void Given_AKeyDifferingInItsLastPart_When_LookedUpInAHashSet_Then_ItIsNotFound()
        {
            // Arrange
            var stored = new HashSet<QueryKey> { new QueryKey("todos", 1) };

            // Act
            var found = stored.Contains(new QueryKey("todos", 2));

            // Assert
            Assert.That(found, Is.False, "Every part takes part in equality, the last one included");
        }

        [Test]
        public void Given_SinglePartKeysWhoseHashCodesCollide_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange — the int 1 and the long 2^32 hash alike, which the assertion reads as well.
            var key = new QueryKey(1);
            var other = new QueryKey(4294967296L);

            // Act
            var reading = (key.GetHashCode() == other.GetHashCode(), key.Equals(other));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)), "A shared hash code is not equality; the parts decide");
        }

        [Test]
        public void Given_AKeyOnePartLongerWhoseHashCodeCollides_When_ComparedWithItsPrefix_Then_TheyAreNotEqual()
        {
            // Arrange — a single part of -527 cancels the seed, and a trailing zero keeps the hash at zero,
            // so the two hash alike, which the assertion reads as well.
            var longer = new QueryKey(-527, 0);
            var prefix = new QueryKey(-527);

            // Act
            var reading = (longer.GetHashCode() == prefix.GetHashCode(), longer.Equals(prefix));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)), "A key equals only a key of its own length, prefix or not");
        }

        [Test]
        public void Given_AKeyBuiltFromAnArray_When_TheArrayIsEditedAfterwards_Then_TheKeyStillEqualsItsOriginalParts()
        {
            // Arrange
            var parts = new object?[] { "todos", 1 };
            var key = new QueryKey(parts);

            // Act
            parts[1] = 2;

            // Assert
            Assert.That(key.Equals(new QueryKey("todos", 1)), Is.True,
                "The key copies its parts, so the caller's array is not part of it");
        }

        [Test]
        public void Given_ANullArray_When_AKeyIsBuilt_Then_ItThrowsArgumentNull()
        {
            // Act + Assert
            Assert.That(() => new QueryKey((object?[])null!), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Given_AKey_When_ComparedWithNull_Then_ItIsNotEqual()
        {
            // Act
            var equal = new QueryKey("todos").Equals((QueryKey?)null);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_AKey_When_ComparedAsAnObjectWithADifferentKey_Then_ItIsNotEqual()
        {
            // Arrange
            object other = new QueryKey("users");

            // Act
            var equal = new QueryKey("todos").Equals(other);

            // Assert
            Assert.That(equal, Is.False, "The object overload compares the parts as the typed one does");
        }

        [Test]
        public void Given_AKey_When_AskedWhetherItStartsWithEachLeadingRunAndALongerKey_Then_OnlyTheRunsMatch()
        {
            // Arrange
            var key = new QueryKey("todos", 1);

            // Act
            var matches = $"{key.StartsWith(new QueryKey())} {key.StartsWith(new QueryKey("todos"))} " +
                $"{key.StartsWith(key)} {key.StartsWith(new QueryKey("todos", 1, "done"))}";

            // Assert
            Assert.That(matches, Is.EqualTo("True True True False"),
                "A filter key matches the keys it leads, and a key longer than the one it is asked of leads nothing");
        }
    }
}
