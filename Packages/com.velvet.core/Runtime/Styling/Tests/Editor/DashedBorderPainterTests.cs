using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the arc-length dash/dot marcher (<see cref="DashedBorderPainter.ComputeSegments"/>) and the
    /// polyline flattener it consumes (<see cref="SilhouetteFace.BuildShearedRoundedRectPolyline"/>). A dashed /
    /// dotted outline is walked by arc length in Chromium's pattern for a stroked border.
    /// A closed polyline wraps its last edge back to point 0; the flattened rounded-rect gains points as its
    /// corners round, matching the corner sampling the shared Painter2D path builder emits. GWT, one assert
    /// per case.
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

        // GREEN_ON_BASE(characterization): the base draws every dot round, so a dot thicker than 3px was already one.
        [Test]
        public void Given_ADottedEdge_When_SegmentsComputed_Then_EveryDotIsZeroLength()
        {
            // Arrange — an open 20-unit edge at width 4, past the 3px up to which a dot is square.
            var edge = new List<Vector2> { new(0f, 0f), new(20f, 0f) };

            // Act — a round dot is a zero-length stroke (from == to), rendered as a round cap.
            var dots = Compute(edge, closed: false, lineWidth: 4f, BorderLineStyle.Dotted);

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
        public void Given_AThreePixelDashedEdge_When_DashesComputed_Then_EachRunIsTwoWidthsLong()
        {
            // Arrange — from 3px up a dash is 2× the width and a gap 1×: dash 6, gap 3 fitted to 1 on 20 units.
            var edge = new List<Vector2> { new(0f, 0f), new(20f, 0f) };

            // Act
            var runs = Compute(edge, closed: false, lineWidth: 3f, BorderLineStyle.Dashed);

            // Assert
            Assert.That((runs[0].To - runs[0].From).magnitude, Is.EqualTo(6f).Within(1e-3f));
        }

        [Test]
        public void Given_AnEdgeThePatternDoesNotDivide_When_DashesComputed_Then_TheLastRunEndsAtTheEdgesEnd()
        {
            // Arrange — dash 6 and gap 4 do not divide 30 units; a gap of 2 fits four dashes end to end.
            var edge = new List<Vector2> { new(0f, 0f), new(30f, 0f) };

            // Act
            var runs = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert
            Assert.That(runs[runs.Count - 1].To.x, Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void Given_AnEdgeNoLongerThanTwoDashes_When_DashesComputed_Then_OneRunCoversIt()
        {
            // Arrange — 10 units is no longer than two dashes of 6, so the line is drawn solid.
            var edge = new List<Vector2> { new(0f, 0f), new(10f, 0f) };

            // Act
            var runs = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert
            Assert.That((runs[0].To - runs[0].From).magnitude, Is.EqualTo(10f).Within(1e-3f));
        }

        [Test]
        public void Given_AnEdgeShorterThanTwoDashesAndAGap_When_DashesComputed_Then_TheTwoDashesShrinkToFit()
        {
            // Arrange — dash 6, gap 4 and dash 6 take 16 units; on 14 each shrinks by 14/16, a dash to 5.25.
            var edge = new List<Vector2> { new(0f, 0f), new(14f, 0f) };

            // Act
            var runs = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert
            Assert.That((runs[0].To - runs[0].From).magnitude, Is.EqualTo(5.25f).Within(1e-3f));
        }

        [Test]
        public void Given_AClosedSquareThePatternDoesNotDivide_When_DashesComputed_Then_EachSideHoldsOneWholeDash()
        {
            // Arrange — dash 6 and gap 4 do not divide the 44-unit outline; a gap of 5 fits one dash per side.
            var square = new List<Vector2> { new(0f, 0f), new(11f, 0f), new(11f, 11f), new(0f, 11f) };

            // Act
            var runs = Compute(square, closed: true, lineWidth: 2f, BorderLineStyle.Dashed);

            // Assert — a dash crossing a corner would be broken into two runs there.
            Assert.That(runs.Count, Is.EqualTo(4));
        }

        [Test]
        public void Given_AThinDottedEdge_When_SegmentsComputed_Then_EachDotIsASquareAWidthLong()
        {
            // Arrange — up to 3px a dot is a butt-capped run as long as the line is wide.
            var edge = new List<Vector2> { new(0f, 0f), new(20f, 0f) };

            // Act
            var dots = Compute(edge, closed: false, lineWidth: 2f, BorderLineStyle.Dotted);

            // Assert
            Assert.That((dots[0].To - dots[0].From).magnitude, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void Given_AThickDottedEdge_When_SegmentsComputed_Then_TheLastDotIsHalfAWidthFromTheEnd()
        {
            // Arrange — 4px dots on 24 units: the gap fits to 8/3 so four dots run from 2 to 22, their caps
            // reaching both ends.
            var edge = new List<Vector2> { new(0f, 0f), new(24f, 0f) };

            // Act
            var dots = Compute(edge, closed: false, lineWidth: 4f, BorderLineStyle.Dotted);

            // Assert
            Assert.That(dots[dots.Count - 1].From.x, Is.EqualTo(22f).Within(1e-3f));
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
            // Arrange — the polyline flattener (an IRoundedRectPathSink) must keep the bezier corners a
            // Painter2D path would draw: a rounded rect samples each corner into chords, so it flattens to
            // strictly more points than a square (whose corners emit none).
            var square = new List<Vector2>();
            SilhouetteFace.BuildShearedRoundedRectPolyline(square,
                new ShearedRoundedRect { Width = 100f, Height = 100f });
            var rounded = new List<Vector2>();

            // Act
            SilhouetteFace.BuildShearedRoundedRectPolyline(rounded, new ShearedRoundedRect
            {
                Width = 100f,
                Height = 100f,
                RadiusTopLeft = 20f,
                RadiusTopRight = 20f,
                RadiusBottomRight = 20f,
                RadiusBottomLeft = 20f,
            });

            // Assert
            Assume.That(square, Is.Not.Empty, "Precondition: the square flattens to a polyline");
            Assert.That(rounded.Count, Is.GreaterThan(square.Count));
        }
    }
}
