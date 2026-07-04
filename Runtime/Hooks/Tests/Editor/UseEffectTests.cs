using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseEffect"/> in a function component.
    /// <list type="bullet">
    /// <item>The effect is asynchronous: it does not run during Mount and runs only when effects are flushed.</item>
    /// <item>It runs once on mount, and on a deps change it runs the previous cleanup and then the new setup.</item>
    /// <item>Deps are compared element-wise under Object.is: a fresh deps array with equal element values does not re-run, while a reconstructed content-equal reference-type dep does re-run.</item>
    /// <item>Omitting deps re-runs (cleanup then setup) on every render; empty deps stays mount-only.</item>
    /// <item>Cleanup runs on unmount; a pending effect of an already-unmounted component is dropped.</item>
    /// <item>Initial mount runs setup exactly once with no setup→cleanup→setup double-invoke.</item>
    /// <item>A parent re-render does not cascade cleanup into a child whose deps are unchanged.</item>
    /// <item>Effects run after layout effects of the same commit.</item>
    /// <item>The Func&lt;IDisposable&gt; overload disposes the previous instance before re-executing.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers, using the
    /// <c>s_{componentName}{FieldName}</c> prefix to avoid collisions across components in the same fixture.
    /// </remarks>
    [TestFixture]
    internal sealed class UseEffectTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetEffectRecorder();
            ResetRenderPhasePassiveEffect();
            ResetOrderRecorder();
            ResetParentWithChildEffect();
            ResetCtsCancellation();
            ResetEveryRender();
        }

        #region Deps-omitted overload (factory runs every render)

        [Test]
        public void Given_NoDeps_When_RerenderedAndFlushed_Then_RunsCleanupThenSetup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EveryRenderEffectRender, key: "every"));
            mounted.FlushEffectsForTest();
            Assume.That(s_everyRenderRunLog, Is.EqualTo(new[] { "effect" }),
                "Precondition: the initial render ran the deps-omitted effect once");

            // Act
            s_everyRenderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_everyRenderRunLog, Is.EqualTo(new[] { "effect", "cleanup", "effect" }),
                "A deps-omitted effect re-runs cleanup then setup on every render");
        }

        [Test]
        public void Given_NoDeps_When_RerenderedMultipleTimes_Then_RunsCleanupThenSetupEachTime()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EveryRenderEffectRender, key: "every"));
            mounted.FlushEffectsForTest();

            // Act
            for (var i = 1; i <= 3; i++)
            {
                s_everyRenderSetTick.Invoke(i);
                mounted.FlushStateForTest();
                mounted.FlushEffectsForTest();
            }

            // Assert
            Assert.That(s_everyRenderRunLog, Is.EqualTo(new[]
            {
                "effect",
                "cleanup", "effect",
                "cleanup", "effect",
                "cleanup", "effect",
            }), "Three re-renders trigger cleanup + setup each time when deps are omitted");
        }

        [Test]
        public void Given_NoDepsVsEmptyDeps_When_Rerendered_Then_OnlyDepsOmittedReRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EveryRenderVsMountOnlyRender, key: "compare"));
            mounted.FlushEffectsForTest();

            // Act
            s_everyRenderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_everyRenderRunLog, Is.EqualTo(new[] { "every", "mount-only", "cleanup-every", "every" }),
                "A deps-omitted effect re-runs on every render while an empty-deps effect stays mount-only");
        }

        [Test]
        public void Given_NoDeps_FuncIDisposableOverload_When_Rerendered_Then_DisposesBeforeReExecution()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EveryRenderDisposableRender, key: "every-disposable"));
            mounted.FlushEffectsForTest();
            Assume.That(s_everyRenderDisposableLog, Is.EqualTo(new[] { "create:0" }),
                "Precondition: the initial render created the first disposable with no prior instance to dispose");

            // Act
            s_everyRenderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_everyRenderDisposableLog, Is.EqualTo(new[] { "create:0", "dispose:0", "create:1" }),
                "The Func<IDisposable> deps-omitted overload disposes the previous instance and creates a new one on re-render");
        }

        #endregion

        [Test]
        public void Given_InitialMount_When_NotYetFlushed_Then_AsyncEffectDoesNotRun()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));

            // Assert — the async effect is deferred until effects are flushed
            Assert.That(s_effectRecorderRunLog, Is.Empty, "The async effect does not run during Mount");
        }

        [Test]
        public void Given_InitialMount_When_EffectsFlushed_Then_EffectRunsOnce()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }), "Flushing runs the mount effect once");
        }

        [Test]
        public void Given_StableDeps_When_Rerendered_Then_EffectRunsOnce()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act
            s_effectRecorderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }), "Stable deps do not re-run the effect");
        }

        [Test]
        public void Given_DepsChanged_When_Rerendered_Then_RunsCleanupThenEffect()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act
            s_effectRecorderDeps = new object[] { 1 };
            s_effectRecorderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect", "cleanup", "effect" }),
                "A deps change runs the previous cleanup then the new setup");
        }

        [Test]
        public void Given_EffectAlreadyRan_When_Unmounted_Then_CleanupRuns()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };
            var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect", "cleanup" }), "Unmount runs the effect cleanup");
        }

        [Test]
        public void Given_UnmountedBeforeFlush_When_EffectsFlushed_Then_PendingEffectIsSkipped()
        {
            // Arrange — unmount drops the pending effect via the CleanupAll path before it can run
            s_effectRecorderDeps = new object[] { 0 };
            var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.Dispose();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.Empty, "Flushing after unmount runs no effects");
        }

        [Test]
        public void Given_RerenderBeforeFlush_When_EffectsFlushed_Then_InitialEffectStillRuns()
        {
            // Arrange
            s_effectRecorderDeps = new object[] { 0 };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));

            // Act — re-render before the mount effect has been flushed
            s_effectRecorderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }),
                "Re-rendering before flush does not lose the initial mount effect");
        }

        [Test]
        public void Given_FreshDepsArrayWithEqualValues_When_Rerendered_Then_DoesNotRerunEffect()
        {
            // Arrange — deps are compared element-wise, not by array reference, so a fresh object[] each
            // render with element-equal values is unchanged. If the array reference were compared instead,
            // every render would invalidate the effect and a fire-and-forget chain would have its
            // CancellationTokenSource cancelled mid-flight. Three re-renders exercise the comparison repeatedly.
            s_effectRecorderDeps = new object[] { "stable" };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act
            for (var i = 1; i <= 3; i++)
            {
                s_effectRecorderDeps = new object[] { "stable" };
                s_effectRecorderSetTick.Invoke(i);
                mounted.FlushStateForTest();
                mounted.FlushEffectsForTest();
            }

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }),
                "Fresh deps arrays carrying equal element values keep the effect run count at one");
        }

        [Test]
        public void Given_FreshRecordDepWithEqualContent_When_Rerendered_Then_RerunsEffect()
        {
            // Arrange — under Object.is, a reference-type dep (record) reconstructed with identical content
            // each render is a changed dep (compared by reference, not by value), so the effect re-runs. A
            // structural comparer would wrongly freeze the effect. Contrast with the string-value case above.
            s_effectRecorderDeps = new object[] { new EffectDepRecord("x") };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));
            mounted.FlushEffectsForTest();

            // Act
            s_effectRecorderDeps = new object[] { new EffectDepRecord("x") };
            s_effectRecorderSetTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect", "cleanup", "effect" }),
                "A fresh content-equal record dep re-runs the effect under Object.is reference comparison");
        }

        [Test]
        public void Given_InitialMountWithStableDeps_When_EffectsFlushed_Then_CleanupDoesNotRunBeforeUnmount()
        {
            // Arrange — initial mount does not perform a setup→cleanup→setup double-invoke; cleanup runs
            // only at unmount or on a deps change.
            s_effectRecorderDeps = new object[] { 0 };
            using var mounted = V.Mount(_root, V.Component(EffectRecorderRender, key: "effect"));

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }),
                "Initial mount runs setup exactly once and does not double-invoke");
        }

        [Test]
        public void Given_ChildEffectStableDeps_When_ParentRerenders_Then_DoesNotRunChildCleanup()
        {
            // Arrange — effect runs are scoped to the owning fiber's deps comparison, not the parent's
            // lifecycle. Mount + flush so the child effect runs once.
            using var mounted = V.Mount(_root, V.Component(ParentWithChildEffectRender, key: "parent"));
            mounted.FlushEffectsForTest();

            // Act — re-render the parent while the child's deps are unchanged
            s_setParentRerenderTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_effectRecorderRunLog, Is.EqualTo(new[] { "effect" }),
                "A parent re-render does not cascade child cleanup when the child's deps are stable; child setup runs once across both renders");
        }

        [Test]
        public void Given_CleanupCancelsCtsOnly_When_Unmounted_Then_AsyncChainSurvivesWithoutObjectDisposed()
        {
            // Arrange + Act — UseEffect cleanup runs the user-provided cleanup exactly once on unmount.
            // The cleanup here only Cancels the CTS (never Disposes it), so an observer downstream of the
            // token can still read IsCancellationRequested. Mount + flush (setup runs) then dispose (cleanup runs).
            using (var mounted = V.Mount(_root, V.Component(CtsCancellationRender, key: "cts-only")))
            {
                mounted.FlushEffectsForTest();
            }

            // Assert — the Token.Register callback reads IsCancellationRequested successfully. Had cleanup
            // also Disposed the CTS, that read would throw and "cancel" would never reach the log.
            Assert.That(s_ctsCancelOrder, Is.EqualTo(new[] { "setup", "cancel" }),
                "Cleanup runs once and only Cancels the CTS, so the async chain observes IsCancellationRequested without crashing");
        }

        [Test]
        public void Given_BothLayoutAndEffectScheduled_When_EffectsFlushed_Then_EffectRunsAfterLayout()
        {
            // Arrange — the layout effect fires synchronously inside Mount, before the async effect
            using var mounted = V.Mount(_root, V.Component(OrderRecorderRender, key: "order"));
            Assume.That(s_orderRecorderRunLog, Is.EqualTo(new[] { "layout" }),
                "Precondition: the layout effect fired synchronously inside Mount");

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_orderRecorderRunLog, Is.EqualTo(new[] { "layout", "effect" }),
                "The async effect runs after the layout effect of the same commit");
        }

        [Test]
        public void Given_RenderPhaseSetState_When_Mounted_Then_RendersExactlyTwice()
        {
            // Arrange + Act — render-phase setState re-runs Render() once before commit
            using var mounted = V.Mount(_root, V.Component(RenderPhasePassiveEffectRender, key: "render-phase"));
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_renderPhasePassiveRenderCount, Is.EqualTo(2),
                "Render-phase setState re-runs Render() once before commit");
        }

        [Test]
        public void Given_RenderPhaseSetState_When_EffectsFlushed_Then_PassiveEffectRunsOnce()
        {
            // Arrange + Act
            using var mounted = V.Mount(_root, V.Component(RenderPhasePassiveEffectRender, key: "render-phase"));
            mounted.FlushEffectsForTest();
            Assume.That(s_renderPhasePassiveRenderCount, Is.EqualTo(2),
                "Precondition: render-phase setState re-ran Render() once before commit");

            // Assert
            Assert.That(s_renderPhasePassiveRunLog, Is.EqualTo(new[] { "effect" }),
                "The mount-only passive effect runs exactly once across the render-phase re-run");
        }

        #region RenderPhasePassiveEffect component (render-phase setState + mount-only passive effect)

        private static List<string> s_renderPhasePassiveRunLog;
        private static string s_renderPhasePassiveTarget;
        private static int s_renderPhasePassiveRenderCount;

        private static void ResetRenderPhasePassiveEffect()
        {
            s_renderPhasePassiveRunLog = new List<string>();
            s_renderPhasePassiveTarget = "normalized";
            s_renderPhasePassiveRenderCount = 0;
        }

        [Component]
        public static VNode RenderPhasePassiveEffectRender()
        {
            s_renderPhasePassiveRenderCount++;
            var (value, setValue) = Hooks.UseState("initial");
            if (value != s_renderPhasePassiveTarget)
            {
                setValue.Invoke(s_renderPhasePassiveTarget);
            }
            Hooks.UseEffect(() =>
            {
                s_renderPhasePassiveRunLog.Add("effect");
                return () => s_renderPhasePassiveRunLog.Add("cleanup");
            }, Array.Empty<object>());
            return V.Label(text: value);
        }

        #endregion

        #region EffectRecorder component (UseEffect with deps)

        internal sealed record EffectDepRecord(string Value);

        private static List<string> s_effectRecorderRunLog;
        private static object[] s_effectRecorderDeps;
        private static Action<int> s_effectRecorderSetTick;

        private static void ResetEffectRecorder()
        {
            s_effectRecorderRunLog = new List<string>();
            s_effectRecorderDeps = Array.Empty<object>();
            s_effectRecorderSetTick = null;
        }

        [Component]
        public static VNode EffectRecorderRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_effectRecorderSetTick = setTick;

            Hooks.UseEffect(() =>
            {
                s_effectRecorderRunLog.Add("effect");
                return () => s_effectRecorderRunLog.Add("cleanup");
            }, s_effectRecorderDeps);

            return V.Label(text: "x");
        }

        #endregion

        #region OrderRecorder component (UseLayoutEffect + UseEffect order)

        private static List<string> s_orderRecorderRunLog;

        private static void ResetOrderRecorder()
        {
            s_orderRecorderRunLog = new List<string>();
        }

        [Component]
        public static VNode OrderRecorderRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_orderRecorderRunLog.Add("layout");
                return null;
            }), Array.Empty<object>());

            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_orderRecorderRunLog.Add("effect");
                return null;
            }), Array.Empty<object>());

            return V.Label(text: "x");
        }

        #endregion

        #region ParentWithChildEffect (parent rerender does not cascade child cleanup)

        private static Action<int> s_setParentRerenderTick;

        private static void ResetParentWithChildEffect()
        {
            s_setParentRerenderTick = null;
        }

        [Component]
        public static VNode ParentWithChildEffectRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setParentRerenderTick = setTick;
            return V.Component(EffectRecorderRender, key: "child-effect");
        }

        #endregion

        #region EveryRenderEffect (deps-omitted overload: factory runs every render)

        private static List<string> s_everyRenderRunLog;
        private static List<string> s_everyRenderDisposableLog;
        private static Action<int> s_everyRenderSetTick;

        private static void ResetEveryRender()
        {
            s_everyRenderRunLog = new List<string>();
            s_everyRenderDisposableLog = new List<string>();
            s_everyRenderSetTick = null;
        }

        [Component]
        public static VNode EveryRenderEffectRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_everyRenderSetTick = setTick;

            Hooks.UseEffect(() =>
            {
                s_everyRenderRunLog.Add("effect");
                return () => s_everyRenderRunLog.Add("cleanup");
            });

            return V.Label(text: "x");
        }

        [Component]
        public static VNode EveryRenderVsMountOnlyRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_everyRenderSetTick = setTick;

            Hooks.UseEffect(() =>
            {
                s_everyRenderRunLog.Add("every");
                return () => s_everyRenderRunLog.Add("cleanup-every");
            });

            Hooks.UseEffect(() =>
            {
                s_everyRenderRunLog.Add("mount-only");
                return () => s_everyRenderRunLog.Add("cleanup-mount-only");
            }, Array.Empty<object>());

            return V.Label(text: "x");
        }

        [Component]
        public static VNode EveryRenderDisposableRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_everyRenderSetTick = setTick;

            Hooks.UseEffect((Func<IDisposable>)(() =>
            {
                var capturedTick = tick;
                s_everyRenderDisposableLog.Add($"create:{capturedTick}");
                return new ActionDisposable(() => s_everyRenderDisposableLog.Add($"dispose:{capturedTick}"));
            }));

            return V.Label(text: "x");
        }

        #endregion

        #region CtsCancellation (cleanup invokes Cancel only, async chain survives)

        private static System.Collections.Generic.List<string> s_ctsCancelOrder;

        private static void ResetCtsCancellation()
        {
            s_ctsCancelOrder = new System.Collections.Generic.List<string>();
        }

        [Component]
        public static VNode CtsCancellationRender()
        {
            Hooks.UseEffect(() =>
            {
                var cts = new System.Threading.CancellationTokenSource();
                s_ctsCancelOrder.Add("setup");
                // Simulate fire-and-forget: register a cancel callback that touches the token.
                // If cleanup Disposed the CTS, this callback would crash on Cancel; with the
                // Cancel-only pattern it logs and proceeds.
                cts.Token.Register(() =>
                {
                    if (cts.IsCancellationRequested) s_ctsCancelOrder.Add("cancel");
                });
                return () =>
                {
                    cts.Cancel();
                    // Intentionally omit cts.Dispose() — the fire-and-forget async chain that
                    // captured the token must observe IsCancellationRequested (or call
                    // TrySetCanceled internally) without ObjectDisposedException.
                };
            }, Array.Empty<object>());

            return V.Label(text: "cts");
        }

        #endregion
    }
}
