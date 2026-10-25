using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which runs of a component body the weaver cuts out as scopes of their own and which it leaves
    /// uncached, by reading the <see cref="MemoizationReportAttribute"/> it leaves on each method and the woven IL.
    /// <list type="bullet">
    /// <item>A custom hook composing only value hooks is a dependency like a state, and a framework member outside the
    /// pure list reads state it was not handed.</item>
    /// <item>A hook value a reference to it was taken of is volatile; one a getter of its own type reads is not.</item>
    /// <item>A store into an object the run did not make and a local the code after the run reads get a run declined; a
    /// conditional expression that opens the body is cut together with its test and is not.</item>
    /// <item>A value type built in place is local to the run around it, so it is not a dependency of that run.</item>
    /// <item>A body whose tail is reached by a branch, and a protected region ahead of or around a run, leave the
    /// runs cut and the gates where the IL stays valid.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The report fields are what the weaver decided about a method, so a count here is not a count of what a render
    /// did. The one case that renders is the member read off a state object.
    /// </remarks>
    [TestFixture]
    internal sealed class ReactiveScopeSelectionTests
    {
        private const string CodeGenAssemblyName = "Unity.Velvet.CodeGen";

        private static readonly int[] s_calls = new int[10];
        private static Action<NamedItem> s_setItem = null!;

        [SetUp]
        public void SetUp()
        {
            Array.Clear(s_calls, 0, s_calls.Length);
            s_setItem = null!;
        }

        private static string Describe(int slot, object? value)
        {
            s_calls[slot]++;
            return slot + ":" + value;
        }

        private static MemoizationReportAttribute ReportOf(string methodName)
        {
            var method = typeof(ReactiveScopeSelectionTests).GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method!.GetCustomAttribute<MemoizationReportAttribute>()!;
        }

        #region A custom hook

        private static int UseCounter()
        {
            var (count, _) = Hooks.UseState(0);
            return count;
        }

        [Component]
        private static VNode ReadsACustomHookResult()
        {
            var counted = UseCounter();
            var (other, _) = Hooks.UseState(0);
            return V.Div("custom-hook",
                V.Label(text: Describe(0, counted)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base caches the body and both labels that read a custom hook's result.
        // Reading `!= 0` as `== 0` in `HookKindOf` makes that result volatile and reddens it.
        [Test]
        public void Given_ACustomHookComposingOnlyValueHooks_When_Compiled_Then_TheBodyAndBothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(ReadsACustomHookResult));

            // Assert
            Assert.That((report.BodyGate, report.Scopes, report.Bailouts.Count), Is.EqualTo((true, 2, 0)));
        }

        #endregion

        #region A framework member outside the pure list

        [Component]
        private static VNode ReadsAnEnvironmentVariable()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            return V.Div("ambient",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, System.Environment.GetEnvironmentVariable("VELVET_UNSET_VARIABLE"))),
                V.Label(text: Describe(2, second)));
        }

        // GREEN_ON_BASE(characterization): the base leaves uncached a label that calls a framework static outside the pure list.
        // Removing the `IsFrameworkType` return from `OfPlainCall`, or the `IsPureFramework` clause above it, reddens it.
        [Test]
        public void Given_AFrameworkStaticTakingAnArgument_When_ItIsOutsideThePureList_Then_TheReportSaysItReadsStateItWasNotHanded()
        {
            // Act
            var report = ReportOf(nameof(ReadsAnEnvironmentVariable));

            // Assert
            Assert.That(
                report.Bailouts.Any(note => note.Contains("GetEnvironmentVariable, which reads state it was not handed")),
                Is.True);
        }

        #endregion

        #region A hook value reached through its address

        [Component]
        private static VNode ReadsAHookValueThroughARefLocal()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            ref int alias = ref first;
            alias += 1;
            return V.Div("ref-local",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base leaves uncached the label reading a hook value a ref local aliases.
        // Reading `< 0` as `<= 0` in `PropagateAddress` reddens it while the aliased hook value is local 0.
        [Test]
        public void Given_AHookValueAliasedByARefLocal_When_Compiled_Then_TheReportSaysAMutationReachesIt()
        {
            // Act
            var report = ReportOf(nameof(ReadsAHookValueThroughARefLocal));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("an opaque value or a mutation reaches")), Is.True);
        }

        private struct Point
        {
            public int X { get; set; }
        }

        [Component]
        private static VNode ReadsAPropertyOfAStructState()
        {
            var (point, _) = Hooks.UseState(new Point());
            var (second, _) = Hooks.UseState(0);
            return V.Div("struct-getter",
                V.Label(text: Describe(0, point.X)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base reads a getter called on a struct state's address as a read.
        // Nothing is left uncached, and reading `==` as `!=` in `ReadsOnly` makes that address volatile and reddens it.
        [Test]
        public void Given_APropertyReadOffAStructState_When_Compiled_Then_NothingIsLeftUncached()
        {
            // Act
            var report = ReportOf(nameof(ReadsAPropertyOfAStructState));

            // Assert
            Assert.That((report.BodyGate, report.Scopes, report.Bailouts.Count), Is.EqualTo((true, 2, 0)));
        }

        #endregion

        #region A conditional expression that opens the body

        [Component]
        private static VNode ChoosesTheClassNameOfTheOnlyLabel(bool flag, string text)
            => V.Label(className: flag ? "on" : "off", text: text);

        // GREEN_ON_BASE(characterization): the base cuts a call whose first argument is a conditional expression.
        // The expression begins at the body's first instruction, and reading `entry < 0` as `entry <= 0` in
        // `ExtendOverEntries` leaves its test outside the run, which the report then lists as declined.
        [Test]
        public void Given_AConditionalExpressionOpeningTheBody_When_Compiled_Then_NoCandidateIsDeclined()
        {
            // Act
            var report = ReportOf(nameof(ChoosesTheClassNameOfTheOnlyLabel));

            // Assert
            Assert.That(report.Bailouts, Is.Empty);
        }

        #endregion

        #region Stores and locals

        [Component]
        private static VNode StoresIntoAnArrayAheadOfTheTree()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            var buffer = new string[1];
            return V.Div("stores",
                V.Label(text: buffer[0] = Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base declines the label whose text is stored into an array it did not make.
        // Reading `==` as `!=` where `ReactiveScopeFlow.Produce` marks a value as made by the run reddens it.
        [Test]
        public void Given_AStoreIntoAnArrayTheRunDidNotMake_When_Compiled_Then_TheReportSaysSo()
        {
            // Act
            var report = ReportOf(nameof(StoresIntoAnArrayAheadOfTheTree));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("it writes into an object it did not create")), Is.True);
        }

        [Component]
        private static VNode ReadsALocalAfterTheScopeThatAssignedIt()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            string? text = null;
            var assigning = V.Label(text: text = Describe(0, first));
            var other = V.Label(text: Describe(1, second));
            return V.Div("reads-after", assigning, other, V.Label(text: text));
        }

        // GREEN_ON_BASE(characterization): the base declines the label that assigns a local the code after it reads.
        // Emptying the list of local accesses `IndexAccesses` builds reddens it, by removing the `list.Add` or by
        // reading `kind == null` as `kind != null`.
        [Test]
        public void Given_ALocalReadAfterTheScopeThatAssignedIt_When_Compiled_Then_TheReportSaysTheScopeIsNotCached()
        {
            // Act
            var report = ReportOf(nameof(ReadsALocalAfterTheScopeThatAssignedIt));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("holds a value the code after it reads")), Is.True);
        }

        private struct Pair
        {
            public int X;
            public int Y;

            public Pair(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [Component]
        private static VNode LabelsBesideAStructBuiltInPlace(int a)
        {
            var pair = new Pair(a, 1);
            return V.Div("in-place",
                V.Label(text: Describe(0, a)),
                V.Label(text: Describe(1, pair) + Describe(3, a)),
                V.Label(text: Describe(2, a)));
        }

        // GREEN_ON_BASE(characterization): the base treats a struct a statement builds in place as local to the run.
        // The Div then depends on the parameter alone, so only the label reading the struct is cached on its own.
        // Reading `==` as `!=` in `InitializesLocal`, `< 0` as `<= 0` where `Normalize` looks for the consumer of
        // the address, or removing the `_internalLocals` return from `ReadLocal` makes the struct a dependency of the
        // Div and reddens it. So does emptying the list of local accesses `IndexAccesses` builds.
        [Test]
        public void Given_AStructBuiltInPlaceBesideTwoLabelsOnTheParameterAlone_When_Compiled_Then_OnlyTheLabelReadingTheStructIsCachedOnItsOwn()
        {
            // Act
            var report = ReportOf(nameof(LabelsBesideAStructBuiltInPlace));

            // Assert
            Assert.That(report.Scopes, Is.EqualTo(1));
        }

        [Component]
        private static VNode TheOnlyLabelReadsAStructBuiltInPlace(int a)
        {
            var pair = new Pair(a, 1);
            return V.Div("only-child", V.Label(text: Describe(1, pair)));
        }

        // GREEN_ON_BASE(characterization): the base caches the one label reading the struct, which depends on a local.
        // The Div depends on the parameter, so the two have as many dependencies under other names. Removing
        // `Key = key;` from `ScopeDependency` makes every pair of equal count read as the same and reddens it.
        [Test]
        public void Given_AStructBuiltInPlaceReadByTheOnlyLabel_When_Compiled_Then_ThatLabelIsCachedOnItsOwn()
        {
            // Act
            var report = ReportOf(nameof(TheOnlyLabelReadsAStructBuiltInPlace));

            // Assert
            Assert.That(report.Scopes, Is.EqualTo(1));
        }

        #endregion

        #region Branches and protected regions

        [Component]
        private static VNode TryAheadOfTheTail()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            try
            {
                Describe(9, first);
            }
            catch (Exception)
            {
            }
            return V.Div("try-ahead",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base caches the body and both labels after a try statement.
        // Reading `!=` as `==` where `CheckHandlers` compares what covers the two ends declines every run in a body
        // holding a handler, and removing the `Send` of an unconditional branch from `ReactiveScopeFlow.Continue`
        // leaves the tail unreached. Either reddens it.
        [Test]
        public void Given_ATryAheadOfTheTail_When_Compiled_Then_TheBodyAndBothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(TryAheadOfTheTail));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 2)));
        }

        [Component]
        private static VNode SwitchAheadOfTheTailThrowing()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            switch (first)
            {
                case 0:
                    break;
                default:
                    throw new InvalidOperationException();
            }
            return V.Div("switch-ahead",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base caches the body and both labels after a switch whose other arm throws.
        // Removing the `Send` of an unconditional branch from `ReactiveScopeFlow.Continue` reddens it in a build without
        // optimization.
        [Test]
        public void Given_ASwitchWhoseOtherArmThrowsAheadOfTheTail_When_Compiled_Then_TheBodyAndBothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(SwitchAheadOfTheTailThrowing));

            // Assert
            Assert.That((report.BodyGate, report.Scopes), Is.EqualTo((true, 2)));
        }

        [Component]
        private static VNode ScopeInsideATry()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            VNode? built;
            try
            {
                built = V.Label(text: Describe(0, first));
            }
            catch (Exception)
            {
                built = null;
            }
            return V.Div("scope-in-try", built, V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base lets no branch from outside a protected region land inside it midway.
        // Reading `||` as `&&` in `CheckHandlers` lets a run begin where the try does, which puts its gate ahead of
        // the try and its branch into it, and reddens it.
        [Test]
        public void Given_AScopeInsideATry_When_Compiled_Then_NoBranchEntersAProtectedRegionMidway()
        {
            // Arrange
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(ReactiveScopeSelectionTests).Assembly.Location);
            var method = assembly.MainModule.GetType(typeof(ReactiveScopeSelectionTests).FullName).Methods
                .Single(each => each.Name == nameof(ScopeInsideATry));

            // Act
            var enters = EntersAProtectedRegionMidway(method);

            // Assert
            Assert.That(enters, Is.False);
        }

        private static bool EntersAProtectedRegionMidway(MethodDefinition method)
            => method.Body.ExceptionHandlers.Any(handler =>
                EntersMidway(method, handler.TryStart, handler.TryEnd)
                || EntersMidway(method, handler.HandlerStart, handler.HandlerEnd));

        // Whether a branch from outside [first, end) lands inside it anywhere but on `first`.
        private static bool EntersMidway(MethodDefinition method, Instruction first, Instruction? end)
        {
            var instructions = method.Body.Instructions;
            var from = instructions.IndexOf(first);
            var to = end == null ? instructions.Count : instructions.IndexOf(end);
            for (var i = 0; i < instructions.Count; i++)
            {
                if (i >= from && i < to) continue;
                if (TargetsOf(instructions[i]).Select(target => instructions.IndexOf(target))
                    .Any(at => at > from && at < to))
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<Instruction> TargetsOf(Instruction instruction)
        {
            if (instruction.Operand is Instruction one) return new[] { one };
            if (instruction.Operand is Instruction[] many) return many;
            return Array.Empty<Instruction>();
        }

        #endregion

        #region A member of a state object

        private sealed class NamedItem
        {
            public readonly string Name;

            public NamedItem(string name)
            {
                Name = name;
            }
        }

        [Component]
        private static VNode ReadsAFieldOfAStateObject()
        {
            var (item, setItem) = Hooks.UseState(new NamedItem("n"));
            var (other, _) = Hooks.UseState(0);
            s_setItem = setItem;
            return V.Div("field",
                V.Label(text: Describe(0, item.Name)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base keys the label on the field it reads off the state object.
        // An equal object then keeps the label, and reading `==` as `!=` where `ReadLocal` decides whether to read a
        // member keys the label on the object instead.
        [Test]
        public void Given_AStateObjectsField_When_AnEqualObjectIsSet_Then_TheLabelReadingTheFieldIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(ReadsAFieldOfAStateObject, key: "field"));

            // Act
            s_setItem(new NamedItem("n"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1] }, Is.EqualTo(new[] { 1, 1 }));
        }

        #endregion

        #region What the declarations over the walk rest on

        // GREEN_ON_BASE(characterization): the base reaches every instruction of every component body the walk follows here.
        // None of them falls off its end either, which the `MUTANT_SURVIVES` over `ReactiveScopeFlow.Walk`,
        // `LoopHeads` and `CheckHost`'s end-of-body test rest on. Removing the `Send` of an unconditional branch from
        // `ReactiveScopeFlow.Continue` leaves instructions unreached and reddens it.
        [Test]
        public void Given_EveryComponentBodyThisAssemblyHolds_When_TheFlowWalkReadsIt_Then_NoInstructionIsUnreachedAndNoneFallsOffTheEnd()
        {
            // Arrange
            var codeGen = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == CodeGenAssemblyName);
            Assume.That(codeGen, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var flowType = codeGen!.GetType("Velvet.CodeGen.ReactiveScopeFlow", throwOnError: true)!;
            using var assembly = AssemblyDefinition.ReadAssembly(typeof(ReactiveScopeSelectionTests).Assembly.Location);
            var problems = new List<string>();
            var followed = 0;

            // Act
            foreach (var method in assembly.MainModule.GetTypes().SelectMany(type => type.Methods).Where(IsComponent))
            {
                var found = WalkProblemsOf(flowType, method);
                if (found == null) continue;
                followed++;
                problems.AddRange(found);
            }
            if (followed == 0) problems.Add("the walk followed no component body");

            // Assert
            Assert.That(problems, Is.Empty);
        }

        private static bool IsComponent(MethodDefinition method)
            => method.HasBody
                && method.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(ComponentAttribute).FullName);

        // What is wrong with the body as the walk reads it, or null where the walk gave up on it: a body it cannot
        // follow is never cut, so this case claims nothing about one.
        private static List<string>? WalkProblemsOf(Type flowType, MethodDefinition method)
        {
            var flow = flowType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, new object[] { method.Body })!;
            if ((bool)flowType.GetProperty("Failed")!.GetValue(flow)!) return null;
            var reached = flowType.GetMethod("Reached")!;
            var instructions = method.Body.Instructions;
            var problems = new List<string>();
            for (var i = 0; i < instructions.Count; i++)
            {
                if (!(bool)reached.Invoke(flow, new object[] { i })!)
                {
                    problems.Add($"{method.FullName}: instruction {i} is not reached");
                }
            }
            var ending = instructions[instructions.Count - 1].OpCode.FlowControl;
            if (ending != FlowControl.Return && ending != FlowControl.Throw && ending != FlowControl.Branch)
            {
                problems.Add($"{method.FullName}: the last instruction falls through");
            }
            return problems;
        }

        #endregion
    }
}
