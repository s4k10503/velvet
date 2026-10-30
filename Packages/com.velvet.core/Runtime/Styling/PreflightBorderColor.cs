using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Tailwind's preflight gives every element `border: 0 solid`, so a border colour nothing else sets is
    // currentcolor. Written to an element that carries a border
    // width utility, under any variant, as a currentcolor layer at StyleLayerPriority.Preflight: every border
    // colour utility outranks it, a stylesheet class through the element's class projection, which is created
    // here for that reason. An element with no width utility keeps its theme's border, as a control's does.
    internal static class PreflightBorderColor
    {
        private static readonly StyleLonghandSet s_widths = StyleArbitraryLonghands.Of(ArbitraryProperty.BorderWidth);

        private static readonly ArbitraryStyle s_currentColor =
            new(ArbitraryProperty.BorderColor, Color.white, ColorReference.Current);

        internal static void Sync(VisualElement element, string[] classNames)
        {
            var applied = StyleArbitraryValueResolver.HasLayerAt(element, ArbitraryProperty.BorderColor,
                StyleLayerPriority.Preflight);
            var wanted = SetsBorderWidth(classNames);
            if (wanted == applied)
            {
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
        }

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
