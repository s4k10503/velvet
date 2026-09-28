using System;
using UnityEngine;
using UnityEngine.UIElements;
using Experimental = UnityEngine.UIElements.Experimental;

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
            Experimental.Easing.InQuad,
            Experimental.Easing.OutQuad,
            Experimental.Easing.InOutQuad,
            Experimental.Easing.Linear,
            Experimental.Easing.InSine,
            Experimental.Easing.OutSine,
            Experimental.Easing.InOutSine,
            Experimental.Easing.InCubic,
            Experimental.Easing.OutCubic,
            Experimental.Easing.InOutCubic,
            Experimental.Easing.InCirc,
            Experimental.Easing.OutCirc,
            Experimental.Easing.InOutCirc,
            Experimental.Easing.InElastic,
            Experimental.Easing.OutElastic,
            Experimental.Easing.InOutElastic,
            Experimental.Easing.InBack,
            Experimental.Easing.OutBack,
            Experimental.Easing.InOutBack,
            Experimental.Easing.InBounce,
            Experimental.Easing.OutBounce,
            Experimental.Easing.InOutBounce,
        };

        public static float Evaluate(EasingMode mode, float t)
        {
            var index = (int)mode;
            var curve = index >= 0 && index < s_curves.Length ? s_curves[index] : s_curves[0];
            return curve(Mathf.Clamp01(t));
        }
    }
}
