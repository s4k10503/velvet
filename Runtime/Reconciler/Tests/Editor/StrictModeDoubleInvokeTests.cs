using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the Editor-only strict-mode double-invoke gate (<see cref="FiberStrictMode"/>).
    /// <list type="bullet">
    /// <item>When the gate is enabled, a function component's render body runs twice on mount so impure output is
    /// surfaced; when disabled it runs once.</item>
    /// <item>An impure render (one whose output depends on mutated module state) is reported as an error under the
    /// gate and is silent when the gate is off.</item>
    /// <item>When the gate is enabled, effect commit on mount runs an extra cleanup -> setup cycle (setup, cleanup,
    /// setup) so non-symmetric cleanup is surfaced; when disabled setup runs once with no cleanup.</item>
    /// <item>Unmount runs the single live cleanup, so setups and cleanups balance over a fiber's lifetime.</item>
    /// <item>An update commit whose deps changed runs cleanup -> setup exactly once: the double-invoke is a
    /// mount-only diagnostic and never tears down a live resource mid-frame.</item>
    /// <item>When the diagnostic second render throws, the first commit stands (its output is retained) and the
    /// non-determinism is reported.</item>
    /// <item>The committed effect factory is the first pass's closure, not the diagnostic pass's.</item>
    /// <item>The diagnostic pass returns pooled descendant objects, so repeated diagnostic re-renders never drain
    /// the pool or corrupt the committed tree.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The gate is a
    /// process-wide switch, so it is forced off in <see cref="SetUp"/> and <see cref="TearDown"/> and toggled on
    /// per test; counters are reset together in <see cref="ResetCounters"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class StrictModeDoubleInvokeTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberStrictMode.Enabled = false;
            ResetCounters();
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        #region Render double-invoke

        [Test]
        public void Given_GateOn_When_PureComponentRenders_Then_RenderRunsTwice()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(PureRender, key: "pure"));

            // Assert — the settled first render plus the diagnostic second render = two body invocations on mount
            Assert.That(s_pureRenderCount, Is.EqualTo(2), "The gate runs the render body twice on mount");
        }

        [Test]
        public void Given_GateOff_When_PureComponentRenders_Then_RenderRunsOnce()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(PureRender, key: "pure"));

            // Assert
            Assert.That(s_pureRenderCount, Is.EqualTo(1), "With the gate off the render body runs once");
        }

        [Test]
        public void Given_GateOn_When_ImpureComponentRenders_Then_LogsError()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            LogAssert.Expect(LogType.Error, new Regex("StrictMode.*impure"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ImpureRender, key: "impure"));

            // Assert — LogAssert.Expect verifies the diverging double-invoke output is reported as impure
        }

        [Test]
        public void Given_GateOff_When_ImpureComponentRenders_Then_NoErrorAndSingleCommit()
        {
            // Act — with the gate off the body runs once, so the hidden mutable state never diverges within a commit
            using var mounted = V.Mount(_root, V.Component(ImpureRender, key: "impure"));

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(1), "A single committed render leaves exactly one element");
        }

        [Test]
        public void Given_GateOn_When_DataAttributeValueIsImpureAtConstantCount_Then_LogsError()
        {
            // Arrange — the only divergence between the two diagnostic passes is a data attribute's VALUE; the
            // entry count is constant, so a count-only signature would not flag it.
            FiberStrictMode.Enabled = true;
            LogAssert.Expect(LogType.Error, new Regex("StrictMode.*impure"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ImpureDataValueRender, key: "impure-data"));

            // Assert — LogAssert.Expect verifies the diverging data value is caught as impure.
        }

        #endregion

        #region Effect double-invoke

        [Test]
        public void Given_GateOn_When_EffectMounts_Then_SetupRunsTwice()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Assert — mount setup -> diagnostic cleanup -> diagnostic setup leaves two setups
            Assert.That(s_effectSetupCount, Is.EqualTo(2), "The mount commit doubles the effect setup");
        }

        [Test]
        public void Given_GateOn_When_EffectMounts_Then_CleanupRunsOnce()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectCleanupCount, Is.EqualTo(1), "The mount double-invoke runs exactly one intermediate cleanup");
        }

        [Test]
        public void Given_GateOff_When_EffectMounts_Then_SetupRunsOnceWithoutCleanup()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That((s_effectSetupCount, s_effectCleanupCount), Is.EqualTo((1, 0)),
                "With the gate off the effect sets up once and runs no cleanup until unmount");
        }

        [Test]
        public void Given_GateOn_When_EffectUnmounts_Then_FinalCleanupBalancesSetup()
        {
            // Arrange
            FiberStrictMode.Enabled = true;
            var mounted = V.Mount(_root, V.Component(EffectRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act — the double cycle leaves one live cleanup, which unmount runs
            mounted.Dispose();

            // Assert
            Assert.That(s_effectCleanupCount, Is.EqualTo(s_effectSetupCount),
                "Over a fiber's lifetime setups and cleanups balance");
        }

        [Test]
        public void Given_GateOn_When_EffectUpdatesWithChangedDeps_Then_RunsCleanupSetupOnce()
        {
            // Arrange — settle the mount first (its setup is doubled to 2, cleanup to 1)
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(DepsEffectRender, key: "deps-effect"));
            mounted.FlushEffectsForTest();
            Assume.That((s_effectSetupCount, s_effectCleanupCount), Is.EqualTo((2, 1)),
                "Precondition: the mount commit doubled setup to 2 and cleanup to 1");

            // Act
            s_depsEffectKey.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — an update commit runs cleanup -> setup exactly once (the double-invoke is mount-only)
            Assert.That((s_effectSetupCount, s_effectCleanupCount), Is.EqualTo((3, 2)),
                "A deps-changed update runs cleanup and setup once each, never doubling mid-frame");
        }

        #endregion

        #region Diagnostic-pass guarantees

        [Test]
        public void Given_GateOn_When_DiagnosticRenderThrows_Then_FirstCommitSurvives()
        {
            // Arrange — the diagnostic (second) render is reported as non-deterministic, but the first commit stands.
            // FiberLogger.LogException emits a context Error line followed by the exception itself.
            FiberStrictMode.Enabled = true;
            LogAssert.Expect(LogType.Error, new Regex("StrictMode.*non-deterministic"));
            LogAssert.Expect(LogType.Error, new Regex("An exception occurred"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowOnSecondRender, key: "throw"));

            // Assert — the committed first render's Label is retained despite the second-pass throw
            Assert.That(GetLabel(_root).text, Is.EqualTo("ok"),
                "A throwing diagnostic pass does not abort the committed first render");
        }

        [Test]
        public void Given_GateOn_When_DiagnosticPassRuns_Then_CommittedEffectUsesFirstPassClosure()
        {
            // Arrange
            FiberStrictMode.Enabled = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(ClosureCaptureEffectRender, key: "closure"));
            mounted.FlushEffectsForTest();

            // Assert — if the diagnostic pass had overwritten the factory, the captured ordinal would be 2
            Assert.That(s_effectCapturedOrdinal, Is.EqualTo(1),
                "The committed effect factory is the first pass's closure, not the diagnostic pass's");
        }

        [Test]
        public void Given_GateOn_When_DiagnosticPassRunsManyTimes_Then_CommittedTreeStaysIntact()
        {
            // Arrange — a nested tree exercises descendant pooled objects; each diagnostic render must return them
            // recursively so the committed tree still reconciles after many re-renders.
            FiberStrictMode.Enabled = true;
            using var mounted = V.Mount(_root, V.Component(NestedRender, key: "nested"));

            // Act
            for (var i = 0; i < 20; i++)
            {
                s_nestedBump.Invoke(i);
                mounted.FlushStateForTest();
            }

            // Assert — the committed container still holds its two children (no pool corruption / stale reuse)
            Assert.That(_root.ElementAt(0).childCount, Is.EqualTo(2),
                "The committed DOM is intact after many diagnostic re-renders");
        }

        #endregion

        private static Label GetLabel(VisualElement root) => (Label)root.ElementAt(0);

        private static void ResetCounters()
        {
            s_pureRenderCount = 0;
            s_impureHiddenState = 0;
            s_impureDataValueState = 0;
            s_effectSetupCount = 0;
            s_effectCleanupCount = 0;
            s_throwRenderCount = 0;
            s_depsEffectKey = null;
            s_closureRenderOrdinal = 0;
            s_effectCapturedOrdinal = 0;
            s_nestedBump = null;
        }

        #region Pure component

        private static int s_pureRenderCount;

        [Component]
        private static VNode PureRender()
        {
            s_pureRenderCount++;
            var (value, _) = Hooks.UseState(0);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region Impure component (mutates hidden state during render)

        private static int s_impureHiddenState;

        [Component]
        private static VNode ImpureRender()
        {
            // Reading + mutating module state during render makes the output depend on invocation count,
            // so the two diagnostic passes diverge.
            s_impureHiddenState++;
            return V.Label(text: $"impure-{s_impureHiddenState}");
        }

        private static int s_impureDataValueState;

        [Component]
        private static VNode ImpureDataValueRender()
        {
            // Impurity confined to a data attribute's VALUE: the entry count stays 1 across both diagnostic
            // passes while the value diverges. A count-only purity signature would miss this, so the signature
            // must fold in the key/value pairs.
            s_impureDataValueState++;
            return V.Div(props: new FiberElementProps
            {
                Data = new Dictionary<string, string> { ["n"] = s_impureDataValueState.ToString() },
            });
        }

        #endregion

        #region Effect component

        private static int s_effectSetupCount;
        private static int s_effectCleanupCount;

        [Component]
        private static VNode EffectRender()
        {
            Hooks.UseEffect(() =>
            {
                s_effectSetupCount++;
                return () => s_effectCleanupCount++;
            }, Array.Empty<object>());
            return V.Label(text: "effect");
        }

        #endregion

        #region Throw-on-second-render component

        private static int s_throwRenderCount;

        [Component]
        private static VNode ThrowOnSecondRender()
        {
            s_throwRenderCount++;
            if (s_throwRenderCount >= 2)
            {
                throw new InvalidOperationException("second render throws");
            }
            return V.Label(text: "ok");
        }

        #endregion

        #region Deps effect component (mount vs update double-invoke)

        private static Action<int> s_depsEffectKey;

        [Component]
        private static VNode DepsEffectRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            s_depsEffectKey = setKey;
            Hooks.UseEffect(() =>
            {
                s_effectSetupCount++;
                return () => s_effectCleanupCount++;
            }, new object[] { key });
            return V.Label(text: key.ToString());
        }

        #endregion

        #region Closure-capture effect component

        private static int s_closureRenderOrdinal;
        private static int s_effectCapturedOrdinal;

        [Component]
        private static VNode ClosureCaptureEffectRender()
        {
            var ordinal = ++s_closureRenderOrdinal;
            Hooks.UseEffect(() =>
            {
                s_effectCapturedOrdinal = ordinal;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "closure");
        }

        #endregion

        #region Nested component (descendant pool exercise)

        private static Action<int> s_nestedBump;

        [Component]
        private static VNode NestedRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_nestedBump = setValue;
            return V.Div(
                children: new VNode[]
                {
                    V.Label(text: $"a-{value}", key: "a"),
                    V.Label(text: $"b-{value}", key: "b"),
                });
        }

        #endregion
    }
}
