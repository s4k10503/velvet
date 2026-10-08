using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when <c>FiberBatchScheduler</c>'s Transition tier renders, on a simulated panel whose clock
    /// only moves when a test advances it, one <c>FrameUpdateMs</c> at a time.
    /// <list type="bullet">
    /// <item>Transition work commits in a later pass than the urgent render that requested it: a request made
    /// inside a Velvet callback its own panel's scheduler is running drains on the next pass, one made anywhere
    /// else — a discrete handler's flush, another panel's callback, a call from outside every panel — on the pass
    /// after that.</item>
    /// <item>An urgent update reaching a component whose deferred value is already queued for the coming pass
    /// commits in that pass without the deferred value, which moves to the pass after — until it has moved as
    /// often as FiberWorkLoop's starvation bound, after which it stays and commits.</item>
    /// <item>A request stays outside every pass after a frame callback's tick has run, and a cleared scheduler
    /// queues a fiber it held afresh.</item>
    /// <item>Requests made outside every pass share one admission, and requests made inside one callback share
    /// one drain; an admission that already ran takes no later request.</item>
    /// <item>A transition requested from inside the Transition tier's own drain renders on the next pass, not
    /// again inside the one that requested it.</item>
    /// <item>The Transition tier's drain commits the Normal / Urgent work still queued before its own, and an
    /// immediate pass does not commit a deferred value through the entry of a component queued beside the
    /// parent that passes its input (TransitionTierGateTests holds the rest of that gate).</item>
    /// <item>A discrete flush that consumed the immediate queue leaves that tier's registered callback in place,
    /// so a later Normal update rides it rather than registering a second one, and still commits.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TransitionFrameSchedulingTests
    {
        private const long FrameMs = 16;

        // Far above the passes a case advances, so a re-requested render that ran again inside the pass
        // requesting it would show as a count well past the passes rather than stopping at them.
        private const int ChainCap = 10;

        private EditorPanelSimulator _sim;

        private static StateUpdater<int> s_setValue;
        private static TransitionStarter s_start;
        private static StateUpdater<int> s_setInput;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<string> s_setGatedQuery;
        private static StateUpdater<int> s_setGatedChildTick;
        private static int s_chainRenders;
        private static ComponentFiber s_chainFiber;
        private static ComponentFiber s_normalLaneFiber;
        private static ComponentFiber s_transitionLaneFiber;
        private static readonly List<string> s_renderOrder = new();

        [SetUp]
        public void SetUp()
        {
            // Same clock reset as SimulatedPanelTestBase.
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_setValue = default;
            s_start = default;
            s_setInput = default;
            s_setTick = default;
            s_setGatedQuery = default;
            s_setGatedChildTick = default;
            s_chainRenders = 0;
            s_chainFiber = null;
            s_normalLaneFiber = null;
            s_transitionLaneFiber = null;
            s_renderOrder.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        [Test]
        public void Given_ATransitionStartedOutsideASchedulerPass_When_TwoFramesPass_Then_ItHasCommitted()
        {
            // Arrange
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(TransitionRender, key: "transition"));
            s_start.Invoke(() => s_setValue.Invoke(1));

            // Act
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(Text("value"), Is.EqualTo("1"),
                "A transition requested outside every pass is admitted by the first and drained by the second");
        }

        [Test]
        public void Given_AnUrgentUpdateADeferredValueTrails_When_TwoFramesPass_Then_TheDeferredValueHasCommitted()
        {
            // Arrange
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            s_setInput.Invoke(v => v + 1);

            // Act — the first pass renders the urgent update, which requests the deferred one from inside it
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(Text("deferred"), Is.EqualTo("1"),
                "The deferred value commits on the pass after the urgent render that observed its input");
        }

        // GREEN_ON_BASE(characterization): the base's fixed delay already held the deferred value out of that pass.
        [Test]
        public void Given_AnUrgentUpdateArrivingBeforeAQueuedDeferredValueDrains_When_ThatPassRuns_Then_OnlyTheUrgentValueCommits()
        {
            // Arrange — the first pass queues the deferred value for the second, and a second urgent update
            // arrives before the second pass runs
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            s_setInput.Invoke(v => v + 1);
            _sim.FrameUpdateMs(FrameMs);
            s_setInput.Invoke(v => v + 1);

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((Text("input"), Text("deferred")), Is.EqualTo(("2", "0")),
                "The urgent render's own request waits for the pass after the one that commits it");
        }

        // GREEN_ON_BASE(characterization): the base's fixed delay already held the deferred value out of that pass.
        [Test]
        public void Given_ADeferredInputChangedInAClickHandler_When_OneFramePasses_Then_TheDeferredValueHasNotCommitted()
        {
            // Arrange — the click's flush renders the urgent update outside every scheduler pass
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            _sim.rootVisualElement.Q<Button>("set-input").SimulateClick();

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((Text("input"), Text("deferred")), Is.EqualTo(("1", "0")),
                "The next pass may share the click's frame, so it admits the deferred render rather than running it");
        }

        [Test]
        public void Given_AClicksCallbacksHaveRun_When_AClickChangesTheDeferredInputAndTwoFramesPass_Then_ItCommitsOnTheSecondOnly()
        {
            // Arrange — a first click and two passes run four kinds of callback: an immediate callback, an
            // admission, a Transition drain and a passive-effect drain, so a marker or admission any of them left
            // behind would be in place for the second click
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            var button = _sim.rootVisualElement.Q<Button>("set-input");
            button.SimulateClick();
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);
            button.SimulateClick();

            // Act
            _sim.FrameUpdateMs(FrameMs);
            var afterOnePass = (Text("input"), Text("deferred"));
            _sim.FrameUpdateMs(FrameMs);

            // Assert — the first value rides along to show the earlier passes did commit it
            Assert.That((afterOnePass.Item1, afterOnePass.Item2, Text("deferred")), Is.EqualTo(("2", "1", "2")),
                "A request from a click is outside every pass after those callbacks ran, and still commits");
        }

        // GREEN_ON_BASE(characterization): the base's fixed delay already kept that click's deferred value out of the next pass.
        [Test]
        public void Given_AUseFrameTickHasRunOnThePanel_When_AClickChangesTheDeferredInputAndOneFramePasses_Then_TheDeferredValueHasNotCommitted()
        {
            // Arrange — the first pass runs the passive effect that subscribes the frame callback, and the
            // second runs its tick, so a marker that tick left behind would be in place for the click
            using var mounted = V.Mount(_sim.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Component(FrameSubscriberRender, key: "frame-subscriber"),
                V.Component(DeferredRender, key: "deferred"),
            }));
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);
            _sim.rootVisualElement.Q<Button>("set-input").SimulateClick();

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((Text("input"), Text("deferred")), Is.EqualTo(("1", "0")),
                "A click after a frame callback's tick is still outside every pass");
        }

        // GREEN_ON_BASE(characterization): the base's Clear already emptied the delayed tier's set as well as its order.
        [Test]
        public void Given_AClearedScheduler_When_AFiberItHeldIsEnrolledOnTheTransitionTierAgain_Then_ItIsPending()
        {
            // Arrange — the fiber was queued before the clear, which also drops the anchor
            using var mounted = MountLanePair();
            var scheduler = mounted.GetSchedulerForTest();
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            scheduler.Clear();

            // Act
            FiberWorkLoop.ScheduleFlush(s_transitionLaneFiber, FiberUpdatePriority.Transition);

            // Assert
            Assert.That(scheduler.DelayedPendingCount, Is.EqualTo(1),
                "Clear forgets every entry, so enrolling the fiber again queues it afresh");
        }

        [Test]
        public void Given_ATransitionOnOnePanelStartedByAnotherPanelsCallback_When_TheFirstPanelTicksTwice_Then_ItCommitsOnTheSecondOnly()
        {
            // Arrange — the second panel's passive-effect drain starts a transition on the first panel's tree, so
            // the request is made inside a scheduler callback, but not one the first panel's scheduler is running
            using var other = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(TransitionRender, key: "transition"));
            using var starter = V.Mount(other.rootVisualElement, V.Component(CrossPanelStarterRender, key: "starter"));
            other.FrameUpdateMs(FrameMs);

            // Act
            _sim.FrameUpdateMs(FrameMs);
            var afterOnePass = Text("value");
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((afterOnePass, Text("value")), Is.EqualTo(("0", "1")),
                "A request from another panel's pass waits for an admission on this panel's next pass");
        }

        [Test]
        public void Given_ADeferredInputChangedInAClickHandler_When_TwoFramesPass_Then_TheDeferredValueHasCommitted()
        {
            // Arrange
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            _sim.rootVisualElement.Q<Button>("set-input").SimulateClick();

            // Act
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(Text("deferred"), Is.EqualTo("1"),
                "The pass after the admitting one drains the deferred render");
        }

        [Test]
        public void Given_ARenderThatReRequestsItsOwnTransition_When_TwoFramesPass_Then_ItRendersOncePerFrame()
        {
            // Arrange — up to ChainCap, each Transition-lane render of this component requests the next; the
            // first pass admits the request made outside it and the second runs the first render of the chain
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(ChainRender, key: "chain"));
            s_chainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);
            var rendersBefore = s_chainRenders;

            // Act
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(s_chainRenders - rendersBefore, Is.EqualTo(2),
                "A transition requested from inside the Transition tier's drain waits for the next pass");
        }

        [Test]
        public void Given_ImmediateAndTransitionWorkOnDifferentComponents_When_TheDelayedDrainRuns_Then_TheImmediateWorkRendersFirst()
        {
            // Arrange — the Transition-lane component is first in the tree and first enqueued, so neither order
            // puts the Normal-lane one ahead of it
            using var mounted = MountLanePair();
            s_renderOrder.Clear();
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(string.Join(", ", s_renderOrder), Is.EqualTo("normal, transition"),
                "The Transition tier's drain commits queued Normal-lane work before its own");
        }

        [Test]
        public void Given_ATransitionAlreadyDrained_When_ASecondIsStartedOutsideAPass_Then_ItCommitsTwoFramesLater()
        {
            // Arrange — the first transition's admission ran and its drain committed it
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(TransitionRender, key: "transition"));
            s_start.Invoke(() => s_setValue.Invoke(1));
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);
            s_start.Invoke(() => s_setValue.Invoke(2));

            // Act
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(Text("value"), Is.EqualTo("2"),
                "A later request outside a pass waits for an admission of its own, not one that already ran");
        }

        // GREEN_ON_BASE(characterization): the base coalesced both requests onto one delayed callback already.
        [Test]
        public void Given_TwoComponentsTransitionsStartedOutsideAPass_When_TheyAreRequested_Then_OneCallbackIsRegistered()
        {
            // Arrange
            using var mounted = MountLanePair();
            var scheduler = mounted.GetSchedulerForTest();
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Assert
            Assert.That(scheduler.ScheduledCallbackCount - callbacksBefore, Is.EqualTo(1),
                "Requests outside every pass share the one admission waiting for the next pass");
        }

        // GREEN_ON_BASE(characterization): the base coalesced both requests onto one delayed callback already.
        [Test]
        public void Given_TwoDeferredChildrenOfOneParent_When_TheParentsUrgentPassRuns_Then_OneDrainIsRegistered()
        {
            // Arrange — the parent's update registers its immediate callback before the count is read
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredParentRender, key: "parent"));
            var scheduler = mounted.GetSchedulerForTest();
            s_setTick.Invoke(v => v + 1);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act — both children request their deferred values from inside the parent's pass
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(scheduler.ScheduledCallbackCount - callbacksBefore, Is.EqualTo(1),
                "Requests made inside one callback share the drain that callback registers");
        }

        [Test]
        public void Given_AParentUpdatedEveryFrameAboveADeferredChild_When_TheDeferralBoundIsReached_Then_TheChildCommitsOnThatFrameAndNotBefore()
        {
            // Arrange — every frame the parent's urgent render hands the child a new value, which moves the
            // child's queued deferred render to the pass after; the first request makes the entry, so the
            // move that reaches the bound is the one on frame bound + 1, and the request on frame bound + 2
            // leaves the entry on the drain that runs in that same pass
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredParentRender, key: "parent"));
            var frames = StarvationThreshold.Read() + 1;
            for (var i = 0; i < frames; i++)
            {
                s_setTick.Invoke(v => v + 1);
                _sim.FrameUpdateMs(FrameMs);
            }
            var beforeTheBound = Text("child-a");

            // Act
            s_setTick.Invoke(v => v + 1);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((beforeTheBound, Text("child-a")), Is.EqualTo(("0", (frames + 1).ToString())),
                "A deferred value an urgent update keeps moving stays put once it has moved as often as the bound");
        }

        // GREEN_ON_BASE(characterization): the base's next registration already committed the update.
        [Test]
        public void Given_AnImmediateCallbackThatAlreadyRan_When_ANormalUpdateArrivesAndAFramePasses_Then_ItCommits()
        {
            // Arrange — one Normal update rendered by the panel's callback
            using var mounted = MountLanePair();
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            _sim.FrameUpdateMs(FrameMs);
            s_renderOrder.Clear();
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(string.Join(", ", s_renderOrder), Is.EqualTo("transition"),
                "The callback that ran retired its registration, so the next update registers one of its own");
        }

        [Test]
        public void Given_AnImmediateCallbackRegisteredAndADiscreteFlushThatDrainedItsQueue_When_ANormalUpdateArrives_Then_NoSecondCallbackIsRegistered()
        {
            // Arrange — a callback has run once first, so the flush below is not the scheduler's first drain
            using var mounted = MountLanePair();
            var scheduler = mounted.GetSchedulerForTest();
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            _sim.FrameUpdateMs(FrameMs);
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            scheduler.FlushImmediate();
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Assert
            Assert.That(scheduler.ScheduledCallbackCount - callbacksBefore, Is.EqualTo(0),
                "The update rides the immediate callback still registered");
        }

        // GREEN_ON_BASE(characterization): the base's second registration already committed the update.
        [Test]
        public void Given_AnImmediateCallbackRegisteredAndADiscreteFlushThatDrainedItsQueue_When_ANormalUpdateArrivesAndAFramePasses_Then_ItCommits()
        {
            // Arrange
            using var mounted = MountLanePair();
            var scheduler = mounted.GetSchedulerForTest();
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            scheduler.FlushImmediate();
            s_renderOrder.Clear();
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(string.Join(", ", s_renderOrder), Is.EqualTo("transition"),
                "The callback the flush left registered commits what arrived after it");
        }

        [Test]
        public void Given_ADeferredChildQueuedBesideTheParentThatPassesItsInput_When_TwoFramesPass_Then_TheDeferredValueCommitsOnlyOnTheSecond()
        {
            // Arrange — the child's own update and the parent's both land on the immediate tier
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(GatedParentRender, key: "gated-parent"));
            s_setGatedChildTick.Invoke(1);
            s_setGatedQuery.Invoke("beta");

            // Act
            _sim.FrameUpdateMs(FrameMs);
            var afterTheUrgentPass = Text("gated-child");
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That((afterTheUrgentPass, Text("gated-child")), Is.EqualTo(("alpha:1", "beta:1")),
                "The child's own immediate entry does not commit the deferred value its parent's pass re-requested");
        }

        private string Text(string name) => _sim.rootVisualElement.Q<Label>(name).text;

        private MountedTree MountLanePair()
            => V.Mount(_sim.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Component(TransitionLaneRender, key: "transition-lane"),
                V.Component(NormalLaneRender, key: "normal-lane"),
            }));

        [Component(Compiler = false)]
        private static VNode TransitionRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setValue = setValue;
            var (_, start) = Hooks.UseTransition();
            s_start = start;
            return V.Label(name: "value", text: value.ToString());
        }

        [Component(Compiler = false)]
        private static VNode FrameSubscriberRender()
        {
            Hooks.UseFrame(_ => { });
            return V.Label();
        }

        [Component(Compiler = false)]
        private static VNode CrossPanelStarterRender()
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_start.Invoke(() => s_setValue.Invoke(1));
                return null;
            }), Array.Empty<object>());
            return V.Label();
        }

        [Component(Compiler = false)]
        private static VNode DeferredRender()
        {
            var (input, setInput) = Hooks.UseState(0);
            s_setInput = setInput;
            var deferred = Hooks.UseDeferredValue(input);
            // Gives every commit a passive-effect drain, so a pass runs that bracketed callback too.
            Hooks.UseEffect((Func<Action>)(() => null));
            return V.Div(children: new VNode[]
            {
                V.Label(name: "input", text: input.ToString()),
                V.Label(name: "deferred", text: deferred.ToString()),
                V.Button(name: "set-input", onClick: () => setInput.Invoke(v => v + 1)),
            });
        }

        [Component(Compiler = false)]
        private static VNode DeferredParentRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(DeferredChildARender, tick, key: "child-a"),
                V.Component(DeferredChildBRender, tick, key: "child-b"),
            });
        }

        [Component(Compiler = false)]
        private static VNode DeferredChildARender(int value)
        {
            var deferred = Hooks.UseDeferredValue(value);
            return V.Label(name: "child-a", text: deferred.ToString());
        }

        [Component(Compiler = false)]
        private static VNode DeferredChildBRender(int value)
        {
            var deferred = Hooks.UseDeferredValue(value);
            return V.Label(name: "child-b", text: deferred.ToString());
        }

        [Component(Compiler = false)]
        private static VNode GatedParentRender()
        {
            var (query, setQuery) = Hooks.UseState("alpha");
            s_setGatedQuery = setQuery;
            return V.Div(children: new VNode[] { V.Component(GatedChildRender, query, key: "gated-child") });
        }

        [Component(Compiler = false)]
        private static VNode GatedChildRender(string query)
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setGatedChildTick = setTick;
            var deferred = Hooks.UseDeferredValue(query);
            return V.Label(name: "gated-child", text: $"{deferred}:{tick}");
        }

        [Component(Compiler = false)]
        private static VNode ChainRender()
        {
            s_chainRenders++;
            var fiber = FiberAmbientStack.Current;
            s_chainFiber = fiber;
            if (FiberWorkLoop.IsRenderingTransitionLane && s_chainRenders < ChainCap)
            {
                FiberWorkLoop.RequestTransitionRerender(fiber);
            }
            return V.Label();
        }

        [Component(Compiler = false)]
        private static VNode TransitionLaneRender()
        {
            s_transitionLaneFiber = FiberAmbientStack.Current;
            s_renderOrder.Add("transition");
            return V.Label();
        }

        [Component(Compiler = false)]
        private static VNode NormalLaneRender()
        {
            s_normalLaneFiber = FiberAmbientStack.Current;
            s_renderOrder.Add("normal");
            return V.Label();
        }
    }
}
