using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The value math a Motion play applies to the skew, shadow and gradient layers at a progress. A pair
    /// that has no continuous path between its ends holds the first until progress reaches one half and then
    /// takes the second.
    /// </summary>
    internal static class MotionPaintInterpolation
    {
        public static SkewSpec Skew(SkewSpec from, SkewSpec to, float progress)
            => new(Mathf.LerpUnclamped(from.XDeg, to.XDeg, progress), Mathf.LerpUnclamped(from.YDeg, to.YDeg, progress),
                null, null);

        /// <summary>
        /// Layer by layer: a layer the other side lacks is the transparent layer of the same kind at zero offset,
        /// blur and spread. A pair of layers differing in kind (inset or not, or a color read from the parent or
        /// the text) steps at one half.
        /// </summary>
        public static ShadowSpec[] Shadow(ShadowSpec[] from, ShadowSpec[] to, float progress)
        {
            var count = Math.Max(from.Length, to.Length);
            var layers = new ShadowSpec[count];
            for (var i = 0; i < count; i++)
            {
                var a = i < from.Length ? from[i] : Vanished(to[i]);
                var b = i < to.Length ? to[i] : Vanished(from[i]);
                layers[i] = ShadowLayer(a, b, progress);
            }
            return layers;
        }

        private static ShadowSpec Vanished(ShadowSpec like)
            => new(float.IsNaN(like.Color.r) ? like.Color : new Color(like.Color.r, like.Color.g, like.Color.b, 0f),
                0f, 0f, 0f, 0f, like.Inset, like.InheritFrom);

        private static ShadowSpec ShadowLayer(ShadowSpec a, ShadowSpec b, float progress)
        {
            if (a.Inset != b.Inset || a.InheritFrom != b.InheritFrom
                || float.IsNaN(a.Color.r) != float.IsNaN(b.Color.r))
            {
                return progress < 0.5f ? a : b;
            }
            var color = float.IsNaN(a.Color.r)
                ? new Color(float.NaN, 0f, 0f, Mathf.Clamp01(Mathf.LerpUnclamped(a.Color.a, b.Color.a, progress)))
                : MotionPropertyInterpolation.LerpColor(a.Color, b.Color, progress);
            return new ShadowSpec(color,
                Mathf.Max(0f, Mathf.LerpUnclamped(a.Blur, b.Blur, progress)),
                Mathf.LerpUnclamped(a.OffsetY, b.OffsetY, progress),
                Mathf.LerpUnclamped(a.Spread, b.Spread, progress),
                Mathf.LerpUnclamped(a.OffsetX, b.OffsetX, progress),
                a.Inset, a.InheritFrom);
        }

        /// <summary>
        /// Stop by stop, for two gradients of one shape, interpolation space and stop count whose stops place
        /// themselves alike (each position, hint and length present on both or on neither).
        /// </summary>
        public static GradientSpec Gradient(GradientSpec from, GradientSpec to, float progress)
        {
            if (!Blends(from, to))
            {
                return progress < 0.5f ? from : to;
            }
            var stops = new GradientStop[from.Stops.Length];
            for (var i = 0; i < stops.Length; i++)
            {
                var a = from.Stops[i];
                var b = to.Stops[i];
                stops[i] = new GradientStop(MotionPropertyInterpolation.LerpColor(a.Color, b.Color, progress),
                    Mathf.LerpUnclamped(a.Position, b.Position, progress),
                    Blend(a.PositionPx, b.PositionPx, progress),
                    Blend(a.Hint, b.Hint, progress),
                    Blend(a.HintPx, b.HintPx, progress));
            }
            return @from with
            {
                AngleDeg = Mathf.LerpUnclamped(from.AngleDeg, to.AngleDeg, progress),
                CenterX = Mathf.LerpUnclamped(from.CenterX, to.CenterX, progress),
                CenterY = Mathf.LerpUnclamped(from.CenterY, to.CenterY, progress),
                Stops = stops,
            };
        }

        private static float Blend(float a, float b, float progress)
            => float.IsNaN(a) ? float.NaN : Mathf.LerpUnclamped(a, b, progress);

        private static bool Blends(GradientSpec a, GradientSpec b)
        {
            if (a.Stops == null || b.Stops == null || a.Stops.Length != b.Stops.Length
                || a.Type != b.Type || a.Interp != b.Interp || a.Hue != b.Hue || a.ToCorner != b.ToCorner
                || !a.Radial.Equals(b.Radial))
            {
                return false;
            }
            for (var i = 0; i < a.Stops.Length; i++)
            {
                if (!SamePlacement(a.Stops[i], b.Stops[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SamePlacement(GradientStop a, GradientStop b)
            => float.IsNaN(a.PositionPx) == float.IsNaN(b.PositionPx)
                && float.IsNaN(a.Hint) == float.IsNaN(b.Hint)
                && float.IsNaN(a.HintPx) == float.IsNaN(b.HintPx);
    }
}
