using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how <see cref="Hooks.UseState{T}(T)"/> and <see cref="Hooks.UseReducer{TState,TAction}"/> queue
    /// their updates by lane, as React's update queues do.
    /// <list type="bullet">
    /// <item>A render folds in the queued updates its lane renders and leaves the rest queued. Urgent and Normal
    /// render together, as React's sync, input-continuous and default lanes do, so only a Transition update
    /// waits for a Transition render: any other render shows the state from before a pending transition's
    /// writes, and the render that reaches the transition replays them in the order they were written over the
    /// state they were skipped from, with the updates applied in between.</item>
    /// <item>A <c>UseState</c> write made while none is scheduled runs its updater at the call, and an equal
    /// result is queued without a render; the render replays the result it computed. A write made behind a
    /// scheduled one runs at the render. A <c>UseReducer</c> dispatch always runs at the render, with the
    /// render's own reducer, and always renders.</item>
    /// <item>A write during its component's render takes the render's lane, so the render that reruns for it
    /// folds it in.</item>
    /// <item>A component that is gone queues nothing and runs no updater.</item>
    /// <item>The lane a flush renders is back to the one outside every flush when it returns, and a parked
    /// transition's resumed slice renders the Transition lane.</item>
    /// <item>The scheduler's update-depth cap drops a starvation-promoted transition's Normal lane without
    /// stranding its queued writes: the Transition lane and the pending flag survive it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class HookUpdateQueueTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            FiberWorkLoop.IsInDiscreteEvent = false;
            s_runsFirst = 0;
            s_runsSecond = 0;
            s_shown.Clear();
        }

        private MountedTree MountRebase()
        {
            // The writes are + 1 (Normal), * 2 (Transition), + 100 (Normal) over 10
            var mounted = V.Mount(_root, V.Component(RebaseRender, key: "rebase"));
            s_rebaseSetCount.Invoke(prev => prev + 1);
            s_rebaseStart.Invoke(() => s_rebaseSetCount.Invoke(prev => prev * 2));
            s_rebaseSetCount.Invoke(prev => prev + 100);
            return mounted;
        }

        [Test]
        public void Given_ATransitionWriteBetweenTwoNormalOnes_When_TheNormalLaneRenders_Then_ItShowsTheOthersWithoutTheTransitionsWrite()
        {
            // Arrange
            using var mounted = MountRebase();

            // Act
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>("rebase-out").text, Is.EqualTo("111"),
                "A render of another lane shows 11 + 100 and not the doubling in between");
        }

        [Test]
        public void Given_ATransitionWriteBetweenTwoNormalOnes_When_AClickRendersTheUrgentLane_Then_ItStillShowsTheOthersWithoutTheTransitionsWrite()
        {
            // Arrange
            using var mounted = MountRebase();
            mounted.FlushStateForTest();

            // Act
            _root.Q<Button>("rebase-click").SimulateClick();

            // Assert
            Assert.That(_root.Q<Label>("rebase-out").text, Is.EqualTo("111"),
                "The Urgent render keeps the Normal write it follows and still skips the transition's");
        }

        [Test]
        public void Given_ATransitionWriteBetweenTwoNormalOnes_When_TheTransitionLaneRenders_Then_ItReplaysOverTheStateItSkippedFrom()
        {
            // Arrange
            using var mounted = MountRebase();
            mounted.FlushStateForTest();
            _root.Q<Button>("rebase-click").SimulateClick();

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the doubling replays over 11 and the later + 100 follows it
            Assert.That(_root.Q<Label>("rebase-out").text, Is.EqualTo("122"),
                "The Transition render applies its write over the state it was skipped from, then the write after it");
        }

        private static StateUpdater<int> s_rebaseSetCount;
        private static TransitionStarter s_rebaseStart;

        [Component]
        private static VNode RebaseRender()
        {
            var (count, setCount) = Hooks.UseState(10);
            var (tick, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_rebaseSetCount = setCount;
            s_rebaseStart = start;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "rebase-click", onClick: () => setTick.Invoke(tick + 1)),
                V.Label(name: "rebase-out", text: count.ToString()),
            });
        }

        [Test]
        public void Given_ANormalWrite_When_AnUrgentUpdaterFollowsAndTheClickRenders_Then_TheFirstRenderAlreadyHasBoth()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UrgentRender, key: "urgent"));
            s_urgentSetCount.Invoke(5);

            // Act
            _root.Q<Button>("urgent-click").SimulateClick();

            // Assert — index 0 is the mount
            Assert.That(s_shown[1], Is.EqualTo(6),
                "The Urgent render folds in the Normal write ahead of it, as React's sync and default lanes render together");
        }

        private static readonly List<int> s_shown = new();
        private static StateUpdater<int> s_urgentSetCount;

        [Component]
        private static VNode UrgentRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_urgentSetCount = setCount;
            s_shown.Add(count);
            return V.Button(name: "urgent-click", onClick: () => setCount.Invoke(c => c + 1));
        }

        // GREEN_ON_BASE(characterization): the base ran the first updater once, at the call, and folding at the render has to keep that.
        [Test]
        public void Given_TwoChainedUpdaters_When_AnUnrelatedRenderFollows_Then_TheFirstDoesNotRunAgain()
        {
            // Arrange — the first updater runs at the call, the second behind it at the render
            using var mounted = ArrangeChainedUpdaters();

            // Act
            s_runsSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_runsFirst, Is.EqualTo(1), "A write that ran at the call replays its result and is not run again");
        }

        // GREEN_ON_BASE(characterization): the base ran the second updater once, at the call, and folding at the render has to keep that.
        [Test]
        public void Given_TwoChainedUpdaters_When_AnUnrelatedRenderFollows_Then_TheSecondDoesNotRunAgain()
        {
            // Arrange
            using var mounted = ArrangeChainedUpdaters();

            // Act
            s_runsSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_runsSecond, Is.EqualTo(1), "A write already folded into the state is not run again by a later render");
        }

        private MountedTree ArrangeChainedUpdaters()
        {
            var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));
            s_runsSetCount.Invoke(prev =>
            {
                s_runsFirst++;
                return prev + 1;
            });
            s_runsSetCount.Invoke(prev =>
            {
                s_runsSecond++;
                return prev * 2;
            });
            mounted.FlushStateForTest();
            return mounted;
        }

        // GREEN_ON_BASE(characterization): the base ran no updater of an unmounted component, and evaluating at the call has to keep that.
        [Test]
        public void Given_AnUnmountedComponent_When_ItsUpdaterIsInvoked_Then_TheUpdaterDoesNotRun()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));
            var setCount = s_runsSetCount;
            mounted.Dispose();

            // Act
            setCount.Invoke(prev =>
            {
                s_runsFirst++;
                return prev + 1;
            });

            // Assert
            Assert.That(s_runsFirst, Is.EqualTo(0), "A setter of an unmounted component runs nothing");
        }

        // GREEN_ON_BASE(characterization): the base returned before it looked at the updater once the component was gone.
        [Test]
        public void Given_AnUnmountedComponent_When_ItsUpdaterIsInvokedWithNull_Then_NothingIsThrown()
        {
            // Arrange
            var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));
            var setCount = s_runsSetCount;
            mounted.Dispose();

            // Act + Assert
            Assert.DoesNotThrow(() => setCount.Invoke((System.Func<int, int>)null));
        }

        private static StateUpdater<int> s_runsSetCount;
        private static StateUpdater<int> s_runsSetTick;
        private static int s_runsFirst;
        private static int s_runsSecond;

        [Component]
        private static VNode RunsRender()
        {
            var (count, setCount) = Hooks.UseState(10);
            var (_, setTick) = Hooks.UseState(0);
            s_runsSetCount = setCount;
            s_runsSetTick = setTick;
            return V.Label(name: "runs-out", text: count.ToString());
        }

        [Test]
        public void Given_ATransitionWriteQueued_When_AnEqualWriteFollowsOnTheNormalLane_Then_ItStillAsksForARender()
        {
            // Arrange — the equal write would bail out if nothing were scheduled, as React's does only then
            using var mounted = V.Mount(_root, V.Component(RebaseRender, key: "rebase"));
            s_rebaseStart.Invoke(() => s_rebaseSetCount.Invoke(11));

            // Act
            s_rebaseSetCount.Invoke(10);

            // Assert
            Assert.That(mounted.Root.LaneQueue.Contains(FiberUpdatePriority.Normal), Is.True,
                "A write behind a scheduled one is not computed at the call, so it cannot bail out");
        }

        [Test]
        public void Given_RepeatedEqualWrites_When_NoRenderFollows_Then_OnlyTheLatestStaysQueued()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));

            // Act
            s_runsSetCount.Invoke(10);
            s_runsSetCount.Invoke(10);
            s_runsSetCount.Invoke(10);

            // Assert — read by name, since no surface a caller reaches shows the queue
            var pending = (System.Collections.ICollection)typeof(HookUpdateQueueSlot<int, StateAction<int>>)
                .GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(mounted.Root.StateSlots[0]);
            Assert.That(pending.Count, Is.EqualTo(1),
                "Writes that bailed out are no-ops, so each replaces the ones before it rather than piling up");
        }

        [Test]
        public void Given_AReducerRebuiltByEveryRender_When_ADispatchIsFoldedByARenderWithADifferentOne_Then_TheRendersReducerDecides()
        {
            // Arrange — the reducer closes over step, so each render hands the hook a different one
            using var mounted = V.Mount(_root, V.Component(SwappedReducerRender, key: "swapped"));
            s_swappedDispatch.Invoke(1);
            mounted.FlushStateForTest();

            // Act — the dispatch is queued under step 1, and the render that folds it has step 5
            s_swappedSetStep.Invoke(5);
            s_swappedDispatch.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — 11 after the first dispatch, then + 1 * 5
            Assert.That(_root.Q<Label>("swapped-out").text, Is.EqualTo("16"),
                "A dispatch is run by the reducer of the render that folds it, over the state before it");
        }

        private static System.Action<int> s_swappedDispatch;
        private static StateUpdater<int> s_swappedSetStep;

        [Component]
        private static VNode SwappedReducerRender()
        {
            var (step, setStep) = Hooks.UseState(1);
            var (total, dispatch) = Hooks.UseReducer<int, int>((state, action) => state + action * step, 10);
            s_swappedSetStep = setStep;
            s_swappedDispatch = dispatch;
            return V.Label(name: "swapped-out", text: total.ToString());
        }

        // GREEN_ON_BASE(characterization): the base wrote a render-phase write straight into the state, and the queued one has to reach the rerun on a click's Urgent lane.
        [Test]
        public void Given_ARenderPhaseWrite_When_AClickRendersTheUrgentLane_Then_TheRerunFoldsItIn()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DerivedRender, key: "derived"));

            // Act
            _root.Q<Button>("derived-click").SimulateClick();

            // Assert
            Assert.That(_root.Q<Label>("derived-out").text, Is.EqualTo("1"),
                "A write made while its component renders is folded in by the rerun it asks for, whichever lane renders");
        }

        [Component]
        private static VNode DerivedRender()
        {
            var (clicks, setClicks) = Hooks.UseState(0);
            var (derived, setDerived) = Hooks.UseState(0);
            if (derived != clicks) setDerived.Invoke(clicks);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "derived-click", onClick: () => setClicks.Invoke(clicks + 1)),
                V.Label(name: "derived-out", text: derived.ToString()),
            });
        }

        [Test]
        public void Given_ATransitionFlush_When_ItReturns_Then_TheRenderLaneIsTheOneOutsideEveryFlush()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RebaseRender, key: "rebase"));
            s_rebaseStart.Invoke(() => s_rebaseSetCount.Invoke(11));

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(FiberWorkLoop.RenderLane, Is.EqualTo(FiberUpdatePriority.Normal),
                "A flush hands the render lane back as it found it, which is not the Transition lane outside a flush");
        }

        private const int StarvationThreshold = 30;

        // Parks a starvation-promoted lane behind an Urgent one, with the transition's write still queued.
        private MountedTree ArrangePromotedTransition()
        {
            var mounted = V.Mount(_root, V.Component(RebaseRender, key: "rebase"));
            s_rebaseStart.Invoke(() => s_rebaseSetCount.Invoke(11));
            for (var i = 0; i < StarvationThreshold - 1; i++)
            {
                s_rebaseSetCount.Invoke(20 + i);
                mounted.FlushStateForTest();
            }
            mounted.Root.ScheduleRerenderForTest(FiberUpdatePriority.Urgent);
            mounted.FlushStateForTest();
            return mounted;
        }

        private static void DropImmediateIntake(MountedTree mounted)
        {
            var scheduler = mounted.GetSchedulerForTest();
            typeof(FiberBatchScheduler).GetMethod("DropImmediateIntake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(scheduler, null);
        }

        [Test]
        public void Given_APromotedTransitionBehindUrgentWork_When_TheUpdateDepthCapDropsTheImmediateIntake_Then_TheTransitionLaneIsQueuedAgain()
        {
            // Arrange
            using var mounted = ArrangePromotedTransition();
            var promoted = mounted.Root.HasPromotedTransition;

            // Act
            DropImmediateIntake(mounted);

            // Assert — the promoted term, since a lane that was never promoted keeps its own Transition lane
            Assert.That(
                (promoted, mounted.Root.LaneQueue.Contains(FiberUpdatePriority.Transition)),
                Is.EqualTo((true, true)),
                "The drop leaves the transition's writes a lane to flush on");
        }

        [Test]
        public void Given_APromotedTransitionBehindUrgentWork_When_TheUpdateDepthCapDropsTheImmediateIntake_Then_ItsFlagStaysLit()
        {
            // Arrange
            using var mounted = ArrangePromotedTransition();
            var promoted = mounted.Root.HasPromotedTransition;

            // Act
            DropImmediateIntake(mounted);

            // Assert
            Assert.That((promoted, mounted.Root.IsTransitionPending), Is.EqualTo((true, true)),
                "The transition's write is still queued, so the drop does not settle it");
        }

        [Test]
        public void Given_APromotedTransitionBehindUrgentWork_When_TheUpdateDepthCapDropsTheImmediateIntake_Then_ThePromotedMarkerRetires()
        {
            // Arrange
            using var mounted = ArrangePromotedTransition();
            var promoted = mounted.Root.HasPromotedTransition;

            // Act
            DropImmediateIntake(mounted);

            // Assert
            Assert.That((promoted, mounted.Root.HasPromotedTransition), Is.EqualTo((true, false)),
                "The Normal lane the marker rode is gone, so a later settle is not skipped for it");
        }
    }
}
