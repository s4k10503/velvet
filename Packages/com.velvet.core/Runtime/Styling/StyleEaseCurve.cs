#nullable enable
using System;
using System.Globalization;
using UnityEngine.UIElements;
using System.Runtime.CompilerServices;

namespace Velvet
{
    // Where a steps() easing function jumps.
    internal enum StepJump
    {
        End,
        Start,
        None,
        Both,
    }

    // A CSS <easing-function> Velvet runs: a cubic-bezier() curve, or steps(n, jump).
    internal readonly struct EaseFunction : IEquatable<EaseFunction>
    {
        public readonly BezierCurve Curve;
        public readonly int Steps;
        public readonly StepJump Jump;

        private EaseFunction(BezierCurve curve, int steps, StepJump jump)
        {
            Curve = curve;
            Steps = steps;
            Jump = jump;
        }

        public bool IsSteps => Steps > 0;

        public static EaseFunction OfSteps(int steps, StepJump jump) => new(default, steps, jump);

        public static implicit operator EaseFunction(BezierCurve curve) => new(curve, 0, StepJump.End);

        // An ease-[...] layer carries a curve as x1, y1, x2, y2 and steps as the count, the jump and an x2 of -1, which
        // no curve holds (CSS wants x1 and x2 in 0 to 1).
        public static EaseFunction FromLayer(float v1, float v2, float v3, float v4)
            => v3 < 0f ? OfSteps((int)v1, (StepJump)(int)v2) : new BezierCurve(v1, v2, v3, v4);

        public (float, float, float, float) ToLayer()
            => IsSteps ? (Steps, (float)Jump, -1f, 0f) : (Curve.X1, Curve.Y1, Curve.X2, Curve.Y2);

        public float Evaluate(float t)
        {
            if (!IsSteps)
            {
                return CubicBezierEvaluator.Evaluate(Curve.X1, Curve.Y1, Curve.X2, Curve.Y2, t);
            }
            if (t >= 1f)
            {
                return 1f;
            }
            var step = (float)Math.Floor(Math.Max(t, 0f) * Steps);
            switch (Jump)
            {
                case StepJump.Start:
                    return Math.Min(1f, (step + 1f) / Steps);
                case StepJump.None:
                    return Math.Min(1f, step / (Steps - 1));
                case StepJump.Both:
                    return (step + 1f) / (Steps + 1f);
                default:
                    return step / Steps;
            }
        }

        public bool Equals(EaseFunction other)
            => Steps == other.Steps && Jump == other.Jump && Curve.X1 == other.Curve.X1 && Curve.Y1 == other.Curve.Y1
                && Curve.X2 == other.Curve.X2 && Curve.Y2 == other.Curve.Y2;

        public override bool Equals(object? obj) => obj is EaseFunction other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Curve.X1, Curve.Y1, Curve.X2, Curve.Y2, Steps, Jump);
    }

    // Runs a USS transition along an easing function transition-timing-function cannot name: it takes only an
    // EasingMode. UI Toolkit still runs the transition, with the duration, delay and reversal the element's transition
    // lists give it, per property; this class replaces the curve it eases by once the engine reports the transition
    // running (RunningStyleTransition.Retime).
    //
    // Two sources give an element a function: the ease-[...] layer, which StyleArbitraryValueResolver resolves (and a
    // theme class, ease-snappy for --ease-snappy, resolves to), and the keyword the element's rules resolve, read when
    // the transition starts: ease-in, ease-out and ease-in-out, behind a variant or not, and the ease-in-out every
    // transition-* utility bundles, take Tailwind v4's curves (--ease-in, --ease-out, --ease-in-out and
    // --default-transition-timing-function). Both also leave a keyword on the element, so an engine that cannot be
    // reached eases by the keyword closest to the function. A timing function written inline (a Motion's own) is the
    // keyword it names; the layer's is written inline too, and is told apart by the keyword it left.
    //
    // The layer outranks the keyword source as an inline value outranks a class.
    internal static class StyleEaseCurve
    {
        // Tailwind v4's --ease-in, --ease-out and --ease-in-out; the last is also --default-transition-timing-function.
        private static readonly BezierCurve s_in = new(0.4f, 0f, 1f, 1f);
        private static readonly BezierCurve s_out = new(0f, 0f, 0.2f, 1f);
        private static readonly BezierCurve s_inOut = new(0.4f, 0f, 0.2f, 1f);

        // The value of an <easing-function> Velvet runs: cubic-bezier() with both x coordinates in [0, 1], as CSS
        // requires, steps(), or one of CSS's easing keywords. `_` reads as a space, as it does in a Tailwind class.
        internal static bool TryParse(ReadOnlySpan<char> value, out EaseFunction function)
        {
            var text = value.ToString().Replace('_', ' ').Trim();
            if (TryParseSteps(text, out function))
            {
                return true;
            }
            if (KeyframeSet.TryParseTiming(text, out var curve))
            {
                function = curve;
                return true;
            }
            return false;
        }

        // steps(n), steps(n, jump-start | jump-end | jump-none | jump-both | start | end), step-start and step-end.
        private static bool TryParseSteps(string text, out EaseFunction function)
        {
            function = default;
            if (text == "step-start" || text == "step-end")
            {
                function = EaseFunction.OfSteps(1, text == "step-start" ? StepJump.Start : StepJump.End);
                return true;
            }
            const string open = "steps(";
            if (!text.StartsWith(open, StringComparison.Ordinal) || text[text.Length - 1] != ')')
            {
                return false;
            }
            var arguments = text.Substring(open.Length, text.Length - open.Length - 1).Split(',');
            if (arguments.Length > 2
                || !int.TryParse(arguments[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                return false;
            }
            var jump = StepJump.End;
            if (arguments.Length == 2 && !TryParseJump(arguments[1].Trim(), out jump))
            {
                return false;
            }
            if (count < (jump == StepJump.None ? 2 : 1))
            {
                return false;
            }
            function = EaseFunction.OfSteps(count, jump);
            return true;
        }

        private static bool TryParseJump(string text, out StepJump jump)
        {
            jump = StepJump.End;
            switch (text)
            {
                case "jump-start":
                case "start":
                    jump = StepJump.Start;
                    return true;
                case "jump-end":
                case "end":
                    return true;
                case "jump-none":
                    jump = StepJump.None;
                    return true;
                case "jump-both":
                    jump = StepJump.Both;
                    return true;
                default:
                    return false;
            }
        }

        // The function's eased output at progress t.
        internal static float Evaluate(in EaseFunction function, float t) => function.Evaluate(t);

        private static readonly EasingMode[] s_candidates =
            { EasingMode.Ease, EasingMode.EaseIn, EasingMode.EaseOut, EasingMode.EaseInOut, EasingMode.Linear };

        // The keyword whose curve lies closest to the given function over its length, which is what the transition eases
        // by wherever the function itself cannot be applied.
        internal static EasingMode NearestMode(in EaseFunction function)
        {
            var best = EasingMode.Ease;
            var bestError = float.MaxValue;
            foreach (var mode in s_candidates)
            {
                var error = 0f;
                for (var step = 1; step < 10; step++)
                {
                    var t = step / 10f;
                    error = Math.Max(error, Math.Abs(UssEasing.Evaluate(mode, t) - function.Evaluate(t)));
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
            public EaseFunction? Layer;
            public EasingMode LayerMode;
            public bool Classed;
            public bool Registered;
            public EaseFunction ShapedFor;
            public Func<float, float>? Shaped;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> s_bindings = new();

        private static readonly EventCallback<TransitionRunEvent> s_onRun = OnRun;

        // The functions this class has given a transition: a transition running on one keeps it, as a CSS transition keeps
        // its timing function until another replaces it.
        private static readonly ConditionalWeakTable<Func<float, float>, object> s_given = new();

        // The function the winning ease-[...] layer gives the element, or none; mode is the keyword left on the element
        // beside it.
        internal static void SetLayer(VisualElement element, EaseFunction? function, EasingMode mode)
        {
            if (!function.HasValue && !s_bindings.TryGetValue(element, out _))
            {
                return;
            }
            var binding = s_bindings.GetValue(element, static _ => new Binding());
            binding.Layer = function;
            binding.LayerMode = mode;
            Sync(element, binding);
        }

        // Whether the element's classes can give it a timing keyword that takes a Tailwind curve: ease-in, ease-out,
        // ease-in-out or a transition-* utility, behind a variant or not.
        internal static void NoteClasses(VisualElement element, string[] classNames)
        {
            var classed = false;
            if (classNames != null)
            {
                foreach (var name in classNames)
                {
                    // A peel leaves a suffix of its class, so a class holding neither word cannot leave one of these.
                    // It is passed over before the peel, which allocates its variants and would run on each patch
                    // (ClassReadAllocationTests).
                    if (name == null || (name.IndexOf("ease-", StringComparison.Ordinal) < 0
                            && name.IndexOf("transition", StringComparison.Ordinal) < 0))
                    {
                        continue;
                    }
                    var leaf = name;
                    while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                    {
                        leaf = payload;
                    }
                    if (leaf == "ease-in" || leaf == "ease-out" || leaf == "ease-in-out"
                        || leaf == "transition" || leaf.StartsWith("transition-", StringComparison.Ordinal))
                    {
                        classed = true;
                        break;
                    }
                }
            }
            if (!classed && !s_bindings.TryGetValue(element, out _))
            {
                return;
            }
            var binding = s_bindings.GetValue(element, static _ => new Binding());
            binding.Classed = classed;
            Sync(element, binding);
        }

        // The function the element's transitions ease by where they resolve to the given keyword, for a tween Velvet
        // runs itself.
        internal static EaseFunction? CurveFor(VisualElement element, EasingMode resolved)
        {
            if (!s_bindings.TryGetValue(element, out var binding))
            {
                return null;
            }
            if (binding.Layer is { } layer)
            {
                return binding.LayerMode == resolved ? layer : null;
            }
            return binding.Classed && element.style.transitionTimingFunction.keyword == StyleKeyword.Null
                ? OfKeyword(resolved)
                : null;
        }

        // Tailwind's curve for a keyword the bundled rules resolve to.
        private static EaseFunction? OfKeyword(EasingMode mode)
        {
            switch (mode)
            {
                case EasingMode.EaseIn:
                    return s_in;
                case EasingMode.EaseOut:
                    return s_out;
                case EasingMode.EaseInOut:
                    return s_inOut;
                default:
                    return null;
            }
        }

        // For an element about to serve another consumer.
        internal static void Release(VisualElement element)
        {
            if (s_bindings.TryGetValue(element, out var binding))
            {
                binding.Layer = null;
                binding.Classed = false;
                Sync(element, binding);
            }
        }

        private static void Sync(VisualElement element, Binding binding)
        {
            var wanted = binding.Layer.HasValue || binding.Classed;
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
                || !s_bindings.TryGetValue(element, out var binding) || !TryResolvedMode(element, out var mode)
                || CurveFor(element, mode) is not { } function)
            {
                return;
            }
            if (binding.Shaped == null || !binding.ShapedFor.Equals(function))
            {
                var shaped = function;
                binding.ShapedFor = function;
                binding.Shaped = t => shaped.Evaluate(t);
                s_given.Add(binding.Shaped, binding);
            }
            RunningStyleTransition.Retime(element, binding.Shaped, static current => s_given.TryGetValue(current, out _));
        }

        // The keyword every transition the element resolves eases by, where there is one.
        private static bool TryResolvedMode(VisualElement element, out EasingMode mode)
        {
            mode = EasingMode.Ease;
            var any = false;
            foreach (var function in element.resolvedStyle.transitionTimingFunction)
            {
                if (any && function.mode != mode)
                {
                    return false;
                }
                mode = function.mode;
                any = true;
            }
            return any;
        }
    }
}
