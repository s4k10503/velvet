#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The inline rotate and scale an element's writers intend, kept apart from what the slots hold. While a skew-*
    // class is on the element the two slots carry the composite of rotate, scale and skew (SkewTransformFactor), so
    // a writer states its value here and the slots are rewritten from it; with no skew they are written as given,
    // which is all an element without one ever does.
    internal static class AuthoredTransform
    {
        private sealed class State
        {
            public float SkewXDeg;
            public float SkewYDeg;
            public StyleRotate Rotate;
            public StyleScale Scale;
        }

        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();

        // The rotate the element's writers intend: the inline slot where nothing is composed into it, and otherwise
        // the value held apart from the composite. A Null keyword means no writer has set one.
        public static StyleRotate Rotate(VisualElement element)
            => s_states.TryGetValue(element, out var state) ? state.Rotate : element.style.rotate;

        public static StyleScale Scale(VisualElement element)
            => s_states.TryGetValue(element, out var state) ? state.Scale : element.style.scale;

        public static void SetRotate(VisualElement element, StyleRotate value)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                element.style.rotate = value;
                return;
            }
            state.Rotate = value;
            Write(element, state);
        }

        public static void SetScale(VisualElement element, StyleScale value)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                element.style.scale = value;
                return;
            }
            state.Scale = value;
            Write(element, state);
        }

        // Starts composing the skew into the slots, taking what they hold now as what the writers intend. Called again
        // for the same element it moves the angles.
        public static void Engage(VisualElement element, float xDeg, float yDeg)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                state = new State { Rotate = element.style.rotate, Scale = element.style.scale };
                s_states.Add(element, state);
            }
            (state.SkewXDeg, state.SkewYDeg) = (xDeg, yDeg);
            Write(element, state);
        }

        // Puts the slots back to what the writers intend. A pooled element's reset calls it too, to drop the record
        // before it nulls the slots.
        public static void Release(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                return;
            }
            s_states.Remove(element);
            element.style.rotate = state.Rotate;
            element.style.scale = state.Scale;
        }

        private static void Write(VisualElement element, State state)
        {
            ComposedSlots(state, out var rotate, out var scale);
            element.style.rotate = rotate;
            element.style.scale = scale;
        }

        // R(theta) * diag(sx, sy) * skewX(x) skewY(y), the order CSS applies the individual `rotate` and `scale`
        // properties around `transform`, as the slots that draw it. A rotation about an axis other than z is read by
        // its angle alone, as the 2D rotate utilities are the only writers.
        private static void ComposedSlots(State state, out Rotate rotate, out Scale scale)
        {
            var theta = RotateRadians(state.Rotate);
            var drawnScale = ScaleAxes(state.Scale);
            var tx = Mathf.Tan(state.SkewXDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(state.SkewYDeg * Mathf.Deg2Rad);
            var cos = Mathf.Cos(theta);
            var sin = Mathf.Sin(theta);
            var p00 = cos * drawnScale.x;
            var p01 = -sin * drawnScale.y;
            var p10 = sin * drawnScale.x;
            var p11 = cos * drawnScale.y;
            var k00 = 1f + (tx * ty);
            if (!SkewTransformFactor.TryFactor((p00 * k00) + (p01 * ty), (p00 * tx) + p01,
                    (p10 * k00) + (p11 * ty), (p10 * tx) + p11, out var rotation, out var factor))
            {
                (rotate, scale) = (new Rotate(new Angle(0f, AngleUnit.Degree)), new Scale(new Vector3(0f, 0f, 1f)));
                return;
            }
            rotation.ToAngleAxis(out var angle, out var axis);
            rotate = new Rotate(new Angle(angle, AngleUnit.Degree), axis);
            scale = new Scale(new Vector3(factor.x, factor.y, 1f));
        }

        private static float RotateRadians(StyleRotate rotate)
            => rotate.keyword == StyleKeyword.Undefined ? rotate.value.angle.ToDegrees() * Mathf.Deg2Rad : 0f;

        private static Vector2 ScaleAxes(StyleScale scale)
            => scale.keyword == StyleKeyword.Undefined ? new Vector2(scale.value.value.x, scale.value.value.y) : Vector2.one;
    }
}
