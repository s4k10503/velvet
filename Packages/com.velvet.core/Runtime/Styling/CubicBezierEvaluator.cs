using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Evaluates a CSS <c>cubic-bezier(x1,y1,x2,y2)</c> easing curve EXACTLY, the way every browser's
    /// <c>cubic-bezier()</c> does — a fixed-duration tween's easing sampled through the real spec algorithm
    /// rather than approximated by one of UI Toolkit's five <c>EasingMode</c> keywords (which cannot express an
    /// arbitrary numeric curve). Pure math: no <c>VisualElement</c> / panel dependency and zero allocations, so
    /// it can be exercised directly — see <see cref="BezierTweenDriver"/> for the piece that applies a sampled
    /// value to a Motion's animated style properties.
    /// </summary>
    /// <remarks>
    /// The endpoints are fixed at (0,0) and (1,1); the two control points (x1,y1) and (x2,y2) shape the curve.
    /// A CSS timing function is a function of TIME, so x must stay monotone: an x1/x2 outside [0,1] makes the
    /// whole curve invalid per the <c>cubic-bezier()</c> spec, exactly like a browser rejecting the declaration —
    /// <see cref="Evaluate"/> warns once and falls back to the default curve instead of clamping the offending
    /// coordinate into range. y is left UNCLAMPED so an overshoot/anticipate curve (a control point with y
    /// outside [0,1]) genuinely passes its target mid-tween instead of being flattened.
    /// </remarks>
    internal static class CubicBezierEvaluator
    {
        // Newton-Raphson converges quadratically for the well-conditioned curves a UI easing uses, so a small
        // fixed budget is plenty; bisection picks up the rare flat-derivative case the fallback exists for.
        private const int NewtonIterations = 8;
        private const int BisectionIterations = 32;

        // The x-solve residual is in [0,1] units, so this absolute tolerance is already well below one output
        // step of any realistic tween duration.
        private const float ConvergenceEpsilon = 1e-6f;

        // Below this slope Newton's correction (residual / derivative) explodes or reverses; hand off to
        // bisection instead of taking a wild step.
        private const float MinSlope = 1e-6f;

        // The fallback curve for an invalid x1/x2 — Tailwind's own default, and the same default
        // StyleTransitionConfig.BezierX1..Y2 already carry, so a rejected value degrades to what an
        // unconfigured Motion would have played anyway rather than an arbitrary hardcoded curve.
        private const float DefaultX1 = 0.4f;
        private const float DefaultY1 = 0f;
        private const float DefaultX2 = 0.2f;
        private const float DefaultY2 = 1f;

        // Evaluate runs on every tick of a running tween (up to 60/sec), so a per-call warning would spam the
        // console for the whole animation instead of surfacing the misconfiguration once. Reset alongside every
        // other subsystem's warn-once state on domain reload / play mode entry.
        private static bool s_warnedInvalidControlPoints;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarnOnceState() => s_warnedInvalidControlPoints = false;
#endif

        /// <summary>
        /// Maps a normalized progress <paramref name="t"/> in [0,1] to its eased output. Endpoints short-circuit
        /// (t&lt;=0 -&gt; 0, t&gt;=1 -&gt; 1); the identity curve (x1==y1 &amp;&amp; x2==y2, e.g. the linear
        /// <c>cubic-bezier(0,0,1,1)</c>) returns <paramref name="t"/> directly.
        /// </summary>
        public static float Evaluate(float x1, float y1, float x2, float y2, float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }
            if (t >= 1f)
            {
                return 1f;
            }

            // A CSS timing function must be monotone in time, so an x1/x2 outside [0,1] makes the WHOLE curve
            // invalid — not just the offending coordinate — the same way a browser rejects the entire
            // cubic-bezier() declaration rather than clamping one axis into range. Finiteness is tested on ALL
            // four control coordinates, not just the x's: every comparison against NaN is false, so a range check
            // alone would wave a non-finite point straight through — and a NaN in y1/y2 clears the x-only range
            // test yet still poisons SampleCurve(y1,y2,s) into a NaN output. Guarding every coordinate keeps
            // Evaluate self-safe regardless of what a caller passes.
            if (!float.IsFinite(x1) || !float.IsFinite(y1) || !float.IsFinite(x2) || !float.IsFinite(y2)
                || x1 < 0f || x1 > 1f || x2 < 0f || x2 > 1f)
            {
                WarnInvalidControlPoints(x1, y1, x2, y2);
                x1 = DefaultX1;
                y1 = DefaultY1;
                x2 = DefaultX2;
                y2 = DefaultY2;
            }

            // When the x and y control coordinates coincide the x-curve equals the y-curve, so solving x(s)=t and
            // evaluating y(s) just returns t — the linear/identity fast path, and correct for any such curve.
            if (x1 == y1 && x2 == y2)
            {
                return t;
            }

            var s = SolveForParam(x1, x2, t);
            return SampleCurve(y1, y2, s);
        }

        private static void WarnInvalidControlPoints(float x1, float y1, float x2, float y2)
        {
            if (s_warnedInvalidControlPoints)
            {
                return;
            }
            s_warnedInvalidControlPoints = true;
            FiberLogger.LogWarning("Bezier",
                $"cubic-bezier control points (x1={x1}, y1={y1}, x2={x2}, y2={y2}) are invalid — a timing " +
                "function's x must stay within [0,1] to be monotone in time, and every coordinate must be finite. " +
                "Falling back to the default curve, cubic-bezier(0.4, 0, 0.2, 1).");
        }

        // Bézier with endpoints fixed at 0 and 1 collapses to the cubic polynomial (a*s + b)*s*s + c*s, whose
        // coefficients depend only on the two intermediate control coordinates. Shared by both axes.
        private static float SampleCurve(float c1, float c2, float s)
        {
            var c = 3f * c1;
            var b = 3f * (c2 - c1) - c;
            var a = 1f - c - b;
            return ((a * s + b) * s + c) * s;
        }

        private static float SampleDerivative(float c1, float c2, float s)
        {
            var c = 3f * c1;
            var b = 3f * (c2 - c1) - c;
            var a = 1f - c - b;
            return (3f * a * s + 2f * b) * s + c;
        }

        // Inverts x(s)=targetX for the curve parameter s. Newton-Raphson seeded at s=targetX (a good guess since
        // x is monotone over [0,1]); on a too-flat derivative it falls back to bisection, which is bounded rather
        // than looping to float underflow so a pathological curve can never spin here forever.
        private static float SolveForParam(float x1, float x2, float targetX)
        {
            var s = targetX;
            for (var i = 0; i < NewtonIterations; i++)
            {
                var residual = SampleCurve(x1, x2, s) - targetX;
                if (Mathf.Abs(residual) < ConvergenceEpsilon)
                {
                    return s;
                }
                var slope = SampleDerivative(x1, x2, s);
                if (Mathf.Abs(slope) < MinSlope)
                {
                    break;
                }
                s -= residual / slope;
            }

            var low = 0f;
            var high = 1f;
            s = targetX;
            for (var i = 0; i < BisectionIterations && low < high; i++)
            {
                var x = SampleCurve(x1, x2, s);
                if (Mathf.Abs(x - targetX) < ConvergenceEpsilon)
                {
                    return s;
                }
                if (targetX > x)
                {
                    low = s;
                }
                else
                {
                    high = s;
                }
                s = (high - low) * 0.5f + low;
            }
            return s;
        }
    }
}
