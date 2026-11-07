#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Runs a USS transition along a cubic-bezier curve, which transition-timing-function cannot name: it takes only
    // an EasingMode. UI Toolkit still runs the transition, with the duration, delay and reversal the element's
    // transition lists give it, per property; this class replaces the curve it eases by once the engine reports the
    // transition running (RunningStyleTransition.Retime).
    //
    // Two sources give an element a curve: the ease-[cubic-bezier(...)] layer, which StyleArbitraryValueResolver
    // resolves (and a theme class, ease-snappy for --ease-snappy, resolves to), and the bundled ease-in, ease-out
    // and ease-in-out classes, which carry Tailwind v4's curves. Both also leave the nearest EasingMode on the
    // element, so a curve the engine will not give up is eased by the keyword closest to it. A curve is applied only
    // while the element's resolved timing function is still that keyword, since another writer of the list (a Motion's
    // own timing, a variant class) then owns the transition's easing.
    //
    // The layer outranks the bundled classes as an inline value outranks a class.
    internal static class StyleEaseCurve
    {
        // Tailwind v4's --ease-in, --ease-out and --ease-in-out.
        private static readonly BezierCurve s_in = new(0.4f, 0f, 1f, 1f);
        private static readonly BezierCurve s_out = new(0f, 0f, 0.2f, 1f);
        private static readonly BezierCurve s_inOut = new(0.4f, 0f, 0.2f, 1f);

        // The value of an <easing-function> Velvet runs: cubic-bezier() with both x coordinates in [0, 1], as CSS
        // requires, or one of CSS's easing keywords (KeyframeSet.TryParseTiming). `_` reads as a space, as it does in a
        // Tailwind class.
        internal static bool TryParse(ReadOnlySpan<char> value, out BezierCurve curve)
            => KeyframeSet.TryParseTiming(value.ToString().Replace('_', ' ').Trim(), out curve);

        // The curve's eased output at progress t.
        internal static float Evaluate(in BezierCurve curve, float t)
            => CubicBezierEvaluator.Evaluate(curve.X1, curve.Y1, curve.X2, curve.Y2, t);

        private static readonly EasingMode[] s_candidates =
            { EasingMode.Ease, EasingMode.EaseIn, EasingMode.EaseOut, EasingMode.EaseInOut, EasingMode.Linear };

        // The keyword whose curve lies closest to the given one over its length, which is what the transition eases by
        // wherever the curve itself cannot be applied.
        internal static EasingMode NearestMode(in BezierCurve curve)
        {
            var best = EasingMode.Ease;
            var bestError = float.MaxValue;
            foreach (var mode in s_candidates)
            {
                var error = 0f;
                for (var step = 1; step < 10; step++)
                {
                    var t = step / 10f;
                    error = Math.Max(error, Math.Abs(UssEasing.Evaluate(mode, t) - Evaluate(curve, t)));
                }
                if (error < bestError)
                {
                    best = mode;
                    bestError = error;
                }
            }
            return best;
        }

        private sealed class Binding
        {
            public BezierCurve? Layer;
            public EasingMode LayerMode;
            public BezierCurve? Bundled;
            public EasingMode BundledMode;
            public bool Registered;
            public BezierCurve ShapedFor;
            public Func<float, float>? Shaped;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> s_bindings = new();

        private static readonly EventCallback<TransitionRunEvent> s_onRun = OnRun;

        // The curve the winning ease-[...] layer gives the element, or none; mode is the keyword left on the element
        // beside it.
        internal static void SetLayer(VisualElement element, BezierCurve? curve, EasingMode mode)
        {
            if (!curve.HasValue && !s_bindings.TryGetValue(element, out _))
            {
                return;
            }
            var binding = s_bindings.GetValue(element, static _ => new Binding());
            binding.Layer = curve;
            binding.LayerMode = mode;
            Sync(element, binding);
        }

        // The curve the element's bundled ease-* classes give it. The class the bundled stylesheet declares last wins,
        // as it does for the keyword (_state_variants.uss).
        internal static void NoteClasses(VisualElement element, string[] classNames)
        {
            var winner = -1;
            if (classNames != null)
            {
                foreach (var name in classNames)
                {
                    winner = Math.Max(winner, Array.IndexOf(s_bundledOrder, name));
                }
            }
            BezierCurve? curve = null;
            var mode = EasingMode.Ease;
            if (winner >= 0)
            {
                switch (s_bundledOrder[winner])
                {
                    case "ease-in":
                        curve = s_in;
                        mode = EasingMode.EaseIn;
                        break;
                    case "ease-in-out":
                        curve = s_inOut;
                        mode = EasingMode.EaseInOut;
                        break;
                    case "ease-out":
                        curve = s_out;
                        mode = EasingMode.EaseOut;
                        break;
                }
            }
            if (!curve.HasValue && !s_bindings.TryGetValue(element, out _))
            {
                return;
            }
            var binding = s_bindings.GetValue(element, static _ => new Binding());
            binding.Bundled = curve;
            binding.BundledMode = mode;
            Sync(element, binding);
        }

        // The bundled classes in the order _state_variants.uss declares them, so the last one present is the one that
        // applies. ease and ease-linear are there for the rank they hold over the curves declared before them; neither
        // carries a curve.
        private static readonly string[] s_bundledOrder = { "ease", "ease-in", "ease-in-out", "ease-linear", "ease-out" };

        // The curve the element eases its transitions by where they resolve to the keyword it was set against, for a
        // tween Velvet runs itself.
        internal static BezierCurve? CurveFor(VisualElement element, EasingMode resolved)
        {
            if (!s_bindings.TryGetValue(element, out var binding))
            {
                return null;
            }
            if (binding.Layer is { } layer)
            {
                return binding.LayerMode == resolved ? layer : null;
            }
            return binding.Bundled is { } bundled && binding.BundledMode == resolved ? bundled : null;
        }

        // For an element about to serve another consumer.
        internal static void Release(VisualElement element)
        {
            if (s_bindings.TryGetValue(element, out var binding))
            {
                binding.Layer = null;
                binding.Bundled = null;
                Sync(element, binding);
            }
        }

        private static void Sync(VisualElement element, Binding binding)
        {
            var wanted = binding.Layer.HasValue || binding.Bundled.HasValue;
            if (wanted == binding.Registered)
            {
                return;
            }
            binding.Registered = wanted;
            if (wanted)
            {
                element.RegisterCallback(s_onRun);
            }
            else
            {
                element.UnregisterCallback(s_onRun);
            }
        }

        private static void OnRun(TransitionRunEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || !ReferenceEquals(evt.target, element)
                || !s_bindings.TryGetValue(element, out var binding))
            {
                return;
            }
            BezierCurve curve;
            EasingMode mode;
            if (binding.Layer is { } layer)
            {
                (curve, mode) = (layer, binding.LayerMode);
            }
            else if (binding.Bundled is { } bundled)
            {
                (curve, mode) = (bundled, binding.BundledMode);
            }
            else
            {
                return;
            }
            if (!EasesBy(element, mode))
            {
                return;
            }
            if (binding.Shaped == null || !binding.ShapedFor.Equals(curve))
            {
                var shaped = curve;
                binding.ShapedFor = curve;
                binding.Shaped = t => Evaluate(shaped, t);
            }
            RunningStyleTransition.Retime(element, binding.Shaped);
        }

        // Whether every transition the element resolves eases by the keyword.
        private static bool EasesBy(VisualElement element, EasingMode mode)
        {
            var any = false;
            foreach (var function in element.resolvedStyle.transitionTimingFunction)
            {
                if (function.mode != mode)
                {
                    return false;
                }
                any = true;
            }
            return any;
        }
    }
}
