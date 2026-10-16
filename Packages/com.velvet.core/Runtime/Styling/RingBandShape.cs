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

        internal static CornerRadii Fit(in CornerRadii radii, float width, float height)
        {
            var factor = CornerRadiusFit.ScaleFactor(width, height, radii);
            return new CornerRadii
            {
                TopLeft = radii.TopLeft * factor,
                TopRight = radii.TopRight * factor,
                BottomRight = radii.BottomRight * factor,
                BottomLeft = radii.BottomLeft * factor,
            };
        }

        internal static CornerRadii Circular(Vector4 r) => CornerRadii.Circular(r.x, r.y, r.z, r.w);

        // The padding box's corners for a border box rounded by outer (already fitted to it) with these border widths:
        // each corner's horizontal radius less the side border beside it and its vertical radius less the top or
        // bottom one, so unequal borders give an elliptical corner, as CSS Backgrounds 3 shapes the inner border edge.
        internal static CornerRadii PaddingRadii(Vector4 outer, float left, float top, float right, float bottom)
            => new()
            {
                TopLeft = new Vector2(Mathf.Max(0f, outer.x - left), Mathf.Max(0f, outer.x - top)),
                TopRight = new Vector2(Mathf.Max(0f, outer.y - right), Mathf.Max(0f, outer.y - top)),
                BottomRight = new Vector2(Mathf.Max(0f, outer.z - right), Mathf.Max(0f, outer.z - bottom)),
                BottomLeft = new Vector2(Mathf.Max(0f, outer.w - left), Mathf.Max(0f, outer.w - bottom)),
            };

        // radii with spread taken off each component, down to 0: an inset shadow's shape inside the padding box.
        internal static CornerRadii Shrink(in CornerRadii radii, float spread)
        {
            static Vector2 Less(Vector2 r, float s) => new(Mathf.Max(0f, r.x - s), Mathf.Max(0f, r.y - s));
            return new CornerRadii
            {
                TopLeft = Less(radii.TopLeft, spread),
                TopRight = Less(radii.TopRight, spread),
                BottomRight = Less(radii.BottomRight, spread),
                BottomLeft = Less(radii.BottomLeft, spread),
            };
        }

        // Fills outer minus hole in color. An empty hole leaves the outer shape whole.
        internal static void Fill(Painter2D painter, Rect outer, Vector4 outerRadii, Rect hole, Vector4 holeRadii, Color color)
            => Fill(painter, outer, Circular(outerRadii), hole, Circular(holeRadii), color);

        internal static void Fill(Painter2D painter, Rect outer, in CornerRadii outerRadii, Rect hole, in CornerRadii holeRadii,
            Color color)
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

        // A rounded rect whose corners are quarter ellipses, each its own horizontal and vertical radius.
        private static void AddRoundedRect(Painter2D p, Rect r, in CornerRadii radii)
        {
            // A corner with either radius 0 is square.
            static Vector2 Corner(Vector2 c) => c.x > 0f && c.y > 0f ? c : Vector2.zero;
            var (tl, tr, br, bl) = (Corner(radii.TopLeft), Corner(radii.TopRight), Corner(radii.BottomRight), Corner(radii.BottomLeft));
            p.MoveTo(new Vector2(r.xMin + tl.x, r.yMin));
            p.LineTo(new Vector2(r.xMax - tr.x, r.yMin));
            if (tr.x > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMax - tr.x + (Kappa * tr.x), r.yMin), new Vector2(r.xMax, r.yMin + tr.y - (Kappa * tr.y)),
                    new Vector2(r.xMax, r.yMin + tr.y));
            }
            p.LineTo(new Vector2(r.xMax, r.yMax - br.y));
            if (br.x > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMax, r.yMax - br.y + (Kappa * br.y)), new Vector2(r.xMax - br.x + (Kappa * br.x), r.yMax),
                    new Vector2(r.xMax - br.x, r.yMax));
            }
            p.LineTo(new Vector2(r.xMin + bl.x, r.yMax));
            if (bl.x > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMin + bl.x - (Kappa * bl.x), r.yMax), new Vector2(r.xMin, r.yMax - bl.y + (Kappa * bl.y)),
                    new Vector2(r.xMin, r.yMax - bl.y));
            }
            p.LineTo(new Vector2(r.xMin, r.yMin + tl.y));
            if (tl.x > 0f)
            {
                p.BezierCurveTo(new Vector2(r.xMin, r.yMin + tl.y - (Kappa * tl.y)), new Vector2(r.xMin + tl.x - (Kappa * tl.x), r.yMin),
                    new Vector2(r.xMin + tl.x, r.yMin));
            }
            p.ClosePath();
        }
    }
}
