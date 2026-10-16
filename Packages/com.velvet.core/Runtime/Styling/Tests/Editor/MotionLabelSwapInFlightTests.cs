using System.Collections.Generic;
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

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AnOpacityPlayInFlight_When_TheLabelSwapsAwayFromItsTarget_Then_TheNewPlayStartsFromTheFrame(TransitionType type)
        {
            // Arrange
            var element = Mount();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" }, 1f, EasingMode.Linear, 0f, type: type);
            element.style.opacity = 0.4f;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-50" }, 1f, EasingMode.Linear, 0f, type: type);
            var start = element.style.opacity.value;
            scheduler.CancelAll();

            // Assert
            Assert.That(start, Is.EqualTo(0.4f).Within(1e-5f));
        }

        [TestCase(TransitionType.Spring)]
        [TestCase(TransitionType.Bezier)]
        public void Given_AWidthPlayInFlight_When_TheLabelSwapsAwayFromItsTarget_Then_TheNewPlayStartsFromTheFrame(TransitionType type)
        {
            // Arrange
            var element = Mount();
            var scheduler = new StyleAnimationScheduler();
            scheduler.PlayVariantEnter(element, new[] { "w-[10px]" }, new[] { "w-[100px]" }, 1f, EasingMode.Linear, 0f, type: type);
            element.style.width = 40f;

            // Act
            scheduler.PlayVariantEnter(element, new[] { "w-[100px]" }, new[] { "w-[200px]" }, 1f, EasingMode.Linear, 0f, type: type);
            var start = element.style.width.value.value;
            scheduler.CancelAll();

            // Assert
            Assert.That(start, Is.EqualTo(40f).Within(1e-4f));
        }

        [Test]
        public void Given_ANativeOpacityTransitionInFlight_When_TheLabelSwaps_Then_TheNewPlayStartsFromThePaint()
        {
            // Arrange
            var element = Mount();
            var now = 100.0;
            element.style.opacity = 1f;
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.SetPanelTimeFunction(element.panel, () => now);
            element.style.transitionProperty = new List<StylePropertyName> { new("opacity") };
            element.style.transitionDuration = new List<TimeValue> { new(2f, TimeUnit.Second) };
            element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
            ForcePanelUpdate(element.panel);
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            element.style.opacity = 0f;
            ForcePanelUpdate(element.panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(element.panel);
            ForcePanelUpdate(element.panel);
            var paint = element.resolvedStyle.opacity;
            var from = new[] { "opacity-100" };
            var to = new[] { "opacity-50" };

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f,
                MotionSlotContext.Read(element, from, to, _ => false));

            // Assert
            Assert.That((plan.Opacity?.from == paint, paint > 0.01f && paint < 0.99f), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base starts a slot no play drives from the swapped-out value too.
        [Test]
        public void Given_AnOpacityNoPlayDrives_When_TheLabelSwaps_Then_TheNewPlayStartsFromTheSwappedOutValue()
        {
            // Arrange
            var element = Mount();
            element.style.opacity = 0.4f;
            var from = new[] { "opacity-100" };
            var to = new[] { "opacity-50" };

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f,
                MotionSlotContext.Read(element, from, to, _ => false));

            // Assert
            Assert.That(plan.Opacity?.from, Is.EqualTo(1f));
        }
    }
}
