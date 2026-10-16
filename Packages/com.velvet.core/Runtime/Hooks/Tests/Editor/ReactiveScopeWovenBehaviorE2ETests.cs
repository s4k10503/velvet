using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the reactive scopes the default-on inner auto-memoization weaver puts below a component's gate:
    /// which child of a woven component is built again when one input changes, and which are not.
    /// <list type="bullet">
    /// <item>A child of a <c>VNode[]</c> children list is cached on the values it reads, so a render that changes
    /// one input builds the children reading it and leaves the others as they were. That holds for a child
    /// reading a state, a prop or a hook value, for a child inside a child that is itself built again, and for a
    /// child the render did not reach (an early return) when it is reached again with equal inputs.</item>
    /// <item>A child whose expression builds nothing is cached as nothing.</item>
    /// <item>A child reading something no dependency stands for — a <c>MutableRef</c>, a mutable static, a call
    /// to a method of the fixture — is built on every render the gate misses, as it was before scopes.</item>
    /// <item>A component opted out with <c>[Component(Compiler = false)]</c> builds every child on every render.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// A child is counted by <c>Built</c>, whose constructor runs where the child's expression runs and which is
    /// among the calls a scope may hold. The IL shape each component compiles to is asserted in
    /// <c>CompilerILPostProcessorE2ETests</c>.
    /// </remarks>
    [TestFixture]
    internal sealed class ReactiveScopeWovenBehaviorE2ETests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_builds.Clear();
            s_ambient = 0;
            s_setLeft = null;
            s_setRight = null;
            s_setCount = null;
            s_setOther = null;
            s_setTick = null;
            s_setShown = null;
            s_setPhase = null;
        }

        private static readonly Dictionary<string, int> s_builds = new();
        private static int s_ambient;
        private static Action<int> s_setLeft;
        private static Action<int> s_setRight;
        private static Action<int> s_setCount;
        private static Action<int> s_setOther;
        private static Action<int> s_setTick;
        private static Action<bool> s_setShown;
        private static Action<int> s_setPhase;

        private static int Builds(string key) => s_builds.TryGetValue(key, out var count) ? count : 0;

        private string LabelText(string name) => _root.Q<Label>(name: name)?.text;

        private sealed class Built
        {
            public Built(string key, string text)
            {
                s_builds[key] = Builds(key) + 1;
                Text = text;
            }

            public string Text { get; }

            public VNode Nothing => null;
        }

        private sealed class HookingToString
        {
            public override string ToString()
            {
                _ = Hooks.UseRef<object>();
                return "hooked";
            }
        }

        private sealed record TitleProps(string Title);

        private static string AmbientLabel(int input) => s_ambient + ":" + input;

        #region Components

        [Component]
        private static VNode TwoChildren()
        {
            var (left, setLeft) = Hooks.UseState(0);
            var (right, setRight) = Hooks.UseState(0);
            s_setLeft = setLeft;
            s_setRight = setRight;
            return V.Div("root",
                V.Label(name: "left", text: new Built("left", left.ToString()).Text),
                V.Label(name: "right", text: new Built("right", right.ToString()).Text));
        }

        [Component(Compiler = false)]
        private static VNode OptedOutChildren()
        {
            var (left, setLeft) = Hooks.UseState(0);
            var (right, setRight) = Hooks.UseState(0);
            s_setLeft = setLeft;
            s_setRight = setRight;
            return V.Div("root",
                V.Label(name: "left", text: new Built("left", left.ToString()).Text),
                V.Label(name: "right", text: new Built("right", right.ToString()).Text));
        }

        [Component]
        private static VNode TitleAndCount(TitleProps props)
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            return V.Div("root",
                V.Label(name: "title", text: new Built("title", props.Title).Text),
                V.Label(name: "count", text: new Built("count", count.ToString()).Text));
        }

        [Component]
        private static VNode TitleParent()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Component(TitleAndCount, new TitleProps("t" + tick), key: "child");
        }

        [Component]
        private static VNode NestedChildren()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            s_setLeft = setA;
            s_setRight = setB;
            return V.Div("root",
                V.Div("group",
                    V.Label(name: "a", text: new Built("a", a.ToString()).Text),
                    V.Label(name: "b", text: new Built("b", b.ToString()).Text)),
                V.Label(name: "b2", text: new Built("b2", b.ToString()).Text));
        }

        [Component]
        private static VNode SkippedChild()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (n, _) = Hooks.UseState(7);
            s_setShown = setShown;
            if (!shown)
            {
                return V.Label(name: "hidden", text: "hidden");
            }
            return V.Div("root",
                V.Label(name: "kept", text: new Built("kept", n.ToString()).Text),
                V.Label(name: "tail", text: "tail"));
        }

        [Component]
        private static VNode ChildBuildingNothing()
        {
            var (flag, _) = Hooks.UseState(true);
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                flag ? new Built("maybe", flag.ToString()).Nothing : V.Label(name: "maybe", text: flag.ToString()),
                V.Label(name: "other", text: other.ToString()));
        }

        [Component]
        private static VNode ReadsARef()
        {
            var (other, setOther) = Hooks.UseState(0);
            var counter = Hooks.UseMutableRef(0);
            s_setOther = setOther;
            counter.Current++;
            return V.Div("root",
                V.Label(name: "ref", text: counter.Current.ToString()),
                V.Label(name: "other", text: other.ToString()));
        }

        [Component]
        private static VNode ReadsAStatic()
        {
            var (input, _) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                V.Label(name: "static", text: s_ambient + ":" + input),
                V.Label(name: "other", text: other.ToString()));
        }

        [Component]
        private static VNode ReadsThroughAHelper()
        {
            var (input, _) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                V.Label(name: "helper", text: AmbientLabel(input)),
                V.Label(name: "other", text: other.ToString()));
        }

        [Component]
        private static VNode DiscardedAttempt()
        {
            var (phase, setPhase) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setPhase = setPhase;
            s_setOther = setOther;
            var attempt = phase % 2 == 1 ? "transient" : "settled";
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            return V.Div("root",
                V.Label(name: "attempt", text: new Built("attempt", attempt).Text),
                V.Label(name: "other", text: other.ToString()));
        }

        [Component]
        private static VNode ChildRunningAHook()
        {
            var (input, _) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                V.Label(name: "hooked", text: ((object)new HookingToString()).ToString() + input),
                V.Label(name: "other", text: other.ToString()));
        }

        // Opted out so that its own gate plays no part in what the case reads.
        [Component(Compiler = false)]
        private static VNode CallsAWovenComponent()
        {
            var (tick, _) = Hooks.UseState(0);
#pragma warning disable VEL103
            return V.Div("host", TwoChildren(), V.Label(name: "tick", text: tick.ToString()));
#pragma warning restore VEL103
        }

        [Component]
        private static VNode GenericChild<T>(T value)
        {
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                V.Label(name: "value", text: new Built("value", Convert.ToString(value)).Text),
                V.Label(name: "other", text: other.ToString()));
        }

        #endregion

        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_OneStateChanges_Then_OnlyTheChildReadingItIsBuilt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoChildren, key: "two"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("left"), Builds("right"), LabelText("left"), LabelText("right")),
                Is.EqualTo((1, 2, "0", "1")),
                "The child reading the changed state is built again and shows it; the other keeps what it built");
        }

        [Test]
        public void Given_ChildrenReadingAPropAndAState_When_TheStateChanges_Then_TheChildReadingTheStablePropIsNotBuilt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TitleAndCount, new TitleProps("t"), key: "child"));

            // Act
            s_setCount(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("title"), Builds("count"), LabelText("title"), LabelText("count")),
                Is.EqualTo((1, 2, "t", "1")));
        }

        [Test]
        public void Given_ChildrenReadingAPropAndAState_When_TheParentHandsANewProp_Then_TheChildReadingTheStateIsNotBuilt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TitleParent, key: "parent"));

            // Act
            s_setTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("title"), Builds("count"), LabelText("title"), LabelText("count")),
                Is.EqualTo((2, 1, "t1", "0")));
        }

        [Test]
        public void Given_ChildrenNestedInAChildReadingBoth_When_OneStateChanges_Then_TheInnerChildNotReadingItKeepsWhatItBuilt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedChildren, key: "nested"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert — the group reads both states, so it is built again, and the label inside it reading only `a` is not
            Assert.That(
                (Builds("a"), Builds("b"), Builds("b2"), LabelText("a"), LabelText("b"), LabelText("b2")),
                Is.EqualTo((1, 2, 2, "0", "1", "1")));
        }

        [Test]
        public void Given_AChildTheRenderDidNotReach_When_ItIsReachedAgainWithEqualInputs_Then_ItIsNotBuiltAgainAndIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SkippedChild, key: "skipped"));
            s_setShown(false);
            mounted.FlushStateForTest();
            var whileHidden = (LabelText("hidden"), LabelText("kept"));

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (whileHidden, Builds("kept"), LabelText("kept")),
                Is.EqualTo((("hidden", (string)null), 1, "7")),
                "The render that returned early left the child's cache as the last render that built it left it");
        }

        [Test]
        public void Given_AChildBuildingNothing_When_AnotherStateChanges_Then_ItIsNotBuiltAgainAndStillShowsNothing()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ChildBuildingNothing, key: "nothing"));

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("maybe"), LabelText("maybe"), LabelText("other")),
                Is.EqualTo((1, (string)null, "1")));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the current count too.
        // What it pins is the ref staying out of every scope: `return true;` in place of `return !TouchesRef(method);`
        // in `ScanMethod` lets the child cache a count nothing compares, and it shows the first.
        [Test]
        public void Given_AChildReadingAMutableRef_When_AnotherStateChanges_Then_ItShowsTheCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsARef, key: "ref"));

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("ref"), Is.EqualTo("2"));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the current value too.
        // What it pins is a mutable static staying out of every scope: `return true;` in place of the last return of
        // `ScanField` caches the child on `input` alone, and it shows the first value.
        [Test]
        public void Given_AChildReadingAMutableStatic_When_AnotherStateChanges_Then_ItShowsTheCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsAStatic, key: "static"));
            s_ambient = 5;

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("static"), Is.EqualTo("5:0"));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the current value too.
        // What it pins is a call the weaver cannot read staying out of every scope: removing
        // `if (!IsRepeatableCall(instr, method)) return false;` from `ScanMethod` caches the child on `input` alone,
        // and it shows the first value.
        [Test]
        public void Given_AChildCallingAMethodReadingAMutableStatic_When_AnotherStateChanges_Then_ItShowsTheCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReadsThroughAHelper, key: "helper"));
            s_ambient = 5;

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("helper"), Is.EqualTo("5:0"));
        }

        // GREEN_ON_BASE(characterization): the base builds every child on every miss of an unwoven body too.
        // What it pins is the opt-out being read ahead of the scopes: `return true;` in place of
        // `return CompilerEnabled(attr);` in `IsCandidate` weaves the body and builds its left child once.
        [Test]
        public void Given_AComponentOptedOutOfTheCompiler_When_OneStateChanges_Then_EveryChildIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OptedOutChildren, key: "opted-out"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((Builds("left"), Builds("right")), Is.EqualTo((2, 2)));
        }

        [Test]
        public void Given_AChildKeptByARenderAfterADiscardedAttempt_When_AnotherStateChanges_Then_ItIsNotBuiltAgain()
        {
            // Arrange — the first attempt of the next render reads a value the render-phase update then replaces
            using var mounted = V.Mount(_root, V.Component(DiscardedAttempt, key: "discarded"));
            s_setPhase(1);
            mounted.FlushStateForTest();

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("attempt"), LabelText("attempt")),
                Is.EqualTo((2, "settled")),
                "The committed deps are those of the settled attempt, not of the one that was discarded");
        }

        // GREEN_ON_BASE(characterization): the base builds the child on every miss and keeps its hook in step as well.
        // What it pins is a hook run inside a child ending that child's cache: `slot.RunsHooksPastGate = true;` in
        // `StoreMemoizedScope` written as `= false` lets the second render hit, skip the hook, and throw for fewer hooks.
        [Test]
        public void Given_AChildRunningAHook_When_AnotherStateChanges_Then_ItIsBuiltAgainWithTheHookInStep()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ChildRunningAHook, key: "hooked"));

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That((LabelText("hooked"), LabelText("other")), Is.EqualTo(("hooked0", "1")));
        }

        // GREEN_ON_BASE(characterization): the base builds the called component's children as well.
        // What it pins is a scope with no slot to cache in running its child: `TryGetMemoizedScope` answering
        // `true` where it was granted none returns the cached nothing in place of the child.
        [Test]
        public void Given_AWovenComponentCalledAsAMethod_When_Rendered_Then_ItsChildrenAreBuilt()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CallsAWovenComponent, key: "caller"));

            // Assert
            Assert.That(
                (LabelText("left"), LabelText("right"), LabelText("tick")),
                Is.EqualTo(("0", "0", "0")));
        }

        [Test]
        public void Given_AGenericComponentWhoseChildReadsAParameter_When_AnotherStateChanges_Then_TheChildIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component<int>(GenericChild, 7, key: "generic"));

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("value"), LabelText("value"), LabelText("other")),
                Is.EqualTo((1, "7", "1")));
        }

        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_OneChangesAndThenTheOther_Then_EachIsBuiltOnceMore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoChildren, key: "two"));
            s_setLeft(1);
            mounted.FlushStateForTest();

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (Builds("left"), Builds("right"), LabelText("left"), LabelText("right")),
                Is.EqualTo((2, 2, "1", "1")),
                "A child kept through a render that changed another input is still compared against what it last built");
        }
    }
}
