using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Tailwind's preflight gives every element `border: 0 solid`, so a border colour nothing else sets is
    // currentcolor. Written to an element that carries a border
    // width utility, under any variant, as a currentcolor layer at StyleLayerPriority.Preflight: every border
    // colour utility outranks it, a stylesheet class through the element's class projection, which is created
    // here for that reason. An element with no width utility, and a UI Toolkit control, keep their theme's border.
    internal static class PreflightBorderColor
    {
        private static readonly StyleLonghandSet s_widths = StyleArbitraryLonghands.Of(ArbitraryProperty.BorderWidth);

        private static readonly ArbitraryStyle s_currentColor =
            new(ArbitraryProperty.BorderColor, Color.white, ColorReference.Current);

        internal static void Sync(VisualElement element, string[] classNames)
        {
            var applied = StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor,
                StyleLayerPriority.Preflight);
            var wanted = SetsBorderWidth(classNames) && !HoldsControlTheme(element);
            if (wanted == applied)
            {
                if (wanted)
                {
                    RankUserClasses(element);
                }
                return;
            }
            if (!wanted)
            {
                StyleArbitraryValueResolver.Clear(element, ArbitraryProperty.BorderColor, StyleLayerPriority.Preflight);
                return;
            }
            if (StyleArbitraryValueResolver.TryGetProjection(element) == null)
            {
                StyleArbitraryValueResolver.GetOrCreateProjection(element).SeedBaseLayer(element);
            }
            StyleArbitraryValueResolver.Apply(element, s_currentColor, StyleLayerPriority.Preflight);
            RankUserClasses(element);
        }

        // A UI Toolkit control keeps its theme's border colour, which its own classes set
        // and no probe reads (StyleUserClassProperties takes engine-owned classes as setting nothing).
        private static bool HoldsControlTheme(VisualElement element)
        {
            foreach (var cls in element.GetClasses())
            {
                if (cls.StartsWith("unity-", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Preflight is a base-layer declaration, so a stylesheet rule of the application's own outranks it: its
        // classes are read here, and a class that writes a border colour takes the property off the layer.
        private static void RankUserClasses(VisualElement element)
            => StyleArbitraryValueResolver.TryGetProjection(element)?.AskForUserClasses(element);

        private static bool SetsBorderWidth(string[] classNames) => Array.Exists(classNames, SetsWidth);

        private static bool SetsWidth(string cls)
        {
            if (string.IsNullOrEmpty(cls) || cls.IndexOf("border", StringComparison.Ordinal) < 0)
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            var leaf = core.Substring(StyleSpacingPrecedence.VariantsLength(core));
            return StyleUtilityProperties.TryGet(leaf, out var rule)
                ? rule.Properties.Overlaps(s_widths)
                : StyleArbitraryValueResolver.IsInlineResolved(leaf)
                    && StyleArbitraryValueResolver.TryParseLayer(leaf, out var style)
                    && StyleArbitraryLonghands.Of(style.Property).Overlaps(s_widths);
        }
    }
}
