using System;
using System.Collections.Generic;

namespace Velvet
{
    // Chooses where a paragraph of words breaks for `text-wrap: balance` and `text-wrap: pretty`, over
    // positions the caller measured. It is Chromium's ScoreLineBreaker (score_line_breaker.cc) with the
    // same cost: the squared slack of every line, a penalty on breaking before the last word, and a
    // penalty per line. Its other rules are Chromium's too: nothing is optimized below four words, a
    // paragraph of more than kMaxLinesForBalance lines (six) or kMaxLinesForOptimal lines (four, for
    // pretty) is left as it breaks, and a result with another number of lines than the greedy one is
    // dropped. Pretty takes the first of Chromium's two triggers, a last line that is one word narrower
    // than a third of the line; the second concerns hyphenated lines, which Velvet does not produce.
    //
    // Pure over its arrays, so the choice is tested without a text engine. The caller owns what a word is
    // and how wide anything measures; see StyleTextBalanceManipulator.
    internal static class TextLineBreaker
    {
        private const int MinWords = 4;
        private const int MaxLinesForBalance = 6;
        private const int MaxLinesForPretty = 4;

        // Chromium's kOrphansPenalty, charged to the break before the last word.
        private const float OrphanPenalty = 10000f;

        // Chromium's kLastLinePenaltyMultiplier, the pretty last line's share of the orphan penalty.
        private const float LastLinePenaltyMultiplier = 4f;

        // Chromium's kShortLineDenominator.
        private const float ShortLineDenominator = 3f;

        // A line this close to the available width is settled by the callback rather than by the
        // arithmetic, which carries the rounding of the measurements it was built from.
        private const float BorderlineTolerancePx = 3f;

        public static int MaxLines(TextWrapStyle style) =>
            style == TextWrapStyle.Balance ? MaxLinesForBalance : MaxLinesForPretty;

        // lineStart[i] is where a line beginning with word i starts, wordEnd[i] where word i ends, both from
        // the start of the paragraph. fitsExactly answers whether words first..end-1 fit one line, and is
        // asked only for a line within the tolerance of the width; without it the arithmetic decides.
        // Returns the indices of the words that begin every line after the first, or null to leave the
        // paragraph as the engine breaks it.
        public static int[]? Plan(
            float[] lineStart, float[] wordEnd, float available, TextWrapStyle style, float linePenalty,
            Func<int, int, bool>? fitsExactly)
        {
            if (style == TextWrapStyle.None || wordEnd.Length < MinWords || lineStart.Length != wordEnd.Length)
            {
                return null;
            }
            var greedy = GreedyStarts(lineStart, wordEnd, available, fitsExactly);
            if (greedy == null || greedy.Count < 2 || greedy.Count > MaxLines(style))
            {
                return null;
            }
            var balanced = style == TextWrapStyle.Balance;
            if (!balanced && !HasShortLastWord(lineStart, wordEnd, greedy[greedy.Count - 1], available))
            {
                return null;
            }
            var chosen = Optimize(lineStart, wordEnd, available, balanced, linePenalty, fitsExactly);
            if (chosen == null || chosen.Count != greedy.Count || SameStarts(chosen, greedy))
            {
                return null;
            }
            return chosen.GetRange(1, chosen.Count - 1).ToArray();
        }

        private static bool Fits(
            float[] lineStart, float[] wordEnd, int first, int end, float available, Func<int, int, bool>? fitsExactly)
        {
            var width = wordEnd[end - 1] - lineStart[first];
            var tolerance = fitsExactly == null ? 0f : BorderlineTolerancePx;
            if (width <= available - tolerance)
            {
                return true;
            }
            if (width > available + tolerance)
            {
                return false;
            }
            return fitsExactly!(first, end);
        }

        // The first word of every line when each line takes as many words as fit; null when one word alone
        // does not fit, which only the engine's own mid-word break can lay out.
        private static List<int>? GreedyStarts(
            float[] lineStart, float[] wordEnd, float available, Func<int, int, bool>? fitsExactly)
        {
            var starts = new List<int>();
            var first = 0;
            while (first < wordEnd.Length)
            {
                if (!Fits(lineStart, wordEnd, first, first + 1, available, fitsExactly))
                {
                    return null;
                }
                var end = first + 1;
                while (end < wordEnd.Length && Fits(lineStart, wordEnd, first, end + 1, available, fitsExactly))
                {
                    end++;
                }
                starts.Add(first);
                first = end;
            }
            return starts;
        }

        private static bool HasShortLastWord(float[] lineStart, float[] wordEnd, int lastLineFirst, float available) =>
            lastLineFirst == wordEnd.Length - 1
            && wordEnd[lastLineFirst] - lineStart[lastLineFirst] < available / ShortLineDenominator;

        private static float BreakPenalty(int beforeWord, int wordCount) =>
            beforeWord == wordCount - 1 ? OrphanPenalty : 0f;

        private static float LineScore(
            float[] lineStart, float[] wordEnd, int first, int end, float available, bool balanced)
        {
            if (end == wordEnd.Length && !balanced)
            {
                return LastLinePenaltyMultiplier * BreakPenalty(first, wordEnd.Length);
            }
            var slack = Math.Max(0f, available - (wordEnd[end - 1] - lineStart[first]));
            return slack * slack;
        }

        // The cheapest division into lines, as the first word of each; null when none is reachable.
        private static List<int>? Optimize(
            float[] lineStart, float[] wordEnd, float available, bool balanced, float linePenalty,
            Func<int, int, bool>? fitsExactly)
        {
            var count = wordEnd.Length;
            var score = new float[count + 1];
            var previous = new int[count + 1];
            for (var i = 1; i <= count; i++)
            {
                score[i] = float.PositiveInfinity;
            }
            for (var end = 1; end <= count; end++)
            {
                var best = float.PositiveInfinity;
                var bestFirst = 0;
                for (var first = end - 1; first >= 0; first--)
                {
                    if (!Fits(lineStart, wordEnd, first, end, available, fitsExactly))
                    {
                        break;
                    }
                    var total = score[first] + LineScore(lineStart, wordEnd, first, end, available, balanced);
                    if (total < best)
                    {
                        best = total;
                        bestFirst = first;
                    }
                }
                score[end] = best + BreakPenalty(end, count) + linePenalty;
                previous[end] = bestFirst;
            }
            if (float.IsInfinity(score[count]))
            {
                return null;
            }
            var starts = new List<int>();
            for (var end = count; end > 0; end = previous[end])
            {
                starts.Add(previous[end]);
            }
            starts.Reverse();
            return starts;
        }

        private static bool SameStarts(List<int> left, List<int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (var i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
