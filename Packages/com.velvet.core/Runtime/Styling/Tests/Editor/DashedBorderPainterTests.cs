using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the arc-length dash/dot marcher (<see cref="DashedBorderPainter.ComputeSegments"/>) and the
    /// polyline flattener it consumes (<see cref="SilhouetteFace.BuildShearedRoundedRectPolyline"/>). UI Toolkit
    /// has no dash-array, so a dashed / dotted outline is walked by arc length: dashes are butt-capped runs of
    /// ~3× the line width spaced by ~2× gaps; dots are zero-length round-capped strokes spaced ~2× the width.
    /// A closed polyline wraps its last edge back to point 0; the flattened rounded-rect gains points as its
    /// corners round (the sink-refactor parity guard). GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DashedBorderPainterTests
    {
        private static List<(Vector2 From, Vector2 To)> Compute(IReadOnlyList<Vector2> polyline, bool closed,
            float lineWidth, BorderLineStyle style)
        {
            var output = new List<(Vector2 From, Vector2 To)>();
            DashedBorderPainter.ComputeSegments(polyline, closed, lineWidth, style, output);
            return output;
        }

        [Test]
        public void Given_AKnownEdgeAndWidth_When_DashesComputed_Then_TheRunCountMatchesTheFormula()
        {
            // Arrange — an open 26-unit edge at width 2: dash 6, gap 4, period 10 → runs at [0,6] [10,16] [20,26].
            var edge = new List<Vector2> { new(0f, 0f), new(26f, 0f) };

            // Act
            var runs = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert
            Assert.That(runs.Count, Is.EqualTo(3));
        }

        [Test]
        public void Given_AKnownEdgeAndWidth_When_DashesComputed_Then_EachRunIsThreeWidthsLong()
        {
            // Arrange
            var edge = new List<Vector2> { new(0f, 0f), new(26f, 0f) };

            // Act — the first painted run spans exactly the dash length (3× the line width).
            var runs = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert
            Assume.That(runs, Is.Not.Empty, "Precondition: at least one dash run");
            Assert.That((runs[0].To - runs[0].From).magnitude, Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_ADottedEdge_When_SegmentsComputed_Then_EveryDotIsZeroLength()
        {
            // Arrange — an open 20-unit edge at width 2.
            var edge = new List<Vector2> { new(0f, 0f), new(20f, 0f) };

            // Act — a dot is a zero-length stroke (from == to), rendered as a round cap.
            var dots = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dotted);

            // Assert
            Assume.That(dots, Is.Not.Empty, "Precondition: at least one dot");
            Assert.That(dots.All(d => d.From == d.To), Is.True);
        }

        [Test]
        public void Given_ADottedEdge_When_SegmentsComputed_Then_ConsecutiveDotsAreTwoWidthsApart()
        {
            // Arrange
            var edge = new List<Vector2> { new(0f, 0f), new(20f, 0f) };

            // Act — dots are spaced by 2× the line width along the arc.
            var dots = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dotted);

            // Assert
            Assume.That(dots.Count, Is.GreaterThan(1), "Precondition: at least two dots to measure spacing");
            Assert.That((dots[1].From - dots[0].From).magnitude, Is.EqualTo(4f).Within(1e-3f));
        }

        [Test]
        public void Given_AClosedSquare_When_SegmentsComputed_Then_TheWrapEdgeBackToPointZeroIsWalked()
        {
            // Arrange — a closed square. Its last edge runs from point 3 back to point 0 (the left edge, x = 0);
            // an OPEN polyline would never traverse it, so a dot on that edge proves the wrap.
            var square = new List<Vector2> { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            // Act
            var dots = Compute(square, closed: true, lineWidth: 2f, BorderLineStyle.Dotted);

            // Assert — a dot sits on the interior of the point-3 → point-0 edge (x == 0, strictly between the
            // two corners), which only the closed wrap produces.
            Assert.That(dots.Any(d => Mathf.Approximately(d.From.x, 0f) && d.From.y > 0.1f && d.From.y < 9.9f), Is.True);
        }

        [Test]
        public void Given_ARoundedRectPolyline_When_Built_Then_ItHasMorePointsThanASquare()
        {
            // Arrange — the sink refactor must keep the bezier corners: a rounded rect samples each corner into
            // chords, so it flattens to strictly more points than a square (whose corners emit none).
            var square = new List<Vector2>();
            SilhouetteFace.BuildShearedRoundedRectPolyline(square, 0f, 100f, 100f, 0f, 0f, 0f, 0f, 0f, 0f);
            var rounded = new List<Vector2>();

            // Act
            SilhouetteFace.BuildShearedRoundedRectPolyline(rounded, 0f, 100f, 100f, 0f, 0f, 20f, 20f, 20f, 20f);

            // Assert
            Assume.That(square, Is.Not.Empty, "Precondition: the square flattens to a polyline");
            Assert.That(rounded.Count, Is.GreaterThan(square.Count));
        }
    }
}
