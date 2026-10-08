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
    /// only moves when a test advances it.
    /// <list type="bullet">
    /// <item>A <c>startTransition</c> update on an otherwise idle tree commits on the next frame.</item>
    /// <item>A <c>UseDeferredValue</c> derivation commits on the frame after the urgent render that observed
    /// the changed input.</item>
    /// <item>A transition requested from inside the Transition tier's own drain renders on the next frame, not
    /// again inside the one that requested it.</item>
    /// <item>The Transition tier's drain commits the Normal / Urgent work still queued before its own, and with
    /// none queued it leaves the immediate tier's registration as it found it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TransitionFrameSchedulingTests
    {
        private const long FrameMs = 16;

        // Far above the two frames a case advances, so a re-requested render that ran again inside the frame
        // requesting it would show as a count well past two rather than stopping at it.
        private const int ChainCap = 10;

        private EditorPanelSimulator _sim;

        private static StateUpdater<int> s_setValue;
        private static TransitionStarter s_start;
        private static StateUpdater<int> s_setInput;
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
        public void Given_ATransitionOnAnIdleTree_When_OneFramePasses_Then_ItHasCommitted()
        {
            // Arrange
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(TransitionRender, key: "transition"));
            s_start.Invoke(() => s_setValue.Invoke(1));

            // Act
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(_sim.rootVisualElement.Q<Label>("value").text, Is.EqualTo("1"),
                "A transition with nothing ahead of it renders on the next frame");
        }

        [Test]
        public void Given_AnUrgentUpdateADeferredValueTrails_When_TwoFramesPass_Then_TheDeferredValueHasCommitted()
        {
            // Arrange
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(DeferredRender, key: "deferred"));
            s_setInput.Invoke(1);

            // Act — the first frame renders the urgent update, which requests the deferred one
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(_sim.rootVisualElement.Q<Label>("deferred").text, Is.EqualTo("1"),
                "The deferred value commits on the frame after the urgent render that observed its input");
        }

        [Test]
        public void Given_ARenderThatReRequestsItsOwnTransition_When_TwoFramesPass_Then_ItRendersOncePerFrame()
        {
            // Arrange — up to ChainCap, each Transition-lane render of this component hands its deferred value
            // a new input, so each one requests the next
            using var mounted = V.Mount(_sim.rootVisualElement, V.Component(ChainRender, key: "chain"));
            var rendersAtMount = s_chainRenders;
            s_chainFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);

            // Act
            _sim.FrameUpdateMs(FrameMs);
            _sim.FrameUpdateMs(FrameMs);

            // Assert
            Assert.That(s_chainRenders - rendersAtMount, Is.EqualTo(2),
                "A transition requested from inside the Transition tier's drain waits for the next frame");
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

        // GREEN_ON_BASE(characterization): a base delayed drain with no immediate work queued left that tier's registration alone.
        [Test]
        public void Given_AnImmediateCallbackStillRegisteredOverAnEmptiedQueue_When_TheDelayedDrainRuns_Then_ALaterNormalUpdateRegistersNoSecondCallback()
        {
            // Arrange — the immediate callback is registered and its only fiber leaves the queue, as an unmount
            // before the frame does
            using var mounted = MountLanePair();
            var scheduler = mounted.GetSchedulerForTest();
            s_normalLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            scheduler.Remove(s_normalLaneFiber);
            scheduler.DrainDelayedForTest();
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_transitionLaneFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Assert
            Assert.That(scheduler.ScheduledCallbackCount - callbacksBefore, Is.EqualTo(0),
                "The update rides the immediate callback already registered");
        }

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
        private static VNode DeferredRender()
        {
            var (input, setInput) = Hooks.UseState(0);
            s_setInput = setInput;
            var deferred = Hooks.UseDeferredValue(input);
            return V.Label(name: "deferred", text: deferred.ToString());
        }

        [Component(Compiler = false)]
        private static VNode ChainRender()
        {
            s_chainRenders++;
            s_chainFiber = FiberAmbientStack.Current;
            var deferred = Hooks.UseDeferredValue(Math.Min(s_chainRenders, ChainCap));
            return V.Label(text: deferred.ToString());
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
