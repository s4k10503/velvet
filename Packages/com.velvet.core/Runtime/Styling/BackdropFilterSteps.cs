#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // One backdrop-filter function with every length measured and currentcolor read: A is blur's deviation or a
    // shadow's x offset in points, hue-rotate's degrees or an amount; B and C are a shadow's y offset and deviation.
    internal struct BackdropStep
    {
        public BackdropFunction Function;
        public float A;
        public float B;
        public float C;
        public Color Color;
    }

    // What the renderer and the transition read a BackdropSpec as, and CSS's interpolation of two filter lists:
    // functions pair by position, a list shorter than the other is padded at its end with the missing functions at
    // their initial values, and two lists that pair nowhere else do not interpolate.
    internal static class BackdropFilterSteps
    {
        public static BackdropStep[] Resolve(BackdropSpec spec, VisualElement element)
        {
            var steps = new BackdropStep[spec.Ops.Length];
            for (var i = 0; i < steps.Length; i++)
            {
                steps[i] = Resolve(spec.Ops[i], element);
            }
            return steps;
        }

        private static BackdropStep Resolve(in BackdropOp op, VisualElement element)
        {
            if (op.Function == BackdropFunction.DropShadow)
            {
                return ResolveShadow(op.Shadow, element);
            }
            var amount = op.Amount;
            if (op.Measured != null)
            {
                amount = StyleRelativeLengths.TryMeasure(element, op.Measured, out var measured) ? Mathf.Max(0f, measured) : 0f;
            }
            return new BackdropStep { Function = op.Function, A = amount };
        }

        private static BackdropStep ResolveShadow(in DropShadowLayer shadow, VisualElement element)
        {
            var step = new BackdropStep
            {
                Function = BackdropFunction.DropShadow,
                A = Measure(element, shadow, 0, shadow.X),
                B = Measure(element, shadow, 1, shadow.Y),
                C = Mathf.Max(0f, Measure(element, shadow, 2, shadow.Deviation)),
                Color = shadow.Color,
            };
            // currentcolor, its alpha a factor on the element's colour (DropShadowLayer.CurrentColor).
            if (float.IsNaN(shadow.Color.r))
            {
                var color = element.resolvedStyle.color;
                step.Color = new Color(color.r, color.g, color.b, color.a * shadow.Color.a);
            }
            return step;
        }

        private static float Measure(VisualElement element, in DropShadowLayer shadow, int index, float literal)
            => shadow.Measured?[index] is { } expression
                ? StyleRelativeLengths.TryMeasure(element, expression, out var px) ? px : 0f
                : literal;

        // The value a function takes when it is absent from one side of an interpolation.
        public static BackdropStep Neutral(BackdropFunction function)
            => new()
            {
                Function = function,
                A = function is BackdropFunction.Brightness or BackdropFunction.Contrast or BackdropFunction.Opacity
                    or BackdropFunction.Saturate ? 1f : 0f,
                Color = Color.clear,
            };

        public static bool TryPair(BackdropStep[] from, BackdropStep[] to, out BackdropStep[] pairedFrom,
            out BackdropStep[] pairedTo)
        {
            var length = Math.Max(from.Length, to.Length);
            pairedFrom = new BackdropStep[length];
            pairedTo = new BackdropStep[length];
            for (var i = 0; i < length; i++)
            {
                var longer = i < from.Length ? from[i] : to[i];
                pairedFrom[i] = i < from.Length ? from[i] : Neutral(longer.Function);
                pairedTo[i] = i < to.Length ? to[i] : Neutral(longer.Function);
                if (pairedFrom[i].Function != pairedTo[i].Function)
                {
                    return false;
                }
            }
            return true;
        }

        // Colours mix premultiplied, as CSS interpolates them.
        public static BackdropStep[] Lerp(BackdropStep[] from, BackdropStep[] to, float t)
        {
            var steps = new BackdropStep[from.Length];
            for (var i = 0; i < steps.Length; i++)
            {
                var a = from[i];
                var b = to[i];
                var alpha = Mathf.LerpUnclamped(a.Color.a, b.Color.a, t);
                var premultiplied = Vector3.LerpUnclamped(Premultiplied(a.Color), Premultiplied(b.Color), t);
                steps[i] = new BackdropStep
                {
                    Function = a.Function,
                    A = Mathf.LerpUnclamped(a.A, b.A, t),
                    B = Mathf.LerpUnclamped(a.B, b.B, t),
                    C = Mathf.LerpUnclamped(a.C, b.C, t),
                    Color = alpha > 0f ? new Color(premultiplied.x / alpha, premultiplied.y / alpha, premultiplied.z / alpha, alpha)
                        : Color.clear,
                };
            }
            return steps;
        }

        private static Vector3 Premultiplied(Color color) => new(color.r * color.a, color.g * color.a, color.b * color.a);

        public static bool Same(BackdropStep[] a, BackdropStep[] b)
        {
            var same = a.Length == b.Length;
            for (var i = 0; same && i < a.Length; i++)
            {
                same = Same(a[i], b[i]);
            }
            return same;
        }

        private static bool Same(in BackdropStep x, in BackdropStep y)
            => x.Function == y.Function && Near(x.A, y.A) && Near(x.B, y.B) && Near(x.C, y.C) && x.Color == y.Color;

        private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 1e-4f;
    }

    // A running transition of a backdrop-filter, on CSS's timing: a delay, a duration eased by the entry's curve, and
    // the reversing shortening of a change back to where the transition started.
    internal sealed class BackdropTransition
    {
        public BackdropStep[] From = Array.Empty<BackdropStep>();
        public BackdropStep[] To = Array.Empty<BackdropStep>();
        public BackdropStep[] ReversingStart = Array.Empty<BackdropStep>();
        public MotionClock Clock = MotionClock.Realtime;
        public double StartSec;
        public float DurationSec;
        public float DelaySec;
        public float ReversingFactor = 1f;
        public EasingMode Easing;

        public float Progress
            => Mathf.Clamp01((float)((Clock.NowSec - StartSec - DelaySec) / Math.Max(DurationSec, 1e-6)));

        public float Eased => UssEasing.Evaluate(Easing, Progress);

        public BackdropStep[] Now => BackdropFilterSteps.Lerp(From, To, Eased);
    }
}
