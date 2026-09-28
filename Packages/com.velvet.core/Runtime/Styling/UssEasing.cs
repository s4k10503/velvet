using System;
using UnityEngine;
using UnityEngine.UIElements;
using UitkEasing = UnityEngine.UIElements.Experimental.Easing;

namespace Velvet
{
    // The curve UI Toolkit eases a USS transition by for each EasingMode, for a driver that steps a value
    // itself and has to land on the curve a variant swap's transition takes. UssEasingTests compares every
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
    }
}
