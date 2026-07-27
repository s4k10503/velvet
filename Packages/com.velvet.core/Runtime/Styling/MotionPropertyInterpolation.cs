using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The value math a per-frame Motion driver applies to a color channel, shared by the spring and the bezier
    /// driver so the two produce the identical sequence of colors for the same progress.
    /// </summary>
    internal static class MotionPropertyInterpolation
    {
        /// <summary>
        /// The color at <paramref name="progress"/> along the straight RGBA line between the endpoints, with
        /// every component clamped back into the representable range. The lerp itself is UNCLAMPED so an
        /// overshoot curve or an under-damped spring genuinely passes its target the way a translate does, but a
        /// component outside [0,1] is not a color any renderer can show, so the excursion is expressed by
        /// saturating rather than by emitting a negative or super-white component.
        /// </summary>
        public static Color LerpColor(Color from, Color to, float progress)
        {
            var c = Color.LerpUnclamped(from, to, progress);
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }
    }
}
