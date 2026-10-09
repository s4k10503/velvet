using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="StyleTransitionConfig.Repeat"/> as the spring driver plays it, against Framer Motion's main-thread
    /// animation over its spring generator: each channel's pass lasts as long as its own spring takes to rest
    /// (<c>calcGeneratorDuration</c>, with Framer's rest thresholds for a travel under 5 and over it), a reversed
    /// pass samples the same spring backwards in time, and a mirrored pass springs from the to-value back.
    /// </summary>
    /// <remarks>
    /// Every expected value was computed with a JavaScript port of Framer's spring generator and tick, not with
    /// <see cref="SpringIntegrator"/>. The spring is Framer's default (stiffness 100, damping 10, mass 1); its opacity
    /// pass, a travel of 1, rests at 1.1 s, and a travel of 100 at 1.05 s. Driven by
    /// <see cref="MotionSpringDriver.Step"/> directly, panel-free.
    /// </remarks>
    [TestFixture]
    internal sealed class SpringRepeatTests
    {
        private const float Stiffness = 100f;
        private const float Damping = 10f;
        private const float Mass = 1f;

        private static MotionSpringState Opacity(MotionRepeat repeat, VisualElement element)
        {
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass, repeat);
            if (state != null)
            {
                MotionSpringDriver.ApplyCurrentValues(element, state);
            }
            return state;
        }

        // The opacity a single step to `seconds` leaves; NaN for a plan that resolved no channel.
        private static float OpacityAt(MotionRepeat repeat, float seconds)
        {
            var element = new VisualElement();
            var state = Opacity(repeat, element);
            if (state == null)
            {
                return float.NaN;
            }
            MotionSpringDriver.Step(element, state, seconds);
            return element.style.opacity.value;
        }

        [TestCase(1f, 1.1f)]
        [TestCase(40f, 1.05f)]
        [TestCase(100f, 1.05f)]
        [TestCase(5f, 0.65f)]
        [TestCase(4.99f, 1.45f)]
        public void Given_ASpringTravel_When_ItsPassIsMeasured_Then_ItRestsWhereFramersGeneratorDoes(float delta, float expected)
        {
            // Arrange / Act
            var pass = MotionSpringDriver.PassDurationSec(delta, Stiffness, Damping, Mass);

            // Assert
            Assert.That(pass, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void Given_ASpringFirstRestingOnThe20SecondSample_When_ItsPassIsMeasured_Then_ItHasNoPass()
        {
            // Arrange — stiffness 1, damping 3.9864: Framer's generator first rests on the 20 000 ms sample, which
            // calcGeneratorDuration reports as infinity.
            // Act
            var pass = MotionSpringDriver.PassDurationSec(1f, 1f, 3.9864f, 1f);

            // Assert
            Assert.That(pass, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Given_ALoopRepeat_When_TheSecondPassIsATenthIn_Then_ItShowsTheSpringATenthIn()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0f);

            // Act
            var opacity = OpacityAt(repeat, 1.2f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0.3402998466f).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeat_When_TheSecondPassIsATenthIn_Then_ItShowsTheFirstPassATenthBeforeItsEnd()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f);

            // Act
            var opacity = OpacityAt(repeat, 1.2f);

            // Assert — the first pass's frame at 1.0 s, still overshooting; a mirrored pass shows 0.6597.
            Assert.That(opacity, Is.EqualTo(1.002170117f).Within(1e-5f));
        }

        [Test]
        public void Given_AMirrorRepeat_When_TheSecondPassIsATenthIn_Then_ItShowsTheSpringBackATenthIn()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Mirror, 0f);

            // Act
            var opacity = OpacityAt(repeat, 1.2f);

            // Assert
            Assert.That(opacity, Is.EqualTo(0.6597001534f).Within(1e-5f));
        }

        [Test]
        public void Given_AReverseRepeatWithADelay_When_TheReversedPassIsATenthIn_Then_ItShowsTheFirstPassATenthBeforeItsEnd()
        {
            // Arrange — the reversed pass starts after the 1.1 s pass and its 0.25 s wait.
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Reverse, 0.25f);

            // Act
            var opacity = OpacityAt(repeat, 1.45f);

            // Assert
            Assert.That(opacity, Is.EqualTo(1.002170117f).Within(1e-5f));
        }

        [Test]
        public void Given_ALoopRepeatWithADelay_When_TheDelayIsRunning_Then_ItShowsTheSpringPastItsPass()
        {
            // Arrange
            var repeat = new MotionRepeat(1f, TransitionRepeatType.Loop, 0.25f);

            // Act — 0.15 s into the wait, where Framer samples the spring at 1.25 s rather than holding its target.
            var opacity = OpacityAt(repeat, 1.25f);

            // Assert
            Assert.That(opacity, Is.EqualTo(1.001425521f).Within(1e-6f));
        }

        [Test]
        public void Given_AnOddReverseRepeat_When_ItsLastPassEnds_Then_ItSettlesOnTheFromValue()
        {
            // Arrange
            var element = new VisualElement();
            var state = Opacity(new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f), element);

            // Act — a plan that resolved no channel leaves (false, NaN).
            var (settled, opacity) = (false, float.NaN);
            if (state != null)
            {
                settled = MotionSpringDriver.Step(element, state, 2.3f);
                opacity = element.style.opacity.value;
            }

            // Assert
            Assert.That((settled, opacity), Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_AnOddLoopRepeat_When_ItsLastPassEnds_Then_ItSettlesOnTheToValue()
        {
            // Arrange
            var element = new VisualElement();
            var state = Opacity(new MotionRepeat(1f, TransitionRepeatType.Loop, 0f), element);

            // Act
            var (settled, opacity) = (false, float.NaN);
            if (state != null)
            {
                settled = MotionSpringDriver.Step(element, state, 2.3f);
                opacity = element.style.opacity.value;
            }

            // Assert
            Assert.That((settled, opacity), Is.EqualTo((true, 1f)));
        }

        [Test]
        public void Given_ARepeatWithADelay_When_ItsLastPassIsAboutToEndAndThenEnds_Then_OnlyTheEndSettles()
        {
            // Arrange — stiffness 200, damping 13: Framer's opacity pass rests at exactly 1 s, so two passes and the
            // 0.25 s wait between them end exactly at 2.25 s, with no wait after the last.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, 200f, 13f, Mass, new MotionRepeat(1f, TransitionRepeatType.Loop, 0.25f));

            // Act — a plan that resolved no channel leaves (true, false).
            var (beforeEnd, atEnd) = (true, false);
            if (state != null)
            {
                beforeEnd = MotionSpringDriver.Step(element, state, 2f);
                atEnd = MotionSpringDriver.Step(element, state, 0.25f);
            }

            // Assert
            Assert.That((beforeEnd, atEnd), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_AChannelStartingPartWay_When_ItsPassIsMeasured_Then_ItIsTheSpringOverTheDistanceLeft()
        {
            // Arrange — opacity 0.5 → 1 travels 0.5, which rests at 1.05 s; a travel of 1.5 would rest at 1.4 s.
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-50" }, new[] { "opacity-100" });

            // Act — NaN for a plan that resolved no channel.
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass,
                new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));
            var pass = state?.Opacity?.PassSec ?? float.NaN;

            // Assert
            Assert.That(pass, Is.EqualTo(1.05f).Within(1e-5f));
        }

        [Test]
        public void Given_AChannelStartingPartWay_When_ItsPlaySpanIsRead_Then_ItIsTheSpringOverTheDistanceLeft()
        {
            // Arrange
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-50" }, new[] { "opacity-100" });

            // Act
            var span = MotionSpringDriver.SpanSec(plan, Stiffness, Damping, Mass, default);

            // Assert
            Assert.That(span, Is.EqualTo(1.05).Within(1e-5));
        }

        [Test]
        public void Given_EveryAxisAndALength_When_ARepeatingPlayIsHalfwayThroughItsFirstPass_Then_EachHasLeftItsFromValue()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(
                new[] { "translate-x-0", "translate-y-0", "scale-50", "rotate-0", "w-0" },
                new[] { "translate-x-4", "translate-y-4", "scale-100", "rotate-45", "w-32" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass,
                new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));

            // Act — a channel the plan did not resolve reads as "missing".
            var moved = "no state";
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 0.5f);
                string Moved(SpringChannel? c) => c == null ? "missing" : (c.Integrator.Value != c.RestingTarget).ToString();
                moved = string.Join(",", Moved(state.TranslateX), Moved(state.TranslateY), Moved(state.Scale),
                    Moved(state.Rotate), state.Lengths is { Count: 1 } lengths ? Moved(lengths[0].Value) : "missing");
            }

            // Assert
            Assert.That(moved, Is.EqualTo("True,True,True,True,True"));
        }

        [Test]
        public void Given_AnEndlessRepeat_When_AThousandSecondsPass_Then_ItHasNotSettled()
        {
            // Arrange
            var element = new VisualElement();
            var state = Opacity(new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Mirror, 0f), element);

            // Act
            var settled = state == null || MotionSpringDriver.Step(element, state, 1000f);

            // Assert
            Assert.That(settled, Is.False);
        }

        [Test]
        public void Given_AColorAndAnOpacity_When_TheColorsShorterPassHasEnded_Then_TheColorIsInItsSecondPassAlone()
        {
            // Arrange — the color springs a travel of 100 and rests at 1.05 s; the opacity rests at 1.1 s.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0", "bg-black" },
                new[] { "opacity-100", "bg-white" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass,
                new MotionRepeat(1f, TransitionRepeatType.Loop, 0f));

            // Act — NaN for a plan that resolved no color channel.
            var progress = float.NaN;
            if (state?.Colors is { Count: 1 } colors)
            {
                MotionSpringDriver.Step(element, state, 1.075f);
                progress = colors[0].Progress.Integrator.Value;
            }

            // Assert — 25 ms into the color's second pass, read as a percentage of its travel.
            Assert.That(progress, Is.EqualTo(0.02865363264f).Within(1e-5f));
        }

        [Test]
        public void Given_ASpringThatDoesNotRestWithin20Seconds_When_ItRepeats_Then_ItPlaysItsFirstPassOn()
        {
            // Arrange — damping 0.1 leaves the spring ringing well past 20 s.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = MotionSpringDriver.Create(plan, Stiffness, 0.1f, Mass,
                new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f));

            // Act — NaN for a plan that resolved no channel.
            var opacity = float.NaN;
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 1f);
                opacity = state.Opacity!.Integrator.Value;
            }

            // Assert
            Assert.That(opacity, Is.EqualTo(1.800801186f).Within(1e-5f));
        }

        [Test]
        public void Given_ARepeatingSpring_When_TwoStepsRun_Then_ItsIntegratorCarriesTheVelocityBetweenThem()
        {
            // Arrange
            var element = new VisualElement();
            var state = Opacity(new MotionRepeat(1f, TransitionRepeatType.Loop, 0f), element);

            // Act — NaN for a plan that resolved no channel.
            var (velocity, expected) = (float.NaN, 0f);
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 0.1f);
                var first = state.Opacity!.Integrator.Value;
                MotionSpringDriver.Step(element, state, 0.05f);
                velocity = state.Opacity.Integrator.Velocity;
                expected = (state.Opacity.Integrator.Value - first) / 0.05f;
            }

            // Assert
            Assert.That(velocity, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void Given_ASpringExitPartWay_When_ItIsRetargeted_Then_TheNextFrameCarriesOnFromWhereItWas()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass);

            // Act — NaN for a plan that resolved no channel.
            var jump = float.NaN;
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 0.3f);
                var before = state.Opacity!.Integrator.Value;
                MotionSpringDriver.Retarget(state);
                MotionSpringDriver.Step(element, state, 1f / 60f);
                jump = Mathf.Abs(state.Opacity.Integrator.Value - before);
            }

            // Assert — a frame of motion, not a jump back to the resting value.
            Assert.That(jump, Is.LessThan(0.1f));
        }

        [Test]
        public void Given_AnOddReverseRepeatingSpringExit_When_ItIsRetargeted_Then_TheReversalEndsOnItsTarget()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass,
                new MotionRepeat(1f, TransitionRepeatType.Reverse, 0f));

            // Act — a plan that resolved no channel reads true, the answer the repeat alone would give.
            var endsAtFrom = true;
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 0.3f);
                MotionSpringDriver.Retarget(state);
                endsAtFrom = MotionSpringDriver.EndsAtFrom(state);
            }

            // Assert
            Assert.That(endsAtFrom, Is.False);
        }

        [Test]
        public void Given_ARepeatingSpringExit_When_ItIsRetargeted_Then_TheReversalSettlesLikeASpringThatDoesNotRepeat()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = MotionSpringDriver.Create(plan, Stiffness, Damping, Mass,
                new MotionRepeat(float.PositiveInfinity, TransitionRepeatType.Loop, 0f));

            // Act — integrated a frame at a time, as the scheduler's tick steps it.
            var settled = false;
            if (state != null)
            {
                MotionSpringDriver.Step(element, state, 0.3f);
                MotionSpringDriver.Retarget(state);
                for (var i = 0; i < 600 && !settled; i++)
                {
                    settled = MotionSpringDriver.Step(element, state, 1f / 60f);
                }
            }

            // Assert
            Assert.That(settled, Is.True);
        }
    }
}
