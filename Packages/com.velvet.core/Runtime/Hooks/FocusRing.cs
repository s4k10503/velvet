#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Focus state of the element carrying <see cref="Ref"/>, returned by <c>Hooks.UseFocusRing</c>.
    /// Rides the same focus-visible heuristic as the <c>focus-visible:</c> styling variant, so the two
    /// surfaces cannot drift.
    /// </summary>
    public readonly struct FocusRing
    {
        /// <summary>True while the element holds focus, from any input modality.</summary>
        public bool IsFocused { get; }

        /// <summary>
        /// True while the element holds focus that did not follow a pointer press in its panel — keyboard,
        /// gamepad (navigation-event-driven), or a programmatic focus after a key press or navigation move
        /// or before any input.
        /// </summary>
        public bool IsFocusVisible { get; }

        /// <summary>
        /// Attach point: pass as the <c>refCallback:</c> argument of any <c>V.*</c> factory. The returned
        /// cleanup detaches the listeners (the standard refCallback contract).
        /// </summary>
        public Func<VisualElement, Action> Ref { get; }

        internal FocusRing(bool isFocused, bool isFocusVisible, Func<VisualElement, Action> @ref)
        {
            IsFocused = isFocused;
            IsFocusVisible = isFocusVisible;
            Ref = @ref;
        }
    }
}
