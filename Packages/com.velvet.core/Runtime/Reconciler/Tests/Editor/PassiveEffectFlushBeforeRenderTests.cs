using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins React's flush of pending passive effects before a render starts: a prior commit's pending effect, or a
    /// presence's exit completion, runs before the next render rather than after it, whichever of the scheduler's
    /// renders that is — the immediate tier's scheduled drain, the Transition tier's drain, the follow-up pass a
    /// layout effect's write asks for, or the resumed slice of a parked time-sliced pass — and a discrete event
    /// still runs it when it renders nothing. The EditMode scheduler never ticks, so what a commit stages stays
    /// pending until one of those runs it.
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

        // Committed through a drain rather than FlushStateForTest, which leaves the fiber queued, so the
        // immediate tier is empty afterwards and no stale entry flushes effect:d1 ahead of what a case's Act does.
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
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:i{input}:m{measured}");
                return (Action)null;
            }, new object[] { input, measured });
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
            Assert.That(s_log, Is.EqualTo(new[] { "render:i1:m0", "effect:i1:m0", "render:i1:m1" }));
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
            Hooks.UseEffect(() =>
            {
                s_log.Add($"effect:m{measured}");
                return (Action)null;
            }, new object[] { measured });
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
            Assert.That(s_log, Is.EqualTo(new[] { "render:m-1", "effect:m-1", "render:m1" }));
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
                V.Div(key: "late", children: new VNode[] { V.Component(LateRowRender, key: "late-row") }),
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
    }
}
