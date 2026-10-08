using System.Collections.Generic;

namespace Velvet
{
    // Finds where a paragraph may break across lines, for TextLineBreaker. An item is the text between two
    // opportunities; the gap between neighbouring items is the white space that separates them, or empty
    // where the break falls between two characters.
    //
    // This is a subset of UAX #14, the line breaking ICU gives Chromium. It covers white space (the space,
    // tab and every other white space but a no-break one), the places a break is allowed beside a CJK
    // character, with the basic kinsoku rule (no break before a closing mark or an iteration or
    // prolonged-sound mark, none after an opening mark), after a hyphen between two letters, and after a
    // zero width space. The rest of the standard is not read: quotation marks, the numeric and symbol
    // rules, emoji and Indic sequences, the CJK symbols outside the marks named above, and the combining
    // behaviour of the CJK classes. A break one of those rules would allow beyond the classes above is not
    // reported, and one it would prohibit can be. A rich-text tag, from a `<` that opens one to its `>`, is never broken inside.
    internal static class TextBreakOpportunities
    {
        private const char NewLine = '\n';
        private const char ZeroWidthSpace = '​';
        private const char Hyphen = '-';
        private const char TrueHyphen = '‐';

        // Fills starts and ends with the character range of every item. Returns false for a paragraph with
        // nothing but white space. The first item starts at 0, so white space leading the paragraph stays
        // on its first line; white space trailing it belongs to no item.
        public static bool Find(string paragraph, List<int> starts, List<int> ends)
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
                if (previous >= 0 && BreakAllowedBetween(before, previous, current))
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
            char.IsWhiteSpace(ch) && ch != NewLine && ch != ' ' && ch != ' ' && ch != ' ';

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
        public static bool BreakAllowedBetween(int before, int previous, int next)
        {
            if (previous == ZeroWidthSpace)
            {
                return true;
            }
            if (previous == Hyphen || previous == TrueHyphen)
            {
                return before >= 0 && IsLetter(before) && IsLetter(next);
            }
            if (IsOpening(previous) || IsNoBreakBefore(next))
            {
                return false;
            }
            return IsIdeographic(previous) || IsCjkClosing(previous) || IsIdeographic(next) || IsCjkOpening(next);
        }

        private static bool IsLetter(int codePoint) =>
            codePoint < 0x10000 ? char.IsLetter((char)codePoint) : char.IsLetter(char.ConvertFromUtf32(codePoint), 0);

        // Kana, hangul, ideographs and the other CJK scripts that break between any two of their characters.
        private static bool IsIdeographic(int cp) =>
            (cp >= 0x2E80 && cp <= 0x2FDF)
            || (cp >= 0x3040 && cp <= 0x30FF && !IsCjkClosing(cp))
            || (cp >= 0x3100 && cp <= 0x318F)
            || (cp >= 0x31A0 && cp <= 0x33FF)
            || (cp >= 0x3400 && cp <= 0x4DBF)
            || (cp >= 0x4E00 && cp <= 0x9FFF)
            || (cp >= 0xA000 && cp <= 0xA4CF)
            || (cp >= 0xAC00 && cp <= 0xD7AF)
            || (cp >= 0xF900 && cp <= 0xFAFF)
            || (cp >= 0x1B000 && cp <= 0x1B16F)
            || (cp >= 0x20000 && cp <= 0x3FFFD);

        // The marks a line may not begin with: closing brackets, the stops and commas of CJK text, the
        // prolonged sound mark, iteration marks and the middle dot, plus the ASCII closers.
        private static bool IsNoBreakBefore(int cp)
        {
            if (cp < 0x80)
            {
                return cp == ')' || cp == ']' || cp == '}' || cp == ',' || cp == '.' || cp == '!' || cp == '?'
                    || cp == ';' || cp == ':';
            }
            return IsCjkClosing(cp);
        }

        // The non-ASCII marks of IsNoBreakBefore, which also let a line break after them.
        private static bool IsCjkClosing(int cp)
        {
            switch (cp)
            {
                case 0x3001: case 0x3002: case 0x3005: case 0x3009: case 0x300B: case 0x300D: case 0x300F:
                case 0x3011: case 0x3015: case 0x3017: case 0x3019: case 0x301B: case 0x303B: case 0x309D:
                case 0x309E: case 0x30A0: case 0x30FB: case 0x30FC: case 0x30FD: case 0x30FE: case 0x203C:
                case 0x2047: case 0x2048: case 0x2049: case 0xFF01: case 0xFF09: case 0xFF0C: case 0xFF0E:
                case 0xFF1A: case 0xFF1B: case 0xFF1F: case 0xFF3D: case 0xFF5D: case 0xFF60: case 0xFF63:
                case 0xFF65:
                    return true;
                default:
                    return false;
            }
        }

        // The marks a line may not end with: opening brackets, CJK and ASCII.
        private static bool IsOpening(int cp)
        {
            if (cp < 0x80)
            {
                return cp == '(' || cp == '[' || cp == '{';
            }
            return IsCjkOpening(cp);
        }

        private static bool IsCjkOpening(int cp)
        {
            switch (cp)
            {
                case 0x3008: case 0x300A: case 0x300C: case 0x300E: case 0x3010: case 0x3014: case 0x3016:
                case 0x3018: case 0x301A: case 0x301D: case 0xFF08: case 0xFF3B: case 0xFF5B: case 0xFF5F:
                case 0xFF62:
                    return true;
                default:
                    return false;
            }
        }
    }
}
