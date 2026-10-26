using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the weaver reads off the hook section of a component body: where the gate on the body lands
    /// once the last hook has run, which shapes a hook's value may be consumed in, and which bodies it leaves alone
    /// because a hook sits where the position-based slot order cannot be kept.
    /// <list type="bullet">
    /// <item>The gate on the body follows every hook call, whether the last hook is a void hook or a tuple the body
    /// stores whole and reads one element at a time.</item>
    /// <item>A tuple a custom hook returns is keyed on every element the body reads.</item>
    /// <item>A body whose hook sits in a try block, or in a catch block after a try that throws, is left as it was.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeHookScanTests
    {
        private VisualElement _root = null!;
        private static Action<int> s_setRight = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setRight = null!;
        }

        private static string Describe(int slot, object? value) => slot + ":" + value;

        private static MemoizationReportAttribute ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeHookScanTests), methodName);

        private static bool GateFollowsEveryHookCall(string methodName)
        {
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeHookScanTests));
            var method = ReactiveScopeTestSupport.MethodOf(assembly, typeof(ReactiveScopeHookScanTests), methodName);
            var gate = ReactiveScopeTestSupport.FirstGateIndex(method);
            var hooks = ReactiveScopeTestSupport.HookCallIndices(method).ToList();
            return gate >= 0 && hooks.Count > 0 && hooks.All(hook => hook < gate);
        }

        #region The gate on the body

        [Component]
        private static VNode EffectAfterAState(int other)
        {
            var (first, _) = Hooks.UseState(0);
            Hooks.UseEffect((Func<Action?>)(() => null));
            return V.Div("effect-last",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base puts the gate on the body after a void hook that is the last hook.
        // Removing the `scan.Boundary = instr;` that `ScanHooks` writes for a void hook leaves the boundary at the
        // store of the state ahead of it, which puts the gate between the two hooks, and reddens it.
        [Test]
        public void Given_AVoidHookAfterAState_When_Compiled_Then_TheGateOnTheBodyFollowsEveryHookCall()
        {
            // Act
            var follows = GateFollowsEveryHookCall(nameof(EffectAfterAState));

            // Assert
            Assert.That(follows, Is.True);
        }

        [Component]
        private static VNode TwoStatesCapturedByALambda(int other)
        {
            var (count, setCount) = Hooks.UseState(0);
            var (name, setName) = Hooks.UseState("n");
            Action bump = () =>
            {
                setCount.Invoke(count + 1);
                setName.Invoke(name + "!");
            };
            return V.Div("lambda-capture",
                V.Label(text: Describe(0, count)),
                V.Label(text: Describe(1, name)),
                V.Label(text: Describe(2, other)),
                V.Label(text: Describe(3, bump)));
        }

        // GREEN_ON_BASE(characterization): the base puts the gate on the body after the second of two tuples stored whole.
        // Removing the `scan.Boundary = stored;` that `TryCaptureTupleTemp` writes leaves the boundary at the first
        // tuple's store, which puts the gate ahead of the second hook, and reddens it.
        [Test]
        public void Given_TwoStatesACapturingLambdaMakesTheCompilerStoreWhole_When_Compiled_Then_TheGateOnTheBodyFollowsEveryHookCall()
        {
            // Act
            var follows = GateFollowsEveryHookCall(nameof(TwoStatesCapturedByALambda));

            // Assert
            Assert.That(follows, Is.True);
        }

        [Component]
        private static VNode StateStoredWholeInALaterLocal(int other)
        {
            var offset0 = other + 1;
            var offset1 = other + 2;
            var offset2 = other + 3;
            var offset3 = other + 4;
            var (count, setCount) = Hooks.UseState(0);
            Action bump = () => setCount.Invoke(count + 1);
            return V.Div("later-local",
                V.Label(text: Describe(0, count)),
                V.Label(text: Describe(1, offset0 + offset1 + offset2 + offset3)),
                V.Label(text: Describe(2, bump)));
        }

        // GREEN_ON_BASE(characterization): the base keys the body as a whole when the tuple a hook returned sits in a local past the fourth.
        // The compiler then loads it with `ldloc.s`, whose operand is the local, and reading `==` as `!=` where
        // `IsLocalAddress` tests for `ldloca` takes every such load for the address of the tuple, which bails the capture.
        [Test]
        public void Given_AStateStoredWholeInALaterLocal_When_Compiled_Then_TheBodyIsKeyedAsAWhole()
        {
            // Act
            var report = ReportOf(nameof(StateStoredWholeInALaterLocal));

            // Assert
            Assert.That(report.BodyGate, Is.True);
        }

        #endregion

        #region A tuple a custom hook returns

        private static (int Left, int Right) UseTwoCounters()
        {
            var (left, _) = Hooks.UseState(0);
            var (right, setRight) = Hooks.UseState(0);
            s_setRight = setRight;
            return (left, right);
        }

        [Component]
        private static VNode ReadsBothHalvesOfACustomHookTuple()
        {
            var pair = UseTwoCounters();
            var left = pair.Left;
            var right = pair.Right;
            return V.Div("pair",
                V.Label(text: Describe(0, left)),
                V.Label(text: Describe(1, right)));
        }

        // GREEN_ON_BASE(characterization): the base keys the body on both elements of the tuple a custom hook returned and the body reads.
        // A change to the second shows in its label. Reading `==` as `!=` where `TupleElementsRead` stops at a store
        // to the tuple's own local stops it at the first store into another local, which leaves the second element out
        // of the key and the label showing the value it had. That holds in a build without optimization, where the
        // compiler reads each element into a local of its own; with it the elements are read where they are used.
        [Test]
        public void Given_ACustomHookTupleReadOneElementAtATime_When_TheSecondElementChanges_Then_ItsLabelShowsTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsBothHalvesOfACustomHookTuple, key: "pair"));

            // Act
            s_setRight(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Query<Label>().ToList().Select(label => label.text), Is.EqualTo(new[] { "0:0", "1:5" }));
        }

        #endregion

        #region A hook whose value is never read

        [Component]
        private static VNode OnlyTheSetterOfAStateIsRead()
        {
            var (_, setCount) = Hooks.UseState(0);
            Action bump = () => setCount.Invoke(1);
            return V.Div("setter-only", V.Label(text: Describe(0, bump)));
        }

        // GREEN_ON_BASE(characterization): the base refuses a body whose only hook has its value never read, when the compiler stores the tuple whole.
        // Removing the `scan.UnreadValue ??= UnreadMessage;` that `TryCaptureTupleTemp` writes for a tuple without
        // its first element leaves the body with a hook the gate cannot key on, and reddens it.
        [Test]
        public void Given_OnlyTheSetterOfAStateReadByACapturingLambda_When_Compiled_Then_TheReportSaysTheValueIsNeverRead()
        {
            // Act
            var report = ReportOf(nameof(OnlyTheSetterOfAStateIsRead));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("a hook's value is never read")), Is.True);
        }

        #endregion

        #region A hook where the slot order cannot be kept

        [Component]
        private static VNode HookInATryBlock()
        {
            try
            {
                var (inside, _) = Hooks.UseState(0);
                Describe(9, inside);
            }
            catch (Exception)
            {
            }
            return V.Div("hook-in-try", V.Label(text: "x"));
        }

        // GREEN_ON_BASE(characterization): the base leaves a body alone whose hook sits in a try block.
        // Reading the first `return true` of `HookInsideHandler` as `false` lets the body through, and reddens it.
        [Test]
        public void Given_AHookInATryBlock_When_Compiled_Then_TheReportSaysTheComponentIsNotCached()
        {
            // Act
            var report = ReportOf(nameof(HookInATryBlock));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("a hook call sits inside a try, catch or finally")), Is.True);
        }

        [Component]
        private static VNode HookInACatchBlockAfterATryThatThrows()
        {
            var (first, _) = Hooks.UseState(0);
            try
            {
                throw new InvalidOperationException();
            }
            catch (InvalidOperationException)
            {
                Hooks.UseId();
            }
            return V.Div("hook-in-catch", V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base leaves a body alone whose hook sits in a catch block that no `leave` skips.
        // The try block here ends in `throw`, so no branch jumps over the hook and the check for a skipped hook passes.
        // Reading the second `return true` of `HookInsideHandler` as `false` lets the body through, and reddens it.
        [Test]
        public void Given_AHookInACatchBlockAfterATryThatThrows_When_Compiled_Then_TheReportSaysTheComponentIsNotCached()
        {
            // Act
            var report = ReportOf(nameof(HookInACatchBlockAfterATryThatThrows));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("a hook call sits inside a try, catch or finally")), Is.True);
        }

        #endregion

        #region The hooks that hold a stable object

        // GREEN_ON_BASE(characterization): the base has no container hook returning a tuple, which the `MUTANT_SURVIVES` over
        // `TryCapture`'s `HoldsStableObject` clause rests on. Adding `UseState` to `ContainerHookNames` reddens it.
        [Test]
        public void Given_TheContainerHooksTheWeaverNames_When_TheirReturnTypesAreRead_Then_NoneReturnsAValueTuple()
        {
            // Arrange
            var codeGen = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "Unity.Velvet.CodeGen");
            Assume.That(codeGen, Is.Not.Null,
                "Precondition: the Unity.Velvet.CodeGen assembly is loaded in the editor domain");
            var names = (HashSet<string>)codeGen!.GetType("Velvet.CodeGen.CompilerWeaver", throwOnError: true)!
                .GetField("ContainerHookNames", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

            // Act
            var tupleReturning = typeof(Hooks).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => names.Contains(method.Name)
                    && method.ReturnType.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
                .Select(method => method.Name)
                .ToList();

            // Assert
            Assert.That((names.Count, tupleReturning), Is.EqualTo((3, new List<string>())));
        }

        #endregion
    }
}
