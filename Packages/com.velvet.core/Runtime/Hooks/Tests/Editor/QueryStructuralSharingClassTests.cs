#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies structural sharing into the classes and records an application declares, TanStack Query's
    /// <c>replaceEqualDeep</c> reaching the properties of a changed object.
    /// <list type="bullet">
    /// <item>An instance whose fields are all equal is the one already held, and a changed one is a copy of the
    /// instance that arrived carrying the held instance of each member that is equal, at any depth.</item>
    /// <item>The instance that arrived is never written, a member that reaches its own instance is not entered
    /// again, and a class the framework declares is kept only as the very instance held.</item>
    /// </list>
    /// <see cref="UseQueryStructuralSharingTests"/> owns the collections and the query that applies the
    /// sharing.
    /// </summary>
    [TestFixture]
    internal sealed class QueryStructuralSharingClassTests
    {
        private sealed record Pair(List<int> Left, List<int> Right);

        private sealed class Leaf
        {
            public string Name = "";
            public int[] Numbers = new int[0];
        }

        private sealed class Branch
        {
            public Leaf First = new();
            public Leaf Second = new();
            public int Count;
        }

        private class Base
        {
            private readonly List<int> _hidden;

            protected Base(List<int> hidden)
            {
                _hidden = hidden;
            }

            public List<int> Hidden => _hidden;
        }

        private sealed class Derived : Base
        {
            public Derived(List<int> hidden, int own) : base(hidden)
            {
                Own = own;
            }

            public int Own;
        }

        // Counts every comparison of it, which is how a walk is told to have entered an instance again.
        private struct Probe
        {
            public static int Comparisons;

            public override bool Equals(object? obj)
            {
                Comparisons++;
                return obj is Probe;
            }

            public override int GetHashCode() => 0;
        }

        private sealed class Cyclic
        {
            public Probe Probe;
            public Cyclic? Next;
        }

        private delegate void Handler();

        private sealed class HandlerTarget
        {
            public void Run()
            {
            }
        }

        private sealed class Asset : ScriptableObject
        {
            public string Name = "";
        }

        private sealed class Link
        {
            public Link? Next;
            public string Tag = "";
        }

        private sealed class TextHolder
        {
            public System.Text.StringBuilder Text = new();
        }

        private static Leaf MakeLeaf(string name, params int[] numbers) => new() { Name = name, Numbers = numbers };

        [Test]
        public void Given_ARecordWithOneEqualAndOneChangedList_When_Replaced_Then_TheEqualListKeepsItsInstance()
        {
            // Arrange
            var held = new Pair(new List<int> { 1 }, new List<int> { 2 });
            var arrived = new Pair(new List<int> { 1 }, new List<int> { 3 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result.Left, held.Left), Is.True, "The member that is equal is the held instance");
        }

        // GREEN_ON_BASE(characterization): the base keeps the arrived record whole, so its changed list is the arrived one there too.
        [Test]
        public void Given_ARecordWithOneEqualAndOneChangedList_When_Replaced_Then_TheChangedListIsTheOneThatArrived()
        {
            // Arrange
            var held = new Pair(new List<int> { 1 }, new List<int> { 2 });
            var arrived = new Pair(new List<int> { 1 }, new List<int> { 3 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result.Right, arrived.Right), Is.True, "The member that changed is the new instance");
        }

        [Test]
        public void Given_ARecordWithOneEqualAndOneChangedList_When_Replaced_Then_TheResultIsACopyOfNeitherInstance()
        {
            // Arrange
            var held = new Pair(new List<int> { 1 }, new List<int> { 2 });
            var arrived = new Pair(new List<int> { 1 }, new List<int> { 3 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result, held), ReferenceEquals(result, arrived)), Is.EqualTo((false, false)));
        }

        // GREEN_ON_BASE(characterization): the base writes no instance at all, so the one that arrived is as it was returned there too.
        [Test]
        public void Given_AChangedRecord_When_Replaced_Then_TheInstanceThatArrivedIsNotWritten()
        {
            // Arrange
            var held = new Pair(new List<int> { 1 }, new List<int> { 2 });
            var arrived = new Pair(new List<int> { 1 }, new List<int> { 3 });
            var arrivedLeft = arrived.Left;

            // Act
            _ = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(arrived.Left, arrivedLeft), Is.True,
                "Sharing builds a copy and leaves the instance the query function returned as it was");
        }

        [Test]
        public void Given_ARecordWithEveryFieldEqual_When_Replaced_Then_TheHeldInstanceIsKept()
        {
            // Arrange
            var held = new Pair(new List<int> { 1 }, new List<int> { 2 });
            var arrived = new Pair(new List<int> { 1 }, new List<int> { 2 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True);
        }

        [Test]
        public void Given_AClassOfClassMembers_When_OnlyOneMemberChanges_Then_TheEqualSiblingKeepsItsInstance()
        {
            // Arrange
            var held = new Branch { First = MakeLeaf("a", 1), Second = MakeLeaf("b", 2), Count = 1 };
            var arrived = new Branch { First = MakeLeaf("a", 1), Second = MakeLeaf("B", 2), Count = 1 };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result.First, held.First), result.Second.Name), Is.EqualTo((true, "B")));
        }

        [Test]
        public void Given_AMemberChangedDeepInside_When_Replaced_Then_ItsEqualPartKeepsItsInstance()
        {
            // Arrange
            var held = new Branch { First = MakeLeaf("a", 1, 2), Second = MakeLeaf("b", 2) };
            var arrived = new Branch { First = MakeLeaf("A", 1, 2), Second = MakeLeaf("b", 2) };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result.First.Numbers, held.First.Numbers), result.First.Name),
                Is.EqualTo((true, "A")), "The array inside the changed member is the held one");
        }

        [Test]
        public void Given_AChangedNumberAndAnEqualMember_When_Replaced_Then_ThatMemberKeepsItsInstanceAndTheNumberIsNew()
        {
            // Arrange
            var held = new Branch { First = MakeLeaf("a"), Count = 1 };
            var arrived = new Branch { First = MakeLeaf("a"), Count = 2 };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((result.Count, ReferenceEquals(result.First, held.First)), Is.EqualTo((2, true)));
        }

        [Test]
        public void Given_AListOfClasses_When_OneElementChanges_Then_TheEqualElementsKeepTheirInstances()
        {
            // Arrange
            var held = new List<Leaf> { MakeLeaf("a"), MakeLeaf("b") };
            var arrived = new List<Leaf> { MakeLeaf("a"), MakeLeaf("B") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result[0], held[0]), ReferenceEquals(result[1], arrived[1])), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AFieldDeclaredInABaseClass_When_ItIsEqualAndAFieldOfTheDerivedClassChanges_Then_TheBaseFieldKeepsItsInstance()
        {
            // Arrange
            var held = new Derived(new List<int> { 1 }, 1);
            var arrived = new Derived(new List<int> { 1 }, 2);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That((ReferenceEquals(result.Hidden, held.Hidden), result.Own), Is.EqualTo((true, 2)),
                "A private field of a base class is shared too");
        }

        [Test]
        public void Given_AClassThatReachesItself_When_Replaced_Then_ItsMembersAreComparedOnce()
        {
            // Arrange
            var held = new Cyclic();
            held.Next = held;
            var arrived = new Cyclic();
            arrived.Next = arrived;
            Probe.Comparisons = 0;

            // Act
            _ = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(Probe.Comparisons, Is.EqualTo(1), "An instance already being compared is not entered again");
        }

        [Test]
        public void Given_EqualChainsOfFiveHundredLinks_When_Replaced_Then_TheHeldChainIsKept()
        {
            // Arrange
            var held = MakeChain(500);
            var arrived = MakeChain(500);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, held), Is.True, "The walk reaches the text at the end of the chain, which is level five hundred");
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, however deep a chain of them runs.
        [Test]
        public void Given_EqualChainsOfFiveHundredAndOneLinks_When_Replaced_Then_TheNewChainIsTaken()
        {
            // Arrange
            var held = MakeChain(501);
            var arrived = MakeChain(501);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True, "Descent stops at level five hundred, as it does for a list");
        }

        // Links nested to the given depth, each holding a text held by a new instance on every call.
        private static Link MakeChain(int links)
        {
            Link? head = null;
            for (var level = 0; level < links; level++)
            {
                head = new Link { Next = head, Tag = new string('x', 1) };
            }
            return head!;
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, a framework one included.
        [Test]
        public void Given_AClassTheFrameworkDeclares_When_ReplacedByAnEqualInstance_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            var held = new System.Text.StringBuilder("a");
            var arrived = new System.Text.StringBuilder("a");

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True, "A framework class is kept only as the very instance held");
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, a delegate included.
        [Test]
        public void Given_AnApplicationDelegateType_When_ReplacedByAnEqualDelegate_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            var target = new HandlerTarget();
            var held = new Handler(target.Run);
            var arrived = new Handler(target.Run);

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, an engine object included.
        [Test]
        public void Given_AnEngineObjectOfAnApplicationType_When_ReplacedByOneWithEqualFields_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            var held = ScriptableObject.CreateInstance<Asset>();
            var arrived = ScriptableObject.CreateInstance<Asset>();
            held.Name = "a";
            arrived.Name = "a";

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, so this holder is the arrived one there too.
        [Test]
        public void Given_AClassHoldingAFrameworkObject_When_ReplacedByOneHoldingAnEqualObject_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            var held = new TextHolder { Text = new System.Text.StringBuilder("a") };
            var arrived = new TextHolder { Text = new System.Text.StringBuilder("a") };

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True, "Nothing in it can be shared, so the arrived instance stands");
        }

        // GREEN_ON_BASE(characterization): the base takes the new instance of every class, so classes of two types are no different there.
        [Test]
        public void Given_AHeldClassAndAnArrivedOneOfAnotherType_When_Replaced_Then_TheNewInstanceIsTaken()
        {
            // Arrange
            Base held = new Derived(new List<int> { 1 }, 1);
            Base arrived = new BaseLike(new List<int> { 1 });

            // Act
            var result = QueryStructuralSharing.Replace(held, arrived);

            // Assert
            Assert.That(ReferenceEquals(result, arrived), Is.True);
        }

        private sealed class BaseLike : Base
        {
            public BaseLike(List<int> hidden) : base(hidden)
            {
            }
        }
    }
}
