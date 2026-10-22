using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a box's dashed side is laid out: a square-cornered side is a line of its own from corner to
    /// corner (<see cref="DashedBorderPainter.ComputeSideRuns"/>), and every side is cut to its own corner of the
    /// box (<see cref="DashedBorderPainter.ComputeSidePieces"/>). GWT, one assert per case.
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
