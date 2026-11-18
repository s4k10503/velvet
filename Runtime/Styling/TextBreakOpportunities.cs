using System.Collections.Generic;

namespace Velvet
{
    // Finds where a paragraph may break across lines, for every consumer that needs line breaking: the
    // balance and pretty breaker (Find) and a min-content width (CollectRuns). An item is the text between
    // two opportunities; the gap between neighbouring items is the white space that separates them, or empty
    // where the break falls between two characters.
    //
    // This is a subset of UAX #14, the line breaking ICU gives Chromium. It covers white space (a tab and
    // every white space but a no-break one), breaks beside a CJK character with the basic kinsoku rule (no
    // break before a closing mark, a stop or comma, a percent sign, an ellipsis, the prolonged sound mark or
    // an iteration mark; none after an opening mark), after a hyphen-minus that follows a letter or digit and
    // does not precede a digit, after the hyphens and dashes of class BA (U+2010, U+2012, U+2013), after a
    // soft hyphen where the caller asks for it, and after a zero width space, which is a break opportunity
    // and not a space: it stays in its item. The rest of the standard is not read: quotation marks, the
    // numeric and symbol rules, the em dash, emoji and Indic sequences, halfwidth katakana, CJK symbols
    // outside the marks named above and the combining behaviour of the CJK classes. A break one of those
    // rules would allow beyond the classes above is not reported, and one it would prohibit can be. A
    // rich-text tag, from a `<` that opens one to its `>`, is never broken inside.
    internal static class TextBreakOpportunities
    {
        private const char NewLine = '\n';
        private const char ZeroWidthSpace = '\u200B';
        private const char SoftHyphen = '\u00AD';
        private const char HyphenMinus = '-';

        // Fills starts and ends with the character range of every item. Returns false for a paragraph with
        // nothing but white space. The first item starts at 0, so white space leading the paragraph stays
        // on its first line; white space trailing it belongs to no item.
        public static bool Find(string paragraph, List<int> starts, List<int> ends, bool softHyphens = false)
        {
            starts.Clear();
            ends.Clear();
            var index = 0;
            while (index < paragraph.Length && IsBreakSpace(paragraph[index]))
            {
                index++;
            }
            if (index == paragraph.Length)
            {
                return false;
            }
            starts.Add(0);
            var before = -1;
            var previous = -1;
            while (index < paragraph.Length)
            {
                if (IsBreakSpace(paragraph[index]))
                {
                    index = CloseAtSpace(paragraph, index, starts, ends);
                    before = -1;
                    previous = -1;
                    continue;
                }
                var tagEnd = TagEnd(paragraph, index);
                if (tagEnd > index)
                {
                    index = tagEnd;
                    continue;
                }
                var length = char.IsSurrogatePair(paragraph, index) ? 2 : 1;
                var current = length == 2 ? char.ConvertToUtf32(paragraph, index) : paragraph[index];
                if (previous >= 0 && BreakAllowedBetween(before, previous, current, softHyphens))
                {
                    ends.Add(index);
                    starts.Add(index);
                }
                before = previous;
                previous = current;
                index += length;
            }
            if (ends.Count < starts.Count)
            {
                ends.Add(paragraph.Length);
            }
            return true;
        }

        // Ends the current item at the white space run starting at index and opens the next after it, unless
        // the run closes the paragraph. Returns where scanning resumes.
        private static int CloseAtSpace(string paragraph, int index, List<int> starts, List<int> ends)
        {
            ends.Add(index);
            var gapEnd = index;
            while (gapEnd < paragraph.Length && IsBreakSpace(paragraph[gapEnd]))
            {
                gapEnd++;
            }
            if (gapEnd < paragraph.Length)
            {
                starts.Add(gapEnd);
            }
            return gapEnd;
        }

        // White space a line may break at: every white space but a line feed, which splits paragraphs
        // before this runs, and the no-break spaces.
        public static bool IsBreakSpace(char ch) =>
            char.IsWhiteSpace(ch) && ch != NewLine && ch != '\u00A0' && ch != '\u2007' && ch != '\u202F';

        // Where a rich-text tag opening at index ends, or index when none does. A tag opens with `<` and a
        // letter or a slash, so `a < b` is not one, and holds no second `<`.
        private static int TagEnd(string paragraph, int index)
        {
            if (paragraph[index] != '<' || index + 1 >= paragraph.Length
                || !(char.IsLetter(paragraph[index + 1]) || paragraph[index + 1] == '/'))
            {
                return index;
            }
            for (var i = index + 1; i < paragraph.Length; i++)
            {
                if (paragraph[i] == '>')
                {
                    return i + 1;
                }
                if (paragraph[i] == '<')
                {
                    return index;
                }
            }
            return index;
        }

        // Whether a line may break between two adjacent characters that are not white space. before is the
        // character ahead of previous, or -1.
        public static bool BreakAllowedBetween(int before, int previous, int next, bool softHyphens = false)
        {
            if (next == ZeroWidthSpace)
            {
                return false;
            }
            if (previous == ZeroWidthSpace)
            {
                return true;
            }
            if (previous == SoftHyphen)
            {
                return softHyphens;
            }
            if (previous == HyphenMinus)
            {
                return before >= 0 && IsLetterOrDigit(before) && !IsDigit(next) && !IsNoBreakBefore(next);
            }
            if (IsHyphenOfClassBa(previous))
            {
                return !IsNoBreakBefore(next);
            }
            if (IsOpening(previous) || IsNoBreakBefore(next))
            {
                return false;
            }
            return IsIdeographic(previous) || IsCjkClosing(previous) || IsIdeographic(next) || IsCjkOpening(next);
        }

        // The runs of text a line may not be broken inside, in order, for a min-content width: each item
        // Find reports, with the soft hyphens it honours.
        public static void CollectRuns(string text, List<string> runs)
        {
            var starts = new List<int>();
            var ends = new List<int>();
            if (!Find(text, starts, ends, softHyphens: true))
            {
                return;
            }
            for (var i = 0; i < starts.Count; i++)
            {
                runs.Add(text.Substring(starts[i], ends[i] - starts[i]));
            }
        }

        private static bool IsHyphenOfClassBa(int cp) => cp == 0x2010 || cp == 0x2012 || cp == 0x2013;

        private static bool IsDigit(int cp) => cp < 0x10000 && char.IsDigit((char)cp);

        private static bool IsLetterOrDigit(int cp) =>
            cp < 0x10000 ? char.IsLetterOrDigit((char)cp) : char.IsLetterOrDigit(char.ConvertFromUtf32(cp), 0);

        private static bool IsLetter(int codePoint) =>
            codePoint < 0x10000 ? char.IsLetter((char)codePoint) : char.IsLetter(char.ConvertFromUtf32(codePoint), 0);

        // Kana, hangul, ideographs and the other CJK scripts that break between any two of their characters.
        // The kana block holds marks a line may not begin with, which IsNoBreakBefore takes out.
        private static readonly (int First, int Last)[] IdeographicRanges =
        {
            (0x2E80, 0x303F), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
            (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE30, 0xFE4F), (0xFF01, 0xFF60), (0xFFE0, 0xFFE6),
            (0x1B000, 0x1B16F), (0x20000, 0x3FFFD),
        };

        private static bool IsIdeographic(int cp)
        {
            foreach (var (first, last) in IdeographicRanges)
            {
                if (cp >= first && cp <= last)
                {
                    return !IsCjkClosing(cp);
                }
            }
            return false;
        }

        // The marks a line may not begin with: closing brackets, the stops and commas of CJK text, the
        // prolonged sound mark, iteration marks and the middle dot, plus the ASCII closers.
        private static bool IsNoBreakBefore(int cp)
        {
            if (cp < 0x80)
            {
                return cp == ')' || cp == ']' || cp == '}' || cp == ',' || cp == '.' || cp == '!' || cp == '?'
                    || cp == ';' || cp == ':' || cp == '%';
            }
            return IsCjkClosing(cp);
        }

        // The non-ASCII marks of IsNoBreakBefore, which also let a line break after them.
        private static readonly HashSet<int> CjkClosing = new()
        {
            0x3001, 0x3002, 0x3005, 0x3009, 0x300B, 0x300D, 0x300F, 0x3011, 0x3015, 0x3017, 0x3019, 0x301B,
            0x303B, 0x309D, 0x309E, 0x30A0, 0x30FB, 0x30FC, 0x30FD, 0x30FE, 0x203C, 0x2047, 0x2048, 0x2049,
            0xFF01, 0xFF05, 0xFF09, 0xFF0C, 0xFF0E, 0xFF1A, 0xFF1B, 0xFF1F, 0xFF3D, 0xFF5D, 0xFF60, 0xFF63, 0xFF65,
            0x2025, 0x2026, 0x301C, 0x301E, 0x301F,
        };

        private static readonly HashSet<int> CjkOpening = new()
        {
            0x3008, 0x300A, 0x300C, 0x300E, 0x3010, 0x3014, 0x3016, 0x3018, 0x301A, 0x301D, 0xFF08, 0xFF3B,
            0xFF5B, 0xFF5F, 0xFF62,
        };

        private static bool IsCjkClosing(int cp) => CjkClosing.Contains(cp);

        // The marks a line may not end with: opening brackets, CJK and ASCII.
        private static bool IsOpening(int cp)
        {
            if (cp < 0x80)
            {
                return cp == '(' || cp == '[' || cp == '{';
            }
            return IsCjkOpening(cp);
        }

        private static bool IsCjkOpening(int cp) => CjkOpening.Contains(cp);
    }
}
