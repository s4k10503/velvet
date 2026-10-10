using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <see cref="StyleAnimationScheduler"/> hands a running spring's velocity to the spring that interrupts
    /// it on the same element, as Framer Motion starts a value's next animation with the motion value's velocity.
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
    }
}
