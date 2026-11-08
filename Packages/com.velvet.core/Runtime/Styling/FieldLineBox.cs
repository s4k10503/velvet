#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A single-line field's input text in a CSS line box: its text element is UI Toolkit's own, which Velvet cannot
    // measure for, so the line box is written to it as an inline height of the line height plus its own padding and
    // border, and its text is aligned middle-* with the horizontal part it resolves, which CSS's half-leading places
    // the same. A multiline field is left in the font's own lines: its text element shows no rich text, so nothing
    // spaces its lines by the line height.
    internal sealed class FieldLineBox
    {
        private readonly TextField _field;
        private readonly TextElement _text;
        private float? _ownedHeight;
        private TextAnchor? _ownedAlign;

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
                _ = new FieldLineBox(field, text);
            }
        }

        private void Settle()
        {
            var lineHeight = StyleTextEffectResolver.LineHeightOf(_text);
            if (_field.multiline || float.IsNaN(lineHeight))
            {
                Release();
                return;
            }
            var style = _text.resolvedStyle;
            var height = lineHeight + style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth;
            var inlineHeight = _text.style.height;
            var heightIsOurs = inlineHeight.keyword == StyleKeyword.Null || inlineHeight.value.value == _ownedHeight;
            if (heightIsOurs && _ownedHeight != height)
            {
                _ownedHeight = height;
                _text.style.height = height;
            }
            var inlineAlign = _text.style.unityTextAlign;
            if (inlineAlign.keyword == StyleKeyword.Undefined && inlineAlign.value != _ownedAlign)
            {
                return;
            }
            var align = style.unityTextAlign;
            var wanted = TextLineBox.Middle(align);
            if (wanted != align)
            {
                _ownedAlign = wanted;
                _text.style.unityTextAlign = wanted;
            }
        }

        private void Release()
        {
            if (_ownedHeight != null && _text.style.height.value.value == _ownedHeight)
            {
                _text.style.height = StyleKeyword.Null;
            }
            _ownedHeight = null;
            if (_ownedAlign != null && _text.style.unityTextAlign.value == _ownedAlign)
            {
                _text.style.unityTextAlign = StyleKeyword.Null;
            }
            _ownedAlign = null;
        }
    }
}
