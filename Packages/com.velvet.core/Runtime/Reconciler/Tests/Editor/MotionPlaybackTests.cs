using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="MotionPlayback"/> on the scheduler: a Spring or Bezier play started on one steps by the
    /// playback's time — its rate, its pause and its delay — and a cancel through it stops the play it started,
    /// without that play's completion, and no other.
    /// </summary>
    [TestFixture]
    internal sealed class MotionPlaybackTests : MotionSimulatedPanelTestsBase
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

        private static StyleTransitionConfig Linear(float delaySec = 0f) => new()
        {
            Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = delaySec,
            BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
        };

        private VisualElement OnPanel(string name)
        {
            var element = new VisualElement { name = name };
            Root.Add(element);
            return element;
        }

        // A linear one-second play on no playback: its opacity reads the seconds the panel has stepped plays by
        // since it started, which a play started beside it on a playback is measured against.
        private VisualElement StartClock()
        {
            var clock = OnPanel("clock");
            _scheduler.PlayVariantEnter(clock, s_hidden, s_visible, Linear());
            return clock;
        }

        private void Ticks(int count)
        {
            for (var i = 0; i < count; i++) Tick();
        }

        private static float Opacity(VisualElement element) => element.style.opacity.value;

        [Test]
        public void Given_ABezierPlayAtHalfRate_When_ThePanelTicks_Then_ItHasCoveredHalfTheTimeOfAPlayOnNoPlayback()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("half");
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(),
                playback: new MotionPlayback { Rate = 0.5f });

            // Act
            Ticks(10);

            // Assert
            var elapsed = Opacity(clock);
            Assert.That(elapsed > 0f ? Opacity(element) : float.NaN, Is.EqualTo(elapsed * 0.5f).Within(1e-4f));
        }

        [Test]
        public void Given_ABezierPlayPartWayThrough_When_ItsRateIsRaisedToTwo_Then_TheFramesAfterCoverTwiceTheTime()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("raised");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            var clockBefore = Opacity(clock);
            var before = Opacity(element);

            // Act
            playback.Rate = 2f;
            Ticks(5);

            // Assert
            var span = Opacity(clock) - clockBefore;
            Assert.That(span > 0f ? Opacity(element) - before : float.NaN, Is.EqualTo(span * 2f).Within(1e-4f));
        }

        // Three times the frame's time is past the spring integrator's longest step, so a play stepped once per
        // frame by the whole of it lags a spring stepped through the same time in frame-sized pieces.
        [Test]
        public void Given_ASpringPlayAtRateThree_When_ThePanelTicks_Then_ItIsWhereASpringSteppedThriceAsLongIs()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("spring");
            var config = new StyleTransitionConfig { Type = TransitionType.Spring };
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, config,
                playback: new MotionPlayback { Rate = 3f });

            // Act
            Ticks(5);

            // Assert
            var elapsed = Opacity(clock);
            Assert.That(elapsed > 0f ? Opacity(element) : float.NaN,
                Is.EqualTo(SpringSteppedFor(config, 3f * elapsed)).Within(1e-4f));
        }

        [Test]
        public void Given_ADelayedBezierPlayOnAPausedPlayback_When_ThePanelTicksPastTheDelayAndThePlaybackResumes_Then_ItIsStillWaitingOutItsDelay()
        {
            // Arrange
            var reference = OnPanel("reference");
            _scheduler.PlayVariantEnter(reference, s_hidden, s_visible, Linear(delaySec: 0.1f));
            var element = OnPanel("delayed");
            var playback = new MotionPlayback { IsPaused = true };
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(delaySec: 0.1f), playback: playback);
            Ticks(20);

            // Act
            playback.IsPaused = false;
            Tick();

            // Assert
            Assert.That((Opacity(reference) > 0f, Opacity(element)), Is.EqualTo((true, 0f)));
        }

        // The delay counts playback seconds, so at half rate a tenth of a second of it takes a fifth of the panel's.
        [Test]
        public void Given_ADelayedBezierPlayAtHalfRate_When_ThePanelTicksPastTheDelay_Then_ItHasPlayedHalfTheTimeLessTheDelay()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("delayed");
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(delaySec: 0.1f),
                playback: new MotionPlayback { Rate = 0.5f });

            // Act
            Ticks(20);

            // Assert
            var elapsed = Opacity(clock);
            Assert.That(elapsed > 0.2f ? Opacity(element) : float.NaN,
                Is.EqualTo(elapsed * 0.5f - 0.1f).Within(1e-4f));
        }

        [Test]
        public void Given_ADelayedSpringPlayOnAPlayback_When_ThePanelTicksPastTheDelay_Then_ItIsWhereASpringSteppedForTheTimeAfterTheDelayIs()
        {
            // Arrange
            var clock = StartClock();
            var element = OnPanel("delayed");
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = 0.1f };
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, config, playback: new MotionPlayback());

            // Act
            Ticks(20);

            // Assert
            var elapsed = Opacity(clock);
            var reference = SpringSteppedFor(config, elapsed - 0.1f);
            Assert.That(elapsed > 0.1f ? Opacity(element) : float.NaN, Is.EqualTo(reference).Within(1e-4f));
        }

        [Test]
        public void Given_ASpringPlayOnAPlaybackWithANegativeDelay_When_Started_Then_ItIsAlreadyThatFarIn()
        {
            // Arrange
            var element = OnPanel("ahead");
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = -0.3f };

            // Act
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, config, playback: new MotionPlayback());

            // Assert
            Assert.That(Opacity(element), Is.EqualTo(SpringSteppedFor(config, 0.3f)).Within(1e-4f));
        }

        [Test]
        public void Given_ABezierPlayOnAPlaybackWithANegativeDelay_When_Started_Then_ItIsAlreadyThatFarIn()
        {
            // Arrange
            var element = OnPanel("ahead");

            // Act
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(delaySec: -0.25f),
                playback: new MotionPlayback());

            // Assert
            Assert.That(Opacity(element), Is.EqualTo(0.25f).Within(1e-4f));
        }

        [Test]
        public void Given_ASpringPlayOnAPlayback_When_ItSettles_Then_ItsCompletionRuns()
        {
            // Arrange
            var completions = 0;
            _scheduler.PlayVariantEnter(OnPanel("spring"), s_hidden, s_visible,
                new StyleTransitionConfig { Type = TransitionType.Spring }, onComplete: () => completions++,
                playback: new MotionPlayback());

            // Act
            AdvancePast(5f);

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        // Its from-value is already within the spring's rest distance of its target, so the first step it takes
        // settles it, and none may be taken inside the delay.
        [Test]
        public void Given_ADelayedSpringPlayOnAPlaybackStartingAtRest_When_ThePanelTicksInsideAndThenPastTheDelay_Then_ItCompletesOnlyPastIt()
        {
            // Arrange
            var completions = 0;
            _scheduler.PlayVariantEnter(OnPanel("resting"), new[] { "opacity-[.9995]" }, s_visible,
                new StyleTransitionConfig { Type = TransitionType.Spring, DelaySec = 0.5f },
                onComplete: () => completions++, playback: new MotionPlayback());

            // Act
            Ticks(5);
            var insideDelay = completions;
            AdvancePast(1f);

            // Assert
            Assert.That((insideDelay, completions), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_ABezierPlayOnAPlayback_When_ItsDurationHasRun_Then_ItsCompletionRuns()
        {
            // Arrange
            var completions = 0;
            _scheduler.PlayVariantEnter(OnPanel("bezier"), s_hidden, s_visible, Linear(),
                onComplete: () => completions++, playback: new MotionPlayback());

            // Act
            AdvancePast(1f);

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        // The opacity a spring from opacity-0 to opacity-100 shows `seconds` in, stepped in frame-sized pieces.
        private static float SpringSteppedFor(StyleTransitionConfig config, float seconds)
        {
            var reference = new VisualElement();
            var state = MotionSpringDriver.Create(MotionSpringClassParser.Resolve(s_hidden, s_visible),
                config.Stiffness, config.Damping, config.Mass);
            for (var remaining = seconds; remaining > 0f; remaining -= 0.016f)
            {
                MotionSpringDriver.Step(reference, state, Math.Min(remaining, 0.016f));
            }
            return reference.style.opacity.value;
        }

        // Framer Motion's cancel() samples the animation at time 0 and tears it down.
        [Test]
        public void Given_ARunningPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_ItHoldsItsStartingOpacityBesideItsTargetClass()
        {
            // Arrange
            var element = OnPanel("cancelled");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
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
        public void Given_APlayACancelHolds_When_AnotherPlayOnItsElementRunsToItsEnd_Then_TheElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("reconverged");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            _scheduler.PlayVariantEnter(element, s_visible, s_hidden, Linear());
            AdvancePast(1f);

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_ThePlaybackClearsItsPlays_Then_TheElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("cleared");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            playback.ClearPlays();

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // The band follows its caster's resolved opacity once its co-fade starts, and a translate leaves that at 1;
        // an ungrouped delayed play starts the co-fade with the tick after its delay.
        [Test]
        public void Given_ARingedElementsDelayedPlayOnAPlayback_When_ThePanelTicksInsideAndThenPastTheDelay_Then_TheBandFollowsOnlyPastIt()
        {
            // Arrange
            var element = OnPanel("ringed");
            var band = RingOverlay.Attach(element, new RingSpec(width: 2f, color: UnityEngine.Color.red, offset: 0f,
                inset: false), Array.Empty<string>()).Overlay;
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, DelaySec = 0.2f,
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[0px]" }, new[] { "translate-x-[100px]" }, config,
                playback: new MotionPlayback());

            // Act
            Ticks(5);
            var insideDelay = band.style.opacity.value;
            Ticks(20);

            // Assert
            Assert.That((insideDelay, band.style.opacity.value), Is.EqualTo((0f, 1f)));
        }

        [Test]
        public void Given_ARunningPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_ItsCompletionNeverRuns()
        {
            // Arrange
            var element = OnPanel("cancelled");
            var playback = new MotionPlayback();
            var completions = 0;
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), onComplete: () => completions++,
                playback: playback);
            Ticks(5);
            var moving = Opacity(element);

            // Act
            playback.CancelPlays();
            AdvancePast(1f);

            // Assert
            Assert.That((moving > 0f, completions), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_APlaybackWhosePlayAnotherPlayReplaced_When_ThePlaybackCancelsItsPlays_Then_TheReplacingPlayRunsOn()
        {
            // Arrange
            var element = OnPanel("replaced");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(2);
            _scheduler.PlayVariantEnter(element, s_visible, s_hidden, Linear());
            Ticks(2);

            // Act
            playback.CancelPlays();
            Ticks(2);

            // Assert
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Undefined));
        }

        [Test]
        public void Given_APlaybackWhoseFirstPlayHasFinished_When_ASecondPlayStartsOnIt_Then_ItListsOnlyTheSecond()
        {
            // Arrange
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(OnPanel("first"), s_hidden, s_visible, Linear(), playback: playback);
            AdvancePast(1f);

            // Act
            _scheduler.PlayVariantEnter(OnPanel("second"), s_hidden, s_visible, Linear(), playback: playback);

            // Assert
            Assert.That(ListedPlays(playback), Is.EqualTo(1));
        }

        // A play the scheduler reports driving is one whose settle the patcher waits on to re-apply an inline token,
        // and a held play never settles.
        [Test]
        public void Given_ARunningPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_TheSchedulerNoLongerReportsItDriving()
        {
            // Arrange
            var element = OnPanel("held");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(2);
            var driving = _scheduler.IsDriving(element);

            // Act
            playback.CancelPlays();

            // Assert
            Assert.That((driving, _scheduler.IsDriving(element)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APlaybackHoldingACancelledPlay_When_ItClearsItsPlays_Then_ItListsNone()
        {
            // Arrange
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(OnPanel("held"), s_hidden, s_visible, Linear(), playback: playback);
            Ticks(2);
            playback.CancelPlays();
            var listed = ListedPlays(playback);

            // Act
            playback.ClearPlays();

            // Assert
            Assert.That((listed, ListedPlays(playback)), Is.EqualTo((1, 0)));
        }

        private static int ListedPlays(MotionPlayback playback)
            => ((ICollection)typeof(MotionPlayback)
                .GetField("_plays", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(playback)).Count;
    }
}
