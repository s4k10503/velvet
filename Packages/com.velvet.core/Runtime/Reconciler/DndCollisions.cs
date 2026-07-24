#nullable enable
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The built-in collision strategies: <see cref="RectIntersection"/>, <see cref="ClosestCenter"/>, and
    /// <see cref="PointerWithin"/>. All are pure functions over a <see cref="DndCollisionQuery"/> (no panel
    /// access), returning a single winning id or null rather than a ranked collision list — the context
    /// only ever consumes the first collision anyway, and the delegate's return can be extended compatibly
    /// later. None of them consider occlusion; all are rect-based only.
    /// </summary>
    public static class DndCollisions
    {
        /// <summary>Largest ActiveRect-droppable intersection area wins. Ties
        /// resolve by registration order (first candidate wins).</summary>
        public static readonly DndCollisionDetection RectIntersection = (in DndCollisionQuery query) =>
        {
            string? winner = null;
            var bestArea = 0f;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var overlap = Intersection(query.ActiveRect, candidate.Rect);
                var area = overlap.width * overlap.height;
                if (area > bestArea)
                {
                    bestArea = area;
                    winner = candidate.Id;
                }
            }
            return winner;
        };

        /// <summary>Droppable whose center is nearest ActiveRect's center wins — the forgiving strategy
        /// sortable lists and coarse grids want. Every candidate collides by definition, so this never
        /// returns null while any candidate exists.</summary>
        public static readonly DndCollisionDetection ClosestCenter = (in DndCollisionQuery query) =>
        {
            string? winner = null;
            var bestDistance = float.MaxValue;
            var activeCenter = query.ActiveRect.center;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var distance = (candidate.Rect.center - activeCenter).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    winner = candidate.Id;
                }
            }
            return winner;
        };

        /// <summary>Droppable rect containing the pointer wins; on nesting, the innermost (smallest
        /// area) container takes it.</summary>
        public static readonly DndCollisionDetection PointerWithin = (in DndCollisionQuery query) =>
        {
            string? winner = null;
            var bestArea = float.MaxValue;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                if (!candidate.Rect.Contains(query.PointerPosition))
                {
                    continue;
                }
                var area = candidate.Rect.width * candidate.Rect.height;
                if (area < bestArea)
                {
                    bestArea = area;
                    winner = candidate.Id;
                }
            }
            return winner;
        };

        private static Rect Intersection(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                return Rect.zero;
            }
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
