#nullable enable
using System;
using System.Globalization;
using System.Text;
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
    // element is aligned middle-* with the horizontal part it resolves (MiddleAlignment), and the placement is moved
    // by a <voffset> around the whole shown text (Raise), which StyleTextEffectResolver writes: a raise of the content
    // height less the line boxes' puts the first line's glyphs half a leading below the content box's top at any box
    // height. A button's are centred in its box instead, as a browser centres a button's content.
    // TextLineBoxPlaybackTests pins both.
    internal sealed class TextLineBox
    {
        // The marker whose rule in _typography.uss makes UI Toolkit send an element CustomStyleResolvedEvent, so the
        // alignment is settled in the style pass that changed it. A label or button Velvet creates has the same custom
        // property from its own preflight class, and only a field's input text, which has neither, carries this one.
        internal const string MarkerClass = "velvet-line-box";

        // A single line the generator lays out to the font's own line ascender and descender, whatever its glyphs,
        // and two of them, whose difference is the font's own line advance.
        private const string Probe = "M";
        private const string TwoLines = "M\nM";

        private float _fontSize = float.NaN;
        private float _pixelsPerPoint = float.NaN;
        private FontDefinition _measuredFont;
        private Font? _legacyFont;
        private FontStyle _style;
        private float _singleLine = float.NaN;
        private float _advance = float.NaN;

        private readonly TextElement _element;
        private readonly bool _centred;
        private readonly MiddleAlignment _alignment = new();
        // Made the first time the element shows text without tags.
        private LineBoxFontOwner? _font;
        private ReconcilerContext? _context;
        private string? _unraised;
        // The app's text the element was last shown with, null before its first.
        private string? _shownRaw;
        private bool _settling;

        // The line height the element was last measured at, so that a line height the layout cannot see change asks it
        // for another measure rather than the one it holds.
        private float _measuredLineHeight;
        private bool _measuredOnce;
        private static readonly System.Reflection.MethodInfo? s_incrementVersion = EngineMember.ElementIncrementVersion.ResolveMethod();
        private static readonly object?[]? s_layoutChange =
            EngineMember.LayoutVersionChange.ResolveField() is { } change ? new[] { change.GetValue(null) } : null;

        // Whether the shown text carries a line height tag, and whether it carries rich text of the app's own, whose
        // tags can space or size a line past what a count of line boxes gives.
        private bool _tagged;
        private bool _ownMarkup;

        // Whether the generator has laid the untagged text out in more than one line, so that it is shown with the
        // preflight's tag until the text changes, whether it has been shown again for it, and the text it was.
        private bool _spansLines;
        private bool _retagged;
        private string? _linesOf;

        // What the last settle read, so a geometry or style event that changed none of it measures nothing.
        private Rect _settledContent;
        private string? _settledShown;
        private float _settledLineHeight = float.NaN;
        private TextAnchor _settledAlign;
        // The element's last few measures by width, since the layout serves a width it measured before from its
        // own cache rather than asking again.
        private readonly Vector2[] _measured = { new(float.NaN, float.NaN), new(float.NaN, float.NaN), new(float.NaN, float.NaN), new(float.NaN, float.NaN) };
        private int _measuredNext;

        public TextLineBox(TextElement element, bool centred)
        {
            _element = element;
            _centred = centred;
            WatchRepaints();
        }

        // Adds the repaint enableRichText is looked for at, again after a pool reset gives the element its type's own
        // painter back.
        public void WatchRepaints()
        {
            var painter = _element.generateVisualContent;
            if (Equals(painter, s_watched))
            {
                return;
            }
            if (Equals(painter, s_plain))
            {
                // A text element's own painter is a static method, so every element holds an equal one, and one
                // combination of it with the watch serves them all without a delegate made per element.
                _element.generateVisualContent = s_watched;
                return;
            }
            _element.generateVisualContent -= s_onRepaint;
            _element.generateVisualContent += s_onRepaint;
        }

        private static readonly Action<MeshGenerationContext> s_onRepaint = mgc => (mgc.visualElement as ILineBoxed)?.LineBox.Repainted();
        private static readonly Action<MeshGenerationContext>? s_plain = new Label().generateVisualContent;
        private static readonly Action<MeshGenerationContext> s_watched =
            (Action<MeshGenerationContext>)Delegate.Combine(s_plain, s_onRepaint);

        // enableRichText changes what the text is shown with and how its lines are spaced, and neither the element's
        // style nor its box, so no event this is handed follows it; the setter repaints the element, which is where it
        // is looked for. The settle that follows writes styles and shows the text again, so it is scheduled rather than
        // run inside the repaint.
        private Action? _richTextChanged;

        private void Repainted()
        {
            if (_element.enableRichText == _richText)
            {
                return;
            }
            _richText = _element.enableRichText;
            _element.schedule.Execute(_richTextChanged ??= RichTextChanged);
        }

        private void RichTextChanged()
        {
            if (_context != null)
            {
                StyleTextEffectResolver.ReapplyElement(_context, _element);
            }
            Settle();
        }

        // The element's enableRichText as the last settle found it.
        private bool _richText = true;

        // The element's own geometry and style events, which it hands over from its HandleEventBubbleUp rather than
        // through callbacks, which every label and button Velvet creates would otherwise register.
        public void Handle(EventBase evt)
        {
            if (evt is CustomStyleResolvedEvent)
            {
                Settle();
                return;
            }
            if (evt is not GeometryChangedEvent || Retag())
            {
                return;
            }
            if (!SizedByItsMeasure())
            {
                Settle();
            }
        }

        // A TextElement's own DoMeasure.
        public delegate Vector2 BaseMeasure(float desiredWidth, VisualElement.MeasureMode widthMode, float desiredHeight,
            VisualElement.MeasureMode heightMode);

        // How far the shown text is raised, in pixels; negative lowers it.
        public float Raise { get; private set; }

        // The shown text without the raise, which is what the element is measured by while it is raised, so that the
        // line count and the box never depend on the raise they decide.
        public string? Unraised => Raise != 0f ? _unraised : null;

        // Whether raw has been laid out in more than one line, and so is shown in the preflight's line height. Another
        // text is measured afresh.
        public bool SpansLines(string? raw)
        {
            if (!string.Equals(raw, _linesOf, StringComparison.Ordinal))
            {
                _linesOf = raw;
                _spansLines = false;
                _retagged = false;
            }
            return _spansLines;
        }

        // The context the element's text is shown in, for an element created with no text effect to apply.
        public void Bind(ReconcilerContext context) => _context = context;

        // Measures the element in line boxes. measure is the element's own measure, which is asked with no height
        // constraint, since a height the generator capped is not a count of its lines.
        public Vector2 Measure(float desiredWidth, VisualElement.MeasureMode widthMode, float desiredHeight,
            VisualElement.MeasureMode heightMode, BaseMeasure measure)
        {
            if (heightMode == VisualElement.MeasureMode.Exactly)
            {
                return measure(desiredWidth, widthMode, desiredHeight, heightMode);
            }
            var lines = Before(_element, this);
            _measuredLineHeight = LineHeightFor(_element);
            _measuredOnce = true;
            var generated = Unraised is { } text
                ? _element.MeasureTextSize(text, desiredWidth, widthMode, float.NaN, VisualElement.MeasureMode.Undefined)
                : measure(desiredWidth, widthMode, float.NaN, VisualElement.MeasureMode.Undefined);
            NoteLines(lines, generated);
            var fit = lines.Fit(generated);
            // The uncapped height is recorded, which is the one a settle compares the box with: a box capped below
            // it is not the size its measure gave it.
            Record(widthMode == VisualElement.MeasureMode.Exactly ? desiredWidth : fit.x, fit.y);
            return heightMode == VisualElement.MeasureMode.AtMost ? new Vector2(fit.x, Mathf.Min(fit.y, desiredHeight)) : fit;
        }

        private static Lines Before(TextElement element, TextLineBox box)
        {
            var lineHeight = LineHeightFor(element);
            if (float.IsNaN(lineHeight))
            {
                return default;
            }
            var single = box.SingleLine(element);
            var tagged = box._tagged && element.enableRichText;
            return new Lines(lineHeight, single, tagged ? lineHeight : box._advance, tagged && box._ownMarkup);
        }

        // The line height an element's lines are measured in, NaN where it has none or no text.
        private static float LineHeightFor(TextElement element)
            => string.IsNullOrEmpty(element.text) ? float.NaN : StyleTextEffectResolver.LineHeightOf(element);

        // The height text takes at a width, in the line boxes of an element Velvet created and in the generator's
        // own lines anywhere else.
        public static float HeightAt(TextElement element, string text, float width)
        {
            var box = (element as ILineBoxed)?.LineBox;
            var lines = box != null ? Before(element, box) : default;
            var generated = element.MeasureTextSize(box?.Unraised ?? text, width, VisualElement.MeasureMode.Exactly, float.NaN,
                VisualElement.MeasureMode.Undefined);
            return lines.Fit(generated).y;
        }

        // The raise the shown text is written with. tagged is whether display carries a line height tag and ownMarkup
        // whether the app's text carries rich text of its own.
        public float Shown(ReconcilerContext context, string display, bool tagged, bool ownMarkup)
        {
            _context = context;
            _tagged = tagged;
            _ownMarkup = ownMarkup;
            if (!string.Equals(display, _unraised, StringComparison.Ordinal))
            {
                // Another text measures differently at every width.
                Array.Fill(_measured, new Vector2(float.NaN, float.NaN));
            }
            _unraised = display;
            return Raise;
        }

        // A pooled element starts its next use unraised, untagged, unowned and with its own alignment.
        public void Reset()
        {
            Raise = 0f;
            _context = null;
            _unraised = null;
            _settledShown = null;
            Array.Fill(_measured, new Vector2(float.NaN, float.NaN));
            _tagged = false;
            _ownMarkup = false;
            _spansLines = false;
            _retagged = false;
            _linesOf = null;
            _shownRaw = null;
            _richText = true;
            _alignment.Release(_element);
            _font?.Release(_element);
        }

        // Settles a laid-out element whose app text has changed, since a text that keeps the element's box raises no
        // geometry event to settle it by. An ancestor's line height reaches the element here too, where no style or
        // geometry event settles it (Given_ALabelUnderANormalLineHeight_When_ItsParentDropsIt_Then_TheLabelIsOnePreflightLineBoxTall).
        public void AfterShown(string raw)
        {
            Remeasure();
            var changed = _shownRaw != null && !string.Equals(raw, _shownRaw, StringComparison.Ordinal);
            _shownRaw = raw;
            if (changed && !_settling)
            {
                Settle();
            }
        }

        // An untagged text the generator wrapped is shown again with the preflight's tag. Read on a geometry change,
        // which follows the measure that found the lines.
        private bool Retag()
        {
            if (!_spansLines || _tagged || _retagged || _context == null || !_element.enableRichText)
            {
                return false;
            }
            _retagged = true;
            _settledShown = null;
            StyleTextEffectResolver.ReapplyElement(_context, _element);
            Settle();
            return true;
        }

        private void NoteLines(in Lines lines, Vector2 generated)
        {
            if (!_tagged && lines.Count(generated) > 1)
            {
                _spansLines = true;
            }
        }

        private void Settle()
        {
            _settling = true;
            try
            {
                SettleBox();
            }
            finally
            {
                _settling = false;
            }
        }

        private void SettleBox()
        {
            _richText = _element.enableRichText;
            Remeasure();
            var lineHeight = StyleTextEffectResolver.LineHeightOf(_element);
            if (float.IsNaN(lineHeight))
            {
                _alignment.Release(_element);
                _font?.Release(_element);
            }
            else if (!_element.enableRichText)
            {
                // Text shown without tags is spaced by its font, whose lines are then the line boxes, so it is placed
                // at the top of the box as the first line box is, and centred in a button.
                (_font ??= new LineBoxFontOwner()).Settle(_element, lineHeight);
                _alignment.Settle(_element, upper: !_centred);
                Raise = 0f;
                return;
            }
            else
            {
                _font?.Release(_element);
                _alignment.Settle(_element);
            }
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
            var lines = Before(_element, this);
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
            NoteLines(lines, generated);
            if (Retag())
            {
                return;
            }
            SetRaise(Mathf.Round(Target(align, lines, content, generated) * 100f) / 100f, context);
        }

        // Asks the layout to measure the element again where its line height is no longer the one it was measured at.
        private void Remeasure()
        {
            if (!_measuredOnce || s_incrementVersion == null || s_layoutChange == null)
            {
                return;
            }
            var lineHeight = LineHeightFor(_element);
            if (lineHeight.Equals(_measuredLineHeight))
            {
                return;
            }
            _measuredLineHeight = lineHeight;
            s_incrementVersion.Invoke(_element, s_layoutChange);
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

        // Records the height the text measured at a width, keyed by the content box's width when the layout sizes the
        // element by it.
        private void Record(float width, float height)
        {
            for (var i = 0; i < _measured.Length; i++)
            {
                if (Mathf.Abs(_measured[i].x - width) < 0.5f)
                {
                    _measured[i] = new Vector2(width, height);
                    return;
                }
            }
            _measured[_measuredNext] = new Vector2(width, height);
            _measuredNext = (_measuredNext + 1) % _measured.Length;
        }

        // The raise that puts the first line's glyphs half a leading below the top of the first line box, which a
        // button centres in its box. Under an upper alignment the raise is not taken above zero.
        private float Target(TextAnchor align, in Lines lines, Rect content, Vector2 generated)
        {
            var spare = content.height - lines.Fit(generated).y;
            if (IsMiddle(align))
            {
                return _centred ? 0f : spare;
            }
            if (align is TextAnchor.UpperLeft or TextAnchor.UpperCenter or TextAnchor.UpperRight)
            {
                return Mathf.Min(0f, -((_centred ? spare * 0.5f : 0f) + lines.Leading * 0.5f));
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
                || !style.unityFontDefinition.Equals(_measuredFont) || style.unityFont != _legacyFont)
            {
                _fontSize = style.fontSize;
                _pixelsPerPoint = pixelsPerPoint;
                _style = style.unityFontStyleAndWeight;
                _measuredFont = style.unityFontDefinition;
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
            private readonly bool _byGenerator;

            // byGenerator takes the height off the generator's own lines, for a text whose markup can make a line other
            // than one line height tall.
            public Lines(float lineHeight, float singleLine, float advance, bool byGenerator)
            {
                LineHeight = lineHeight;
                _singleLine = singleLine;
                _advance = advance;
                _byGenerator = byGenerator;
            }

            public float LineHeight { get; }

            // The line height less the single-line height, or zero where the element has no line height.
            public float Leading => _singleLine > 0f ? LineHeight - _singleLine : 0f;

            // How many lines the generator laid out, read off its height.
            public int Count(Vector2 generated)
                => _advance > 0f ? 1 + Mathf.Max(0, Mathf.RoundToInt((generated.y - _singleLine) / _advance)) : 1;

            // The generator's measure with its height replaced by the line boxes, or unchanged where the element has no
            // line height. Under markup of the app's own, the first line takes the line height and every later one the
            // advance the generator gave it.
            public Vector2 Fit(Vector2 generated)
            {
                if (!(generated.y > 0f) || !(_singleLine > 0f))
                {
                    return generated;
                }
                var height = _byGenerator ? generated.y + Leading : Count(generated) * LineHeight;
                return new Vector2(generated.x, Mathf.Max(0f, height));
            }
        }
    }

    // A text element's alignment written middle-* (or upper-*, for text whose lines are its line boxes) with the
    // horizontal part its rules or its parent give it, since CSS has no vertical alignment inside a line box. The part is read from the rules beneath the inline value
    // (StyleCascade.TextAlign) rather than by clearing it, so a rule outside Velvet's own and a theme's default both
    // reach it. An inline value somebody else wrote is left alone, and where the rules cannot be read
    // the alignment is given back rather than written once and kept.
    internal sealed class MiddleAlignment
    {
        // The alignment this wrote inline, or null while the element's alignment is its own.
        private TextAnchor? _owned;

        // upper writes upper-* rather than middle-*.
        public void Settle(VisualElement element, bool upper = false)
        {
            if (!StyleCascade.ReadsTextAlign)
            {
                Release(element);
                return;
            }
            var inline = element.style.unityTextAlign;
            if (inline.keyword == StyleKeyword.Undefined && inline.value != _owned)
            {
                _owned = null;
                return;
            }
            TextAnchor underlying;
            if (_owned == null)
            {
                underlying = element.resolvedStyle.unityTextAlign;
            }
            else if (StyleCascade.TextAlign(element) is { } cascaded)
            {
                underlying = cascaded;
            }
            else
            {
                return;
            }
            var wanted = upper ? Upper(underlying) : Middle(underlying);
            if (wanted == underlying)
            {
                Release(element);
                return;
            }
            if (_owned == wanted)
            {
                return;
            }
            _owned = wanted;
            element.style.unityTextAlign = wanted;
        }

        public void Release(VisualElement element)
        {
            if (_owned == null)
            {
                return;
            }
            _owned = null;
            element.style.unityTextAlign = StyleKeyword.Null;
        }

        // The middle-* anchor with the horizontal part of align.
        internal static TextAnchor Middle(TextAnchor align) => align switch
        {
            TextAnchor.UpperCenter or TextAnchor.LowerCenter => TextAnchor.MiddleCenter,
            TextAnchor.UpperRight or TextAnchor.LowerRight => TextAnchor.MiddleRight,
            TextAnchor.UpperLeft or TextAnchor.LowerLeft => TextAnchor.MiddleLeft,
            _ => align,
        };

        // The upper-* anchor with the horizontal part of align.
        internal static TextAnchor Upper(TextAnchor align) => align switch
        {
            TextAnchor.MiddleCenter or TextAnchor.LowerCenter => TextAnchor.UpperCenter,
            TextAnchor.MiddleRight or TextAnchor.LowerRight => TextAnchor.UpperRight,
            TextAnchor.MiddleLeft or TextAnchor.LowerLeft => TextAnchor.UpperLeft,
            _ => align,
        };
    }

    // The app's own <voffset> and <line-height> tags inside the ones Velvet writes: the raise is added to each of the
    // app's offsets and opened again after each of them closes, and Velvet's line height is opened again after each of
    // the app's closes (Given_AnOffsetOfTheAppsUnderARaise_When_Shown_Then_TheRaiseIsAddedToItAndOpenedAgainAfterIt,
    // Given_ALineHeightOfTheAppsThatCloses_When_Shown_Then_ThePreflightLineHeightIsOpenedAgainAfterIt). Text inside
    // <noparse> is shown as written, and left so.
    internal static class TextLineBoxMarkup
    {
        private const string NoParse = "noparse";

        // display with each px or em offset of the app's raised by raise pixels and the raise opened again after each
        // of its closing tags. fontSize is the element's, which an em offset and a <size> tag are taken of.
        public static string Raised(string display, float raise, float fontSize)
        {
            var reopen = "<voffset=" + Pixels(raise) + "px>";
            var sb = new StringBuilder(display.Length + 32);
            var sizes = new System.Collections.Generic.List<float>();
            var size = fontSize;
            var copied = 0;
            foreach (var tag in Tags(display))
            {
                var name = tag.Name;
                if (name.Equals("size", StringComparison.OrdinalIgnoreCase))
                {
                    if (tag.Value != null)
                    {
                        sizes.Add(size);
                        size = SizeOf(tag.Value, fontSize, size);
                    }
                    continue;
                }
                if (name.Equals("/size", StringComparison.OrdinalIgnoreCase))
                {
                    if (sizes.Count > 0)
                    {
                        size = sizes[sizes.Count - 1];
                        sizes.RemoveAt(sizes.Count - 1);
                    }
                    continue;
                }
                string? replacement = null;
                var offset = name.Equals("voffset", StringComparison.OrdinalIgnoreCase) ? OffsetOf(tag.Value ?? string.Empty, size) : null;
                if (offset != null)
                {
                    replacement = "<voffset=" + Pixels(offset.Value + raise) + "px>";
                }
                else if (name.Equals("/voffset", StringComparison.OrdinalIgnoreCase))
                {
                    replacement = "</voffset>" + reopen;
                }
                if (replacement != null)
                {
                    sb.Append(display, copied, tag.Start - copied).Append(replacement);
                    copied = tag.End;
                }
            }
            return sb.Append(display, copied, display.Length - copied).ToString();
        }

        // Whether text holds a tag whose name starts with name, in any case. Only the text after each '<' is compared, so
        // a text with none is read once.
        public static bool HasTag(string text, string name)
        {
            var i = text.IndexOf('<');
            while (i >= 0 && string.Compare(text, i + 1, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                i = text.IndexOf('<', i + 1);
            }
            return i >= 0;
        }

        // text with open written again after each of its own closing line height tags.
        public static string Reopened(string text, string open)
        {
            if (!HasTag(text, "/line-height"))
            {
                return text;
            }
            var sb = new StringBuilder(text.Length + 32);
            var copied = 0;
            foreach (var tag in Tags(text))
            {
                if (tag.Name.Equals("/line-height", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append(text, copied, tag.End - copied).Append(open);
                    copied = tag.End;
                }
            }
            return sb.Append(text, copied, text.Length - copied).ToString();
        }

        private static string Pixels(float px) => px.ToString("0.##", CultureInfo.InvariantCulture);

        // An offset in pixels: a bare number or px as written, an em of the font size in effect. Null for a percentage,
        // which the generator refuses, and anything it cannot read.
        private static float? OffsetOf(string value, float size)
        {
            if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                return Number(value.Substring(0, value.Length - 2)) * size;
            }
            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                return null;
            }
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2);
            }
            return Number(value);
        }

        // The font size a <size> tag sets, which the generator takes of the element's own size for a relative one.
        private static float SizeOf(string value, float fontSize, float current)
        {
            if (value.EndsWith("em", StringComparison.OrdinalIgnoreCase))
            {
                return Number(value.Substring(0, value.Length - 2)) is { } em ? fontSize * em : current;
            }
            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                return Number(value.Substring(0, value.Length - 1)) is { } percent ? fontSize * percent / 100f : current;
            }
            var relative = value.StartsWith("+", StringComparison.Ordinal) || value.StartsWith("-", StringComparison.Ordinal);
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 2);
            }
            return Number(value) is { } px ? (relative ? fontSize + px : px) : current;
        }

        private static float? Number(string value)
            => float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

        private readonly struct Tag
        {
            public Tag(int start, int end, string name, string? value)
            {
                Start = start;
                End = end;
                Name = name;
                Value = value;
            }

            public int Start { get; }
            public int End { get; }
            public string Name { get; }
            public string? Value { get; }
        }

        // Each tag outside <noparse>, its name before any '=' and its value after it, unquoted.
        private static System.Collections.Generic.IEnumerable<Tag> Tags(string text)
        {
            var noParse = false;
            for (var i = text.IndexOf('<'); i >= 0 && i < text.Length; i = text.IndexOf('<', i + 1))
            {
                var close = text.IndexOf('>', i + 1);
                if (close < 0)
                {
                    yield break;
                }
                var body = text.Substring(i + 1, close - i - 1);
                var equals = body.IndexOf('=');
                var name = (equals < 0 ? body : body.Substring(0, equals)).Trim();
                if (noParse)
                {
                    if (name.Equals("/" + NoParse, StringComparison.OrdinalIgnoreCase))
                    {
                        noParse = false;
                    }
                    continue;
                }
                if (name.Equals(NoParse, StringComparison.OrdinalIgnoreCase))
                {
                    noParse = true;
                    continue;
                }
                var value = equals < 0 ? null : body.Substring(equals + 1).Trim().Trim('"', '\'');
                yield return new Tag(i, close + 1, name, value);
                i = close;
            }
        }
    }
}
