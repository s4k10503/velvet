#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a <see cref="QueryKey"/> part that is a tuple, a record or an anonymous type compares: by its
    /// members, each compared as a part is, where v5's <c>hashKey</c> serialises an object whole.
    /// <list type="bullet">
    /// <item>A key rebuilt with an equal record, tuple or anonymous object holding a collection is equal to the
    /// first and hashes alike, where the collection was a new instance each time; one member differing, or the
    /// record being of another type, makes it unequal.</item>
    /// <item>Members are read through every record a record derives from, and through records nested inside
    /// records and collections, whatever order a dictionary among them was filled in.</item>
    /// <item>A record that declares its own <c>Equals</c>, and a class that declares none, keep what they
    /// have.</item>
    /// <item>A filter record leads a key's record the way a filter array leads an array, and prints its
    /// members.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class QueryKeyMembersTests
    {
        private sealed record Filter(string Status, List<int> Ids);

        private sealed record OtherFilter(string Status, List<int> Ids);

        private sealed record Paged(Filter Filter, int Page);

        private sealed record Tagged(Dictionary<string, int> Tags);

        private record Scope(int Id, int[] Values);

        private sealed record Narrow(int Id, int[] Values, int Extra) : Scope(Id, Values);

        // Equal by id, whatever the array holds.
        private sealed record ById(int Id, int[] Values)
        {
            public bool Equals(ById? other) => other != null && Id == other.Id;

            public override int GetHashCode() => Id;
        }

        private sealed class Holder
        {
            internal Holder(int[] values) => Values = values;

            internal int[] Values { get; }
        }

        [Test]
        public void Given_TwoKeysHoldingEqualRecordsOfAList_When_Compared_Then_TheyAreEqual()
        {
            // Arrange
            var first = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var second = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "A record compares its list by what it holds, as v5 hashes an object's array");
        }

        [Test]
        public void Given_TwoKeysHoldingEqualRecordsOfAList_When_Hashed_Then_TheHashCodesAreEqual()
        {
            // Arrange
            var first = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var second = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));

            // Act
            var firstHash = first.GetHashCode();
            var secondHash = second.GetHashCode();

            // Assert
            Assert.That(firstHash, Is.EqualTo(secondHash), "Equal keys hash alike, so a dictionary finds the entry");
        }

        // GREEN_ON_BASE(characterization): the base compares a record's list by reference, which also tells two different lists apart.
        [Test]
        public void Given_TwoKeysHoldingRecordsWhoseListsDiffer_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            var first = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var second = new QueryKey("todos", new Filter("open", new List<int> { 1, 3 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "One element of a member's collection differing makes the keys differ");
        }

        // GREEN_ON_BASE(characterization): the base's records differ on a scalar member too.
        [Test]
        public void Given_TwoKeysHoldingRecordsWhoseScalarMembersDiffer_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            var shared = new List<int> { 1, 2 };
            var first = new QueryKey("todos", new Filter("open", shared));
            var second = new QueryKey("todos", new Filter("done", shared));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "A member that is not a collection still decides");
        }

        // GREEN_ON_BASE(characterization): two record types are two types to the base's Equals as well.
        [Test]
        public void Given_RecordsOfTwoTypesHoldingTheSameMembers_When_KeysAreCompared_Then_TheyAreNotEqual()
        {
            // Arrange
            var first = new QueryKey(new Filter("open", new List<int> { 1 }));
            var second = new QueryKey(new OtherFilter("open", new List<int> { 1 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "The record's type is part of what it is");
        }

        [Test]
        public void Given_RecordsNestedInARecord_When_KeysAreRebuilt_Then_TheyAreEqual()
        {
            // Arrange
            var first = new QueryKey(new Paged(new Filter("open", new List<int> { 1 }), 2));
            var second = new QueryKey(new Paged(new Filter("open", new List<int> { 1 }), 2));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "Members are compared as parts at any depth");
        }

        [Test]
        public void Given_RecordsHoldingADictionary_When_ItsEntriesWereAddedInAnotherOrder_Then_TheKeysAreEqual()
        {
            // Arrange
            var first = new QueryKey(new Tagged(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
            var second = new QueryKey(new Tagged(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "A dictionary inside a record is order-free, as v5 sorts an object's keys");
        }

        [Test]
        public void Given_TuplesHoldingEqualArrays_When_KeysAreRebuilt_Then_TheyAreEqual()
        {
            // Arrange
            var first = new QueryKey("todos", (1, new[] { 1, 2 }));
            var second = new QueryKey("todos", (1, new[] { 1, 2 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "A tuple compares its items as parts");
        }

        // GREEN_ON_BASE(characterization): the base's tuples differ on an item too.
        [Test]
        public void Given_TuplesWhoseArraysDiffer_When_Compared_Then_TheKeysAreNotEqual()
        {
            // Arrange
            var first = new QueryKey((1, new[] { 1, 2 }));
            var second = new QueryKey((1, new[] { 1, 3 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "One element of an item's collection differing makes the keys differ");
        }

        [Test]
        public void Given_AnonymousObjectsHoldingEqualLists_When_KeysAreRebuilt_Then_TheyAreEqual()
        {
            // Arrange
            var first = new QueryKey(new { Ids = new List<int> { 1, 2 } });
            var second = new QueryKey(new { Ids = new List<int> { 1, 2 } });

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "An anonymous type compares its members as parts");
        }

        [Test]
        public void Given_RecordsDerivedFromARecord_When_TheBaseMembersAreEqualArrays_Then_TheKeysAreEqual()
        {
            // Arrange
            var first = new QueryKey(new Narrow(1, new[] { 1, 2 }, 3));
            var second = new QueryKey(new Narrow(1, new[] { 1, 2 }, 3));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "The members a derived record inherits take part");
        }

        // GREEN_ON_BASE(characterization): a record that declares its own Equals keeps it on the base as well.
        [Test]
        public void Given_RecordsDeclaringTheirOwnEquals_When_OnlyTheirArraysDiffer_Then_TheKeysAreEqual()
        {
            // Arrange
            var first = new QueryKey(new ById(1, new[] { 1 }));
            var second = new QueryKey(new ById(1, new[] { 2 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.True, "A type that declares its own equality keeps it");
        }

        // GREEN_ON_BASE(characterization): a class with no equality of its own is compared by reference on the base too.
        [Test]
        public void Given_InstancesOfAClassWithNoEquality_When_TheyHoldEqualArrays_Then_TheKeysAreNotEqual()
        {
            // Arrange
            var first = new QueryKey(new Holder(new[] { 1 }));
            var second = new QueryKey(new Holder(new[] { 1 }));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "Only a record, a tuple or an anonymous type is taken apart");
        }

        [Test]
        public void Given_AFilterRecordWithAShorterList_When_ItIsMatchedAgainstAKey_Then_TheKeyMatches()
        {
            // Arrange
            var key = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var filter = new QueryKey("todos", new Filter("open", new List<int> { 1 }));

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.True, "A filter's list leads the key's, as partialMatchKey reads an array in an object");
        }

        [Test]
        public void Given_AFilterRecordLeavingAMemberNull_When_ItIsMatchedAgainstAKey_Then_TheMemberIsNotNamed()
        {
            // Arrange
            var key = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var filter = new QueryKey("todos", new Filter(null!, new List<int> { 1 }));

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.True, "partialMatchKey visits only the properties a filter has, so a null member names nothing");
        }

        // GREEN_ON_BASE(characterization): the base compares a record whole, so a null member never matches a set one.
        [Test]
        public void Given_TwoKeysHoldingRecordsWhereOneMemberIsNull_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            var shared = new List<int> { 1 };
            var first = new QueryKey(new Filter("open", shared));
            var second = new QueryKey(new Filter(null!, shared));

            // Act
            var equal = first.Equals(second);

            // Assert
            Assert.That(equal, Is.False, "Only a filter may leave a member out; key equality compares them all");
        }

        // GREEN_ON_BASE(characterization): a filter list that does not lead the key's list matches nothing on the base either.
        [Test]
        public void Given_AFilterRecordWhoseListIsNotALeadingRun_When_ItIsMatchedAgainstAKey_Then_TheKeyDoesNotMatch()
        {
            // Arrange
            var key = new QueryKey("todos", new Filter("open", new List<int> { 1, 2 }));
            var filter = new QueryKey("todos", new Filter("open", new List<int> { 2 }));

            // Act
            var matches = key.Matches(filter);

            // Assert
            Assert.That(matches, Is.False, "A leading run is what a filter's list names");
        }

        [Test]
        public void Given_AKeyHoldingARecordOfAList_When_Printed_Then_ItsMembersArePrinted()
        {
            // Act
            var printed = new QueryKey(new Filter("open", new List<int> { 1, 2 })).ToString();

            // Assert
            Assert.That(printed, Is.EqualTo("[Filter(open, [1, 2])]"),
                "A part compared by its members prints them, so keys differing in them print differently");
        }

        // GREEN_ON_BASE(characterization): the base prints a tuple through its own ToString, which is the same text.
        [Test]
        public void Given_AKeyHoldingATuple_When_Printed_Then_ItsItemsArePrinted()
        {
            // Act
            var printed = new QueryKey((1, "a")).ToString();

            // Assert
            Assert.That(printed, Is.EqualTo("[(1, a)]"), "A tuple prints its items without a type name");
        }
    }
}
