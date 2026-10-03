using System;
using System.Globalization;

namespace Velvet
{
    // Tailwind's order between two utilities that write the same properties: by character code, a run of
    // digits in both compared as a number first and as text second, and a candidate that is a prefix of
    // the other first. The later of two such utilities at one priority wins, since Tailwind emits its rule
    // later. StyleCandidateOrderTests pins the order against Tailwind's output.
    internal static class StyleCandidateOrder
    {
        internal static int Compare(string a, string b)
        {
            var shorter = Math.Min(a.Length, b.Length);
            for (var i = 0; i < shorter; i++)
            {
                if (IsDigit(a[i]) && IsDigit(b[i]))
                {
                    var runA = DigitRun(a, i);
                    var runB = DigitRun(b, i);
                    var byValue = Number(runA).CompareTo(Number(runB));
                    if (byValue != 0)
                    {
                        return byValue;
                    }
                    var byText = runA.SequenceCompareTo(runB);
                    if (byText != 0)
                    {
                        return byText;
                    }
                    continue;
                }
                if (a[i] != b[i])
                {
                    return a[i] - b[i];
                }
            }
            return a.Length - b.Length;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static ReadOnlySpan<char> DigitRun(string s, int start)
        {
            var end = start + 1;
            while (end < s.Length && IsDigit(s[end]))
            {
                end++;
            }
            return s.AsSpan(start, end - start);
        }

        private static double Number(ReadOnlySpan<char> digits)
            => double.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
