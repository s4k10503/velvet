using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the pure CSS <c>cubic-bezier(x1,y1,x2,y2)</c> evaluation (<see cref="CubicBezierEvaluator.Evaluate"/>):
    /// the boundary clamps, the linear/identity fast path, an exact front-loaded reference point that a keyword
    /// easing could not reproduce, an unclamped overshoot, solver monotonicity, and the reject-and-fall-back-to-
    /// default-curve path (with its one-shot warning) for both an out-of-range x1/x2 and a non-finite coordinate
    /// on any of the four axes. Panel-free by design — the evaluator is pure math with no
    /// <c>VisualElement</c>/panel dependency.
    /// </summary>
    /// <remarks>
    /// The reference midpoint (0.7756) was computed independently with a Python implementation of the same
    /// WebKit UnitBezier Newton/bisection algorithm every browser's <c>cubic-bezier()</c> is built on. GWT, one
    /// assert per case (Assume for preconditions). The warn-once static is reset before/after every test
    /// (reflection, mirroring DropShadowBakeTests) so the out-of-range cases stay deterministic regardless of
    /// run order or of another fixture having already tripped the same one-shot flag.
    /// </remarks>
    [TestFixture]
    internal sealed class CubicBezierEvaluatorTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly MethodInfo ResetWarnOnceMethod =
            typeof(CubicBezierEvaluator).GetMethod("ResetWarnOnceState", Priv);

        [SetUp]
        public void SetUp() => ResetWarnOnceMethod?.Invoke(null, null);

        [TearDown]
        public void TearDown() => ResetWarnOnceMethod?.Invoke(null, null);

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
    }
}
