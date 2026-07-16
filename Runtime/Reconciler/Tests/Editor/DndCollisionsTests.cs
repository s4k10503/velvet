using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the three built-in collision strategies as pure functions over a
    /// <c>DndCollisionQuery</c> — no panel involved: <c>RectIntersection</c> ranks by overlap area
    /// (ties to registration order), <c>ClosestCenter</c> by center distance, and
    /// <c>PointerWithin</c> by pointer containment with innermost-wins nesting.
    /// </summary>
    internal sealed class DndCollisionsTests
    {
        private static DndCollisionQuery Query(Rect activeRect, Vector2 pointer, params DndDroppableRect[] droppables)
            => new(activeRect, pointer, new List<DndDroppableRect>(droppables));

        private static DndDroppableRect Candidate(string id, Rect rect) => new(id, rect, null);

        [Test]
        public void Given_TwoOverlappingCandidates_When_RectIntersectionRuns_Then_TheLargerOverlapWins()
        {
            // Arrange — the active rect overlaps "small" by 10x10 and "large" by 30x10.
            var query = Query(new Rect(0, 0, 40, 10), Vector2.zero,
                Candidate("small", new Rect(30, 0, 100, 100)),
                Candidate("large", new Rect(10, 0, 100, 100)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("large"));
        }

        [Test]
        public void Given_NoCandidateOverlapsTheActiveRect_When_RectIntersectionRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero,
                Candidate("far", new Rect(100, 100, 10, 10)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }

        [Test]
        public void Given_TwoCandidatesWithEqualOverlap_When_RectIntersectionRuns_Then_TheFirstRegisteredWins()
        {
            // Arrange — both candidates overlap the active rect by an identical 10x10 corner.
            var query = Query(new Rect(0, 0, 20, 20), Vector2.zero,
                Candidate("first", new Rect(10, 10, 50, 50)),
                Candidate("second", new Rect(10, 10, 50, 50)));

            // Act
            var winner = DndCollisions.RectIntersection(in query);

            // Assert — ties resolve deterministically by registration order.
            Assert.That(winner, Is.EqualTo("first"));
        }

        [Test]
        public void Given_TwoCandidates_When_ClosestCenterRuns_Then_TheNearerCenterWinsEvenWithoutOverlap()
        {
            // Arrange — neither candidate overlaps the active rect; "near" has the closer center.
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero,
                Candidate("far", new Rect(200, 200, 10, 10)),
                Candidate("near", new Rect(30, 0, 10, 10)));

            // Act
            var winner = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("near"));
        }

        [Test]
        public void Given_NoCandidates_When_ClosestCenterRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), Vector2.zero);

            // Act
            var winner = DndCollisions.ClosestCenter(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }

        [Test]
        public void Given_APointerInsideOneCandidate_When_PointerWithinRuns_Then_TheContainingRectWins()
        {
            // Arrange — the active rect overlaps "other" more, but the POINTER sits inside "hit".
            var query = Query(new Rect(0, 0, 60, 60), new Vector2(105, 105),
                Candidate("other", new Rect(0, 0, 50, 50)),
                Candidate("hit", new Rect(100, 100, 20, 20)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(winner, Is.EqualTo("hit"));
        }

        [Test]
        public void Given_NestedCandidatesBothContainingThePointer_When_PointerWithinRuns_Then_TheInnermostWins()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), new Vector2(55, 55),
                Candidate("outer", new Rect(0, 0, 200, 200)),
                Candidate("inner", new Rect(50, 50, 20, 20)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert — on nesting, the smallest containing rect takes the collision.
            Assert.That(winner, Is.EqualTo("inner"));
        }

        [Test]
        public void Given_APointerOutsideEveryCandidate_When_PointerWithinRuns_Then_NoCollisionIsReported()
        {
            // Arrange
            var query = Query(new Rect(0, 0, 10, 10), new Vector2(500, 500),
                Candidate("a", new Rect(0, 0, 50, 50)),
                Candidate("b", new Rect(100, 100, 50, 50)));

            // Act
            var winner = DndCollisions.PointerWithin(in query);

            // Assert
            Assert.That(winner, Is.Null);
        }
    }
}
