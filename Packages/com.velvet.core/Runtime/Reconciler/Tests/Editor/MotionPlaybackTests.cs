using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
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

        // The replacing play is on no playback, so the cancel has to leave it running: four frames into its linear
        // second from 1 it reads 0.936, where a cancel taking it would hold it at 1.
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
            Assert.That(Opacity(element), Is.EqualTo(1f - 4 * 0.016f).Within(1e-4f));
        }

        [Test]
        public void Given_APlaybackWhosePlayItsOwnNextPlayReplaced_When_ThatPlayStarts_Then_ItListsOnlyTheNewOne()
        {
            // Arrange
            var element = OnPanel("replaced");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(2);

            // Act
            _scheduler.PlayVariantEnter(element, s_visible, s_hidden, Linear(), playback: playback);

            // Assert
            Assert.That(ListedPlays(playback), Is.EqualTo(1));
        }

        // The first playback's play was replaced by the second's, which a cancel then holds, so the element's held
        // play belongs to the second playback alone.
        private (MotionPlayback First, VisualElement Element) ReplacedByAHeldPlay()
        {
            var element = OnPanel("shared");
            var first = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: first);
            Ticks(2);
            var second = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_visible, s_hidden, Linear(), playback: second);
            Ticks(2);
            second.CancelPlays();
            return (first, element);
        }

        [Test]
        public void Given_APlaybackWhosePlayAnotherPlaybacksHeldPlayReplaced_When_ItStartsAnotherPlay_Then_ItListsOnlyThatOne()
        {
            // Arrange
            var (first, _) = ReplacedByAHeldPlay();

            // Act
            _scheduler.PlayVariantEnter(OnPanel("other"), s_hidden, s_visible, Linear(), playback: first);

            // Assert
            Assert.That(ListedPlays(first), Is.EqualTo(1));
        }

        [Test]
        public void Given_APlaybackWhosePlayAnotherPlaybacksHeldPlayReplaced_When_ItReleasesItsHeldPlays_Then_TheOtherHeldPlayStays()
        {
            // Arrange
            var (first, element) = ReplacedByAHeldPlay();

            // Act
            first.ReleaseHeldPlays();

            // Assert
            var opacity = element.style.opacity;
            Assert.That((opacity.keyword, opacity.value), Is.EqualTo((StyleKeyword.Undefined, 1f)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_ThePlaybackReleasesItsHeldPlays_Then_TheElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("released");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            playback.ReleaseHeldPlays();

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // A reseed starts its plays on the playback before it releases the held ones the plays did not replace.
        [Test]
        public void Given_APlayACancelHolds_When_ThePlaybackStartsAPlayElsewhereAndThenReleasesItsHeldPlays_Then_TheHeldElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("released");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;
            _scheduler.PlayVariantEnter(OnPanel("elsewhere"), s_hidden, s_visible, Linear(), playback: playback);

            // Act
            playback.ReleaseHeldPlays();

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // Framer Motion's cancel() leaves a value where it stopped until an animation of that value starts, so the
        // later play, driving translate alone, leaves the held opacity, through its finish too.
        [Test]
        public void Given_APlayACancelHolds_When_APlayOnAnotherPropertyRunsToItsEnd_Then_TheOpacityStaysHeld()
        {
            // Arrange
            var element = OnPanel("kept");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[0px]" }, new[] { "translate-x-[10px]" },
                Linear());
            AdvancePast(1f);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((opacity.keyword, opacity.value), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        // Translate is one style for both axes, so the later play writes x as well as y. The held x is not 0, which
        // a translate written without an x channel would also show.
        [Test]
        public void Given_ATranslateXPlayACancelHolds_When_ATranslateYPlayRunsToItsEnd_Then_XStaysHeldAndYLandsWhereThatPlayDoes()
        {
            // Arrange
            var element = OnPanel("crossed");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[5px]" }, new[] { "translate-x-[15px]" },
                Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "translate-y-[0px]" }, new[] { "translate-y-[20px]" },
                Linear());
            AdvancePast(1f);

            // Assert
            var translate = element.style.translate.value;
            Assert.That((translate.x.value, translate.y.value), Is.EqualTo((5f, 20f)));
        }

        // The case above with the axes swapped, on a held spring.
        [Test]
        public void Given_ATranslateYSpringPlayACancelHolds_When_ATranslateXPlayRunsToItsEnd_Then_YStaysHeldAndXLandsWhereThatPlayDoes()
        {
            // Arrange
            var element = OnPanel("crossed-spring");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "translate-y-[5px]" }, new[] { "translate-y-[15px]" },
                new StyleTransitionConfig { Type = TransitionType.Spring }, playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[0px]" }, new[] { "translate-x-[20px]" },
                Linear());
            AdvancePast(1f);

            // Assert
            var translate = element.style.translate.value;
            Assert.That((translate.x.value, translate.y.value), Is.EqualTo((20f, 5f)));
        }

        // The payload writes the same inline slot the held play does, so without the held value written back the
        // element would show 0.5 while the next play starts from 0.
        [Test]
        public void Given_APlayACancelHolds_When_AHoverPayloadWritesItsOpacityInline_Then_TheHeldOpacityIsWrittenBack()
        {
            // Arrange
            var ctx = new ReconcilerContext();
            var element = OnPanel("hovered");
            var playback = new MotionPlayback();
            ctx.StyleAnimationScheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            StyleVariantPayload.Apply(element, new[] { "opacity-[.5]" }, true, StyleLayerPriority.Hover, ctx);

            // Assert
            var opacity = element.style.opacity;
            Assert.That((opacity.keyword, opacity.value), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        // The second play takes translate alone, so the first stays held on opacity until the second is cancelled
        // too, which releases it rather than leaving it listed with no entry on the element.
        [Test]
        public void Given_AHeldPlayAndALaterPlayOnAnotherPropertyOfItsElement_When_TheLaterPlayIsCancelled_Then_TheFirstPlaybackListsNothing()
        {
            // Arrange
            var element = OnPanel("twice-held");
            var first = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: first);
            Ticks(5);
            first.CancelPlays();
            var second = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[0px]" }, new[] { "translate-x-[10px]" },
                Linear(), playback: second);
            Ticks(2);

            // Act
            second.CancelPlays();

            // Assert
            Assert.That(ListedPlays(first), Is.Zero);
        }

        // A held play the next play takes every channel from is released, and leaves its playback's list with it.
        [Test]
        public void Given_AColorAndWidthPlayACancelHolds_When_APlayOnTheSameColorAndWidthStarts_Then_ThePlaybackListsNothing()
        {
            // Arrange
            var element = OnPanel("taken-over");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "bg-[#000000]", "w-[0px]" },
                new[] { "bg-[#ffffff]", "w-[100px]" }, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "bg-[#ffffff]", "w-[100px]" },
                new[] { "bg-[#ff0000]", "w-[50px]" }, Linear());

            // Assert
            Assert.That(ListedPlays(playback), Is.Zero);
        }

        // The held fade started from opacity-0; the next play's own from-side is opacity-100.
        [Test]
        public void Given_APlayACancelHolds_When_TheNextOpacityPlayStartsOnItsElement_Then_ItStartsFromTheHeldOpacity()
        {
            // Arrange
            var element = OnPanel("continued");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, s_visible, new[] { "opacity-50" }, Linear());

            // Assert
            Assert.That((element.style.opacity.keyword, Opacity(element)), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_AnExitStartsOnItsElement_Then_TheExitStartsFromTheHeldOpacity()
        {
            // Arrange
            var element = OnPanel("leaving");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var exit = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 1f, ExitFromClass = "opacity-100", ExitToClass = "opacity-50",
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };

            // Act
            _scheduler.PlayExit(element, exit, onComplete: null, restoreFromOnCancel: true);

            // Assert
            Assert.That((element.style.opacity.keyword, Opacity(element)), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_AColorPlayACancelHolds_When_TheNextColorPlayStartsOnItsElement_Then_ItStartsFromTheHeldColor()
        {
            // Arrange
            var element = OnPanel("colored");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "bg-[#000000]" }, new[] { "bg-[#ffffff]" }, Linear(),
                playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "bg-[#ffffff]" }, new[] { "bg-[#ff0000]" }, Linear());

            // Assert
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Color.black));
        }

        [Test]
        public void Given_AWidthPlayACancelHolds_When_TheNextWidthPlayStartsOnItsElement_Then_ItStartsFromTheHeldWidth()
        {
            // Arrange
            var element = OnPanel("wide");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "w-[0px]" }, new[] { "w-[100px]" }, Linear(),
                playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "w-[100px]" }, new[] { "w-[50px]" }, Linear());

            // Assert
            Assert.That(element.style.width.value.value, Is.EqualTo(0f));
        }

        // A width held in pixels is no start for one in percent, which starts from its own from-side.
        [Test]
        public void Given_APixelWidthPlayACancelHolds_When_APercentWidthPlayStartsOnItsElement_Then_ItStartsFromItsOwnFromSide()
        {
            // Arrange
            var element = OnPanel("wide");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "w-[0px]" }, new[] { "w-[100px]" }, Linear(),
                playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "w-[100%]" }, new[] { "w-[50%]" }, Linear());

            // Assert
            var width = element.style.width.value;
            Assert.That((width.unit, width.value), Is.EqualTo((LengthUnit.Percent, 100f)));
        }

        [Test]
        public void Given_ATransformPlayACancelHolds_When_TheNextTransformPlayStartsOnItsElement_Then_EachAxisStartsFromItsHeldValue()
        {
            // Arrange
            var element = OnPanel("turned");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element,
                new[] { "translate-x-[0px]", "translate-y-[0px]", "scale-50", "rotate-45" },
                new[] { "translate-x-[10px]", "translate-y-[20px]", "scale-100", "rotate-90" }, Linear(),
                playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element,
                new[] { "translate-x-[30px]", "translate-y-[30px]", "scale-150", "rotate-180" },
                new[] { "translate-x-[40px]", "translate-y-[40px]", "scale-125", "rotate-n90" }, Linear());

            // Assert
            var translate = element.style.translate.value;
            Assert.That((translate.x.value, translate.y.value, element.style.scale.value.value.x,
                    element.style.rotate.value.angle.value),
                Is.EqualTo((0f, 0f, 0.5f, 45f)));
        }

        // The held play drove opacity alone, so the colour and the width start from the next play's own from-side.
        [Test]
        public void Given_AnOpacityPlayACancelHolds_When_APlayAlsoDrivingAColorAndAWidthStartsOnItsElement_Then_OnlyItsOpacityStartsFromTheHeldValue()
        {
            // Arrange
            var element = OnPanel("widened");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "opacity-100", "bg-[#ffffff]", "w-[100px]" },
                new[] { "opacity-50", "bg-[#ff0000]", "w-[50px]" }, Linear());

            // Assert
            Assert.That((Opacity(element), element.style.backgroundColor.value, element.style.width.value.value),
                Is.EqualTo((0f, Color.white, 100f)));
        }

        // The held play drove a colour and a width, neither of which the next play drives.
        [Test]
        public void Given_AColorAndWidthPlayACancelHolds_When_AnOpacityPlayStartsOnItsElement_Then_ItStartsFromItsOwnFromSide()
        {
            // Arrange
            var element = OnPanel("faded");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "bg-[#000000]", "w-[0px]" },
                new[] { "bg-[#ffffff]", "w-[100px]" }, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.PlayVariantEnter(element, s_visible, new[] { "opacity-50" }, Linear());

            // Assert
            Assert.That(Opacity(element), Is.EqualTo(1f));
        }

        // The band follows its caster's resolved opacity, which the cancel holds at the fade's start.
        [Test]
        public void Given_ARingedElementsPlayOnAPlayback_When_ThePlaybackCancelsItAndThePanelTicks_Then_TheBandStaysAtTheFadesStart()
        {
            // Arrange
            var element = OnPanel("ringed");
            var band = RingOverlay.Attach(element, new RingSpec(width: 2f, color: Color.red, offset: 0f,
                inset: false), Array.Empty<string>()).Overlay;
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);

            // Act
            playback.CancelPlays();
            Ticks(2);

            // Assert
            var opacity = band.style.opacity;
            Assert.That((opacity.keyword, opacity.value), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_ItsElementIsTornDown_Then_ItCarriesNoInlineOpacityAndThePlaybackListsNothing()
        {
            // Arrange
            var element = OnPanel("torn-down");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();

            // Act
            _scheduler.CancelEnter(element);
            _scheduler.CancelExitForTeardown(element);

            // Assert
            Assert.That((element.style.opacity.keyword == StyleKeyword.Undefined, ListedPlays(playback)),
                Is.EqualTo((false, 0)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_TheSchedulerCancelsEverything_Then_ItsElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("cancelled");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            _scheduler.CancelAll();

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // A tween runs on the classes the element resolves, so it takes a held play's values off first.
        [Test]
        public void Given_APlayACancelHolds_When_ATweenStartsOnItsElement_Then_TheHeldOpacityComesOff()
        {
            // Arrange
            var element = OnPanel("tweened");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            _scheduler.PlayVariantEnter(element, new[] { "translate-x-[0px]" }, new[] { "translate-x-[10px]" },
                new StyleTransitionConfig { DurationSec = 1f });

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        [Test]
        public void Given_APlayACancelHolds_When_ATweenExitStartsOnItsElement_Then_TheHeldOpacityComesOff()
        {
            // Arrange
            var element = OnPanel("tween-leaving");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;
            var exit = new StyleTransitionConfig
            {
                DurationSec = 1f, ExitFromClass = "translate-x-[0px]", ExitToClass = "translate-x-[10px]",
            };

            // Act
            _scheduler.PlayExit(element, exit, onComplete: null, restoreFromOnCancel: true);

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // The band holds its own inline opacity while the play is held, which the release takes off with the play's.
        [Test]
        public void Given_ARingedElementsPlayACancelHolds_When_ThePlaybackReleasesItsHeldPlays_Then_TheBandCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("ringed");
            var band = RingOverlay.Attach(element, new RingSpec(width: 2f, color: Color.red, offset: 0f,
                inset: false), Array.Empty<string>()).Overlay;
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible, Linear(), playback: playback);
            Ticks(5);
            playback.CancelPlays();
            Ticks(1);
            var held = band.style.opacity.keyword;

            // Act
            playback.ReleaseHeldPlays();

            // Assert
            Assert.That((held, band.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        [Test]
        public void Given_ARunningSpringPlayOnAPlayback_When_ThePlaybackCancelsItsPlays_Then_ItHoldsItsStartingOpacity()
        {
            // Arrange
            var element = OnPanel("spring-held");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible,
                new StyleTransitionConfig { Type = TransitionType.Spring }, playback: playback);
            Ticks(5);

            // Act
            playback.CancelPlays();

            // Assert
            Assert.That((element.style.opacity.keyword, Opacity(element)), Is.EqualTo((StyleKeyword.Undefined, 0f)));
        }

        [Test]
        public void Given_ASpringPlayACancelHolds_When_ThePlaybackReleasesItsHeldPlays_Then_TheElementCarriesNoInlineOpacity()
        {
            // Arrange
            var element = OnPanel("spring-released");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, s_hidden, s_visible,
                new StyleTransitionConfig { Type = TransitionType.Spring }, playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.opacity.keyword;

            // Act
            playback.ReleaseHeldPlays();

            // Assert
            Assert.That((held, element.style.opacity.keyword == StyleKeyword.Undefined),
                Is.EqualTo((StyleKeyword.Undefined, false)));
        }

        // w-[100px] is an inline-resolved class the element keeps as its resting pose, so releasing the held width
        // writes that class's value back rather than leaving the slot empty.
        [Test]
        public void Given_AWidthPlayACancelHolds_When_ThePlaybackReleasesItsHeldPlays_Then_TheElementCarriesItsRestingWidth()
        {
            // Arrange
            var element = OnPanel("wide");
            var playback = new MotionPlayback();
            _scheduler.PlayVariantEnter(element, new[] { "w-[0px]" }, new[] { "w-[100px]" }, Linear(),
                playback: playback);
            Ticks(5);
            playback.CancelPlays();
            var held = element.style.width.value.value;

            // Act
            playback.ReleaseHeldPlays();

            // Assert
            var width = element.style.width;
            Assert.That((held, width.keyword, width.value.value), Is.EqualTo((0f, StyleKeyword.Undefined, 100f)));
        }

        // Its from-value is already within the spring's rest distance of its target and it has no delay, so its
        // first sample, at time 0, settles it.
        [Test]
        public void Given_ASpringPlayOnAPlaybackStartingAtRest_When_Started_Then_ItCompletesAtOnce()
        {
            // Arrange
            var completions = 0;

            // Act
            _scheduler.PlayVariantEnter(OnPanel("resting"), new[] { "opacity-[.9995]" }, s_visible,
                new StyleTransitionConfig { Type = TransitionType.Spring }, onComplete: () => completions++,
                playback: new MotionPlayback());

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        // Two frames of 0.016 s add up to the 0.032 s duration exactly, in float.
        [Test]
        public void Given_ABezierPlayOnAPlaybackLastingTwoFrames_When_TwoFramesRun_Then_ItHasCompleted()
        {
            // Arrange
            var completions = 0;
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.032f,
                BezierX1 = 0f, BezierY1 = 0f, BezierX2 = 1f, BezierY2 = 1f,
            };
            _scheduler.PlayVariantEnter(OnPanel("short"), s_hidden, s_visible, config, onComplete: () => completions++,
                playback: new MotionPlayback());

            // Act
            Ticks(2);

            // Assert
            Assert.That(completions, Is.EqualTo(1));
        }

        private static int ListedPlays(MotionPlayback playback)
            => ((ICollection)typeof(MotionPlayback)
                .GetField("_plays", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(playback)).Count;
    }
}
