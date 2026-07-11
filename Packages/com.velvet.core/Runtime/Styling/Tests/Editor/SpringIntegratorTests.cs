using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="SpringIntegrator"/>'s pure physics: it converges to its target given enough time, an
    /// underdamped configuration overshoots before settling (Framer Motion's spring is underdamped by default),
    /// and retargeting an in-flight spring carries its CURRENT value/velocity forward instead of resetting them
    /// (the continuity an interrupted AnimatePresence exit/enter needs). No panel or VisualElement involved —
    /// this is deterministic math, driven directly. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class SpringIntegratorTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        [Test]
        public void Given_ACriticallyDampedSpring_When_SteppedForEnoughTime_Then_ItSettlesAtTheTarget()
        {
            // Arrange — stiffness 100 / mass 1 critically damps at damping = 2*sqrt(stiffness*mass) = 20 (no
            // ringing, the fastest non-oscillating approach).
            var spring = new SpringIntegrator(initialValue: 0f);
            const float target = 100f;

            // Act — 180 ticks (3 simulated seconds) is comfortably past this spring's settle time.
            for (var i = 0; i < 180; i++)
            {
                spring.Step(FixedDeltaSec, target, stiffness: 100f, damping: 20f, mass: 1f);
            }

            // Assert
            Assert.That(spring.Value, Is.EqualTo(target).Within(0.5f));
        }

        [Test]
        public void Given_AnUnderdampedSpring_When_SteppedTowardATarget_Then_ItOvershootsBeforeSettling()
        {
            // Arrange — damping (2) sits well below critical (20 for this stiffness/mass), so the spring rings
            // past its target before settling instead of approaching it monotonically.
            var spring = new SpringIntegrator(initialValue: 0f);
            const float target = 100f;
            var peakValue = float.NegativeInfinity;

            // Act — step long enough to pass through and beyond the target at least once.
            for (var i = 0; i < 180; i++)
            {
                spring.Step(FixedDeltaSec, target, stiffness: 100f, damping: 2f, mass: 1f);
                if (spring.Value > peakValue)
                {
                    peakValue = spring.Value;
                }
            }

            // Assert
            Assert.That(peakValue, Is.GreaterThan(target));
        }

        [Test]
        public void Given_ASpringMidFlightTowardOneTarget_When_RetargetedToADifferentValue_Then_TheNextStepContinuesFromItsCurrentValueAndVelocity()
        {
            // Arrange — run partway toward 100 so the spring has accumulated a nonzero value/velocity, then
            // capture that state right before retargeting.
            var spring = new SpringIntegrator(initialValue: 0f);
            for (var i = 0; i < 10; i++)
            {
                spring.Step(FixedDeltaSec, 100f, stiffness: 100f, damping: 10f, mass: 1f);
            }
            var capturedValue = spring.Value;
            var capturedVelocity = spring.Velocity;
            Assume.That(capturedVelocity, Is.Not.EqualTo(0f), "Precondition: the spring has built up velocity heading toward the first target");

            // Act — retarget to a wildly different value (mirrors an exit-cancel reversing back toward the
            // resting value) and take one more step; a FRESH spring seeded with the exact captured state is the
            // reference for what one step from "here" should produce with nothing reset.
            spring.Step(FixedDeltaSec, -50f, stiffness: 100f, damping: 10f, mass: 1f);
            var reference = new SpringIntegrator(capturedValue, capturedVelocity);
            reference.Step(FixedDeltaSec, -50f, stiffness: 100f, damping: 10f, mass: 1f);

            // Assert — the retargeted spring's value matches the reference exactly: the retarget carried the
            // SAME value/velocity forward (no discontinuity) rather than resetting either.
            Assert.That(spring.Value, Is.EqualTo(reference.Value).Within(1e-6f));
        }
    }
}
