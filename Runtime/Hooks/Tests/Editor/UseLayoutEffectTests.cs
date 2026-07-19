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
    internal sealed record EffectTestState(int Count, string Label)
    {
        public static readonly EffectTestState Initial = new(0, "init");
    }

    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseLayoutEffect"/> in a function component.
    /// <list type="bullet">
    /// <item>The effect runs synchronously immediately after Render completes, with its factory observing already-committed refs.</item>
    /// <item>It runs once on mount; on a deps change it runs the previous cleanup then the new setup; equal deps skip it.</item>
    /// <item>Omitting deps re-runs (cleanup then setup) on every render; empty deps stays mount-only.</item>
    /// <item>A render-phase setState re-runs Render() before commit yet the mount-only effect still runs exactly once, and deps that swing to a transient value and back to the committed value do not re-run.</item>
    /// <item>Cleanup runs on unmount; a re-mount re-runs the effect even with unchanged deps because the slot was discarded.</item>
    /// <item>Multiple effects run independently by their own deps, and a deps change runs all cleanups before any setup.</item>
    /// <item>The Func&lt;IDisposable&gt; overload disposes the previous instance before re-executing.</item>
    /// <item>Deps may reference render-closure values that are not state; calling the hook conditionally violates the call-count invariant; calling it outside Render throws.</item>
    /// <item>When Render throws before a later effect call, no partial pending effect runs.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class UseLayoutEffectTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetEffectWithDeps();
            ResetEffectMountOnly();
            ResetRenderPhaseLayoutEffect();
            ResetRenderPhaseDepsOscillation();
            ResetMultiEffect();
            ResetEffectDisposable();
            ResetEffectWithRef();
            ResetEffectSetState();
            ResetEffectRenderClosure();
            ResetConditionalEffect();
            ResetPartialRenderFailure();
            ResetEveryRender();
        }

        #region Deps-omitted overload (factory runs every render)

        [Test]
        public void Given_NoDeps_When_FirstRender_Then_FactoryRunsOnce()
        {
            // Arrange
            var runCount = 0;
            s_everyRenderFactory = () => { runCount++; return null; };

            // Act
            using var mounted = V.Mount(_root, V.Component(EveryRenderEffectRender, key: "every"));

            // Assert
            Assert.That(runCount, Is.EqualTo(1), "The first render runs the deps-omitted layout effect once");
        }

        [Test]
        public void Given_NoDeps_When_Rerendered_Then_FactoryRunsAgain()
        {
            // Arrange
            var runCount = 0;
            s_everyRenderFactory = () => { runCount++; return null; };
            using var mounted = V.Mount(_root, V.Component(EveryRenderEffectRender, key: "every"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the first render ran the factory once");

            // Act
            s_everyRenderSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(2), "A re-render runs the factory again because deps were omitted");
        }

        [Test]
        public void Given_NoDeps_When_Rerendered_Then_PreviousCleanupRunsBeforeReExecution()
        {
            // Arrange
            var cleanupCount = 0;
            s_everyRenderFactory = () => () => cleanupCount++;
            using var mounted = V.Mount(_root, V.Component(EveryRenderEffectRender, key: "every"));
            Assume.That(cleanupCount, Is.EqualTo(0), "Precondition: the first mount has no prior cleanup to run");

            // Act
            s_everyRenderSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "The previous cleanup runs before each re-execution when deps are omitted");
        }

        [Test]
        public void Given_NoDepsVsEmptyDeps_When_Rerendered_Then_OnlyDepsOmittedReRuns()
        {
            // Arrange
            var everyRenderRunCount = 0;
            var mountOnlyRunCount = 0;
            s_everyRenderFactory = () => { everyRenderRunCount++; return null; };
            s_everyRenderMountOnlyFactory = () => { mountOnlyRunCount++; return null; };
            using var mounted = V.Mount(_root, V.Component(EveryRenderVsMountOnlyRender, key: "compare"));
            Assume.That(everyRenderRunCount, Is.EqualTo(1), "Precondition: the deps-omitted effect ran once on mount");
            Assume.That(mountOnlyRunCount, Is.EqualTo(1), "Precondition: the empty-deps effect ran once on mount");

            // Act
            s_everyRenderSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert — the deps-omitted effect re-ran while the empty-deps effect stayed mount-only
            Assert.That(everyRenderRunCount, Is.EqualTo(2),
                "A deps-omitted effect re-runs on every render while an empty-deps effect stays mount-only");
        }

        [Test]
        public void Given_NoDeps_FuncIDisposableOverload_When_Rerendered_Then_DisposesBeforeReExecution()
        {
            // Arrange
            var disposeLog = new List<int>();
            var nextHandle = 0;
            s_everyRenderDisposableFactory = () =>
            {
                var handle = nextHandle++;
                return new ActionDisposable(() => disposeLog.Add(handle));
            };
            using var mounted = V.Mount(_root, V.Component(EveryRenderDisposableRender, key: "every-disposable"));
            Assume.That(disposeLog, Is.Empty, "Precondition: the initial mount has no prior disposable to dispose");

            // Act
            s_everyRenderSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(disposeLog, Is.EqualTo(new[] { 0 }),
                "The Func<IDisposable> deps-omitted overload disposes the previous instance before re-creating");
        }

        #endregion

        #region Effect with deps

        [Test]
        public void Given_Deps_When_Mounted_Then_EffectRunsOnce()
        {
            // Arrange
            var runCount = 0;
            s_effectWithDepsFactory = () => { runCount++; return null; };
            s_effectWithDepsSelector = s => new object[] { s.Count };

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));

            // Assert
            Assert.That(runCount, Is.EqualTo(1), "The effect runs once on mount");
        }

        [Test]
        public void Given_Deps_When_DepsChange_Then_EffectReRuns()
        {
            // Arrange
            var runCount = 0;
            s_effectWithDepsFactory = () => { runCount++; return null; };
            s_effectWithDepsSelector = s => new object[] { s.Count };
            using var mounted = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act
            s_effectWithDepsSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(2), "A deps change re-runs the effect");
        }

        [Test]
        public void Given_Deps_When_DepsUnchanged_Then_EffectIsSkipped()
        {
            // Arrange
            var runCount = 0;
            s_effectWithDepsFactory = () => { runCount++; return null; };
            s_effectWithDepsSelector = s => new object[] { s.Count };
            using var mounted = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act — change only Label, which is not part of the deps selector
            s_effectWithDepsSetState.Invoke(EffectTestState.Initial with { Label = "changed" });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(1), "An unchanged dep skips the effect re-run");
        }

        [Test]
        public void Given_Deps_When_DepsChange_Then_PreviousCleanupRunsBeforeReExecution()
        {
            // Arrange
            var cleanupCount = 0;
            s_effectWithDepsFactory = () => () => cleanupCount++;
            s_effectWithDepsSelector = s => new object[] { s.Count };
            using var mounted = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));
            Assume.That(cleanupCount, Is.EqualTo(0), "Precondition: no cleanup has run on mount");

            // Act
            s_effectWithDepsSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "The previous cleanup runs before re-execution");
        }

        #endregion

        #region Effect with empty deps (mount-only)

        [Test]
        public void Given_EmptyDeps_When_Mounted_Then_EffectRunsOnce()
        {
            // Arrange
            var runCount = 0;
            s_effectMountOnlyFactory = () => { runCount++; return null; };

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectMountOnlyRender, key: "mount"));

            // Assert
            Assert.That(runCount, Is.EqualTo(1), "An empty-deps effect runs once on mount");
        }

        [Test]
        public void Given_EmptyDeps_When_StateChanges_Then_EffectDoesNotReRun()
        {
            // Arrange
            var runCount = 0;
            s_effectMountOnlyFactory = () => { runCount++; return null; };
            using var mounted = V.Mount(_root, V.Component(EffectMountOnlyRender, key: "mount"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act
            s_effectMountOnlySetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();
            s_effectMountOnlySetState.Invoke(EffectTestState.Initial with { Count = 2 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(1), "An empty-deps effect does not re-run on state changes");
        }

        #endregion

        #region Render-phase setState interaction (mount layout effect must survive the re-run)

        [Test]
        public void Given_RenderPhaseSetState_When_Mounted_Then_RendersExactlyTwice()
        {
            // Arrange
            s_renderPhaseEffectFactory = () => null;
            s_renderPhaseTarget = "normalized";

            // Act
            using var mounted = V.Mount(_root, V.Component(RenderPhaseLayoutEffectRender, key: "render-phase"));

            // Assert
            Assert.That(s_renderPhaseRenderCount, Is.EqualTo(2),
                "Render-phase setState re-runs Render() once before commit");
        }

        [Test]
        public void Given_RenderPhaseSetState_When_Mounted_Then_MountOnlyEffectRunsOnce()
        {
            // Arrange
            var runCount = 0;
            s_renderPhaseEffectFactory = () => { runCount++; return null; };
            s_renderPhaseTarget = "normalized";

            // Act
            using var mounted = V.Mount(_root, V.Component(RenderPhaseLayoutEffectRender, key: "render-phase"));
            Assume.That(s_renderPhaseRenderCount, Is.EqualTo(2),
                "Precondition: render-phase setState re-ran Render() once before commit");

            // Assert
            Assert.That(runCount, Is.EqualTo(1),
                "The mount-only layout effect runs exactly once across the render-phase re-run");
        }

        [Test]
        public void Given_DepsSwingToTransientAndBack_When_RenderPhaseReRuns_Then_EffectDoesNotReRun()
        {
            // Arrange — mount commits the dep ["settled"] and runs the layout effect once
            using var mounted = V.Mount(_root, V.Component(RenderPhaseDepsOscillationRender, key: "osc"));
            Assume.That(s_oscSetupCount, Is.EqualTo(1), "Precondition: the layout effect ran once on mount");
            Assume.That(s_oscCleanupCount, Is.EqualTo(0), "Precondition: no cleanup has run on mount");

            // Act — drive a render-phase re-run whose discarded attempt swings the dep to "transient" and whose
            // settled attempt returns it to the committed "settled". The settled deps are compared against the
            // committed render, so they are unchanged and the effect must not re-run.
            s_oscSetPhase(1);
            mounted.FlushStateForTest();
            Assume.That(s_oscRenderCount, Is.EqualTo(3),
                "Precondition: 1 mount render + 2 render-phase attempts (phase 1 -> normalized phase 2)");

            // Assert
            Assert.That(s_oscSetupCount, Is.EqualTo(1),
                "The committed dep is unchanged across the re-run, so the layout effect does not re-run");
        }

        #endregion

        #region Cleanup

        [Test]
        public void Given_MountOnlyEffect_When_Unmounted_Then_CleanupRuns()
        {
            // Arrange
            var cleanupCount = 0;
            s_effectMountOnlyFactory = () => () => cleanupCount++;
            var mounted = V.Mount(_root, V.Component(EffectMountOnlyRender, key: "mount"));
            Assume.That(cleanupCount, Is.EqualTo(0), "Precondition: no cleanup has run while mounted");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "Unmount runs the layout effect cleanup");
        }

        [Test]
        public void Given_EffectWithDeps_When_Unmounted_Then_CleanupRuns()
        {
            // Arrange
            var cleanupCount = 0;
            s_effectWithDepsFactory = () => () => cleanupCount++;
            s_effectWithDepsSelector = s => new object[] { s.Count };
            var mounted = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "Unmount runs the cleanup of a deps-based effect");
        }

        #endregion

        #region Multiple effects

        [Test]
        public void Given_TwoEffects_When_OneDepChanges_Then_OnlyThatEffectReRuns()
        {
            // Arrange
            var countEffectRuns = 0;
            var labelEffectRuns = 0;
            s_multiEffect1Factory = () => { countEffectRuns++; return null; };
            s_multiEffect1Selector = s => new object[] { s.Count };
            s_multiEffect2Factory = () => { labelEffectRuns++; return null; };
            s_multiEffect2Selector = s => new object[] { s.Label };
            using var mounted = V.Mount(_root, V.Component(MultiEffectRender, key: "multi"));
            Assume.That(countEffectRuns, Is.EqualTo(1), "Precondition: the count effect ran once on mount");
            Assume.That(labelEffectRuns, Is.EqualTo(1), "Precondition: the label effect ran once on mount");

            // Act — change only Count
            s_multiEffectSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert — the label effect did not re-run because its dep is unchanged
            Assert.That(labelEffectRuns, Is.EqualTo(1),
                "An effect runs only when its own dep changes, independent of sibling effects");
        }

        [Test]
        public void Given_TwoEffects_When_OneDepChanges_Then_ThatEffectReRuns()
        {
            // Arrange
            var countEffectRuns = 0;
            var labelEffectRuns = 0;
            s_multiEffect1Factory = () => { countEffectRuns++; return null; };
            s_multiEffect1Selector = s => new object[] { s.Count };
            s_multiEffect2Factory = () => { labelEffectRuns++; return null; };
            s_multiEffect2Selector = s => new object[] { s.Label };
            using var mounted = V.Mount(_root, V.Component(MultiEffectRender, key: "multi"));
            Assume.That(countEffectRuns, Is.EqualTo(1), "Precondition: the count effect ran once on mount");

            // Act — change only Count, which is the count effect's dep
            s_multiEffectSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(countEffectRuns, Is.EqualTo(2), "An effect re-runs when its own dep changes");
        }

        [Test]
        public void Given_TwoEffects_When_SharedDepChanges_Then_AllCleanupsRunBeforeAnySetup()
        {
            // Arrange
            var order = new List<string>();
            s_multiEffect1Factory = () => { order.Add("e1"); return () => order.Add("c1"); };
            s_multiEffect1Selector = s => new object[] { s.Count };
            s_multiEffect2Factory = () => { order.Add("e2"); return () => order.Add("c2"); };
            s_multiEffect2Selector = s => new object[] { s.Count };
            using var mounted = V.Mount(_root, V.Component(MultiEffectRender, key: "multi"));
            Assume.That(order, Is.EqualTo(new[] { "e1", "e2" }), "Precondition: both setups ran in order on mount");
            order.Clear();

            // Act
            s_multiEffectSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(order, Is.EqualTo(new[] { "c1", "c2", "e1", "e2" }),
                "A deps change runs all cleanups before any setup, in two passes");
        }

        #endregion

        #region IDisposable overload

        [Test]
        public void Given_DisposableOverload_When_DepsChange_Then_PreviousInstanceIsDisposed()
        {
            // Arrange
            var disposed = false;
            var disposable = new ActionDisposable(() => disposed = true);
            s_effectDisposableFactory = () => disposable;
            s_effectDisposableSelector = s => new object[] { s.Count };
            using var mounted = V.Mount(_root, V.Component(EffectDisposableRender, key: "disposable"));
            Assume.That(disposed, Is.False, "Precondition: nothing is disposed while mounted");

            // Act
            s_effectDisposableSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(disposed, Is.True, "The IDisposable is disposed when deps change");
        }

        #endregion

        #region Effect timing

        [Test]
        public void Given_EffectReadingRef_When_FactoryRuns_Then_RefCurrentIsAlreadyCommitted()
        {
            // Arrange
            VisualElement capturedElement = null;
            s_effectWithRefCallback = ref_ =>
            {
                capturedElement = ref_.Current;
                return null;
            };

            // Act
            using var mounted = V.Mount(_root, V.Component(EffectWithRefRender, key: "withref"));

            // Assert — the refCallback fires during Reconcile, before the layout-effect commit, so Current is committed
            Assert.That(capturedElement, Is.SameAs(_root.Q<Label>()),
                "The layout effect factory observes Ref.Current already committed");
        }

        [Test]
        public void Given_EffectReadingRef_When_Rerendered_Then_RefCurrentTracksCommittedElement()
        {
            // Arrange
            var observedCurrents = new List<VisualElement>();
            s_effectWithRefCallback = ref_ =>
            {
                observedCurrents.Add(ref_.Current);
                return null;
            };
            using var mounted = V.Mount(_root, V.Component(EffectWithRefRender, key: "withref"));
            var initialLabel = _root.Q<Label>();
            Assume.That(observedCurrents, Has.Count.EqualTo(1), "Precondition: the effect ran once on mount");
            Assume.That(observedCurrents[0], Is.SameAs(initialLabel), "Precondition: Current pointed at the mounted Label");

            // Act
            s_effectWithRefSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(observedCurrents[1], Is.SameAs(_root.Q<Label>()),
                "Ref.Current still points to the committed Label on re-render");
        }

        [Test]
        public void Given_EffectWithCleanupReadingRef_When_Unmounted_Then_CleanupSeesValidRef()
        {
            // Arrange
            VisualElement capturedOnCleanup = null;
            s_effectWithRefCallback = ref_ => () => capturedOnCleanup = ref_.Current;
            var mounted = V.Mount(_root, V.Component(EffectWithRefRender, key: "withref"));
            var mountedLabel = _root.Q<Label>();
            Assume.That(capturedOnCleanup, Is.Null, "Precondition: cleanup has not run while mounted");

            // Act
            mounted.Dispose();

            // Assert — cleanup runs before the refCallback's own cleanup detaches the element
            Assert.That(capturedOnCleanup, Is.SameAs(mountedLabel),
                "The layout effect cleanup observes the still-valid Ref.Current");
        }

        [Test]
        public void Given_SetStateInsideEffect_When_Mounted_Then_TheNewStateIsVisibleBeforeTheCallerRegainsControl()
        {
            // Arrange + Act
            using var mounted = V.Mount(_root, V.Component(EffectSetStateRender, key: "setstate"));

            // Assert — a layout-effect setState flushes synchronously before the mount returns
            // (before anything can paint), so the caller never observes the pre-write state.
            Assert.That(s_effectSetStateCurrentCount, Is.EqualTo(1),
                "The in-effect setter commits before the initial mount returns");
        }

        [Test]
        public void Given_SetStateInsideEffect_When_AnotherFlushRuns_Then_NothingFurtherCommits()
        {
            // Arrange — the mount already committed the in-effect write.
            using var mounted = V.Mount(_root, V.Component(EffectSetStateRender, key: "setstate"));
            Assume.That(s_effectSetStateCurrentCount, Is.EqualTo(1), "Precondition: the write committed at mount");

            // Act
            mounted.FlushStateForTest();

            // Assert — the guarded setter converged; later flushes find nothing to do.
            Assert.That(s_effectSetStateCurrentCount, Is.EqualTo(1), "The settled state does not change again");
        }

        [Test]
        public void Given_SetStateInsideEffect_When_Mounted_Then_RendersExactlyTwice()
        {
            // Arrange + Act — the mount runs the initial render, the effect writes, and the
            // synchronous follow-up pass commits the second render before returning.
            using var mounted = V.Mount(_root, V.Component(EffectSetStateRender, key: "setstate"));

            // Assert — initial render + the render triggered by the setter inside the effect
            Assert.That(s_effectSetStateRenderCount, Is.EqualTo(2),
                "A setState inside the effect triggers exactly one additional render");
        }

        #endregion

        #region Position-based hook semantics

        [Test]
        public void Given_RenderClosureDep_When_NonStateValueChanges_Then_EffectReRuns()
        {
            // Arrange
            var runCount = 0;
            object lastDepSeen = null;
            var externalValue = "A";
            s_effectRenderClosureFactory = value =>
            {
                runCount++;
                lastDepSeen = value;
                return null;
            };
            s_effectRenderClosureProvider = () => externalValue;
            using var mounted = V.Mount(_root, V.Component(EffectRenderClosureRender, key: "closure"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");
            Assume.That(lastDepSeen, Is.EqualTo("A"), "Precondition: the first dep value was A");

            // Act — change a render-closure value that is not held in state
            externalValue = "B";
            s_effectRenderClosureSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(2),
                "The effect re-runs when a render-closure dep not contained in state changes");
        }

        [Test]
        public void Given_RenderClosureDep_When_NonStateValueUnchanged_Then_EffectDoesNotReRun()
        {
            // Arrange
            var runCount = 0;
            var externalValue = "A";
            s_effectRenderClosureFactory = _ => { runCount++; return null; };
            s_effectRenderClosureProvider = () => externalValue;
            using var mounted = V.Mount(_root, V.Component(EffectRenderClosureRender, key: "closure"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on mount");

            // Act — re-render without changing the render-closure value
            s_effectRenderClosureSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert
            Assert.That(runCount, Is.EqualTo(1),
                "The effect does not re-run when the render-closure dep is unchanged");
        }

#if UNITY_EDITOR
        [Test]
        public void Given_ConditionalEffectCall_When_CallCountDiffersBetweenRenders_Then_LogsError()
        {
            // Arrange — call-count validation is enabled only under UNITY_EDITOR
            s_conditionalEffectFactory = () => null;
            using var mounted = V.Mount(_root, V.Component(ConditionalEffectRender, key: "conditional"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"UseLayoutEffect call count differs between previous render"));

            // Act
            s_conditionalEffectSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the call-count violation was logged
        }
#endif

        [Test]
        public void Given_CalledOutsideRender_When_Invoked_Then_ThrowsInvalidOperationException()
        {
            // Act + Assert
            Assert.Throws<InvalidOperationException>(() =>
                Hooks.UseLayoutEffect((Func<Action>)(() => null), Array.Empty<object>()));
        }

        [Test]
        public void Given_MountOnlyEffect_When_Remounted_Then_EffectRunsAgain()
        {
            // Arrange
            var runCount = 0;
            var cleanupCount = 0;
            s_effectMountOnlyFactory = () => { runCount++; return () => cleanupCount++; };
            var first = V.Mount(_root, V.Component(EffectMountOnlyRender, key: "mount"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on the first mount");
            first.Dispose();
            Assume.That(cleanupCount, Is.EqualTo(1), "Precondition: cleanup ran on unmount");

            // Act
            using var second = V.Mount(_root, V.Component(EffectMountOnlyRender, key: "mount"));

            // Assert
            Assert.That(runCount, Is.EqualTo(2), "A mount-only effect fires again on re-mount after unmount");
        }

        [Test]
        public void Given_DepsEffect_When_RemountedWithSameDeps_Then_EffectRunsAgain()
        {
            // Arrange
            var runCount = 0;
            s_effectWithDepsFactory = () => { runCount++; return null; };
            s_effectWithDepsSelector = s => new object[] { s.Count };
            var first = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));
            Assume.That(runCount, Is.EqualTo(1), "Precondition: the effect ran once on the first mount");
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(EffectWithDepsRender, key: "deps"));

            // Assert — the discarded slot makes the re-mount an initial run regardless of equal deps
            Assert.That(runCount, Is.EqualTo(2),
                "A re-mount fires the deps effect again as an initial run because the slot was discarded");
        }

        [Test]
        public void Given_RenderThrowsAfterFirstEffect_When_Rerendered_Then_NoPartialPendingEffectRuns()
        {
            // Arrange
            var effect1Runs = 0;
            var shouldThrow = false;
            s_partialRenderFactory = () => { effect1Runs++; return null; };
            s_partialRenderShouldThrow = () => shouldThrow;
            using var mounted = V.Mount(_root, V.Component(PartialRenderFailureRender, key: "partial"));
            Assume.That(effect1Runs, Is.EqualTo(1), "Precondition: the first effect ran once on mount");

            // Act
            shouldThrow = true;
            LogAssert.Expect(LogType.Exception, new Regex("Intentional"));
            s_partialRenderSetState.Invoke(EffectTestState.Initial with { Count = 1 });
            mounted.FlushStateForTest();

            // Assert — Render threw before reaching the effect call, so the pending effect did not run
            Assert.That(effect1Runs, Is.EqualTo(1),
                "When Render throws before a later effect call, the first pending effect does not run either");
        }

        #endregion

        #region EffectWithDeps component (callback + depsSelector)

        private static Func<Action> s_effectWithDepsFactory;
        private static Func<EffectTestState, object[]> s_effectWithDepsSelector;
        private static Action<EffectTestState> s_effectWithDepsSetState;

        private static void ResetEffectWithDeps()
        {
            s_effectWithDepsFactory = null;
            s_effectWithDepsSelector = null;
            s_effectWithDepsSetState = null;
        }

        [Component]
        private static VNode EffectWithDepsRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_effectWithDepsSetState = setState;
            Hooks.UseLayoutEffect(s_effectWithDepsFactory, s_effectWithDepsSelector(state));
            return V.Label(text: state.Label);
        }

        #endregion

        #region EffectMountOnly component (Func<Action> + empty deps)

        private static Func<Action> s_effectMountOnlyFactory;
        private static Action<EffectTestState> s_effectMountOnlySetState;

        private static void ResetEffectMountOnly()
        {
            s_effectMountOnlyFactory = null;
            s_effectMountOnlySetState = null;
        }

        [Component]
        private static VNode EffectMountOnlyRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_effectMountOnlySetState = setState;
            Hooks.UseLayoutEffect(s_effectMountOnlyFactory, Array.Empty<object>());
            return V.Label(text: state.Label);
        }

        #endregion

        #region RenderPhaseLayoutEffect component (render-phase setState + mount-only layout effect)

        private static Func<Action> s_renderPhaseEffectFactory;
        private static string s_renderPhaseTarget;
        private static int s_renderPhaseRenderCount;

        private static void ResetRenderPhaseLayoutEffect()
        {
            s_renderPhaseEffectFactory = null;
            s_renderPhaseTarget = null;
            s_renderPhaseRenderCount = 0;
        }

        [Component]
        private static VNode RenderPhaseLayoutEffectRender()
        {
            s_renderPhaseRenderCount++;
            var (value, setValue) = Hooks.UseState("initial");
            // Render-phase normalization: drive state toward the target once. The setter bails out
            // (Object.is) on the second render, so the render loop settles after one re-run.
            if (value != s_renderPhaseTarget)
            {
                setValue.Invoke(s_renderPhaseTarget);
            }
            Hooks.UseLayoutEffect(s_renderPhaseEffectFactory, Array.Empty<object>());
            return V.Label(text: value);
        }

        #endregion

        #region RenderPhaseDepsOscillation component (deps swing to a transient value and back to committed)

        private static int s_oscSetupCount;
        private static int s_oscCleanupCount;
        private static int s_oscRenderCount;
        private static Action<int> s_oscSetPhase;

        private static void ResetRenderPhaseDepsOscillation()
        {
            s_oscSetupCount = 0;
            s_oscCleanupCount = 0;
            s_oscRenderCount = 0;
            s_oscSetPhase = null;
        }

        [Component]
        private static VNode RenderPhaseDepsOscillationRender()
        {
            s_oscRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_oscSetPhase = setPhase;
            // An odd phase is a transient render-phase attempt that normalizes to the next even phase in one
            // re-run; the dep therefore swings to "transient" on the discarded attempt and back to the
            // committed "settled" on the settled attempt.
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var dep = phase % 2 == 1 ? "transient" : "settled";
            Hooks.UseLayoutEffect(() =>
            {
                s_oscSetupCount++;
                return () => s_oscCleanupCount++;
            }, new object[] { dep });
            return V.Label(text: dep);
        }

        #endregion

        #region MultiEffect component (2 effects, ordering test)

        private static Func<Action> s_multiEffect1Factory;
        private static Func<EffectTestState, object[]> s_multiEffect1Selector;
        private static Func<Action> s_multiEffect2Factory;
        private static Func<EffectTestState, object[]> s_multiEffect2Selector;
        private static Action<EffectTestState> s_multiEffectSetState;

        private static void ResetMultiEffect()
        {
            s_multiEffect1Factory = null;
            s_multiEffect1Selector = null;
            s_multiEffect2Factory = null;
            s_multiEffect2Selector = null;
            s_multiEffectSetState = null;
        }

        [Component]
        private static VNode MultiEffectRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_multiEffectSetState = setState;
            Hooks.UseLayoutEffect(s_multiEffect1Factory, s_multiEffect1Selector(state));
            Hooks.UseLayoutEffect(s_multiEffect2Factory, s_multiEffect2Selector(state));
            return V.Label(text: $"{state.Count}-{state.Label}");
        }

        #endregion

        #region EffectDisposable component (IDisposable overload)

        private static Func<IDisposable> s_effectDisposableFactory;
        private static Func<EffectTestState, object[]> s_effectDisposableSelector;
        private static Action<EffectTestState> s_effectDisposableSetState;

        private static void ResetEffectDisposable()
        {
            s_effectDisposableFactory = null;
            s_effectDisposableSelector = null;
            s_effectDisposableSetState = null;
        }

        [Component]
        private static VNode EffectDisposableRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_effectDisposableSetState = setState;
            Hooks.UseLayoutEffect(s_effectDisposableFactory, s_effectDisposableSelector(state));
            return V.Label(text: state.Label);
        }

        #endregion

        #region EffectWithRef component (effect via Ref<Label>)

        private static Func<Ref<Label>, Action> s_effectWithRefCallback;
        private static Action<EffectTestState> s_effectWithRefSetState;
        private static readonly Ref<Label> s_effectWithRefLabelRef = new();

        private static void ResetEffectWithRef()
        {
            s_effectWithRefCallback = null;
            s_effectWithRefSetState = null;
            s_effectWithRefLabelRef.Set(null);
        }

        [Component]
        private static VNode EffectWithRefRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_effectWithRefSetState = setState;
            Hooks.UseLayoutEffect(() => s_effectWithRefCallback(s_effectWithRefLabelRef), new object[] { state.Count });
            return V.Label(text: state.Label, refCallback: s_effectWithRefLabelRef.SetElement);
        }

        #endregion

        #region EffectSetState component (re-render by calling the setter inside an effect)

        private static int s_effectSetStateCurrentCount;
        private static int s_effectSetStateRenderCount;

        private static void ResetEffectSetState()
        {
            s_effectSetStateCurrentCount = 0;
            s_effectSetStateRenderCount = 0;
        }

        [Component]
        private static VNode EffectSetStateRender()
        {
            s_effectSetStateRenderCount++;
            var (count, setCount) = Hooks.UseState(0);
            s_effectSetStateCurrentCount = count;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (count == 0)
                {
                    setCount.Invoke(1);
                }
                return null;
            }), new object[] { count });
            return V.Label(text: count.ToString());
        }

        #endregion

        #region EffectRenderClosure component (deps are render-closure values)

        private static Func<object, Action> s_effectRenderClosureFactory;
        private static Func<object> s_effectRenderClosureProvider;
        private static Action<EffectTestState> s_effectRenderClosureSetState;

        private static void ResetEffectRenderClosure()
        {
            s_effectRenderClosureFactory = null;
            s_effectRenderClosureProvider = null;
            s_effectRenderClosureSetState = null;
        }

        [Component]
        private static VNode EffectRenderClosureRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_effectRenderClosureSetState = setState;
            var currentValue = s_effectRenderClosureProvider();
            Hooks.UseLayoutEffect(() => s_effectRenderClosureFactory(currentValue), new object[] { currentValue });
            return V.Label(text: state.Label);
        }

        #endregion

        #region ConditionalEffect component (conditional UseLayoutEffect call inside Render)

        private static Func<Action> s_conditionalEffectFactory;
        private static Action<EffectTestState> s_conditionalEffectSetState;

        private static void ResetConditionalEffect()
        {
            s_conditionalEffectFactory = null;
            s_conditionalEffectSetState = null;
        }

        [Component]
        private static VNode ConditionalEffectRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_conditionalEffectSetState = setState;
            if (state.Count > 0)
            {
                Hooks.UseLayoutEffect(s_conditionalEffectFactory, new object[] { state.Count });
            }
            return V.Label(text: state.Label);
        }

        #endregion

        #region PartialRenderFailure component (Render throws after an effect)

        private static Func<Action> s_partialRenderFactory;
        private static Func<bool> s_partialRenderShouldThrow;
        private static Action<EffectTestState> s_partialRenderSetState;

        private static void ResetPartialRenderFailure()
        {
            s_partialRenderFactory = null;
            s_partialRenderShouldThrow = null;
            s_partialRenderSetState = null;
        }

        [Component]
        private static VNode PartialRenderFailureRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_partialRenderSetState = setState;
            Hooks.UseLayoutEffect(s_partialRenderFactory, new object[] { state.Count });
            if (s_partialRenderShouldThrow())
            {
                throw new InvalidOperationException("Intentional render failure");
            }
            return V.Label(text: state.Label);
        }

        #endregion

        #region EveryRenderEffect components (deps-omitted overload)

        private static Func<Action> s_everyRenderFactory;
        private static Func<Action> s_everyRenderMountOnlyFactory;
        private static Func<IDisposable> s_everyRenderDisposableFactory;
        private static Action<EffectTestState> s_everyRenderSetState;

        private static void ResetEveryRender()
        {
            s_everyRenderFactory = null;
            s_everyRenderMountOnlyFactory = null;
            s_everyRenderDisposableFactory = null;
            s_everyRenderSetState = null;
        }

        [Component]
        private static VNode EveryRenderEffectRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_everyRenderSetState = setState;
            Hooks.UseLayoutEffect(s_everyRenderFactory);
            return V.Label(text: state.Label);
        }

        [Component]
        private static VNode EveryRenderVsMountOnlyRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_everyRenderSetState = setState;
            Hooks.UseLayoutEffect(s_everyRenderFactory);
            Hooks.UseLayoutEffect(s_everyRenderMountOnlyFactory, Array.Empty<object>());
            return V.Label(text: state.Label);
        }

        [Component]
        private static VNode EveryRenderDisposableRender()
        {
            var (state, setState) = Hooks.UseState(EffectTestState.Initial);
            s_everyRenderSetState = setState;
            Hooks.UseLayoutEffect(s_everyRenderDisposableFactory);
            return V.Label(text: state.Label);
        }

        #endregion
    }
}
