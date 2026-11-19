using System;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the hold a <c>To</c> step derives from a spring its <see cref="StyleTransitionConfig.DurationSec"/>
    /// describes: the first 50ms sample at or past that duration, which is where Framer Motion's sequence finds
    /// the spring's generator done. Read through <c>SequenceWalker.ResolveHoldFromTransition</c> by reflection,
    /// since a step holding 0.30s against one holding 0.35s is closer than a fake-clock walk resolves.
    /// </summary>
    internal sealed class SequenceSpringDurationHoldTests
    {
        private static float Hold(StyleTransitionConfig transition)
        {
            var method = typeof(StyleTransitionConfig).Assembly.GetType("Velvet.SequenceWalker")
                ?.GetMethod("ResolveHoldFromTransition", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "SequenceWalker.ResolveHoldFromTransition(StyleTransitionConfig) no longer resolves.");
            return (float)method.Invoke(null, new object[] { transition })!;
        }

        [Test]
        public void Given_ASpringDescribedByADurationBetweenSamples_When_ItsHoldIsDerived_Then_ItIsTheNextSample()
        {
            // Arrange — Framer's sequence reports spring({ duration: 310 }) done at 350ms.
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.31f };

            // Act
            var hold = Hold(spring);

            // Assert
            Assert.That(hold, Is.EqualTo(0.35f).Within(1e-6f));
        }

        [Test]
        public void Given_ASpringDescribedByADurationOnASample_When_ItsHoldIsDerived_Then_ItIsThatSample()
        {
            // Arrange — Framer's sequence reports spring({ duration: 300 }) done at 300ms.
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.3f };

            // Act
            var hold = Hold(spring);

            // Assert
            Assert.That(hold, Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void Given_ASpringDescribedByADurationWithADelay_When_ItsHoldIsDerived_Then_ItIsTheDelayPlusTheSample()
        {
            // Arrange
            var spring = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.31f, DelaySec = 0.2f };

            // Act
            var hold = Hold(spring);

            // Assert
            Assert.That(hold, Is.EqualTo(0.55f).Within(1e-6f));
        }
    }
}
