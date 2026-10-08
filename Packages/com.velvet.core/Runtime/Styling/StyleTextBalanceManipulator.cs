using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Realises `text-wrap: balance` and `text-wrap: pretty` on a text leaf by writing line breaks into the
    // text it DISPLAYS, so the box keeps the width the cascade gives it. The text the leaf was given is
    // not changed: StyleTextEffectResolver builds the displayed string from the raw text it captured and
    // asks Break for the newlines, the way it applies text-transform. Which breaks come is TextLineBreaker's
    // choice; this class measures for it and turns its answer into a string.
    //
    // One per leaf whose resolved text-wrap-style is balance or pretty, wherever in the tree the class was
    // written — the style inherits as it does in CSS, so the resolver attaches it to every text leaf under
    // the element that carries the class. It is tracked in ReconcilerContext.TextBalanceManipulators and
    // removed on cleanup. Its events re-derive the leaf (StyleTextEffectResolver.ReapplyElement) when the
    // width the text is laid out in, or the font it is measured in, changes; Break answers from the last
    // result while neither the text nor those have.
    //
    // A paragraph is the text between newlines and a word is a run between spaces, so text with no spaces
    // (CJK) has nothing to break and is left to the engine, as is a paragraph holding any other white space.
    // The width broken against is the leaf's own, so an ancestor widening alone does not re-derive a leaf
    // whose width it leaves as it was.
    internal sealed class StyleTextBalanceManipulator : StyleTextItemManipulator
    {
        // Content room below this leaves nothing to redistribute, and keeps a division by it away.
        private const float MinBalanceableWidthPx = 1f;

        // The measured height of a line that wraps exceeds the one-line height by a whole line; this absorbs
        // the float rounding of the two measurements.
        private const float HeightEpsilonPx = 0.5f;

        // A paragraph wider than the line limit plus this many lines of the available width takes more lines
        // than TextLineBreaker optimizes, so its words are never measured. The spare line covers the room
        // the lines leave unused.
        private const int SpareLines = 1;

        private readonly ReconcilerContext _ctx;

        private TextWrapStyle _style;

        private bool _hasResult;
        private string _resultFor = string.Empty;
        private string _result = string.Empty;
        private TextWrapStyle _resultStyle;
        private float _resultWidth;
        private float _resultFontSize;
        private FontDefinition _resultFont;
        private float _resultSpacing;

        internal StyleTextBalanceManipulator(ReconcilerContext ctx, TextWrapStyle style)
        {
            _ctx = ctx;
            _style = style;
        }

        // The resolver calls this on every resolve; a change of style needs no invalidation, since the
        // style is part of what the last result is keyed on.
        internal void SetStyle(TextWrapStyle style)
        {
            _style = style;
        }

        // Whether the leaf has a width to break in. False before its first layout, when the resolver leaves
        // the text as it is.
        internal bool CanBreak =>
            target is TextElement textElement && textElement.contentRect.width >= MinBalanceableWidthPx;

        protected override void Derive(TextElement textElement, VisualElement parent)
        {
            var signature = ComputeSignature(textElement);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }
            _lastSignature = signature;
            _hasSignature = true;
            StyleTextEffectResolver.ReapplyElement(_ctx, textElement);
        }

        // Nothing was written outside the displayed text, which the resolver rewrites on its own.
        protected override void Clear()
        {
            _hasResult = false;
        }

        // The terms are what Break keys its result on besides the text, which reaches the resolver on its
        // own path.
        private static int ComputeSignature(TextElement textElement)
        {
            var style = textElement.resolvedStyle;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(textElement.contentRect.width * 4f);
                hash = hash * 31 + style.fontSize.GetHashCode();
                hash = hash * 31 + (style.letterSpacing + style.wordSpacing).GetHashCode();
                hash = hash * 31 + style.unityFontDefinition.GetHashCode();
                hash = hash * 31 + (int)style.unityFontStyleAndWeight;
                return hash;
            }
        }

        // The text with a newline in place of the space before every word that begins a line. text is what the
        // leaf displays before decoration and line height wrap it.
        internal string Break(string text)
        {
            var textElement = (TextElement)target;
            var width = textElement.contentRect.width;
            var resolved = textElement.resolvedStyle;
            var fontSize = resolved.fontSize;
            var font = resolved.unityFontDefinition;
            var spacing = resolved.letterSpacing + resolved.wordSpacing;
            if (_hasResult && _resultStyle == _style && _resultWidth == width && _resultFontSize == fontSize
                && _resultSpacing == spacing && _resultFont.Equals(font) && _resultFor == text)
            {
                return _result;
            }

            var broken = BreakParagraphs(textElement, text, _style, width, fontSize);
            if (broken == null)
            {
                // The font has not resolved, so nothing was measured. Not recorded: the same inputs are a
                // different question once it has.
                return text;
            }
            _hasResult = true;
            _resultStyle = _style;
            _resultWidth = width;
            _resultFontSize = fontSize;
            _resultSpacing = spacing;
            _resultFont = font;
            _resultFor = text;
            _result = broken;
            return broken;
        }

        private static string? BreakParagraphs(
            TextElement textElement, string text, TextWrapStyle style, float width, float fontSize)
        {
            var spaceAdvance = MeasureSpaceAdvance(textElement);
            if (float.IsNaN(spaceAdvance))
            {
                return null;
            }
            var builder = new StringBuilder(text.Length + 4);
            var start = 0;
            while (start <= text.Length)
            {
                var end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    end = text.Length;
                }
                var paragraph = text.Substring(start, end - start);
                builder.Append(BreakParagraph(textElement, paragraph, style, width, fontSize, spaceAdvance));
                if (end < text.Length)
                {
                    builder.Append('\n');
                }
                start = end + 1;
            }
            return builder.ToString();
        }

        private static string BreakParagraph(
            TextElement textElement, string paragraph, TextWrapStyle style, float width, float fontSize,
            float spaceAdvance)
        {
            if (!TryFindWords(paragraph, out var starts, out var ends))
            {
                return paragraph;
            }
            var maxLines = TextLineBreaker.MaxLines(style);
            var whole = Measure(textElement, paragraph);
            if (float.IsNaN(whole) || whole > (maxLines + SpareLines) * width)
            {
                return paragraph;
            }

            var lineStart = new float[starts.Count];
            var wordEnd = new float[starts.Count];
            for (var i = 0; i < starts.Count; i++)
            {
                wordEnd[i] = Measure(textElement, paragraph.Substring(0, ends[i]));
                if (float.IsNaN(wordEnd[i]))
                {
                    return paragraph;
                }
                lineStart[i] = i == 0 ? 0f : wordEnd[i - 1] + (starts[i] - ends[i - 1]) * spaceAdvance;
            }

            var breaks = TextLineBreaker.Plan(
                lineStart, wordEnd, width, style, 4f * width * fontSize,
                (first, end) => FitsOneLine(textElement, LineText(paragraph, starts, ends, first, end), width));
            return breaks == null ? paragraph : InsertBreaks(paragraph, starts, ends, breaks);
        }

        // Words first..end-1 as one line; the first line keeps the white space before its first word.
        private static string LineText(string paragraph, List<int> starts, List<int> ends, int first, int end)
        {
            var from = first == 0 ? 0 : starts[first];
            return paragraph.Substring(from, ends[end - 1] - from);
        }

        private static string InsertBreaks(string paragraph, List<int> starts, List<int> ends, int[] breaks)
        {
            var builder = new StringBuilder(paragraph.Length);
            var copied = 0;
            foreach (var word in breaks)
            {
                builder.Append(paragraph, copied, ends[word - 1] - copied);
                builder.Append('\n');
                copied = starts[word];
            }
            builder.Append(paragraph, copied, paragraph.Length - copied);
            return builder.ToString();
        }

        // False when the paragraph holds a white space other than the space, which the offsets between words
        // would misprice.
        private static bool TryFindWords(string paragraph, out List<int> starts, out List<int> ends)
        {
            starts = new List<int>();
            ends = new List<int>();
            var inWord = false;
            for (var i = 0; i < paragraph.Length; i++)
            {
                var ch = paragraph[i];
                if (ch != ' ' && char.IsWhiteSpace(ch))
                {
                    return false;
                }
                if (ch == ' ' && inWord)
                {
                    ends.Add(i);
                    inWord = false;
                }
                else if (ch != ' ' && !inWord)
                {
                    starts.Add(i);
                    inWord = true;
                }
            }
            if (inWord)
            {
                ends.Add(paragraph.Length);
            }
            return starts.Count >= 1;
        }

        // The advance one space adds, from a run of nine words against the same nine with no spaces between,
        // so the two measurements' rounding to the pixel grid is spread over eight spaces.
        private static float MeasureSpaceAdvance(TextElement textElement) =>
            (Measure(textElement, "x x x x x x x x x") - Measure(textElement, "xxxxxxxxx")) / 8f;

        private static float Measure(TextElement textElement, string text) =>
            textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        // The engine's own answer, for a line too near the width for the arithmetic: whether it lays the line
        // out in one line's height.
        private static bool FitsOneLine(TextElement textElement, string line, float width)
        {
            var single = textElement.MeasureTextSize(
                line, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).y;
            var wrapped = textElement.MeasureTextSize(
                line, width, VisualElement.MeasureMode.Exactly,
                float.NaN, VisualElement.MeasureMode.Undefined).y;
            return wrapped <= single + HeightEpsilonPx;
        }
    }
}
