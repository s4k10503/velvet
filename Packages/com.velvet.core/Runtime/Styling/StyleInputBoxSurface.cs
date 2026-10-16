using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace Velvet
{
    // Sends the surface utilities written on a field control to the box the control draws its value in, the way a
    // utility written on an <input> or a <select> paints the field itself, instead of leaving them on the outer
    // control. The controls are the text-input fields (TextField, IntegerField, FloatField, …) and the popup
    // fields (DropdownField, EnumField, …); the box is the child carrying BaseField's input class.
    //
    // Two routes reach the box, split by where the variant's condition lives:
    //   - Route rewrites the class list a factory hands the node. A surface token becomes the input-box scope of
    //     the child-combinator variant (StyleChildVariantClass), so StyleChildVariantManipulator applies it, with
    //     its state, theme, responsive and relational layers, to the box and releases it on teardown. The
    //     state signals are the box's own, as an input's are; the relational sources are looked up from the
    //     control (RelationalAnchorOf).
    //   - DestinationFor redirects a payload that a condition on the OUTER control decides (first:, has-[…]:,
    //     data-[…]:, aria-[…]:, supports-[…]:) while it is being applied, so the control evaluates the
    //     condition and the box takes the paint. StyleVariantPayload.Apply calls it.
    // Layout, size, margin and every other utility stay on the outer control. The transition utilities go to
    // both, since each element tweens only its own properties.
    //
    // A paint family (gradient, border line style, shadow, ring, outline, skew, animate) is driven from the class
    // source the reconciler records per element; the box is not reconciled, so EnsurePaintState records one for it
    // before the first payload lands.
    internal static class StyleInputBoxSurface
    {
        private static readonly string[] s_paddings =
            { "p-", "px-", "py-", "pt-", "pr-", "pb-", "pl-", "ps-", "pe-" };

        private static readonly StyleLonghandSet s_boxLonghands = MotionNativeTransitionGuard.SetOf(
            StyleLonghand.Color, StyleLonghand.Opacity, StyleLonghand.Translate, StyleLonghand.Rotate,
            StyleLonghand.Scale, StyleLonghand.TransformOrigin, StyleLonghand.FontSize, StyleLonghand.UnityFont,
            StyleLonghand.UnityFontDefinition, StyleLonghand.UnityFontStyle, StyleLonghand.UnityTextAlign,
            StyleLonghand.UnityTextAutoSize, StyleLonghand.UnityTextGenerator, StyleLonghand.UnityTextOutlineColor,
            StyleLonghand.UnityTextOutlineWidth, StyleLonghand.UnityTextOverflowPosition,
            StyleLonghand.UnityParagraphSpacing, StyleLonghand.LetterSpacing, StyleLonghand.WordSpacing,
            StyleLonghand.TextShadow, StyleLonghand.TextOverflow, StyleLonghand.WhiteSpace);

        private static readonly StyleLonghandSet s_textOverflowLonghands = MotionNativeTransitionGuard.SetOf(
            StyleLonghand.TextOverflow, StyleLonghand.UnityTextOverflowPosition);

        private static readonly StyleLonghandSet s_transitionLonghands = MotionNativeTransitionGuard.SetOf(
            StyleLonghand.TransitionProperty, StyleLonghand.TransitionDuration, StyleLonghand.TransitionDelay,
            StyleLonghand.TransitionTimingFunction);

        private enum Placement
        {
            Control,
            Box,
            Both,
        }

        // Keyed by the parsed array, which a cached class string hands back unchanged and nothing mutates, so a
        // field re-rendered with the same classes keeps the same array, as the reconciler's class diff needs.
        private static readonly ConditionalWeakTable<string[], string[]> s_routed = new();

        // classNames itself when no token is routed, so a field without one allocates nothing and keeps the shared
        // parsed array.
        public static string[] Route(string[] classNames)
        {
            if (s_routed.TryGetValue(classNames, out var cached))
            {
                return cached;
            }
            var result = RouteUncached(classNames);
            if (!ReferenceEquals(result, classNames))
            {
                s_routed.AddOrUpdate(classNames, result);
            }
            return result;
        }

        private static string[] RouteUncached(string[] classNames)
        {
            List<string>? routed = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                var token = classNames[i];
                var placement = PlacementOfToken(token);
                var scoped = StyleChildVariantClass.InputBoxPrefix + token;
                if (placement == Placement.Control || !StyleChildVariantClass.TryParse(scoped, out _))
                {
                    routed?.Add(token);
                    continue;
                }
                if (routed == null)
                {
                    routed = new List<string>(classNames.Length + 1);
                    for (var j = 0; j < i; j++)
                    {
                        routed.Add(classNames[j]);
                    }
                }
                if (placement == Placement.Both)
                {
                    routed.Add(token);
                }
                routed.Add(scoped);
            }
            return routed?.ToArray() ?? classNames;
        }

        // Whether elementType is a field control whose factory routes. V.Custom<T> asks this for its T.
        public static bool IsControlType(Type elementType)
        {
            var type = elementType;
            while (type != null && !IsFieldBase(type))
            {
                type = type.BaseType;
            }
            return type != null;
        }

        private static bool IsFieldBase(Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }
            var definition = type.GetGenericTypeDefinition();
            return definition == typeof(TextInputBaseField<>) || definition == typeof(BasePopupField<,>);
        }

        public static bool IsTextInputControl(VisualElement element)
            => element.ClassListContains(TextInputBaseField<string>.ussClassName);

        public static bool IsControl(VisualElement element)
            => element.ClassListContains(TextInputBaseField<string>.ussClassName)
               || element.ClassListContains(BasePopupField<string, string>.ussClassName);

        public static bool IsBox(VisualElement element)
            => element.ClassListContains(StyleChildVariantClass.InputBoxClass)
               && element.parent != null && IsControl(element.parent);

        public static bool TryGetBox(VisualElement control, out VisualElement box)
        {
            for (var i = 0; i < control.childCount; i++)
            {
                var child = control.ElementAt(i);
                if (child.ClassListContains(StyleChildVariantClass.InputBoxClass))
                {
                    box = child;
                    return true;
                }
            }
            box = null!;
            return false;
        }

        // The element a payload is applied to: the box when target is a field control and core is a surface
        // utility, target itself otherwise.
        public static VisualElement DestinationFor(VisualElement target, string core)
            => IsSurfaceCore(core) && IsControl(target) && TryGetBox(target, out var box) ? box : target;

        // The box a payload applied to a field control also lands on, for a utility both of them take.
        public static bool TryGetSharedBox(VisualElement target, string core, out VisualElement box)
        {
            box = null!;
            return PlacementOfCore(core) == Placement.Both && IsControl(target) && TryGetBox(target, out box);
        }

        // The element inside a box that draws its text, which a text-overflow utility lands on as well: CSS
        // applies text-overflow to the input, whose own text it is.
        public static bool TryGetBoxText(VisualElement box, string core, out VisualElement text)
        {
            text = null!;
            if (!IsBox(box) || !LonghandsOf(core).Overlaps(s_textOverflowLonghands))
            {
                return false;
            }
            var found = box.Q<TextElement>();
            text = found!;
            return found != null;
        }

        // The element a relational lookup starts from: a box looks for its sources as the control does, so a
        // peer is a sibling of the control and a group an ancestor of it.
        public static VisualElement RelationalAnchorOf(VisualElement element)
            => IsBox(element) ? element.parent : element;

        // Puts the box on the record a variant re-sync reads for an element it did not reconcile, so a paint
        // family's first payload attaches its painter at once.
        public static void EnsurePaintState(ReconcilerContext ctx, VisualElement box)
        {
            if (!ctx.VariantGateClasses.TryGetValue(box, out var state))
            {
                state = new VariantGateState();
                ctx.VariantGateClasses[box] = state;
            }
            state.Reconciled ??= Array.Empty<string>();
            state.PaintTail ??= true;
        }

        // The classes of a plain class list (a gesture channel's) the box takes, and those the control keeps.
        public static string[] Surface(string[] classNames)
            => Array.FindAll(classNames, token => PlacementOfToken(token) != Placement.Control);

        public static string[] NotSurface(string[] classNames)
            => Array.FindAll(classNames, token => PlacementOfToken(token) != Placement.Box);

        public static bool IsSurfaceToken(string token) => PlacementOfToken(token) == Placement.Box;

        public static bool IsSurfaceCore(string core) => PlacementOfCore(core) == Placement.Box;

        // A token's placement, whatever state, theme, responsive or relational layers wrap it. Variants the
        // child-combinator refuses are not peeled here, so they stay on the control for DestinationFor.
        private static Placement PlacementOfToken(string token)
        {
            if (string.IsNullOrEmpty(token) || StyleChildVariantClass.IsChildVariant(token))
            {
                return Placement.Control;
            }
            var leaf = token;
            while (StyleVariantClass.TryParse(leaf, out _, out var next) && !string.IsNullOrEmpty(next))
            {
                leaf = next!;
            }
            return PlacementOfCore(StyleArbitraryValueResolver.StripImportant(leaf, out _));
        }

        private static Placement PlacementOfCore(string core)
        {
            if (core.StartsWith("bg-", StringComparison.Ordinal)
                // The nine-slice utilities follow the background image bg-[addr:…] puts on the box.
                || core.StartsWith("slice-", StringComparison.Ordinal)
                || core == "border" || core.StartsWith("border-", StringComparison.Ordinal)
                || core == "rounded" || core.StartsWith("rounded-", StringComparison.Ordinal)
                || StartsWithAny(core, s_paddings)
                || StyleShadowClass.IsShadowClass(core)
                || StyleRingClass.IsRingClass(core)
                || StyleGradientClass.IsGradientClass(core)
                // CSS's filter, opacity, transforms, color and text styling apply to the element and what it
                // contains; a declared label is neither the input nor inside it.
                || StyleFilterValueParser.IsFilterLeaf(core)
                || StyleFontClass.IsFontToken(core)
                || StyleTextEffectClass.IsTextEffectToken(core)
                || StyleSkewClass.IsSkewClass(core)
                || StyleAnimateClass.IsAnimateClass(core)
                || core.StartsWith("caret-", StringComparison.Ordinal)
                || core.StartsWith("selection:", StringComparison.Ordinal))
            {
                return Placement.Box;
            }
            var longhands = LonghandsOf(core);
            if (longhands.IsEmpty)
            {
                return Placement.Control;
            }
            if (longhands.Union(s_boxLonghands) == s_boxLonghands)
            {
                return Placement.Box;
            }
            return longhands.Union(s_transitionLonghands) == s_transitionLonghands
                ? Placement.Both
                : Placement.Control;
        }

        // What a utility writes: its bundled rule's properties, or the inline property it resolves to.
        private static StyleLonghandSet LonghandsOf(string core)
        {
            if (StyleUtilityProperties.TryGet(core, out var rule))
            {
                return rule.Properties;
            }
            return StyleArbitraryValueResolver.IsInlineResolved(core)
                   && StyleArbitraryValueResolver.TryParse(core, out var style)
                ? StyleArbitraryLonghands.Of(style.Property)
                : StyleLonghandSet.Empty;
        }

        private static bool StartsWithAny(string text, string[] prefixes)
            => Array.Exists(prefixes, prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }
}
