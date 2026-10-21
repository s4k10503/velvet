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
    /// keeps that channel's velocity too, a spring its duration describes included.
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

        // Mounts at pose a, starts the play to pose b, and lets it run six frames.
        private (MountedTree Mounted, VisualElement Motion) MidwayToB(string from, string to,
            StyleTransitionConfig transition)
            => MidwayToB(new MotionVariant(from), new MotionVariant(to), transition);

        private (MountedTree Mounted, VisualElement Motion) MidwayToB(MotionVariant from, MotionVariant to,
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

        // GREEN_ON_BASE(characterization): the base already stays near the value drawn when the same driver flips the label back.
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

            // Act — the flip and one frame of the play it starts, which moves a few hundredths of the travel at most.
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — drawn strictly between the two poses before the flip, and still near it a frame after; a play
            // restarted from pose b would be most of the travel away.
            var midway = drawn > low + 0.01f * (high - low) && drawn < high - 0.01f * (high - low);
            Assert.That(midway ? Read(m, channel) : float.NaN, Is.EqualTo(drawn).Within(0.1f * (high - low)));
        }

        // GREEN_ON_BASE(characterization): the base already moves this channel further for one frame after the flip.
        [Test]
        public void Given_ASpringMotionMovingTowardItsLabel_When_TheLabelFlipsBack_Then_ItKeepsMovingThatWayForAFrame()
        {
            // Arrange — a translate, which Framer keeps on its main thread with its own velocity, six frames in.
            var (mounted, m) = MidwayToB("translate-x-[0px]", "translate-x-[100px]", s_physics);
            using var _ = mounted;
            var drawn = Read(m, "translate-x");

            // Act
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — carried, its velocity takes it a little further for a frame; started from rest it would
            // already be falling, and started from pose b it would be near 100.
            Assert.That(Read(m, "translate-x") - drawn, Is.InRange(0.01f, 20f));
        }

        // The play to pose b runs on one driver and the one back to pose a on the other: each pose names its own.
        // GREEN_ON_BASE(characterization): the base already stays near the value drawn when the return play uses the other driver.
        [TestCase(TransitionType.Bezier, "opacity-0", "opacity-100", "opacity", 0f, 1f)]
        [TestCase(TransitionType.Bezier, "translate-x-[0px]", "translate-x-[100px]", "translate-x", 0f, 100f)]
        [TestCase(TransitionType.Bezier, "w-[0px]", "w-[100px]", "width", 0f, 100f)]
        [TestCase(TransitionType.Bezier, "bg-[#000000]", "bg-[#ffffff]", "background-red", 0f, 1f)]
        [TestCase(TransitionType.Spring, "opacity-0", "opacity-100", "opacity", 0f, 1f)]
        [TestCase(TransitionType.Spring, "translate-x-[0px]", "translate-x-[100px]", "translate-x", 0f, 100f)]
        [TestCase(TransitionType.Spring, "w-[0px]", "w-[100px]", "width", 0f, 100f)]
        [TestCase(TransitionType.Spring, "bg-[#000000]", "bg-[#ffffff]", "background-red", 0f, 1f)]
        public void Given_AMotionMidwayToItsLabelOnOneDriver_When_TheLabelFlipsBackOnTheOther_Then_ItContinuesFromTheValueItWasDrawnAt(
            TransitionType toB, string from, string to, string channel, float low, float high)
        {
            // Arrange
            var (toBTransition, backTransition) = toB == TransitionType.Bezier ? (s_bezier, s_physics) : (s_physics, s_bezier);
            var (mounted, m) = MidwayToB(new MotionVariant(from, backTransition), new MotionVariant(to, toBTransition),
                toBTransition);
            using var _ = mounted;
            var drawn = Read(m, channel);

            // Act — the flip and one frame of the play it starts.
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — as the same-driver case above, at twice its margin: a spring back from a bezier starts with
            // the curve's slope, which six frames into this one is steeper than a spring's at that point and takes
            // a translate or a width about a tenth of the travel in one frame. A restart from pose b is still most
            // of the travel away.
            var midway = drawn > low + 0.01f * (high - low) && drawn < high - 0.01f * (high - low);
            Assert.That(midway ? Read(m, channel) : float.NaN, Is.EqualTo(drawn).Within(0.2f * (high - low)));
        }

        // GREEN_ON_BASE(characterization): the base already moves this channel further for one frame when a spring interrupts the bezier.
        [Test]
        public void Given_AMotionMovingTowardItsLabelOnABezier_When_TheLabelFlipsBackOnASpring_Then_ItKeepsMovingThatWayForAFrame()
        {
            // Arrange — the spring back takes the velocity of the bezier's curve where it stands.
            var (mounted, m) = MidwayToB(new MotionVariant("translate-x-[0px]", s_physics),
                new MotionVariant("translate-x-[100px]", s_bezier), s_bezier);
            using var _ = mounted;
            var drawn = Read(m, "translate-x");

            // Act
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — started at rest it would already be falling, and started from pose b it would be near 100.
            Assert.That(Read(m, "translate-x") - drawn, Is.InRange(0.01f, 20f));
        }

        // GREEN_ON_BASE(characterization): the base ignores a duration, so this is its physics spring, which already keeps moving for a frame.
        [Test]
        public void Given_ASpringItsDurationDescribesMovingTowardItsLabel_When_TheLabelFlipsBack_Then_ItKeepsMovingThatWayForAFrame()
        {
            // Arrange — Framer's getSpringOptions keeps the value's velocity for a spring its duration describes.
            var (mounted, m) = MidwayToB("translate-x-[0px]", "translate-x-[100px]",
                new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f });
            using var _ = mounted;
            var drawn = Read(m, "translate-x");

            // Act
            FlipBackToA(mounted);
            Ticks(1);

            // Assert — as the case above; started with no velocity it would already be falling.
            Assert.That(Read(m, "translate-x") - drawn, Is.InRange(0.01f, 20f));
        }
    }
}
