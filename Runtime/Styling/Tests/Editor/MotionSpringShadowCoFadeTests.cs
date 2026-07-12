using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Extends <c>ShadowAnimationVisibilityTests</c>' co-fade coverage to the spring path. A spring-driven
    /// Motion enter/exit writes its animated opacity as a per-frame inline style tick (StartSpringTick), not a
    /// CSS transition, but the drop-shadow paint is still opacity-blind either way, so it still needs the SAME
    /// co-fade (CollectShadowsForCoFade + StartShadowCoFadeTick) the tween paths already get — the tick samples
    /// resolvedStyle.opacity and does not care what is driving it. Needs a real (simulated) panel: the co-fade
    /// tick's sampling requires resolvedStyle, which <c>ShadowAnimationVisibilityTests</c>' panel-free setup
    /// cannot resolve — exactly why a missing co-fade wire on the spring path was invisible to that suite.
    /// </summary>
    [TestFixture]
    internal sealed class MotionSpringShadowCoFadeTests
    {
        private static readonly ShadowSpec Spec =
            new(new Color(0f, 0f, 0f, 0.3f), blur: 20f, offsetY: 4f, spread: 0f);

        private EditorPanelSimulator _sim;
        private StyleAnimationScheduler _scheduler;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _scheduler = new StyleAnimationScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        // Attaches a shadow paint to a child of target and returns the child's binding, mirroring
        // ShadowAnimationVisibilityTests' own helper.
        private DropShadowBinding AttachShadowChild(VisualElement target)
        {
            var child = new VisualElement();
            target.Add(child);
            return DropShadowSilhouette.Attach(child, Spec, System.Array.Empty<string>(), skewXDeg: 0f);
        }

        [Test]
        public void Given_AShadowedSpringEnter_When_APanelTickRunsMidFlight_Then_TheDescendantShadowIsCoFading()
        {
            // Arrange — a spring-driven enter (opacity 0 -> 100) on a target carrying a shadow-painted child.
            var target = new VisualElement();
            Root.Add(target);
            var binding = AttachShadowChild(target);
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                EnterFromClass = "opacity-0",
                EnterToClass = "opacity-100",
            };

            // Act — a few ticks start the spring and let it climb without fully settling.
            _scheduler.PlayEnter(target, config);
            Tick();
            Tick();
            Tick();
            Assume.That(target.resolvedStyle.opacity, Is.LessThan(1f),
                "Precondition: the caster is still mid-climb, not yet settled at full opacity");

            // Assert — an un-cofaded shadow would sit stuck at its resting full strength; the co-fade tick must
            // have already pulled it down alongside the still-translucent caster.
            Assert.That(binding.ShadowOpacity, Is.LessThan(1f));
        }

        [Test]
        public void Given_AShadowedSpringExit_When_APanelTickRunsMidFlight_Then_TheDescendantShadowIsCoFading()
        {
            // Arrange — a spring-driven exit (opacity 100 -> 0) on a target carrying a shadow-painted child.
            var target = new VisualElement();
            Root.Add(target);
            var binding = AttachShadowChild(target);
            target.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };

            // Act
            _scheduler.PlayExit(target, config, onComplete: null);
            Tick();
            Tick();
            Tick();
            Assume.That(target.resolvedStyle.opacity, Is.GreaterThan(0f),
                "Precondition: the caster is still mid-fade, not yet settled at zero opacity");

            // Assert — the shadow must be following the caster's fade down, not sitting untouched at full.
            Assert.That(binding.ShadowOpacity, Is.LessThan(1f));
        }
    }
}
