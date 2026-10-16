using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <see cref="StyleAnimationScheduler"/> hands a running spring's velocity to the spring that interrupts
    /// it on the same element, and a running bezier play's likewise, as Framer Motion starts a value's next animation with the motion value's velocity.
    /// </summary>
    [TestFixture]
    internal sealed class SpringInterruptionSchedulerTests : MotionSimulatedPanelTestsBase
    {
        private StyleAnimationScheduler _scheduler;

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            _scheduler = null;
            base.TearDown();
        }

        // The spring the scheduler is running for the element's enter, read from its private bookkeeping.
        private MotionSpringState RunningEnterSpring(VisualElement element)
        {
            var enters = (IDictionary)typeof(StyleAnimationScheduler)
                .GetField("_pendingEnters", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(_scheduler);
            var pending = enters[element];
            return (MotionSpringState)pending?.GetType().GetField("Spring")!.GetValue(pending);
        }

        [Test]
        public void Given_AnOpacitySpringRisingPartWay_When_ALabelChangeSendsItBack_Then_TheNewSpringStartsWithItsVelocity()
        {
            // Arrange — six frames into fading in, the opacity is still rising.
            _scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("opacity-100");
            Root.Add(element);
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring };
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, spring);
            for (var i = 0; i < 6; i++) Tick();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-0" }, spring);

            // Assert — Framer hands the opacity's rising velocity unscaled to the browser easing, as progress toward
            // opacity 0, which shrinks the displacement from that target the driver's start velocity measures: below
            // zero, where a spring started at rest has zero. NaN when no spring took over.
            var startVelocity = RunningEnterSpring(element)?.Opacity?.StartVelocity ?? float.NaN;
            Assert.That(startVelocity, Is.LessThan(0f));
        }

        private static StyleTransitionConfig LinearBezier() => new()
        {
            Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f, DurationSec = 1f,
        };

        // Six frames into a linear second of fading in, the opacity is still rising at one per second.
        private (VisualElement Element, MotionPlayback Playback) RisingBezier()
        {
            _scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("opacity-100");
            Root.Add(element);
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, LinearBezier(),
                playback: playback);
            for (var i = 0; i < 6; i++) Tick();
            return (element, playback);
        }

        private float SpringStartVelocityAfterALabelChangeBack(VisualElement element)
        {
            _scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-0" },
                new StyleTransitionConfig { Type = TransitionType.Spring });
            return RunningEnterSpring(element)?.Opacity?.StartVelocity ?? float.NaN;
        }

        // Linear (0, 0, 1, 1) over one second moves at 1 per second, which the accelerated driver signs by the
        // spring's travel, here toward 0. NaN when no spring took over.
        [Test]
        public void Given_AnOpacityBezierRisingPartWay_When_ALabelChangeSendsItBackWithASpring_Then_TheNewSpringStartsWithItsVelocity()
        {
            // Arrange
            var (element, _) = RisingBezier();

            // Act
            var startVelocity = SpringStartVelocityAfterALabelChangeBack(element);

            // Assert
            Assert.That(startVelocity, Is.EqualTo(-1f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base hands a bezier's spring no velocity at all, so it starts at rest
        // there as well; the case pins that a paused playback hands none on.
        [Test]
        public void Given_AnOpacityBezierOnAHeldPlayback_When_ALabelChangeSendsItBackWithASpring_Then_TheNewSpringStartsAtRest()
        {
            // Arrange
            var (element, playback) = RisingBezier();
            playback.Rate = 0f;

            // Act
            var startVelocity = SpringStartVelocityAfterALabelChangeBack(element);

            // Assert
            Assert.That(startVelocity, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Given_AnOpacityBezierExitPartWay_When_ALabelSwapsAnEnterOver_Then_TheSpringStartsFromTheExitsFrame()
        {
            // Arrange — six frames into a linear fade out, which the swapped-out opacity-0 is not where it is.
            _scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            Root.Add(element);
            var exit = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
                DurationSec = 1f, ExitFromClass = "opacity-100", ExitToClass = "opacity-0",
            };
            _scheduler.PlayExit(element, exit, null);
            for (var i = 0; i < 6; i++) Tick();
            var frame = element.style.opacity.value;

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                new StyleTransitionConfig { Type = TransitionType.Spring });
            var origin = RunningEnterSpring(element)?.Opacity?.Origin ?? float.NaN;

            // Assert — NaN unless the exit's frame sits between both of its ends.
            Assert.That(frame > 0.01f && frame < 0.99f ? origin : float.NaN, Is.EqualTo(frame).Within(1e-5f));
        }
    }
}
