#nullable enable
using UnityEngine.UIElements;

namespace Velvet
{
    // Writes StyleOverrides, Velvet's style prop. A member whose property a utility can also write is registered
    // as a layer at StyleLayerPriority.InlineStyle, which ranks it the way React's style prop ranks against
    // className whatever the write order; a keyword value (Initial, Auto, …) is the same layer carrying the
    // keyword, and a Null keyword or no value is no override at all. The background image is ranked by its own
    // record (StyleArbitraryValueResolver.WriteBackgroundImageOverride).
    internal static class StyleOverridesLayer
    {
        // Writes each member that differs between the two; on mount oldStyles is StyleOverrides.Empty.
        public static void Diff(VisualElement element, StyleOverrides oldStyles, StyleOverrides newStyles)
        {
            if (!Equals(oldStyles.BackgroundImage, newStyles.BackgroundImage))
            {
                StyleArbitraryValueResolver.WriteBackgroundImageOverride(element, newStyles.BackgroundImage);
            }
            if (!Equals(oldStyles.BackgroundRepeat, newStyles.BackgroundRepeat))
            {
                Write(element, ArbitraryProperty.BackgroundRepeat, newStyles.BackgroundRepeat);
            }
            if (!Equals(oldStyles.UnitySliceType, newStyles.UnitySliceType))
            {
                Write(element, ArbitraryProperty.SliceType, newStyles.UnitySliceType);
            }
            if (!Equals(oldStyles.UnitySliceTop, newStyles.UnitySliceTop))
            {
                Write(element, ArbitraryProperty.SliceTop, newStyles.UnitySliceTop);
            }
            if (!Equals(oldStyles.UnitySliceRight, newStyles.UnitySliceRight))
            {
                Write(element, ArbitraryProperty.SliceRight, newStyles.UnitySliceRight);
            }
            if (!Equals(oldStyles.UnitySliceBottom, newStyles.UnitySliceBottom))
            {
                Write(element, ArbitraryProperty.SliceBottom, newStyles.UnitySliceBottom);
            }
            if (!Equals(oldStyles.UnitySliceLeft, newStyles.UnitySliceLeft))
            {
                Write(element, ArbitraryProperty.SliceLeft, newStyles.UnitySliceLeft);
            }
            if (!Equals(oldStyles.UnitySliceScale, newStyles.UnitySliceScale))
            {
                Write(element, ArbitraryProperty.SliceScale, newStyles.UnitySliceScale);
            }
            if (!Equals(oldStyles.BackgroundColor, newStyles.BackgroundColor))
            {
                Write(element, ArbitraryProperty.BackgroundColor, newStyles.BackgroundColor);
            }
            if (!Equals(oldStyles.Color, newStyles.Color))
            {
                Write(element, ArbitraryProperty.TextColor, newStyles.Color);
            }
        }

        private static void Write(VisualElement element, ArbitraryProperty property, StyleColor? value)
            => Write(element, property, value?.keyword, new ArbitraryStyle(property, value.GetValueOrDefault().value));

        private static void Write(VisualElement element, ArbitraryProperty property, StyleInt? value)
            => Write(element, property, value?.keyword,
                new ArbitraryStyle(property, value.GetValueOrDefault().value, LengthUnit.Pixel));

        private static void Write(VisualElement element, ArbitraryProperty property, StyleFloat? value)
            => Write(element, property, value?.keyword,
                new ArbitraryStyle(property, value.GetValueOrDefault().value, LengthUnit.Pixel));

        // Value carries the x repeat and Value2 the y repeat.
        private static void Write(VisualElement element, ArbitraryProperty property, StyleBackgroundRepeat? value)
            => Write(element, property, value?.keyword, new ArbitraryStyle(property,
                (float)value.GetValueOrDefault().value.x, (float)value.GetValueOrDefault().value.y, 0f, 0f));

        private static void Write(VisualElement element, ArbitraryProperty property, StyleEnum<SliceType>? value)
            => Write(element, property, value?.keyword,
                new ArbitraryStyle(property, (float)value.GetValueOrDefault().value, LengthUnit.Pixel));

        private static void Write(VisualElement element, ArbitraryProperty property, StyleKeyword? keyword,
            in ArbitraryStyle value)
        {
            if (keyword is null or StyleKeyword.Null)
            {
                StyleArbitraryValueResolver.Clear(element, property, StyleLayerPriority.InlineStyle);
                return;
            }
            StyleArbitraryValueResolver.Apply(element,
                keyword == StyleKeyword.Undefined ? value : new ArbitraryStyle(property, keyword.Value),
                StyleLayerPriority.InlineStyle);
        }
    }
}
