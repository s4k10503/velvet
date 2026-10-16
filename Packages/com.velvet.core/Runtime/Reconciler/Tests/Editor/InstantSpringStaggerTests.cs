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
    /// Pins a spring with a zero duration — instant, as Framer Motion makes a transition of no duration — waiting
    /// out the stagger the reconciler hands its play, as Framer's instant animation waits out its delay: a label
    /// change a coordinator staggers, and an exit an <see cref="V.AnimatePresence"/> staggers.
    /// </summary>
    internal sealed class InstantSpringStaggerTests
    {
        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("visible")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("visible"));
        }

        private readonly record struct KeysState(string Keys);

        private sealed class KeysStore : Store<KeysState>
        {
            public KeysStore() : base(new KeysState("ab")) { }
            public void Set(string keys) => SetState(_ => new KeysState(keys));
            protected override void ResetCore() => SetState(_ => new KeysState("ab"));
        }

        private static readonly StyleTransitionConfig s_instant = new() { Type = TransitionType.Spring, DurationSec = 0f };

        private static readonly StyleTransitionConfig s_bezier = new() { Type = TransitionType.Bezier, DurationSec = 0.3f };

        // The coordinator applies no class; its visible pose staggers the children a label change sends there.
        private static readonly Dictionary<string, MotionVariant> s_coordinator = new()
        {
            ["hidden"] = new MotionVariant(null),
            ["visible"] = new MotionVariant(null, new StyleTransitionConfig { StaggerChildrenSec = 0.2f }),
        };

        // A child moves to hidden on a bezier and back to visible on an instant spring.
        private static readonly Dictionary<string, MotionVariant> s_child = new()
        {
            ["hidden"] = new MotionVariant("translate-x-[0px]", s_bezier),
            ["visible"] = new MotionVariant("translate-x-[100px]", s_instant),
        };

        private static readonly Dictionary<string, MotionVariant> s_exiting = new()
        {
            ["shown"] = "opacity-100",
            ["gone"] = new MotionVariant("opacity-0", s_instant),
        };

        private static LabelStore s_labels;
        private static KeysStore s_keys;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_labels = null;
            s_keys = null;
        }

        [TearDown]
        public void TearDown()
        {
            s_labels?.Dispose();
            s_keys?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Ticks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _sim.FrameUpdateMs(16);
            }
        }

        [Component]
        private static VNode Coordinator()
        {
            var label = Hooks.UseStore(s_labels, s => s.Label);
            return V.Motion(key: "p", name: "p", variants: s_coordinator, animate: label,
                children: new VNode[]
                {
                    V.Motion(key: "c0", name: "c0", variants: s_child),
                    V.Motion(key: "c1", name: "c1", variants: s_child),
                });
        }

        [Component]
        private static VNode Presence()
        {
            var keys = Hooks.UseStore(s_keys, s => s.Keys);
            var children = new List<VNode>();
            foreach (var key in keys)
            {
                var name = key.ToString();
                children.Add(V.Motion(key: name, name: name, variants: s_exiting, animate: "shown", exit: "gone"));
            }
            return V.AnimatePresence(key: "presence", initial: false, staggerSec: 0.2f, children: children.ToArray());
        }

        private static float TranslateX(VisualElement element) => element.style.translate.value.x.value;

        [Test]
        public void Given_AStaggeredChildMidwayOnABezier_When_ItsLabelChangesToAnInstantSpring_Then_ItHoldsTheValueDrawnThroughItsStagger()
        {
            // Arrange — resting at visible (100), six frames into the bezier back to hidden (0).
            s_labels = new LabelStore();
            using var mounted = V.Mount(Root, V.Component(Coordinator, key: "root"));
            Ticks(1);
            s_labels.Set("hidden");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(6);
            var c1 = Root.Q<VisualElement>("c1");
            var drawn = TranslateX(c1);

            // Act — back to visible: the second child's slot is 0.2 s on, and a frame of it passes.
            s_labels.Set("visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(1);

            // Assert — still where the bezier drew it, as Framer's delayed instant animation leaves the value; the
            // swapped-out pose would put it at 0, and landing without the stagger at 100. NaN unless the bezier
            // had it midway.
            var midway = drawn > 5f && drawn < 95f;
            Assert.That(midway ? TranslateX(c1) : float.NaN, Is.EqualTo(drawn).Within(1f));
        }

        [Test]
        public void Given_AStaggeredChildMidwayOnABezier_When_ItsLabelChangesToAnInstantSpringAndItsStaggerRuns_Then_ItLands()
        {
            // Arrange
            s_labels = new LabelStore();
            using var mounted = V.Mount(Root, V.Component(Coordinator, key: "root"));
            Ticks(1);
            s_labels.Set("hidden");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(6);

            // Act — past the second child's 0.2 s slot.
            s_labels.Set("visible");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(20);

            // Assert
            Assert.That(TranslateX(Root.Q<VisualElement>("c1")), Is.EqualTo(100f).Within(1e-3f));
        }

        [Test]
        public void Given_APresenceStaggeringTwoInstantSpringExits_When_BothAreRemoved_Then_TheSecondIsHeldThroughItsSlotAndTheFirstIsNot()
        {
            // Arrange
            s_keys = new KeysStore();
            using var mounted = V.Mount(Root, V.Component(Presence, key: "root"));
            Ticks(1);

            // Act — both removed, then 0.1 s: past the first exit's slot at 0 and inside the second's at 0.2 s.
            s_keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(6);

            // Assert
            Assert.That((Root.Q<VisualElement>("a") != null, Root.Q<VisualElement>("b") != null),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_APresenceStaggeringTwoInstantSpringExits_When_TheSecondSlotHasRun_Then_BothAreRemoved()
        {
            // Arrange
            s_keys = new KeysStore();
            using var mounted = V.Mount(Root, V.Component(Presence, key: "root"));
            Ticks(1);

            // Act — past the second exit's slot.
            s_keys.Set(string.Empty);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(20);

            // Assert
            Assert.That((Root.Q<VisualElement>("a") != null, Root.Q<VisualElement>("b") != null),
                Is.EqualTo((false, false)));
        }
    }
}
