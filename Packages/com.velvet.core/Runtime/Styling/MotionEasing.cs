#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // A Motion transition eases EaseIn, EaseOut and EaseInOut by Framer Motion's easeIn, easeOut and easeInOut, the
    // cubic-beziers motion-utils' ease.ts declares, where UI Toolkit's transition eases those keywords by quadratics
    // (UssEasing). Every other mode keeps UI Toolkit's curve.
    internal static class MotionEasing
    {
        internal static readonly KeywordCurves Curves =
            new(new BezierCurve(0.42f, 0f, 1f, 1f), new BezierCurve(0f, 0f, 0.58f, 1f), new BezierCurve(0.42f, 0f, 0.58f, 1f));

        private static readonly (EasingMode Mode, Func<float, float> Curve)[] s_named =
        {
            (EasingMode.EaseIn, t => Evaluate(EasingMode.EaseIn, t)),
            (EasingMode.EaseOut, t => Evaluate(EasingMode.EaseOut, t)),
            (EasingMode.EaseInOut, t => Evaluate(EasingMode.EaseInOut, t)),
        };

        internal static float Evaluate(EasingMode mode, float t)
            => Curves.For(mode) is { } curve
                ? CubicBezierEvaluator.Evaluate(curve.X1, curve.Y1, curve.X2, curve.Y2, t)
                : UssEasing.Evaluate(mode, t);

        // The curve a transition UI Toolkit runs on its own curve for one of the three keywords eases by instead.
        // That curve is told by its values at two points against UssEasing's, which UssEasingTests holds within 1e-6
        // of the engine's for every mode: a curve within 1e-5 of one of the three at both is taken for it, and any
        // other gets null.
        internal static Func<float, float>? InPlaceOfEngineCurve(Func<float, float> engineCurve)
        {
            foreach (var (mode, curve) in s_named)
            {
                var distance = Math.Max(Math.Abs(engineCurve(0.3f) - UssEasing.Evaluate(mode, 0.3f)),
                    Math.Abs(engineCurve(0.7f) - UssEasing.Evaluate(mode, 0.7f)));
                // MUTANT_SURVIVES(equivalent, boundary): the engine's curve for the keyword lies within 1e-6 of
                // UssEasing's, so whether a distance of exactly 1e-5 matches decides nothing for it.
                if (distance < 1e-5f)
                {
                    return curve;
                }
            }
            return null;
        }
    }
}
