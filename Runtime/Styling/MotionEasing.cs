#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    internal static class MotionEasing
    {
        private static readonly KeywordCurves s_keywords =
            new(new BezierCurve(0.42f, 0f, 1f, 1f), new BezierCurve(0f, 0f, 0.58f, 1f), new BezierCurve(0.42f, 0f, 0.58f, 1f));

        // backOut. back.ts makes backIn its reverse and backInOut backIn's mirror; a cubic-bezier reversed is the one
        // through its control points turned about (0.5, 0.5).
        private static readonly BezierCurve s_backOut = new(0.33f, 1.53f, 0.69f, 0.99f);
        private static readonly BezierCurve s_backIn =
            new(1f - s_backOut.X2, 1f - s_backOut.Y2, 1f - s_backOut.X1, 1f - s_backOut.Y1);

        // waapi/easing/supported.ts's backIn, circIn and circOut.
        private static readonly BezierCurve s_acceleratedBackIn = new(0.31f, 0.01f, 0.66f, -0.59f);
        private static readonly BezierCurve s_acceleratedCircIn = new(0f, 0.65f, 0.55f, 1f);
        private static readonly BezierCurve s_acceleratedCircOut = new(0.55f, 0f, 1f, 0.45f);

        private static readonly (EasingMode Mode, Func<float, float>? Curve, Func<float, float>? Accelerated)[] s_named =
        {
            Named(EasingMode.EaseIn),
            Named(EasingMode.EaseOut),
            Named(EasingMode.EaseInOut),
            Named(EasingMode.EaseInBack),
            Named(EasingMode.EaseOutBack),
            Named(EasingMode.EaseInOutBack),
            Named(EasingMode.EaseInCirc),
            Named(EasingMode.EaseOutCirc),
        };

        private static (EasingMode, Func<float, float>?, Func<float, float>?) Named(EasingMode mode)
            => (mode, CurveOf(mode, accelerated: false), CurveOf(mode, accelerated: true));

        private static Func<float, float>? CurveOf(EasingMode mode, bool accelerated)
            => FunctionOf(mode, accelerated) is { } function ? function.Evaluate : null;

        // The function a Motion transition eases mode by, null where UI Toolkit's curve for it stands: EaseIn, EaseOut and
        // EaseInOut take Framer Motion's easeIn, easeOut and easeInOut, the cubic-beziers motion-utils' ease.ts declares,
        // and EaseInBack, EaseOutBack and EaseInOutBack its backIn, backOut and backInOut (back.ts), where UI Toolkit's
        // transition eases those modes by quadratics and by back curves of its own (UssEasing). A value Framer hands the
        // Web Animations API (accelerated) takes the CSS curves its supportedWaapiEasing table names for backIn, circIn
        // and circOut.
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
                case EasingMode.EaseInCirc:
                    return accelerated ? s_acceleratedCircIn : null;
                case EasingMode.EaseOutCirc:
                    return accelerated ? s_acceleratedCircOut : null;
                default:
                    return s_keywords.For(mode);
            }
        }

        internal static float Evaluate(EasingMode mode, float t, bool accelerated = false)
            => FunctionOf(mode, accelerated) is { } function ? function.Evaluate(t) : UssEasing.Evaluate(mode, t);

        // The mode a keyframe of a sequence segment Framer hands the Web Animations API eases by: map-easing.ts looks
        // each named easing of the keyframes' array up in supportedWaapiEasing, and takes ease-out for backInOut and
        // circInOut, which that table leaves out.
        internal static EasingMode PerKeyframeAccelerated(EasingMode mode)
            => mode == EasingMode.EaseInOutBack || mode == EasingMode.EaseInOutCirc ? EasingMode.EaseOut : mode;

        // The curve a transition UI Toolkit runs on its own curve for one of the modes above eases by instead, null where
        // that is the engine's own. That curve is told by its values at two points against UssEasing's, which
        // UssEasingTests holds within 1e-6 of the engine's for every mode: a curve within 1e-5 of one of those modes at
        // both is taken for it, and any other gets null.
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
