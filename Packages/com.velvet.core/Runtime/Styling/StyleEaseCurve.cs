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

    // A CSS <easing-function> Velvet runs: a cubic-bezier() curve, or steps(n, jump); or a curve mirrored as motion-utils'
    // mirrorEasing mirrors one, which MotionEasing gives backInOut.
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

        // Held as a step count of -1, which no steps() has, so Equals and GetHashCode tell it from the curve itself.
        public static EaseFunction MirrorOf(BezierCurve curve) => new(curve, -1, StepJump.End);

        // An ease-[...] layer carries a curve as x1, y1, x2, y2 and steps as the count, the jump and an x2 of -1, which
        // no curve holds (CSS wants x1 and x2 in 0 to 1).
        public static EaseFunction FromLayer(float v1, float v2, float v3, float v4)
            => v3 < 0f ? OfSteps((int)v1, (StepJump)(int)v2) : new BezierCurve(v1, v2, v3, v4);

        public (float, float, float, float) ToLayer()
            => IsSteps ? (Steps, (float)Jump, -1f, 0f) : (Curve.X1, Curve.Y1, Curve.X2, Curve.Y2);

        public float Evaluate(float t)
        {
            if (Steps < 0)
            {
                // MUTANT_SURVIVES(equivalent, boundary): both halves give 0.5 at 0.5.
                if (t <= 0.5f)
                {
                    return OnCurve(2f * t) / 2f;
                }
                return (2f - OnCurve(2f * (1f - t))) / 2f;
            }
            if (!IsSteps)
            {
                return OnCurve(t);
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

        private float OnCurve(float t) => CubicBezierEvaluator.Evaluate(Curve.X1, Curve.Y1, Curve.X2, Curve.Y2, t);

        public bool Equals(EaseFunction other)
            => Steps == other.Steps && Jump == other.Jump && Curve.X1 == other.Curve.X1 && Curve.Y1 == other.Curve.Y1
                && Curve.X2 == other.Curve.X2 && Curve.Y2 == other.Curve.Y2;

        public override bool Equals(object? obj) => obj is EaseFunction other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Curve.X1, Curve.Y1, Curve.X2, Curve.Y2, Steps, Jump);
    }

    // The cubic-beziers one source gives the ease-in, ease-out and ease-in-out keywords.
    internal readonly struct KeywordCurves
    {
        private readonly BezierCurve _in;
        private readonly BezierCurve _out;
        private readonly BezierCurve _inOut;

        public KeywordCurves(BezierCurve easeIn, BezierCurve easeOut, BezierCurve easeInOut)
        {
            _in = easeIn;
            _out = easeOut;
            _inOut = easeInOut;
        }

        public EaseFunction? For(EasingMode mode)
        {
            switch (mode)
            {
                case EasingMode.EaseIn:
                    return _in;
                case EasingMode.EaseOut:
                    return _out;
                case EasingMode.EaseInOut:
                    return _inOut;
                default:
                    return null;
            }
        }
    }

    // Runs a USS transition along an easing function transition-timing-function cannot name: it takes only an
    // EasingMode. UI Toolkit still runs the transition, with the duration, delay and reversal the element's transition
    // lists give it, per property; this class replaces the curve it eases by once the engine reports the transition
    // running (RunningStyleTransition.Retime).
    //
    // Two sources give an element a function: the ease-[...] layer, which StyleArbitraryValueResolver resolves (and a
    // theme class, ease-snappy for --ease-snappy, resolves to), and the keyword the element's rules resolve, read when
    // the transition starts: ease-in, ease-out and ease-in-out, behind a variant or not, and the ease-in-out each
    // transition-* utility but transition-none bundles, take Tailwind v4's curves (--ease-in, --ease-out, --ease-in-out
    // and --default-transition-timing-function). Both also leave a keyword on the element, so an engine that cannot be
    // reached eases by the keyword closest to the function. A timing function a Motion's tween writes (a play
    // MotionTweenTiming holds) takes MotionEasing's curve for each of its keywords that has one, over both sources;
    // one other code writes inline where the classes give the keyword is the keyword it names; the layer's is written
    // inline too, and is told apart by the keyword it left. The lists a narrowing writes in the rules' place
    // (MotionTweenTiming.Narrow) stand for the rules.
    //
    // The layer outranks the keyword source as an inline value outranks a class.
    internal static class StyleEaseCurve
    {
        // Tailwind v4's --ease-in, --ease-out and --ease-in-out; the last is also --default-transition-timing-function.
        private static readonly KeywordCurves s_tailwind =
            new(new BezierCurve(0.4f, 0f, 1f, 1f), new BezierCurve(0f, 0f, 0.2f, 1f), new BezierCurve(0.4f, 0f, 0.2f, 1f));

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

        // A tween Velvet runs itself, at progress t through its run, unclamped: below 0 is inside its delay, where it shows
        // its start whatever the function, as a steps(n, jump-start) asked at 0 would already have jumped.
        internal static float Evaluate(EaseFunction? function, EasingMode mode, float t)
            => t < 0f ? 0f : function is { } given ? Evaluate(given, t) : UssEasing.Evaluate(mode, t);

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
            public bool Tweened;
            // The repeat of the latest tween NoteTween saw.
            public MotionRepeat TweenRepeat;
            public bool Registered;
            public EaseFunction ShapedFor;
            public Func<float, float>? Shaped;
            public Func<Func<float, float>, Func<float, float>?>? ToShaped;
        }

        private static readonly ConditionalWeakTable<VisualElement, Binding> s_bindings = new();

        private static readonly EventCallback<TransitionRunEvent> s_onRun = OnRun;

        private static readonly Func<Func<float, float>, Func<float, float>?> s_engineToMotion =
            static current => MotionEasing.InPlaceOfEngineCurve(current, accelerated: false);

        private static readonly Func<Func<float, float>, Func<float, float>?> s_engineToAcceleratedMotion =
            static current => MotionEasing.InPlaceOfEngineCurve(current, accelerated: true);

        private static readonly Func<Func<float, float>, Func<float, float>?> s_keep = static _ => null;

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
        // runs itself of the given property.
        internal static EaseFunction? CurveFor(VisualElement element, EasingMode resolved, string? property = null)
        {
            if (MotionTweenTiming.PlaysCurve(element))
            {
                return MotionEasing.FunctionOf(resolved, OnWebAnimations(element, property));
            }
            if (!s_bindings.TryGetValue(element, out var binding))
            {
                return null;
            }
            if (binding.Layer is { } layer)
            {
                return binding.LayerMode == resolved ? layer : null;
            }
            return binding.Classed && MotionTweenTiming.PeekOwn(element).Curve.keyword == StyleKeyword.Null
                ? s_tailwind.For(resolved)
                : null;
        }

        // Whether Framer runs the property on the Web Animations API under the tween timing the element: opacity and filter
        // are among the values accelerated-values.ts names, and the tween's repeat can keep them off it
        // (MotionRepeat.KeepsWebAnimations).
        private static bool OnWebAnimations(VisualElement element, string? property)
            => (property == "opacity" || property == "filter")
                && (!s_bindings.TryGetValue(element, out var binding) || binding.TweenRepeat.KeepsWebAnimations);

        // For an element whose transitions a Motion's tween on that repeat is about to time, which UI Toolkit then runs;
        // listened to until NoteTweenEnded finds no tween left playing on it.
        internal static void NoteTween(VisualElement element, MotionRepeat repeat)
        {
            var binding = s_bindings.GetValue(element, static _ => new Binding());
            binding.Tweened = true;
            binding.TweenRepeat = repeat;
            Sync(element, binding);
        }

        internal static void NoteTweenEnded(VisualElement element)
        {
            if (s_bindings.TryGetValue(element, out var binding))
            {
                binding.Tweened = MotionTweenTiming.Playing(element);
                Sync(element, binding);
            }
        }

        // For an element about to serve another consumer.
        internal static void Release(VisualElement element)
        {
            if (s_bindings.TryGetValue(element, out var binding))
            {
                binding.Layer = null;
                binding.Classed = false;
                binding.Tweened = false;
                Sync(element, binding);
            }
        }

        private static void Sync(VisualElement element, Binding binding)
        {
            var wanted = binding.Layer.HasValue || binding.Classed || binding.Tweened;
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
            var classes = MotionTweenTiming.PlaysCurve(element) ? null : ToShaped(element, binding);
            // Only the transitions the event reports, which start now: one already running keeps the curve it started
            // on, as a CSS transition keeps its timing function. Under a tween each runs by its own keyword, since a
            // tween's PropertyOverrides entries can name several, where the classes and the layer give one function for
            // every transition.
            foreach (var name in evt.stylePropertyNames)
            {
                RunningStyleTransition.Retime(element, name,
                    classes ?? (OnWebAnimations(element, name.ToString()) ? s_engineToAcceleratedMotion : s_engineToMotion));
            }
        }

        // Replaces a curve with the classes' or the layer's function for the keyword every transition of the element
        // resolves, and keeps it where they give none.
        private static Func<Func<float, float>, Func<float, float>?> ToShaped(VisualElement element, Binding binding)
        {
            if (!TryResolvedMode(element, out var mode) || CurveFor(element, mode) is not { } function)
            {
                return s_keep;
            }
            if (binding.Shaped == null || !binding.ShapedFor.Equals(function))
            {
                var shaped = function;
                binding.ShapedFor = function;
                binding.Shaped = t => shaped.Evaluate(t);
                var given = binding.Shaped;
                binding.ToShaped = _ => given;
            }
            return binding.ToShaped!;
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
