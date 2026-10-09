using UnityEngine.UIElements;

namespace Velvet
{
    // Configures the element's StyleGestureClassManipulator from the whileHover/whileTap/whileFocus class
    // strings — the discrete-state variant layer, distinct from the paint/wrapper effect layers above.
    internal sealed class FiberGestureApplier
    {
        private readonly ReconcilerContext _ctx;

        public FiberGestureApplier(ReconcilerContext ctx)
        {
            _ctx = ctx;
        }

        // Configures (creates / updates / removes) the element's StyleGestureClassManipulator from the
        // whileHover/whileTap/whileFocus class strings. Creates one when gesture classes are present and none
        // exists, updates the existing one's classes when present, and removes it (clearing the tracking entry)
        // once all three class strings are empty.
        internal void ApplyGestureManipulator(VisualElement element, string? whileHoverClass, string? whileTapClass, string? whileFocusClass)
        {
            var hoverClasses = V.ParseClassNames(whileHoverClass);
            var tapClasses = V.ParseClassNames(whileTapClass);
            var focusClasses = V.ParseClassNames(whileFocusClass);
            // A field's surface classes follow its hover, press and focus on the box, as hover: and focus: do
            // (StyleInputBoxSurface); the rest stay on the control.
            if (StyleInputBoxSurface.IsControl(element) && StyleInputBoxSurface.TryGetBox(element, out var box))
            {
                Configure(box, StyleInputBoxSurface.Surface(hoverClasses), StyleInputBoxSurface.Surface(tapClasses),
                    StyleInputBoxSurface.Surface(focusClasses));
                hoverClasses = StyleInputBoxSurface.NotSurface(hoverClasses);
                tapClasses = StyleInputBoxSurface.NotSurface(tapClasses);
                focusClasses = StyleInputBoxSurface.NotSurface(focusClasses);
            }
            Configure(element, hoverClasses, tapClasses, focusClasses);
        }

        private void Configure(VisualElement element, string[] hoverClasses, string[] tapClasses, string[] focusClasses)
        {
            var hasGesture = hoverClasses.Length > 0 || tapClasses.Length > 0 || focusClasses.Length > 0;

            if (_ctx.GestureManipulators.TryGetValue(element, out var existing))
            {
                if (hasGesture)
                {
                    existing.UpdateClasses(hoverClasses, tapClasses, focusClasses);
                }
                else
                {
                    element.RemoveManipulator(existing);
                    _ctx.GestureManipulators.Remove(element);
                }
            }
            else if (hasGesture)
            {
                var manipulator = new StyleGestureClassManipulator(hoverClasses, tapClasses, focusClasses);
                element.AddManipulator(manipulator);
                _ctx.GestureManipulators[element] = manipulator;
            }
        }
    }
}
