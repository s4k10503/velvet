using System;
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
    // Layout, size, margin and every other utility stay on the outer control.
    //
    // A paint family (gradient, border line style, shadow, ring, outline) is driven from the class source the
    // reconciler records per element; the box is not reconciled, so EnsurePaintState records one for it before
    // the first payload lands.
    internal static class StyleInputBoxSurface
    {
        private static readonly string[] s_paddings =
            { "p-", "px-", "py-", "pt-", "pr-", "pb-", "pl-", "ps-", "pe-" };

        // classNames itself when no token is a surface utility, so a field without one allocates nothing and
        // keeps the shared parsed array.
        public static string[] Route(string[] classNames)
        {
            string[]? routed = null;
            for (var i = 0; i < classNames.Length; i++)
            {
                var token = classNames[i];
                if (!IsSurfaceToken(token))
                {
                    continue;
                }
                var scoped = StyleChildVariantClass.InputBoxPrefix + token;
                if (!StyleChildVariantClass.TryParse(scoped, out _))
                {
                    continue;
                }
                routed ??= (string[])classNames.Clone();
                routed[i] = scoped;
            }
            return routed ?? classNames;
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

        // The surface classes of a plain class list (a gesture channel's), and the rest.
        public static string[] Surface(string[] classNames) => Array.FindAll(classNames, IsSurfaceToken);

        public static string[] NotSurface(string[] classNames)
            => Array.FindAll(classNames, token => !IsSurfaceToken(token));

        // A surface token, whatever state, theme, responsive or relational layers wrap it. Variants the
        // child-combinator refuses are not peeled here, so they stay on the control for DestinationFor.
        public static bool IsSurfaceToken(string token)
        {
            if (string.IsNullOrEmpty(token) || StyleChildVariantClass.IsChildVariant(token))
            {
                return false;
            }
            var leaf = token;
            while (StyleVariantClass.TryParse(leaf, out _, out var next) && !string.IsNullOrEmpty(next))
            {
                leaf = next!;
            }
            return IsSurfaceCore(StyleArbitraryValueResolver.StripImportant(leaf, out _));
        }

        public static bool IsSurfaceCore(string core)
            => core.StartsWith("bg-", StringComparison.Ordinal)
               // The nine-slice utilities follow the background image bg-[addr:…] puts on the box.
               || core.StartsWith("slice-", StringComparison.Ordinal)
               || core == "border" || core.StartsWith("border-", StringComparison.Ordinal)
               || core == "rounded" || core.StartsWith("rounded-", StringComparison.Ordinal)
               || StartsWithAny(core, s_paddings)
               || StyleShadowClass.IsShadowClass(core)
               || StyleRingClass.IsRingClass(core)
               || StyleGradientClass.IsGradientClass(core)
               || core.StartsWith("caret-", StringComparison.Ordinal)
               || core.StartsWith("selection:", StringComparison.Ordinal);

        private static bool StartsWithAny(string text, string[] prefixes)
            => Array.Exists(prefixes, prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }
}
