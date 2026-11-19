using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A skewed element a Motion draws keeps its shear while the Motion's channels write the rotate and scale
    /// slots, and a Motion that animates the skew draws the angles into the same slots.
    /// </summary>
    internal sealed class SkewMotionTests
    {
        [Test]
        public void Given_AMotionBesideASkewAndARotateClass_When_Mounted_Then_TheSlotsCarryTheComposite()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(),
                new VNode[] { V.Motion(className: "skew-x-12 rotate-45", name: "card", enterOnMount: false) });
            var card = scope.Root.Q<VisualElement>("card");

            // Assert
            Assert.That(SkewCompositeMath.Drawn(card),
                Is.EqualTo(SkewCompositeMath.Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElementUnderASpringOnRotate_When_Stepped_Then_TheSlotsCarryTheRotateAndTheSkew()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.Engage(element, 12f, 6f);
            var plan = MotionSpringClassParser.Resolve(new[] { "rotate-0" }, new[] { "rotate-90" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 10f, mass: 1f, perValueDefaults: true);

            // Act
            MotionSpringDriver.Step(element, state, 0.1f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(state.Rotate!.Integrator.Value, 1f, 1f, 12f, 6f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewedElementUnderASpringOnScale_When_Stepped_Then_TheSlotsCarryTheScaleAndTheSkew()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.Engage(element, 12f, 6f);
            var plan = MotionSpringClassParser.Resolve(new[] { "scale-50" }, new[] { "scale-150" });
            var state = MotionSpringDriver.Create(plan, stiffness: 100f, damping: 10f, mass: 1f, perValueDefaults: true);

            // Act
            MotionSpringDriver.Step(element, state, 0.1f);

            // Assert
            var factor = state.Scale!.Integrator.Value;
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(0f, factor, factor, 12f, 6f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASpringEnterFromASkewedPose_When_Started_Then_TheSlotsCarryThatShear()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var element = new VisualElement();

            // Act
            ctx.StyleAnimationScheduler.PlayVariantEnter(element, fromClasses: new[] { "skew-x-12" },
                toClasses: new[] { "opacity-100" }, durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                type: TransitionType.Spring, stiffness: 100f, damping: 20f, mass: 1f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element),
                Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }
    }
}
