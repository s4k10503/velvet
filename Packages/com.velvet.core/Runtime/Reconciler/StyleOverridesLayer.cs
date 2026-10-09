#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // Writes a StyleOverrides member whose slot a utility's arbitrary value can also write (bg-[#…], text-[#…],
    // slice-[…]). Both are inline, so a plain write would leave whichever came last; registering the member as
    // a layer at StyleLayerPriority.InlineStyle ranks it the way React's style prop ranks against className,
    // whatever the write order. A keyword value (Initial, Auto, …) is the same layer carrying the keyword; a
    // Null keyword or no value is no override at all.
    internal static class StyleOverridesLayer
    {
        public static void Write(VisualElement element, ArbitraryProperty property, StyleColor? value)
        {
            if (value is not { keyword: not StyleKeyword.Null } color)
            {
                StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Apply(element, color.keyword == StyleKeyword.Undefined
                ? new ArbitraryStyle(property, color.value)
                : new ArbitraryStyle(property, color.keyword), StyleLayerPriority.InlineStyle);
        }

        public static void Write(VisualElement element, ArbitraryProperty property, StyleInt? value)
        {
            if (value is not { keyword: not StyleKeyword.Null } inset)
            {
                StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Apply(element, inset.keyword == StyleKeyword.Undefined
                ? new ArbitraryStyle(property, inset.value, LengthUnit.Pixel)
                : new ArbitraryStyle(property, inset.keyword), StyleLayerPriority.InlineStyle);
        }

        public static void Write(VisualElement element, ArbitraryProperty property, StyleFloat? value)
        {
            if (value is not { keyword: not StyleKeyword.Null } factor)
            {
                StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Apply(element, factor.keyword == StyleKeyword.Undefined
                ? new ArbitraryStyle(property, factor.value, LengthUnit.Pixel)
                : new ArbitraryStyle(property, factor.keyword), StyleLayerPriority.InlineStyle);
        }
    }
}
