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

    // Arc-length dash/dot marcher on top of Painter2D. UI Toolkit's Painter2D has no dash-array, so a dashed
    // or dotted outline is walked here: the outline is a flat polyline (SilhouetteFace.BuildShearedRoundedRect-
    // Polyline for a full border, a 2-point segment for a divide edge), and this walks it by arc length
    // emitting alternating painted runs. Dashed: butt-capped straight runs, dash ≈ 3× the line width, gap ≈ 2×.
    // Dotted: zero-length round-capped strokes spaced ≈ 2× the line width — a zero-length round-capped stroke
    // renders a filled dot of the line's diameter (the standard canvas dotted-line trick). `closed` wraps the
    // last point back to point 0 (a full border); an open polyline leaves the raw ends (a divide edge).
    internal static class DashedBorderPainter
    {
        private const float DashLengthFactor = 3f;
        private const float DashGapFactor = 2f;
        private const float DotSpacingFactor = 2f;

        // Reused across draws so a steady-state repaint allocates nothing. UI Toolkit generateVisualContent
        // callbacks run sequentially on the main thread, so a single shared buffer is safe (no re-entrancy).
        private static readonly List<(Vector2 From, Vector2 To)> s_segments = new();

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
            if (s_segments.Count == 0)
            {
                return;
            }

            painter.strokeColor = color;
            painter.lineWidth = lineWidth;
            painter.lineJoin = LineJoin.Miter;
            // A dot is a zero-length stroke: only a round cap gives it a visible (circular) footprint. A dash
            // uses butt caps so a run's length is exactly the dash length (a round cap would bleed half a width
            // past each end and close the gaps).
            painter.lineCap = style == BorderLineStyle.Dotted ? LineCap.Round : LineCap.Butt;

            foreach (var (from, to) in s_segments)
            {
                painter.BeginPath();
                painter.MoveTo(from);
                painter.LineTo(to);
                painter.Stroke();
            }
        }

        // Pure arc-length marcher: fills `output` with the individual stroke runs for the style. A Dashed run
        // is a (from, to) pair; a Dotted run is a zero-length dot (from == to). Painter-free so it is unit-
        // testable headless — a Painter2D cannot be read back. Runs are broken at polyline vertices, so a dash
        // that crosses a corner becomes two runs meeting at the corner.
        public static void ComputeSegments(IReadOnlyList<Vector2> polyline, bool closed, float lineWidth,
            BorderLineStyle style, List<(Vector2 From, Vector2 To)> output)
        {
            output.Clear();
            if (polyline == null || polyline.Count < 2 || lineWidth <= 0f || style == BorderLineStyle.Solid)
            {
                return;
            }
            if (style == BorderLineStyle.Dotted)
            {
                ComputeDots(polyline, closed, lineWidth, output);
            }
            else
            {
                ComputeDashes(polyline, closed, lineWidth, output);
            }
        }

        private static void ComputeDashes(IReadOnlyList<Vector2> polyline, bool closed, float lineWidth,
            List<(Vector2 From, Vector2 To)> output)
        {
            var dashLen = lineWidth * DashLengthFactor;
            var gapLen = lineWidth * DashGapFactor;
            var period = dashLen + gapLen;
            if (period <= 0f)
            {
                return;
            }

            // Position within the current [0, period) dash cycle: [0, dashLen) paints, the rest is the gap.
            var phase = 0f;
            var count = polyline.Count;
            var segCount = closed ? count : count - 1;
            for (var s = 0; s < segCount; s++)
            {
                var a = polyline[s];
                var b = polyline[(s + 1) % count];
                var segVec = b - a;
                var segLen = segVec.magnitude;
                if (segLen < 1e-6f)
                {
                    continue;
                }
                var dir = segVec / segLen;

                var walked = 0f;
                while (walked < segLen - 1e-6f)
                {
                    var inDash = phase < dashLen;
                    var remainingInState = inDash ? dashLen - phase : period - phase;
                    var step = Mathf.Min(remainingInState, segLen - walked);
                    if (inDash)
                    {
                        var from = a + (dir * walked);
                        var to = a + (dir * (walked + step));
                        output.Add((from, to));
                    }
                    walked += step;
                    phase += step;
                    if (phase >= period)
                    {
                        phase -= period;
                    }
                }
            }
        }

        private static void ComputeDots(IReadOnlyList<Vector2> polyline, bool closed, float lineWidth,
            List<(Vector2 From, Vector2 To)> output)
        {
            var spacing = lineWidth * DotSpacingFactor;
            if (spacing < 1e-6f)
            {
                return;
            }

            // Distance still to travel before the next dot; 0 places the first dot at the polyline start.
            var remaining = 0f;
            var count = polyline.Count;
            var segCount = closed ? count : count - 1;
            for (var s = 0; s < segCount; s++)
            {
                var a = polyline[s];
                var b = polyline[(s + 1) % count];
                var segVec = b - a;
                var segLen = segVec.magnitude;
                if (segLen < 1e-6f)
                {
                    continue;
                }
                var dir = segVec / segLen;

                var pos = 0f;
                while (remaining < segLen - pos)
                {
                    pos += remaining;
                    var pt = a + (dir * pos);
                    output.Add((pt, pt));
                    remaining = spacing;
                }
                remaining -= segLen - pos;
            }
        }
    }
}
