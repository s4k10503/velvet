#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // A Motion transition eases EaseIn, EaseOut and EaseInOut by Framer Motion's easeIn, easeOut and easeInOut, the
    // cubic-beziers motion-utils' ease.ts declares, and EaseInBack, EaseOutBack and EaseInOutBack by its backIn, backOut
    // and backInOut (back.ts), where UI Toolkit's transition eases those modes by quadratics and by back curves of its
    // own (UssEasing). Every other mode keeps UI Toolkit's curve.
    //
    // A value Framer hands the Web Animations API (accelerated) eases backIn by the CSS curve its supportedWaapiEasing
    // table names, cubic-bezier(0.31, 0.01, 0.66, -0.59), rather than by back.ts's.
    internal static class MotionEasing
    {
        private static readonly KeywordCurves s_keywords =
            new(new BezierCurve(0.42f, 0f, 1f, 1f), new BezierCurve(0f, 0f, 0.58f, 1f), new BezierCurve(0.42f, 0f, 0.58f, 1f));

        // backOut. back.ts makes backIn its reverse and backInOut backIn's mirror; a cubic-bezier reversed is the one
        // through its control points turned about (0.5, 0.5).
        private static readonly BezierCurve s_backOut = new(0.33f, 1.53f, 0.69f, 0.99f);
        private static readonly BezierCurve s_backIn =
            new(1f - s_backOut.X2, 1f - s_backOut.Y2, 1f - s_backOut.X1, 1f - s_backOut.Y1);

        // waapi/easing/supported.ts's backIn.
        private static readonly BezierCurve s_acceleratedBackIn = new(0.31f, 0.01f, 0.66f, -0.59f);

        private static readonly (EasingMode Mode, Func<float, float> Curve, Func<float, float> Accelerated)[] s_named =
        {
            Named(EasingMode.EaseIn),
            Named(EasingMode.EaseOut),
            Named(EasingMode.EaseInOut),
            Named(EasingMode.EaseInBack),
            Named(EasingMode.EaseOutBack),
            Named(EasingMode.EaseInOutBack),
        };

        private static (EasingMode, Func<float, float>, Func<float, float>) Named(EasingMode mode)
            => (mode, t => Evaluate(mode, t), t => Evaluate(mode, t, accelerated: true));

        internal static EaseFunction? FunctionOf(EasingMode mode, bool accelerated)
        {
            switch (mode)
            {
                case EasingMode.EaseInBack:
                    return accelerated ? s_acceleratedBackIn : s_backIn;
                case EasingMode.EaseOutBack:
                    return s_backOut;
                case EasingMode.EaseInOutBack:
                    return EaseFunction.MirrorOf(s_backIn);
                default:
                    return s_keywords.For(mode);
            }
        }

        internal static float Evaluate(EasingMode mode, float t, bool accelerated = false)
            => FunctionOf(mode, accelerated) is { } function ? function.Evaluate(t) : UssEasing.Evaluate(mode, t);

        // The curve a transition UI Toolkit runs on its own curve for one of the six modes above eases by instead.
        // That curve is told by its values at two points against UssEasing's, which UssEasingTests holds within 1e-6
        // of the engine's for every mode: a curve within 1e-5 of one of the six at both is taken for it, and any
        // other gets null.
        internal static Func<float, float>? InPlaceOfEngineCurve(Func<float, float> engineCurve, bool accelerated)
        {
            foreach (var (mode, curve, acceleratedCurve) in s_named)
            {
                var distance = Math.Max(Math.Abs(engineCurve(0.3f) - UssEasing.Evaluate(mode, 0.3f)),
                    Math.Abs(engineCurve(0.7f) - UssEasing.Evaluate(mode, 0.7f)));
                // MUTANT_SURVIVES(equivalent, boundary): the engine's curve for the mode lies within 1e-6 of
                // UssEasing's, so whether a distance of exactly 1e-5 matches decides nothing for it.
                if (distance < 1e-5f)
                {
                    return accelerated ? acceleratedCurve : curve;
                }
            }
            return null;
        }
    }
}
