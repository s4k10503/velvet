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

#pragma warning disable CS8524 // no discard arm: a new edge has to name the colour it reads
        public Color ColorOf(DivideEdge side) => side switch
        {
            DivideEdge.Left => LeftColor,
            DivideEdge.Top => TopColor,
            DivideEdge.Right => RightColor,
            DivideEdge.Bottom => BottomColor,
        };
#pragma warning restore CS8524

        private static float Finite(float width) => float.IsNaN(width) ? 0f : Mathf.Max(0f, width);
    }

    /// <summary>
    /// Fills a border as CSS draws one: the band between the border edge and the padding edge, each side its own
    /// width and colour, each inner corner's radius the outer one less the widths of the two sides meeting there. Two
    /// sides meet across a corner on the line from its outer corner to its inner one, the line a dashed or dotted side
    /// is cut on as well (DashedBorderPainter.SideQuad): the wider side takes the larger part, and a side of no width
    /// leaves the whole corner to the other.
    /// </summary>
    internal static class BorderBandPainter
    {
        // Points each corner's arc is sampled at, ends included.
        internal const int CornerSamples = 17;

        private static readonly List<Vector2> s_outer = new();
        private static readonly List<Vector2> s_inner = new();
        private static readonly List<Vector2> s_clipped = new();
        private static readonly Vector2[] s_quad = new Vector2[4];

        // Fills the border of a box whose border edge is rounded by radii, which are fitted to it first.
        public static void Paint(Painter2D painter, in ShearedRoundedRect box, in BorderSides sides)
        {
            var w = box.Width;
            var h = box.Height;
            var shear = new SilhouetteFace.Shearing(w, h, box.TanX, box.TanY);
            var outer = Fitted(box.Radii, w, h);
            var borderEdge = new Rect(0f, 0f, w, h);
            var paddingEdge = Rect.MinMaxRect(sides.Left, sides.Top, w - sides.Right, h - sides.Bottom);
            var inner = InnerRadii(outer, sides);
            if (sides.Uniform)
            {
                if (sides.Left <= 0f || sides.LeftColor.a <= 0.004f) return;
                BuildContour(s_outer, borderEdge, outer, shear);
                BuildContour(s_inner, paddingEdge, inner, shear);
                painter.BeginPath();
                Trace(painter, s_outer);
                if (paddingEdge.width > 0f && paddingEdge.height > 0f) Trace(painter, s_inner);
                painter.fillColor = sides.LeftColor;
                painter.Fill(FillRule.OddEven);
                return;
            }
            BuildContour(s_outer, borderEdge, outer, SilhouetteFace.Shearing.None);
            BuildContour(s_inner, paddingEdge, inner, SilhouetteFace.Shearing.None);
            var quadBox = new BorderBox
            {
                Size = new Vector2(w, h),
                Left = sides.Left,
                Top = sides.Top,
                Right = sides.Right,
                Bottom = sides.Bottom,
            };
            for (var side = DivideEdge.Left; side <= DivideEdge.Bottom; side++)
            {
                if (quadBox.Width(side) <= 0f || sides.ColorOf(side).a <= 0.004f) continue;
                DashedBorderPainter.SideQuad(side, quadBox, s_quad);
                DashedBorderPainter.ClipConvex(s_outer, s_quad, s_clipped);
                if (s_clipped.Count < 3) continue;
                painter.BeginPath();
                TraceSheared(painter, s_clipped, shear);
                if (paddingEdge.width > 0f && paddingEdge.height > 0f)
                {
                    DashedBorderPainter.ClipConvex(s_inner, s_quad, s_clipped);
                    if (s_clipped.Count >= 3) TraceSheared(painter, s_clipped, shear);
                }
                painter.fillColor = sides.ColorOf(side);
                painter.Fill(FillRule.OddEven);
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

        // A quarter of the ellipse centred inward of corner (0 the top-left, then clockwise) by radius, sampled from where
        // the side before it ends to where the side after it starts.
        private static void AddCorner(List<Vector2> points, Rect rect, Vector2 radius, int corner,
            in SilhouetteFace.Shearing shear)
        {
            for (var i = 0; i < CornerSamples; i++)
            {
                points.Add(OnCorner(rect, radius, corner, (float)i / (CornerSamples - 1), shear));
            }
        }

        // The point a fraction t of the way round corner's arc, clockwise in a y-down box; the corner itself where it is
        // square.
        private static Vector2 OnCorner(Rect rect, Vector2 radius, int corner, float t, in SilhouetteFace.Shearing shear)
        {
            var right = corner is 1 or 2;
            var bottom = corner is 2 or 3;
            var at = new Vector2(right ? rect.xMax : rect.xMin, bottom ? rect.yMax : rect.yMin);
            if (radius.x > 0f && radius.y > 0f)
            {
                var centre = at + new Vector2(right ? -radius.x : radius.x, bottom ? -radius.y : radius.y);
                var angle = Mathf.PI * (1f + (0.5f * corner) + (0.5f * t));
                at = centre + new Vector2(radius.x * Mathf.Cos(angle), radius.y * Mathf.Sin(angle));
            }
            return shear.Apply(at.x, at.y);
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

        // The contour is clipped upright, where the side quads are, and sheared after.
        private static void TraceSheared(Painter2D painter, List<Vector2> contour, in SilhouetteFace.Shearing shear)
        {
            var at = shear.Apply(contour[0].x, contour[0].y);
            painter.MoveTo(at);
            for (var i = 1; i < contour.Count; i++)
            {
                at = LineTo(painter, at, shear.Apply(contour[i].x, contour[i].y));
            }
            painter.ClosePath();
        }

        // A square corner repeats its point, which adds no segment.
        private static Vector2 LineTo(Painter2D painter, Vector2 from, Vector2 to)
        {
            if (to != from) painter.LineTo(to);
            return to;
        }
    }
}
