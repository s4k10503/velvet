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

    // Arc-length dash/dot marcher on top of Painter2D. A dashed or dotted outline is walked here: the outline
    // is a flat polyline (SilhouetteFace.BuildShearedRoundedRectPolyline for a full border, a 2-point segment
    // for a divide edge), and this walks it by arc length emitting the painted runs, in the pattern
    // styling-flexbox-and-gap.md describes and DashedBorderPainterTests pins. A round dot is a zero-length
    // round-capped stroke. `closed` wraps the last point back to point 0 (a full border); an open polyline
    // leaves the raw ends (a divide edge).
    internal static class DashedBorderPainter
    {
        // Reused across draws so a steady-state repaint allocates nothing. UI Toolkit generateVisualContent
        // callbacks run sequentially on the main thread, so a single shared buffer is safe (no re-entrancy).
        private static readonly List<(Vector2 From, Vector2 To)> s_segments = new();
        private static readonly List<(float Start, float End)> s_intervals = new();
        private static readonly Vector2[] s_side = new Vector2[2];

        // A no-op for Solid (the native border already draws that), a near-transparent color, or a
        // hairline width — callers do not pre-filter for the latter two.
        public static void StrokeDashed(Painter2D painter, IReadOnlyList<Vector2> polyline, bool closed,
            float lineWidth, Color color, BorderLineStyle style)
        {
            if (style == BorderLineStyle.Solid || color.a <= 0.004f || lineWidth <= 0.01f)
            {
                return;
            }
            ComputeSegments(polyline, closed, lineWidth, style, s_segments);
            Stroke(painter, lineWidth, color);
        }

        // Strokes one side of a square-cornered size.x × size.y box on its own, as StrokeDashed's guards allow.
        public static void StrokeSide(Painter2D painter, DivideEdge side, Vector2 size, float lineWidth, Color color,
            BorderLineStyle style)
        {
            if (style == BorderLineStyle.Solid || color.a <= 0.004f || lineWidth <= 0.01f)
            {
                return;
            }
            ComputeSideRuns(side, size, lineWidth, style, s_segments);
            Stroke(painter, lineWidth, color);
        }

        private static void Stroke(Painter2D painter, float lineWidth, Color color)
        {
            if (s_segments.Count == 0)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.lineJoin = LineJoin.Miter;
            // A round dot is a zero-length run, which only a round cap gives a footprint. Every other run is
            // butt-capped so it is exactly as long as computed: a round cap would bleed half a width past each
            // end and close the gaps.
            var first = s_segments[0];
            painter.lineCap = first.From == first.To ? LineCap.Round : LineCap.Butt;

            foreach (var (from, to) in s_segments)
            {
                painter.BeginPath();
                painter.MoveTo(from);
                painter.LineTo(to);
                painter.Stroke();
            }
        }

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
