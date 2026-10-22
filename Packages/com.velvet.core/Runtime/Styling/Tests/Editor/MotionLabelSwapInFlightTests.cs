using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

        [Test]
        public void Given_ANativeOpacityTransitionInFlight_When_TheLabelSwaps_Then_TheNewPlayStartsFromThePaint()
        {
            // Arrange
            var element = MountMidwayThroughANativeFade();
            var paint = element.resolvedStyle.opacity;
            var from = new[] { "opacity-100" };
            var to = new[] { "opacity-50" };

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f,
                MotionSlotContext.Read(element, from, to, _ => false));

            // Assert — NaN unless the transition is part of the way, so a paint at either end cannot pass by
            // meeting the swapped-out value.
            Assert.That(paint > 0.01f && paint < 0.99f ? plan.Opacity?.from ?? float.NaN : float.NaN,
                Is.EqualTo(paint).Within(1e-5f));
        }

        // The transition the native test above builds, a linear 2 s fade from 1 to 0 a quarter of the way in.
        private VisualElement MountMidwayThroughANativeFade()
        {
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
            return element;
        }

        // The fade moves at -0.5 per second; the spring toward 0.5 from the painted 0.75 has negative travel, which
        // the accelerated driver signs the velocity by.
        [Test]
        public void Given_ANativeOpacityTransitionInFlight_When_ASpringLabelSwapsIt_Then_TheSpringStartsWithTheTransitionsVelocity()
        {
            // Arrange
            var element = MountMidwayThroughANativeFade();
            var scheduler = new StyleAnimationScheduler();

            // Act
            scheduler.PlayVariantEnter(element, new[] { "opacity-100" }, new[] { "opacity-50" },
                new StyleTransitionConfig { Type = TransitionType.Spring });
            var enters = (IDictionary)typeof(StyleAnimationScheduler)
                .GetField("_pendingEnters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scheduler);
            var pending = enters[element];
            var spring = (MotionSpringState)pending?.GetType().GetField("Spring")!.GetValue(pending);
            var startVelocity = spring?.Opacity?.StartVelocity ?? float.NaN;
            scheduler.CancelAll();

            // Assert
            Assert.That(startVelocity, Is.EqualTo(0.5f).Within(1e-3f));
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
