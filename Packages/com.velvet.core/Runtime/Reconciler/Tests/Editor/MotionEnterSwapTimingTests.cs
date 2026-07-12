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
    /// Pins the frame discipline of a classic (tween) variant enter's class swap: the from-state
    /// must survive the tick that started the enter, because the panel computes styles only after
    /// the timer queue drains — a swap that runs before the from-state's first style pass leaves
    /// the CSS transition with no property change to animate, degenerating the whole enter into an
    /// instant jump. The dangerous shape is production's own: the mount runs inside the panel's
    /// timer tick (the batch scheduler's drain is itself a scheduled item), the enter's swap is
    /// registered on a freshly attached element, and a zero-delay swap item then becomes runnable
    /// in the very tick that mounted the element.
    /// </summary>
    [TestFixture]
    internal sealed class MotionEnterSwapTimingTests
    {
        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private readonly record struct SetState(string Keys);

        private sealed class SetStore : Store<SetState>
        {
            public SetStore(string initial) : base(new SetState(initial)) { }
            public void Set(string keys) => SetState(_ => new SetState(keys));
            protected override void ResetCore() => SetState(_ => new SetState(""));
        }

        private static SetStore s_store;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_store = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        private VisualElement StartEnterInsideATimerTick(StyleAnimationScheduler scheduler)
        {
            var element = new VisualElement();
            Root.Add(element);
            element.AddToClassList("opacity-100");
            Tick();
            element.schedule.Execute(() =>
            {
                scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                    durationSec: 0.3f, easing: EasingMode.EaseInOut, delaySec: 0f);
            });
            return element;
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mirror production: the enter's step 1 (strip to-classes, apply from-classes,
            // schedule the swap) runs inside the panel's own timer tick.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);

            // Act — the single tick that both starts the enter and drains the timer queue.
            Tick();

            // Assert — the from-state must survive the tick that started the enter; a swap that ran
            // in the same tick strips it before the panel computes it once, so the transition sees
            // no change and the enter degenerates to an instant jump.
            Assert.That(element.ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AVariantEnterStartedInsideATimerTick_When_TheNextTickRuns_Then_TheSwapReachesTheAnimateState()
        {
            // Arrange — same production shape as above.
            var scheduler = new StyleAnimationScheduler();
            var element = StartEnterInsideATimerTick(scheduler);
            Tick();
            Assume.That(element.ClassListContains("opacity-0"), Is.True,
                "Precondition: the from-state survived the starting tick");

            // Act — the next tick is where the deferred swap belongs.
            Tick();

            // Assert — the swap did fire on the following tick (the enter must still make progress,
            // not park the from-state forever).
            Assert.That(element.ClassListContains("opacity-0"), Is.False);
        }

        [Component]
        private static VNode LateMountHost()
        {
            var keys = Hooks.UseStore(s_store, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                children.Add(V.Motion(name: "late-" + key, key: key.ToString(), variants: s_fade,
                    initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }));
            }
            return V.Div(name: "host", children: children.ToArray());
        }

        [Test]
        public void Given_AMotionMountedByATimerTickDrain_When_ThatTickEnds_Then_TheFromStateIsStillApplied()
        {
            // Arrange — mount the host and settle, then dirty the store WITHOUT a manual drain, so
            // the new Motion's whole mount (create detached -> play enter -> attach) happens inside
            // the panel's own timer tick via the batch scheduler's scheduled drain, exactly like
            // production. The enter's zero-delay swap item is attached mid-tick with its deadline
            // already reached.
            using var store = new SetStore("");
            s_store = store;
            using var mounted = V.Mount(Root, V.Component(LateMountHost, key: "root"));
            Tick();
            store.Set("a");

            // Act — the single tick that both drains the batch (mounting the Motion) and the timer
            // queue (where the just-scheduled swap must NOT yet run).
            Tick();

            // Assert — the from-state survived its mounting tick; swapping in the same tick would
            // strip it before its first style pass and the enter would play as an instant jump.
            Assert.That(Root.Q<VisualElement>("late-a").ClassListContains("opacity-0"), Is.True);
        }
    }
}
