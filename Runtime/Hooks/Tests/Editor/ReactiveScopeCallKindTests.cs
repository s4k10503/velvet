using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the weaver reads a call it meets inside a run of a component body, by reading the
    /// <see cref="MemoizationReportAttribute"/> it leaves on each method.
    /// <list type="bullet">
    /// <item>A framework call or construction outside the pure list reads state it was not handed, and a call the
    /// pure list names does not.</item>
    /// <item>A call that is not known to leave its arguments alone declines a run reading a stable object it was
    /// handed; a call that is known to leave them alone does not.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeCallKindTests
    {
        private static string Describe(int slot, object? value) => slot + ":" + value;

        private static MemoizationReportAttribute ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeCallKindTests), methodName);

        private static bool DeclinesALabelForAMutation(string note)
            => note.StartsWith("the call to V.Label", StringComparison.Ordinal)
                && note.Contains("reads L1, which an opaque value or a mutation reaches");

        #region Constructing a framework object

        [Component]
        private static VNode HandsAStableRefToAFrameworkConstructor()
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            new WeakReference(held);
            return V.Div("framework-new",
                V.Label(text: Describe(0, first)),
                V.Label(text: held.Current));
        }

        // GREEN_ON_BASE(characterization): the base reads a framework constructor as leaving its arguments alone.
        // The label reading the stable ref is cached beside the one reading the state. Removing the
        // `IsFrameworkType` clause from `OfConstruction` makes the constructor a call that may change the ref it
        // was handed, and declines that label.
        [Test]
        public void Given_AStableRefHandedToAFrameworkConstructor_When_Compiled_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(HandsAStableRefToAFrameworkConstructor));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        #endregion

        #region A call on a stable object

        private sealed class Tally
        {
            public string Name = "tally";

            public void Touch()
            {
                Name += "!";
            }
        }

        [Component]
        private static VNode CallsAMethodOnAStableObject()
        {
            var (first, _) = Hooks.UseState(0);
            var tally = Hooks.UseRef<Tally>(() => new Tally());
            tally.Current!.Touch();
            return V.Div("method",
                V.Label(text: Describe(0, first)),
                V.Label(text: tally.Current!.Name));
        }

        // GREEN_ON_BASE(characterization): the base declines the label reading a stable object an instance method was called on.
        // Removing the `get_` clause from `OfPlainCall` reads every instance call as a getter, which leaves the
        // object as it found it, and leaves that label cached.
        [Test]
        public void Given_AMethodCalledOnAStableObject_When_Compiled_Then_TheLabelReadingItIsDeclined()
        {
            // Act
            var report = ReportOf(nameof(CallsAMethodOnAStableObject));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Any(DeclinesALabelForAMutation)), Is.EqualTo((1, true)));
        }

        #endregion

        #region A delegate

        [Component]
        private static VNode InvokesAFuncParameter(Func<int, string> render, int other)
            => V.Div("func",
                V.Label(text: Describe(0, other)),
                V.Label(text: render(7)));

        // GREEN_ON_BASE(characterization): the base reads the invocation of a Func as a function of its arguments.
        // Both labels are cached, each on the parameter it reads. Reading `==` as `!=` where
        // `OfPlainCall` compares the name with `Invoke`, or removing the `System.Func` clause from `IsDelegateType`, sends the invocation to the
        // framework members outside the pure list and declines the label that makes it.
        [Test]
        public void Given_AFuncInvokedByOneLabel_When_Compiled_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(InvokesAFuncParameter));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        #endregion

        #region A member of a type on the pure list

        [Component]
        private static VNode TestsAStableStringForEmptiness()
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            _ = string.IsNullOrEmpty(held.Current);
            return V.Div("returns-a-value",
                V.Label(text: Describe(0, first)),
                V.Label(text: held.Current));
        }

        // GREEN_ON_BASE(characterization): the base reads a member of a pure type that returns a value as leaving its arguments alone.
        // The label reading the stable ref stays cached. Reading `==` as `!=` in
        // `IsVoidMutator` makes that member a call that may change the ref and declines the label.
        [Test]
        public void Given_AStableRefHandedToAValueReturningMemberOfAPureType_When_Compiled_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(TestsAStableStringForEmptiness));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        [Component]
        private static VNode ClearsAStableArray()
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<int[]>(() => new int[2]);
            Array.Clear(held.Current!, 0, 2);
            return V.Div("returns-nothing",
                V.Label(text: Describe(0, first)),
                V.Label(text: held.Current!.Length.ToString()));
        }

        // GREEN_ON_BASE(characterization): the base reads a void member of a pure type as a call that may change what it is handed.
        // The label reading the cleared array is declined. Reading `==` as `!=` in
        // `IsVoidMutator` reads the call as leaving the array alone and leaves that label cached.
        [Test]
        public void Given_AStableArrayHandedToAVoidMemberOfAPureType_When_Compiled_Then_TheLabelReadingItIsDeclined()
        {
            // Act
            var report = ReportOf(nameof(ClearsAStableArray));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Any(DeclinesALabelForAMutation)), Is.EqualTo((1, true)));
        }

        #endregion

        #region A framework namespace outside the pure list

        [Component]
        private static VNode ReadsARandomNumber()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            return V.Div("unity",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, UnityEngine.Random.Range(0, 10))),
                V.Label(text: Describe(2, second)));
        }

        // GREEN_ON_BASE(characterization): the base leaves uncached a label that calls a UnityEngine static outside the pure list.
        // Removing the `Unity` clause from `IsFrameworkType` reads the call as a function of its two arguments.
        [Test]
        public void Given_AUnityEngineStaticTakingArguments_When_ItIsOutsideThePureList_Then_TheReportSaysItReadsStateItWasNotHanded()
        {
            // Act
            var report = ReportOf(nameof(ReadsARandomNumber));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("Range, which reads state it was not handed")), Is.True);
        }

        [Component]
        private static VNode CreatesAnInstructionWithCecil()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            return V.Div("mono",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop))),
                V.Label(text: Describe(2, second)));
        }

        // GREEN_ON_BASE(characterization): the base leaves uncached a label that calls a Mono static outside the pure list.
        // Removing the `Mono.` clause from `IsFrameworkType` reads the call as a function of its argument.
        [Test]
        public void Given_AMonoStaticTakingAnArgument_When_ItIsOutsideThePureList_Then_TheReportSaysItReadsStateItWasNotHanded()
        {
            // Act
            var report = ReportOf(nameof(CreatesAnInstructionWithCecil));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("Create, which reads state it was not handed")), Is.True);
        }

        #endregion

        #region Members the pure list names by prefix

        [Component]
        private static VNode HashesAParameterByIdentity(string text, int other)
            => V.Div("compiler-services",
                V.Label(text: Describe(0, other)),
                V.Label(text: Describe(1, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(text))));

        // GREEN_ON_BASE(characterization): the base reads a member of System.Runtime.CompilerServices as a function of its arguments.
        // Both labels are cached, each on the parameter it reads. Removing the `CompilerServicesNamespace`
        // clause from `IsPureFramework` sends the call to the framework members outside the pure list.
        [Test]
        public void Given_ACompilerServicesMemberTakingAParameter_When_Compiled_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(HashesAParameterByIdentity));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        [Component]
        private static VNode PairsAParameterWithAConstant(string text, int other)
            => V.Div("value-tuple",
                V.Label(text: Describe(0, other)),
                V.Label(text: Describe(1, ValueTuple.Create(text, 1))));

        // GREEN_ON_BASE(characterization): the base reads a member of System.ValueTuple as a function of its arguments.
        // Both labels are cached, each on the parameter it reads. Removing the `ValueTuplePrefix` clause from
        // `IsPureFramework` sends the call to the framework members outside the pure list.
        [Test]
        public void Given_AValueTupleMemberTakingAParameter_When_Compiled_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(PairsAParameterWithAConstant));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        #endregion
    }
}
