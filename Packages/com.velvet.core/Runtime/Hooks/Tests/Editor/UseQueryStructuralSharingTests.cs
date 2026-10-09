#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the structural sharing of a query's data, TanStack Query's <c>replaceEqualDeep</c>.
    /// <list type="bullet">
    /// <item>New data that is deeply equal to the data held is replaced by it, for an array, a list and a
    /// dictionary compared element by element and entry by entry, a string compared by <c>Equals</c>; any
    /// other class, a record included, is kept only as the very instance held.</item>
    /// <item>Where the new data differs, the parts that are equal keep their held instances, at any depth, and
    /// a dictionary keeps its comparer. A length that differs, a previous value of none and a type that
    /// differs each take the new data.</item>
    /// <item>A query's data keeps the instance it held when an equal result lands, a function in the options
    /// chooses the data instead and is handed none the first time, and a function that throws makes the
    /// request fail.</item>
    /// <item>A component reading <c>Data</c> re-renders when a class equal by its own <c>Equals</c> lands as a
    /// new instance, since a result property changes when its instance does, and does not when equal text
    /// lands as a new string. Descent stops at depth 500, and collections of unmanaged elements compare
    /// without sharing parts.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UseQueryStructuralSharingTests
    {
        private sealed record Todo(int Id, string Title);

        private sealed record Holder(List<int> Items);

        private sealed class Entity
        {
            public Entity(int id)
            {
                Id = id;
            }

            public int Id { get; }

            public override bool Equals(object? obj) => obj is Entity other && other.Id == Id;

            public override int GetHashCode() => Id;
        }

        private VisualElement _root = null!;
        private static QueryClient s_client = null!;
        private static Func<object?, object, object>? s_sharing;
        private static int s_renderCount;
        private static readonly List<VelvetTaskCompletionSource<object>> s_sources = new();
        private static readonly List<QueryResult<object>> s_renders = new();

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_client = new QueryClient();
            s_sharing = null;
            s_renderCount = 0;
            s_sources.Clear();
            s_renders.Clear();
        }

        [Test]
        public void Given_TheSameInstance_When_Replaced_Then_ItIsKept()
        {
            // Arrange
            var held = new List<Todo> { new(1, "a") };

            // Act
            var result = QueryStructuralSharing.Replace(held, held);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_TwoListsHoldingTheSameRecordInstances_When_Replaced_Then_TheHeldListIsKept()
        {
            // Arrange
            var first = new Todo(1, "a");
            var second = new Todo(2, "b");
            var held = new List<Todo> { first, second };
            var arrived = new List<Todo> { first, second };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True, "Deeply equal data is the instance already held");
        }

        [Test]
        public void Given_TwoListsHoldingEqualButDistinctRecords_When_Replaced_Then_TheNewListIsTaken()
        {
            // Arrange
            var held = new List<Todo> { new(1, "a") };
            var arrived = new List<Todo> { new(1, "a") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result, arrived), ReferenceEquals(result[0], arrived[0])), Is.EqualTo((true, true)),
                "A record is a class instance, which replaceEqualDeep keeps only as the very instance held");
        }

        [Test]
        public void Given_AListSharingOneRecordInstanceAndHoldingAChangedOne_When_Replaced_Then_TheSharedRecordIsKeptAndTheChangedOneIsNew()
        {
            // Arrange
            var shared = new Todo(1, "a");
            var held = new List<Todo> { shared, new(2, "b") };
            var arrived = new List<Todo> { shared, new(2, "B") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], shared), ReferenceEquals(result[1], arrived[1])), Is.EqualTo((true, true)),
                "A record changed in place of the held one is the new instance");
        }

        [Test]
        public void Given_AnArrayOfEqualButDistinctRecordsWithOneChanged_When_Replaced_Then_EveryRecordIsTheNewInstance()
        {
            // Arrange
            var held = new[] { new Todo(1, "a"), new Todo(2, "b") };
            var arrived = new[] { new Todo(1, "a"), new Todo(2, "B") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], arrived[0]), ReferenceEquals(result[1], arrived[1])), Is.EqualTo((true, true)),
                "A record is a class instance, which replaceEqualDeep keeps only as the very instance held");
        }

        [Test]
        public void Given_TwoEqualArraysOfIntegers_When_Replaced_Then_TheHeldArrayIsKept()
        {
            // Arrange
            var held = new[] { 1, 2, 3 };
            var arrived = new[] { 1, 2, 3 };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_AListLongerThanTheHeldOne_When_Replaced_Then_TheSharedPrefixKeepsItsInstances()
        {
            // Arrange
            var shared = new Todo(1, "a");
            var held = new List<Todo> { shared };
            var arrived = new List<Todo> { shared, new(2, "b") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], held[0]), result.Count), Is.EqualTo((true, 2)),
                "A list is not the held one because the part it shares with it is equal");
        }

        [Test]
        public void Given_AListShorterThanTheHeldOne_When_Replaced_Then_ItIsNotTheHeldListButKeepsTheEqualRecord()
        {
            // Arrange
            var shared = new Todo(1, "a");
            var held = new List<Todo> { shared, new(2, "b") };
            var arrived = new List<Todo> { shared };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], held[0]), result.Count), Is.EqualTo((true, 1)),
                "Everything the new list holds equals the held list's, yet the held list holds more");
        }

        [Test]
        public void Given_ADictionaryWithOneChangedValue_When_Replaced_Then_TheEqualValueKeepsItsInstanceAndTheChangedOneIsNew()
        {
            // Arrange
            var unchanged = new Todo(1, "x");
            var held = new Dictionary<string, Todo> { ["a"] = unchanged, ["b"] = new(2, "y") };
            var arrived = new Dictionary<string, Todo> { ["a"] = unchanged, ["b"] = new(2, "Y") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result["a"], held["a"]), ReferenceEquals(result["b"], arrived["b"])),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ADictionaryWithACustomComparerAndOneChangedValue_When_Replaced_Then_TheResultKeepsTheComparer()
        {
            // Arrange
            var unchanged = new Todo(1, "x");
            var held = new Dictionary<string, Todo>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = unchanged, ["b"] = new(2, "y"),
            };
            var arrived = new Dictionary<string, Todo>(StringComparer.OrdinalIgnoreCase)
            {
                ["a"] = unchanged, ["b"] = new(2, "Y"),
            };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(result.ContainsKey("A"), Is.True, "The copy is looked up as the data that arrived was");
        }

        [Test]
        public void Given_EqualDictionaries_When_Replaced_Then_TheHeldDictionaryIsKept()
        {
            // Arrange
            var shared = new Todo(1, "x");
            var held = new Dictionary<string, Todo> { ["a"] = shared };
            var arrived = new Dictionary<string, Todo> { ["a"] = shared };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_ListsNestedInAList_When_OneInnerListIsEqual_Then_ItKeepsItsInstance()
        {
            // Arrange
            var shared = new Todo(1, "a");
            var held = new List<List<Todo>> { new() { shared }, new() { new(2, "b") } };
            var arrived = new List<List<Todo>> { new() { shared }, new() { new(2, "B") } };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], held[0]), ReferenceEquals(result[1], arrived[1])),
                Is.EqualTo((true, true)), "Equal parts are found at any depth");
        }

        [Test]
        public void Given_AClassEqualByItsOwnEquals_When_ReplacedByAnotherInstance_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            var held = new Entity(1);
            var arrived = new Entity(1);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True,
                "A class other than a record is kept only as the very instance held, since its Equals may mean less than all of it");
        }

        [Test]
        public void Given_ARecordHoldingAList_When_ReplacedByAnEqualRecordHoldingAnotherList_Then_TheNewRecordIsTaken()
        {
            // Arrange
            var held = new Holder(new List<int> { 1 });
            var arrived = new Holder(new List<int> { 1 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True, "A record compares a collection it holds by reference");
        }

        [Test]
        public void Given_EqualStringsOfTwoInstances_When_Replaced_Then_TheHeldInstanceIsKept()
        {
            // Arrange
            var held = new string('a', 3);
            var arrived = new string('a', 3);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_NoHeldData_When_Replaced_Then_TheNewDataIsTaken()
        {
            // Arrange
            var arrived = new List<Todo> { new(1, "a") };

            // Act
            var result = QueryStructuralSharing.Replace(null, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        [Test]
        public void Given_HeldAndNewDataOfDifferentTypes_When_Replaced_Then_TheNewDataIsTaken()
        {
            // Arrange
            object held = new[] { 1 };
            object arrived = new List<int> { 1 };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True, "An array and a list are not the same shape of data");
        }

        [Test]
        public void Given_EqualListsNestedFiveHundredAndFiveHundredAndOneDeep_When_Replaced_Then_OnlyTheShallowerKeepsTheHeldInstance()
        {
            // Arrange — the string at the bottom is a new instance each time, so it is the descent that keeps it.
            // The shallower chain ends at depth 500 and the deeper at 501, where the descent stops.
            var heldShallow = Chain(500);
            var heldDeep = Chain(501);

            // Act
            var shallow = QueryStructuralSharing.Replace(heldShallow, Chain(500));
            var deep = QueryStructuralSharing.Replace(heldDeep, Chain(501));

            // Assert
            Assert.That((ReferenceEquals(shallow, heldShallow), ReferenceEquals(deep, heldDeep)), Is.EqualTo((true, false)),
                "Sharing stops descending at the depth v5 stops at and takes the new data there");
        }

        [Test]
        public void Given_EqualListsOfIntegers_When_Replaced_Then_TheHeldListIsKept()
        {
            // Arrange
            var held = new List<int> { 1, 2 };

            // Act
            var result = QueryStructuralSharing.Replace(held, new List<int> { 1, 2 });

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_ListsOfIntegersDifferingInAnElement_When_Replaced_Then_TheNewListIsTaken()
        {
            // Arrange
            var arrived = new List<int> { 1, 3 };

            // Act
            var result = QueryStructuralSharing.Replace(new List<int> { 1, 2 }, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        [Test]
        public void Given_ArraysOfIntegersDifferingInLength_When_Replaced_Then_TheNewArrayIsTaken()
        {
            // Arrange
            var arrived = new[] { 1, 2, 3 };

            // Act
            var result = QueryStructuralSharing.Replace(new[] { 1, 2 }, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        [Test]
        public void Given_AResolvedQuery_When_ARefetchLandsAnEqualList_Then_DataKeepsTheEarlierInstance()
        {
            // Arrange
            using var mounted = MountResolved(new List<int> { 1 });
            var first = Last().Data;
            Last().Refetch();

            // Act
            Land(1, new List<int> { 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ReferenceEquals(first, Last().Data), Is.True,
                "A component comparing the data by reference sees nothing change when nothing did");
        }

        [Test]
        public void Given_AFunctionReturningTheArrivedData_When_ARefetchLandsAnEqualList_Then_DataIsTheNewInstance()
        {
            // Arrange
            s_sharing = (_, arrived) => arrived;
            using var mounted = MountResolved(new List<int> { 1 });
            var first = Last().Data;
            Last().Refetch();

            // Act
            Land(1, new List<int> { 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ReferenceEquals(first, Last().Data), Is.False, "The function chooses the data, which turns the default off");
        }

        [Test]
        public void Given_AFunction_When_TheFirstRequestLands_Then_ItIsHandedNoHeldDataAndTheDataThatArrived()
        {
            // Arrange
            object? handedHeld = new object();
            object? handedArrived = null;
            var arrived = new List<Todo> { new(1, "a") };
            s_sharing = (held, next) =>
            {
                handedHeld = held;
                handedArrived = next;
                return next;
            };
            using var mounted = MountResolved(arrived);

            // Act
            var reading = (handedHeld == null, ReferenceEquals(handedArrived, arrived));

            // Assert
            Assert.That(reading, Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AFunction_When_ARefetchLands_Then_ItIsHandedTheDataTheEntryHeld()
        {
            // Arrange
            object? handedHeld = null;
            s_sharing = (held, next) =>
            {
                handedHeld = held;
                return next;
            };
            using var mounted = MountResolved(new List<Todo> { new(1, "a") });
            var first = Last().Data;
            Last().Refetch();

            // Act
            Land(1, new List<Todo> { new(2, "b") });

            // Assert
            Assert.That(ReferenceEquals(handedHeld, first), Is.True);
        }

        [Test]
        public void Given_AFunctionThatThrows_When_TheRequestLands_Then_TheQueryReportsTheError()
        {
            // Arrange
            s_sharing = (_, _) => throw new InvalidOperationException("sharing-failed");
            using var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();

            // Act
            Land(0, new List<Todo> { new(1, "a") });
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Last().Status, Last().Error?.Message), Is.EqualTo((QueryStatus.Error, "sharing-failed")),
                "A function that fails is a failed request, as v5 reports a structural sharing that throws");
        }

        [Test]
        public void Given_AComponentReadingData_When_AClassEqualByItsOwnEqualsLandsAsANewInstance_Then_ItRerenders()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DataReader, key: "reader"));
            mounted.FlushEffectsForTest();
            Land(0, new Entity(1));
            mounted.FlushStateForTest();
            var rendersBefore = s_renderCount;
            s_renders[s_renders.Count - 1].Refetch();

            // Act
            Land(1, new Entity(1));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount > rendersBefore, Is.True,
                "Data is a different instance, which is a change to a property the component read");
        }

        // Lists nested to the given depth around a string held by a new instance on every call.
        private static List<object> Chain(int depth)
        {
            object inner = new string('x', 1);
            for (var level = 0; level < depth; level++)
            {
                inner = new List<object> { inner };
            }
            return (List<object>)inner;
        }

        [Test]
        public void Given_AComponentReadingData_When_EqualTextLandsAsANewInstance_Then_ItDoesNotRerender()
        {
            // Arrange
            s_sharing = (_, arrived) => arrived;
            using var mounted = V.Mount(_root, V.Component(DataReader, key: "reader"));
            mounted.FlushEffectsForTest();
            Land(0, new string('a', 3));
            mounted.FlushStateForTest();
            var rendersBefore = s_renderCount;
            s_renders[s_renders.Count - 1].Refetch();

            // Act
            Land(1, new string('a', 3));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(rendersBefore), "v5 compares a string by value, so equal text is no change");
        }

        private MountedTree MountResolved(object data)
        {
            var mounted = V.Mount(_root, V.Component(Reader, key: "reader"));
            mounted.FlushEffectsForTest();
            Land(0, data);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static QueryResult<object> Last() => s_renders[s_renders.Count - 1];

        private static void Land(int request, object data) => s_sources[request].TrySetResult(data);

        private static VelvetTask<object> Fetch(CancellationToken token)
        {
            var source = new VelvetTaskCompletionSource<object>();
            s_sources.Add(source);
            return source.Task;
        }

        [Component]
        private static VNode Reader()
        {
            s_renders.Add(Hooks.UseQuery(
                new QueryOptions<object>(new QueryKey("todos"), Fetch)
                {
                    Retry = 0,
                    StructuralSharing = s_sharing,
                    NotifyOnChangeProps = QueryProperties.All,
                },
                s_client));
            return V.Label(text: "reader");
        }

        [Component]
        private static VNode DataReader()
        {
            var result = Hooks.UseQuery(
                new QueryOptions<object>(new QueryKey("todos"), Fetch) { Retry = 0, StructuralSharing = s_sharing },
                s_client);
            s_renderCount++;
            s_renders.Add(result);
            _ = result.Data;
            return V.Label(text: "data");
        }
    }
}
