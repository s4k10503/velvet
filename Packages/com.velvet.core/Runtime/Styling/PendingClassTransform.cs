#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The scale, rotate and transform origin a layoutId Motion's bundled utility classes give it after a render has
    // changed its class list and before a style pass has matched its rules again, read off the class list: StyleCascade
    // and resolvedStyle give what the last style pass matched, and a pass run inside the render writes before the next
    // one (Given_AHolderScaledByAStylesheetClass_When_ItTakesTheLeadBackInTheRenderThatRescalesIt_Then_ItsMoveIsDrawnAtTheNewScale).
    // A value is the one the rule of the bundled class holding the property declares. Null once the style pass has
    // caught up, and where a class holding the property is one whose value is not read here, a gated or an important one.
    // A rule of a user's own stylesheet is not seen here at all; its value is the style pass's.
    internal static class PendingClassTransform
    {
        private enum Kind : byte
        {
            // No bundled class on the list declares the property.
            Absent,
            Value,
            // A class on the list declares it with a value not read here.
            Unread,
        }

        private struct Declared
        {
            public Kind ScaleKind;
            public float Scale;
            public Kind RotateKind;
            public float Rotate;
            public Kind OriginKind;
            public Vector2 Origin;
        }

        private sealed class State
        {
            // What the class list was at the last patch, and what its bundled classes declared.
            public int Signature;
            public Declared Seen;
            // What the classes the computed style was matched against declared, and the rules hash it had then, while a
            // patch has changed the list since.
            public bool Pending;
            public long? StaleHash;
            public Declared Before;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, State> s_states = new();

        // Called as a patch of a layoutId Motion has written its class list, its first one included. Returns whether the
        // patch changed the list.
        public static bool Observe(VisualElement element)
        {
            var signature = Signature(element);
            if (!s_states.TryGetValue(element, out var state))
            {
                s_states.Add(element, new State { Signature = signature, Seen = Read(element) });
                return false;
            }
            if (signature == state.Signature) return false;
            // A second change before the style pass leaves the classes that pass last saw where they were.
            if (!IsStale(element, state))
            {
                (state.Pending, state.StaleHash, state.Before) = (true, StyleCascade.RulesHash(element), state.Seen);
            }
            (state.Signature, state.Seen) = (signature, Read(element));
            return true;
        }

        public static void Forget(VisualElement element) => s_states.Remove(element);

        public static Vector3? DeclaredScale(VisualElement element) =>
            TryPending(element, out var state)
                ? Resolve(state.Seen.ScaleKind, state.Before.ScaleKind, new Vector3(state.Seen.Scale, state.Seen.Scale, 1f), Vector3.one)
                : null;

        public static Rotate? DeclaredRotate(VisualElement element)
        {
            if (!TryPending(element, out var state)) return null;
            var degrees = Resolve(state.Seen.RotateKind, state.Before.RotateKind, state.Seen.Rotate, 0f);
            return degrees is { } value ? new Rotate(new Angle(value, AngleUnit.Degree)) : null;
        }

        // As fractions of the element's size.
        public static Vector2? DeclaredOrigin(VisualElement element) =>
            TryPending(element, out var state)
                ? Resolve(state.Seen.OriginKind, state.Before.OriginKind, state.Seen.Origin, new Vector2(0.5f, 0.5f))
                : null;

        // The class's value where it is one; the property's initial value where the class giving it has gone and none of
        // those read here gives it now; else nothing.
        private static T? Resolve<T>(Kind now, Kind before, T value, T initial) where T : struct =>
            now == Kind.Value ? value
            : now == Kind.Absent && before == Kind.Value ? initial
            : (T?)null;

        private static bool TryPending(VisualElement element, out State state) =>
            s_states.TryGetValue(element, out state) && IsStale(element, state);

        private static bool IsStale(VisualElement element, State state)
        {
            if (!state.Pending) return false;
            if (StyleCascade.RulesHash(element) == state.StaleHash) return true;
            state.Pending = false;
            return false;
        }

        // Order-free, as the cascade is.
        private static int Signature(VisualElement element)
        {
            var (signature, count) = (0, 0);
            foreach (var cls in element.GetClasses()) (signature, count) = (signature + cls.GetHashCode(), count + 1);
            return signature * 31 + count;
        }

        // Of two bundled classes on the list declaring a property, the one later in the cascade holds it.
        private static Declared Read(VisualElement element)
        {
            var declared = new Declared();
            int scaleAt = -1, rotateAt = -1, originAt = -1;
            bool scaleUnread = false, rotateUnread = false, originUnread = false;
            foreach (var cls in element.GetClasses())
            {
                if (!StyleUtilityProperties.TryGet(cls, out var rule))
                {
                    // An important class outranks the plain ones and is not ranked here.
                    var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                    if (!important || !StyleUtilityProperties.TryGet(core, out var plain)) continue;
                    rule = plain;
                }
                else if (rule.Gate == StyleUtilityGate.None)
                {
                    if (rule.Properties.Contains(StyleLonghand.Scale) && rule.CascadePosition > scaleAt)
                    {
                        scaleAt = rule.CascadePosition;
                        declared.ScaleKind = TryScaleOf(cls, out declared.Scale) ? Kind.Value : Kind.Unread;
                    }
                    if (rule.Properties.Contains(StyleLonghand.Rotate) && rule.CascadePosition > rotateAt)
                    {
                        rotateAt = rule.CascadePosition;
                        declared.RotateKind = TryRotateOf(cls, out declared.Rotate) ? Kind.Value : Kind.Unread;
                    }
                    if (rule.Properties.Contains(StyleLonghand.TransformOrigin) && rule.CascadePosition > originAt)
                    {
                        originAt = rule.CascadePosition;
                        declared.OriginKind = TryOriginOf(cls, out declared.Origin) ? Kind.Value : Kind.Unread;
                    }
                    continue;
                }
                // A gated rule outranks every plain one while its gate holds, which a class list does not tell.
                scaleUnread |= rule.Properties.Contains(StyleLonghand.Scale);
                rotateUnread |= rule.Properties.Contains(StyleLonghand.Rotate);
                originUnread |= rule.Properties.Contains(StyleLonghand.TransformOrigin);
            }
            if (scaleUnread) declared.ScaleKind = Kind.Unread;
            if (rotateUnread) declared.RotateKind = Kind.Unread;
            if (originUnread) declared.OriginKind = Kind.Unread;
            return declared;
        }

        // The uniform scale-N, through the table the per-axis presets mirror the sheet's scale classes by.
        internal static bool TryScaleOf(string cls, out float scale)
        {
            scale = 1f;
            return cls.StartsWith("scale-", System.StringComparison.Ordinal)
                && StyleArbitraryValueResolver.TryGetAxisScale(cls.Substring("scale-".Length), out scale);
        }

        // rotate-N and rotate-nN, in degrees.
        internal static bool TryRotateOf(string cls, out float degrees)
        {
            degrees = 0f;
            if (!cls.StartsWith("rotate-", System.StringComparison.Ordinal)) return false;
            var suffix = cls.Substring("rotate-".Length);
            var negated = suffix.StartsWith("n", System.StringComparison.Ordinal);
            if (!StyleArbitraryValueResolver.TryGetRotateScale(negated ? suffix.Substring(1) : suffix, out degrees)) return false;
            if (negated) degrees = -degrees;
            return true;
        }

        // origin-<side>[-<side>], each side a keyword of one axis and center the middle of either; an axis no keyword
        // names is centred, as `origin-top` is `center top`.
        internal static bool TryOriginOf(string cls, out Vector2 origin)
        {
            origin = new Vector2(0.5f, 0.5f);
            if (!cls.StartsWith("origin-", System.StringComparison.Ordinal)) return false;
            foreach (var side in cls.Substring("origin-".Length).Split('-'))
            {
                switch (side)
                {
                    case "left": origin.x = 0f; break;
                    case "right": origin.x = 1f; break;
                    case "top": origin.y = 0f; break;
                    case "bottom": origin.y = 1f; break;
                    case "center": break;
                    default: return false;
                }
            }
            return true;
        }
    }
}
