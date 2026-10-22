using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The width and colour of each side of a border.
    internal readonly struct BorderSides
    {
        public float Left { get; init; }
        public float Top { get; init; }
        public float Right { get; init; }
        public float Bottom { get; init; }
        public Color LeftColor { get; init; }
        public Color TopColor { get; init; }
        public Color RightColor { get; init; }
        public Color BottomColor { get; init; }

        // The widths the element is laid out with, in the given colours.
        public static BorderSides Of(IResolvedStyle style, Color left, Color top, Color right, Color bottom) => new()
        {
            Left = Finite(style.borderLeftWidth),
            Top = Finite(style.borderTopWidth),
            Right = Finite(style.borderRightWidth),
            Bottom = Finite(style.borderBottomWidth),
            LeftColor = left,
            TopColor = top,
            RightColor = right,
            BottomColor = bottom,
        };

        // Whether every side has the left one's width and colour, which a single stroke draws.
        public bool Uniform => Top == Left && Right == Left && Bottom == Left
            && TopColor == LeftColor && RightColor == LeftColor && BottomColor == LeftColor;

        // Side 0 is the top, then clockwise.
        public float Width(int side) => side switch { 0 => Top, 1 => Right, 2 => Bottom, _ => Left };

        public Color ColorOf(int side) => side switch { 0 => TopColor, 1 => RightColor, 2 => BottomColor, _ => LeftColor };

        private static float Finite(float width) => float.IsNaN(width) ? 0f : Mathf.Max(0f, width);
    }

    /// <summary>
    /// Fills a border as CSS draws one: the band between the border edge and the padding edge, each side its own
    /// width and colour, each inner corner's radius the outer one less the widths of the two sides meeting there, and
    /// two sides of different colours meeting halfway round the corner between them.
    /// </summary>
    internal static class BorderBandPainter
    {
        // Points each corner's arc is sampled at, ends included; odd, so a corner has a middle sample.
        internal const int CornerSamples = 17;

        private static readonly List<Vector2> s_outer = new();
        private static readonly List<Vector2> s_inner = new();

        // Fills the border of a box whose border edge is rounded by radii, which are fitted to it first.
        public static void Paint(Painter2D painter, in ShearedRoundedRect box, in BorderSides sides)
        {
            var w = box.Width;
            var h = box.Height;
            var shear = new SilhouetteFace.Shearing(w, h, box.TanX, box.TanY);
            var outer = Fitted(box.Radii, w, h);
            var paddingEdge = Rect.MinMaxRect(sides.Left, sides.Top, w - sides.Right, h - sides.Bottom);
            BuildContour(s_outer, new Rect(0f, 0f, w, h), outer, shear);
            BuildContour(s_inner, paddingEdge, InnerRadii(outer, sides), shear);
            if (sides.Uniform)
            {
                if (sides.Left <= 0f || sides.LeftColor.a <= 0.004f) return;
                painter.BeginPath();
                Trace(painter, s_outer);
                if (paddingEdge.width > 0f && paddingEdge.height > 0f) Trace(painter, s_inner);
                painter.fillColor = sides.LeftColor;
                painter.Fill(FillRule.OddEven);
                return;
            }
            for (var side = 0; side < 4; side++)
            {
                if (sides.Width(side) <= 0f || sides.ColorOf(side).a <= 0.004f) continue;
                painter.BeginPath();
                TraceSide(painter, side);
                painter.fillColor = sides.ColorOf(side);
                painter.Fill();
            }
        }

        // The radii scaled down together where adjacent ones overlap on a side, as CSS fits them to the border edge.
        internal static CornerRadii Fitted(in CornerRadii radii, float width, float height)
        {
            var nonNegative = new CornerRadii
            {
                TopLeft = Vector2.Max(Vector2.zero, radii.TopLeft),
                TopRight = Vector2.Max(Vector2.zero, radii.TopRight),
                BottomRight = Vector2.Max(Vector2.zero, radii.BottomRight),
                BottomLeft = Vector2.Max(Vector2.zero, radii.BottomLeft),
            };
            var factor = CornerRadiusFit.ScaleFactor(width, height, nonNegative);
            return new CornerRadii
            {
                TopLeft = nonNegative.TopLeft * factor,
                TopRight = nonNegative.TopRight * factor,
                BottomRight = nonNegative.BottomRight * factor,
                BottomLeft = nonNegative.BottomLeft * factor,
            };
        }

        // The padding edge's radii: each border-edge radius less, on each axis, the width of the side across it.
        internal static CornerRadii InnerRadii(in CornerRadii outer, in BorderSides sides) => new()
        {
            TopLeft = Vector2.Max(Vector2.zero, outer.TopLeft - new Vector2(sides.Left, sides.Top)),
            TopRight = Vector2.Max(Vector2.zero, outer.TopRight - new Vector2(sides.Right, sides.Top)),
            BottomRight = Vector2.Max(Vector2.zero, outer.BottomRight - new Vector2(sides.Right, sides.Bottom)),
            BottomLeft = Vector2.Max(Vector2.zero, outer.BottomLeft - new Vector2(sides.Left, sides.Bottom)),
        };

        // The outline of rect rounded by radii, clockwise from the top-left corner's start on the left side: each
        // corner CornerSamples points along its elliptical arc, which a square corner repeats at the corner itself.
        internal static void BuildContour(List<Vector2> points, Rect rect, in CornerRadii radii,
            in SilhouetteFace.Shearing shear)
        {
            points.Clear();
            AddCorner(points, rect, radii.TopLeft, 0, shear);
            AddCorner(points, rect, radii.TopRight, 1, shear);
            AddCorner(points, rect, radii.BottomRight, 2, shear);
            AddCorner(points, rect, radii.BottomLeft, 3, shear);
        }

        // A quarter of the ellipse centred inward of corner (0 the top-left, then clockwise) by radius, a quarter turn
        // clockwise in a y-down box from where the side before it ends.
        private static void AddCorner(List<Vector2> points, Rect rect, Vector2 radius, int corner,
            in SilhouetteFace.Shearing shear)
        {
            var right = corner is 1 or 2;
            var bottom = corner is 2 or 3;
            var at = new Vector2(right ? rect.xMax : rect.xMin, bottom ? rect.yMax : rect.yMin);
            var rounded = radius.x > 0f && radius.y > 0f;
            var centre = rounded ? at + new Vector2(right ? -radius.x : radius.x, bottom ? -radius.y : radius.y) : at;
            var startAngle = Mathf.PI * (1f + (0.5f * corner));
            for (var i = 0; i < CornerSamples; i++)
            {
                var angle = startAngle + (Mathf.PI * 0.5f * i / (CornerSamples - 1));
                var point = rounded ? centre + new Vector2(radius.x * Mathf.Cos(angle), radius.y * Mathf.Sin(angle)) : at;
                points.Add(shear.Apply(point.x, point.y));
            }
        }

        private static void Trace(Painter2D painter, List<Vector2> contour)
        {
            painter.MoveTo(contour[0]);
            for (var i = 1; i < contour.Count; i++)
            {
                LineTo(painter, contour[i - 1], contour[i]);
            }
            painter.ClosePath();
        }

        // A square corner repeats its point, which adds no segment.
        private static Vector2 LineTo(Painter2D painter, Vector2 from, Vector2 to)
        {
            if (to != from) painter.LineTo(to);
            return to;
        }

        // Side 0 (the top) runs from the middle of the top-left corner to the middle of the top-right one, and so on
        // clockwise: along the border edge, then back along the padding edge.
        private static void TraceSide(Painter2D painter, int side)
        {
            const int mid = CornerSamples / 2;
            var count = s_outer.Count;
            var from = (side * CornerSamples) + mid;
            var to = (((side + 1) % 4) * CornerSamples) + mid;
            var steps = ((to - from) + count) % count;
            var last = s_outer[from];
            painter.MoveTo(last);
            for (var i = 1; i <= steps; i++)
            {
                last = LineTo(painter, last, s_outer[(from + i) % count]);
            }
            for (var i = steps; i >= 0; i--)
            {
                last = LineTo(painter, last, s_inner[(from + i) % count]);
            }
            painter.ClosePath();
        }
    }
}
