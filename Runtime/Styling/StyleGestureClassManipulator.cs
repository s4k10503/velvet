using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Manipulator that toggles CSS classes dynamically in response to pointer / focus events,
    // driving the whileHover / whileTap / whileFocus gesture classes.
    // whileFocus uses the element's own FocusEvent / BlurEvent
    // (only focusable elements — buttons, fields — ever fire them).
    // Note: in multi-touch environments, only the first pointer is tracked (single-pointer assumption).
    // Hover uses the bubbling PointerOverEvent/PointerOutEvent pair (which trickle
    // down and bubble up) so a pointer over a descendant still counts as hovering this element, matching CSS
    // :hover; the non-bubbling PointerEnter/PointerLeave only fire on the element itself.
    // PointerOut clears hover only once the pointer leaves this element's bounds.
    internal sealed class StyleGestureClassManipulator : Manipulator
    {
        private string[] _hoverClasses;
        private string[] _tapClasses;
        private string[] _focusClasses;
        private bool _isHovered;
        private bool _isTapped;
        private bool _isFocused;

        public StyleGestureClassManipulator(string[] hoverClasses, string[] tapClasses, string[] focusClasses)
        {
            _hoverClasses = hoverClasses ?? Array.Empty<string>();
            _tapClasses = tapClasses ?? Array.Empty<string>();
            _focusClasses = focusClasses ?? Array.Empty<string>();
        }

        public void UpdateClasses(string[] hoverClasses, string[] tapClasses, string[] focusClasses)
        {
            var oldHover = _hoverClasses;
            var oldTap = _tapClasses;
            var oldFocus = _focusClasses;
            _hoverClasses = hoverClasses ?? Array.Empty<string>();
            _tapClasses = tapClasses ?? Array.Empty<string>();
            _focusClasses = focusClasses ?? Array.Empty<string>();

            if (target == null)
            {
                return;
            }

            if (_isHovered)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldHover);
                StyleAnimationClassUtils.AddClasses(target, _hoverClasses);
            }

            if (_isTapped)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldTap);
                StyleAnimationClassUtils.AddClasses(target, _tapClasses);
            }

            if (_isFocused)
            {
                StyleAnimationClassUtils.RemoveClasses(target, oldFocus);
                StyleAnimationClassUtils.AddClasses(target, _focusClasses);
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerOverEvent>(OnPointerOver);
            target.RegisterCallback<PointerOutEvent>(OnPointerOut);
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<FocusEvent>(OnFocus);
            target.RegisterCallback<BlurEvent>(OnBlur);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            if (_isHovered)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _hoverClasses);
            }

            if (_isTapped)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _tapClasses);
            }

            if (_isFocused)
            {
                StyleAnimationClassUtils.RemoveClasses(target, _focusClasses);
            }

            _isHovered = false;
            _isTapped = false;
            _isFocused = false;

            target.UnregisterCallback<PointerOverEvent>(OnPointerOver);
            target.UnregisterCallback<PointerOutEvent>(OnPointerOut);
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<FocusEvent>(OnFocus);
            target.UnregisterCallback<BlurEvent>(OnBlur);
        }

        private void OnPointerOver(PointerOverEvent evt)
        {
            if (_isHovered)
            {
                return;
            }

            _isHovered = true;
            StyleAnimationClassUtils.AddClasses(target, _hoverClasses);
        }

        private void OnPointerOut(PointerOutEvent evt)
        {
            // PointerOut bubbles; while the pointer merely crosses between descendants it is still inside us,
            // so only treat it as a real leave once the pointer is outside our bounds.
            if (target.worldBound.Contains(evt.position))
            {
                return;
            }

            if (_isHovered)
            {
                _isHovered = false;
                StyleAnimationClassUtils.RemoveClasses(target, _hoverClasses);
            }

            if (_isTapped)
            {
                _isTapped = false;
                StyleAnimationClassUtils.RemoveClasses(target, _tapClasses);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (_isTapped)
            {
                return;
            }

            _isTapped = true;
            StyleAnimationClassUtils.AddClasses(target, _tapClasses);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_isTapped)
            {
                return;
            }

            _isTapped = false;
            StyleAnimationClassUtils.RemoveClasses(target, _tapClasses);
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (_isTapped)
            {
                _isTapped = false;
                StyleAnimationClassUtils.RemoveClasses(target, _tapClasses);
            }
        }

        private void OnFocus(FocusEvent evt)
        {
            if (_isFocused)
            {
                return;
            }

            _isFocused = true;
            StyleAnimationClassUtils.AddClasses(target, _focusClasses);
        }

        private void OnBlur(BlurEvent evt)
        {
            if (!_isFocused)
            {
                return;
            }

            _isFocused = false;
            StyleAnimationClassUtils.RemoveClasses(target, _focusClasses);
        }
    }
}
