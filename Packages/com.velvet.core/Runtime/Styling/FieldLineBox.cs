#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // A single-line field's input text in a CSS line box: its text element is UI Toolkit's own, which Velvet cannot
    // measure for, so the line box is written to it as an inline height of the line height plus its own padding and
    // border, and its text is aligned middle-* (MiddleAlignment), which CSS's half-leading places the same. A
    // multiline field's text element shows no rich text, so its lines are spaced by its font instead (LineBoxFont).
    internal sealed class FieldLineBox
    {
        private static readonly ConditionalWeakTable<TextField, FieldLineBox> s_boxes = new();

        private readonly TextField _field;
        private readonly TextElement _text;
        private readonly MiddleAlignment _alignment = new();
        private readonly LineBoxFontOwner _font = new();
        private float? _ownedHeight;

        private FieldLineBox(TextField field, TextElement text)
        {
            _field = field;
            _text = text;
            text.AddToClassList(TextLineBox.MarkerClass);
            text.RegisterCallback<GeometryChangedEvent>(_ => Settle());
            text.RegisterCallback<CustomStyleResolvedEvent>(_ => Settle());
        }

        public static void Attach(TextField field)
        {
            if (field.textEdition is TextElement text)
            {
                s_boxes.Add(field, new FieldLineBox(field, text));
            }
        }

        // A pooled field starts its next use with its input text's own height and alignment.
        public static void Reset(TextField field)
        {
            if (s_boxes.TryGetValue(field, out var box))
            {
                box.Release();
            }
        }

        private void Settle()
        {
            var lineHeight = StyleTextEffectResolver.LineHeightOf(_text);
            if (_field.multiline || float.IsNaN(lineHeight))
            {
                ReleaseLineBox();
                if (_field.multiline)
                {
                    _font.Settle(_text, lineHeight);
                }
                else
                {
                    _font.Release(_text);
                }
                return;
            }
            _font.Release(_text);
            var style = _text.resolvedStyle;
            var height = lineHeight + style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth;
            var inlineHeight = _text.style.height;
            var heightIsOurs = inlineHeight.keyword == StyleKeyword.Null || inlineHeight.value.value == _ownedHeight;
            if (heightIsOurs && _ownedHeight != height)
            {
                _ownedHeight = height;
                _text.style.height = height;
            }
            _alignment.Settle(_text);
        }

        private void Release()
        {
            _font.Release(_text);
            ReleaseLineBox();
        }

        private void ReleaseLineBox()
        {
            if (_ownedHeight != null && _text.style.height.value.value == _ownedHeight)
            {
                _text.style.height = StyleKeyword.Null;
            }
            _ownedHeight = null;
            _alignment.Release(_text);
        }
    }
}
