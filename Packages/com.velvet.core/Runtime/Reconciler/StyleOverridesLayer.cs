#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Writes a StyleOverrides member whose slot a utility's arbitrary value can also write (bg-[#…], text-[#…],
    // slice-[…]). Both are inline, so a plain write would leave whichever came last; registering the member as
    // a layer at StyleLayerPriority.InlineStyle ranks it the way React's style prop ranks against className,
    // whatever the write order. A keyword value (Initial, Auto, …) has no layer form and is written as it is,
    // so a utility written after it replaces it.
    internal static class StyleOverridesLayer
    {
        public static void Write(VisualElement element, ArbitraryProperty property, StyleColor? value,
            Action<IStyle, StyleColor> writeKeyword)
        {
            if (value is { keyword: StyleKeyword.Undefined } color)
            {
                StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(property, color.value),
                    StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
            if (value is { keyword: not StyleKeyword.Null } keyword)
            {
                writeKeyword(element.style, keyword);
            }
        }

        public static void Write(VisualElement element, ArbitraryProperty property, StyleInt? value,
            Action<IStyle, StyleInt> writeKeyword)
        {
            if (value is { keyword: StyleKeyword.Undefined } inset)
            {
                StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(property, inset.value, LengthUnit.Pixel),
                    StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
            if (value is { keyword: not StyleKeyword.Null } keyword)
            {
                writeKeyword(element.style, keyword);
            }
        }

        public static void Write(VisualElement element, ArbitraryProperty property, StyleFloat? value,
            Action<IStyle, StyleFloat> writeKeyword)
        {
            if (value is { keyword: StyleKeyword.Undefined } factor)
            {
                StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(property, factor.value, LengthUnit.Pixel),
                    StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
            if (value is { keyword: not StyleKeyword.Null } keyword)
            {
                writeKeyword(element.style, keyword);
            }
        }
    }
}
