#nullable enable
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet
{
    // The style an element's matching rules give it beneath its inline values, which UI Toolkit caches under the
    // element's matchingRulesHash (StyleCache). That cache is internal to the engine and so read by reflection;
    // where the members a read needs do not resolve, that read reports nothing and the caller keeps its own default.
    // Given_APulseOverANamedOpacity_When_AQuarterThroughItsLoop_Then_ItIsHalfwayFromThatOpacityToHalf,
    // Given_ASpinOverANamedRotation_When_AQuarterThroughItsLoop_Then_ItHasTurnedAQuarterFromThatRotation and
    // Given_APingOverAnOpacityAndAScale_When_ItsLoopStarts_Then_ItStartsFromThoseValues fail when the opacity,
    // rotate or scale read stops giving the cascaded value.
    internal static class StyleCascade
    {
        private static readonly FieldInfo? s_style = EngineMember.ElementComputedStyle.ResolveField();
        private static readonly FieldInfo? s_hash = EngineMember.ComputedStyleMatchingRulesHash.ResolveField();
        private static readonly MethodInfo? s_tryGet = EngineMember.TryGetComputedStyle.ResolveMethod();
        private static readonly PropertyInfo? s_opacity = EngineMember.ComputedStyleOpacity.ResolveProperty();
        private static readonly PropertyInfo? s_rotate = EngineMember.ComputedStyleRotate.ResolveProperty();
        private static readonly PropertyInfo? s_scale = EngineMember.ComputedStyleScale.ResolveProperty();
        private static readonly PropertyInfo? s_translate = EngineMember.ComputedStyleTranslate.ResolveProperty();

        private static readonly object?[] s_args = new object?[2];

        // The cached ComputedStyle boxed, or null where it cannot be read. Members of it are read off the result
        // by their PropertyInfo.
        public static object? Of(VisualElement element)
        {
            if (s_style == null || s_hash == null || s_tryGet == null) return null;
            s_args[0] = s_hash.GetValue(s_style.GetValue(element));
            s_args[1] = null;
            return s_tryGet.Invoke(null, s_args) is true ? s_args[1] : null;
        }

        public static bool TryReadOpacity(VisualElement element, out float opacity)
        {
            opacity = 1f;
            if (s_opacity == null || Of(element) is not { } style) return false;
            opacity = (float)s_opacity.GetValue(style);
            return true;
        }

        public static bool TryReadRotate(VisualElement element, out Rotate rotate)
        {
            rotate = default;
            if (s_rotate == null || Of(element) is not { } style) return false;
            rotate = (Rotate)s_rotate.GetValue(style);
            return true;
        }

        public static bool TryReadScale(VisualElement element, out Scale scale)
        {
            scale = default;
            if (s_scale == null || Of(element) is not { } style) return false;
            scale = (Scale)s_scale.GetValue(style);
            return true;
        }

        public static bool TryReadTranslate(VisualElement element, out Translate translate)
        {
            translate = default;
            if (s_translate == null || Of(element) is not { } style) return false;
            translate = (Translate)s_translate.GetValue(style);
            return true;
        }
    }
}
