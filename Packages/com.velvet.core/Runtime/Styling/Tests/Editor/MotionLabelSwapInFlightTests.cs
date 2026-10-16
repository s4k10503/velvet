using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class MotionLabelSwapInFlightTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Mount()
        {
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(element.panel);
            return element;
        }

        // A play whose negative delay has it start already into its run: it is running, and has written a frame
        // inline, before any tick, so the frame the next label swap reads is the play's own.
        private static StyleTransitionConfig PlayAlreadyUnderway(TransitionType type) => type == TransitionType.Spring
            ? new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = -0.15f }
            : new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
                DurationSec = 1f, DelaySec = -0.15f,
            };

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AnOpacityPlayInFlight_When_TheLabelSwapsAwayFromItsTarget_Then_TheNewPlayStartsFromTheFrame(TransitionType type)
        {
            // Arrange
            var element = Mount();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, PlayAlreadyUnderway(type));
            var frame = element.style.opacity.value;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-50" }, 1f, EasingMode.Linear, 0f, type: type);
            var start = element.style.opacity.value;
            scheduler.CancelAll();

            // Assert — NaN unless the first play's frame sits between both of its ends, so a play that wrote none
            // cannot pass by starting from the swapped-out value.
            Assert.That(frame > 0.01f && frame < 0.99f ? start : float.NaN, Is.EqualTo(frame).Within(1e-5f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AWidthPlayInFlight_When_TheLabelSwapsAwayFromItsTarget_Then_TheNewPlayStartsFromTheFrame(TransitionType type)
        {
            // Arrange
            var element = Mount();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "w-[10px]" }, new[] { "w-[100px]" }, PlayAlreadyUnderway(type));
            var frame = element.style.width.value.value;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "w-[100px]" }, new[] { "w-[200px]" }, 1f, EasingMode.Linear, 0f, type: type);
            var start = element.style.width.value.value;
            scheduler.CancelAll();

            // Assert — NaN unless the first play's frame sits between both of its ends.
            Assert.That(frame > 10.5f && frame < 99.5f ? start : float.NaN, Is.EqualTo(frame).Within(1e-4f));
        }

        // GREEN_ON_BASE(characterization): the base starts a slot no play drives from the swapped-out value too, and
        // the panel shows opacity 1 where that value is 0.75, so a start read off the screen would read 1.
        [Test]
        public void Given_AnOpacityNoPlayDrives_When_TheLabelSwaps_Then_TheNewPlayStartsFromTheSwappedOutValue()
        {
            // Arrange
            var element = Mount();
            var from = new[] { "opacity-75" };
            var to = new[] { "opacity-50" };

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f,
                MotionSlotContext.Read(element, from, to, _ => false));

            // Assert
            Assert.That(plan.Opacity?.from, Is.EqualTo(0.75f).Within(1e-5f));
        }
    }
}
