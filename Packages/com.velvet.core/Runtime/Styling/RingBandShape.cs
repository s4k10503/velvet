using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The shapes a ring, its offset fill, an outline and the inset bands are painted in: a rounded rect with a
    // rounded hole, filled even-odd, so each corner of the outer edge and of the hole takes its own radius, which
    // UI Toolkit's style, holding one radius per corner, gives a native border no way to set.
    // Corner radii travel as x, y, z, w: top-left, top-right, bottom-right, bottom-left.
    internal static class RingBandShape
    {
        private const float Kappa = 0.5522848f;

        // An outset box-shadow's corner radius for a corner of radius r spread by s, as CSS Backgrounds 3 gives it:
        // r + s where r is at least s, and r + s(1 + (r/s - 1)^3) below that, so a square corner stays square. A
        // negative spread, an inset shadow's, takes s off r down to 0.
        internal static float SpreadRadius(float r, float s)
        {
            if (s <= 0f)
            {
                return Mathf.Max(0f, r + s);
            }
            if (r >= s)
            {
                return r + s;
            }
            var ratio = (r / s) - 1f;
            return r + (s * (1f + (ratio * ratio * ratio)));
        }

        internal static Vector4 SpreadRadii(Vector4 r, float s)
            => new(SpreadRadius(r.x, s), SpreadRadius(r.y, s), SpreadRadius(r.z, s), SpreadRadius(r.w, s));

        // An outline's corner grown by g past the border edge: r + g where the corner is rounded, square where it
        // is not, as RingBandShapeTests records Chromium painting it.
        internal static Vector4 OutlineRadii(Vector4 r, float g)
            => new(OutlineRadius(r.x, g), OutlineRadius(r.y, g), OutlineRadius(r.z, g), OutlineRadius(r.w, g));

        private static float OutlineRadius(float r, float g) => r > 0f ? Mathf.Max(0f, r + g) : 0f;

        // radii scaled down together until adjacent corners fit their side, as CSS fits border radii.
        internal static Vector4 Fit(Vector4 radii, float width, float height)
        {
            var factor = CornerRadiusFit.ScaleFactor(width, height, CornerRadii.Circular(radii.x, radii.y, radii.z, radii.w));
            return radii * factor;
        }

        // Fills outer minus hole in color. An empty hole leaves the outer shape whole.
        internal static void Fill(Painter2D painter, Rect outer, Vector4 outerRadii, Rect hole, Vector4 holeRadii, Color color)
        {
            if (color.a <= 0f || outer.width <= 0f || outer.height <= 0f)
            {
                return;
            }
            painter.BeginPath();
            AddRoundedRect(painter, outer, Fit(outerRadii, outer.width, outer.height));
            if (hole.width > 0f && hole.height > 0f)
            {
                AddRoundedRect(painter, hole, Fit(holeRadii, hole.width, hole.height));
            }
            painter.fillColor = color;
            painter.Fill(FillRule.OddEven);
        }

        private static void AddRoundedRect(Painter2D p, Rect r, Vector4 radii)
        {
            var (tl, tr, br, bl) = (radii.x, radii.y, radii.z, radii.w);
            p.MoveTo(new Vector2(r.xMin + tl, r.yMin));
            p.LineTo(new Vector2(r.xMax - tr, r.yMin));
            if (tr > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMax - tr + (Kappa * tr), r.yMin), new Vector2(r.xMax, r.yMin + tr - (Kappa * tr)),
                    new Vector2(r.xMax, r.yMin + tr));
            }
            p.LineTo(new Vector2(r.xMax, r.yMax - br));
            if (br > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMax, r.yMax - br + (Kappa * br)), new Vector2(r.xMax - br + (Kappa * br), r.yMax),
                    new Vector2(r.xMax - br, r.yMax));
            }
            p.LineTo(new Vector2(r.xMin + bl, r.yMax));
            if (bl > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMin + bl - (Kappa * bl), r.yMax), new Vector2(r.xMin, r.yMax - bl + (Kappa * bl)),
                    new Vector2(r.xMin, r.yMax - bl));
            }
            p.LineTo(new Vector2(r.xMin, r.yMin + tl));
            if (tl > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMin, r.yMin + tl - (Kappa * tl)), new Vector2(r.xMin + tl - (Kappa * tl), r.yMin),
                    new Vector2(r.xMin + tl, r.yMin));
            }
            p.ClosePath();
        }
    }
}
