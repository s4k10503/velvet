using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what an exception leaving an immediate-tier drain leaves behind: the tier still registers a
    /// frame-boundary drain for a later update, and for an update queued before the exception left. Once a bounded
    /// run of drains in a row has thrown, what is still queued is dropped with an error instead, and nothing is
    /// reported where nothing is queued; a drain that completes starts that run again, and a dropped component's
    /// next update commits. A Transition-tier callback whose immediate drain throws commits the transition it served in a
    /// later pass, and leaves one waiting on another admission where it was.
    /// </summary>
    /// <remarks>
    /// The exception is thrown by an imperative-handle factory in the drain's layout commit, which nothing between
    /// it and the drain catches. Each assertion on the scheduler folds in that the drains did throw, so a change
    /// that stops the exception leaving reddens those cases rather than passing them with nothing measured. The
    /// case reading the log for an absent drop cannot fold that in, and passes vacuously under such a change.
    /// </remarks>
    [TestFixture]
    internal sealed class DrainExceptionRecoveryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = default;
            s_setOther = default;
            s_setTransitioned = default;
            s_startTransition = default;
            s_setSecond = default;
            s_startSecond = default;
            s_queueOtherBeforeThrowing = false;
            s_queueSelfBeforeThrowing = false;
            s_selfQueuesLeft = int.MaxValue;
            s_throwsLeft = int.MaxValue;
            s_handle.Set(null);
        }

        [Test]
        public void Given_AnExceptionThatLeftAnImmediateDrain_When_AnotherComponentUpdates_Then_AFrameBoundaryDrainIsRegistered()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_setTick.Invoke(1);
            var threw = DrainThrows(scheduler);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_setOther.Invoke(1);

            // Assert
            Assert.That((threw, scheduler.ScheduledCallbackCount - callbacksBefore), Is.EqualTo((true, 1)),
                "A drain that threw must not leave the immediate tier refusing to register the next one");
        }

        [Test]
        public void Given_AnUpdateQueuedInADrainBeforeAnExceptionLeftIt_When_TheExceptionLeaves_Then_AFrameBoundaryDrainIsRegisteredForIt()
        {
            // Arrange — the factory updates the other component, then throws
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueOtherBeforeThrowing = true;
            s_setTick.Invoke(1);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler);

            // Assert
            Assert.That(
                (threw, scheduler.ImmediatePendingCount, scheduler.ScheduledCallbackCount - callbacksBefore),
                Is.EqualTo((true, 1, 1)),
                "An update the drain that threw never reached is left with a drain registered to commit it");
        }

        [Test]
        public void Given_ACommitThatQueuesItselfAndThrowsOnEveryDrain_When_TheBoundedRunOfThrowingDrainsEnds_Then_ItsUpdateIsDroppedWithNoDrainRegistered()
        {
            // Arrange — every drain but the last of the run throws and re-arms
            LogAssert.Expect(LogType.Error, new Regex("in a row threw"));
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueSelfBeforeThrowing = true;
            s_setTick.Invoke(1);
            var earlierDrainsThrew = true;
            for (var i = 1; i < ThrowingDrainLimit; i++) earlierDrainsThrew &= DrainThrows(scheduler);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler);

            // Assert
            Assert.That(
                (earlierDrainsThrew, threw, scheduler.ImmediatePendingCount,
                    scheduler.ScheduledCallbackCount - callbacksBefore),
                Is.EqualTo((true, true, 0, 0)),
                "A commit that throws on every drain is dropped rather than throwing once a frame for good");
        }

        [Test]
        public void Given_ThrowingDrainsInterruptedByOneThatCompletes_When_ADrainThrowsAgain_Then_ItsQueuedUpdateStillHasADrainRegistered()
        {
            // Arrange — one throwing drain short of the bound, then a drain that completes
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueSelfBeforeThrowing = true;
            s_throwsLeft = ThrowingDrainLimit - 1;
            s_setTick.Invoke(1);
            var earlierDrainsThrew = true;
            for (var i = 1; i < ThrowingDrainLimit; i++) earlierDrainsThrew &= DrainThrows(scheduler);
            var completingDrainThrew = DrainThrows(scheduler);
            s_throwsLeft = 1;
            s_setTick.Invoke(100);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler);

            // Assert
            Assert.That(
                (earlierDrainsThrew, completingDrainThrew, threw, scheduler.ImmediatePendingCount,
                    scheduler.ScheduledCallbackCount - callbacksBefore),
                Is.EqualTo((true, false, true, 1, 1)),
                "A drain that completes starts the run of throwing drains again");
        }

        [Test]
        public void Given_AnUpdateDroppedAfterTheBoundedRunOfThrowingDrains_When_TheComponentUpdatesAgain_Then_ItCommits()
        {
            // Arrange — the drop has to leave the component clean and unqueued for its next update to enrol
            LogAssert.Expect(LogType.Error, new Regex("in a row threw"));
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueSelfBeforeThrowing = true;
            s_setTick.Invoke(1);
            var runThrew = true;
            for (var i = 0; i < ThrowingDrainLimit; i++) runThrew &= DrainThrows(scheduler);
            s_throwsLeft = 0;
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_setTick.Invoke(50);
            scheduler.DrainImmediateForTest();

            // Assert
            Assert.That((runThrew, scheduler.ScheduledCallbackCount - callbacksBefore, _root.Q<Label>().text),
                Is.EqualTo((true, 1, "owner:50")),
                "A component whose update was dropped renders its next one");
        }

        // GREEN_ON_BASE(characterization): the base drops nothing after a run of throwing drains and logs no drop.
        // What this pins is that the bound leaves a run ending with nothing queued alone.
        [Test]
        public void Given_TheBoundedRunOfThrowingDrainsEndingWithNothingQueued_When_TheLastOneThrows_Then_NoDropIsReported()
        {
            // Arrange — the last drain of the run throws without queueing anything again
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueSelfBeforeThrowing = true;
            s_selfQueuesLeft = ThrowingDrainLimit - 1;
            s_setTick.Invoke(1);
            for (var i = 1; i < ThrowingDrainLimit; i++) DrainThrows(scheduler);

            // Act
            DrainThrows(scheduler);

            // Assert — the drop's error log is what this reads; with nothing queued there is nothing to drop
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Given_AnImmediateDrainThatThrowsInsideATransitionTiersCallback_When_TheNextPassesRun_Then_OnlyTheTransitionItServedCommits()
        {
            // Arrange — the callback that runs serves "transition"; "second" waits for an earlier admission than the
            // one the next pass runs; the immediate update that callback commits first is the one whose factory throws
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            QueueTwoTransitionsAndAThrowingUpdate(scheduler);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler.RunDelayedCallbackForTest);
            var registered = scheduler.ScheduledCallbackCount - callbacksBefore;
            var admitted = scheduler.TryRunAdmitCallbackForTest();
            scheduler.RunDelayedCallbackForTest();

            // Assert
            Assert.That((threw, registered, admitted, Text(_root, "transition"), Text(_root, "second")),
                Is.EqualTo((true, 1, true, "transition:1", "second:0")),
                "A transition left waiting for a callback that has run is moved to one that will, and no other is");
        }

        [Test]
        public void Given_AnImmediateDrainThatThrowsInsideATransitionTiersCallbackOnAPanel_When_TheNextPassRuns_Then_OnlyTheTransitionItServedCommits()
        {
            // Arrange — on a panel, a request made inside a pass is admitted to a drain registered directly
            using var sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            using var mounted = V.Mount(sim.rootVisualElement, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            QueueTwoTransitionsAndAThrowingUpdate(scheduler);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler.RunDelayedCallbackForTest);
            var registered = scheduler.ScheduledCallbackCount - callbacksBefore;
            scheduler.RunDelayedCallbackForTest();

            // Assert
            Assert.That((threw, registered, Text(sim.rootVisualElement, "transition"), Text(sim.rootVisualElement, "second")),
                Is.EqualTo((true, 1, "transition:1", "second:0")),
                "A transition left waiting for a callback that has run is moved to the drain the pass it ran in registered");
        }

        private static void QueueTwoTransitionsAndAThrowingUpdate(FiberBatchScheduler scheduler)
        {
            s_throwsLeft = 1;
            s_startSecond.Invoke(() => s_setSecond.Invoke(1));
            scheduler.DrainImmediateForTest();
            scheduler.RunAdmitCallbackForTest();
            s_startTransition.Invoke(() => s_setTransitioned.Invoke(1));
            scheduler.DrainImmediateForTest();
            scheduler.RunAdmitCallbackForTest();
            s_setTick.Invoke(1);
        }

        private static string Text(VisualElement root, string name) => root.Q<Label>(name).text;

        private static bool DrainThrows(FiberBatchScheduler scheduler) => DrainThrows(scheduler.RunImmediateCallbackForTest);

        private static bool DrainThrows(Action drain)
        {
            try
            {
                drain();
            }
            catch (InvalidOperationException thrown) when (thrown.Message == HandleFailure)
            {
                return true;
            }
            return false;
        }

        // FiberBatchScheduler.ConsecutiveThrowingDrainLimit, spelled out rather than reflected for so that the base,
        // which has no such constant, still runs these cases. The dropping case reddens when the two differ.
        private const int ThrowingDrainLimit = 3;

        private const string HandleFailure = "imperative handle failed";

        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setOther;
        private static StateUpdater<int> s_setTransitioned;
        private static TransitionStarter s_startTransition;
        private static StateUpdater<int> s_setSecond;
        private static TransitionStarter s_startSecond;
        private static bool s_queueOtherBeforeThrowing;
        private static bool s_queueSelfBeforeThrowing;
        private static int s_selfQueuesLeft;
        private static int s_throwsLeft;
        private static readonly Ref<string> s_handle = new();

        private static VNode HostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(HandleOwnerRender, key: "owner"),
                V.Component(OtherRender, key: "other"),
                V.Component(TransitionRender, key: "transition"),
                V.Component(SecondTransitionRender, key: "second"),
            });

        [Component(Compiler = false)]
        private static VNode TransitionRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setTransitioned = setValue;
            var (_, start) = Hooks.UseTransition();
            s_startTransition = start;
            return V.Label(name: "transition", text: "transition:" + value);
        }

        [Component(Compiler = false)]
        private static VNode SecondTransitionRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setSecond = setValue;
            var (_, start) = Hooks.UseTransition();
            s_startSecond = start;
            return V.Label(name: "second", text: "second:" + value);
        }

        private static VNode HandleOwnerRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (tick == 0 || s_throwsLeft == 0) return "handle";
                s_throwsLeft--;
                if (s_queueOtherBeforeThrowing) s_setOther.Invoke(1);
                if (s_queueSelfBeforeThrowing && s_selfQueuesLeft-- > 0) s_setTick.Invoke(tick + 1);
                throw new InvalidOperationException(HandleFailure);
            });
            return V.Label(text: "owner:" + tick);
        }

        private static VNode OtherRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setOther = setValue;
            return V.Label(text: "other:" + value);
        }
    }
}
