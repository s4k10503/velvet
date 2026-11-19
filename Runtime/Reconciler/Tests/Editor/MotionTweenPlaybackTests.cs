using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a <see cref="TransitionType.Tween"/> play started on a <see cref="MotionPlayback"/> on the default
    /// clock: it steps by the playback's time, its pause, rate and delay, and a cancel through the playback stops
    /// it, as <see cref="MotionPlaybackTests"/> pins for a bezier play. A linear one-second play on no playback
    /// stands for the seconds the panel has stepped, which each play on a playback is measured against.
    /// </summary>
    [TestFixture]
    internal sealed class MotionTweenPlaybackTests : MotionSimulatedPanelTestsBase
    {
        private static readonly string[] s_hidden = { "opacity-0" };
        private static readonly string[] s_visible = { "opacity-100" };

        private StyleAnimationScheduler _scheduler;

        public override void SetUp()
        {
            base.SetUp();
            _scheduler = new StyleAnimationScheduler();
        }

        public override void TearDown()
        {
            _scheduler?.CancelAll();
            _scheduler = null;
            base.TearDown();
        }

        private static StyleTransitionConfig LinearTween(float delaySec = 0f) => new()
        {
            DurationSec = 1f, DelaySec = delaySec, Easing = EasingMode.Linear,
        };

        private VisualElement OnPanel(string name)
        {
            var element = new VisualElement { name = name };
            element.AddToClassList("opacity-100");
            Root.Add(element);
            return element;
        }

        private VisualElement StartClock()
        {
            var clock = OnPanel("clock");
            _scheduler.PlayVariantEnter(clock, s_hidden, s_visible, new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f,
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            });
            return clock;
        }

        private void Ticks(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        private static float Opacity(VisualElement element) => element.style.opacity.value;

        [Test]
        public void Given_ATweenPlayAtHalfRate_When_ThePanelTicks_Then_ItHasCoveredHalfTheTimeOfAPlayOnNoPlayback()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("half");
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(),
                playback: new MotionPlayback { Rate = 0.5f });

            // Act
            Ticks(10);

            // Assert
            var elapsed = Opacity(clock);
            Assert.That(elapsed > 0f ? Opacity(element) : float.NaN, Is.EqualTo(elapsed * 0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ATweenPlayPartWayThrough_When_ItsRateIsRaisedToTwo_Then_TheFramesAfterCoverTwiceTheTime()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("raised");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(), playback: playback);
            Ticks(5);
            var clockBefore = Opacity(clock);
            var before = Opacity(element);

            // Act
            playback.Rate = 2f;
            Ticks(5);

            // Assert
            var span = Opacity(clock) - clockBefore;
            Assert.That(span > 0f && before > 0f ? Opacity(element) - before : float.NaN,
                Is.EqualTo(span * 2f).Within(1e-4f));
        }

        [Test]
        public void Given_ATweenPlayPartWayThrough_When_ItsPlaybackIsPausedAndThePanelTicks_Then_ItsOpacityStopsAdvancing()
        {
            // Arrange
            var element = OnPanel("paused");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(), playback: playback);
            Ticks(4);
            var beforePause = Opacity(element);
            playback.IsPaused = true;

            // Act
            Ticks(4);

            // Assert
            Assert.That(beforePause > 0f ? Opacity(element) : float.NaN, Is.EqualTo(beforePause).Within(1e-6f));
        }

        [Test]
        public void Given_ATweenPlayStartedOnAPausedPlayback_When_ThePanelTicks_Then_ItHoldsItsFromOpacityInline()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("held");
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(),
                playback: new MotionPlayback { IsPaused = true });

            // Act
            Ticks(10);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((Opacity(clock) > 0f, opacity.keyword, opacity.value),
                Is.EqualTo((true, StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ADelayedTweenPlayOnAPlayback_When_ThePanelTicksPastTheDelay_Then_ItHasPlayedTheTimeLessTheDelay()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("delayed");
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(delaySec: 0.1f),
                playback: new MotionPlayback());

            // Act
            Ticks(20);

            // Assert
            var elapsed = Opacity(clock);
            Assert.That(elapsed > 0.2f ? Opacity(element) : float.NaN, Is.EqualTo(elapsed - 0.1f).Within(1e-4f));
        }

        [Test]
        public void Given_ARunningTweenPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_ItHoldsItsStartingOpacityBesideItsTargetClass()
        {
            // Arrange
            var element = OnPanel("cancelled");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(), playback: playback);
            Ticks(5);
            var moving = Opacity(element);

            // Act
            playback.CancelPlays();
            Ticks(5);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((moving > 0f, opacity.keyword, opacity.value, element.ClassListContains("opacity-100")),
                Is.EqualTo((true, StyleKeyword.Undefined, 0f, true)));
        }

        [Test]
        public void Given_ARunningTweenPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_ItsCompletionNeverRuns()
        {
            // Arrange
            var element = OnPanel("cancelled");
            var playback = new MotionPlayback();
            var completions = 0;
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, LinearTween(), onComplete: () => completions++,
                playback: playback);
            Ticks(5);
            var moving = Opacity(element);

            // Act
            playback.CancelPlays();
            AdvancePast(1f);

            // Assert
            Assert.That((moving > 0f, completions), Is.EqualTo((true, 0)));
        }
    }
}
