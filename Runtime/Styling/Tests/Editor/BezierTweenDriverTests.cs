using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the bezier-driven variant enter/exit path end to end: the pure CSS <c>cubic-bezier(x1,y1,x2,y2)</c>
    /// evaluation (<see cref="CubicBezierEvaluator.Evaluate"/>) — boundary clamps, the linear/identity fast path,
    /// an exact front-loaded reference point no keyword easing could reproduce, unclamped overshoot, solver
    /// monotonicity, and the reject-and-fall-back-to-default-curve path (with its one-shot warning) for an
    /// out-of-range or non-finite control point; validation of user-supplied bezier parameters
    /// (<c>StyleTransitionConfig.Type == Bezier</c>) mirroring the spring path's guard, since a non-finite control
    /// point would propagate into every inline style write and never reach the target, leaving the completion
    /// callback that removes a presence exit's ghost never firing; that a custom curve survives the config's copy
    /// builders unchanged, since forgetting one of the four <c>Bezier*</c> fields in an object-initializer rebuild
    /// would silently reset a caller's curve to the default; and the driven tween itself
    /// (<see cref="BezierTweenDriver.Step"/>), which moves the inline style along the exact curve and reports done
    /// once elapsed reaches the fixed duration, with an exit-cancel retarget
    /// (<see cref="BezierTweenDriver.Retarget"/>) restarting a fresh full-duration reversal from the current value.
    /// </summary>
    /// <remarks>
    /// Panel-free by design, exactly like <see cref="MotionSpringDriverTests"/>: the scheduler's bezier path never
    /// reads <c>resolvedStyle</c> (numeric from/to values come from <see cref="MotionSpringClassParser"/>'s
    /// class-name parsing), and the recurring tick it registers needs a live panel clock the EditMode PlayerLoop
    /// never drives — so the tick's own math is exercised by calling <see cref="BezierTweenDriver.Step"/> directly
    /// in a loop. The reference midpoint (0.7756) was computed independently with a Python implementation of the
    /// same WebKit UnitBezier Newton/bisection algorithm every browser's <c>cubic-bezier()</c> is built on. The
    /// warn-once static is reset before/after every test (reflection, mirroring DropShadowBakeTests) so the
    /// out-of-range cases stay deterministic regardless of run order or of another fixture having already tripped
    /// the same one-shot flag. GWT, one assert per case (Assume for preconditions).
    /// </remarks>
    [TestFixture]
    internal sealed class BezierTweenDriverTests
    {
        private const float FixedDeltaSec = 1f / 60f;

        // Tailwind's own default curve, cubic-bezier(0.4,0,0.2,1).
        private const float X1 = 0.4f;
        private const float Y1 = 0f;
        private const float X2 = 0.2f;
        private const float Y2 = 1f;

        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly MethodInfo ResetWarnOnceMethod =
            typeof(CubicBezierEvaluator).GetMethod("ResetWarnOnceState", Priv);

        [SetUp]
        public void SetUp() => ResetWarnOnceMethod?.Invoke(null, null);

        [TearDown]
        public void TearDown() => ResetWarnOnceMethod?.Invoke(null, null);

        [Test]
        public void Given_ABezierVariantEnter_When_Started_Then_ClassesLandAtRestWithInlineOpacityAtTheFromValue()
        {
            // Arrange — the element already carries the resting (to) class, matching PlayVariantEnter's
            // precondition (the factory / reconciler create it with variants[animate] applied before calling this).
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            element.AddToClassList("opacity-100");

            // Act — a bezier enter from "hidden" (opacity-0) to the already-applied "visible" (opacity-100).
            scheduler.PlayVariantEnter(element, fromClasses: new[] { "opacity-0" }, toClasses: new[] { "opacity-100" },
                durationSec: 0.3f, easing: EasingMode.EaseOut, delaySec: 0f,
                type: TransitionType.Bezier, bezierX1: X1, bezierY1: Y1, bezierX2: X2, bezierY2: Y2);

            // Assert — the class swap resolved immediately (opacity-100 present, opacity-0 never added), and the
            // inline style shows the FROM pose synchronously so the element does not flash at the resting value.
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0"), element.style.opacity.value),
                Is.EqualTo((true, false, 0f)));
        }

        [Test]
        public void Given_ABezierDrivenOpacityChannel_When_SteppedRepeatedly_Then_TheInlineOpacityMovesTowardTheTarget()
        {
            // Arrange — an opacity bezier starting at 0, heading to 1 over a full second (so neither sample below
            // has reached the end yet).
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            Assume.That(element.style.opacity.value, Is.EqualTo(0f), "Precondition: starts at the from-value");

            // Act — a handful of early ticks, then many more later ticks.
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            var earlyOpacity = element.style.opacity.value;
            for (var i = 0; i < 40; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            var laterOpacity = element.style.opacity.value;

            // Assert — opacity has moved further toward the target (1) as more ticks run.
            Assert.That(laterOpacity, Is.GreaterThan(earlyOpacity));
        }

        [Test]
        public void Given_ABezierDrivenOpacityChannel_When_SteppedUntilElapsedReachesDuration_Then_TheOpacityRestsExactlyAtTheTarget()
        {
            // Arrange
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 0.3f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);

            // Act — step until elapsed reaches the fixed duration (the cap only guards against a regression that
            // never completes).
            var completed = false;
            for (var i = 0; i < 600 && !completed; i++)
            {
                completed = BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(completed, Is.True, "Precondition: the tween completed within the tick budget");

            // Assert — the t>=1 early-return is exact, so the value rests EXACTLY at the target (tighter than a
            // spring's convergence-based settle).
            Assert.That(element.style.opacity.value, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ASettledBezierChannel_When_InlineOverridesCleared_Then_TheOpacityStyleIsRemoved()
        {
            // Arrange — run the tween to completion first (Assume guards the precondition).
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 0.3f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            var completed = false;
            for (var i = 0; i < 600 && !completed; i++)
            {
                completed = BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(completed, Is.True, "Precondition: the tween completed within the tick budget");

            // Act — the scheduler calls this on completion so the resting classes' own opacity takes back over.
            BezierTweenDriver.ClearInlineOverrides(element, state!);

            // Assert
            Assert.That(element.style.opacity.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ABezierChannelMidExit_When_Retargeted_Then_ItHeadsBackTowardTheValueItStartedFrom()
        {
            // Arrange — an exit-shaped channel: starts at the resting value (1, opaque) and heads to the exit
            // value (0). A few ticks in, it is partway there but not yet done.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(state!.Opacity!.To, Is.EqualTo(0f), "Precondition: heading toward the exit value");

            // Act — an exit-cancel (the key re-entered mid-exit): retarget back toward the resting value.
            BezierTweenDriver.Retarget(state!);

            // Assert — the channel's goal flipped to the value it originally started from (its RestingTarget).
            Assert.That(state!.Opacity!.To, Is.EqualTo(1f));
        }

        [Test]
        public void Given_ABezierChannelMidExit_When_Retargeted_Then_ElapsedResetsToZero()
        {
            // Arrange — pins the "fresh full-duration reversal" design decision explicitly (no spring analog):
            // a retarget restarts the clock rather than replaying time in reverse.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-100" }, new[] { "opacity-0" });
            var state = BezierTweenDriver.Create(plan, X1, Y1, X2, Y2, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");
            BezierTweenDriver.ApplyCurrentValues(element, state!);
            for (var i = 0; i < 5; i++)
            {
                BezierTweenDriver.Step(element, state!, FixedDeltaSec);
            }
            Assume.That(state!.ElapsedSec, Is.GreaterThan(0f), "Precondition: the forward tween has advanced");

            // Act
            BezierTweenDriver.Retarget(state!);

            // Assert — the reversal starts from a clean clock.
            Assert.That(state!.ElapsedSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_AnOvershootBezierCurve_When_SteppedPartway_Then_TheInlineValueExceedsTheTargetMomentarily()
        {
            // Arrange — an overshoot curve (y1 past 1) driving opacity 0→1. This is the regression pin that
            // would catch someone substituting Mathf.Lerp for Mathf.LerpUnclamped: a clamp would silently flatten
            // the overshoot and this assert would go RED.
            var element = new VisualElement();
            var plan = MotionSpringClassParser.Resolve(new[] { "opacity-0" }, new[] { "opacity-100" });
            var state = BezierTweenDriver.Create(plan, 0.34f, 1.56f, 0.64f, 1f, durationSec: 1f);
            Assume.That(state, Is.Not.Null, "Precondition: the plan resolves an opacity channel");

            // Act — advance to 60% of the duration, where the overshoot curve is already past its target.
            BezierTweenDriver.Step(element, state!, 0.6f);

            // Assert — the inline value momentarily exceeds the target (1), i.e. it actually overshoots.
            Assert.That(element.style.opacity.value, Is.GreaterThan(1f));
        }

        [Test]
        public void Given_TInputZero_When_Evaluated_Then_ReturnsZero()
        {
            // Arrange / Act — the lower boundary short-circuits regardless of the curve.
            var output = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0f);

            // Assert
            Assert.That(output, Is.EqualTo(0f));
        }

        [Test]
        public void Given_TInputOne_When_Evaluated_Then_ReturnsOne()
        {
            // Arrange / Act — the upper boundary short-circuits regardless of the curve.
            var output = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 1f);

            // Assert
            Assert.That(output, Is.EqualTo(1f));
        }

        [Test]
        public void Given_TheLinearIdentityCurve_When_EvaluatedAtAnArbitraryT_Then_TheOutputEqualsTheInput()
        {
            // Arrange / Act — cubic-bezier(0,0,1,1) is the identity curve: the fast path returns t directly.
            var output = CubicBezierEvaluator.Evaluate(0f, 0f, 1f, 1f, 0.37f);

            // Assert
            Assert.That(output, Is.EqualTo(0.37f).Within(1e-4f));
        }

        [Test]
        public void Given_TailwindsDefaultCurve_When_EvaluatedAtItsMidpoint_Then_ItFrontLoadsPastTheLinearMidpoint()
        {
            // Arrange / Act — Tailwind's own default curve, cubic-bezier(0.4,0,0.2,1). The whole reason this
            // exists: at the temporal midpoint the eased progress is ~0.776, NOT the 0.5 the closest keyword
            // (ease-in-out, symmetric) would give — an exact curve no EasingMode keyword can express.
            var output = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0.5f);

            // Assert
            Assert.That(output, Is.EqualTo(0.7756f).Within(0.001f));
        }

        [Test]
        public void Given_AnOvershootCurve_When_EvaluatedPastItsPeak_Then_TheOutputExceedsOne()
        {
            // Arrange / Act — a back/anticipate curve whose control points push y past 1; the evaluator leaves
            // y1/y2 unclamped, so the eased output genuinely overshoots its target mid-curve.
            var output = CubicBezierEvaluator.Evaluate(0.34f, 1.56f, 0.64f, 1f, 0.6f);

            // Assert
            Assert.That(output, Is.GreaterThan(1f));
        }

        [Test]
        public void Given_AMonotonicEaseCurve_When_EvaluatedAtIncreasingInputs_Then_TheOutputNeverDecreases()
        {
            // Arrange — a solver-robustness sweep (independent of any hand-computed value): a well-behaved ease
            // curve must map increasing time to non-decreasing output across the whole range.
            var nonDecreasing = true;
            var previous = float.NegativeInfinity;

            // Act — step-0.05 sweep folded into one boolean.
            for (var t = 0f; t <= 1f + 1e-4f; t += 0.05f)
            {
                var output = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, t);
                if (output < previous - 1e-5f)
                {
                    nonDecreasing = false;
                }
                previous = output;
            }

            // Assert
            Assert.That(nonDecreasing, Is.True);
        }

        [Test]
        public void Given_AnX1AboveOne_When_Evaluated_Then_TheOutputMatchesTheDefaultCurveInsteadOfClamping()
        {
            // Arrange — an x1 above 1 is invalid (a timing function's x must stay monotone); silently clamping
            // it to 1 would evaluate a DIFFERENT curve, cubic-bezier(1,0,0.2,1), not the default one. The
            // reference is a valid call, so it logs nothing.
            var expected = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0.5f);
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act
            var output = CubicBezierEvaluator.Evaluate(2f, 0f, 0.2f, 1f, 0.5f);

            // Assert
            Assert.That(output, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ANaNXControlPoint_When_Evaluated_Then_TheOutputMatchesTheDefaultCurveInsteadOfPassingNaNThrough()
        {
            // Arrange — a NaN control point is not caught by an ordinary out-of-range comparison (every
            // comparison against NaN is false), so without an explicit finiteness check the NaN would flow
            // straight into the solver and poison the output; the finite fallback must degrade it to the default
            // curve exactly like an out-of-range value. The reference is a valid call, so it logs nothing.
            var expected = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0.5f);
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act
            var output = CubicBezierEvaluator.Evaluate(float.NaN, 0f, 0.2f, 1f, 0.5f);

            // Assert
            Assert.That(output, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ANaNYControlPoint_When_Evaluated_Then_TheOutputMatchesTheDefaultCurveInsteadOfPassingNaNThrough()
        {
            // Arrange — a NaN in y1/y2 survives the x-axis range/finiteness test (its x's are valid) but still
            // poisons SampleCurve(y1,y2,s) into a NaN output, so the finiteness guard has to cover every
            // coordinate, not just the two x's. With a valid x pair here, only guarding y catches this — it must
            // degrade to the default curve and warn exactly like any other invalid control point. The reference
            // is a valid call, so it logs nothing.
            var expected = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0.5f);
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act
            var output = CubicBezierEvaluator.Evaluate(0.4f, float.NaN, 0.2f, 1f, 0.5f);

            // Assert
            Assert.That(output, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ASecondInvalidCall_When_Evaluated_Then_ItStaysSilentButStillFallsBack()
        {
            // Arrange — Evaluate runs on every tick of a running tween (up to 60/sec); the first invalid call
            // consumes the one-shot warning so a whole animation's worth of subsequent ticks does not spam it.
            // No LogAssert.Expect is registered for the second call below, so an unexpected repeat warning would
            // fail this test under the project's strict LogAssert mode.
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));
            CubicBezierEvaluator.Evaluate(2f, 0f, 0.2f, 1f, 0.3f);
            var expected = CubicBezierEvaluator.Evaluate(0.4f, 0f, 0.2f, 1f, 0.6f);

            // Act
            var output = CubicBezierEvaluator.Evaluate(2f, 0f, 0.2f, 1f, 0.6f);

            // Assert — the fallback still applies even though the diagnostic stays silent the second time.
            Assert.That(output, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ANaNBezierControlPoint_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act — a NaN control point would propagate NaN into every style write.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0.3f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: float.NaN, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert — the play degrades to an immediate completion instead of a forever-tick.
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_ANegativeBezierDuration_When_AVariantEnterPlays_Then_ItWarnsAndCompletesImmediately()
        {
            // Arrange
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;
            LogAssert.Expect(LogType.Warning, new Regex("[Bb]ezier"));

            // Act — a negative duration is out of the accepted range (unlike a zero, which is intentional).
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                -1f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: 0.4f, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }

        [Test]
        public void Given_AZeroBezierDuration_When_AVariantEnterPlays_Then_ItCompletesImmediatelyWithoutWarning()
        {
            // Arrange — no LogAssert.Expect: a zero duration is an intentional no-animation, so any warning here
            // would fail the test under NUnit's strict LogAssert mode.
            var scheduler = new StyleAnimationScheduler();
            var element = new VisualElement();
            var completed = false;

            // Act — a zero duration degrades exactly like StyleTransitionConfig.None: silent immediate complete.
            scheduler.PlayVariantEnter(element, new[] { "opacity-0" }, new[] { "opacity-100" },
                0f, EasingMode.Linear, 0f, onComplete: () => completed = true,
                additionalDelaySec: 0f, propertyOverrides: null,
                type: TransitionType.Bezier, bezierX1: 0.4f, bezierY1: 0f, bezierX2: 0.2f, bezierY2: 1f);

            // Assert
            Assert.That(completed, Is.True);
        }

        // A deliberately non-default curve, so a reset back to the (0.4,0,0.2,1) default would be visible.
        private static StyleTransitionConfig CustomBezier() => new()
        {
            Type = TransitionType.Bezier,
            DurationSec = 0.3f,
            BezierX1 = 0.11f,
            BezierY1 = 0.22f,
            BezierX2 = 0.33f,
            BezierY2 = 0.44f,
        };

        [Test]
        public void Given_ACustomBezierConfig_When_WithIsCalled_Then_TheCurveSurvivesUnchanged()
        {
            // Arrange
            var config = CustomBezier();

            // Act — With() only tunes the top-level timing; the curve must pass through untouched.
            var result = config.With(durationSec: 0.5f);

            // Assert
            Assert.That(
                (result.BezierX1, result.BezierY1, result.BezierX2, result.BezierY2),
                Is.EqualTo((0.11f, 0.22f, 0.33f, 0.44f)));
        }

        [Test]
        public void Given_ACustomBezierConfig_When_WithExitClassesIsCalled_Then_TheCurveSurvivesUnchanged()
        {
            // Arrange
            var config = CustomBezier();

            // Act — WithExitClasses() replaces only the exit class pair; the curve must pass through untouched.
            var result = config.WithExitClasses("opacity-100", "opacity-0");

            // Assert
            Assert.That(
                (result.BezierX1, result.BezierY1, result.BezierX2, result.BezierY2),
                Is.EqualTo((0.11f, 0.22f, 0.33f, 0.44f)));
        }
    }
}
