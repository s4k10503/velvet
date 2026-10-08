using System;
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
    // choice and where a line may break is TextBreakOpportunities'; this class measures for them and turns
    // their answer into a string.
    //
    // One per leaf whose resolved text-wrap-style is balance or pretty, wherever in the tree the class was
    // written — the style inherits as it does in CSS, so the resolver attaches it to every text leaf under
    // the element that carries the class. It is tracked in ReconcilerContext.TextBalanceManipulators and
    // removed on cleanup. Its events re-derive the leaf (StyleTextEffectResolver.ReapplyElement) when the
    // width the text is laid out in, or the font it is measured in, changes; Break answers from the last
    // result while neither the text nor those have.
    //
    // The width broken against is the leaf's own. A leaf whose box UI Toolkit sizes from its text takes the
    // width of its longest displayed line, so widening an ancestor moves nothing on it; Derive reads the
    // widening off the parent and breaks once against the room the leaf would get unbroken (see
    // _widthOverride), then returns to the leaf's own width at its next derive.
    internal sealed class StyleTextBalanceManipulator : StyleTextItemManipulator
    {
        // Content room below this leaves nothing to redistribute, and keeps a division by it away.
        private const float MinBalanceableWidthPx = 1f;

        // The measured height of a line that wraps exceeds the one-line height by a whole line; this absorbs
        // the float rounding of the two measurements.
        private const float HeightEpsilonPx = 0.5f;

        // A paragraph wider than the line limit plus this many lines of the available width takes more lines
        // than TextLineBreaker optimizes, so its items are never measured. The spare line covers the room
        // the lines leave unused.
        private const int SpareLines = 1;

        // A parent has to widen by more than this for the leaf to be re-broken against the room it gained,
        // so the pixel-grid rounding of a layout pass is not a widening.
        private const float ParentGrowthHysteresisPx = 2f;

        // How far a leaf's box may differ from the width of the lines it displays and still be read as
        // sized by them. Measured text is rounded up to the pixel grid.
        private const float SizedByTextTolerancePx = 1.5f;

        private readonly ReconcilerContext _ctx;

        private TextWrapStyle _style;

        // The room the parent last offered the leaf's content box, and the width Break uses in place of the
        // leaf's own for the one derive that follows the parent widening; 0 when it uses the leaf's own.
        private float _lastParentAvailable = float.NaN;
        private float _widthOverride;

        private bool _hasResult;
        private string _resultFor = string.Empty;
        private string _result = string.Empty;
        private TextWrapStyle _resultStyle;
        private bool _resultPreserves;
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

        private bool DisplaysBreaks => _hasResult && !string.Equals(_result, _resultFor, StringComparison.Ordinal);

        protected override void Derive(TextElement textElement, VisualElement parent)
        {
            _widthOverride = 0f;
            var available = ParentContentRoom(textElement, parent);
            if (available > _lastParentAvailable + ParentGrowthHysteresisPx
                && DisplaysBreaks && IsSizedByItsText(textElement))
            {
                _widthOverride = NaturalWidth(textElement, available);
            }
            _lastParentAvailable = available;

            var signature = ComputeSignature(textElement, _widthOverride);
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
            _widthOverride = 0f;
            _lastParentAvailable = float.NaN;
        }

        // The room the parent's content box gives the leaf's own content box.
        private static float ParentContentRoom(TextElement textElement, VisualElement parent)
        {
            var style = textElement.resolvedStyle;
            return parent.contentRect.width - style.marginLeft - style.marginRight - style.paddingLeft
                - style.paddingRight - style.borderLeftWidth - style.borderRightWidth;
        }

        // Whether the leaf's box is as wide as the longest line it displays, which is what a box sized from
        // its text is.
        private static bool IsSizedByItsText(TextElement textElement)
        {
            var lines = Measure(textElement, textElement.text ?? string.Empty);
            return Math.Abs(lines - textElement.contentRect.width) <= SizedByTextTolerancePx;
        }

        // The width the leaf's text takes unbroken in the room: its longest line there.
        private float NaturalWidth(TextElement textElement, float available)
        {
            if (!(available >= MinBalanceableWidthPx))
            {
                return 0f;
            }
            var width = textElement.MeasureTextSize(
                _resultFor, available, VisualElement.MeasureMode.AtMost,
                float.NaN, VisualElement.MeasureMode.Undefined).x;
            return width >= MinBalanceableWidthPx ? width : 0f;
        }

        // The terms are what Break keys its result on besides the text, which reaches the resolver on its
        // own path, and the white-space, since a variant can switch the wrap mode without moving the box.
        private static int ComputeSignature(TextElement textElement, float widthOverride)
        {
            var style = textElement.resolvedStyle;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(textElement.contentRect.width * 4f);
                hash = hash * 31 + Mathf.RoundToInt(widthOverride * 4f);
                hash = hash * 31 + style.fontSize.GetHashCode();
                hash = hash * 31 + (style.letterSpacing + style.wordSpacing).GetHashCode();
                hash = hash * 31 + style.unityFontDefinition.GetHashCode();
                hash = hash * 31 + (int)style.unityFontStyleAndWeight;
                hash = hash * 31 + (int)style.whiteSpace;
                return hash;
            }
        }

        // The text with a newline at every break the style asks for. text is what the leaf displays before
        // decoration and line height wrap it; preserves says whether its white space is kept as written.
        internal string Break(string text, bool preserves)
        {
            var textElement = (TextElement)target;
            var width = _widthOverride > 0f ? _widthOverride : textElement.contentRect.width;
            var resolved = textElement.resolvedStyle;
            var fontSize = resolved.fontSize;
            var font = resolved.unityFontDefinition;
            var spacing = resolved.letterSpacing + resolved.wordSpacing;
            if (_hasResult && _resultStyle == _style && _resultPreserves == preserves && _resultWidth == width
                && _resultFontSize == fontSize && _resultSpacing == spacing && _resultFont.Equals(font)
                && _resultFor == text)
            {
                return _result;
            }

            var broken = new ParagraphBreaker(textElement, _style, width, fontSize, preserves).Break(text);
            if (broken == null)
            {
                // The font has not resolved, so nothing was measured. Not recorded: the same inputs are a
                // different question once it has.
                return text;
            }
            _hasResult = true;
            _resultStyle = _style;
            _resultPreserves = preserves;
            _resultWidth = width;
            _resultFontSize = fontSize;
            _resultSpacing = spacing;
            _resultFont = font;
            _resultFor = text;
            _result = broken;
            return broken;
        }

        private static float Measure(TextElement textElement, string text) =>
            textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        // One pass over a text: measures its items and applies TextLineBreaker's answer to each paragraph.
        private sealed class ParagraphBreaker
        {
            private readonly TextElement _element;
            private readonly TextWrapStyle _style;
            private readonly float _width;
            private readonly float _fontSize;
            private readonly bool _preserves;
            private readonly List<int> _starts = new();
            private readonly List<int> _ends = new();
            private readonly Dictionary<string, float> _gaps = new(StringComparer.Ordinal);
            private bool _unmeasurable;

            public ParagraphBreaker(TextElement element, TextWrapStyle style, float width, float fontSize, bool preserves)
            {
                _element = element;
                _style = style;
                _width = width;
                _fontSize = fontSize;
                _preserves = preserves;
            }

            // Null when a measurement came back without a number.
            public string? Break(string text)
            {
                var builder = new StringBuilder(text.Length + 4);
                var start = 0;
                while (start <= text.Length)
                {
                    var end = text.IndexOf('\n', start);
                    if (end < 0)
                    {
                        end = text.Length;
                    }
                    builder.Append(BreakParagraph(text.Substring(start, end - start)));
                    if (end < text.Length)
                    {
                        builder.Append('\n');
                    }
                    start = end + 1;
                }
                return _unmeasurable ? null : builder.ToString();
            }

            private string BreakParagraph(string paragraph)
            {
                if (_unmeasurable || !TextBreakOpportunities.Find(paragraph, _starts, _ends))
                {
                    return paragraph;
                }
                var whole = Measure(_element, paragraph);
                if (float.IsNaN(whole))
                {
                    _unmeasurable = true;
                    return paragraph;
                }
                if (_starts.Count < 4 || whole > (TextLineBreaker.MaxLines(_style) + SpareLines) * _width)
                {
                    return paragraph;
                }
                if (!TryMeasureItems(paragraph, out var lineStart, out var wordEnd))
                {
                    return paragraph;
                }

                var breaks = TextLineBreaker.Plan(
                    lineStart, wordEnd, _width, _style, 4f * _width * _fontSize,
                    (first, end) => FitsOneLine(LineText(paragraph, first, end)));
                if (breaks == null || breaks.Length + 1 > EngineLines(paragraph))
                {
                    return paragraph;
                }
                var broken = InsertBreaks(paragraph, breaks);
                return AllLinesFit(paragraph, breaks) ? broken : paragraph;
            }

            // The position every item ends at, and the one a line starting with it starts at, from the start
            // of the paragraph. A gap's width is the advance of the white space in it.
            private bool TryMeasureItems(string paragraph, out float[] lineStart, out float[] wordEnd)
            {
                lineStart = new float[_starts.Count];
                wordEnd = new float[_starts.Count];
                for (var i = 0; i < _starts.Count; i++)
                {
                    wordEnd[i] = Measure(_element, paragraph.Substring(0, _ends[i]));
                    var gap = i == 0 ? 0f : GapAdvance(paragraph.Substring(_ends[i - 1], _starts[i] - _ends[i - 1]));
                    lineStart[i] = i == 0 ? 0f : wordEnd[i - 1] + gap;
                    if (float.IsNaN(wordEnd[i]) || float.IsNaN(gap))
                    {
                        _unmeasurable = true;
                        return false;
                    }
                }
                return true;
            }

            // The advance of a run of white space. A single space is measured as eight spaces in nine
            // characters, so the pixel-grid rounding of the two measurements is spread over eight; any
            // other run is measured between two letters, where a tab stop or a wide space is an estimate
            // that AllLinesFit checks.
            private float GapAdvance(string gap)
            {
                if (gap.Length == 0)
                {
                    return 0f;
                }
                if (_gaps.TryGetValue(gap, out var known))
                {
                    return known;
                }
                var advance = gap == " "
                    ? (Measure(_element, "x x x x x x x x x") - Measure(_element, "xxxxxxxxx")) / 8f
                    : Measure(_element, "x" + gap + "x") - Measure(_element, "xx");
                _gaps[gap] = advance;
                return advance;
            }

            // Items first..end-1 as one line; the first line keeps the white space before its first item.
            private string LineText(string paragraph, int first, int end)
            {
                var from = first == 0 ? 0 : _starts[first];
                return paragraph.Substring(from, _ends[end - 1] - from);
            }

            // A gap is replaced by the newline when the white space collapses, and kept ahead of it when it
            // is preserved.
            private string InsertBreaks(string paragraph, int[] breaks)
            {
                var builder = new StringBuilder(paragraph.Length + breaks.Length);
                var copied = 0;
                foreach (var item in breaks)
                {
                    var gapStart = _ends[item - 1];
                    var gapEnd = _starts[item];
                    var keep = _preserves || gapStart == gapEnd ? gapEnd : gapStart;
                    builder.Append(paragraph, copied, keep - copied);
                    builder.Append('\n');
                    copied = gapEnd;
                }
                builder.Append(paragraph, copied, paragraph.Length - copied);
                return builder.ToString();
            }

            // Whether the engine lays out every line the breaks make in one line's height, which the
            // arithmetic over rounded measurements cannot promise.
            private bool AllLinesFit(string paragraph, int[] breaks)
            {
                var first = 0;
                foreach (var item in breaks)
                {
                    if (!FitsOneLine(LineText(paragraph, first, item)))
                    {
                        return false;
                    }
                    first = item;
                }
                return FitsOneLine(LineText(paragraph, first, _starts.Count));
            }

            // How many lines the engine breaks the paragraph into at this width, 0 when it cannot say. A
            // division with more lines than that is not one the style could have asked for.
            private int EngineLines(string paragraph)
            {
                var single = Height(paragraph, float.NaN, VisualElement.MeasureMode.Undefined);
                var wrapped = Height(paragraph, _width, VisualElement.MeasureMode.Exactly);
                return single > 0f && !float.IsNaN(wrapped) ? Mathf.RoundToInt(wrapped / single) : 0;
            }

            // The engine's own answer: whether it lays the line out in one line's height at the width.
            private bool FitsOneLine(string line)
            {
                var single = Height(line, float.NaN, VisualElement.MeasureMode.Undefined);
                var wrapped = Height(line, _width, VisualElement.MeasureMode.Exactly);
                return wrapped <= single + HeightEpsilonPx;
            }

            private float Height(string text, float width, VisualElement.MeasureMode mode) =>
                _element.MeasureTextSize(text, width, mode, float.NaN, VisualElement.MeasureMode.Undefined).y;
        }
    }
}
