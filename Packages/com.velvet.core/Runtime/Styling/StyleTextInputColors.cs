using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Realises caret-*, selection:bg-* and selection:text-* on a text input. None of them has a USS rule: each is
    // read off the class lists of the input's box, its control and their ancestors. Among classes that
    // compete, the one Tailwind emits last wins, which for utilities of one property is the order
    // StyleCandidateOrder.Compare gives, whatever order the classes were added in. A value that does not
    // parse is skipped, as Tailwind emits no rule for it, and `-inherit` competes like any value.
    // - caret-color inherits, so the nearest element carrying a caret-* decides, among its own classes;
    //   `-inherit` winning there passes the question to its parent.
    // - selection:* is `& *::selection, &::selection`, which reaches the input's selection from every
    //   element carrying one at one specificity, so all of them compete at once; `-inherit` winning takes
    //   the parent's selection, which every element from the next one up reaches.
    // `caret-current` takes the input's resolved text colour, scaled by an opacity modifier.
    //
    // It runs on the input text element's custom-style event, which the rule _text_input.uss keys on
    // InputTextClass keeps firing on every style pass, and a class change on the control or an ancestor
    // restyles the input below it. Ordering: that event comes after the box's and the control's in the same
    // pass, where the engine writes a colour from --unity-cursor-color / --unity-selection-color, so a
    // utility written here wins over a theme's.
    //
    // A colour a utility wrote and no utility asks for any more goes back to the theme's, read off the box's
    // resolved custom style or else the control's, or else to the colour a freshly built field carries. It is
    // written here rather than left to the engine, which was measured leaving a utility's colour in place
    // when the theme sat on the control and the utility left the box; TextInputColorUtilityTests holds a
    // theme on each. A colour written from a refCallback is left alone while no utility is involved.
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
            if (TryResolveCaret(box, input, out var caret))
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
            selection.cursorColor = ThemeOr(box, control, s_cursorColor, FiberTextFieldPoolHelper.DefaultCursorColor);
        }

        private static void ApplySelection(State state, VisualElement box, VisualElement control, TextElement input,
            ITextSelection selection)
        {
            var hasBackground = TryResolveSelection(box, SelectionBackgroundPrefix, input, out var background);
            var hasText = TryResolveSelection(box, SelectionTextPrefix, input, out var text);
            if (hasBackground)
            {
                selection.selectionColor = background;
                state.SelectionWritten = true;
            }
            else if (state.SelectionWritten)
            {
                state.SelectionWritten = false;
                selection.selectionColor = ThemeOr(box, control, s_selectionColor, FiberTextFieldPoolHelper.DefaultSelectionColor);
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

        private struct Winner
        {
            public string? Class;
            public Color Color;
            public bool Inherits;
        }

        private static bool TryResolveCaret(VisualElement box, TextElement input, out Color color)
        {
            for (var element = box; element != null; element = element.hierarchy.parent)
            {
                var winner = default(Winner);
                Consider(element, CaretPrefix, input, ref winner);
                if (winner.Class != null && !winner.Inherits)
                {
                    color = winner.Color;
                    return true;
                }
            }

            color = default;
            return false;
        }

        private static bool TryResolveSelection(VisualElement box, string prefix, TextElement input, out Color color)
        {
            for (var start = box; start != null; start = start.hierarchy.parent)
            {
                var winner = default(Winner);
                for (var element = start; element != null; element = element.hierarchy.parent)
                {
                    Consider(element, prefix, input, ref winner);
                }

                if (winner.Class == null)
                {
                    break;
                }

                if (!winner.Inherits)
                {
                    color = winner.Color;
                    return true;
                }
            }

            color = default;
            return false;
        }

        private static void Consider(VisualElement element, string prefix, TextElement input, ref Winner winner)
        {
            foreach (var cls in element.GetClasses())
            {
                if (!cls.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                // MUTANT_SURVIVES(equivalent, boundary): only an identical class compares equal, and it resolves to the same value.
                if (winner.Class != null && StyleCandidateOrder.Compare(cls, winner.Class) < 0)
                {
                    continue;
                }

                var suffix = cls.Substring(prefix.Length);
                if (TryValue(suffix, input, out var value))
                {
                    winner.Class = cls;
                    winner.Color = value;
                    winner.Inherits = suffix == Inherit;
                }
            }
        }

        private static bool TryValue(string suffix, TextElement input, out Color color)
        {
            color = default;
            if (suffix == Inherit)
            {
                return true;
            }

            if (!suffix.StartsWith(Current, System.StringComparison.Ordinal))
            {
                return StyleColorValueParser.TryParseColorSuffix(suffix, out color);
            }

            color = input.resolvedStyle.color;
            if (suffix.Length == Current.Length)
            {
                return true;
            }

            if (suffix[Current.Length] != '/')
            {
                return false;
            }

            if (!StyleColorValueParser.TryParseAlphaModifier(System.MemoryExtensions.AsSpan(suffix, Current.Length + 1), out var alpha))
            {
                return false;
            }

            color.a *= alpha;
            return true;
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

        private static Color ThemeOr(VisualElement box, VisualElement control, CustomStyleProperty<Color> property,
            Color fallback)
        {
            if (box.customStyle.TryGetValue(property, out var fromBox))
            {
                return fromBox;
            }

            if (control.customStyle.TryGetValue(property, out var fromControl))
            {
                return fromControl;
            }

            return fallback;
        }
    }
}
