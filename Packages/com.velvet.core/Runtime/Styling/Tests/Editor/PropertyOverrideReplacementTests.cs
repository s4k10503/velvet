using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a <see cref="StylePropertyTransition"/> replacing the enclosing config for its property, as Framer
    /// Motion's per-value transition does: only its delay comes from the enclosing config, and a duration or easing
    /// it leaves out takes Framer's keyframes defaults.
    /// </summary>
    [TestFixture]
    internal sealed class PropertyOverrideReplacementTests
    {
        private static TweenChannelTiming? OpacityTiming(params StylePropertyTransition[] overrides)
            => new DrivenTweenTiming(0f, 1f, EasingMode.Linear, overrides).For(StyleLonghandSet.Of(StyleLonghand.Opacity));

        [Test]
        public void Given_AnEntryNamingOnlyADelay_When_ItsChannelTimingIsRead_Then_ItRunsThreeTenthsOfASecond()
        {
            // Arrange
            var entry = new StylePropertyTransition("opacity", delaySec: 0.1f);

            // Act
            var timing = OpacityTiming(entry);

            // Assert
            Assert.That(timing?.DurationSec ?? float.NaN, Is.EqualTo(0.3f));
        }

        [Test]
        public void Given_AnEntryNamingOnlyADuration_When_ItsChannelTimingIsRead_Then_ItEasesOut()
        {
            // Arrange
            var entry = new StylePropertyTransition("opacity", durationSec: 0.5f);

            // Act
            var timing = OpacityTiming(entry);

            // Assert
            Assert.That(timing is { } own ? own.Easing : (EasingMode)(-1), Is.EqualTo(EasingMode.EaseOut));
        }

        [Test]
        public void Given_ABezierPlayWhoseEntryNamesALongerDelay_When_ItsOverridesEndIsRead_Then_TheEntrysDelayReplacesThePlays()
        {
            // Arrange — the entry starts 0.3s in, 0.2s after the play's own 0.1s delay, and runs 0.5s.
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = 0.1f,
                PropertyOverrides = new[] { new StylePropertyTransition("opacity", durationSec: 0.5f, delaySec: 0.3f) },
            };

            // Act
            var end = MotionPlaySpan.OverridesEndSec(config);

            // Assert
            Assert.That(end, Is.EqualTo(0.7).Within(1e-6));
        }
    }
}
