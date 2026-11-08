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
    // reading the line count off the generator's height. The placement is moved by a <voffset> around the whole
    // shown text (Raise), which StyleTextEffectResolver writes and which grows the generator's block by its own
    // size: under a middle-* alignment a raise of the content height less the line boxes' puts the first line's
    // glyphs half a leading below the content box's top at any box height, and under an upper-* alignment a
    // lowering by half the leading does where the leading is not negative. TextLineBoxPlaybackTests pins both.
    internal sealed class TextLineBox
    {
        // A single line the generator lays out to the font's own line ascender and descender, whatever its glyphs.
        private const string Probe = "M";

        private float _fontSize = float.NaN;
        private float _pixelsPerPoint = float.NaN;
        private FontDefinition _font;
        private Font? _legacyFont;
        private FontStyle _style;
        private float _singleLine = float.NaN;

        private readonly TextElement _element;
        private readonly System.Action _settle;
        private IVisualElementScheduledItem? _scheduled;

        public TextLineBox(TextElement element)
        {
            _element = element;
            _settle = Settle;
            element.RegisterCallback<GeometryChangedEvent>(_ => Settle());
        }

        // How far the shown text is raised, in pixels; negative lowers it.
        public float Raise { get; private set; }

        // The context that last showed the element's text, which is what shows it again with a new raise.
        private ReconcilerContext? _context;

        // Read before the element's own measure, so that the width the text handle keeps from its last measure is
        // the element's text's rather than the probe's.
        public static Lines Before(TextElement element, TextLineBox box, VisualElement.MeasureMode heightMode)
        {
            if (heightMode == VisualElement.MeasureMode.Exactly || string.IsNullOrEmpty(element.text))
            {
                return default;
            }
            var lineHeight = StyleTextEffectResolver.LineHeightOf(element);
            return new Lines(lineHeight, float.IsNaN(lineHeight) ? float.NaN : box.SingleLine(element), box.Raise);
        }

        // The height text takes at a width, in the line boxes of an element Velvet created and in the generator's
        // own lines anywhere else.
        public static float HeightAt(TextElement element, string text, float width)
        {
            var lines = element is ILineBoxed boxed ? Before(element, boxed.LineBox, VisualElement.MeasureMode.Undefined) : default;
            var generated = element.MeasureTextSize(text, width, VisualElement.MeasureMode.Exactly, float.NaN,
                VisualElement.MeasureMode.Undefined);
            return lines.Fit(generated, float.NaN, VisualElement.MeasureMode.Undefined).y;
        }

        // The raise the shown text is written with, and the context that wrote it.
        public float Shown(ReconcilerContext context)
        {
            _context = context;
            Schedule();
            return Raise;
        }

        // A pooled element starts its next use unraised and unowned.
        public void Reset()
        {
            Raise = 0f;
            _context = null;
        }

        // A class change can move the alignment without moving the box, so a showing settles again once the style
        // it was shown under has resolved.
        private void Schedule()
        {
            if (_scheduled == null)
            {
                _scheduled = _element.schedule.Execute(_settle);
            }
            else
            {
                _scheduled.ExecuteLater(0);
            }
        }

        private void Settle()
        {
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
            var raise = Mathf.Round(Target(lines, content) * 100f) / 100f;
            // A change under half a pixel is not shown, since each one shows the text again.
            if (Mathf.Abs(raise - Raise) < 0.5f)
            {
                return;
            }
            Raise = raise;
            StyleTextEffectResolver.ReapplyElement(context, _element);
        }

        private float Target(Lines lines, Rect content)
        {
            var align = _element.resolvedStyle.unityTextAlign;
            if (align is TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight)
            {
                return lines.Leading > 0f ? -lines.Leading * 0.5f : 0f;
            }
            if (align is not (TextAnchor.MiddleLeft or TextAnchor.MiddleCenter or TextAnchor.MiddleRight))
            {
                return 0f;
            }
            var shown = StyleTextEffectResolver.DisplayedText(_context!, _element);
            var generated = _element.MeasureTextSize(shown, content.width, VisualElement.MeasureMode.Exactly, float.NaN,
                VisualElement.MeasureMode.Undefined);
            return generated.y > 0f
                ? content.height - lines.Fit(generated, float.NaN, VisualElement.MeasureMode.Undefined).y
                : Raise;
        }

        // What the generator makes one line of the element's font, kept until the font, its size or style, or the
        // panel's scale changes.
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
            }
            return _singleLine;
        }

        public readonly struct Lines
        {
            private readonly float _lineHeight;
            private readonly float _singleLine;
            private readonly float _raise;

            public Lines(float lineHeight, float singleLine, float raise)
            {
                _lineHeight = lineHeight;
                _singleLine = singleLine;
                _raise = raise;
            }

            // The line height less the single-line height, or zero where the element has no line height.
            public float Leading => _singleLine > 0f ? _lineHeight - _singleLine : 0f;

            // The generator's measure with the raise's growth taken out and its height replaced by the line boxes,
            // or with the growth alone taken out where the element has no line height.
            public Vector2 Fit(Vector2 generated, float desiredHeight, VisualElement.MeasureMode heightMode)
            {
                if (!(generated.y > 0f))
                {
                    return generated;
                }
                var height = Mathf.Max(0f, generated.y - Mathf.Abs(_raise));
                if (_singleLine > 0f)
                {
                    var lines = _lineHeight > 0f ? 1 + Mathf.Max(0, Mathf.RoundToInt((height - _singleLine) / _lineHeight)) : 1;
                    height = lines * Mathf.Max(0f, _lineHeight);
                }
                if (heightMode == VisualElement.MeasureMode.AtMost)
                {
                    height = Mathf.Min(height, desiredHeight);
                }
                return new Vector2(generated.x, height);
            }
        }
    }
}
