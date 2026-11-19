#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <see cref="QueryKey"/> equality, which decides which cache entry a query reads, and its
    /// filter match, which decides which entries an invalidation reaches.
    /// <list type="bullet">
    /// <item>A key rebuilt with equal parts finds the entry an earlier key stored; a key differing in any part
    /// does not, a part of another type included, and neither does a key one part longer, even where the hash
    /// codes collide.</item>
    /// <item>A sequence part other than a string compares element by element: rebuilt with equal elements, as
    /// another kind of sequence included, it is equal and hashes alike; with an element differing or one more
    /// element it does not match. A string is matched and hashed whole, not as its characters, and a
    /// sequence type declaring its own equality keeps it.</item>
    /// <item>A dictionary part compares by key and value whatever order its entries were added in, and a set
    /// part by element in any order, as v5 sorts an object's keys before hashing; each is equal and hashes
    /// alike when rebuilt in another order, and differs where a value, a key or an element does.</item>
    /// <item>Editing the array a key was built from does not move the key, and a null array is refused.</item>
    /// <item>No key equals null or a key under the object overload that differs from it.</item>
    /// <item>A filter matches each key it leads and not a shorter key; where a filter part is an array or a list
    /// it matches a part that starts with it, and where it is a dictionary it matches a part holding at least its
    /// entries, at any depth, as v5's <c>partialMatchKey</c> does, while a set part is matched whole.</item>
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
        public void Given_AnArrayPartRebuiltWithEqualElements_When_Compared_Then_TheKeysAreEqual()
        {
            // Act
            var equal = new QueryKey("todos", new[] { 1, 2 }).Equals(new QueryKey("todos", new[] { 1, 2 }));

            // Assert
            Assert.That(equal, Is.True, "A sequence part compares by its elements, not by instance");
        }

        [Test]
        public void Given_AnArrayPartAndAListPartWithEqualElements_When_Hashed_Then_TheHashCodesMatch()
        {
            // Act
            var same = new QueryKey("todos", new[] { 1, 2 }).GetHashCode()
                == new QueryKey("todos", new List<int> { 1, 2 }).GetHashCode();

            // Assert
            Assert.That(same, Is.True, "A sequence part hashes by its elements, so equal keys find one entry");
        }

        [Test]
        public void Given_SequencePartsDifferingInAnElementOrAFilterLongerThanThePart_When_Matched_Then_NeitherMatches()
        {
            // Arrange — matched as filters rather than compared, since a filter match compares the parts
            // without the hash codes, which would tell these apart before any element did.
            var key = new QueryKey(new[] { 1, 2 });

            // Act
            var reading = (key.Matches(new QueryKey(new[] { 1, 3 })), key.Matches(new QueryKey(new[] { 1, 2, 3 })));

            // Assert
            Assert.That(reading, Is.EqualTo((false, false)), "Every element of a filter counts, and it may not run on past the part");
        }

        [Test]
        public void Given_ASequencePartRunningOnPastTheFilterPart_When_MatchedAndCompared_Then_ItMatchesButIsNotEqual()
        {
            // Arrange
            var key = new QueryKey(new[] { 1, 2, 3 });
            var filter = new QueryKey(new[] { 1, 2 });

            // Act
            var reading = (key.Matches(filter), key.Equals(filter));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)), "A filter array matches the arrays it leads, as v5's partialMatchKey does");
        }

        [Test]
        public void Given_AStringPartAndACharArrayPart_When_MatchedAndHashed_Then_NeitherAgrees()
        {
            // Arrange
            var text = new QueryKey("ab");
            var characters = new QueryKey(new[] { 'a', 'b' });

            // Act
            var reading = (text.Matches(characters), text.GetHashCode() == characters.GetHashCode());

            // Assert
            Assert.That(reading, Is.EqualTo((false, false)), "A string is one value, not a sequence of characters");
        }

        [Test]
        public void Given_AKeyWithASequencePart_When_Printed_Then_ItsElementsArePrinted()
        {
            // Act
            var printed = new QueryKey("todos", new[] { 1, 2 }).ToString();

            // Assert
            Assert.That(printed, Is.EqualTo("[todos, [1, 2]]"),
                "A part compared by its elements prints them, so keys differing in them print differently");
        }

        [Test]
        public void Given_ASequencePartDeclaringItsOwnEquality_When_Compared_Then_ItsEqualityDecides()
        {
            // Act
            var equal = new QueryKey(new IdSequence(1, 1, 2)).Equals(new QueryKey(new IdSequence(1, 3)));

            // Assert
            Assert.That(equal, Is.True, "A sequence type that overrides Equals is compared by it, not by its elements");
        }

        // Equal by id, whatever it enumerates.
        private sealed class IdSequence : IEnumerable
        {
            private readonly int _id;
            private readonly int[] _items;

            public IdSequence(int id, params int[] items)
            {
                _id = id;
                _items = items;
            }

            public IEnumerator GetEnumerator() => _items.GetEnumerator();

            public override bool Equals(object? obj) => obj is IdSequence other && other._id == _id;

            public override int GetHashCode() => _id;
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
        public void Given_AKey_When_AskedWhetherItMatchesEachLeadingRunAndALongerKey_Then_OnlyTheRunsMatch()
        {
            // Arrange
            var key = new QueryKey("todos", 1);

            // Act
            var matches = $"{key.Matches(new QueryKey())} {key.Matches(new QueryKey("todos"))} " +
                $"{key.Matches(key)} {key.Matches(new QueryKey("todos", 1, "done"))}";

            // Assert
            Assert.That(matches, Is.EqualTo("True True True False"),
                "A filter key matches the keys it leads, and a key longer than the one it is asked of leads nothing");
        }

        [Test]
        public void Given_DictionaryPartsBuiltInDifferentOrders_When_Compared_Then_TheKeysAreEqualAndHashAlike()
        {
            // Arrange
            var first = new QueryKey("todos", new Dictionary<string, int> { ["status"] = 1, ["page"] = 2 });
            var second = new QueryKey("todos", new Dictionary<string, int> { ["page"] = 2, ["status"] = 1 });

            // Act
            var reading = (first.Equals(second), first.GetHashCode() == second.GetHashCode());

            // Assert
            Assert.That(reading, Is.EqualTo((true, true)), "An object's keys are sorted before it is hashed, so their order names nothing");
        }

        [Test]
        public void Given_DictionaryPartsDifferingInAValueOrInAKey_When_Matched_Then_NeitherMatches()
        {
            // Arrange — matched as filters rather than compared, since a filter match compares the parts
            // without the hash codes, which would tell these apart before any entry did.
            var key = new QueryKey(new Dictionary<string, int> { ["page"] = 1 });

            // Act
            var reading = (key.Matches(new QueryKey(new Dictionary<string, int> { ["page"] = 2 })),
                key.Matches(new QueryKey(new Dictionary<string, int> { ["size"] = 1 })));

            // Assert
            Assert.That(reading, Is.EqualTo((false, false)), "A value and a key both take part in the comparison");
        }

        [Test]
        public void Given_DictionaryPartsWhoseHashCodesCollideAndOneHoldsAnExtraEntry_When_Compared_Then_TheKeysAreNotEqual()
        {
            // Arrange — the entries 0 -> 0 and 1 -> -31 each hash to zero, so the two dictionaries hash alike,
            // which the assertion reads as well.
            var larger = new QueryKey(new Dictionary<int, int> { [0] = 0, [1] = -31 });
            var smaller = new QueryKey(new Dictionary<int, int> { [0] = 0 });

            // Act
            var reading = (larger.GetHashCode() == smaller.GetHashCode(), larger.Equals(smaller));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)), "A dictionary holding every entry of another and one more is not equal to it");
        }

        [Test]
        public void Given_DictionaryPartsHoldingEqualListsInDifferentInstances_When_Compared_Then_TheKeysAreEqual()
        {
            // Arrange
            var first = new QueryKey(new Dictionary<string, List<int>> { ["ids"] = new() { 1, 2 } });
            var second = new QueryKey(new Dictionary<string, List<int>> { ["ids"] = new() { 1, 2 } });

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "A value that is itself a collection compares by what it holds");
        }

        [Test]
        public void Given_SetPartsBuiltInDifferentOrders_When_Compared_Then_TheKeysAreEqualAndHashAlike()
        {
            // Arrange
            var first = new QueryKey("tags", new HashSet<string> { "a", "b", "c" });
            var second = new QueryKey("tags", new HashSet<string> { "c", "a", "b" });

            // Act
            var reading = (first.Equals(second), first.GetHashCode() == second.GetHashCode());

            // Assert
            Assert.That(reading, Is.EqualTo((true, true)), "A set has no order to name an entry by");
        }

        [Test]
        public void Given_SetPartsDifferingInAnElement_When_Matched_Then_NeitherMatches()
        {
            // Arrange — matched as filters rather than compared, for the reason the sequence cases give.
            var set = new QueryKey(new HashSet<int> { 1, 2 });
            var other = new QueryKey(new HashSet<int> { 1, 3 });

            // Act
            var reading = (set.Matches(other), other.Matches(set));

            // Assert
            Assert.That(reading, Is.EqualTo((false, false)), "Every element of one set must be found in the other");
        }

        [Test]
        public void Given_SetPartsWhoseHashCodesCollideAndOneHoldsAnExtraElement_When_Compared_Then_TheKeysAreNotEqual()
        {
            // Arrange — the elements 1 and -1 hash to values that cancel, so adding 0 leaves the hash alone,
            // which the assertion reads as well.
            var smaller = new QueryKey(new HashSet<int> { 1, -1 });
            var larger = new QueryKey(new HashSet<int> { 1, -1, 0 });

            // Act
            var reading = (smaller.GetHashCode() == larger.GetHashCode(), smaller.Equals(larger));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)), "A set whose every element another holds is not equal to it while the other holds more");
        }

        [Test]
        public void Given_ADictionaryPartAndAListOfItsEntries_When_Compared_Then_TheKeysAreNotEqual()
        {
            // Arrange
            var dictionary = new QueryKey(new Dictionary<string, int> { ["page"] = 1 });
            var entries = new QueryKey(new List<KeyValuePair<string, int>> { new("page", 1) });

            // Act
            var equal = dictionary.Equals(entries);

            // Assert
            Assert.That(equal, Is.False, "A dictionary is compared as one, not as the sequence of its entries");
        }

        [Test]
        public void Given_AFilterDictionaryNamingFewerEntries_When_Matched_Then_ItMatchesOnlyWhereTheNamedEntriesAgree()
        {
            // Arrange
            var key = new QueryKey("todos", new Dictionary<string, int> { ["status"] = 1, ["page"] = 2 });

            // Act
            var reading = (
                key.Matches(new QueryKey("todos", new Dictionary<string, int> { ["status"] = 1 })),
                key.Matches(new QueryKey("todos", new Dictionary<string, int> { ["status"] = 2 })),
                key.Matches(new QueryKey("todos", new Dictionary<string, int> { ["size"] = 1 })));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false, false)),
                "A filter object matches on the properties it names: one with another value, or one the key lacks, does not");
        }

        [Test]
        public void Given_AFilterDictionaryNamingMoreEntriesThanThePart_When_Matched_Then_ItDoesNotMatch()
        {
            // Arrange
            var key = new QueryKey(new Dictionary<string, int> { ["status"] = 1 });
            var filter = new QueryKey(new Dictionary<string, int> { ["status"] = 1, ["page"] = 2 });

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.False, "A filter cannot name an entry the part does not hold");
        }

        [Test]
        public void Given_AFilterWithNestedPartialParts_When_Matched_Then_TheyMatchAtAnyDepth()
        {
            // Arrange
            var key = new QueryKey(new Dictionary<string, object>
            {
                ["filter"] = new Dictionary<string, object> { ["status"] = 1, ["page"] = 2 },
                ["ids"] = new[] { 1, 2, 3 },
            });
            var filter = new QueryKey(new Dictionary<string, object>
            {
                ["filter"] = new Dictionary<string, object> { ["status"] = 1 },
                ["ids"] = new[] { 1, 2 },
            });

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.True, "partialMatchKey recurses into the values of an object and the items of an array");
        }

        [Test]
        public void Given_AFilterSetNamingFewerElementsThanThePart_When_Matched_Then_ItDoesNotMatch()
        {
            // Arrange
            var key = new QueryKey(new HashSet<int> { 1, 2 });
            var filter = new QueryKey(new HashSet<int> { 1 });

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.False, "A set has no leading run or named entries, so it is matched whole");
        }

        [Test]
        public void Given_KeysWithADictionaryAndASetPart_When_Printed_Then_TheirEntriesArePrinted()
        {
            // Act
            var printed = new QueryKey("todos", new Dictionary<string, int> { ["page"] = 2 }, new HashSet<int> { 7 }).ToString();

            // Assert
            Assert.That(printed, Is.EqualTo("[todos, {page: 2}, {7}]"),
                "A part compared by its entries prints them, so keys differing in them print differently");
        }
    }
}
