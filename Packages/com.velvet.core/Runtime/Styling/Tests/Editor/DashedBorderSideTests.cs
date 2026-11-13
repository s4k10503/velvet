using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a box's dashed side is laid out and painted: a square-cornered side is a line of its own from
    /// corner to corner (<see cref="DashedBorderPainter.ComputeSideRuns"/>), every side is cut to its own corner of the
    /// box (<see cref="DashedBorderPainter.ComputeSidePieces"/>), and a side is painted with its box's shear. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DashedBorderSideTests
    {
        [Test]
        public void Given_ASquareCorneredBox_When_ItsTopSidesDashesAreComputed_Then_TheLastEndsAtTheCorner()
        {
            // Arrange — the top of a 40 × 20 box at width 2: dash 6, gap 4 fitted to 16/3, ending on a dash at 40.
            // One pattern over the whole outline ends the top's last dash at 37.
            var runs = new List<(Vector2 From, Vector2 To)>();

            // Act
            DashedBorderPainter.ComputeSideRuns(DivideEdge.Top, new Vector2(40f, 20f), 2f, BorderLineStyle.Dashed, runs);

            // Assert
            Assert.That(runs[runs.Count - 1].To.x, Is.EqualTo(40f).Within(1e-3f));
        }

        [Test]
        public void Given_ASquareBoxWithEveryBorderTwoWide_When_ItsTopSidesPiecesAreComputed_Then_TheFirstStopsAtTheCornerMiter()
        {
            // Arrange — the top's first dash covers 0 to 6 across the 2-wide band; the left side owns the corner's
            // lower half, the triangle under the line from (0, 0) to (2, 2), which takes 2 of its 12.
            var box = new BorderBox { Size = new Vector2(40f, 20f), Left = 2f, Top = 2f, Right = 2f, Bottom = 2f };
            var points = new List<Vector2>();
            var counts = new List<int>();

            // Act
            DashedBorderPainter.ComputeSidePieces(DivideEdge.Top, box, BorderLineStyle.Dashed, points, counts);

            // Assert
            Assert.That(Area(points, counts[0]), Is.EqualTo(10f).Within(1e-3f));
        }

        [Test]
        public void Given_ABoxWhoseSideWidthsAddUpToMoreThanItsWidth_When_ItsRightSidesPiecesAreComputed_Then_TheyStopAtTheLeftWidth()
        {
            // Arrange — 15 wide on the left and on the right of a box 20 across: the right side's one run covers x 5
            // to 20, and the left side owns everything left of x 15.
            var box = new BorderBox { Size = new Vector2(20f, 20f), Left = 15f, Top = 2f, Right = 15f, Bottom = 2f };
            var points = new List<Vector2>();
            var counts = new List<int>();

            // Act
            DashedBorderPainter.ComputeSidePieces(DivideEdge.Right, box, BorderLineStyle.Dashed, points, counts);

            // Assert
            Assert.That(points.Min(p => p.x), Is.EqualTo(15f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base paints a piece the clip leaves with three corners.
        [Test]
        public void Given_ADashedBoxRoundedLessThanHalfItsBorder_When_ItsLeftSidesPiecesAreComputed_Then_TheyReachIntoTheCornerTheTopsFirstDashCrosses()
        {
            // Arrange — 8 wide all round and rounded 4, the stroke's centre line is square, and the outline's first dash
            // runs right from its corner at (4, 4) across the whole band. The left side owns what lies below the line
            // from (0, 0) to (8, 8), so it paints that dash's end there, the triangle (4, 4), (4, 8), (8, 8).
            var box = new BorderBox
            {
                Size = new Vector2(80f, 40f), Left = 8f, Top = 8f, Right = 8f, Bottom = 8f,
                RadiusTopLeft = 4f, RadiusTopRight = 4f, RadiusBottomRight = 4f, RadiusBottomLeft = 4f,
            };
            var points = new List<Vector2>();
            var counts = new List<int>();

            // Act
            DashedBorderPainter.ComputeSidePieces(DivideEdge.Left, box, BorderLineStyle.Dashed, points, counts);

            // Assert
            Assert.That(Covers(points, counts, new Vector2(5f, 7f)), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base shears a dashed side skewed on x alone.
        [Test]
        public void Given_ALeftSideSkewedOnXAlone_When_Painted_Then_ItsImageSpansTheShearedSide()
        {
            // Arrange — the left side of a 40 × 20 box at width 2 lies along x 0 to 2; skewX(45deg) about the box's centre
            // carries its top 10 left and its bottom 10 right, out to x -10 and 10, where an upright side is 2 wide.
            var box = new BorderBox { Size = new Vector2(40f, 20f), Left = 2f, Top = 2f, Right = 2f, Bottom = 2f };
            var image = ScriptableObject.CreateInstance<VectorImage>();
            try
            {
                // Act
                using (var painter = new Painter2D())
                {
                    DashedBorderPainter.PaintSide(painter, DivideEdge.Left, box, Color.white, BorderLineStyle.Dashed,
                        new SilhouetteFace.Shearing(40f, 20f, 1f, 0f));
                    painter.SaveToVectorImage(image);
                }

                // Assert — within the antialiased fringe the tessellation adds.
                Assert.That(image.width, Is.EqualTo(20f).Within(2f));
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        // GREEN_ON_BASE(characterization): the base's Painter2D meshes a sub-path left open as it meshes the closed one.
        // A `MUTANT_SURVIVES` over PaintSide's ClosePath rests on this.
        [Test]
        public void Given_AQuadLeftOpen_When_Filled_Then_ItMeshesToAsManyVerticesAsTheClosedOne()
        {
            // Arrange
            var closed = FilledVertices(close: true);

            // Act
            var open = FilledVertices(close: false);

            // Assert — -1 where the closed quad meshed to nothing, which no count can equal.
            Assert.That(open, Is.EqualTo(closed > 0 ? closed : -1));
        }

        // The vertices of a filled 12 × 8 quad saved to a VectorImage, its sub-path closed or left open.
        private static int FilledVertices(bool close)
        {
            var image = ScriptableObject.CreateInstance<VectorImage>();
            try
            {
                using (var painter = new Painter2D())
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(0f, 0f));
                    painter.LineTo(new Vector2(12f, 0f));
                    painter.LineTo(new Vector2(12f, 8f));
                    painter.LineTo(new Vector2(0f, 8f));
                    if (close)
                    {
                        painter.ClosePath();
                    }
                    painter.Fill();
                    painter.SaveToVectorImage(image);
                }
                var vertices = typeof(VectorImage).GetField("vertices", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(image) as System.Array;
                return vertices?.Length ?? 0;
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        // Whether point lies inside one of the convex pieces counts lays out in points.
        private static bool Covers(List<Vector2> points, List<int> counts, Vector2 point)
        {
            var at = 0;
            foreach (var count in counts)
            {
                var positive = false;
                var negative = false;
                for (var i = 0; i < count; i++)
                {
                    var p = points[at + i];
                    var q = points[at + ((i + 1) % count)];
                    var side = ((q.x - p.x) * (point.y - p.y)) - ((q.y - p.y) * (point.x - p.x));
                    positive |= side > 0f;
                    negative |= side < 0f;
                }
                if (!(positive && negative))
                {
                    return true;
                }
                at += count;
            }
            return false;
        }

        // The area of the polygon whose count vertices open points.
        private static float Area(List<Vector2> points, int count)
        {
            var twice = 0f;
            for (var i = 0; i < count; i++)
            {
                var p = points[i];
                var q = points[(i + 1) % count];
                twice += (p.x * q.y) - (q.x * p.y);
            }
            return Mathf.Abs(twice) * 0.5f;
        }
    }
}
