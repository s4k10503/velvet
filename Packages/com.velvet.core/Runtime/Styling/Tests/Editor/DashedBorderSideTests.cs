using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a square-cornered box's dashed side is laid out
    /// (<see cref="DashedBorderPainter.ComputeSideRuns"/>): each side is a line of its own from corner to corner.
    /// GWT, one assert per case.
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
    }
}
