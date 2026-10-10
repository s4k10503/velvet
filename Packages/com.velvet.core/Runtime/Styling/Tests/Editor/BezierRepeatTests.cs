using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="StyleTransitionConfig.Repeat"/> / <see cref="StyleTransitionConfig.RepeatType"/> /
    /// <see cref="StyleTransitionConfig.RepeatDelaySec"/> as the bezier driver plays them, against the values Framer
    /// Motion's repeat timing gives the same pass: one opacity channel from 0 to 1 over a one-second pass, so the
    /// opacity read back is the fraction of the span the play shows.
    /// </summary>
    /// <remarks>
    /// The curve is CSS's <c>ease-in</c>, which is not its own time-reversal, so a reversed pass and a mirrored pass
    /// land on different values and each case separates the two. Driven by <see cref="BezierTweenDriver.Step"/>
    /// directly, panel-free, as <see cref="BezierTweenDriverTests"/> is.
    /// </remarks>
    [TestFixture]
    internal sealed class BezierRepeatTests
    {
        private const float X1 = 0.42f;
        private const float Y1 = 0f;
        private const float X2 = 1f;
        private const float Y2 = 1f;
        private const float PassSec = 1f;

        private static float Eased(float progress) => CubicBezierEvaluator.Evaluate(X1, Y1, X2, Y2, progress);

        // Whether the last step reported the play done, and the opacity it wrote. NaN stands in for a plan that
        // resolved no channel, so such a regression fails the comparison rather than skipping the case.
        private static (bool done, float opacity) Play(MotionRepeat repeat, params float[] steps)
        {
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec, repeat);
            if (state == null)
            {
                return (false, float.NaN);
            }
            BezierTweenDriver.ApplyCurrentValues(element, state);
            var done = false;
            foreach (var dt in steps)
            {
                done = BezierTweenDriver.Step(element, state, dt);
            }
            return (done, element.style.opacity.value);
        }

        [Test]
        public void Given_ALoopRepeat_When_TheSecondPassIsAQuarterIn_Then_ItShowsTheCurveAQuarterInFromTheStart()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0f);

            // Act
            var (_, opacity) = Play(repeat, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(Eased(0.25f)).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeat_When_TheSecondPassIsAQuarterIn_Then_ItShowsTheCurveThreeQuartersInPlayedBackwards()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f);

            // Act
            var (_, opacity) = Play(repeat, 1.25f);

            // Assert — the first pass's frame at 0.75 s, which a mirrored pass (1 - Eased(0.25)) does not show.
            Assert.That(opacity, Is.EqualTo(Eased(0.75f)).Within(1e-5f));
        }

        [Test]
        public void Given_AMirrorRepeat_When_TheSecondPassIsAQuarterIn_Then_ItShowsTheCurveAQuarterInFromTheToValue()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Mirror, 0f);

            // Act
            var (_, opacity) = Play(repeat, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(1f - Eased(0.25f)).Within(1e-5f));
        }

        [Test]
        public void Given_ALoopRepeat_When_ExactlyOnePassHasElapsed_Then_ItShowsTheEndOfThatPass()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0f);

            // Act
            var (_, opacity) = Play(repeat, PassSec);

            // Assert — the boundary belongs to the pass it ends, not to the start of the next one (opacity 0).
            Assert.That(opacity, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeat_When_ExactlyOnePassHasElapsed_Then_ItShowsTheTurnaroundAtTheToValue()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f);

            // Act
            var (_, opacity) = Play(repeat, PassSec);

            // Assert — the end of the forward pass, not the end of the reversed one (opacity 0).
            Assert.That(opacity, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeatOutlastedByASlowerChannel_When_TheClockLandsExactlyOnTheEnd_Then_ItShowsTheEndOfTheLastPass()
        {
            // Arrange — three passes end at 3 s, and a slower channel holds the play's end at 4 s. Reached by the
            // fall-through into the pass arithmetic, 4 s reads as the end of a fourth pass, a reversed one.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec,
                new MotionRepeat(2f, TransitionRepeatType.Reverse, 0f));
            state!.SlowestTimingEndSec = 4f;

            // Act
            BezierTweenDriver.Step(element, state, 4f);

            // Assert — two repeats are an even count, so the play ends on the to-value (opacity 0 on the fourth pass).
            Assert.That(element.style.opacity.value, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AnEndlessLoop_When_FourPassesElapseInOneStep_Then_ItShowsTheEndOfTheFourthPass()
        {
            // Arrange
            var repeat = new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0f);

            // Act — four passes is two whole cycles of the folded clock.
            var (_, opacity) = Play(repeat, 4f * PassSec);

            // Assert
            Assert.That(opacity, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_ALoopRepeatWithADelay_When_TheDelayIsRunning_Then_ItHoldsTheValueThePassEndedOn()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0.5f);

            // Act — a quarter of a second into the half-second wait after the first pass.
            var (_, opacity) = Play(repeat, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeatWithADelay_When_TheReversedPassIsAQuarterIn_Then_ItShowsTheCurveThreeQuartersIn()
        {
            // Arrange — the reversed pass starts 1.5 s in, after the first pass and its wait.
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0.5f);

            // Act
            var (_, opacity) = Play(repeat, 1.75f);

            // Assert
            Assert.That(opacity, Is.EqualTo(Eased(0.75f)).Within(1e-5f));
        }

        [Test]
        public void Given_TwoRepeatsWithADelay_When_TheThirdPassIsAboutToEndAndThenEnds_Then_OnlyTheEndReportsDone()
        {
            // Arrange — three passes and the two waits between them: 3.5 s, with no wait after the last.
            var repeat = new MotionRepeat(2f, TransitionRepeatType.Loop, 0.25f);
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec, repeat);

            // Act — a plan that resolved no channel leaves (true, false), the reverse of the expected pair.
            var beforeEnd = true;
            var atEnd = false;
            if (state != null)
            {
                beforeEnd = BezierTweenDriver.Step(element, state, 3.25f);
                atEnd = BezierTweenDriver.Step(element, state, 0.25f);
            }

            // Assert
            Assert.That((beforeEnd, atEnd), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AnOddReverseRepeat_When_ItsLastPassEnds_Then_ItEndsOnTheFromValue()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f);

            // Act
            var result = Play(repeat, 2.5f);

            // Assert
            Assert.That(result, Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_AnOddMirrorRepeat_When_ItsLastPassEnds_Then_ItEndsOnTheFromValue()
        {
            // Arrange
            var repeat = new MotionRepeat(3f, TransitionRepeatType.Mirror, 0.5f);

            // Act
            var result = Play(repeat, 10f);

            // Assert
            Assert.That(result, Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_AnOddLoopRepeat_When_ItsLastPassEnds_Then_ItEndsOnTheToValue()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0f);

            // Act
            var result = Play(repeat, 2.5f);

            // Assert
            Assert.That(result, Is.EqualTo((true, 1f)));
        }

        [Test]
        public void Given_AnEvenReverseRepeat_When_ItsLastPassEnds_Then_ItEndsOnTheToValue()
        {
            // Arrange
            var repeat = new MotionRepeat(2f, TransitionRepeatType.Reverse, 0f);

            // Act
            var result = Play(repeat, 3.5f);

            // Assert
            Assert.That(result, Is.EqualTo((true, 1f)));
        }

        [Test]
        public void Given_AnEndlessRepeat_When_AThousandSecondsPass_Then_ItHasNotEnded()
        {
            // Arrange
            var repeat = new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Reverse, 0f);

            // Act
            var (done, _) = Play(repeat, 1000f);

            // Assert
            Assert.That(done, Is.False);
        }

        [Test]
        public void Given_AnEndlessRepeat_When_TenSecondsPass_Then_ItsClockStaysWithinTwoPasses()
        {
            // Arrange — a cycle of two passes and their waits is 3 s.
            var repeat = new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0.5f);
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec, repeat);

            // Act — NaN for a plan that resolved no channel.
            var elapsed = float.NaN;
            if (state != null)
            {
                BezierTweenDriver.Step(element, state, 10.25f);
                elapsed = state.ElapsedSec;
            }

            // Assert — 10.25 s folds to 1.25 s, the same frame, three cycles earlier.
            Assert.That(elapsed, Is.EqualTo(1.25f).Within(1e-5f));
        }

        [Test]
        public void Given_AZeroDurationStateWithARepeatDelay_When_ItIsFirstWritten_Then_ItShowsTheEnd()
        {
            // Arrange — the scheduler builds no such state; a direct driver caller can.
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0.5f);
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 0f, repeat);

            // Act — NaN for a plan that resolved no channel.
            var opacity = float.NaN;
            if (state != null)
            {
                BezierTweenDriver.ApplyCurrentValues(element, state);
                opacity = element.style.opacity.value;
            }

            // Assert
            Assert.That(opacity, Is.EqualTo(1f));
        }

        [Test]
        public void Given_AnEndlessRepeatingExit_When_ItIsRetargeted_Then_TheReversalEndsAfterOnePass()
        {
            // Arrange
            var repeat = new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0f);
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec, repeat);

            // Act
            var done = false;
            if (state != null)
            {
                BezierTweenDriver.Step(element, state, 2.5f);
                BezierTweenDriver.Retarget(state);
                done = BezierTweenDriver.Step(element, state, PassSec);
            }

            // Assert
            Assert.That(done, Is.True);
        }

        [Test]
        public void Given_AMirroredPass_When_ItIsRetargeted_Then_TheReversalStartsFromTheValueTheMirroredPassShowed()
        {
            // Arrange — an exit-shaped channel 1 → 0, a quarter into its mirrored second pass.
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Mirror, 0f);
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, PassSec, repeat);

            // Act — NaN for a plan that resolved no channel.
            var from = float.NaN;
            if (state != null)
            {
                BezierTweenDriver.Step(element, state, 1.25f);
                BezierTweenDriver.Retarget(state);
                from = state.Opacity!.From;
            }

            // Assert — the mirrored pass runs 0 → 1 on the ease-in curve, so a quarter in it shows Eased(0.25).
            Assert.That(from, Is.EqualTo(Eased(0.25f)).Within(1e-5f));
        }

        [TestCase(-1f)]
        [TestCase(1.5f)]
        [TestCase(float.NaN)]
        [TestCase(float.NegativeInfinity)]
        public void Given_ARepeatThatIsNotAWholeCountOfPasses_When_AConfigIsBuilt_Then_ItThrows(float value)
        {
            // Arrange
            TestDelegate build = () => _ = new StyleTransitionConfig { Repeat = value };

            // Act / Assert
            Assert.That(build, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_AnEndlessRepeat_When_AConfigIsBuilt_Then_ItKeepsIt()
        {
            // Arrange / Act
            var config = new StyleTransitionConfig { Repeat = float.PositiveInfinity };

            // Assert
            Assert.That(config.Repeat, Is.EqualTo(float.PositiveInfinity));
        }

        [TestCase(-0.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Given_ARepeatDelayThatIsNotAFiniteWait_When_AConfigIsBuilt_Then_ItThrows(float value)
        {
            // Arrange
            TestDelegate build = () => _ = new StyleTransitionConfig { RepeatDelaySec = value };

            // Act / Assert
            Assert.That(build, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static StyleTransitionConfig Repeating() => new()
        {
            Type = TransitionType.Bezier,
            DurationSec = 0.5f,
            Repeat = 3f,
            RepeatType = TransitionRepeatType.Mirror,
            RepeatDelaySec = 0.25f,
        };

        [Test]
        public void Given_ARepeatingConfig_When_WithIsCalled_Then_TheRepeatSurvivesUnchanged()
        {
            // Arrange
            var config = Repeating();

            // Act
            var result = config.With(durationSec: 0.4f);

            // Assert
            Assert.That((result.Repeat, result.RepeatType, result.RepeatDelaySec),
                Is.EqualTo((3f, TransitionRepeatType.Mirror, 0.25f)));
        }

        [Test]
        public void Given_ARepeatingConfig_When_WithExitClassesIsCalled_Then_TheRepeatSurvivesUnchanged()
        {
            // Arrange
            var config = Repeating();

            // Act
            var result = config.WithExitClasses("opacity-100", "opacity-0");

            // Assert
            Assert.That((result.Repeat, result.RepeatType, result.RepeatDelaySec),
                Is.EqualTo((3f, TransitionRepeatType.Mirror, 0.25f)));
        }

        private static readonly string[] s_hidden = { "opacity-0" };
        private static readonly string[] s_visible = { "opacity-100" };

        [Test]
        public void Given_ARepeatingBezierConfig_When_ItsPlaySpanIsRead_Then_ItCoversEveryPassAndTheWaitsBetween()
        {
            // Arrange
            var config = Repeating();

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert — four half-second passes and three quarter-second waits.
            Assert.That(span, Is.EqualTo(2.75).Within(1e-5));
        }

        [Test]
        public void Given_AnEndlesslyRepeatingBezierConfig_When_ItsPlaySpanIsRead_Then_ItNeverEnds()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0.5f, Repeat = float.PositiveInfinity,
            };

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert
            Assert.That(span, Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void Given_AZeroDurationRepeatingBezierConfig_When_ItsPlaySpanIsRead_Then_ItIsZero()
        {
            // Arrange — a zero-duration bezier lands at once, so its repeat delay never runs.
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Bezier, DurationSec = 0f, Repeat = 2f, RepeatDelaySec = 0.25f,
            };

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert
            Assert.That(span, Is.EqualTo(0.0));
        }

        [Test]
        public void Given_ASpringConfig_When_ItsPlaySpanIsRead_Then_ItIsTheTimeItsChannelTakesToRestNotItsDuration()
        {
            // Arrange — Framer hands an opacity's spring to the browser as an easing over a travel of 100, which rests
            // at 1.05 s on its default spring; DurationSec is not read.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, DurationSec = 0.5f };

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert
            Assert.That(span, Is.EqualTo(1.05).Within(1e-5));
        }

        [Test]
        public void Given_ARepeatingSpringConfig_When_ItsPlaySpanIsRead_Then_ItsSlowestChannelsPassesDecide()
        {
            // Arrange — the color rests at 1.05 s and the opacity at 1.1 s: two passes and a wait of the latter.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Repeat = 1f, RepeatDelaySec = 0.25f };

            // Act
            var span = MotionPlaySpan.Of(config, new[] { "opacity-0", "bg-black" }, new[] { "opacity-100", "bg-white" });

            // Assert
            Assert.That(span, Is.EqualTo(2.45).Within(1e-5));
        }

        [Test]
        public void Given_ASpringWithParametersAPlayRefuses_When_ItsPlaySpanIsRead_Then_ItIsZero()
        {
            // Arrange — such a play completes at once.
            var config = new StyleTransitionConfig { Type = TransitionType.Spring, Stiffness = 0f, Repeat = 1f };

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert
            Assert.That(span, Is.EqualTo(0.0));
        }

        [Test]
        public void Given_ATweenConfig_When_ItsPlaySpanIsRead_Then_ItIsItsDuration()
        {
            // Arrange
            var config = new StyleTransitionConfig { DurationSec = 0.3f };

            // Act
            var span = MotionPlaySpan.Of(config, s_hidden, s_visible);

            // Assert
            Assert.That(span, Is.EqualTo(0.3).Within(1e-6));
        }
    }
}
