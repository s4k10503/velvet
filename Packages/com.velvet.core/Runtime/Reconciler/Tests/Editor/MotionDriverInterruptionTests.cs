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
    /// Pins a label change that interrupts a running spring or bezier swap on the same driver: it continues from
    /// the value each channel is drawn at, as Framer Motion animates every value from the one it has, and a spring
    /// keeps that channel's velocity too. A spring its duration describes takes none of it, as Framer starts one
    /// with zero velocity.
    /// </summary>
    internal sealed class MotionDriverInterruptionTests
    {
        private readonly record struct LabelState(string Label);

        private sealed class LabelStore : Store<LabelState>
        {
            public LabelStore() : base(new LabelState("a")) { }
            public void Set(string label) => SetState(_ => new LabelState(label));
            protected override void ResetCore() => SetState(_ => new LabelState("a"));
        }

        private static LabelStore s_labels;
        private static Dictionary<string, MotionVariant> s_variants;
        private static StyleTransitionConfig s_transition;

        private EditorPanelSimulator _sim;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            s_labels = null;
            s_variants = null;
            s_transition = null;
        }

        [TearDown]
        public void TearDown()
        {
            s_labels?.Dispose();
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
        private static VNode Box()
        {
            var label = Hooks.UseStore(s_labels, s => s.Label);
            return V.Motion(key: "m", name: "m", variants: s_variants, animate: label, transition: s_transition);
        }

        private static readonly StyleTransitionConfig s_physics = new()
        {
            Type = TransitionType.Spring, Stiffness = 170f, Damping = 26f,
        };

        private static readonly StyleTransitionConfig s_bezier = new() { Type = TransitionType.Bezier, DurationSec = 0.3f };

        private static float Read(VisualElement m, string channel) => channel switch
        {
            "opacity" => m.style.opacity.value,
            "translate-x" => m.style.translate.value.x.value,
            "translate-y" => m.style.translate.value.y.value,
            "scale" => m.style.scale.value.value.x,
            "rotate" => m.style.rotate.value.angle.ToDegrees(),
            "width" => m.style.width.value.value,
            "background-red" => m.style.backgroundColor.value.r,
            _ => float.NaN,
        };

        // Mounts at pose a, starts the spring to pose b, and lets it run six frames.
        private (MountedTree Mounted, VisualElement Motion) MidwayToB(string from, string to,
            StyleTransitionConfig transition)
        {
            s_labels = new LabelStore();
            s_variants = new Dictionary<string, MotionVariant> { ["a"] = from, ["b"] = to };
            s_transition = transition;
            var mounted = V.Mount(Root, V.Component(Box, key: "root"));
            Ticks(1);
            s_labels.Set("b");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            Ticks(6);
            return (mounted, Root.Q<VisualElement>("m"));
        }

        private static void FlipBackToA(MountedTree mounted)
        {
            s_labels.Set("a");
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        [TestCase(TransitionType.Spring, "opacity-0", "opacity-100", "opacity", 0f, 1f)]
        [TestCase(TransitionType.Spring, "translate-x-[0px]", "translate-x-[100px]", "translate-x", 0f, 100f)]
        [TestCase(TransitionType.Spring, "translate-y-[0px]", "translate-y-[100px]", "translate-y", 0f, 100f)]
        [TestCase(TransitionType.Spring, "scale-50", "scale-100", "scale", 0.5f, 1f)]
        [TestCase(TransitionType.Spring, "rotate-45", "rotate-n45", "rotate", -45f, 45f)]
        [TestCase(TransitionType.Spring, "w-[0px]", "w-[100px]", "width", 0f, 100f)]
        [TestCase(TransitionType.Spring, "bg-[#000000]", "bg-[#ffffff]", "background-red", 0f, 1f)]
        [TestCase(TransitionType.Bezier, "opacity-0", "opacity-100", "opacity", 0f, 1f)]
        [TestCase(TransitionType.Bezier, "translate-x-[0px]", "translate-x-[100px]", "translate-x", 0f, 100f)]
        [TestCase(TransitionType.Bezier, "translate-y-[0px]", "translate-y-[100px]", "translate-y", 0f, 100f)]
        [TestCase(TransitionType.Bezier, "scale-50", "scale-100", "scale", 0.5f, 1f)]
        [TestCase(TransitionType.Bezier, "rotate-45", "rotate-n45", "rotate", -45f, 45f)]
        [TestCase(TransitionType.Bezier, "w-[0px]", "w-[100px]", "width", 0f, 100f)]
        [TestCase(TransitionType.Bezier, "bg-[#000000]", "bg-[#ffffff]", "background-red", 0f, 1f)]
        public void Given_ADrivenMotionMidwayToItsLabel_When_TheLabelFlipsBack_Then_ItContinuesFromTheValueItWasDrawnAt(
            TransitionType driver, string from, string to, string channel, float low, float high)
        {
            // Arrange
            var (mounted, m) = MidwayToB(from, to, driver == TransitionType.Spring ? s_physics : s_bezier);
            using var _ = mounted;
            var drawn = Read(m, channel);

            // Act
            FlipBackToA(mounted);

            // Assert — drawn strictly between the two poses before the flip, and still there after it.
            var midway = drawn > low + 0.01f * (high - low) && drawn < high - 0.01f * (high - low);
            Assert.That(midway ? Read(m, channel) : float.NaN, Is.EqualTo(drawn).Within(1e-3f * (high - low)));
        }

        [Test]
        public void Given_ASpringMotionRisingTowardItsLabel_When_TheLabelFlipsBack_Then_ItKeepsRisingForAFrame()
        {
            // Arrange — rising toward 1, six frames in.
            var (mounted, m) = MidwayToB("opacity-0", "opacity-100", s_physics);
            using var _ = mounted;
            var drawn = m.style.opacity.value;

            // Act
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — carried, its velocity lifts it a little further for a frame; started from rest it would
            // already be falling, and started from pose b it would be near 1.
            Assert.That(m.style.opacity.value - drawn, Is.InRange(0.01f, 0.2f));
        }

        [Test]
        public void Given_ASpringItsDurationDescribesRisingTowardItsLabel_When_TheLabelFlipsBack_Then_ItFallsFromTheNextFrame()
        {
            // Arrange
            var (mounted, m) = MidwayToB("opacity-0", "opacity-100",
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f });
            using var _ = mounted;
            var drawn = m.style.opacity.value;

            // Act
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — started with no velocity, it moves toward 0 at once; it was drawn midway beforehand.
            var midway = drawn > 0.01f && drawn < 0.99f;
            Assert.That(midway ? m.style.opacity.value - drawn : float.NaN, Is.InRange(-0.2f, -0.0001f));
        }
    }
}
