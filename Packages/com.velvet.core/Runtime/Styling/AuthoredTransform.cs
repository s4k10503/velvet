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
            // The rotate and scale the slots were last composed from, which a restyle is compared against.
            public (float Degrees, Vector2 Scale) Composed;
        }

        // Carries the custom property that makes UI Toolkit send CustomStyleResolvedEvent on a restyle, the way
        // DivideDashPainter's marker does; a rule of the application's own that gives the element a rotate or a scale
        // reaches the slots through that event.
        internal const string MarkerClass = "velvet-skew-observed";

        private static readonly EventCallback<CustomStyleResolvedEvent> s_onStyleResolved = OnStyleResolved;

        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();

        // The rotate the element's writers intend: the inline slot where nothing is composed into it, and otherwise
        // the value held apart from the composite. A Null keyword means no writer has set one.
        public static StyleRotate Rotate(VisualElement element)
            => s_states.TryGetValue(element, out var state) ? state.Rotate : element.style.rotate;

        public static StyleScale Scale(VisualElement element)
            => s_states.TryGetValue(element, out var state) ? state.Scale : element.style.scale;

        // The angles the slots shear by now; false where the element carries no skew.
        public static bool TryGetSkew(VisualElement element, out float xDeg, out float yDeg)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                (xDeg, yDeg) = (0f, 0f);
                return false;
            }
            (xDeg, yDeg) = (state.SkewXDeg, state.SkewYDeg);
            return true;
        }

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
                element.RegisterCallback(s_onStyleResolved);
            }
            element.AddToClassList(MarkerClass);
            (state.SkewXDeg, state.SkewYDeg) = (xDeg, yDeg);
            Write(element, state);
        }

        // Composes the slots again for an element whose class list moved: a rotate-* or scale-* class gives the
        // rotate or scale no writer has set, so a class that came or went moves the composite. Nothing for an
        // element with no skew.
        public static void Resync(VisualElement element)
        {
            if (s_states.TryGetValue(element, out var state))
            {
                element.AddToClassList(MarkerClass);
                Write(element, state);
            }
        }

        // Composes the slots again when a restyle left the rotate or scale the rules give the element different from
        // the pair they were last composed from, and only then.
        private static void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            if (s_states.TryGetValue(element, out var state) && Unskewed(element, state) != state.Composed)
            {
                Write(element, state);
            }
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
            element.UnregisterCallback(s_onStyleResolved);
            element.RemoveFromClassList(MarkerClass);
            element.style.rotate = state.Rotate;
            element.style.scale = state.Scale;
        }

        private static void Write(VisualElement element, State state)
        {
            state.Composed = Unskewed(element, state);
            ComposedSlots(state, out var rotate, out var scale);
            element.style.rotate = rotate;
            element.style.scale = scale;
        }

        // R(theta) * diag(sx, sy) * skewX(x) skewY(y), the order CSS applies the individual `rotate` and `scale`
        // properties around `transform`, as the slots that draw it. A rotation about an axis other than z is read by
        // its angle alone, as the 2D rotate utilities are the only writers; a rule's own is declined
        // (BundledClassTransform.OfRules).
        private static void ComposedSlots(State state, out Rotate rotate, out Scale scale)
        {
            var (theta, drawnScale) = state.Composed;
            var tx = Mathf.Tan(state.SkewXDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(state.SkewYDeg * Mathf.Deg2Rad);
            var cos = Mathf.Cos(theta * Mathf.Deg2Rad);
            var sin = Mathf.Sin(theta * Mathf.Deg2Rad);
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

        // The rotate in degrees and the scale on each axis the element would be drawn with had it no skew. An inline
        // value outranks the stylesheet's, so the rules speak only for a slot no writer has set.
        private static (float Degrees, Vector2 Scale) Unskewed(VisualElement element, State state)
        {
            var rotated = state.Rotate.keyword != StyleKeyword.Null;
            var scaled = state.Scale.keyword != StyleKeyword.Null;
            var declared = rotated && scaled ? (RotateDegrees: 0f, Scale: Vector3.one) : BundledClassTransform.OfRules(element);
            var degrees = !rotated ? declared.RotateDegrees
                : state.Rotate.keyword == StyleKeyword.Undefined ? state.Rotate.value.angle.ToDegrees() : 0f;
            var axes = !scaled ? new Vector2(declared.Scale.x, declared.Scale.y)
                : state.Scale.keyword == StyleKeyword.Undefined ? new Vector2(state.Scale.value.value.x, state.Scale.value.value.y)
                : Vector2.one;
            return (degrees, axes);
        }

        // What the element is drawn at, less the skew: its resolved style, or the value its writers and classes give
        // it where the resolved one is the composite.
        public static Rotate ResolvedRotate(VisualElement element)
            => s_states.TryGetValue(element, out var state)
                ? new Rotate(new Angle(Unskewed(element, state).Degrees, AngleUnit.Degree))
                : element.resolvedStyle.rotate;

        public static Scale ResolvedScale(VisualElement element)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                return element.resolvedStyle.scale;
            }
            var axes = Unskewed(element, state).Scale;
            return new Scale(new Vector3(axes.x, axes.y, 1f));
        }
    }
}
