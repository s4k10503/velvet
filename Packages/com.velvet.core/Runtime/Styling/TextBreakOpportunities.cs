using System.Collections.Generic;

namespace Velvet
{
    // Splits text into the runs a line may not be broken inside, for the min-content width of a text item.
    // Follows the part of Unicode line breaking (UAX #14) that decides the common cases: a break after a
    // breaking space, after a hyphen between letters, and between ideographs, kana and Hangul syllables
    // (except where punctuation forbids it). It is not the full algorithm: a class it does not model is
    // left unbroken.
    internal static class TextBreakOpportunities
    {
        // Breaking spaces. U+00A0, U+2007, U+202F, U+2060 and U+FEFF keep their neighbours together.
        private static bool IsBreakingSpace(int c)
            => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f'
                || c == 0x1680 || (c >= 0x2000 && c <= 0x200B && c != 0x2007)
                || c == 0x205F || c == 0x3000;

        private const string NoBreakBefore = ",.:;!?)]}%、。，．）］｝〕〉》」』】〗〙〟！？：；・ー々ゝゞヽヾ…‥〜";
        private const string NoBreakAfter = "([{（［｛〔〈《「『【〖〘〝";

        // Characters that break against any neighbour (UAX #14 class ID, minus the punctuation sets above).
        private static readonly (int First, int Last)[] IdeographicRanges =
        {
            (0x2E80, 0x303F), (0x3041, 0x33FF), (0x3400, 0x4DBF), (0x4E00, 0x9FFF), (0xA000, 0xA4CF),
            (0xAC00, 0xD7A3), (0xF900, 0xFAFF), (0xFE30, 0xFE4F), (0xFF01, 0xFF60), (0xFFE0, 0xFFE6),
            (0x20000, 0x3FFFD),
        };

        private static bool IsIdeographic(int cp)
        {
            foreach (var (first, last) in IdeographicRanges)
            {
                if (cp >= first && cp <= last)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsHyphen(int cp) => cp == '-' || cp == 0x2010 || cp == 0x2013;

        // Appends each unbreakable run of text, in order. Breaking spaces separate runs and belong to none.
        public static void CollectRuns(string text, List<string> runs)
        {
            var start = 0;
            var i = 0;
            while (i < text.Length)
            {
                var cp = CodePointAt(text, i);
                var width = cp > 0xFFFF ? 2 : 1;
                if (IsBreakingSpace(cp))
                {
                    Flush(text, start, i, runs);
                    start = i + width;
                }
                else if (i + width < text.Length && BreaksAfter(text, i, cp, CodePointAt(text, i + width)))
                {
                    Flush(text, start, i + width, runs);
                    start = i + width;
                }
                i += width;
            }
            Flush(text, start, text.Length, runs);
        }

        private static int CodePointAt(string text, int index)
            => char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : text[index];

        private static bool BreaksAfter(string text, int index, int cp, int next)
        {
            if (IsBreakingSpace(next))
            {
                return false;
            }
            if (cp == 0x00AD)
            {
                return true;
            }
            if (IsHyphen(cp))
            {
                return index > 0 && char.IsLetterOrDigit(text[index - 1]) && next < 0x10000 && char.IsLetter((char)next);
            }
            if (!IsIdeographic(cp) && !IsIdeographic(next))
            {
                return false;
            }
            return !(next < 0x10000 && NoBreakBefore.IndexOf((char)next) >= 0)
                && !(cp < 0x10000 && NoBreakAfter.IndexOf((char)cp) >= 0);
        }

        private static void Flush(string text, int start, int end, List<string> runs)
        {
            if (end > start)
            {
                runs.Add(text.Substring(start, end - start));
            }
        }
    }
}
