using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The selected text of a text input, drawn again above the selection highlight. UI Toolkit draws the
    // highlight after the glyphs (TextElement.OnGenerateTextOver), so an opaque selection:bg-* would hide the
    // text it covers, and the engine has no colour for selected text at all.
    //
    // The drawing is a text element laid over the input's content box, holding the input's text with every
    // run but the selected one at zero alpha. It is the input's sibling rather than its child: a child
    // would take the input's measure function away, and the input would stop sizing to its text
    // (SelectionTextOverlayTests holds the input's height across the first selection). So it carries the
    // input's classes and inline translate, and is re-placed when the input's box, its content box or that
    // translate moves. It is created the first time the input holds focus and a
    // non-empty selection, and hidden whenever it does not, so a field nobody is selecting in lays out no
    // second copy of its text. The selection has no public change event, so while the input holds focus
    // each scheduler tick compares the selection, the shown text and the colours with what was last
    // composed, and composes again only when one of them moved; outside focus nothing is scheduled.
    // SelectionTextOverlayTests pins the composed text, the drawing's placement and lifetime, and what an
    // idle tick allocates.
    internal sealed class SelectionTextOverlay
    {
        internal const string ClassName = "velvet-selection-text";

        private readonly TextElement _input;
        private readonly IVisualElementScheduledItem _poll;
        private TextElement? _drawing;
        private Color? _textColor;

        // Where the drawing was last placed from, and what it was last composed from.
        private (Rect Layout, Rect Content, StyleTranslate Translate) _placed;
        private (string Shown, bool Masked, int Start, int End, Color? TextColor, float BaseAlpha) _composed;

        internal SelectionTextOverlay(TextElement input)
        {
            _input = input;
            input.RegisterCallback<FocusInEvent>(OnFocusIn);
            input.RegisterCallback<FocusOutEvent>(OnFocusOut);
            input.RegisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            _poll = input.schedule.Execute(Refresh).Every(0);
            if (!IsFocused())
            {
                _poll.Pause();
            }
        }

        internal void SetTextColor(Color? textColor)
        {
            _textColor = textColor;
            Refresh();
        }

        internal void Detach()
        {
            _poll.Pause();
            _input.UnregisterCallback<FocusInEvent>(OnFocusIn);
            _input.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            _input.UnregisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            _drawing?.RemoveFromHierarchy();
            _drawing = null;
        }

        private bool IsFocused() => _input.focusController?.focusedElement == _input;

        private void OnFocusIn(FocusInEvent _)
        {
            _poll.Resume();
            Refresh();
        }

        private void OnFocusOut(FocusOutEvent _)
        {
            _poll.Pause();
            Hide();
        }

        private void OnInputGeometryChanged(GeometryChangedEvent _)
        {
            if (_drawing != null)
            {
                Place(_drawing);
            }
        }

        private void Refresh()
        {
            var selection = (ITextSelection)_input;
            var start = Mathf.Min(selection.cursorIndex, selection.selectIndex);
            var end = Mathf.Max(selection.cursorIndex, selection.selectIndex);
            if (start == end || !IsFocused())
            {
                Hide();
                return;
            }

            var drawing = _drawing ??= CreateDrawing();
            if (drawing.hierarchy.parent != _input.hierarchy.parent || !Placement().Equals(_placed))
            {
                Place(drawing);
            }

            if (drawing.style.display == DisplayStyle.None)
            {
                drawing.style.display = StyleKeyword.Null;
            }

            var composed = (_input.text ?? string.Empty, ((ITextEdition)_input).isPassword, start, end, _textColor,
                _input.resolvedStyle.color.a);
            if (composed.Equals(_composed))
            {
                return;
            }

            _composed = composed;
            drawing.text = Compose(Displayed(_input), start, end, _textColor, composed.Item6);
        }

        private void Hide()
        {
            if (_drawing != null && _drawing.style.display != DisplayStyle.None)
            {
                _drawing.style.display = DisplayStyle.None;
            }
        }

        private TextElement CreateDrawing()
        {
            var drawing = new TextElement { pickingMode = PickingMode.Ignore };
            drawing.AddToClassList(ClassName);
            drawing.style.position = Position.Absolute;
            drawing.style.marginLeft = drawing.style.marginTop = drawing.style.marginRight = drawing.style.marginBottom = 0f;
            drawing.style.paddingLeft = drawing.style.paddingTop = drawing.style.paddingRight = drawing.style.paddingBottom = 0f;
            Place(drawing);
            return drawing;
        }

        // Beside the input, over its content box in the parent's space; put back there whenever it is no
        // longer the input's sibling.
        private void Place(TextElement drawing)
        {
            var parent = _input.hierarchy.parent;
            if (parent != null && drawing.hierarchy.parent != parent)
            {
                parent.hierarchy.Insert(parent.hierarchy.IndexOf(_input) + 1, drawing);
                foreach (var cls in _input.GetClasses())
                {
                    if (cls != StyleTextInputColors.InputTextClass)
                    {
                        drawing.AddToClassList(cls);
                    }
                }
            }

            _placed = Placement();
            drawing.style.left = _placed.Layout.x + _placed.Content.x;
            drawing.style.top = _placed.Layout.y + _placed.Content.y;
            drawing.style.width = _placed.Content.width;
            drawing.style.height = _placed.Content.height;
            drawing.style.translate = _placed.Translate;
        }

        private (Rect Layout, Rect Content, StyleTranslate Translate) Placement()
            => (_input.layout, _input.contentRect, _input.style.translate);

        // What the input shows: its text, or the mask in its place for a password field.
        internal static string Displayed(TextElement input)
        {
            var edition = (ITextEdition)input;
            var shown = input.text ?? string.Empty;
            return edition.isPassword ? new string(edition.maskChar, shown.Length) : shown;
        }

        // The drawing's rich text: the run [start, end) visible in textColor, or at baseAlpha in the colour it
        // inherits when textColor is null, and the rest at zero alpha. Every '<' of the shown text is wrapped
        // in noparse on its own, so no text the user types can close a noparse span this opened.
        internal static string Compose(string shown, int start, int end, Color? textColor, float baseAlpha)
        {
            start = Mathf.Clamp(start, 0, shown.Length);
            end = Mathf.Clamp(end, start, shown.Length);
            var builder = new StringBuilder(shown.Length + 64);
            builder.Append("<alpha=#00>");
            AppendEscaped(builder, shown, 0, start);
            builder.Append(textColor is { } color
                ? "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">"
                : "<alpha=#" + ((Color32)new Color(0f, 0f, 0f, baseAlpha)).a.ToString("X2") + ">");
            AppendEscaped(builder, shown, start, end);
            builder.Append(textColor.HasValue ? "</color><alpha=#00>" : "<alpha=#00>");
            AppendEscaped(builder, shown, end, shown.Length);
            return builder.ToString();
        }

        private static void AppendEscaped(StringBuilder builder, string shown, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                if (shown[i] == '<')
                {
                    builder.Append("<noparse><</noparse>");
                }
                else
                {
                    builder.Append(shown[i]);
                }
            }
        }
    }
}
