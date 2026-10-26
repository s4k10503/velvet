using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the weaver writes the gates of the scopes it cut, by reading the IL it wrote and by counting the
    /// calls that build each label's text.
    /// <list type="bullet">
    /// <item>A gate that follows a protected region ends the region before it, and a jump table that lands on the first
    /// instruction of a run lands on the gate in front of it.</item>
    /// <item>A scope nested in another that starts at the same instruction is gated after it, so a hit on the outer
    /// scope does not run the inner scope's gate or the call that wraps it.</item>
    /// <item>A scope returns its cached tree cast to the type the call returns, and a parameter of a generic type is
    /// boxed into a key like any other value.</item>
    /// <item>A body that gets no gate and no scope is left with the instruction forms the compiler wrote.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeInjectionTests
    {
        private VisualElement _root = null!;

        private static readonly int[] s_calls = new int[10];
        private static Action<int> s_setBeside = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Array.Clear(s_calls, 0, s_calls.Length);
            s_setBeside = null!;
        }

        private static string Describe(int slot, object? value)
        {
            s_calls[slot]++;
            return slot + ":" + value;
        }

        private static MethodDefinition MethodOf(AssemblyDefinition assembly, string methodName)
            => ReactiveScopeTestSupport.MethodOf(assembly, typeof(ReactiveScopeInjectionTests), methodName);

        #region A gate after a protected region

        [Component]
        private static VNode AssignsALabelRightAfterATryStatement()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            try
            {
                Describe(9, first);
            }
            catch (Exception)
            {
                Describe(8, first);
            }
            var label = V.Label(text: Describe(0, first));
            return V.Div("after-try", label, V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base ends the catch block in front of the gate of a label that begins where the block ends.
        // Removing the `EndProtectedRegionsBefore` call from `GateOne` leaves the gate inside the block, whose branch
        // to the run then leaves it without a `leave`.
        [Test]
        public void Given_ALabelThatBeginsWhereACatchBlockEnds_When_Compiled_Then_NoBranchLeavesAProtectedRegion()
        {
            // Arrange
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeInjectionTests));
            var method = MethodOf(assembly, nameof(AssignsALabelRightAfterATryStatement));

            // Act
            var leaves = ReactiveScopeTestSupport.BranchesOutOfAProtectedRegion(method);

            // Assert
            Assert.That(leaves, Is.False);
        }

        [Component]
        private static VNode ChoosesALabelByAnIndexedSwitch()
        {
            var (mode, _) = Hooks.UseState(1);
            var (other, _) = Hooks.UseState(0);
            VNode content;
            switch (mode)
            {
                case 0:
                    content = V.Label(text: Describe(0, other));
                    break;
                case 1:
                    content = V.Label(text: Describe(1, other + 1));
                    break;
                case 2:
                    content = V.Label(text: Describe(2, other + 2));
                    break;
                default:
                    content = V.Label(text: "default");
                    break;
            }
            return V.Div("switch", content);
        }

        // GREEN_ON_BASE(characterization): the base sends a jump table that lands on the first instruction of a run to the gate in front of it.
        // The label of the second arm renders. Removing the loop in `Retarget` over the targets of a `switch` sends the
        // table to the run itself, ahead of the key it stores, and the first render throws an ArgumentNullException
        // for a missing key array.
        [Test]
        public void Given_ALabelBuiltInAnArmOfAnIndexedSwitch_When_Mounted_Then_TheLabelShowsItsText()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ChoosesALabelByAnIndexedSwitch, key: "switch"));

            // Assert
            Assert.That(_root.Query<Label>().ToList().Select(label => label.text).Single(), Is.EqualTo("1:1"));
        }

        #endregion

        #region Nested scopes

        private static VNode Wrap(VNode child, int extra)
        {
            s_calls[2]++;
            return V.Fragment(child, V.Text(extra.ToString()));
        }

        [Component]
        private static VNode WrapsALabelThatSharesItsFirstInstruction()
        {
            var (inner, _) = Hooks.UseState(0);
            var (outer, _) = Hooks.UseState(0);
            var (beside, setBeside) = Hooks.UseState(0);
            s_setBeside = setBeside;
            return V.Div("nested",
                Wrap(V.Label(text: Describe(0, inner)), outer),
                V.Label(text: Describe(1, beside)));
        }

        // GREEN_ON_BASE(characterization): the base gates the call that wraps a label before the label, so a hit on the wrapper skips both.
        // A change to the state read outside both leaves each built once. Removing the `Sort` in `ReactiveScopePlan.Select`
        // numbers and gates the label first, and reading `!=` as `==` in `CompareOuterFirst` leaves the two in the order
        // they were found; either makes the first render throw an ArgumentNullException for a missing key array.
        [Test]
        public void Given_ACallWrappingALabelAndSharingItsFirstInstruction_When_OnlyAnUnrelatedStateChanges_Then_NeitherIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(WrapsALabelThatSharesItsFirstInstruction, key: "nested"));

            // Act
            s_setBeside(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[2] }, Is.EqualTo(new[] { 1, 1 }));
        }

        #endregion

        #region The type a scope returns

        [Component]
        private static VNode TwoLabelsBesideEachOther()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            return V.Div("labels",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base casts the tree a gate hands back to the type the call returns.
        // Each of the two labels does. Removing the `Castclass` from `BuildGate`, or reading `!=` as `==` where it
        // compares the result type with the type of a tree, leaves the cast out.
        [Test]
        public void Given_TwoScopesBuildingElements_When_Compiled_Then_EachHitReturnsTheCachedTreeCastToAnElement()
        {
            // Arrange
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeInjectionTests));
            var instructions = MethodOf(assembly, nameof(TwoLabelsBesideEachOther)).Body.Instructions.ToList();

            // Act
            var casts = instructions.Count(instruction => instruction.OpCode.Code == Code.Castclass
                && ((TypeReference)instruction.Operand).Name == nameof(ElementNode));

            // Assert
            Assert.That(casts, Is.EqualTo(2));
        }

        [Component]
        private static VNode ReadsAParameterOfAGenericType<TItem>(TItem item, int other)
            => V.Div("generic",
                V.Label(text: Describe(0, item)),
                V.Label(text: Describe(1, other)));

        // GREEN_ON_BASE(characterization): the base boxes a parameter of a generic type into the key of each scope that reads it, as it does into the key of the body.
        // Removing the `IsGenericParameter` clause from `AppendRead` leaves the label's key storing the parameter unboxed.
        [Test]
        public void Given_ALabelReadingAParameterOfAGenericType_When_Compiled_Then_ItsKeyBoxesTheParameterAsTheKeyOfTheBodyDoes()
        {
            // Arrange
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeInjectionTests));
            var instructions = MethodOf(assembly, nameof(ReadsAParameterOfAGenericType)).Body.Instructions.ToList();

            // Act
            var boxedIntoAKey = instructions
                .Where((instruction, index) => instruction.OpCode.Code == Code.Box
                    && ((TypeReference)instruction.Operand).IsGenericParameter
                    && index + 1 < instructions.Count
                    && instructions[index + 1].OpCode.Code == Code.Stelem_Ref)
                .Count();

            // Assert
            Assert.That(boxedIntoAKey, Is.EqualTo(2));
        }

        #endregion

        #region A body that gets no plan

        [Component]
        private static VNode ReadsOnlyAHookNoKeyCanStandFor()
        {
            var blocker = Hooks.UseBlocker(true);
            return V.Div("blocker", V.Label(text: Describe(0, blocker)));
        }

        // GREEN_ON_BASE(characterization): the base leaves a body with no gate and no scope exactly as the compiler wrote it.
        // Removing the `snapshot.Restore()` that `TryWeaveMethod` calls when the plan is empty keeps the long forms that
        // reading the body for scopes rewrote its short instructions into.
        [Test]
        public void Given_ABodyWithNeitherAGateNorAScope_When_Compiled_Then_ItKeepsTheShortInstructionFormsTheCompilerWrote()
        {
            // Arrange
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeInjectionTests));
            var method = MethodOf(assembly, nameof(ReadsOnlyAHookNoKeyCanStandFor));

            // Act
            var longForms = method.Body.Instructions
                .Where(instruction => instruction.OpCode.Code is Code.Ldloc or Code.Stloc or Code.Br or Code.Brtrue or Code.Brfalse)
                .ToList();

            // Assert
            Assert.That(longForms, Is.Empty);
        }

        #endregion
    }
}
