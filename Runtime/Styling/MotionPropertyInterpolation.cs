using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The value math a per-frame Motion driver applies to a property channel, shared by the spring and the
    /// bezier driver so the two emit identical values for the same progress.
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

        /// <summary>
        /// A magnitude a driver is about to write, saturated at zero for the properties that have no negative
        /// meaning (see <see cref="MotionPropertyClassParser.AllowsNegativeLength"/>). Channels interpolate
        /// UNCLAMPED for the same reason colors do — an under-damped spring or an anticipate curve genuinely
        /// passes its target — but a width or a corner radius below zero is not a value the layout engine can
        /// honor, so the excursion saturates as it is emitted instead of being written out and silently
        /// corrected downstream.
        /// </summary>
        public static float ClampLength(ArbitraryProperty property, float value)
            => MotionPropertyClassParser.AllowsNegativeLength(property) ? value : Mathf.Max(0f, value);

        /// <summary>The magnitude at <paramref name="progress"/> between the endpoints, emitted through <see cref="ClampLength"/>.</summary>
        public static float LerpLength(ArbitraryProperty property, float from, float to, float progress)
            => ClampLength(property, Mathf.LerpUnclamped(from, to, progress));
    }
}
