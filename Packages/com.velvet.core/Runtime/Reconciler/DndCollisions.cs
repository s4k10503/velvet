#nullable enable
using System;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The built-in collision strategies: <see cref="RectIntersection"/>, <see cref="ClosestCenter"/>,
    /// <see cref="ClosestCorners"/> and <see cref="PointerWithin"/>. Each is a pure function over a
    /// <see cref="DndCollisionQuery"/> (no panel access) returning every colliding droppable ranked best
    /// first, with the same ranking value, rounding and tie order as the algorithm of the same name in
    /// <c>@dnd-kit/core</c> (<c>packages/core/src/utilities/algorithms</c>). The context takes the first
    /// entry as the over target. None of them consider occlusion; all are rect-based only.
    /// </summary>
    public static class DndCollisions
    {
        /// <summary>Droppables overlapping the active rect, ranked by intersection ratio —
        /// <c>intersection / (droppable + active - intersection)</c>, rounded to four decimals — greatest
        /// first. Rects that only touch at an edge or corner do not collide. Equal ratios keep
        /// registration order.</summary>
        public static readonly DndCollisionDetection RectIntersection = (in DndCollisionQuery query) =>
        {
            var found = new DndCollision[query.Droppables.Count];
            var count = 0;
            var active = query.ActiveRect;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var ratio = IntersectionRatio(candidate.Rect, active);
                if (ratio > 0)
                {
                    found[count++] = new DndCollision(candidate.Id, candidate.Data, ratio);
                }
            }
            return Rank(found, count, descending: true);
        };

        /// <summary>Every droppable, ranked by the distance from its center to the active rect's center,
        /// nearest first — the forgiving strategy sortable lists and coarse grids want. Equal distances
        /// keep registration order.</summary>
        public static readonly DndCollisionDetection ClosestCenter = (in DndCollisionQuery query) =>
        {
            var found = new DndCollision[query.Droppables.Count];
            var activeCenter = query.ActiveRect.center;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var center = candidate.Rect.center;
                found[i] = new DndCollision(
                    candidate.Id, candidate.Data, Distance(center.x, center.y, activeCenter.x, activeCenter.y));
            }
            return Rank(found, found.Length, descending: false);
        };

        /// <summary>Every droppable, ranked by the mean distance between its four corners and the active
        /// rect's corresponding four (top-left, top-right, bottom-left, bottom-right), rounded to four
        /// decimals, nearest first. Equal values keep registration order.</summary>
        public static readonly DndCollisionDetection ClosestCorners = (in DndCollisionQuery query) =>
        {
            var found = new DndCollision[query.Droppables.Count];
            var active = new Vector2[4];
            Corners(query.ActiveRect, active);
            var corners = new Vector2[4];
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                Corners(candidate.Rect, corners);
                var sum = 0.0;
                for (var c = 0; c < 4; c++)
                {
                    sum += Distance(corners[c].x, corners[c].y, active[c].x, active[c].y);
                }
                found[i] = new DndCollision(candidate.Id, candidate.Data, RoundToFourDecimals(sum / 4));
            }
            return Rank(found, found.Length, descending: false);
        };

        /// <summary>Droppables whose rect contains the pointer, edges included, ranked by the mean
        /// distance from the pointer to the rect's four corners, rounded to four decimals, smallest
        /// first. Equal values keep registration order.</summary>
        public static readonly DndCollisionDetection PointerWithin = (in DndCollisionQuery query) =>
        {
            var found = new DndCollision[query.Droppables.Count];
            var count = 0;
            var pointer = query.PointerPosition;
            var corners = new Vector2[4];
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var rect = candidate.Rect;
                if (!(rect.yMin <= pointer.y && pointer.y <= rect.yMax
                    && rect.xMin <= pointer.x && pointer.x <= rect.xMax))
                {
                    continue;
                }
                Corners(rect, corners);
                var sum = 0.0;
                for (var c = 0; c < 4; c++)
                {
                    sum += Distance(pointer.x, pointer.y, corners[c].x, corners[c].y);
                }
                found[count++] = new DndCollision(candidate.Id, candidate.Data, RoundToFourDecimals(sum / 4));
            }
            return Rank(found, count, descending: false);
        };

        private static double IntersectionRatio(Rect entry, Rect target)
        {
            double left = Math.Max(target.xMin, entry.xMin);
            double top = Math.Max(target.yMin, entry.yMin);
            double right = Math.Min(target.xMax, entry.xMax);
            double bottom = Math.Min(target.yMax, entry.yMax);
            if (!(left < right && top < bottom))
            {
                return 0;
            }
            double targetArea = (double)target.width * target.height;
            double entryArea = (double)entry.width * entry.height;
            var intersectionArea = (right - left) * (bottom - top);
            return RoundToFourDecimals(intersectionArea / (targetArea + entryArea - intersectionArea));
        }

        private static void Corners(Rect rect, Vector2[] into)
        {
            into[0] = new Vector2(rect.xMin, rect.yMin);
            into[1] = new Vector2(rect.xMax, rect.yMin);
            into[2] = new Vector2(rect.xMin, rect.yMax);
            into[3] = new Vector2(rect.xMax, rect.yMax);
        }

        private static double Distance(double ax, double ay, double bx, double by)
        {
            var dx = ax - bx;
            var dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double RoundToFourDecimals(double value)
            => Math.Round(value, 4, MidpointRounding.AwayFromZero);

        // Insertion sort, so an equal pair stays in registration order.
        private static DndCollision[] Rank(DndCollision[] found, int count, bool descending)
        {
            if (count == 0)
            {
                return Array.Empty<DndCollision>();
            }
            for (var i = 1; i < count; i++)
            {
                var item = found[i];
                var j = i - 1;
                while (j >= 0 && (descending ? found[j].Value < item.Value : found[j].Value > item.Value))
                {
                    found[j + 1] = found[j];
                    j--;
                }
                found[j + 1] = item;
            }
            if (count == found.Length)
            {
                return found;
            }
            var ranked = new DndCollision[count];
            Array.Copy(found, ranked, count);
            return ranked;
        }
    }
}
