using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Realises caret-*, selection:bg-* and selection:text-* on a text input. None of them has a USS rule: each is
    // read off the class lists of the input's box, its control and their ancestors, nearest first, as CSS
    // inherits caret-color and as Tailwind's selection: reaches a descendant's selection. On one element the
    // class Tailwind emits last wins, which for utilities of one property is the order
    // StyleCandidateOrder.Compare gives, whatever order the classes were added in. A value that does not
    // parse is skipped, as Tailwind emits no rule for it; `-inherit` competes like any value, and winning
    // hands the question to the parent. `caret-current` takes the input's resolved text colour.
    //
    // It runs on the input text element's custom-style event, which the rule _text_input.uss keys on
    // InputTextClass keeps firing on every style pass, and a class change on the control or an ancestor
    // restyles the input below it. Ordering: that event comes after the box's and the control's in the same
    // pass, where the engine writes a colour from --unity-cursor-color / --unity-selection-color, so a
    // utility written here wins over a theme's.
    //
    // A colour a utility wrote and no utility asks for any more goes back to the theme's, which the engine
    // has just written where the box or control declares one, or else to the colour a freshly built field
    // carries. A colour written from a refCallback is left alone while no utility is involved.
    //
    // UI Toolkit draws the selection highlight over the glyphs (TextElement.OnGenerateTextOver), so while
    // either selection utility applies, SelectionTextOverlay draws the selected text again above it.
    // TextInputColorUtilityTests and SelectionTextOverlayTests pin each of these.
    internal static class StyleTextInputColors
    {
        // The class _text_input.uss keys its rule on.
        internal const string InputTextClass = "velvet-text-input";

        private const string CaretPrefix = "caret-";
        private const string SelectionBackgroundPrefix = "selection:bg-";
        private const string SelectionTextPrefix = "selection:text-";
        private const string Inherit = "inherit";
        private const string Current = "current";

        private static readonly CustomStyleProperty<Color> s_cursorColor = new("--unity-cursor-color");
        private static readonly CustomStyleProperty<Color> s_selectionColor = new("--unity-selection-color");
        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();
        private static readonly EventCallback<CustomStyleResolvedEvent> s_onResolved = OnResolved;

        private sealed class State
        {
            public bool CaretWritten;
            public bool SelectionWritten;
            public SelectionTextOverlay? Overlay;
        }

        // Registered for every text-input control Velvet builds, since a variant or an ancestor can bring a
        // utility to any of them later. One static delegate, which the engine registers once per element.
        internal static void Track(VisualElement control)
        {
            if (!StyleInputBoxSurface.IsTextInputControl(control))
            {
                return;
            }

            if (!StyleInputBoxSurface.TryGetBox(control, out var box))
            {
                return;
            }

            var input = box.Q<TextElement>();
            if (input == null)
            {
                return;
            }

            input.AddToClassList(InputTextClass);
            input.RegisterCallback(s_onResolved);
        }

        // A recycled field carries nothing of the last consumer's utilities.
        internal static void Forget(VisualElement control)
        {
            if (s_states.TryGetValue(control, out var state))
            {
                state.Overlay?.Detach();
                s_states.Remove(control);
            }
        }

        private static void OnResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.currentTarget is not TextElement input)
            {
                return;
            }

            var box = BoxOf(input);
            if (box?.hierarchy.parent is not VisualElement control)
            {
                return;
            }

            var state = s_states.GetValue(control, _ => new State());
            var selection = (ITextSelection)input;
            ApplyCaret(state, box, control, input, selection);
            ApplySelection(state, box, control, input, selection);
        }

        // The obsolete per-element colours are what the utilities write: the --unity-cursor-color and
        // --unity-selection-color properties the warning points to are the theme's channel, which a utility
        // has to outrank rather than share.
#pragma warning disable CS0618
        private static void ApplyCaret(State state, VisualElement box, VisualElement control, TextElement input,
            ITextSelection selection)
        {
            if (TryResolve(box, CaretPrefix, input, out var caret))
            {
                selection.cursorColor = caret;
                state.CaretWritten = true;
                return;
            }

            if (!state.CaretWritten)
            {
                return;
            }

            state.CaretWritten = false;
            if (!Declares(box, s_cursorColor) && !Declares(control, s_cursorColor))
            {
                selection.cursorColor = FiberTextFieldPoolHelper.DefaultCursorColor;
            }
        }

        private static void ApplySelection(State state, VisualElement box, VisualElement control, TextElement input,
            ITextSelection selection)
        {
            var hasBackground = TryResolve(box, SelectionBackgroundPrefix, input, out var background);
            var hasText = TryResolve(box, SelectionTextPrefix, input, out var text);
            if (hasBackground)
            {
                selection.selectionColor = background;
                state.SelectionWritten = true;
            }
            else if (state.SelectionWritten)
            {
                state.SelectionWritten = false;
                if (!Declares(box, s_selectionColor) && !Declares(control, s_selectionColor))
                {
                    selection.selectionColor = FiberTextFieldPoolHelper.DefaultSelectionColor;
                }
            }

            if (hasBackground || hasText)
            {
                state.Overlay ??= new SelectionTextOverlay(input);
                state.Overlay.SetTextColor(hasText ? text : null);
                return;
            }

            state.Overlay?.Detach();
            state.Overlay = null;
        }
#pragma warning restore CS0618

        // The nearest element, from the box up, whose class list resolves the utility.
        private static bool TryResolve(VisualElement box, string prefix, TextElement input, out Color color)
        {
            color = default;
            var element = box;
            while (element != null && !Read(element, prefix, input, out color))
            {
                element = element.hierarchy.parent;
            }

            return element != null;
        }

        // False for an element whose class list leaves the question to its parent: none of the utility, only
        // values that do not parse, or `-inherit` winning among those that do.
        private static bool Read(VisualElement element, string prefix, TextElement input, out Color color)
        {
            color = default;
            string? winner = null;
            var inherits = false;
            foreach (var cls in element.GetClasses())
            {
                if (!cls.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                // MUTANT_SURVIVES(equivalent, boundary): a class list holds a class once, and only an identical class compares equal.
                if (winner != null && StyleCandidateOrder.Compare(cls, winner) < 0)
                {
                    continue;
                }

                var suffix = cls.Substring(prefix.Length);
                if (TryValue(suffix, input, out var value))
                {
                    winner = cls;
                    color = value;
                    inherits = suffix == Inherit;
                }
            }

            return winner != null && !inherits;
        }

        private static bool TryValue(string suffix, TextElement input, out Color color)
        {
            color = default;
            if (suffix == Inherit)
            {
                return true;
            }

            if (suffix == Current)
            {
                color = input.resolvedStyle.color;
                return true;
            }

            return StyleColorValueParser.TryParseColorSuffix(suffix, out color);
        }

        private static VisualElement? BoxOf(TextElement input)
        {
            var element = input.hierarchy.parent;
            while (element != null && !element.ClassListContains(StyleChildVariantClass.InputBoxClass))
            {
                element = element.hierarchy.parent;
            }

            return element;
        }

        private static bool Declares(VisualElement element, CustomStyleProperty<Color> property)
            => element.customStyle.TryGetValue(property, out _);
    }
}
