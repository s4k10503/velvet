using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// reading a state, a prop or a hook value, for a child inside a child that is itself built again, for an arm of a
    /// conditional whose condition is not cached, and for a child the render did not reach (an early return) when it
    /// is reached again with equal inputs.</item>
    /// <item>A child whose expression builds nothing is cached as nothing.</item>
    /// <item>A component opted out with <c>[Component(Compiler = false)]</c> builds every child on every render.</item>
    /// <item>A child reading something no dependency stands for — a <c>MutableRef</c>, a mutable static, a call
    /// to a method of the fixture — is built on every render the gate misses, as it was before scopes.</item>
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
            s_setRightText = null;
            s_seen.Clear();
            s_setLeft = null;
            s_setRight = null;
            s_setCount = null;
            s_setOther = null;
            s_setTick = null;
            s_setShown = null;
            s_setPhase = null;
            s_setPhaseText = null;
            s_strictCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        private static readonly Dictionary<string, int> s_builds = new();
        private static int s_ambient;
        private static Action<string> s_setRightText;
        private static readonly List<object> s_seen = new();
        private static Action<int> s_setLeft;
        private static Action<int> s_setRight;
        private static Action<int> s_setCount;
        private static Action<int> s_setOther;
        private static Action<int> s_setTick;
        private static Action<bool> s_setShown;
        private static Action<int> s_setPhase;
        private static Action<string> s_setPhaseText;
        private static int s_strictCalls;
        private static readonly int[] s_strictTags = { 0, 1, 0, 0 };

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

        // Counts where its constructor runs: a V factory takes it as the `data` of an element, so the count is of the
        // scope's expression running, and it reads nothing but a constant.
        private sealed class Counted : Dictionary<string, string>
        {
            public Counted(string key)
            {
                s_builds[key] = Builds(key) + 1;
            }
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
        private static VNode StableHandler()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState("0");
            s_setCount = setCount;
            s_setRightText = setOther;
            Action onClick = () => s_seen.Add(count);
            s_seen.Add(onClick);
            return V.Div("root", V.Button(name: "add", onClick: onClick), V.Label(name: "other", text: other));
        }

        [Component]
        private static VNode DerivedSequence()
        {
            var (threshold, setThreshold) = Hooks.UseState(1);
            var (other, setOther) = Hooks.UseState("0");
            s_setCount = setThreshold;
            s_setRightText = setOther;
            var visible = System.Linq.Enumerable.Where(new[] { 1, 2, 3 }, item => item > threshold);
            s_seen.Add(visible);
            return V.Div("root", V.Label(name: "other", text: other), V.Label(name: "shown", text: System.Linq.Enumerable.Count(visible).ToString()));
        }

        [Component]
        private static VNode TextChildren()
        {
            var (left, _) = Hooks.UseState("0");
            var (right, setRight) = Hooks.UseState("0");
            s_setRightText = setRight;
            return V.Div("root",
                V.Label(name: "left", text: left, data: new Counted("left")),
                V.Label(name: "right", text: right, data: new Counted("right")));
        }

        [Component(Compiler = false)]
        private static VNode OptedOutTextChildren()
        {
            var (left, _) = Hooks.UseState("0");
            var (right, setRight) = Hooks.UseState("0");
            s_setRightText = setRight;
            return V.Div("root",
                V.Label(name: "left", text: left, data: new Counted("left")),
                V.Label(name: "right", text: right, data: new Counted("right")));
        }

        [Component]
        private static VNode SkippedTextChild()
        {
            var (shown, setShown) = Hooks.UseState(true);
            var (text, _) = Hooks.UseState("7");
            s_setShown = setShown;
            if (!shown)
            {
                return V.Label(name: "hidden", text: "hidden");
            }
            return V.Div("root", V.Label(name: "kept", text: text, data: new Counted("kept")), V.Label(name: "tail", text: "tail"));
        }

        [Component]
        private static VNode DiscardedTextAttempt()
        {
            var (phase, setPhase) = Hooks.UseState("a");
            var (other, setOther) = Hooks.UseState("0");
            s_setPhaseText = setPhase;
            s_setRightText = setOther;
            if (phase == "b")
            {
                setPhase.Invoke("c");
            }
            return V.Div("root",
                V.Label(name: "attempt", text: phase, data: new Counted("attempt")),
                V.Label(name: "other", text: other));
        }

        [Component]
        private static VNode RevertedTextAttempt()
        {
            var (phase, setPhase) = Hooks.UseState("a");
            var (other, setOther) = Hooks.UseState("0");
            s_setPhaseText = setPhase;
            s_setRightText = setOther;
            if (phase == "b")
            {
                setPhase.Invoke("a");
            }
            return V.Div("root",
                V.Label(name: "attempt", text: phase, data: new Counted("attempt")),
                V.Label(name: "other", text: other));
        }

        // Opted out so that its own gate plays no part in what the case reads.
        [Component(Compiler = false)]
        private static VNode CallsAWovenTextComponent()
        {
#pragma warning disable VEL103
            return V.Div("host", TextChildren());
#pragma warning restore VEL103
        }

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
        private static VNode ChildInAConditionalArm()
        {
            var counter = Hooks.UseMutableRef(0);
            var (left, setLeft) = Hooks.UseState(0);
            var (right, _) = Hooks.UseState(0);
            s_setLeft = setLeft;
            return V.Div("root",
                counter.Current > 0
                    ? V.Label(name: "arm", text: new Built("taken", left.ToString()).Text)
                    : V.Label(name: "arm", text: new Built("other", right.ToString()).Text),
                V.Label(name: "tail", text: left.ToString()));
        }

        // The calls the weaver injects, by hand, so that the dependencies of each pass can be chosen: the first pass
        // of a mount reads tag 0, its diagnostic pass tag 1.
        [Component]
        private static VNode HandMemoizedScope()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            Hooks.TryGetMemoizedVNode(MethodBase.GetCurrentMethod().MethodHandle, new object[] { shown }, out var slot, out _);
            if (!shown)
            {
                return V.Label(name: "hidden", text: "hidden");
            }
            var deps = new object[] { s_strictTags[Math.Min(s_strictCalls++, s_strictTags.Length - 1)] };
            if (!Hooks.TryGetMemoizedScope(slot, 0, deps, out var node))
            {
                node = V.Label(name: "scoped", text: new Built("scoped", "s").Text);
                Hooks.StoreMemoizedScope(slot, 0, deps, node);
            }
            return V.Div("root", node);
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

        private static System.Threading.Tasks.TaskCompletionSource<int> s_source = new();

        [Component]
        private static VNode ReadsATaskGetter()
        {
            var task = Hooks.UseMemo(() => s_source.Task, "task");
            var (other, setOther) = Hooks.UseState(0);
            s_setOther = setOther;
            return V.Div("root",
                V.Label(name: "done", text: task.IsCompleted.ToString()),
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
        public void Given_AHandlerReadingState_When_AnotherTextChanges_Then_TheRenderHandsTheSameDelegate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StableHandler, key: "handler"));

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ReferenceEquals(s_seen[0], s_seen[^1]), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base builds the delegate afresh on every render, so it hands another
        // one when the state changes too.
        // What it pins is a changed capture reaching the gate of the lambda: emptying
        // `plan.Deps.SelectMany(DependencyStore)` in `BuildScopeGate` serves the first delegate again.
        [Test]
        public void Given_AHandlerReadingState_When_ThatStateChanges_Then_TheRenderHandsAnotherDelegate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(StableHandler, key: "handler"));

            // Act
            s_setCount(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ReferenceEquals(s_seen[0], s_seen[^1]), Is.False);
        }

        [Test]
        public void Given_ASequenceDerivedFromState_When_AnotherTextChanges_Then_TheRenderHandsTheSameSequence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DerivedSequence, key: "sequence"));

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ReferenceEquals(s_seen[0], s_seen[^1]), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base derives the sequence afresh on every render, so it shows the new
        // count too.
        // What it pins is a changed input reaching the gate of the derived value: emptying
        // `plan.Deps.SelectMany(DependencyStore)` in `BuildScopeGate` serves the first sequence again.
        [Test]
        public void Given_ASequenceDerivedFromState_When_ThatStateChanges_Then_TheRenderHandsAnotherSequence()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DerivedSequence, key: "sequence"));

            // Act
            s_setCount(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("shown"), Is.EqualTo("1"));
        }

        [Test]
        public void Given_TwoChildrenReadingDifferentTexts_When_OneChanges_Then_TheOtherIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TextChildren, key: "text"));

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("left"), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the new text too.
        // What it pins is a scope whose dependencies changed being built again: `true` in place of
        // `!ObjectIs.AreEqualDeps(slot.LastDeps, deps)` in `TryHitScope` serves the child the first text.
        [Test]
        public void Given_TwoChildrenReadingDifferentTexts_When_OneChanges_Then_TheChildReadingItShowsIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TextChildren, key: "text"));

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("right"), Is.EqualTo("1"));
        }

        // GREEN_ON_BASE(characterization): the base builds every child on every miss of an unwoven body too.
        // What it pins is the opt-out being read ahead of the scopes: `return true;` in place of
        // `return CompilerEnabled(attr);` in `IsCandidate` weaves the body and builds its left child once.
        [Test]
        public void Given_ATextComponentOptedOutOfTheCompiler_When_OneTextChanges_Then_EveryChildIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OptedOutTextChildren, key: "opted-out-text"));

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("left"), Is.EqualTo(2));
        }

        [Test]
        public void Given_ATextChildTheRenderDidNotReach_When_ItIsReachedAgainWithEqualInputs_Then_ItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SkippedTextChild, key: "skipped-text"));
            s_setShown(false);
            mounted.FlushStateForTest();

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("kept"), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base builds the child again and shows it too.
        // What it pins is the hit handing back the node the scope built: `cached = null;` in place of
        // `cached = hit?.CachedResult;` in `TryGetMemoizedScope` leaves the label out.
        [Test]
        public void Given_ATextChildTheRenderDidNotReach_When_ItIsReachedAgainWithEqualInputs_Then_ItIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SkippedTextChild, key: "skipped-text"));
            s_setShown(false);
            mounted.FlushStateForTest();

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("kept"), Is.EqualTo("7"), "The render that returned early left the child's cache as the last render that built it left it");
        }

        [Test]
        public void Given_ATextChildKeptByARenderAfterADiscardedAttempt_When_AnotherTextChanges_Then_ItIsNotBuiltAgain()
        {
            // Arrange — the first attempt of the next render reads a text the render-phase update then replaces
            using var mounted = V.Mount(_root, V.Component(DiscardedTextAttempt, key: "discarded-text"));
            s_setPhaseText("b");
            mounted.FlushStateForTest();

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("attempt"), Is.EqualTo(3), "Three builds are the mount, the discarded attempt and the settled one; the render that changes another text adds none");
        }

        [Test]
        public void Given_ATextChildStagedByAnAttemptTheGateThenHitsOver_When_AnotherTextChanges_Then_ItIsNotBuiltAgain()
        {
            // Arrange — the first attempt of the next render builds the child for a text the render-phase update then puts back
            using var mounted = V.Mount(_root, V.Component(RevertedTextAttempt, key: "reverted-text"));
            s_setPhaseText("b");
            mounted.FlushStateForTest();

            // Act
            s_setRightText("1");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("attempt"), Is.EqualTo(2), "Two builds are the mount and the attempt the gate then hit over; the render that changes another text adds none");
        }

        // GREEN_ON_BASE(characterization): the base builds the child afresh on every miss, so it shows the settled text too.
        // What it pins is each attempt of a render starting its scopes from what is committed: removing
        // `scope.NextCachedResult = scope.CachedResult;` from `RestageScopes` commits the node the discarded attempt built.
        [Test]
        public void Given_ATextChildStagedByAnAttemptTheGateThenHitsOver_When_TheRenderSettles_Then_ItShowsTheTextThatSettled()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RevertedTextAttempt, key: "reverted-text"));

            // Act
            s_setPhaseText("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("attempt"), Is.EqualTo("a"));
        }

        // GREEN_ON_BASE(characterization): the base builds the called component's children as well.
        // What it pins is a scope with no slot to cache in running its child: `TryGetMemoizedScope` answering
        // `true` where it was granted none returns the cached nothing in place of the child.
        [Test]
        public void Given_AWovenTextComponentCalledAsAMethod_When_Rendered_Then_ItsChildrenAreBuilt()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CallsAWovenTextComponent, key: "caller-text"));

            // Assert
            Assert.That(LabelText("left"), Is.EqualTo("0"));
        }

        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_OneStateChanges_Then_TheOtherIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoChildren, key: "two"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("left"), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the new value too.
        // What it pins is a scope whose dependencies changed being built again: `true` in place of
        // `!ObjectIs.AreEqualDeps(slot.LastDeps, deps)` in `TryHitScope` serves the child the first value.
        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_OneStateChanges_Then_TheChildReadingItShowsIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoChildren, key: "two"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("right"), Is.EqualTo("1"));
        }

        [Test]
        public void Given_ChildrenReadingAPropAndAState_When_TheStateChanges_Then_TheChildReadingTheStablePropIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TitleAndCount, new TitleProps("t"), key: "child"));

            // Act
            s_setCount(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("title"), Is.EqualTo(1));
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
            Assert.That(Builds("left"), Is.EqualTo(2));
        }

        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_OneChangesAndThenTheOther_Then_TheFirstIsBuiltOnceMore()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoChildren, key: "two"));
            s_setLeft(1);
            mounted.FlushStateForTest();

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("left"), Is.EqualTo(2), "A child kept through a render that changed another input is still compared against what it last built");
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
            Assert.That(Builds("count"), Is.EqualTo(1));
        }

        [Test]
        public void Given_ChildrenNestedInAChildReadingBoth_When_OneStateChanges_Then_TheInnerChildNotReadingItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedChildren, key: "nested"));

            // Act
            s_setRight(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("a"), Is.EqualTo(1), "The group reads both states and is built again; the label inside it reading only the first is not");
        }

        [Test]
        public void Given_AChildInTheLastArmOfAConditionalThatIsNoScope_When_AnotherStateChanges_Then_ItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ChildInAConditionalArm, key: "arm"));

            // Act
            s_setLeft(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("other"), Is.EqualTo(1), "The condition reads a MutableRef, so the conditional is built on every miss and only its arms are cached");
        }

        [Test]
        public void Given_StrictMode_When_TheDiagnosticRenderBuildsAScopeOfOtherInputs_Then_TheNextRenderStillHitsTheCommittedOne()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(HandMemoizedScope, key: "strict"));
            s_setShown(false);
            mounted.FlushStateForTest();

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("scoped"), Is.EqualTo(2), "The mount built it twice, as its two passes read different inputs, and the render showing it again built it no more");
        }

        [Test]
        public void Given_AChildTheRenderDidNotReach_When_ItIsReachedAgainWithEqualInputs_Then_ItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SkippedChild, key: "skipped"));
            s_setShown(false);
            mounted.FlushStateForTest();

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("kept"), Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base builds the child again and shows it too.
        // What it pins is the hit handing back the node the scope built: `cached = null;` in place of
        // `cached = hit?.CachedResult;` in `TryGetMemoizedScope` leaves the label out.
        [Test]
        public void Given_AChildTheRenderDidNotReach_When_ItIsReachedAgainWithEqualInputs_Then_ItIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SkippedChild, key: "skipped"));
            s_setShown(false);
            mounted.FlushStateForTest();

            // Act
            s_setShown(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("kept"), Is.EqualTo("7"), "The render that returned early left the child's cache as the last render that built it left it");
        }

        [Test]
        public void Given_AChildBuildingNothing_When_AnotherStateChanges_Then_ItIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ChildBuildingNothing, key: "nothing"));

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Builds("maybe"), Is.EqualTo(1));
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
        // What it pins is a property read that answers from outside the render staying out of every scope: `return true;`
        // at the top of `IsPlainGetter` caches the child on the task alone, and it shows the first value.
        [Test]
        public void Given_AChildReadingATaskProperty_When_AnotherStateChanges_Then_ItShowsTheCurrentValue()
        {
            // Arrange
            s_source = new System.Threading.Tasks.TaskCompletionSource<int>();
            using var mounted = V.Mount(_root, V.Component(ReadsATaskGetter, key: "task"));
            s_source.SetResult(1);

            // Act
            s_setOther(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelText("done"), Is.EqualTo("True"));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds every child on a miss, so it shows the current value too.
        // What it pins is a mutable static staying out of every scope: `return true;` in place of the return that
        // answers for a static field in `ScanField` caches the child on `input` alone, and it shows the first value.
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
            Assert.That(Builds("attempt"), Is.EqualTo(2), "The committed deps are those of the settled attempt, not of the one that was discarded");
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
            Assert.That(LabelText("hooked"), Is.EqualTo("hooked0"));
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
            Assert.That(LabelText("left"), Is.EqualTo("0"));
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
            Assert.That(Builds("value"), Is.EqualTo(1));
        }
    }
}
