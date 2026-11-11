#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // A Motion transition eases EaseIn, EaseOut and EaseInOut by Framer Motion's easeIn, easeOut and easeInOut, the
    // cubic-beziers motion-utils' ease.ts declares, and EaseInBack, EaseOutBack and EaseInOutBack by its backIn, backOut
    // and backInOut (back.ts), where UI Toolkit's transition eases those modes by quadratics and by back curves of its
    // own (UssEasing). Every other mode keeps UI Toolkit's curve.
    internal static class MotionEasing
    {
        private static readonly KeywordCurves s_keywords =
            new(new BezierCurve(0.42f, 0f, 1f, 1f), new BezierCurve(0f, 0f, 0.58f, 1f), new BezierCurve(0.42f, 0f, 0.58f, 1f));

        // backOut. back.ts makes backIn its reverse and backInOut backIn's mirror; a cubic-bezier reversed is the one
        // through its control points turned about (0.5, 0.5).
        private static readonly BezierCurve s_backOut = new(0.33f, 1.53f, 0.69f, 0.99f);
        private static readonly BezierCurve s_backIn =
            new(1f - s_backOut.X2, 1f - s_backOut.Y2, 1f - s_backOut.X1, 1f - s_backOut.Y1);

        private static readonly (EasingMode Mode, Func<float, float> Curve)[] s_named =
        {
            (EasingMode.EaseIn, t => Evaluate(EasingMode.EaseIn, t)),
            (EasingMode.EaseOut, t => Evaluate(EasingMode.EaseOut, t)),
            (EasingMode.EaseInOut, t => Evaluate(EasingMode.EaseInOut, t)),
            (EasingMode.EaseInBack, t => Evaluate(EasingMode.EaseInBack, t)),
            (EasingMode.EaseOutBack, t => Evaluate(EasingMode.EaseOutBack, t)),
            (EasingMode.EaseInOutBack, t => Evaluate(EasingMode.EaseInOutBack, t)),
        };

        internal static EaseFunction? FunctionOf(EasingMode mode)
        {
            switch (mode)
            {
                case EasingMode.EaseInBack:
                    return s_backIn;
                case EasingMode.EaseOutBack:
                    return s_backOut;
                case EasingMode.EaseInOutBack:
                    return EaseFunction.MirrorOf(s_backIn);
                default:
                    return s_keywords.For(mode);
            }
        }

        internal static float Evaluate(EasingMode mode, float t)
            => FunctionOf(mode) is { } function ? function.Evaluate(t) : UssEasing.Evaluate(mode, t);

        // The curve a transition UI Toolkit runs on its own curve for one of the six modes above eases by instead.
        // That curve is told by its values at two points against UssEasing's, which UssEasingTests holds within 1e-6
        // of the engine's for every mode: a curve within 1e-5 of one of the six at both is taken for it, and any
        // other gets null.
        internal static Func<float, float>? InPlaceOfEngineCurve(Func<float, float> engineCurve)
        {
            foreach (var (mode, curve) in s_named)
            {
                var distance = Math.Max(Math.Abs(engineCurve(0.3f) - UssEasing.Evaluate(mode, 0.3f)),
                    Math.Abs(engineCurve(0.7f) - UssEasing.Evaluate(mode, 0.7f)));
                // MUTANT_SURVIVES(equivalent, boundary): the engine's curve for the mode lies within 1e-6 of
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
