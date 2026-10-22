using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A colour carried towards what gives it now as one value, with one restart and one reversal decision for the colour
    // rather than one a channel: from where it stands, on the transition the element declares for its property, starting
    // over whenever the target changes, and a change back to where the running transition started shortened by how far
    // that one had got — LayoutIdCarry's rules for a number.
    internal sealed class ColorCarry
    {
        // Stands in for a zero duration, and is added to the time elapsed, so that a change with no transition lands in
        // the pass that sees it.
        private const float MinDurationSec = 1e-4f;

        private Color _from;
        private float _elapsedSec;
        private float _delaySec;
        private float _durationSec;
        private EasingMode _easing;
        private float _eased;
        private Color _reversingStart;
        private float _shortening = 1f;

        public Color Value { get; private set; }
        public Color Target { get; private set; }
        public bool Landed => _elapsedSec >= _delaySec + _durationSec;

        public void Land(Color value)
        {
            Value = _from = Target = value;
            _elapsedSec = _delaySec = _durationSec = 0f;
        }

        // Carries the colour towards target over dtSec more.
        public Color Step(Color target, float dtSec, VisualElement element, string property, string? shorthand)
        {
            if (!Same(target, Target)) Retarget(target, element, property, shorthand);
            _elapsedSec += dtSec;
            var t = Mathf.Clamp01((_elapsedSec - _delaySec + MinDurationSec) / Mathf.Max(_durationSec, MinDurationSec));
            _eased = UssEasing.Evaluate(_easing, t);
            Value = t < 1f ? Color.LerpUnclamped(_from, Target, _eased) : Target;
            return Value;
        }

        private void Retarget(Color target, VisualElement element, string property, string? shorthand)
        {
            var reverses = !Landed && Same(target, _reversingStart);
            _shortening = reverses ? Mathf.Clamp01(Mathf.Abs(1f - (1f - _eased) * _shortening)) : 1f;
            _reversingStart = reverses ? Target : Value;
            (_from, Target, _elapsedSec) = (Value, target, 0f);
            (_durationSec, _delaySec, _easing) = MotionNativeTransitionGuard.Drives(element, property) ? (0f, 0f, EasingMode.Ease)
                : StyleCascade.Transition(element, property, shorthand);
            _durationSec *= _shortening;
            if (_delaySec < 0f) _delaySec *= _shortening;
        }

        private static bool Same(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }
}
