using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The corners of a box, in the order a four-value border-radius lists them.
    [Flags]
    internal enum RadiusCorners
    {
        None = 0,
        TopLeft = 1 << 0,
        TopRight = 1 << 1,
        BottomRight = 1 << 2,
        BottomLeft = 1 << 3,
        All = TopLeft | TopRight | BottomRight | BottomLeft,
    }

    // Four corner radii, each with its horizontal component in x and its vertical one in y.
    internal readonly struct CornerRadii
    {
        public Vector2 TopLeft { get; init; }
        public Vector2 TopRight { get; init; }
        public Vector2 BottomRight { get; init; }
        public Vector2 BottomLeft { get; init; }

        public static CornerRadii Circular(float topLeft, float topRight, float bottomRight, float bottomLeft) => new()
        {
            TopLeft = new Vector2(topLeft, topLeft),
            TopRight = new Vector2(topRight, topRight),
            BottomRight = new Vector2(bottomRight, bottomRight),
            BottomLeft = new Vector2(bottomLeft, bottomLeft),
        };
    }

    /// <summary>
    /// Scales an element's declared corner radii the way CSS does once adjacent ones overlap on a side
    /// (Backgrounds 3 §5.5), and holds the result inline.
    /// </summary>
    /// <remarks>
    /// A held radius hides the declared one from every public reading, so the declared radii reach this by
    /// routes of their own: a <c>rounded-*</c> class through the custom properties <c>_radius_declared.uss</c>
    /// restates it as, and an inline writer through <see cref="SetInline"/>, which
    /// <see cref="StyleArbitraryValueResolver.ApplyInline"/> calls in place of writing a radius slot itself. A
    /// corner neither route declares is never held, since nothing would say when its value changed.
    ///
    /// A refit a size change causes suspends the element's transitions while it writes: a new size is not a
    /// declared change, and a <c>transition-all</c> element would otherwise animate from the unfitted radius
    /// when it is first laid out.
    /// </remarks>
    internal static class CornerRadiusFit
    {
        private const int CornerCount = 4;

        // The prefix of every bundled class that declares a radius.
        private const string RadiusClassPrefix = "rounded";

        // Named by Generators~/src/Velvet.StyleTable/CornerRadiusDeclarationSheet.cs, in RadiusCorners order.
        private static readonly CustomStyleProperty<string>[] s_declaredProperties =
        {
            new("--velvet-radius-top-left"),
            new("--velvet-radius-top-right"),
            new("--velvet-radius-bottom-right"),
            new("--velvet-radius-bottom-left"),
        };

        // The spelling MotionNativeTransitionGuard suspends transitions with.
        private static readonly List<StylePropertyName> s_noTransitions = new() { new StylePropertyName("none") };

        private sealed class State
        {
            // What an inline writer declared for each corner; null where none has.
            public readonly Length?[] Inline = new Length?[CornerCount];

            // What the element's rules declare for each corner, as the restating custom properties report it.
            public readonly Length?[] Sheet = new Length?[CornerCount];

            // What this last wrote to each corner's inline slot; null where it holds nothing.
            public readonly Length?[] Written = new Length?[CornerCount];

        }

        private static readonly ConditionalWeakTable<VisualElement, State> s_states = new();

        public static void TrackClass(VisualElement element, string cls)
        {
            if (cls.StartsWith(RadiusClassPrefix, StringComparison.Ordinal))
            {
                GetOrAttach(element);
            }
        }

        public static void SetInline(VisualElement element, RadiusCorners corners, Length value)
        {
            var state = GetOrAttach(element);
            Assign(state.Inline, corners, value);
            RefreshDeclared(element, state);
        }

        public static void ClearInline(VisualElement element, RadiusCorners corners)
        {
            if (!s_states.TryGetValue(element, out var state))
            {
                WriteSlots(element, corners, new StyleLength(StyleKeyword.Null));
                return;
            }
            Assign(state.Inline, corners, null);
            RefreshDeclared(element, state);
        }

        // Drops what the fit tracks, for an element on its way to the pool; its callbacks find nothing after
        // this. The inline slots themselves are the caller's to clear.
        public static void Release(VisualElement element) => s_states.Remove(element);

        /// <summary>
        /// The factor CSS scales every radius by so that no two adjacent radii overlap on a side — 1 where none
        /// does.
        /// </summary>
        internal static float ScaleFactor(float width, float height, in CornerRadii radii)
        {
            var factor = 1f;
            factor = Reduce(factor, width, radii.TopLeft.x + radii.TopRight.x);
            factor = Reduce(factor, width, radii.BottomLeft.x + radii.BottomRight.x);
            factor = Reduce(factor, height, radii.TopLeft.y + radii.BottomLeft.y);
            factor = Reduce(factor, height, radii.TopRight.y + radii.BottomRight.y);
            return factor;
        }

        private static float Reduce(float factor, float side, float sum)
        {
            // MUTANT_SURVIVES(equivalent): where the sum equals the side the ratio is 1, and a factor is never above 1.
            return sum > side ? Mathf.Min(factor, side / sum) : factor;
        }

        private static void Assign(Length?[] slots, RadiusCorners corners, Length? value)
        {
            var corner = 0;
            for (var bits = (int)corners; bits != 0; bits >>= 1)
            {
                if ((bits & 1) != 0)
                {
                    slots[corner] = value;
                }
                corner++;
            }
        }

        private static readonly EventCallback<CustomStyleResolvedEvent> s_onStyleResolved = OnStyleResolved;
        private static readonly EventCallback<GeometryChangedEvent> s_onGeometryChanged = OnGeometryChanged;

        private static State GetOrAttach(VisualElement element)
        {
            if (s_states.TryGetValue(element, out var state))
            {
                return state;
            }
            state = new State();
            element.RegisterCallback(s_onStyleResolved);
            element.RegisterCallback(s_onGeometryChanged);
            s_states.Add(element, state);
            return state;
        }

        private static void OnStyleResolved(CustomStyleResolvedEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            if (!s_states.TryGetValue(element, out var state))
            {
                return;
            }
            for (var i = 0; i < CornerCount; i++)
            {
                state.Sheet[i] = evt.customStyle.TryGetValue(s_declaredProperties[i], out var text)
                    && StyleArbitraryValueResolver.TryParseValue(text.AsSpan(), out var value, out var unit)
                        ? new Length(value, unit)
                        : null;
            }
            RefreshDeclared(element, state);
        }

        private static void OnGeometryChanged(GeometryChangedEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            if (evt.oldRect.size != evt.newRect.size && s_states.TryGetValue(element, out var state))
            {
                Refresh(element, state, suspendTransitions: true);
            }
        }

        private static void RefreshDeclared(VisualElement element, State state)
            => Refresh(element, state, suspendTransitions: false);

        private static Length? Declared(State state, int corner) => state.Inline[corner] ?? state.Sheet[corner];

        private static void Refresh(VisualElement element, State state, bool suspendTransitions)
        {
            // A corner nothing tracked declares any longer gives back what this wrote first, so the value read
            // for it below is the cascade's own rather than ours.
            for (var i = 0; i < CornerCount; i++)
            {
                if (Declared(state, i) == null && state.Written[i] != null)
                {
                    WriteCorner(element, state, i, null);
                }
            }

            // The factor is taken before any write: a write changes only a declared corner, whose radius the
            // factor reads from its declaration rather than from the element.
            var factor = FactorFor(element, state);
            var restore = default(StyleList<StylePropertyName>);
            var suspended = false;
            for (var i = 0; i < CornerCount; i++)
            {
                var target = TargetFor(state, i, factor);
                if (target == state.Written[i])
                {
                    continue;
                }
                if (suspendTransitions && !suspended)
                {
                    restore = element.style.transitionProperty;
                    element.style.transitionProperty = s_noTransitions;
                    suspended = true;
                }
                WriteCorner(element, state, i, target);
            }
            if (suspended)
            {
                element.style.transitionProperty = restore;
            }
        }

        private static Length? TargetFor(State state, int corner, float factor)
        {
            if (Declared(state, corner) is not { } declared)
            {
                return null;
            }
            if (factor < 1f)
            {
                return new Length(declared.value * factor, declared.unit);
            }
            // A class radius that fits is the stylesheet's to paint.
            return state.Inline[corner] != null ? declared : null;
        }

        private static float FactorFor(VisualElement element, State state)
        {
            var width = element.layout.width;
            var height = element.layout.height;
            // Unlaid-out (NaN) and empty boxes alike: nothing is painted to fit.
            if (!(width * height > 0f))
            {
                return 1f;
            }
            var radii = new CornerRadii
            {
                TopLeft = RadiusOf(element, state, 0, width, height),
                TopRight = RadiusOf(element, state, 1, width, height),
                BottomRight = RadiusOf(element, state, 2, width, height),
                BottomLeft = RadiusOf(element, state, 3, width, height),
            };
            return ScaleFactor(width, height, radii);
        }

        private static Vector2 RadiusOf(VisualElement element, State state, int corner, float width, float height)
        {
            if (Declared(state, corner) is { } declared)
            {
                return declared.unit == LengthUnit.Percent
                    ? new Vector2(Mathf.Max(0f, width * declared.value / 100f), Mathf.Max(0f, height * declared.value / 100f))
                    : new Vector2(Mathf.Max(0f, declared.value), Mathf.Max(0f, declared.value));
            }
            var resolved = ResolvedCorner(element.resolvedStyle, corner);
            return float.IsNaN(resolved) ? Vector2.zero : new Vector2(Mathf.Max(0f, resolved), Mathf.Max(0f, resolved));
        }

        private static float ResolvedCorner(IResolvedStyle style, int corner) => corner switch
        {
            0 => style.borderTopLeftRadius,
            1 => style.borderTopRightRadius,
            2 => style.borderBottomRightRadius,
            _ => style.borderBottomLeftRadius,
        };

        private static void WriteCorner(VisualElement element, State state, int corner, Length? target)
        {
            state.Written[corner] = target;
            var value = target is { } length ? new StyleLength(length) : new StyleLength(StyleKeyword.Null);
            WriteSlots(element, (RadiusCorners)(1 << corner), value);
        }

        private static void WriteSlots(VisualElement element, RadiusCorners corners, StyleLength value)
        {
            var style = element.style;
            if ((corners & RadiusCorners.TopLeft) != 0)
            {
                style.borderTopLeftRadius = value;
            }
            if ((corners & RadiusCorners.TopRight) != 0)
            {
                style.borderTopRightRadius = value;
            }
            if ((corners & RadiusCorners.BottomRight) != 0)
            {
                style.borderBottomRightRadius = value;
            }
            if ((corners & RadiusCorners.BottomLeft) != 0)
            {
                style.borderBottomLeftRadius = value;
            }
        }
    }
}
