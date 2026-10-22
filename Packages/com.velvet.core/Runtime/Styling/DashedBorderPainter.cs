using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Which line style a border / divider paints. UI Toolkit has no CSS border-style, so a non-solid style is
    // painted by hand (DashedBorderPainter); Solid is the native path and never reaches the marcher.
    internal enum BorderLineStyle
    {
        Solid,
        Dashed,
        Dotted,
    }

    // A laid-out box's border as the side painter reads it: its size, each side's width and each corner's radius.
    internal readonly struct BorderBox
    {
        public Vector2 Size { get; init; }
        public float Left { get; init; }
        public float Top { get; init; }
        public float Right { get; init; }
        public float Bottom { get; init; }
        public float RadiusTopLeft { get; init; }
        public float RadiusTopRight { get; init; }
        public float RadiusBottomRight { get; init; }
        public float RadiusBottomLeft { get; init; }

        // A radius per axis for each corner, which stands in for the four above where set: a layoutId projection's
        // (ProjectedCornerFace).
        public CornerRadii? Radii { get; init; }

        public bool IsSquare
        {
            get
            {
                if (Radii is { } radii)
                {
                    return Square(radii);
                }
                return RadiusTopLeft <= 0f && RadiusTopRight <= 0f && RadiusBottomRight <= 0f && RadiusBottomLeft <= 0f;
            }
        }

        private static bool Square(in CornerRadii radii)
            => IsZero(radii.TopLeft) && IsZero(radii.TopRight) && IsZero(radii.BottomRight) && IsZero(radii.BottomLeft);

        private static bool IsZero(Vector2 radius) => radius.x <= 0f || radius.y <= 0f;

#pragma warning disable CS8524 // no discard arm: a new edge has to name the width it reads
        public float Width(DivideEdge side) => side switch
        {
            DivideEdge.Left => Left,
            DivideEdge.Top => Top,
            DivideEdge.Right => Right,
            DivideEdge.Bottom => Bottom,
        };
#pragma warning restore CS8524
    }

    // Arc-length dash/dot marcher on top of Painter2D. A dashed or dotted outline is walked here: the outline
    // is a flat polyline, a box side's own line or a rounded box's whole outline, and this walks it by arc length
    // emitting the painted runs, in the pattern styling-flexbox-and-gap.md describes and DashedBorderPainterTests
    // pins. A round dot is a zero-length run. `closed` wraps the last point back to point 0; an open polyline
    // leaves the raw ends.
    internal static class DashedBorderPainter
    {
        private const int DotSides = 16;

        // Reused across draws so a steady-state repaint allocates nothing. UI Toolkit generateVisualContent
        // callbacks run sequentially on the main thread, so a single shared buffer is safe (no re-entrancy).
        private static readonly List<(Vector2 From, Vector2 To)> s_segments = new();
        private static readonly List<(float Start, float End)> s_intervals = new();
        private static readonly Vector2[] s_side = new Vector2[2];
        private static readonly List<Vector2> s_outline = new();
        private static readonly List<Vector2> s_points = new();
        private static readonly List<int> s_counts = new();
        private static readonly List<Vector2> s_piece = new();
        private static readonly List<Vector2> s_clipped = new();
        private static readonly Vector2[] s_quad = new Vector2[4];
        private static readonly List<Vector2> s_scratch = new();

        // Fills one side of box in its pattern. A no-op for Solid (the native border already draws that), a
        // near-transparent color, or a hairline width — callers do not pre-filter for the latter two.
        public static void PaintSide(Painter2D painter, DivideEdge side, in BorderBox box, Color color,
            BorderLineStyle style)
            => PaintSide(painter, side, box, color, style, SilhouetteFace.Shearing.None);

        // As above, each point carried by shear, as a skewed element's face is (SkewSilhouette).
        public static void PaintSide(Painter2D painter, DivideEdge side, in BorderBox box, Color color,
            BorderLineStyle style, in SilhouetteFace.Shearing shear)
        {
            if (color.a <= 0.004f)
            {
                return;
            }
            ComputeSidePieces(side, box, style, s_points, s_counts);
            painter.fillColor = color;
            var sheared = shear.TanX != 0f || shear.TanY != 0f;
            var at = 0;
            foreach (var count in s_counts)
            {
                painter.BeginPath();
                var first = s_points[at];
                painter.MoveTo(sheared ? shear.Apply(first.x, first.y) : first);
                for (var i = 1; i < count; i++)
                {
                    var point = s_points[at + i];
                    painter.LineTo(sheared ? shear.Apply(point.x, point.y) : point);
                }
                painter.ClosePath();
                painter.Fill();
                at += count;
            }
        }

        // The filled pieces one side of box paints, as convex polygons: points holds their vertices one polygon
        // after another and counts how many each has. A square box strokes the side as a line of its own
        // (ComputeSideRuns); a rounded one walks its whole outline at the side's width. Each run is then cut to
        // the side's own region, the quad between its outer edge, the inner edge the border widths leave and the
        // lines joining their corners, so two sides meeting at a corner each paint their own half of it and
        // a translucent color is laid there once.
        public static void ComputeSidePieces(DivideEdge side, in BorderBox box, BorderLineStyle style,
            List<Vector2> points, List<int> counts)
        {
            points.Clear();
            counts.Clear();
            var width = box.Width(side);
            if (style == BorderLineStyle.Solid || width <= 0.01f)
            {
                return;
            }
            if (box.IsSquare)
            {
                ComputeSideRuns(side, box.Size, width, style, s_segments);
            }
            else
            {
                SilhouetteFace.BuildShearedRoundedRectPolyline(s_outline, box.Radii is { } radii
                    ? new ShearedRoundedRect { Width = box.Size.x, Height = box.Size.y, Inset = width * 0.5f, Radii = radii }
                    : new ShearedRoundedRect
                    {
                        Width = box.Size.x,
                        Height = box.Size.y,
                        Inset = width * 0.5f,
                        RadiusTopLeft = box.RadiusTopLeft,
                        RadiusTopRight = box.RadiusTopRight,
                        RadiusBottomRight = box.RadiusBottomRight,
                        RadiusBottomLeft = box.RadiusBottomLeft,
                    });
                ComputeSegments(s_outline, closed: true, width, style, s_segments);
            }
            SideQuad(side, box, s_quad);
            foreach (var (from, to) in s_segments)
            {
                RunPolygon(from, to, width * 0.5f, s_piece);
                ClipConvex(s_piece, s_quad, s_clipped);
                if (s_clipped.Count >= 3)
                {
                    points.AddRange(s_clipped);
                    counts.Add(s_clipped.Count);
                }
            }
        }

        // Outer corner, outer corner, inner corner, inner corner, wound the same way for every side. Where two
        // opposite widths add up to more than the box, the inner edge they leave is empty: it is taken as having no
        // extent on that axis, at the left or top width held inside the box, so the four quads stay convex and do
        // not overlap.
        internal static void SideQuad(DivideEdge side, in BorderBox box, Vector2[] quad)
        {
            var w = box.Size.x;
            var h = box.Size.y;
            var left = Mathf.Min(box.Left, w);
            var top = Mathf.Min(box.Top, h);
            var right = Mathf.Max(w - box.Right, left);
            var bottom = Mathf.Max(h - box.Bottom, top);
            var innerTopLeft = new Vector2(left, top);
            var innerTopRight = new Vector2(right, top);
            var innerBottomRight = new Vector2(right, bottom);
            var innerBottomLeft = new Vector2(left, bottom);
#pragma warning disable CS8524 // no discard arm: a new edge has to name its quad
            (quad[0], quad[1], quad[2], quad[3]) = side switch
            {
                DivideEdge.Top => (new Vector2(0f, 0f), new Vector2(w, 0f), innerTopRight, innerTopLeft),
                DivideEdge.Right => (new Vector2(w, 0f), new Vector2(w, h), innerBottomRight, innerTopRight),
                DivideEdge.Bottom => (new Vector2(w, h), new Vector2(0f, h), innerBottomLeft, innerBottomRight),
                DivideEdge.Left => (new Vector2(0f, h), new Vector2(0f, 0f), innerTopLeft, innerBottomLeft),
            };
#pragma warning restore CS8524
        }

        // A run's footprint: the rectangle a butt-capped stroke of it covers, or a round dot's circle.
        private static void RunPolygon(Vector2 from, Vector2 to, float half, List<Vector2> polygon)
        {
            polygon.Clear();
            var along = to - from;
            var length = along.magnitude;
            if (length < 1e-6f)
            {
                for (var i = 0; i < DotSides; i++)
                {
                    var angle = i * 2f * Mathf.PI / DotSides;
                    polygon.Add(from + (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * half));
                }
                return;
            }
            var normal = new Vector2(-along.y, along.x) * (half / length);
            polygon.Add(from + normal);
            polygon.Add(to + normal);
            polygon.Add(to - normal);
            polygon.Add(from - normal);
        }

        // Sutherland-Hodgman: subject cut by each edge of the convex clip polygon in turn, keeping the side the
        // clip polygon's centre lies on, so the winding of either polygon does not matter.
        internal static void ClipConvex(List<Vector2> subject, Vector2[] clip, List<Vector2> output)
        {
            output.Clear();
            output.AddRange(subject);
            var centre = Vector2.zero;
            foreach (var corner in clip)
            {
                centre += corner;
            }
            centre /= clip.Length;
            for (var e = 0; e < clip.Length && output.Count > 0; e++)
            {
                var a = clip[e];
                var b = clip[(e + 1) % clip.Length];
                var inside = Mathf.Sign(Cross(b - a, centre - a));
                s_scratch.Clear();
                s_scratch.AddRange(output);
                output.Clear();
                for (var i = 0; i < s_scratch.Count; i++)
                {
                    var p = s_scratch[i];
                    var q = s_scratch[(i + 1) % s_scratch.Count];
                    var dp = Cross(b - a, p - a) * inside;
                    var dq = Cross(b - a, q - a) * inside;
                    if (dp >= 0f)
                    {
                        output.Add(p);
                    }
                    if ((dp >= 0f) != (dq >= 0f))
                    {
                        output.Add(p + ((q - p) * (dp / (dp - dq))));
                    }
                }
            }
        }

        private static float Cross(Vector2 u, Vector2 v) => (u.x * v.y) - (u.y * v.x);

        // Pure arc-length marcher: fills `output` with the individual stroke runs for the style, a (from, to)
        // pair each, and a round dot as a zero-length one (from == to). Painter-free so it is unit-testable
        // headless — a Painter2D cannot be read back. Runs are broken at polyline vertices, so a dash that
        // crosses a corner becomes two runs meeting at the corner.
        public static void ComputeSegments(IReadOnlyList<Vector2> polyline, bool closed, float lineWidth,
            BorderLineStyle style, List<(Vector2 From, Vector2 To)> output)
        {
            output.Clear();
            if (polyline == null || polyline.Count < 2 || lineWidth <= 0f || style == BorderLineStyle.Solid)
            {
                return;
            }
            var length = PathLength(polyline, closed);
            if (length < 1e-6f)
            {
                return;
            }
            Intervals(length, closed, lineWidth, style, s_intervals);
            Emit(polyline, closed, s_intervals, output);
        }

        // The runs one side of a square-cornered size.x × size.y box paints: a line of its own from outer corner
        // to outer corner along the middle of the side's band, so each side starts and ends its own pattern.
        public static void ComputeSideRuns(DivideEdge side, Vector2 size, float lineWidth, BorderLineStyle style,
            List<(Vector2 From, Vector2 To)> output)
        {
            var half = lineWidth * 0.5f;
#pragma warning disable CS8524 // no discard arm: a new edge has to name the line it strokes
            (s_side[0], s_side[1]) = side switch
            {
                DivideEdge.Left => (new Vector2(half, 0f), new Vector2(half, size.y)),
                DivideEdge.Right => (new Vector2(size.x - half, 0f), new Vector2(size.x - half, size.y)),
                DivideEdge.Top => (new Vector2(0f, half), new Vector2(size.x, half)),
                DivideEdge.Bottom => (new Vector2(0f, size.y - half), new Vector2(size.x, size.y - half)),
            };
#pragma warning restore CS8524
            ComputeSegments(s_side, closed: false, lineWidth, style, output);
        }

        // The painted stretches along an outline of the given length, by arc length, in order. The pattern is
        // sized by the width rounded to a whole pixel, and stroked at the width itself.
        private static void Intervals(float length, bool closed, float lineWidth, BorderLineStyle style,
            List<(float Start, float End)> intervals)
        {
            intervals.Clear();
            var width = Mathf.Max(1f, Mathf.Floor(lineWidth + 0.5f));
            if (style == BorderLineStyle.Dotted && width > 3f)
            {
                RoundDots(length, closed, width, intervals);
                return;
            }
            var dash = width;
            var gap = width;
            if (style == BorderLineStyle.Dashed)
            {
                dash *= width >= 3f ? 2f : 3f;
                gap *= width >= 3f ? 1f : 2f;
            }
            if (length <= dash * 2f)
            {
                // No room for two dashes: the line is drawn solid.
                intervals.Add((0f, length));
                return;
            }
            var twoDashes = 2f * dash + gap + (closed ? gap : 0f);
            if (length <= twoDashes)
            {
                var scale = length / twoDashes;
                dash *= scale;
                gap *= scale;
            }
            else if (style == BorderLineStyle.Dashed)
            {
                gap = BestGap(length, dash, gap, closed);
            }
            if (style == BorderLineStyle.Dotted && !closed)
            {
                SquareDots(length, (int)width, dash, gap, intervals);
                return;
            }
            Repeat(0f, length, dash, gap, intervals);
        }

        private static void Repeat(float from, float to, float dash, float gap, List<(float Start, float End)> intervals)
        {
            for (var at = from; at < to - 1e-4f; at += dash + gap)
            {
                intervals.Add((at, Mathf.Min(at + dash, to)));
            }
        }

        // How an open line of square dots ends, by its width and its whole length's remainder over two widths:
        // whether the first dot is drawn on its own, the pixels it grows by, the pixels its gap gains (or, below
        // zero, loses), whether the last dot is drawn on its own, and the pixels it grows by. The pattern alone
        // leaves a gap or a part dot at an end unless the length is an odd multiple of the width.
        private static readonly (bool Start, int Growth, int Offset, bool End, int EndGrowth)[][] s_squareDotEnds =
        {
            new[] { (true, 1, 1, false, 0), (false, 0, 0, false, 0) },
            new[] { (true, 0, -1, true, 0), (true, 0, -1, false, 0), (false, 0, 0, false, 0), (true, 0, 1, false, 0) },
            new[]
            {
                (true, 1, 1, true, 1), (true, 0, -1, true, 0), (true, 0, -1, false, 0),
                (false, 0, 0, false, 0), (true, 0, 1, false, 0), (true, 0, 1, true, 0),
            },
        };

        // An open line of square dots with a whole dot at each end (s_squareDotEnds): a first dot drawn on its own
        // moves the pattern to two widths past the line's start, give or take its gap's pixel, and a last dot
        // drawn on its own stops the pattern a pixel short of it.
        private static void SquareDots(float length, int width, float dash, float gap,
            List<(float Start, float End)> intervals)
        {
            var ends = s_squareDotEnds[width - 1][(int)Mathf.Round(length) % (2 * width)];
            var from = 0f;
            var to = length;
            if (ends.Start)
            {
                intervals.Add((0f, width + ends.Growth));
                from = (2 * width) + ends.Offset;
            }
            if (ends.End)
            {
                to = length - (width + ends.EndGrowth + 1);
            }
            Repeat(from, to, dash, gap, intervals);
            if (ends.End)
            {
                intervals.Add((length - width - ends.EndGrowth, length));
            }
        }

        // Round dots a diameter across, spaced so a whole number of them fits. An open line's first and last
        // dot sit half a diameter in, so their caps end at its ends.
        private static void RoundDots(float length, bool closed, float width, List<(float Start, float End)> intervals)
        {
            var inset = closed ? 0f : width * 0.5f;
            if (length < width * 2f)
            {
                intervals.Add((inset, inset));
                return;
            }
            var pitch = BestGap(length, width, width, closed) + width;
            var count = closed ? Mathf.Round(length / pitch) : Mathf.Round((length - width) / pitch) + 1f;
            for (var i = 0; i < count; i++)
            {
                var at = inset + (i * pitch);
                intervals.Add((at, at));
            }
        }

        // The gap nearest the preferred one that fits a whole number of dashes into the outline, the number the
        // preferred gap fits or one more, with as many gaps as an open line has between its dashes, or a closed
        // one has dashes.
        private static float BestGap(float length, float dash, float gap, bool closed)
        {
            var available = closed ? length : length + gap;
            var fewer = Mathf.Floor(available / (dash + gap));
            var more = fewer + 1f;
            var fewerGap = (length - (fewer * dash)) / (closed ? fewer : fewer - 1f);
            var moreGap = (length - (more * dash)) / (closed ? more : more - 1f);
            return moreGap <= 0f || Mathf.Abs(fewerGap - gap) < Mathf.Abs(moreGap - gap) ? fewerGap : moreGap;
        }

        private static float PathLength(IReadOnlyList<Vector2> polyline, bool closed)
        {
            var length = 0f;
            for (var s = 0; s < SegmentCount(polyline, closed); s++)
            {
                length += Segment(polyline, s).magnitude;
            }
            return length;
        }

        private static int SegmentCount(IReadOnlyList<Vector2> polyline, bool closed)
            => closed ? polyline.Count : polyline.Count - 1;

        private static Vector2 Segment(IReadOnlyList<Vector2> polyline, int s)
            => polyline[(s + 1) % polyline.Count] - polyline[s];

        // The point arc length `at` reaches on segment s, which starts at arc length segStart.
        private static Vector2 PointOn(IReadOnlyList<Vector2> polyline, int s, float segStart, float at)
        {
            var segment = Segment(polyline, s);
            var segLen = segment.magnitude;
            return segLen < 1e-6f ? polyline[s] : polyline[s] + (segment * ((at - segStart) / segLen));
        }

        // Lays each interval onto the polyline, breaking a stretch at every vertex it crosses.
        private static void Emit(IReadOnlyList<Vector2> polyline, bool closed, List<(float Start, float End)> intervals,
            List<(Vector2 From, Vector2 To)> output)
        {
            var segCount = SegmentCount(polyline, closed);
            var s = 0;
            var segStart = 0f;
            foreach (var (start, end) in intervals)
            {
                while (s < segCount - 1 && start >= segStart + Segment(polyline, s).magnitude)
                {
                    segStart += Segment(polyline, s).magnitude;
                    s++;
                }
                if (start == end)
                {
                    var dot = PointOn(polyline, s, segStart, start);
                    output.Add((dot, dot));
                    continue;
                }
                var k = s;
                var kStart = segStart;
                for (; k < segCount; k++)
                {
                    var kEnd = kStart + Segment(polyline, k).magnitude;
                    var from = Mathf.Max(start, kStart);
                    var to = Mathf.Min(end, kEnd);
                    if (to - from > 1e-6f)
                    {
                        output.Add((PointOn(polyline, k, kStart, from), PointOn(polyline, k, kStart, to)));
                    }
                    if (end <= kEnd)
                    {
                        break;
                    }
                    kStart = kEnd;
                }
            }
        }
    }
}
