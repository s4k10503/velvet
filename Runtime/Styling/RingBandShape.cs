using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The shapes a ring, its offset fill and an outline are painted in: a rounded rect with a rounded hole, filled
    // even-odd, so the outer edge and the hole each take their own radii, which UI Toolkit's style, holding one radius
    // per corner and one border for the whole box, gives a native border no way to set.
    internal static class RingBandShape
    {
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

        internal static CornerRadii Spread(in CornerRadii radii, float spread) => Map(radii, r => SpreadRadius(r, spread));

        // An outline's corner grown by g past the border edge: r + g where the corner is rounded, square where it is
        // not.
        internal static CornerRadii Outline(in CornerRadii radii, float g)
            => Map(radii, r => r > 0f ? Mathf.Max(0f, r + g) : 0f);

        // radii with spread taken off each component, down to 0: the shape inside the padding box a spread inset shape
        // leaves.
        internal static CornerRadii Shrink(in CornerRadii radii, float spread) => Map(radii, r => Mathf.Max(0f, r - spread));

        // The padding box's corners for a border box rounded by outer (already fitted to it) with these border widths:
        // each corner's horizontal radius less the side border beside it and its vertical radius less the top or
        // bottom one, so unequal borders give an elliptical corner, as CSS Backgrounds 3 shapes the inner border edge.
        internal static CornerRadii PaddingRadii(in CornerRadii outer, float left, float top, float right, float bottom)
            => new()
            {
                TopLeft = Less(outer.TopLeft, left, top),
                TopRight = Less(outer.TopRight, right, top),
                BottomRight = Less(outer.BottomRight, right, bottom),
                BottomLeft = Less(outer.BottomLeft, left, bottom),
            };

        private static Vector2 Less(Vector2 r, float x, float y) => new(Mathf.Max(0f, r.x - x), Mathf.Max(0f, r.y - y));

        private static CornerRadii Map(in CornerRadii radii, System.Func<float, float> f) => new()
        {
            TopLeft = new Vector2(f(radii.TopLeft.x), f(radii.TopLeft.y)),
            TopRight = new Vector2(f(radii.TopRight.x), f(radii.TopRight.y)),
            BottomRight = new Vector2(f(radii.BottomRight.x), f(radii.BottomRight.y)),
            BottomLeft = new Vector2(f(radii.BottomLeft.x), f(radii.BottomLeft.y)),
        };

        // Fills outer, rounded by outerRadii, less hole, rounded by holeRadii, in color. Each is fitted to its own box
        // first; an empty hole leaves the outer shape whole.
        internal static void Fill(Painter2D painter, Rect outer, in CornerRadii outerRadii, Rect hole, in CornerRadii holeRadii,
            Color color)
        {
            if (color.a <= 0f || outer.width <= 0f || outer.height <= 0f)
            {
                return;
            }
            painter.BeginPath();
            Trace(painter, outer, outerRadii);
            if (hole.width > 0f && hole.height > 0f)
            {
                Trace(painter, hole, holeRadii);
            }
            painter.fillColor = color;
            painter.Fill(FillRule.OddEven);
        }

        private static void Trace(Painter2D painter, Rect rect, in CornerRadii radii)
        {
            SilhouetteFace.EmitRoundedRect(new SilhouetteFace.Painter2DSink(painter), rect,
                BorderBandPainter.Fitted(radii, rect.width, rect.height), SilhouetteFace.Shearing.None);
            painter.ClosePath();
        }
    }
}
