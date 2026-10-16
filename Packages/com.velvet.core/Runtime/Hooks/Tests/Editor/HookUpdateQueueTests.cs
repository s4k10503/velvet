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
    /// <item>A render folds in the queued updates of its own lane and the lanes above it, and leaves the rest
    /// queued: an urgent render shows the state from before a pending transition's writes, and the render that
    /// reaches the transition replays them in the order they were written over the state they were skipped
    /// from, with the updates applied in between.</item>
    /// <item>An update written while nothing is queued runs its updater or reducer at the call, and a result
    /// equal to the committed state queues nothing. It does not run again at the render; one queued behind
    /// another runs at the render, once, and not again by a later render that has nothing left to fold.</item>
    /// <item>A reducer that is not the one the update was computed with runs again at the render, so the
    /// render's own reducer decides the state.</item>
    /// <item>A write during its component's render takes the render's lane, so the render that reruns for it
    /// folds it in.</item>
    /// <item>A component that is gone queues nothing and runs no updater.</item>
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
        }

        [Test]
        public void Given_ATransitionWriteBetweenTwoNormalOnes_When_EachLaneRenders_Then_TheTransitionReplaysOverTheStateItSkippedFrom()
        {
            // Arrange — the writes are + 1 (Normal), * 2 (Transition), + 100 (Normal) over 10
            using var mounted = V.Mount(_root, V.Component(RebaseRender, key: "rebase"));
            s_rebaseSetCount.Invoke(prev => prev + 1);
            s_rebaseStart.Invoke(() => s_rebaseSetCount.Invoke(prev => prev * 2));
            s_rebaseSetCount.Invoke(prev => prev + 100);

            // Act — the Normal lane renders, then a click renders the Urgent lane, then the Transition lane does
            mounted.FlushStateForTest();
            var afterNormal = _root.Q<Label>("rebase-out").text;
            _root.Q<Button>("rebase-click").SimulateClick();
            var afterUrgent = _root.Q<Label>("rebase-out").text;
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert — the Normal and the Urgent render show 11 + 100 without the doubling, and the Transition
            // render replays it over 11: 22, then + 100
            Assert.That(
                $"{afterNormal}|{afterUrgent}|{_root.Q<Label>("rebase-out").text}",
                Is.EqualTo("111|111|122"),
                "A render skips the updates of lanes below its own, and the one that reaches them replays them over the state they were skipped from");
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

        // GREEN_ON_BASE(characterization): the base ran each updater once at the call, and folding at the render has to keep that.
        [Test]
        public void Given_TwoChainedUpdaters_When_AnUnrelatedRenderFollows_Then_NeitherRunsAgain()
        {
            // Arrange — the first updater runs at the call, the second behind it at the render
            using var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));
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

            // Act
            s_runsSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (_root.Q<Label>("runs-out").text, s_runsFirst, s_runsSecond),
                Is.EqualTo(("22", 1, 1)),
                "An update that has been folded into the state is not replayed by a render that has nothing left to fold");
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
        public void Given_AReducerRebuiltByEveryRender_When_ADispatchIsFoldedByARenderWithADifferentOne_Then_TheRendersReducerDecides()
        {
            // Arrange — the reducer closes over step, so each render hands the hook a different one
            using var mounted = V.Mount(_root, V.Component(SwappedReducerRender, key: "swapped"));
            s_swappedDispatch.Invoke(1);
            mounted.FlushStateForTest();

            // Act — the dispatch is computed with step 1, and the render that folds it has step 5
            s_swappedSetStep.Invoke(5);
            s_swappedDispatch.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — 11 after the first dispatch, then + 1 * 5
            Assert.That(_root.Q<Label>("swapped-out").text, Is.EqualTo("16"),
                "A dispatch computed with another reducer is computed again with the one the render holds, over the state before it");
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
        public void Given_ANormalFlush_When_ItReturns_Then_TheRenderLaneIsTheOneOutsideEveryFlush()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RunsRender, key: "runs"));
            s_runsSetTick.Invoke(1);

            // Act
            mounted.FlushStateForTest();

            // Assert — read by name, since a flush's lane is not part of any surface a caller reaches
            var renderLane = typeof(FiberWorkLoop).GetField("RenderLane", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(renderLane.GetValue(null), Is.EqualTo(FiberUpdatePriority.Transition),
                "A flush hands the render lane back as it found it, which outside a flush folds every lane in");
        }
    }
}
