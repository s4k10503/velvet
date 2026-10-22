using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when React runs pending passive effects, against React v19.1.0's work loop:
    /// <list type="bullet">
    /// <item>before a render starts, so a prior commit's pending effect, or a presence's exit completion, runs
    /// before the next render — the immediate tier's scheduled drain, the Transition tier's drain, the follow-up
    /// pass a layout effect's write asks for, a resumed slice of a parked time-sliced pass and a VirtualList range
    /// rendered outside a pass — and a discrete event runs it when it renders nothing;</item>
    /// <item>as a sync-lane commit ends — a click's, a commit-phase write's on a drain, a mount or a resumed pass,
    /// a store change's — while a default-lane drain leaves its effects pending, and an update a flushed effect
    /// schedules is a default-lane one;</item>
    /// <item>ahead of a transition, an update one of them schedules rendering before the transition does, whether
    /// the Transition tier's drain or a parked pass's resume flushed it, and a VirtualList range leaving the update
    /// its flush scheduled to the scheduler;</item>
    /// <item>never from inside a passive drain already running, so what an effect's own render stages waits for
    /// that drain to return.</item>
    /// </list>
    /// The EditMode scheduler never ticks, so what a commit stages stays pending until one of those runs it.
    /// </summary>
    [TestFixture]
    internal sealed class PassiveEffectFlushBeforeRenderTests
    {
        private VisualElement _root;
        private MountedTree _mounted;
        private static readonly List<string> s_log = new();
        private static StateUpdater<int> s_setDep;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static StateUpdater<int> s_setTick;
        private static ComponentFiber s_counterFiber;

        // A dep-keyed effect, a tick setter reachable outside a click, and a button whose click sets nothing.
        [Component(Compiler = false)]
        private static VNode Counter()
        {
            var (dep, setDep) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            s_setDep = setDep;
            s_setTick = setTick;
            s_counterFiber = FiberAmbientStack.Current;
            s_log.Add($"render:t{tick}:d{dep}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                return (Action)null;
            }, new object[] { dep });
            return V.Button(name: "idle", onClick: () => { });
        }

        // Committed through a drain, so the immediate tier is empty afterwards and no stale entry flushes effect:d1
        // ahead of what a case's Act does.
        private void MountCounterWithAPendingEffect()
        {
            _mounted = V.Mount(_root, V.Component(Counter, key: "c"));
            s_setDep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();
        }

        [Test]
        public void Given_APendingPassiveEffect_When_TheImmediateTiersScheduledDrainReRenders_Then_TheEffectRunsBeforeTheRender()
        {
            // Arrange
            MountCounterWithAPendingEffect();
            s_setTick.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().RunImmediateCallbackForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:t1:d1" }));
        }

        [Test]
        public void Given_APendingPassiveEffect_When_TheTransitionTiersDrainReRenders_Then_TheEffectRunsBeforeTheRender()
        {
            // Arrange
            MountCounterWithAPendingEffect();
            s_counterFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            _mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:t0:d1" }));
        }

        // GREEN_ON_BASE(characterization): the base already runs pending passive effects when a click's handler returns.
        // A drain now flushes before its render as well, so this is the case that pins the boundary's own flush: drop
        // `batchScheduler?.FlushPendingPassiveEffects();` from FiberDiscreteEventScope and this reddens, while
        // PassiveEffectFlushBeforeUpdateTests' click case, whose click renders, stays green.
        [Test]
        public void Given_APendingPassiveEffect_When_AClickSchedulesNoUpdate_Then_TheEffectRuns()
        {
            // Arrange
            MountCounterWithAPendingEffect();

            // Act
            _root.Q<Button>("idle").SimulateClick();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1" }));
        }

        private static StateUpdater<int> s_setMeasuredInput;

        // The measure-in-layout-effect shape: a render with a new input writes the measurement from its layout
        // effect, which the drain commits in a follow-up pass before it returns.
        [Component(Compiler = false)]
        private static VNode Measured()
        {
            var (input, setInput) = Hooks.UseState(0);
            var (measured, setMeasured) = Hooks.UseState(0);
            s_setMeasuredInput = setInput;
            s_log.Add($"render:i{input}:m{measured}");
            Hooks.UseLayoutEffect(() =>
            {
                setMeasured.Invoke(input);
                return (Action)null;
            }, new object[] { input });
            // Keyed on the input alone, so the follow-up pass stages no effect of its own for its commit's end.
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:i{input}");
                return (Action)null;
            }, new object[] { input });
            return V.Label(text: "measured");
        }

        [Test]
        public void Given_ALayoutEffectWritingStateInADrainsFirstPass_When_TheFollowUpPassRenders_Then_TheFirstPassesEffectRunsBeforeIt()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Measured, key: "m"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_setMeasuredInput.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:i1:m0", "effect:i1", "render:i1:m1" }));
        }

        // Measured's write, made by the mount's layout effect, which the mount commits in a follow-up pass before
        // it returns.
        [Component(Compiler = false)]
        private static VNode MeasuredOnMount()
        {
            var (measured, setMeasured) = Hooks.UseState(-1);
            s_log.Add($"render:m{measured}");
            Hooks.UseLayoutEffect(() =>
            {
                setMeasured.Invoke(1);
                return (Action)null;
            }, Array.Empty<object>());
            // A mount effect only, so the follow-up pass stages no effect of its own for its commit's end.
            Hooks.UseEffect(() =>
            {
                s_log.Add("effect:mount");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "measured");
        }

        [Test]
        public void Given_ALayoutEffectWritingStateOnMount_When_TheMountsFollowUpPassRenders_Then_TheMountsEffectRunsBeforeIt()
        {
            // Arrange
            var tree = V.Component(MeasuredOnMount, key: "m");

            // Act
            _mounted = V.Mount(_root, tree);

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:m-1", "effect:mount", "render:m1" }));
        }

        private static StateUpdater<int> s_setPresenceTick;

        // Tick 1 removes the presence's only child, a Label with nothing to exit, so the removal completes from the
        // passive-effect flush; tick 2 adds the key back.
        [Component(Compiler = false)]
        private static VNode QuietPresenceHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setPresenceTick = setTick;
            s_log.Add($"render:t{tick}");
            return V.Div(children: new VNode[]
            {
                V.AnimatePresence(key: "p", onExitComplete: () => s_log.Add("exit"),
                    children: tick == 1 ? Array.Empty<VNode>() : new VNode[] { V.Label(text: "a", key: "a") }),
            });
        }

        [Test]
        public void Given_AQuietExitWaitingForThePassiveFlush_When_ARenderAddsTheKeyBack_Then_OnExitCompleteRunsBeforeThatRender()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(QuietPresenceHost, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setPresenceTick.Invoke(1);
            _mounted.FlushStateForTest();
            s_log.Clear();
            s_setPresenceTick.Invoke(2);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "exit", "render:t2" }));
        }

        private static bool s_resumeRowsShown;
        private static Func<VNode> s_lateRow = LateRowRender;
        private static ComponentFiber s_resumeListFiber;
        private static StateUpdater<int> s_setSiblingDep;

        // Keyed Divs at the top level, so the list's own reconcile takes the time-sliceable keyed path, with a first
        // row whose render outlasts a Transition slice's budget, so the drain's slice parks after it, and a last row
        // that a resumed slice renders.
        [Component(Compiler = false)]
        private static VNode ResumeListRender()
        {
            s_resumeListFiber = FiberAmbientStack.Current;
            if (!s_resumeRowsShown) return V.Fragment(children: new VNode[] { V.Label(text: "empty", key: "empty") });
            return V.Fragment(children: new VNode[]
            {
                V.Div(key: "slow", children: new VNode[] { V.Component(SlowRowRender, key: "row") }),
                V.Div(key: "row1", children: new VNode[] { V.Label(text: "row-1") }),
                V.Div(key: "row2", children: new VNode[] { V.Label(text: "row-2") }),
                V.Div(key: "late", children: new VNode[] { V.Component(s_lateRow, key: "late-row") }),
            });
        }

        [Component(Compiler = false)]
        private static VNode SlowRowRender()
        {
            System.Threading.Thread.Sleep(25);
            return V.Label(text: "slow");
        }

        [Component(Compiler = false)]
        private static VNode LateRowRender()
        {
            s_log.Add("render:late");
            return V.Label(text: "late");
        }

        [Component(Compiler = false)]
        private static VNode EffectSiblingRender()
        {
            var (dep, setDep) = Hooks.UseState(0);
            s_setSiblingDep = setDep;
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                return (Action)null;
            }, new object[] { dep });
            return V.Label(text: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode ResumeHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(ResumeListRender, key: "list"),
                V.Component(EffectSiblingRender, key: "sibling"),
            });

        [Test]
        public void Given_APassiveEffectCommittedWhileATransitionPassIsParked_When_ThePassResumes_Then_TheEffectRunsBeforeTheResumedSliceRenders()
        {
            // Arrange — the sibling commits on its own while the list's pass is parked
            s_resumeRowsShown = false;
            s_lateRow = LateRowRender;
            _mounted = V.Mount(_root, V.Component(ResumeHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_resumeRowsShown = true;
            s_resumeListFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            s_setSiblingDep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            s_resumeListFiber.DrainTimeSlicedReconcileForTest();

            // Assert — a pass that had not parked rendered the late row before the log was cleared
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:late" }));
        }

        // --- The end of a sync-lane commit ---

        // GREEN_ON_BASE(characterization): the base leaves a scheduled drain's effects to the next frame as well.
        // What this pins is that only a sync-lane commit flushes its effects as it ends, as React flushes them for
        // a sync lane alone: make every drain flush at its end and this reddens.
        [Test]
        public void Given_AnUpdateTheScheduledDrainRendersInOnePass_When_TheDrainEnds_Then_ItsEffectIsStillPending()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(Counter, key: "c"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_setDep.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().RunImmediateCallbackForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:t0:d1" }));
        }

        [Component(Compiler = false)]
        private static VNode ClickBumpedRender()
        {
            var (dep, setDep) = Hooks.UseState(0);
            s_log.Add($"render:d{dep}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                return (Action)null;
            }, new object[] { dep });
            return V.Button(name: "bump", onClick: () => setDep.Invoke(d => d + 1));
        }

        [Test]
        public void Given_AClickThatChangesAnEffectsDep_When_ItsCommitEnds_Then_TheEffectHasRun()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ClickBumpedRender, key: "c"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();

            // Act
            _root.Q<Button>("bump").SimulateClick();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:d1", "effect:d1" }));
        }

        // GREEN_ON_BASE(characterization): the base's discrete flush stands down while a reconcile is on the stack too.
        // What this pins is that neither the click's flush nor the passive flush that ends its commit runs past it.
        [Test]
        public void Given_AReconcileOnTheStack_When_AClickSchedulesAnUpdate_Then_NothingRendersOrRuns()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(ClickBumpedRender, key: "c"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            _mounted.GetSchedulerForTest().SetReconcileActiveProbe(() => true);

            // Act
            _root.Q<Button>("bump").SimulateClick();

            // Assert
            Assert.That(s_log, Is.Empty);
        }

        private static StateUpdater<int> s_setFollowUpInput;

        // The measured value is what the effect is keyed on, so only the follow-up pass's commit stages it.
        [Component(Compiler = false)]
        private static VNode FollowUpEffectRender()
        {
            var (input, setInput) = Hooks.UseState(0);
            var (measured, setMeasured) = Hooks.UseState(0);
            s_setFollowUpInput = setInput;
            s_log.Add($"render:i{input}:m{measured}");
            Hooks.UseLayoutEffect(() =>
            {
                setMeasured.Invoke(input);
                return (Action)null;
            }, new object[] { input });
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:m{measured}");
                return (Action)null;
            }, new object[] { measured });
            return V.Label(text: "follow-up");
        }

        [Test]
        public void Given_ALayoutEffectWritingStateInADrain_When_TheFollowUpPassCommits_Then_ItsEffectRunsBeforeTheDrainReturns()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(FollowUpEffectRender, key: "f"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_setFollowUpInput.Invoke(1);

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:i1:m0", "render:i1:m1", "effect:m1" }));
        }

        [Component(Compiler = false)]
        private static VNode MountFollowUpEffectRender()
        {
            var (measured, setMeasured) = Hooks.UseState(-1);
            s_log.Add($"render:m{measured}");
            Hooks.UseLayoutEffect(() =>
            {
                setMeasured.Invoke(1);
                return (Action)null;
            }, Array.Empty<object>());
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:m{measured}");
                return (Action)null;
            }, new object[] { measured });
            return V.Label(text: "mount-follow-up");
        }

        // The last entry alone, since the mount's own effect is the pre-render flush's, which the case above pins.
        [Test]
        public void Given_ALayoutEffectWritingStateOnMount_When_TheFollowUpPassCommits_Then_ItsEffectRunsBeforeTheMountReturns()
        {
            // Arrange
            var tree = V.Component(MountFollowUpEffectRender, key: "m");

            // Act
            _mounted = V.Mount(_root, tree);

            // Assert
            Assert.That(s_log[s_log.Count - 1], Is.EqualTo("effect:m1"));
        }

        private static bool s_writeOnNextCommit;
        private static ComponentFiber s_transitionWriterFiber;

        // Writes from the first commit after s_writeOnNextCommit is set, whichever lane rendered it.
        [Component(Compiler = false)]
        private static VNode TransitionWriterRender()
        {
            var (measured, setMeasured) = Hooks.UseState(0);
            s_transitionWriterFiber = FiberAmbientStack.Current;
            s_log.Add($"render:m{measured}");
            Hooks.UseLayoutEffect(() =>
            {
                if (s_writeOnNextCommit)
                {
                    s_writeOnNextCommit = false;
                    setMeasured.Invoke(1);
                }
                return (Action)null;
            });
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:m{measured}");
                return (Action)null;
            }, new object[] { measured });
            return V.Label(text: "transition-writer");
        }

        [Test]
        public void Given_ATransitionCommitWhoseLayoutEffectWritesState_When_TheDrainCommitsTheWrite_Then_ItsEffectRunsBeforeTheDrainReturns()
        {
            // Arrange
            s_writeOnNextCommit = false;
            _mounted = V.Mount(_root, V.Component(TransitionWriterRender, key: "w"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_writeOnNextCommit = true;
            s_transitionWriterFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            _mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:m0", "render:m1", "effect:m1" }));
        }

        [Component(Compiler = false)]
        private static VNode LateWritingRowRender()
        {
            var (written, setWritten) = Hooks.UseState(0);
            s_log.Add($"render:late:{written}");
            Hooks.UseLayoutEffect(() =>
            {
                setWritten.Invoke(1);
                return (Action)null;
            }, Array.Empty<object>());
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:late:{written}");
                return (Action)null;
            }, new object[] { written });
            return V.Label(text: "late");
        }

        // The last entry alone, since the row's mount effect is the pre-render flush's.
        [Test]
        public void Given_AParkedPassWhoseLastSliceMountsARowWritingStateFromItsLayoutEffect_When_ThePassCompletes_Then_TheWritesEffectRunsBeforeTheResumeReturns()
        {
            // Arrange
            s_resumeRowsShown = false;
            s_lateRow = LateWritingRowRender;
            _mounted = V.Mount(_root, V.Component(ResumeHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_resumeRowsShown = true;
            s_resumeListFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            s_log.Clear();

            // Act
            s_resumeListFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(s_log[s_log.Count - 1], Is.EqualTo("effect:late:1"));
        }

        private sealed class ExternalCounter
        {
            private Action _listeners;
            private int _value;

            public Action Subscribe(Action onStoreChange)
            {
                _listeners += onStoreChange;
                return () => _listeners -= onStoreChange;
            }

            public int GetSnapshot() => _value;

            public void Set(int value)
            {
                _value = value;
                _listeners?.Invoke();
            }
        }

        private static ExternalCounter s_externalCounter;

        [Component(Compiler = false)]
        private static VNode ExternalReaderRender()
        {
            var value = Hooks.UseSyncExternalStore(s_externalCounter.Subscribe, s_externalCounter.GetSnapshot);
            s_log.Add($"render:v{value}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:v{value}");
                return (Action)null;
            }, new object[] { value });
            return V.Label(text: "external");
        }

        [Test]
        public void Given_AStoreChangeTheMainThreadRenders_When_ItsCommitEnds_Then_TheReadersEffectHasRun()
        {
            // Arrange
            s_externalCounter = new ExternalCounter();
            _mounted = V.Mount(_root, V.Component(ExternalReaderRender, key: "r"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_externalCounter.Set(5);

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:v5", "effect:v5" }));
        }

        [Component(Compiler = false)]
        private static VNode ReaderBesideFollowerHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(ExternalReaderRender, key: "reader"),
                V.Component(StoreFollowerRender, key: "follower"),
            });

        [Test]
        public void Given_AStoreChangeAndThenAnOrdinaryUpdate_When_TheMainThreadRendersBoth_Then_TheReadersEffectHasRun()
        {
            // Arrange — the ordinary update joins a tier the store change already holds
            s_externalCounter = new ExternalCounter();
            _mounted = V.Mount(_root, V.Component(ReaderBesideFollowerHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_log.Clear();
            s_externalCounter.Set(5);
            s_setStoreFollower.Invoke(1);

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(s_log, Does.Contain("effect:v5"));
        }

        // --- Work a passive flush schedules ahead of a transition ---

        private static StateUpdater<int> s_setWriterDep;
        private static StateUpdater<int> s_setOtherValue;
        private static ComponentFiber s_transitionTargetFiber;

        [Component(Compiler = false)]
        private static VNode EffectWriterRender()
        {
            var (dep, setDep) = Hooks.UseState(0);
            s_setWriterDep = setDep;
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:d{dep}");
                if (dep > 0) s_setOtherValue.Invoke(dep);
                return (Action)null;
            }, new object[] { dep });
            return V.Label(text: "writer");
        }

        [Component(Compiler = false)]
        private static VNode OtherRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setOtherValue = setValue;
            s_log.Add($"render:other:{value}");
            return V.Label(text: "other");
        }

        [Component(Compiler = false)]
        private static VNode TransitionTargetRender()
        {
            s_transitionTargetFiber = FiberAmbientStack.Current;
            s_log.Add("render:transition");
            return V.Label(text: "target");
        }

        [Component(Compiler = false)]
        private static VNode WriterOtherTargetHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(EffectWriterRender, key: "writer"),
                V.Component(OtherRender, key: "other"),
                V.Component(TransitionTargetRender, key: "target"),
            });

        [Test]
        public void Given_APendingEffectThatSetsState_When_TheTransitionTierDrains_Then_ThatUpdateRendersBeforeTheTransition()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(WriterOtherTargetHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setWriterDep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();
            s_transitionTargetFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            _mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:other:1", "render:transition" }));
        }

        // Far above FiberBatchScheduler.NestedUpdateLimit, and finite, so a transition tier that no bound stopped
        // still returns, having rendered every tick first.
        private const int RunawayTicks = 200;

        // Sets state from every commit's effect up to RunawayTicks, so each flush the transition tier makes
        // schedules another render until then.
        [Component(Compiler = false)]
        private static VNode RunawayRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_log.Add("render:runaway");
            Hooks.UseEffect(() =>
            {
                if (tick < RunawayTicks) setTick.Invoke(tick + 1);
                return (Action)null;
            }, new object[] { tick });
            return V.Label(text: "runaway");
        }

        [Component(Compiler = false)]
        private static VNode RunawayAndTargetHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(RunawayRender, key: "runaway"),
                V.Component(TransitionTargetRender, key: "target"),
            });

        // GREEN_ON_BASE(characterization): the base renders the transition without flushing anything ahead of it.
        // What this pins is that an effect setting state on every commit holds the transition back for a bounded
        // number of renders, not for as long as it keeps setting state.
        [Test]
        public void Given_AnEffectThatSetsStateOnEveryCommit_When_TheTransitionTierDrains_Then_TheTransitionRendersBeforeTheEffectStops()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(RunawayAndTargetHostRender, key: "host"));
            s_log.Clear();
            s_transitionTargetFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            _mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the renders the runaway made before the transition rendered
            Assert.That(s_log.TakeWhile(entry => entry != "render:transition").Count(), Is.LessThan(RunawayTicks));
        }

        // --- A flush reached from inside a running passive drain ---

        private static StateUpdater<int> s_setNestedTarget;
        private static MountedTree s_nestedMounted;
        private static VisualElement s_nestedRoot;

        [Component(Compiler = false)]
        private static VNode NestedTargetRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setNestedTarget = setValue;
            Hooks.UseEffect(() =>
            {
                s_log.Add($"target:v{value}");
                return (Action)null;
            }, new object[] { value });
            return V.Button(name: "nested-idle", onClick: () => { });
        }

        // Renders the target from inside its own mount effect, then clicks a button whose handler sets nothing,
        // whose discrete boundary is a passive flush of its own.
        [Component(Compiler = false)]
        private static VNode NestedFlusherRender()
        {
            Hooks.UseEffect(() =>
            {
                s_log.Add("flusher:start");
                s_setNestedTarget.Invoke(1);
                s_nestedMounted.GetSchedulerForTest().FlushImmediate();
                s_nestedRoot.Q<Button>("nested-idle").SimulateClick();
                s_log.Add("flusher:end");
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "flusher");
        }

        [Component(Compiler = false)]
        private static VNode NestedHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(NestedTargetRender, key: "target"),
                V.Component(NestedFlusherRender, key: "flusher"),
            });

        [Test]
        public void Given_AnEffectThatRendersAndClicksWhileThePassiveFlushRunsIt_When_TheFlushRuns_Then_TheEffectsThatRenderStagedWaitForItToReturn()
        {
            // Arrange
            s_nestedRoot = _root;
            _mounted = V.Mount(_root, V.Component(NestedHostRender, key: "host"));
            s_nestedMounted = _mounted;

            // Act
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "target:v0", "flusher:start", "flusher:end", "target:v1" }));
        }

        // --- A VirtualList range rendered outside a pass ---

        [Component(Compiler = false)]
        private static VNode VirtualRowRender(string id)
        {
            s_log.Add("render:row-" + id);
            return V.Label(text: id);
        }

        [Component(Compiler = false)]
        private static VNode ListBesideAnEffectRender()
            => V.Div(children: new VNode[]
            {
                V.Component(EffectSiblingRender, key: "sibling"),
                V.VirtualList(items: new[] { "a", "b", "c", "d", "e", "f" }, keySelector: item => item,
                    itemHeight: 50f, renderer: item => V.Component<string>(VirtualRowRender, item, key: item),
                    overscan: 0, key: "list"),
            });

        [Test]
        public void Given_APendingPassiveEffect_When_AVirtualListRendersARangeOutsideAPass_Then_TheEffectRunsBeforeTheRows()
        {
            // Arrange — the sibling's mount effect is pending, and no layout pass has given the list a range
            _mounted = V.Mount(_root, V.Component(ListBesideAnEffectRender, key: "host"));
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];
            s_log.Clear();

            // Act — what a scroll to the top of a 100-unit viewport renders
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);

            // Assert
            Assert.That(s_log.FirstOrDefault(), Is.EqualTo("effect:d0"));
        }

        // --- Updates a flush schedules are ordinary ones ---

        private static StateUpdater<int> s_setStoreFollower;

        [Component(Compiler = false)]
        private static VNode StoreFollowerRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setStoreFollower = setValue;
            s_log.Add($"render:follower:{value}");
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:follower:{value}");
                return (Action)null;
            }, new object[] { value });
            return V.Label(text: "follower");
        }

        // Hands each snapshot it commits to the follower from its effect.
        [Component(Compiler = false)]
        private static VNode HandingExternalReaderRender()
        {
            var value = Hooks.UseSyncExternalStore(s_externalCounter.Subscribe, s_externalCounter.GetSnapshot);
            s_log.Add($"render:v{value}");
            Hooks.UseEffect(() =>
            {
                if (value > 0) s_setStoreFollower.Invoke(value);
                return (Action)null;
            }, new object[] { value });
            return V.Label(text: "handing");
        }

        [Component(Compiler = false)]
        private static VNode ReaderAndFollowerHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(HandingExternalReaderRender, key: "reader"),
                V.Component(StoreFollowerRender, key: "follower"),
            });

        // The store change's render flushes the reader's effect as its commit ends, which hands the follower 5.
        private void MountReaderAndRenderAStoreChange()
        {
            s_externalCounter = new ExternalCounter();
            _mounted = V.Mount(_root, V.Component(ReaderAndFollowerHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_externalCounter.Set(5);
            VelvetMainThread.RunHandoffs();
            s_log.Clear();
        }

        [Test]
        public void Given_AnUpdateAStoreRendersEffectScheduled_When_AnOrdinaryDrainRendersIt_Then_ItsEffectIsStillPending()
        {
            // Arrange
            MountReaderAndRenderAStoreChange();

            // Act
            _mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "render:follower:5" }));
        }

        // GREEN_ON_BASE(characterization): the base posts a flush for every store change that finds none owed.
        // What this pins is that an update the store render's own effects queued does not leave the owed flag set,
        // which kept the next change from being posted.
        [Test]
        public void Given_AnUpdateAStoreRendersEffectScheduled_When_TheStoreChangesAgain_Then_ThePostedWorkRendersTheReader()
        {
            // Arrange
            MountReaderAndRenderAStoreChange();
            s_externalCounter.Set(6);

            // Act
            VelvetMainThread.RunHandoffs();

            // Assert
            Assert.That(s_log, Does.Contain("render:v6"));
        }

        [Component(Compiler = false)]
        private static VNode ResumeListWriterOtherHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(ResumeListRender, key: "list"),
                V.Component(EffectWriterRender, key: "writer"),
                V.Component(OtherRender, key: "other"),
            });

        [Test]
        public void Given_AnEffectPendingAsAParkedPassResumes_When_ItSchedulesAnUpdate_Then_TheUpdateRendersBeforeTheResumedSlice()
        {
            // Arrange — the writer's effect is staged after the pass parks
            s_resumeRowsShown = false;
            s_lateRow = LateRowRender;
            _mounted = V.Mount(_root, V.Component(ResumeListWriterOtherHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_resumeRowsShown = true;
            s_resumeListFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _mounted.GetSchedulerForTest().DrainDelayedForTest();
            s_setWriterDep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_log.Clear();

            // Act
            s_resumeListFiber.DrainTimeSlicedReconcileForTest();

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:other:1", "render:late" }));
        }

        [Component(Compiler = false)]
        private static VNode WriterOtherListHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(EffectWriterRender, key: "writer"),
                V.Component(OtherRender, key: "other"),
                V.VirtualList(items: new[] { "a", "b", "c", "d", "e", "f" }, keySelector: item => item,
                    itemHeight: 50f, renderer: item => V.Component<string>(VirtualRowRender, item, key: item),
                    overscan: 0, key: "list"),
            });

        [Test]
        public void Given_AnEffectARangeRendersFlushSchedulesAnUpdate_When_TheRangesRowsMount_Then_TheUpdateIsLeftToTheScheduler()
        {
            // Arrange
            _mounted = V.Mount(_root, V.Component(WriterOtherListHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            s_setWriterDep.Invoke(1);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
            var controller = _mounted.Root.Reconciler.Context.VirtualListControllers[_root.Q<ScrollView>()];
            s_log.Clear();

            // Act — what a scroll to the top of a 100-unit viewport renders
            controller.UpdateVisibleRange(scrollY: 0f, viewportHeight: 100f);

            // Assert
            Assert.That(s_log, Is.EqualTo(new[] { "effect:d1", "render:row-a", "render:row-b" }));
        }
    }
}
