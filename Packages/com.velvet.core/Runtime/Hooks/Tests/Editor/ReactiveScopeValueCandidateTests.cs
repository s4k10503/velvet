using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil.Cil;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which values a local is given the weaver caches in a scope of its own, by counting the value gates it
    /// writes into each component method and reading the <see cref="MemoizationReportAttribute"/> it leaves there.
    /// <list type="bullet">
    /// <item>Only a value the run computes is a candidate; a local copied from another is not.</item>
    /// <item>A value whose type can hold a node anywhere but in a node, an array of nodes or a list of nodes is declined,
    /// and a type nested nine levels deep is taken to hold one.</item>
    /// <item>A value handed on through locals, members or calls the build reads as pure is followed eight steps deep,
    /// and declined past that, or where it is handed to code that may write into it.</item>
    /// <item>A value that starts with the node it is built from is gated ahead of that node.</item>
    /// <item>A scope inside the run that stores a local does not make what that run stores worth caching.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeValueCandidateTests
    {
        private static int[] Build(int seed) => new[] { seed };

        private static MemoizationReportAttribute? ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeValueCandidateTests), methodName);

        // The indices, in instruction order, of the calls the woven method makes to the member of Hooks of that name.
        private static List<int> CallsTo(string methodName, string hookName)
        {
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeValueCandidateTests));
            var instructions = ReactiveScopeTestSupport
                .MethodOf(assembly, typeof(ReactiveScopeValueCandidateTests), methodName).Body.Instructions.ToList();
            var calls = new List<int>();
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode.Code == Code.Call
                    && instructions[i].Operand is Mono.Cecil.MethodReference callee
                    && callee.DeclaringType.FullName == typeof(Hooks).FullName
                    && callee.Name == hookName)
                {
                    calls.Add(i);
                }
            }
            return calls;
        }

        // -1 where the method carries no report, which no expected count is.
        private static int ValueGates(string methodName)
            => ReportOf(methodName) == null ? -1 : CallsTo(methodName, nameof(Hooks.TryGetValueScope)).Count;

        private static bool DeclinesTheValue(string methodName, string reason)
            => ReportOf(methodName)?.Bailouts.Any(note => note.StartsWith("the value stored in local", StringComparison.Ordinal)
                && note.EndsWith(reason, StringComparison.Ordinal)) ?? false;

        #region What Cecil says of a generic parameter

        // GREEN_ON_BASE(characterization): Cecil reads a generic parameter as a type that contains one, which a `MUTANT_SURVIVES` in `CachesAsValue` rests on.
        [Test]
        public void Given_AGenericParameter_When_AskedWhetherItContainsOne_Then_ItSaysItDoes()
        {
            // Arrange
            var parameter = new Mono.Cecil.GenericParameter("T", new Mono.Cecil.TypeReference("Synthetic", "Owner", null, null));

            // Act
            var contains = parameter.ContainsGenericParameter;

            // Assert
            Assert.That(contains, Is.True);
        }

        #endregion

        #region A local copied from another

        [Component]
        private static VNode CopiesAProp(int[] rows, int other)
        {
            var copy = rows;
            return V.Div("copied", V.Text(string.Join(",", copy)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches no value that is only a copy of a prop.
        // Removing the `ComputesValue` clause from `FindValues`, reading `==` as `!=` where `ComputesValue` compares
        // with `newarr`, or returning true at its end, makes the copy a candidate and gates it.
        [Test]
        public void Given_ALocalCopiedFromAPropAndReadByALabel_When_Compiled_Then_NoValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(CopiesAProp));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        #endregion

        #region A computed object that holds no node

        private sealed class Row
        {
            public int Value;
        }

        private static Row MakeRow(int seed) => new() { Value = seed };

        [Component]
        private static VNode ComputesARow(int seed, int other)
        {
            var row = MakeRow(seed);
            return V.Div("row", V.Text(row.ToString()), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches a computed object of a class whose fields hold no node.
        // Removing the `FieldsReachNodes` clause from `ReachesNodes`, reading `==` as `!=` where `FieldsReachNodes`
        // tests the resolved definition, or `!=` as `==` where it compares the base type with `System.Object`, reads
        // the class as one that can hold a node and declines it.
        [Test]
        public void Given_AComputedObjectWhoseFieldsHoldNoNode_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(ComputesARow));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Component]
        private static VNode ComputesAList(int seed, int other)
        {
            var rows = Build(seed).ToList();
            return V.Div("list", V.Text(string.Join(",", rows)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches a computed list of numbers, which is not an array.
        // Removing the `DerivesFromVNode` clause from `CachesAsValue` leaves only arrays as candidates.
        [Test]
        public void Given_AComputedListOfNumbers_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(ComputesAList));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        private sealed class Leaf
        {
            public int Value;
        }

        private sealed class Pair
        {
            public Leaf First = new();
            public Leaf Second = new();
        }

        private static Pair MakePair(int seed) => new();

        [Component]
        private static VNode ComputesAPairOfLeaves(int seed, int other)
        {
            var pair = MakePair(seed);
            return V.Div("pair", V.Text(pair.ToString()), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches an object whose two fields are of one class that holds no node.
        // Returning true where `ReachesNodes` meets a type it has already seen reads the second field as one that can
        // hold a node and declines the value.
        [Test]
        public void Given_AComputedObjectWithTwoFieldsOfOneClass_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(ComputesAPairOfLeaves));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        #endregion

        #region Nodes in an array and in a list

        [Component]
        private static VNode ComputesAnArrayOfNodes(string name, int other)
        {
            var nodes = Enumerable.Repeat<VNode>(V.Text(name), 2).ToArray();
            return V.Div("outer", V.Div("inner", nodes), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches an array of nodes, whose nodes the sweep keeps out of the pool.
        // Removing the `IsNodeList` guard from `NodeHolderProblem`, or reading `==` as `!=` where `IsNodeList` tests the
        // array's rank, declines it as a value that can hold nodes.
        [Test]
        public void Given_AComputedArrayOfNodes_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(ComputesAnArrayOfNodes));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base gates the array ahead of the node it is built from, whose run starts where its own does.
        // The first scope gate is the one on the body. Removing the sort of the kept scopes from
        // `ReactiveScopePlan.Select` leaves the values after every node, so the node's gate comes first.
        [Test]
        public void Given_AnArrayOfNodesThatStartsWithTheNodeItHolds_When_Compiled_Then_ItsGateComesBeforeThatNodesGate()
        {
            // Arrange
            var scopeGates = CallsTo(nameof(ComputesAnArrayOfNodes), nameof(Hooks.TryGetScope));

            // Act
            var valueGate = CallsTo(nameof(ComputesAnArrayOfNodes), nameof(Hooks.TryGetValueScope)).Single();

            // Assert
            Assert.That(valueGate, Is.LessThan(scopeGates[1]));
        }

        [Component]
        private static VNode ComputesAListOfNodes(string name, int other)
        {
            var nodes = Enumerable.Repeat<VNode>(V.Text(name), 2).ToList();
            return V.Div("outer", V.Div("inner", Enumerable.ToArray(nodes)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches a list of nodes, whose nodes the sweep keeps out of the pool.
        // Reading `==` as `!=` where `IsNodeList` compares the generic type with `List`1`, or where it counts the type
        // arguments, declines it as a value that can hold nodes.
        [Test]
        public void Given_AComputedListOfNodes_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(ComputesAListOfNodes));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        #endregion

        #region Types nested to the depth the walk follows

        private sealed class C0 { public C1 Next = null!; }
        private sealed class C1 { public C2 Next = null!; }
        private sealed class C2 { public C3 Next = null!; }
        private sealed class C3 { public C4 Next = null!; }
        private sealed class C4 { public C5 Next = null!; }
        private sealed class C5 { public C6 Next = null!; }
        private sealed class C6 { public C7 Next = null!; }
        private sealed class C7 { public C8 Next = null!; }
        private sealed class C8 { public int Value; }

        private static C0 MakeChain(int seed) => new();

        [Component]
        private static VNode ComputesAChainOfNineClasses(int seed, int other)
        {
            var chain = MakeChain(seed);
            return V.Div("chain", V.Text(chain.ToString()), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base takes a class reached through eight fields to hold a node.
        // Reading `>=` as `>` where `ReachesNodes` compares the depth with `MaxAliasDepth`, or `+` as `-` where
        // `FieldsReachNodes` steps into a field, follows the chain to its end and caches the value.
        [Test]
        public void Given_AComputedObjectWhoseFieldsNestNineClassesDeep_When_Compiled_Then_TheReportSaysItCanHoldNodes()
        {
            // Act
            var declined = DeclinesTheValue(nameof(ComputesAChainOfNineClasses), "it can hold nodes that nothing keeps out of the pool");

            // Assert
            Assert.That(declined, Is.True);
        }

        private static int[][][][][][][][][] MakeGrid(int seed) => new int[1][][][][][][][][];

        [Component]
        private static VNode ComputesAnArrayNestedNineDeep(int seed, int other)
        {
            var grid = MakeGrid(seed);
            return V.Div("grid", V.Text(grid.ToString()), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base takes an array nested nine deep to hold a node.
        // Reading `+` as `-` where `ReachesNodes` steps into an array's elements follows the arrays to the number
        // they hold and caches the value.
        [Test]
        public void Given_AComputedArrayNestedNineDeep_When_Compiled_Then_TheReportSaysItCanHoldNodes()
        {
            // Act
            var declined = DeclinesTheValue(nameof(ComputesAnArrayNestedNineDeep), "it can hold nodes that nothing keeps out of the pool");

            // Assert
            Assert.That(declined, Is.True);
        }

        private static List<List<List<List<List<List<List<List<List<int>>>>>>>>> MakeNest(int seed) => new();

        [Component]
        private static VNode ComputesAListNestedNineDeep(int seed, int other)
        {
            var nest = MakeNest(seed);
            return V.Div("nest", V.Text(nest.ToString()), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base takes a list nested nine deep to hold a node.
        // Reading `+` as `-` where `ReachesNodes` steps into a type argument follows the lists to the number they
        // hold and caches the value.
        [Test]
        public void Given_AComputedListNestedNineDeep_When_Compiled_Then_TheReportSaysItCanHoldNodes()
        {
            // Act
            var declined = DeclinesTheValue(nameof(ComputesAListNestedNineDeep), "it can hold nodes that nothing keeps out of the pool");

            // Assert
            Assert.That(declined, Is.True);
        }

        #endregion

        #region Following a value through what reads it

        private sealed class Link
        {
            public Link Next = null!;
            public int Value;
        }

        private static Link MakeLink(int seed) => new();

        [Component]
        private static VNode ReadsAValueNineMembersDeep(int seed, int other)
        {
            var link = MakeLink(seed);
            return V.Div("deep",
                V.Text(link.ToString()),
                V.Text(Convert.ToString(link.Next.Next.Next.Next.Next.Next.Next.Next.Value)),
                V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base stops following a value read through nine members in a row.
        // Reading `>=` as `>` where `ConsumerProblem` compares the depth of a member read with `MaxAliasDepth` follows
        // the ninth and caches the value.
        [Test]
        public void Given_AValueReadThroughNineMembersInARow_When_Compiled_Then_TheReportSaysItIsPassedOnTooDeeply()
        {
            // Act
            var declined = DeclinesTheValue(nameof(ReadsAValueNineMembersDeep), "is passed on too deeply to follow");

            // Assert
            Assert.That(declined, Is.True);
        }

        [Component]
        private static VNode AliasesAValueNineTimes(int seed, int other)
        {
            var rows = Build(seed);
            var a1 = rows;
            var a2 = a1;
            var a3 = a2;
            var a4 = a3;
            var a5 = a4;
            var a6 = a5;
            var a7 = a6;
            var a8 = a7;
            var a9 = a8;
            return V.Div("chained", V.Text(string.Join(",", rows)), V.Text(string.Join(",", a9)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base stops following a value handed through nine locals in a row.
        // Reading `>=` as `>` where `AliasProblem` compares the depth with `MaxAliasDepth`, or `+` as `-` where it
        // steps into the next local, follows the ninth and caches the value.
        [Test]
        public void Given_AValueHandedThroughNineLocalsInARow_When_Compiled_Then_TheReportSaysItIsPassedOnTooDeeply()
        {
            // Act
            var declined = DeclinesTheValue(nameof(AliasesAValueNineTimes), "is passed on too deeply to follow");

            // Assert
            Assert.That(declined, Is.True);
        }

        [Component]
        private static VNode AliasesAValueBackIntoItself(int seed, int other)
        {
            var rows = Build(seed);
            var alias = rows;
            rows = alias;
            return V.Div("aliased", V.Text(string.Join(",", rows)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base caches a value handed to another local and back.
        // Removing the guard on the locals already seen from `AliasProblem` follows the two locals round until the
        // depth runs out, and declines the value as passed on too deeply.
        [Test]
        public void Given_AValueHandedToAnotherLocalAndBack_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(AliasesAValueBackIntoItself));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Component]
        private static VNode PassesAValueThroughNinePureCalls(int seed, int other)
        {
            var rows = Build(seed);
            return V.Div("pure",
                V.Text(string.Join(",", rows)),
                V.Text(Convert.ToString(rows.AsEnumerable().AsEnumerable().AsEnumerable().AsEnumerable().AsEnumerable()
                    .AsEnumerable().AsEnumerable().AsEnumerable().AsEnumerable().Count())),
                V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base stops following a value through nine pure calls that each return what may be it.
        // Reading `>=` as `>` where `CallProblem` compares the depth with `MaxAliasDepth`, or `+` as `-` where it steps
        // into the result, follows the ninth and caches the value.
        [Test]
        public void Given_AValueHandedThroughNinePureCallsInARow_When_Compiled_Then_TheReportSaysItIsPassedOnTooDeeply()
        {
            // Act
            var declined = DeclinesTheValue(nameof(PassesAValueThroughNinePureCalls), "is passed on too deeply to follow");

            // Assert
            Assert.That(declined, Is.True);
        }

        [Component]
        private static VNode SumsAValue(int seed, int other)
        {
            var rows = Build(seed);
            return V.Div("summed", V.Text(string.Join(",", rows)), V.Text(Convert.ToString(rows.Sum())), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base stops following a value at a pure call whose result is a number.
        // Reading `==` as `!=` where `CallProblem` tells a construction from a call takes the type the sum is declared
        // on for its result, and follows the number on to where the node built from it is stored into the array of
        // children, which declines the value.
        [Test]
        public void Given_AValueSummedByAPureCall_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(SumsAValue));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Component]
        private static VNode PicksAValueInAConditional(int seed, int other)
        {
            var rows = Build(seed);
            var shown = other > 0 ? rows : Array.Empty<int>();
            return V.Div("arm", V.Text(string.Join(",", rows)), V.Text(string.Join(",", shown)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base declines a value one arm of a conditional reads, since nothing takes that read off the stack as it is.
        // Reading `<` as `<=` where `ConsumerProblem` bounds its search for the instruction that takes the read reads
        // past the last instruction, throws, and leaves the method unwoven with no report.
        [Test]
        public void Given_AValueReadByOneArmOfAConditional_When_Compiled_Then_TheReportSaysItIsLeftOnTheStack()
        {
            // Act
            var declined = DeclinesTheValue(nameof(PicksAValueInAConditional), "is left on the stack");

            // Assert
            Assert.That(declined, Is.True);
        }

        private static int[] Grow(int[] rows) => rows;

        [Component]
        private static VNode HandsAValueToItsOwnNextValue(int seed, int other)
        {
            var rows = Build(seed);
            var shifted = other + 1;
            rows = Grow(rows);
            return V.Div("regrown", V.Text(string.Join(",", rows)), V.Text(Convert.ToString(shifted)));
        }

        // GREEN_ON_BASE(characterization): the base caches a value its own run computes from the last value of its local.
        // Reading `>=` as `>` where `ReadsOnly` skips the reads inside the run takes the run's first instruction for
        // one outside it, and declines the value because that read is handed to a call that may write into it.
        [Test]
        public void Given_AValueComputedFromTheLastValueOfItsOwnLocal_When_Compiled_Then_OneValueGateIsWritten()
        {
            // Act
            var gates = ValueGates(nameof(HandsAValueToItsOwnNextValue));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        #endregion

        #region A value computed inside the run of another

        private static int[] Wrap(VNode node, int count) => new[] { count };

        [Component]
        private static VNode ComputesAValueInsideTheRunOfAnother(int seed, int other)
        {
            var rows = Build(seed);
            var shifted = other + 1;
            var inner = Build(rows[0]);
            rows = Wrap(V.Text(string.Join(",", inner)), inner.Length);
            return V.Div("nested", V.Text(Convert.ToString(shifted)));
        }

        // The run that stores `rows` again holds the statement that computes `inner`, and the scope reading `inner`
        // sits inside it too. Only `inner` is read by a scope; what `rows` is given there nothing reads.
        [Test]
        public void Given_AValueComputedInsideTheRunThatStoresAnotherLocal_When_Compiled_Then_OnlyTheValueAScopeReadsIsGated()
        {
            // Act
            var gates = ValueGates(nameof(ComputesAValueInsideTheRunOfAnother));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        #endregion
    }
}
