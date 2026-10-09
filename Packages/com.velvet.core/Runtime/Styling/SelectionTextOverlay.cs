using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The selected text of a text input, drawn again above the selection highlight. UI Toolkit draws the
    // highlight after the glyphs (TextElement.OnGenerateTextOver), so an opaque selection:bg-* would hide the
    // text it covers, and the engine has no colour for selected text at all. A browser paints ::selection
    // behind the glyphs and in their ::selection colour; this draws the same picture from above.
    //
    // It is a child of the input's own text element, laid over that element's content box, holding the same
    // text and inheriting its text styles. It shows that text through rich text: the selected run in the
    // selection text colour (or the inherited colour), every other run at zero alpha. The input's value and
    // its own text are never touched.
    //
    // The selection has no public change event, so the overlay compares what it would show on each scheduler
    // tick and on every style pass StyleTextInputColors sees, and rewrites its text only when that differs.
    // SelectionTextOverlayTests pins the composed text, the overlay's placement and its lifetime.
    internal sealed class SelectionTextOverlay : TextElement
    {
        internal const string ClassName = "velvet-selection-text";

        private readonly TextElement _input;
        private readonly IVisualElementScheduledItem? _poll;
        private Color? _textColor;

        internal SelectionTextOverlay(TextElement input)
        {
            _input = input;
            AddToClassList(ClassName);
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.marginLeft = style.marginTop = style.marginRight = style.marginBottom = 0f;
            style.paddingLeft = style.paddingTop = style.paddingRight = style.paddingBottom = 0f;
            input.Add(this);
            input.RegisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            _poll = schedule.Execute(Refresh).Every(0);
            FitToInput();
        }

        internal void SetTextColor(Color? textColor)
        {
            _textColor = textColor;
            Refresh();
        }

        internal void Detach()
        {
            _poll?.Pause();
            _input.UnregisterCallback<GeometryChangedEvent>(OnInputGeometryChanged);
            RemoveFromHierarchy();
        }

        private void OnInputGeometryChanged(GeometryChangedEvent _) => FitToInput();

        // The input's content box, in the input's own space, which an absolute child is placed in.
        private void FitToInput()
        {
            var content = _input.contentRect;
            style.left = content.x;
            style.top = content.y;
            style.width = content.width;
            style.height = content.height;
        }

        private void Refresh()
        {
            var selection = (ITextSelection)_input;
            var focused = _input.focusController?.focusedElement == _input;
            var start = Mathf.Min(selection.cursorIndex, selection.selectIndex);
            var end = Mathf.Max(selection.cursorIndex, selection.selectIndex);
            var composed = Compose(Displayed(_input), focused ? start : 0, focused ? end : 0, _textColor);
            if (text != composed)
            {
                text = composed;
            }
        }

        // What the input shows: its text, or the mask in its place for a password field.
        internal static string Displayed(TextElement input)
        {
            var edition = (ITextEdition)input;
            var shown = input.text ?? string.Empty;
            return edition.isPassword ? new string(edition.maskChar, shown.Length) : shown;
        }

        // The overlay's rich text: the run [start, end) visible in textColor (or the inherited colour when null),
        // the rest at zero alpha. Every '<' of the shown text is wrapped in noparse.
        internal static string Compose(string shown, int start, int end, Color? textColor)
        {
            start = Mathf.Clamp(start, 0, shown.Length);
            end = Mathf.Clamp(end, start, shown.Length);
            var builder = new StringBuilder(shown.Length + 64);
            builder.Append("<alpha=#00>");
            AppendEscaped(builder, shown, 0, start);
            builder.Append(textColor is { } color ? "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">" : "<alpha=#FF>");
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
