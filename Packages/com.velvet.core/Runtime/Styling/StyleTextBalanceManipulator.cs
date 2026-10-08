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
    // result while neither the text nor those have, and keeps what it measured of a text across widths.
    //
    // The width broken against is the leaf's own, except for a leaf whose box UI Toolkit sizes from its
    // text: its own width is only the longest line it last displayed, so it is broken against the room its
    // parent offers it instead (LeafWidth). A parent offering more room by more than the hysteresis
    // re-derives such a leaf, the baseline moving only when the room has moved that far.
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

        // A paragraph of more items than this is left to the engine, which bounds the measurements one
        // text costs. Seven lines of CJK text at a wide width fit well inside it.
        private const int MaxItems = 400;

        // A parent has to offer more room than this, from where it last was, for the leaf to be re-broken
        // against it, so the pixel-grid rounding of a layout pass is not a widening.
        private const float RoomHysteresisPx = 2f;

        // How far a leaf's box may differ from the width of the lines it displays and still be read as
        // sized by them, and how much room beyond the box makes the difference matter. Measured text is
        // rounded up to the pixel grid.
        private const float SizedByTextTolerancePx = 1.5f;

        private readonly ReconcilerContext _ctx;

        private TextWrapStyle _style;

        // False until the leaf has been laid out since it was attached, so a recycled element's contentRect
        // from its previous tenant is not read as its width.
        private bool _laidOut;

        private float _roomBaseline = float.NaN;
        private int _roomEpoch;

        // What was measured of one text under one font, kept across widths. A paragraph's measurements
        // depend on neither the width nor the style.
        private string? _measuredFor;
        private MeasureKey _measureKey;
        private readonly Dictionary<string, MeasuredParagraph> _measured = new(StringComparer.Ordinal);

        private bool _hasResult;
        private TextWrapStyle _resultStyle;
        private float _resultWidth;
        private string _result = string.Empty;

        private bool _recheckPending;

        private bool _hasDecision;
        private float _decisionOwn;
        private float _decisionRoom;
        private string _decisionDisplayed = string.Empty;
        private string _decisionFor = string.Empty;
        private float _decisionWidth;

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

        // Whether the leaf has a width of its own to break in: laid out since it was attached, and wide
        // enough to hold anything. False before that, when the resolver leaves the text as it is.
        internal bool CanBreak =>
            _laidOut && target is TextElement textElement && textElement.contentRect.width >= MinBalanceableWidthPx;

        // Whether the text Break was last given since BeginBreak came back with newlines in it.
        internal bool Broke { get; private set; }

        internal void BeginBreak()
        {
            Broke = false;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            // Ahead of the base's, so the flag is set when the same event derives.
            target.RegisterCallback<AttachToPanelEvent>(OnAttachedHere);
            target.RegisterCallback<GeometryChangedEvent>(OnLaidOutHere);
            target.schedule.Execute(MarkLaidOut);
            base.RegisterCallbacksOnTarget();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<AttachToPanelEvent>(OnAttachedHere);
            target.UnregisterCallback<GeometryChangedEvent>(OnLaidOutHere);
            base.UnregisterCallbacksFromTarget();
        }

        private void OnAttachedHere(AttachToPanelEvent evt)
        {
            _laidOut = false;
            target.schedule.Execute(MarkLaidOut);
        }

        private void OnLaidOutHere(GeometryChangedEvent evt)
        {
            _laidOut = true;
        }

        // Runs on a scheduler tick, after the layout pass of the frame the manipulator was attached in, so
        // a leaf attached to a settled layout with nothing to move it still gets broken.
        private void MarkLaidOut()
        {
            _laidOut = true;
            _hasSignature = false;
            Apply();
        }

        protected override void Derive(TextElement textElement, VisualElement parent)
        {
            TrackRoom(ParentContentRoom(textElement, parent));
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
            _hasDecision = false;
            _measuredFor = null;
            _measured.Clear();
            _roomBaseline = float.NaN;
            _laidOut = false;
        }

        // The room moves the baseline only when it has moved by more than the hysteresis, so a widening in
        // steps smaller than it adds up to one; a widening also bumps the epoch, which re-derives the leaf.
        private void TrackRoom(float room)
        {
            if (float.IsNaN(room))
            {
                return;
            }
            if (float.IsNaN(_roomBaseline))
            {
                _roomBaseline = room;
            }
            else if (room > _roomBaseline + RoomHysteresisPx)
            {
                _roomBaseline = room;
                _roomEpoch++;
            }
            else if (room < _roomBaseline - RoomHysteresisPx)
            {
                _roomBaseline = room;
            }
        }

        // The room the parent's content box gives the leaf's own content box.
        private static float ParentContentRoom(TextElement textElement, VisualElement parent)
        {
            var style = textElement.resolvedStyle;
            return parent.contentRect.width - style.marginLeft - style.marginRight - style.paddingLeft
                - style.paddingRight - style.borderLeftWidth - style.borderRightWidth;
        }

        // The terms are what Break keys its result on besides the text, which reaches the resolver on its
        // own path, and the white-space, since a variant can switch the wrap mode without moving the box.
        private int ComputeSignature(TextElement textElement)
        {
            var style = textElement.resolvedStyle;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(textElement.contentRect.width * 4f);
                hash = hash * 31 + _roomEpoch;
                hash = hash * 31 + new MeasureKey(style).GetHashCode();
                hash = hash * 31 + (int)style.whiteSpace;
                hash = hash * 31 + (_laidOut ? 1 : 0);
                return hash;
            }
        }

        // The width the leaf's text breaks against. Its own, unless its box is sized by the lines it
        // displays and its parent offers more room than that: then the room, as the text takes it
        // unbroken, so a leaf does not stay as narrow as its last text and font left it.
        private float LeafWidth(TextElement textElement, string text)
        {
            var own = textElement.contentRect.width;
            var parent = textElement.parent;
            if (parent == null)
            {
                return own;
            }
            var room = ParentContentRoom(textElement, parent);
            var displayed = textElement.text ?? string.Empty;
            if (_hasDecision && _decisionOwn == own && _decisionRoom == room
                && string.Equals(_decisionDisplayed, displayed, StringComparison.Ordinal)
                && string.Equals(_decisionFor, text, StringComparison.Ordinal))
            {
                return _decisionWidth;
            }
            var width = own;
            if (room > own + SizedByTextTolerancePx && IsSizedByItsLines(textElement, displayed, own))
            {
                var natural = textElement.MeasureTextSize(
                    text, room, VisualElement.MeasureMode.AtMost,
                    float.NaN, VisualElement.MeasureMode.Undefined).x;
                width = natural >= MinBalanceableWidthPx ? Math.Min(natural, CeilingContentWidth(textElement)) : own;
            }
            _hasDecision = true;
            _decisionOwn = own;
            _decisionRoom = room;
            _decisionDisplayed = displayed;
            _decisionFor = text;
            _decisionWidth = width;
            return width;
        }

        // The widest the leaf's content box may be under a max-width of its cascade, or infinity.
        private static float CeilingContentWidth(TextElement textElement)
        {
            var style = textElement.resolvedStyle;
            var declared = style.maxWidth;
            if (declared.keyword == StyleKeyword.None)
            {
                return float.PositiveInfinity;
            }
            return declared.value - style.paddingLeft - style.paddingRight - style.borderLeftWidth
                - style.borderRightWidth;
        }

        // Whether the leaf's box is as wide as the longest line it displays, which is what a box sized from
        // its text is.
        private static bool IsSizedByItsLines(TextElement textElement, string displayed, float own)
        {
            var lines = Measure(textElement, displayed);
            return Math.Abs(lines - own) <= SizedByTextTolerancePx;
        }

        // The text with a newline at every break the style asks for. text is what the leaf displays before
        // decoration and line height wrap it. A white space at a break is dropped.
        internal string Break(string text)
        {
            var textElement = (TextElement)target;
            var key = new MeasureKey(textElement.resolvedStyle);
            if (_measuredFor == null || !key.Matches(_measureKey)
                || !string.Equals(_measuredFor, text, StringComparison.Ordinal))
            {
                _measured.Clear();
                _hasResult = false;
                _hasDecision = false;
                _measureKey = key;
                _measuredFor = text;
            }

            var width = LeafWidth(textElement, text);
            if (_hasResult && _resultStyle == _style && _resultWidth == width)
            {
                Broke = !string.Equals(_result, text, StringComparison.Ordinal);
                return _result;
            }

            var broken = new ParagraphBreaker(this, textElement, width).Break(text);
            if (broken == null)
            {
                // The font has not resolved, so nothing was measured. Not recorded: the same inputs are a
                // different question once it has.
                _measuredFor = null;
                return text;
            }
            ScheduleKeyRecheck();
            _hasResult = true;
            _resultStyle = _style;
            _resultWidth = width;
            _result = broken;
            Broke = !string.Equals(broken, text, StringComparison.Ordinal);
            return broken;
        }

        // The style a measurement read is the last one UI Toolkit resolved, and a class or an inline write
        // reaches it on the next style pass, which raises no event of its own when the box does not move. So
        // a result is compared against the style once that pass has run, and the leaf re-derived if the font
        // moved under it.
        private void ScheduleKeyRecheck()
        {
            if (_recheckPending)
            {
                return;
            }
            _recheckPending = true;
            target.schedule.Execute(RecheckKey);
        }

        private void RecheckKey()
        {
            _recheckPending = false;
            if (target is TextElement textElement && _measuredFor != null
                && !new MeasureKey(textElement.resolvedStyle).Matches(_measureKey))
            {
                _hasSignature = false;
                Apply();
            }
        }

        private static float Measure(TextElement textElement, string text) =>
            textElement.MeasureTextSize(
                text, float.NaN, VisualElement.MeasureMode.Undefined,
                float.NaN, VisualElement.MeasureMode.Undefined).x;

        // What a measurement of text depends on besides the text.
        private readonly struct MeasureKey
        {
            private readonly float _fontSize;
            private readonly float _letterSpacing;
            private readonly float _wordSpacing;
            private readonly FontDefinition _font;
            private readonly FontStyle _fontStyle;

            public MeasureKey(IResolvedStyle style)
            {
                _fontSize = style.fontSize;
                _letterSpacing = style.letterSpacing;
                _wordSpacing = style.wordSpacing;
                _font = style.unityFontDefinition;
                _fontStyle = style.unityFontStyleAndWeight;
            }

            public bool Matches(in MeasureKey other) =>
                _fontSize == other._fontSize && _letterSpacing == other._letterSpacing
                && _wordSpacing == other._wordSpacing && _fontStyle == other._fontStyle && _font.Equals(other._font);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _fontSize.GetHashCode();
                    hash = hash * 31 + _letterSpacing.GetHashCode();
                    hash = hash * 31 + _wordSpacing.GetHashCode();
                    hash = hash * 31 + _font.GetHashCode();
                    return hash * 31 + (int)_fontStyle;
                }
            }
        }

        // A paragraph's items and where each ends, measured once per text and font. Skip is set for a
        // paragraph the breaker leaves to the engine whatever the width.
        private sealed class MeasuredParagraph
        {
            public bool Skip;
            public readonly List<int> Starts = new();
            public readonly List<int> Ends = new();
            public float[] LineStart = Array.Empty<float>();
            public float[] WordEnd = Array.Empty<float>();
            public float Whole;
        }

        // One pass over a text at a width: applies TextLineBreaker's answer to each paragraph.
        private sealed class ParagraphBreaker
        {
            private readonly StyleTextBalanceManipulator _owner;
            private readonly TextElement _element;
            private readonly float _width;
            private readonly Dictionary<string, float> _gaps = new(StringComparer.Ordinal);
            private bool _unmeasurable;

            public ParagraphBreaker(StyleTextBalanceManipulator owner, TextElement element, float width)
            {
                _owner = owner;
                _element = element;
                _width = width;
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
                var measured = Measured(paragraph);
                if (measured == null || measured.Skip)
                {
                    return paragraph;
                }
                var style = _owner._style;
                if (measured.Whole > (TextLineBreaker.MaxLines(style) + SpareLines) * _width)
                {
                    return paragraph;
                }

                var breaks = TextLineBreaker.Plan(
                    measured.LineStart, measured.WordEnd, _width, style, 4f * _width * _element.resolvedStyle.fontSize,
                    (first, end) => FitsOneLine(LineText(paragraph, measured, first, end)));
                if (breaks == null || breaks.Length + 1 > EngineLines(paragraph)
                    || !AllLinesFit(paragraph, measured, breaks))
                {
                    return paragraph;
                }
                return InsertBreaks(paragraph, measured, breaks);
            }

            // Null when a measurement came back without a number.
            private MeasuredParagraph? Measured(string paragraph)
            {
                if (_unmeasurable)
                {
                    return null;
                }
                if (_owner._measured.TryGetValue(paragraph, out var known))
                {
                    return known;
                }
                var measured = new MeasuredParagraph();
                if (!TextBreakOpportunities.Find(paragraph, measured.Starts, measured.Ends))
                {
                    measured.Skip = true;
                }
                else
                {
                    measured.Whole = Measure(_element, paragraph);
                    measured.Skip = float.IsNaN(measured.Whole) || measured.Starts.Count < 4
                        || measured.Starts.Count > MaxItems;
                    _unmeasurable = float.IsNaN(measured.Whole);
                }
                if (!measured.Skip && !TryMeasureItems(paragraph, measured))
                {
                    _unmeasurable = true;
                    return null;
                }
                if (!_unmeasurable)
                {
                    _owner._measured[paragraph] = measured;
                }
                return _unmeasurable ? null : measured;
            }

            // The position every item ends at, and the one a line starting with it starts at, from the start
            // of the paragraph. A gap's width is the advance of the white space in it.
            private bool TryMeasureItems(string paragraph, MeasuredParagraph measured)
            {
                var count = measured.Starts.Count;
                measured.LineStart = new float[count];
                measured.WordEnd = new float[count];
                for (var i = 0; i < count; i++)
                {
                    measured.WordEnd[i] = Measure(_element, paragraph.Substring(0, measured.Ends[i]));
                    var gap = i == 0 ? 0f : GapAdvance(
                        paragraph.Substring(measured.Ends[i - 1], measured.Starts[i] - measured.Ends[i - 1]));
                    measured.LineStart[i] = i == 0 ? 0f : measured.WordEnd[i - 1] + gap;
                    if (float.IsNaN(measured.WordEnd[i]) || float.IsNaN(gap))
                    {
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
            private static string LineText(string paragraph, MeasuredParagraph measured, int first, int end)
            {
                var from = first == 0 ? 0 : measured.Starts[first];
                return paragraph.Substring(from, measured.Ends[end - 1] - from);
            }

            // The gap before each item that begins a line becomes the newline.
            private static string InsertBreaks(string paragraph, MeasuredParagraph measured, int[] breaks)
            {
                var builder = new StringBuilder(paragraph.Length + breaks.Length);
                var copied = 0;
                foreach (var item in breaks)
                {
                    var gapStart = measured.Ends[item - 1];
                    builder.Append(paragraph, copied, gapStart - copied);
                    builder.Append('\n');
                    copied = measured.Starts[item];
                }
                builder.Append(paragraph, copied, paragraph.Length - copied);
                return builder.ToString();
            }

            // Whether the engine lays out every line the breaks make in one line's height, which the
            // arithmetic over rounded measurements cannot promise.
            private bool AllLinesFit(string paragraph, MeasuredParagraph measured, int[] breaks)
            {
                var first = 0;
                foreach (var item in breaks)
                {
                    if (!FitsOneLine(LineText(paragraph, measured, first, item)))
                    {
                        return false;
                    }
                    first = item;
                }
                return FitsOneLine(LineText(paragraph, measured, first, measured.Starts.Count));
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
