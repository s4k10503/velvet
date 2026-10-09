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
    // run but the selected one at zero alpha. It is created the first time the input holds focus and a
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

        // What the drawing was last composed from.
        private string? _composedFrom;
        private bool _composedMasked;
        private int _composedStart;
        private int _composedEnd;
        private Color? _composedTextColor;
        private float _composedBaseAlpha;

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
                FitToInput(_drawing);
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
            var shown = _input.text ?? string.Empty;
            var masked = ((ITextEdition)_input).isPassword;
            var baseAlpha = _input.resolvedStyle.color.a;
            if (drawing.style.display == DisplayStyle.None)
            {
                drawing.style.display = StyleKeyword.Null;
            }

            if (ReferenceEquals(shown, _composedFrom) && masked == _composedMasked && start == _composedStart && end == _composedEnd
                && _textColor == _composedTextColor && baseAlpha == _composedBaseAlpha)
            {
                return;
            }

            _composedFrom = shown;
            _composedMasked = masked;
            _composedStart = start;
            _composedEnd = end;
            _composedTextColor = _textColor;
            _composedBaseAlpha = baseAlpha;
            drawing.text = Compose(Displayed(_input), start, end, _textColor, baseAlpha);
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
            _input.Add(drawing);
            FitToInput(drawing);
            return drawing;
        }

        // The input's content box, in the input's own space, which an absolute child is placed in.
        private void FitToInput(TextElement drawing)
        {
            var content = _input.contentRect;
            drawing.style.left = content.x;
            drawing.style.top = content.y;
            drawing.style.width = content.width;
            drawing.style.height = content.height;
        }

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
