using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <c>StyleTransitionConfig.ScaledBy</c>, the transition an animation sequence hands its Motion at a
    /// speed other than 1: what it divides, what it multiplies, and that it carries every other settable property
    /// across unchanged — a property added to the config later is a case of that last test without anyone writing
    /// one.
    /// </summary>
    internal sealed class StyleTransitionConfigScaledByTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] TimeSpans =
        {
            nameof(StyleTransitionConfig.DurationSec),
            nameof(StyleTransitionConfig.DelaySec),
            nameof(StyleTransitionConfig.StaggerChildrenSec),
            nameof(StyleTransitionConfig.DelayChildrenSec),
        };

        private static readonly string[] Recomputed = TimeSpans.Concat(new[]
        {
            nameof(StyleTransitionConfig.PropertyOverrides),
            nameof(StyleTransitionConfig.Layout),
            nameof(StyleTransitionConfig.Stiffness),
            nameof(StyleTransitionConfig.Damping),
        }).ToArray();

        private static IEnumerable<string> CopiedProperties() => typeof(StyleTransitionConfig)
            .GetProperties(Instance)
            .Where(property => property.CanWrite && !Recomputed.Contains(property.Name))
            .Select(property => property.Name);

        // A value differing from the property's default, so a copy that dropped it reads the default instead.
        private static object SampleOf(Type type)
        {
            if (type == typeof(float)) return 0.37f;
            if (type == typeof(string)) return "sample-class";
            if (type == typeof(TransitionType)) return TransitionType.Bezier;
            if (type == typeof(EasingMode)) return EasingMode.Linear;
            if (type == typeof(EasingMode?)) return EasingMode.EaseInOut;
            if (type == typeof(TransitionWhen)) return TransitionWhen.BeforeChildren;
            throw new NotSupportedException($"No sample value for a {type}: add one, and decide whether ScaledBy copies it.");
        }

        private static StyleTransitionConfig Spring(float stiffness, float damping, float mass)
            => new() { Type = TransitionType.Spring, Stiffness = stiffness, Damping = damping, Mass = mass };

        [TestCaseSource(nameof(CopiedProperties))]
        public void Given_AConfigWithThisPropertySet_When_ScaledByTwo_Then_TheCopyCarriesItUnchanged(string name)
        {
            // Arrange
            var property = typeof(StyleTransitionConfig).GetProperty(name, Instance);
            var sample = SampleOf(property.PropertyType);
            var config = new StyleTransitionConfig();
            property.SetValue(config, sample);

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(property.GetValue(scaled), Is.EqualTo(sample));
        }

        [TestCaseSource(nameof(TimeSpans))]
        public void Given_ATimeSpanOfEightTenthsOfASecond_When_ScaledByTwo_Then_ItIsHalved(string name)
        {
            // Arrange
            var property = typeof(StyleTransitionConfig).GetProperty(name, Instance);
            var config = new StyleTransitionConfig();
            property.SetValue(config, 0.8f);

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That((float)property.GetValue(scaled), Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void Given_ARateOfOne_When_Scaled_Then_TheSameInstanceComesBack()
        {
            // Arrange
            var config = new StyleTransitionConfig { DurationSec = 0.8f };

            // Act
            var scaled = config.ScaledBy(1f);

            // Assert
            Assert.That(scaled, Is.SameAs(config));
        }

        [Test]
        public void Given_APropertyOverride_When_ScaledByTwo_Then_ItsDurationIsHalved()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.8f) },
            };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides[0].DurationSec.Value, Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void Given_APropertyOverride_When_ScaledByTwo_Then_ItsDelayIsHalved()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", delaySec: 0.6f) },
            };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides[0].DelaySec.Value, Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void Given_APropertyOverride_When_ScaledByTwo_Then_ItsPropertyIsCarried()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.8f) },
            };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides[0].Property, Is.EqualTo("opacity"));
        }

        [Test]
        public void Given_APropertyOverride_When_ScaledByTwo_Then_ItsEasingIsCarried()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", easing: EasingMode.Linear) },
            };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides[0].Easing, Is.EqualTo(EasingMode.Linear));
        }

        [Test]
        public void Given_APropertyOverrideWithNoDurationOfItsOwn_When_ScaledByTwo_Then_ItStillHasNone()
        {
            // Arrange — a null duration falls back to the top-level one, which is scaled on its own.
            var config = new StyleTransitionConfig
            {
                DurationSec = 0.8f,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity") },
            };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides[0].DurationSec, Is.Null);
        }

        [Test]
        public void Given_NoPropertyOverrides_When_ScaledByTwo_Then_ThereAreStillNone()
        {
            // Arrange
            var config = new StyleTransitionConfig { DurationSec = 0.8f };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.PropertyOverrides, Is.Null);
        }

        [Test]
        public void Given_ALayoutTransition_When_ScaledByTwo_Then_ItsDurationIsHalved()
        {
            // Arrange
            var config = new StyleTransitionConfig { Layout = new StyleTransitionConfig { DurationSec = 0.8f } };

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.Layout.DurationSec, Is.EqualTo(0.4f).Within(1e-6f));
        }

        [Test]
        public void Given_ASpring_When_ScaledByTwo_Then_ItsStiffnessIsQuadrupled()
        {
            // Arrange
            var config = Spring(100f, 10f, 2f);

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.Stiffness, Is.EqualTo(400f).Within(1e-3f));
        }

        [Test]
        public void Given_ASpring_When_ScaledByTwo_Then_ItsDampingIsDoubled()
        {
            // Arrange
            var config = Spring(100f, 10f, 2f);

            // Act
            var scaled = config.ScaledBy(2f);

            // Assert
            Assert.That(scaled.Damping, Is.EqualTo(20f).Within(1e-4f));
        }

        // Stiffness and damping together are what make the scaled spring the same trajectory on a faster
        // clock, so this reddens when either of them is left unscaled.
        [Test]
        public void Given_ASpring_When_ScaledByTwo_Then_HalfwayThroughTheOriginalsTimeItIsWhereTheOriginalIs()
        {
            // Arrange
            var config = Spring(100f, 10f, 2f);
            var original = SpringIntegrator.Solve(100.0, 0.0, 0.3, (config.Stiffness, config.Damping, config.Mass));

            // Act
            var scaled = config.ScaledBy(2f);
            var faster = SpringIntegrator.Solve(100.0, 0.0, 0.15, (scaled.Stiffness, scaled.Damping, scaled.Mass));

            // Assert
            Assert.That(faster.Displacement, Is.EqualTo(original.Displacement).Within(1e-6));
        }
    }
}
