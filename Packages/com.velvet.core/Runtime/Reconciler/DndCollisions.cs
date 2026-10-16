#nullable enable
using System;
using BigInteger = System.Numerics.BigInteger;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The built-in collision strategies: <see cref="RectIntersection"/>, <see cref="ClosestCenter"/>,
    /// <see cref="ClosestCorners"/> and <see cref="PointerWithin"/>. Each is a pure function over a
    /// <see cref="DndCollisionQuery"/> (no panel access) returning every colliding droppable ranked best
    /// first, with the same ranking value, rounding and tie order as the algorithm of the same name in
    /// <c>@dnd-kit/core</c> (<c>packages/core/src/utilities/algorithms</c>), computed in double precision
    /// from the rects' float edges as it computes in JavaScript numbers. The context takes the first
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
            DndCollision[]? found = null;
            var count = 0;
            var active = query.ActiveRect;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var ratio = IntersectionRatio(candidate.Rect, active);
                if (ratio > 0)
                {
                    found ??= new DndCollision[query.Droppables.Count];
                    found[count++] = new DndCollision(candidate.Id, candidate.Data, ratio);
                }
            }
            return Rank(found, count, descending: true);
        };

        /// <summary>Every droppable, ranked by the distance from its center to the active rect's center,
        /// nearest first — the forgiving strategy sortable lists and coarse grids want. Equal distances
        /// keep registration order. A rect with a NaN edge is left out.</summary>
        public static readonly DndCollisionDetection ClosestCenter = (in DndCollisionQuery query) =>
        {
            DndCollision[]? found = null;
            var count = 0;
            var active = query.ActiveRect;
            var activeX = CenterX(active);
            var activeY = CenterY(active);
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var distance = Distance(CenterX(candidate.Rect), CenterY(candidate.Rect), activeX, activeY);
                if (double.IsNaN(distance))
                {
                    continue;
                }
                found ??= new DndCollision[query.Droppables.Count];
                found[count++] = new DndCollision(candidate.Id, candidate.Data, distance);
            }
            return Rank(found, count, descending: false);
        };

        /// <summary>Every droppable, ranked by the mean distance between its four corners and the active
        /// rect's corresponding four (top-left, top-right, bottom-left, bottom-right), rounded to four
        /// decimals, nearest first. Equal values keep registration order. A rect with a NaN edge is left
        /// out.</summary>
        public static readonly DndCollisionDetection ClosestCorners = (in DndCollisionQuery query) =>
        {
            DndCollision[]? found = null;
            var count = 0;
            var active = query.ActiveRect;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var sum = 0.0;
                for (var c = 0; c < 4; c++)
                {
                    Corner(candidate.Rect, c, out var x, out var y);
                    Corner(active, c, out var activeX, out var activeY);
                    sum += Distance(x, y, activeX, activeY);
                }
                if (double.IsNaN(sum))
                {
                    continue;
                }
                found ??= new DndCollision[query.Droppables.Count];
                found[count++] = new DndCollision(candidate.Id, candidate.Data, RoundToFourDecimals(sum / 4));
            }
            return Rank(found, count, descending: false);
        };

        /// <summary>Droppables whose rect contains the pointer, edges included, ranked by the mean
        /// distance from the pointer to the rect's four corners, rounded to four decimals, smallest
        /// first. Equal values keep registration order.</summary>
        public static readonly DndCollisionDetection PointerWithin = (in DndCollisionQuery query) =>
        {
            DndCollision[]? found = null;
            var count = 0;
            var pointer = query.PointerPosition;
            for (var i = 0; i < query.Droppables.Count; i++)
            {
                var candidate = query.Droppables[i];
                var rect = candidate.Rect;
                if (!(rect.y <= pointer.y && pointer.y <= Bottom(rect)
                    && rect.x <= pointer.x && pointer.x <= Right(rect)))
                {
                    continue;
                }
                var sum = 0.0;
                for (var c = 0; c < 4; c++)
                {
                    Corner(rect, c, out var x, out var y);
                    sum += Distance(pointer.x, pointer.y, x, y);
                }
                found ??= new DndCollision[query.Droppables.Count];
                found[count++] = new DndCollision(candidate.Id, candidate.Data, RoundToFourDecimals(sum / 4));
            }
            return Rank(found, count, descending: false);
        };

        // Edges, centers and corners are summed in double from the float fields, as dnd-kit sums JavaScript
        // numbers; Rect.xMax, Rect.center and Vector2 sum in float and can land a ranking value on the
        // other side of a four-decimal rounding step.
        private static double Right(Rect rect) => (double)rect.x + rect.width;

        private static double Bottom(Rect rect) => (double)rect.y + rect.height;

        private static double CenterX(Rect rect) => rect.x + rect.width * 0.5;

        private static double CenterY(Rect rect) => rect.y + rect.height * 0.5;

        // Corners are numbered top-left, top-right, bottom-left, bottom-right.
        private static void Corner(Rect rect, int index, out double x, out double y)
        {
            // MUTANT_SURVIVES(equivalent): both rects of a pair map an index through this one function, so
            // flipping the choice permutes their corners alike and pairs the same ones.
            x = (index & 1) == 0 ? rect.x : Right(rect);
            // MUTANT_SURVIVES(equivalent): as the line above, for the vertical choice.
            y = (index & 2) == 0 ? rect.y : Bottom(rect);
        }

        private static double IntersectionRatio(Rect entry, Rect target)
        {
            var width = Math.Min(Right(target), Right(entry)) - Math.Max(target.x, entry.x);
            var height = Math.Min(Bottom(target), Bottom(entry)) - Math.Max(target.y, entry.y);
            var intersectionArea = Math.Max(0, width) * Math.Max(0, height);
            double targetArea = (double)target.width * target.height;
            double entryArea = (double)entry.width * entry.height;
            return RoundToFourDecimals(intersectionArea / (targetArea + entryArea - intersectionArea));
        }

        private static double Distance(double ax, double ay, double bx, double by)
        {
            var dx = ax - bx;
            var dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // JavaScript's toFixed(4) on the exact binary value, halves going up — not Math.Round, which is not
        // exact-decimal. It agrees with toFixed(4) below 2^53 / 10^4 (about 9.007e11), well above any panel
        // distance or ratio; the two rounding cases in DndInteractionTests pin it.
        private static double RoundToFourDecimals(double value)
        {
            // MUTANT_SURVIVES(equivalent): zero of either sign comes back from the arithmetic below as a
            // zero that compares equal to it.
            if (!(value > 0))
            {
                return value;
            }
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0)
            {
                // MUTANT_SURVIVES(equivalent): a subnormal is so small that 10000 times it rounds to 0
                // whichever exponent it is read with.
                exponent = 1;
            }
            else
            {
                mantissa |= 1L << 52;
            }
            exponent -= 1075;
            var scaled = new BigInteger(mantissa) * 10000;
            BigInteger rounded;
            // MUTANT_SURVIVES(equivalent): at an exponent of exactly 0 the shifting branch's
            // ((scaled << 1) + 1) >> 1 is scaled, which is what the first branch yields.
            if (exponent >= 0)
            {
                rounded = scaled << exponent;
            }
            else
            {
                var shift = -exponent;
                rounded = ((scaled << 1) + (BigInteger.One << shift)) >> (shift + 1);
            }
            return (double)rounded / 10000.0;
        }

        // Insertion sort, so an equal pair stays in registration order.
        private static DndCollision[] Rank(DndCollision[]? found, int count, bool descending)
        {
            if (found == null)
            {
                return Array.Empty<DndCollision>();
            }
            for (var i = 1; i < count; i++)
            {
                var item = found[i];
                int j;
                for (j = i - 1; j >= 0 && (descending ? found[j].Value < item.Value : found[j].Value > item.Value); j--)
                {
                    found[j + 1] = found[j];
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
