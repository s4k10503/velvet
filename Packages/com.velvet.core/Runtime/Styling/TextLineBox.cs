#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A text element Velvet creates, which measures and places its text in CSS line boxes.
    internal interface ILineBoxed
    {
        TextLineBox LineBox { get; }
    }

    // CSS gives text one line box per line, each the line height tall, with the glyphs centred in it by half the
    // difference between the line height and the font's content height. UI Toolkit's generator makes a block one
    // content height tall for the first line, advances by the <line-height> tag only between lines, and places
    // that block in the content box by the vertical part of -unity-text-align.
    //
    // So the element measures its text with the generator and replaces the height with that many line boxes,
    // reading the line count off the generator's height. CSS has no vertical alignment inside a line box, so the
    // element is aligned middle-* with the horizontal part it resolves, and the placement is moved by a <voffset>
    // around the whole shown text (Raise), which StyleTextEffectResolver writes: a raise of the content height less
    // the line boxes' puts the first line's glyphs half a leading below the content box's top at any box height.
    // TextLineBoxPlaybackTests pins it.
    internal sealed class TextLineBox
    {
        // The marker whose rule in _typography.uss makes UI Toolkit send the element CustomStyleResolvedEvent, so the
        // alignment is settled in the style pass that changed it.
        internal const string MarkerClass = "velvet-line-box";

        // A single line the generator lays out to the font's own line ascender and descender, whatever its glyphs,
        // and two of them, whose difference is the font's own line advance.
        private const string Probe = "M";
        private const string TwoLines = "M\nM";

        // The utilities that write -unity-text-align, in the order the bundled sheets declare them, since among
        // several on one element the later rule is the one UI Toolkit applies. TextAlignSheetOrderTests reads the
        // sheets to pin it.
        internal static readonly System.Collections.Generic.KeyValuePair<string, TextAnchor>[] AlignClassesInSheetOrder =
        {
            new("text-center", TextAnchor.MiddleCenter),
            new("text-end", TextAnchor.MiddleRight),
            new("text-left", TextAnchor.MiddleLeft),
            new("text-right", TextAnchor.MiddleRight),
            new("text-start", TextAnchor.MiddleLeft),
            new("text-top-center", TextAnchor.UpperCenter),
            new("text-top-left", TextAnchor.UpperLeft),
        };

        private float _fontSize = float.NaN;
        private float _pixelsPerPoint = float.NaN;
        private FontDefinition _font;
        private Font? _legacyFont;
        private FontStyle _style;
        private float _singleLine = float.NaN;
        private float _advance = float.NaN;

        private readonly TextElement _element;
        private ReconcilerContext? _context;
        private string? _unraised;

        // The alignment this wrote inline, or null while the element's alignment is its own.
        private TextAnchor? _owned;

        // What the last settle read, so a geometry or style event that changed none of it measures nothing.
        private Rect _settledContent;
        private string? _settledShown;
        private float _settledLineHeight = float.NaN;
        private TextAnchor _settledAlign;
        // The element's last few measures by width, since the layout serves a width it measured before from its
        // own cache rather than asking again.
        private readonly Vector2[] _measured = { new(float.NaN, float.NaN), new(float.NaN, float.NaN), new(float.NaN, float.NaN), new(float.NaN, float.NaN) };
        private int _measuredNext;

        public TextLineBox(TextElement element)
        {
            _element = element;
            element.AddToClassList(MarkerClass);
            element.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!SizedByItsMeasure())
                {
                    Settle();
                }
            });
            element.RegisterCallback<CustomStyleResolvedEvent>(_ => Settle());
        }

        // How far the shown text is raised, in pixels; negative lowers it.
        public float Raise { get; private set; }

        // The shown text without the raise, which is what the element is measured by while it is raised, so that the
        // line count and the box never depend on the raise they decide.
        public string? Unraised => Raise != 0f ? _unraised : null;

        // Read before the element's own measure, so that the width the text handle keeps from its last measure is
        // the element's text's rather than the probe's.
        public static Lines Before(TextElement element, TextLineBox box, VisualElement.MeasureMode heightMode)
        {
            if (heightMode == VisualElement.MeasureMode.Exactly || string.IsNullOrEmpty(element.text))
            {
                return default;
            }
            var lineHeight = StyleTextEffectResolver.LineHeightOf(element, out var declared);
            if (float.IsNaN(lineHeight))
            {
                return default;
            }
            var single = box.SingleLine(element);
            // Rich text carries the line height as a tag (StyleTextEffectResolver writes the preflight's where the text
            // can span lines); without one the lines advance by the font's own.
            var tagged = element.enableRichText && (declared || StyleTextEffectResolver.CanSpanLines(element.text));
            return new Lines(lineHeight, single, tagged ? lineHeight : box._advance);
        }

        // The height text takes at a width, in the line boxes of an element Velvet created and in the generator's
        // own lines anywhere else.
        public static float HeightAt(TextElement element, string text, float width)
        {
            var box = (element as ILineBoxed)?.LineBox;
            var lines = box != null ? Before(element, box, VisualElement.MeasureMode.Undefined) : default;
            var generated = element.MeasureTextSize(box?.Unraised ?? text, width, VisualElement.MeasureMode.Exactly, float.NaN,
                VisualElement.MeasureMode.Undefined);
            return lines.Fit(generated, float.NaN, VisualElement.MeasureMode.Undefined).y;
        }

        // The raise the shown text is written with, the context that wrote it and the text it raises.
        public float Shown(ReconcilerContext context, string display)
        {
            _context = context;
            if (!string.Equals(display, _unraised, System.StringComparison.Ordinal))
            {
                // Another text measures differently at every width.
                System.Array.Fill(_measured, new Vector2(float.NaN, float.NaN));
            }
            _unraised = display;
            // A pooled element's classes are cleared when it is returned.
            if (!_element.ClassListContains(MarkerClass))
            {
                _element.AddToClassList(MarkerClass);
            }
            return Raise;
        }

        // A pooled element starts its next use unraised, unowned and with its own alignment.
        public void Reset()
        {
            Raise = 0f;
            _context = null;
            _unraised = null;
            _settledShown = null;
            if (_owned != null)
            {
                _owned = null;
                _element.style.unityTextAlign = StyleKeyword.Null;
            }
        }

        // Aligns the element middle-* with the horizontal part it resolves. While the inline value is this one's, the
        // part is read from the element's own alignment classes and otherwise inherited from its parent, since the
        // resolved value is the inline one. An inline value somebody else wrote is left alone.
        private void Align()
        {
            if (float.IsNaN(StyleTextEffectResolver.LineHeightOf(_element)))
            {
                Release();
                return;
            }
            var inline = _element.style.unityTextAlign;
            if (inline.keyword == StyleKeyword.Undefined && inline.value != _owned)
            {
                _owned = null;
                return;
            }
            var underlying = _owned == null ? _element.resolvedStyle.unityTextAlign : Underlying();
            var wanted = Middle(underlying);
            if (_owned == null && wanted == underlying)
            {
                return;
            }
            if (_owned == wanted)
            {
                return;
            }
            _owned = wanted;
            _element.style.unityTextAlign = wanted;
        }

        private void Release()
        {
            if (_owned == null)
            {
                return;
            }
            _owned = null;
            _element.style.unityTextAlign = StyleKeyword.Null;
        }

        private TextAnchor Underlying()
        {
            TextAnchor? plain = null;
            TextAnchor? important = null;
            var rank = -1;
            var importantRank = -1;
            foreach (var cls in _element.GetClasses())
            {
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var isImportant);
                for (var i = 0; i < AlignClassesInSheetOrder.Length; i++)
                {
                    if (core != AlignClassesInSheetOrder[i].Key)
                    {
                        continue;
                    }
                    if (isImportant && i > importantRank)
                    {
                        importantRank = i;
                        important = AlignClassesInSheetOrder[i].Value;
                    }
                    else if (!isImportant && i > rank)
                    {
                        rank = i;
                        plain = AlignClassesInSheetOrder[i].Value;
                    }
                }
            }
            return important ?? plain ?? _element.hierarchy.parent?.resolvedStyle.unityTextAlign ?? TextAnchor.UpperLeft;
        }

        private static TextAnchor Middle(TextAnchor align) => align switch
        {
            TextAnchor.UpperCenter or TextAnchor.LowerCenter => TextAnchor.MiddleCenter,
            TextAnchor.UpperRight or TextAnchor.LowerRight => TextAnchor.MiddleRight,
            TextAnchor.UpperLeft or TextAnchor.LowerLeft => TextAnchor.MiddleLeft,
            _ => align,
        };

        private void Settle()
        {
            Align();
            var context = _context;
            if (context == null || _element.panel == null)
            {
                return;
            }
            var content = _element.contentRect;
            if (!(content.height >= 0f) || !(content.width > 0f))
            {
                return;
            }
            var lines = Before(_element, this, VisualElement.MeasureMode.Undefined);
            var shown = Unraised ?? StyleTextEffectResolver.DisplayedText(context, _element);
            var align = _element.resolvedStyle.unityTextAlign;
            if (content == _settledContent && ReferenceEquals(shown, _settledShown) && align == _settledAlign
                && lines.LineHeight.Equals(_settledLineHeight))
            {
                return;
            }
            _settledContent = content;
            _settledShown = shown;
            _settledAlign = align;
            _settledLineHeight = lines.LineHeight;

            // A middle-aligned box its own measure sized at this width needs no raise, and no measure to know it.
            if (IsMiddle(align) && IsMeasuredSize(content))
            {
                SetRaise(0f, context);
                return;
            }
            var generated = _element.MeasureTextSize(shown, content.width, VisualElement.MeasureMode.Exactly, float.NaN,
                VisualElement.MeasureMode.Undefined);
            if (!(generated.y > 0f))
            {
                return;
            }
            SetRaise(Mathf.Round(Target(align, lines, content, generated) * 100f) / 100f, context);
        }

        private void SetRaise(float raise, ReconcilerContext context)
        {
            // A change under half a pixel is not shown, since each one shows the text again.
            if (Mathf.Abs(raise - Raise) < 0.5f)
            {
                return;
            }
            Raise = raise;
            StyleTextEffectResolver.ReapplyElement(context, _element);
        }

        // An unraised, middle-aligned box at the size its own measure gave it, which a settle would leave alone. Read
        // first on a geometry change, the event every relayout of every text sends.
        private bool SizedByItsMeasure()
        {
            var content = _element.contentRect;
            return Raise == 0f && IsMiddle(_element.resolvedStyle.unityTextAlign) && IsMeasuredSize(content);
        }

        private bool IsMeasuredSize(Rect content)
        {
            foreach (var size in _measured)
            {
                if (Mathf.Abs(content.width - size.x) < 0.5f)
                {
                    return Mathf.Abs(content.height - size.y) < 0.5f;
                }
            }
            return false;
        }

        private static bool IsMiddle(TextAnchor align)
            => align is TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight;

        // Records a measure, whose width is the content box's when the layout sizes the element by it. A height the
        // layout dictated is not one the text gave, so it is not recorded.
        public Vector2 Measured(float width, VisualElement.MeasureMode widthMode, VisualElement.MeasureMode heightMode, Vector2 measure)
        {
            if (heightMode == VisualElement.MeasureMode.Exactly)
            {
                return measure;
            }
            var key = widthMode == VisualElement.MeasureMode.Exactly ? width : measure.x;
            for (var i = 0; i < _measured.Length; i++)
            {
                if (Mathf.Abs(_measured[i].x - key) < 0.5f)
                {
                    _measured[i] = new Vector2(key, measure.y);
                    return measure;
                }
            }
            _measured[_measuredNext] = new Vector2(key, measure.y);
            _measuredNext = (_measuredNext + 1) % _measured.Length;
            return measure;
        }

        private static float Target(TextAnchor align, Lines lines, Rect content, Vector2 generated)
        {
            if (IsMiddle(align))
            {
                return content.height - lines.Fit(generated, float.NaN, VisualElement.MeasureMode.Undefined).y;
            }
            if (align is TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight)
            {
                return lines.Leading > 0f ? -lines.Leading * 0.5f : 0f;
            }
            return 0f;
        }

        // What the generator makes one line of the element's font, and its advance between two, kept until the font,
        // its size or style, or the panel's scale changes.
        private float SingleLine(TextElement element)
        {
            var style = element.resolvedStyle;
            var pixelsPerPoint = element.panel?.scaledPixelsPerPoint ?? float.NaN;
            if (style.fontSize != _fontSize || pixelsPerPoint != _pixelsPerPoint || style.unityFontStyleAndWeight != _style
                || !style.unityFontDefinition.Equals(_font) || style.unityFont != _legacyFont)
            {
                _fontSize = style.fontSize;
                _pixelsPerPoint = pixelsPerPoint;
                _style = style.unityFontStyleAndWeight;
                _font = style.unityFontDefinition;
                _legacyFont = style.unityFont;
                _singleLine = element.MeasureTextSize(Probe, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN,
                    VisualElement.MeasureMode.Undefined).y;
                _advance = element.MeasureTextSize(TwoLines, float.NaN, VisualElement.MeasureMode.Undefined, float.NaN,
                    VisualElement.MeasureMode.Undefined).y - _singleLine;
            }
            return _singleLine;
        }

        public readonly struct Lines
        {
            private readonly float _singleLine;
            private readonly float _advance;

            public Lines(float lineHeight, float singleLine, float advance)
            {
                LineHeight = lineHeight;
                _singleLine = singleLine;
                _advance = advance;
            }

            public float LineHeight { get; }

            // The line height less the single-line height, or zero where the element has no line height.
            public float Leading => _singleLine > 0f ? LineHeight - _singleLine : 0f;

            // How many lines the generator laid out, read off its height.
            public int Count(Vector2 generated)
                => _advance > 0f ? 1 + Mathf.Max(0, Mathf.RoundToInt((generated.y - _singleLine) / _advance)) : 1;

            // The generator's measure with its height replaced by the line boxes, or unchanged where the element has no
            // line height.
            public Vector2 Fit(Vector2 generated, float desiredHeight, VisualElement.MeasureMode heightMode)
            {
                if (!(generated.y > 0f))
                {
                    return generated;
                }
                var height = _singleLine > 0f ? Count(generated) * Mathf.Max(0f, LineHeight) : generated.y;
                if (heightMode == VisualElement.MeasureMode.AtMost)
                {
                    height = Mathf.Min(height, desiredHeight);
                }
                return new Vector2(generated.x, height);
            }
        }
    }
}
