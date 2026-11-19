using System;
using UnityEngine;
using UnityEngine.UIElements;
using UitkEasing = UnityEngine.UIElements.Experimental.Easing;

namespace Velvet
{
    // The curve UI Toolkit eases a USS transition by for each EasingMode, for a driver that steps a value
    // itself and has to land on the curve the engine's own transition takes. UssEasingTests compares every
    // mode against UI Toolkit's own mapping.
    internal static class UssEasing
    {
        // Indexed by EasingMode; UI Toolkit eases a value outside the enum as Ease.
        private static readonly Func<float, float>[] s_curves =
        {
            t => t * (1.8f + t * (-0.6f + t * -0.2f)),
            UitkEasing.InQuad,
            UitkEasing.OutQuad,
            UitkEasing.InOutQuad,
            UitkEasing.Linear,
            UitkEasing.InSine,
            UitkEasing.OutSine,
            UitkEasing.InOutSine,
            UitkEasing.InCubic,
            UitkEasing.OutCubic,
            UitkEasing.InOutCubic,
            UitkEasing.InCirc,
            UitkEasing.OutCirc,
            UitkEasing.InOutCirc,
            UitkEasing.InElastic,
            UitkEasing.OutElastic,
            UitkEasing.InOutElastic,
            UitkEasing.InBack,
            UitkEasing.OutBack,
            UitkEasing.InOutBack,
            UitkEasing.InBounce,
            UitkEasing.OutBounce,
            UitkEasing.InOutBounce,
        };

        public static float Evaluate(EasingMode mode, float t)
        {
            var index = (int)mode;
            // MUTANT_SURVIVES(equivalent, boundary): index 0 is Ease, which the fallback picks as well.
            var curve = index >= 0 && index < s_curves.Length ? s_curves[index] : s_curves[0];
            return curve(Mathf.Clamp01(t));
        }

        // Whether a curve is taken for the one UI Toolkit eases mode by: within 1e-5 of this class's at 0.3 and 0.7, which
        // UssEasingTests holds within 1e-6 of the engine's for every mode.
        public static bool Is(Func<float, float> curve, EasingMode mode)
        {
            var distance = Math.Max(Math.Abs(curve(0.3f) - Evaluate(mode, 0.3f)), Math.Abs(curve(0.7f) - Evaluate(mode, 0.7f)));
            // MUTANT_SURVIVES(equivalent, boundary): the engine's curve for the mode lies within 1e-6 of this class's, so
            // whether a distance of exactly 1e-5 matches decides nothing for it.
            return distance < 1e-5f;
        }
    }
}
