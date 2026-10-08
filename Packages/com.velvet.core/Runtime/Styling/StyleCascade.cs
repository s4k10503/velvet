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

        public static bool TryReadOpacity(object style, out float opacity)
        {
            opacity = 1f;
            if (s_opacity == null) return false;
            opacity = (float)s_opacity.GetValue(style);
            return true;
        }

        public static bool TryReadRotate(object style, out Rotate rotate)
        {
            rotate = default;
            if (s_rotate == null) return false;
            rotate = (Rotate)s_rotate.GetValue(style);
            return true;
        }

        public static bool TryReadScale(object style, out Scale scale)
        {
            scale = default;
            if (s_scale == null) return false;
            scale = (Scale)s_scale.GetValue(style);
            return true;
        }

        public static bool TryReadTranslate(object style, out Translate translate)
        {
            translate = default;
            if (s_translate == null) return false;
            translate = (Translate)s_translate.GetValue(style);
            return true;
        }
    }

    // What an element's classes give the four slots a loop writes, read once and kept until the element's class list
    // changes, so that a steady tick does not go through reflection again. The class list is the change signal
    // because hover: and dark: act by changing it (Given_APulseOverAHoverOpacity_... and
    // Given_APulseOverADarkOpacity_... in AnimateBaseValueTests). A rule that matches by pseudo-state or by an
    // ancestor alone, with no class change on the element, is not seen until the next class change.
    internal struct CascadeSnapshot
    {
        private int _key;
        private bool _read;
        private bool _hasOpacity;
        private bool _hasRotate;
        private bool _hasScale;
        private bool _hasTranslate;
        private float _opacity;
        private Rotate _rotate;
        private Scale _scale;
        private Translate _translate;

        public void Refresh(VisualElement element)
        {
            var key = ClassKey(element);
            if (_read && key == _key) return;
            _read = false;
            if (StyleCascade.Of(element) is not { } style) return;
            _hasOpacity = StyleCascade.TryReadOpacity(style, out _opacity);
            _hasRotate = StyleCascade.TryReadRotate(style, out _rotate);
            _hasScale = StyleCascade.TryReadScale(style, out _scale);
            _hasTranslate = StyleCascade.TryReadTranslate(style, out _translate);
            // The sentinel key of a list that cannot be read stays unmatched, so such an element reads every time.
            _key = key;
            _read = key != UnreadableKey;
        }

        public bool TryOpacity(out float opacity)
        {
            opacity = _opacity;
            return _hasOpacity;
        }

        public bool TryRotate(out Rotate rotate)
        {
            rotate = _rotate;
            return _hasRotate;
        }

        public bool TryScale(out Scale scale)
        {
            scale = _scale;
            return _hasScale;
        }

        public bool TryTranslate(out Translate translate)
        {
            translate = _translate;
            return _hasTranslate;
        }

        private const int UnreadableKey = int.MinValue;

        // The class list as a hash, read without allocating: GetClasses hands back the element's own list.
        private static int ClassKey(VisualElement element)
        {
            if (element.GetClasses() is not System.Collections.Generic.List<string> classes) return UnreadableKey;
            var key = classes.Count;
            for (var i = 0; i < classes.Count; i++)
            {
                key = unchecked((key * 31) + classes[i].GetHashCode());
            }
            return key == UnreadableKey ? 0 : key;
        }
    }
}
