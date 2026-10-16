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
            s_setRightText = null;
            s_setShown = null;
            s_setPhaseText = null;
            s_strictCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        private static readonly Dictionary<string, int> s_builds = new();
        private static Action<string> s_setRightText;
        private static Action<bool> s_setShown;
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

        #region Components

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

        #endregion

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
            Assert.That(Builds("attempt"), Is.EqualTo(3), "The committed deps are those of the settled attempt, not of the one that was discarded");
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
            Assert.That(Builds("attempt"), Is.EqualTo(2), "The attempt the gate hit over left no staging for the settle to promote");
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

    }
}
