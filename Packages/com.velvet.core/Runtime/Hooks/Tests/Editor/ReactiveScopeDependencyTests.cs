using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which values a scope is keyed on and which runs the weaver declines because of what they read, by
    /// counting the calls that build each label's text and reading the <see cref="MemoizationReportAttribute"/> it
    /// leaves on each method.
    /// <list type="bullet">
    /// <item>A scope that reads one member of an object is keyed on that member, a scope that also hands the object on
    /// is keyed on the object, and a value the scope reads twice is one dependency.</item>
    /// <item>A member is read in the key only where it is a field or a getter the compiler wrote; a getter the author
    /// wrote runs once, in the scope.</item>
    /// <item>A local the scope assigns after reading it, and a closure the code after the scope assigns or one of its
    /// lambdas writes, decline the scope.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeDependencyTests
    {
        private VisualElement _root = null!;

        private static readonly int[] s_calls = new int[10];
        private static Action<NamedItem> s_setItem = null!;
        private static Action<int> s_setNumber = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Array.Clear(s_calls, 0, s_calls.Length);
            s_setItem = null!;
            s_setNumber = null!;
        }

        private static string Describe(int slot, object? value)
        {
            s_calls[slot]++;
            return slot + ":" + value;
        }

        private static MemoizationReportAttribute ReportOf(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeDependencyTests), methodName);

        private static List<int> KeySizesOf(string methodName)
        {
            using var assembly = ReactiveScopeTestSupport.ReadWoven(typeof(ReactiveScopeDependencyTests));
            return ReactiveScopeTestSupport.KeySizes(
                ReactiveScopeTestSupport.MethodOf(assembly, typeof(ReactiveScopeDependencyTests), methodName));
        }

        private sealed class NamedItem
        {
            public readonly string Name;
            public readonly int Id;

            public NamedItem(string name, int id = 0)
            {
                Name = name;
                Id = id;
            }

            public string Tag { get; } = "tag";

            public string Computed
            {
                get
                {
                    s_calls[2]++;
                    return "computed";
                }
            }

            public override string ToString() => Name + "#" + Id;
        }

        private sealed class Holder<T>
        {
            public Holder(T value)
            {
                Value = value;
            }

            public T Value { get; }
        }

        private static VNode Hold(Action action) => V.Text("hold");

        private static VNode Show(int number) => V.Text(number.ToString());

        #region A member of an object

        [Component]
        private static VNode ReadsAnAutomaticPropertyOfAStateObject()
        {
            var (item, setItem) = Hooks.UseState(new NamedItem("n"));
            var (other, _) = Hooks.UseState(0);
            s_setItem = setItem;
            return V.Div("property",
                V.Label(text: Describe(0, item.Tag)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base keys the label on the property the compiler wrote, so an equal property keeps the label.
        // Reading `!=` as `==` where `IsAutomaticGetter` tests the parameter count, `==` as `!=` where it tests the
        // definition against null, or `==` as `!=` where it tests an attribute's name makes the getter no member at
        // all, which keys the label on the object and builds it again.
        [Test]
        public void Given_AnAutomaticPropertyOfAStateObject_When_AnObjectWithAnEqualPropertyIsSet_Then_TheLabelIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsAnAutomaticPropertyOfAStateObject, key: "property"));

            // Act
            s_setItem(new NamedItem("other"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[0], Is.EqualTo(1));
        }

        [Component]
        private static VNode ReadsAComputedPropertyOfAStateObject()
        {
            var (item, _) = Hooks.UseState(new NamedItem("n"));
            var (other, _) = Hooks.UseState(0);
            return V.Div("computed",
                V.Label(text: item.Computed),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base runs a getter its author wrote once for each time the label is built, and not in the key.
        // Reading the `return false` that ends `IsAutomaticGetter` as `true` reads the getter as a member, which runs
        // it in the key as well and counts a second call on the first render.
        [Test]
        public void Given_AGetterTheAuthorWrote_When_TheLabelReadingItIsMounted_Then_TheGetterRunsOnce()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ReadsAComputedPropertyOfAStateObject, key: "computed"));

            // Assert
            Assert.That(s_calls[2], Is.EqualTo(1));
        }

        [Component]
        private static VNode ReadsAMemberAndHandsTheObjectOn()
        {
            var (item, setItem) = Hooks.UseState(new NamedItem("n", 1));
            var (other, _) = Hooks.UseState(0);
            s_setItem = setItem;
            return V.Div("member-and-whole",
                V.Label(text: item.Name + Describe(0, item)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base keys a label that reads a member and also hands the object on on the object alone.
        // The body is keyed on its two states and each label on one value. Reading `!=` as `==` where `AllReadMembers`
        // compares keys leaves a use of the whole object out of what it checks, which keys the label on the member as
        // well and gives its key a second entry.
        [Test]
        public void Given_ALabelReadingAMemberAndHandingTheObjectOn_When_Compiled_Then_ItsKeyHoldsTheObjectOnly()
        {
            // Act
            var sizes = KeySizesOf(nameof(ReadsAMemberAndHandsTheObjectOn));

            // Assert
            Assert.That(sizes, Is.EqualTo(new[] { 2, 1, 1 }));
        }

        [Component]
        private static VNode ReadsAPropertyOfAGenericStateObject()
        {
            var (holder, _) = Hooks.UseState(new Holder<string>("value"));
            var (other, _) = Hooks.UseState(0);
            return V.Div("generic-holder",
                V.Label(text: holder.Value),
                V.Label(text: Describe(0, other)));
        }

        // GREEN_ON_BASE(characterization): the base keys a label on the object when the property it reads has a type the caller cannot name.
        // The component renders. Removing the `IsStorable` clause from `IsAutomaticGetter` keys it on a value of the
        // generic parameter, and mounting throws a BadImageFormatException.
        [Test]
        public void Given_APropertyOfAGenericClassReadByALabel_When_Mounted_Then_TheLabelShowsTheValue()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ReadsAPropertyOfAGenericStateObject, key: "generic-holder"));

            // Assert
            Assert.That(_root.Query<Label>().ToList().Select(label => label.text).First(), Is.EqualTo("value"));
        }

        [Component]
        private static VNode ReadsAFieldOfAnObjectAClosureHolds()
        {
            var (item, setItem) = Hooks.UseState(new NamedItem("n"));
            var (other, _) = Hooks.UseState(0);
            s_setItem = setItem;
            return V.Div("closure-member",
                V.Label(text: Describe(0, item.Name)),
                V.Label(text: Describe(1, other)),
                Hold(() => Describe(9, item)));
        }

        // GREEN_ON_BASE(characterization): the base keys a label on the field it reads off an object a closure holds, so an equal field keeps it.
        // Reading `index + 2` as `index - 2` in `ReadClosure` looks for the member at the wrong instruction and keys
        // the label on the object instead.
        [Test]
        public void Given_AFieldOfAnObjectAClosureHolds_When_AnObjectWithAnEqualFieldIsSet_Then_TheLabelReadingItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsAFieldOfAnObjectAClosureHolds, key: "closure-member"));

            // Act
            s_setItem(new NamedItem("n"));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[0], Is.EqualTo(1));
        }

        [Component]
        private static VNode ReadsAMemberOfAnObjectThatMayBeNull()
        {
            var (item, _) = Hooks.UseState((NamedItem?)null);
            var (show, _) = Hooks.UseState(false);
            var (other, _) = Hooks.UseState(0);
            return V.Div("null-item",
                V.Label(text: show ? item!.Name : "anonymous"),
                V.Label(text: Describe(0, other)));
        }

        // GREEN_ON_BASE(characterization): the base reads the member in the key as nothing when the object is null, so a label that does not reach it renders.
        // Removing the `Br` that `AppendMemberRead` writes after the null case runs on into the member read with null
        // on the stack, which throws while the key is built.
        [Test]
        public void Given_AMemberReadOnlyWhenAFlagIsSet_When_TheObjectIsNullAndTheFlagIsNot_Then_TheLabelShowsTheOtherBranch()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ReadsAMemberOfAnObjectThatMayBeNull, key: "null-item"));

            // Assert
            Assert.That(_root.Query<Label>().ToList().Select(label => label.text).First(), Is.EqualTo("anonymous"));
        }

        #endregion

        #region A value read twice

        [Component]
        private static VNode ReadsOneStateTwiceInALabel()
        {
            var (first, _) = Hooks.UseState(0);
            var (second, _) = Hooks.UseState(0);
            return V.Div("local-twice",
                V.Label(text: Describe(0, first) + Describe(2, first)),
                V.Label(text: Describe(1, second)));
        }

        // GREEN_ON_BASE(characterization): the base counts a state a label reads twice as one entry of that label's key.
        // The body is keyed on its two states and each label on one. Removing the `seen.Add` guard of `AddLocal`
        // gives the first label's key a second entry.
        [Test]
        public void Given_ALabelReadingOneStateTwice_When_Compiled_Then_ItsKeyHoldsThatStateOnce()
        {
            // Act
            var sizes = KeySizesOf(nameof(ReadsOneStateTwiceInALabel));

            // Assert
            Assert.That(sizes, Is.EqualTo(new[] { 2, 1, 1 }));
        }

        [Component]
        private static VNode ReadsOneMemberTwiceInALabel()
        {
            var (item, _) = Hooks.UseState(new NamedItem("n"));
            var (other, _) = Hooks.UseState(0);
            return V.Div("member-twice",
                V.Label(text: item.Name + item.Name),
                V.Label(text: Describe(0, other)));
        }

        // GREEN_ON_BASE(characterization): the base counts a member a label reads twice as one entry of that label's key.
        // Removing the `seen.Add` guard of `AddMember` gives the first label's key a second entry.
        [Test]
        public void Given_ALabelReadingOneMemberTwice_When_Compiled_Then_ItsKeyHoldsThatMemberOnce()
        {
            // Act
            var sizes = KeySizesOf(nameof(ReadsOneMemberTwiceInALabel));

            // Assert
            Assert.That(sizes, Is.EqualTo(new[] { 2, 1, 1 }));
        }

        [Component]
        private static VNode ReadsOneCapturedStateTwiceInALabel()
        {
            var (captured, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            return V.Div("closure-twice",
                V.Label(text: Describe(0, captured) + Describe(2, captured)),
                V.Label(text: Describe(1, other)),
                Hold(() => Describe(9, captured)));
        }

        // GREEN_ON_BASE(characterization): the base counts a captured state a label reads twice as one entry of that label's key.
        // Removing the `seen.Add` guard of `AddClosureField` gives the first label's key a second entry.
        [Test]
        public void Given_ALabelReadingOneCapturedStateTwice_When_Compiled_Then_ItsKeyHoldsThatStateOnce()
        {
            // Act
            var sizes = KeySizesOf(nameof(ReadsOneCapturedStateTwiceInALabel));

            // Assert
            Assert.That(sizes, Is.EqualTo(new[] { 2, 1, 1, 1 }));
        }

        [Component]
        private static VNode ReadsANullableState()
        {
            var (maybe, _) = Hooks.UseState((int?)null);
            var (other, _) = Hooks.UseState(0);
            return V.Div("nullable",
                V.Label(text: Describe(0, maybe)),
                V.Label(text: Describe(1, other)));
        }

        // GREEN_ON_BASE(characterization): the base keys a label on a state of a generic type that is not a tuple.
        // Both labels are cached. Removing the `System.ValueTuple` clause from `AddLocal` reads every generic type as
        // a tuple and declines the label.
        [Test]
        public void Given_AStateOfANullableType_When_ALabelReadsIt_Then_BothLabelsAreCached()
        {
            // Act
            var report = ReportOf(nameof(ReadsANullableState));

            // Assert
            Assert.That((report.Scopes, report.Bailouts.Count), Is.EqualTo((2, 0)));
        }

        #endregion

        #region A local the run assigns

        [Component]
        private static VNode ReadsAndThenIncrementsALocal()
        {
            var (first, _) = Hooks.UseState(0);
            var counter = 0;
            return V.Div("increments",
                V.Label(text: Describe(0, first)),
                Show(counter += 1));
        }

        // GREEN_ON_BASE(characterization): the base declines a call whose first operand reads a local that the same operand then assigns.
        // Reading `>=` as `>` where `CheckLocals` picks out the accesses inside the run leaves out the read at its
        // first instruction, and accepts a run that assigns what it began by reading.
        [Test]
        public void Given_ACallWhoseFirstOperandReadsAndThenAssignsALocal_When_Compiled_Then_TheReportSaysItWritesAfterReading()
        {
            // Act
            var report = ReportOf(nameof(ReadsAndThenIncrementsALocal));

            // Assert
            Assert.That(
                report.Bailouts.Any(note => note.StartsWith("the call to ReactiveScopeDependencyTests.Show", StringComparison.Ordinal)
                    && note.Contains("after reading what it held")),
                Is.True);
        }

        [Component]
        private static VNode ReadsALocalThroughAConditionalReference(bool flag)
        {
            var (first, _) = Hooks.UseState(0);
            var left = 1;
            var right = 2;
            return V.Div("ref-conditional",
                V.Label(text: Describe(0, first)),
                V.Label(text: (flag ? ref left : ref right).ToString()));
        }

        // GREEN_ON_BASE(characterization): the base follows an address that reaches its consumer through a conditional, and weaves the body.
        // Reading `j < _flow.Instructions.Length` as `<=` in `Normalize` walks one past the last instruction when the
        // address merges with another and no instruction pops it by name, which leaves the body unwoven.
        [Test]
        public void Given_ALabelCallingAMethodOnALocalChosenByAConditionalReference_When_Compiled_Then_TheBodyIsWoven()
        {
            // Act
            var report = ReportOf(nameof(ReadsALocalThroughAConditionalReference));

            // Assert
            Assert.That(report?.BodyGate, Is.True);
        }

        [Component]
        private static VNode ReadsALocalThatAnEarlierLabelAssigned()
        {
            var (number, setNumber) = Hooks.UseState(0);
            s_setNumber = setNumber;
            string text;
            string spare;
            var first = V.Label(text: text = Describe(0, number));
            text = spare = Describe(1, number);
            var second = V.Label(text: text);
            Describe(2, spare);
            return V.Div("reassigned", first, second);
        }

        // GREEN_ON_BASE(characterization): the base keys the second label on the local it reads, though the first label assigned that local and held it to itself.
        // A change to the state shows in both labels. Removing the `_internalLocals = new HashSet<int>();` that
        // `TryBuildFrom` starts each attempt with carries the first label's local into the second, which then reads
        // it as its own and is keyed on nothing.
        [Test]
        public void Given_ALocalOneLabelAssignedAndAnotherReadsAfterItIsAssignedAgain_When_TheStateChanges_Then_BothLabelsShowTheNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsALocalThatAnEarlierLabelAssigned, key: "reassigned"));

            // Act
            s_setNumber(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Query<Label>().ToList().Select(label => label.text), Is.EqualTo(new[] { "0:5", "1:5" }));
        }

        #endregion

        #region A closure

        [Component]
        private static VNode PassesALambdaThatWritesAVariable()
        {
            var (first, _) = Hooks.UseState(0);
            var count = 0;
            return V.Div("lambda-writes",
                Hold(() => count++),
                V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base declines a call that takes a lambda assigning a variable of its closure.
        // Reading `==` as `!=` where `ComputeClosureProblem` counts the closure's fields skips every check below it,
        // and reading `true` as `false` where `LambdaWritesClosure` finds a write, or `!=` as `==` where
        // `WritesOwnField` tests whose field it is, finds no write; each accepts the call.
        [Test]
        public void Given_ALambdaThatAssignsACapturedVariable_When_PassedToACall_Then_TheReportSaysTheLambdaAssignsAVariableOfItsClosure()
        {
            // Act
            var report = ReportOf(nameof(PassesALambdaThatWritesAVariable));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("a lambda it captures assigns a variable of its closure")), Is.True);
        }

        [Component]
        private static VNode AssignsACapturedVariableAfterTheCall()
        {
            var (first, _) = Hooks.UseState(0);
            try
            {
                Describe(8, first);
            }
            catch (Exception)
            {
            }
            var count = first;
            var node = Hold(() => Describe(9, count));
            count = 99;
            return V.Div("assigned-after", node, V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base declines a call whose lambda reads a variable that the code after the call assigns.
        // Reading `==` as `!=` where `AssignedAfter` compares the closure local with the target of a store finds no
        // store to it, and accepts the call.
        [Test]
        public void Given_ACapturedVariableAssignedAfterTheCallThatTakesTheLambda_When_Compiled_Then_TheReportSaysTheCodeAfterItAssignsAVariable()
        {
            // Act
            var report = ReportOf(nameof(AssignsACapturedVariableAfterTheCall));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("the code after it assigns a variable its closure holds")), Is.True);
        }

        [Component]
        private static VNode PassesALambdaThatReadsACapturedNumber()
        {
            var (first, _) = Hooks.UseState(0);
            var count = 0;
            return V.Div("lambda-reads",
                Hold(() => Describe(9, count.ToString())),
                V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base takes a lambda that calls ToString on a captured number as reading it, not writing it.
        // Reading `==` as `!=` where `WritesOwnField` tests for a store, `==` as `!=` where `ReadsThroughAddress`
        // tests for a missing consumer, or `!=` as `==` where it tests the consumer's opcode treats the address taken
        // for the call as a write, and declines the call.
        [Test]
        public void Given_ALambdaThatCallsToStringOnACapturedNumber_When_PassedToACall_Then_NothingIsDeclined()
        {
            // Act
            var report = ReportOf(nameof(PassesALambdaThatReadsACapturedNumber));

            // Assert
            Assert.That(report.Bailouts, Is.Empty);
        }

        private struct Point
        {
            public int X { get; set; }
        }

        [Component]
        private static VNode PassesALambdaThatReadsAPropertyOfACapturedStruct()
        {
            var (first, _) = Hooks.UseState(0);
            var point = new Point();
            return V.Div("lambda-struct",
                Hold(() => Describe(9, point.X)),
                V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base takes a lambda that reads a property of a captured struct as reading it.
        // Removing the `get_` clause from `ReadsThroughAddress` treats the address taken for the getter as a write.
        [Test]
        public void Given_ALambdaThatReadsAPropertyOfACapturedStruct_When_PassedToACall_Then_NothingIsDeclined()
        {
            // Act
            var report = ReportOf(nameof(PassesALambdaThatReadsAPropertyOfACapturedStruct));

            // Assert
            Assert.That(report.Bailouts, Is.Empty);
        }

        private static int get_Peek(ref int number) => number;

        [Component]
        private static VNode PassesALambdaThatTakesACapturedNumberByReference()
        {
            var (first, _) = Hooks.UseState(0);
            var count = 0;
            return V.Div("lambda-by-ref",
                Hold(() => Describe(9, get_Peek(ref count))),
                V.Label(text: Describe(0, first)));
        }

        // GREEN_ON_BASE(characterization): the base declines a lambda that hands a captured number by reference to a static method of a class.
        // The method's name starts with `get_`, but a getter is read through the address only on a value type.
        // Removing the `IsValueType` clause from `ReadsThroughAddress` reads the call as a read and accepts it.
        [Test]
        public void Given_ALambdaThatHandsACapturedNumberByReferenceToAStaticMethod_When_PassedToACall_Then_TheReportSaysTheLambdaAssignsAVariableOfItsClosure()
        {
            // Act
            var report = ReportOf(nameof(PassesALambdaThatTakesACapturedNumberByReference));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("a lambda it captures assigns a variable of its closure")), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base has no closure class this assembly holds that declares a static field or a method without a body.
        // The `MUTANT_SURVIVES` over `ReadEveryClosureField`'s static test and `LambdaWritesClosure`'s body test rest on it.
        // No cut of the weaver reddens it; a compiler that wrote either into a closure class does.
        [Test]
        public void Given_EveryClosureClassThisAssemblyHolds_When_ItsMembersAreRead_Then_ItDeclaresNoStaticFieldAndNoMethodWithoutABody()
        {
            // Arrange
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var closures = typeof(ReactiveScopeDependencyTests).Assembly.GetTypes()
                .Where(type => type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
                .ToList();

            // Act
            var offenders = closures
                .SelectMany(type => type.GetFields(all).Where(field => field.IsStatic).Select(field => type.Name + "." + field.Name)
                    .Concat(type.GetMethods(all).Where(method => method.GetMethodBody() == null).Select(method => type.Name + "." + method.Name)))
                .ToList();

            // Assert
            Assert.That($"{closures.Count > 0}|{string.Join(", ", offenders)}", Is.EqualTo("True|"));
        }

        #endregion
    }
}
