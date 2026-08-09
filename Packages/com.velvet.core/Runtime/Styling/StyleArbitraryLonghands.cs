namespace Velvet
{
    // The USS longhands each arbitrary-value property writes, in the vocabulary StyleUtilityProperties.g.cs
    // pins. Two vocabularies describe one cascade — an inline layer and a USS class contend for the same
    // storage slot — and only this translation lets StyleClassProjection compare them.
    //
    // The filter family and FilterCustom map to nothing on purpose. Filters COMPOSE rather than override, so
    // "a class already claims filter" is not a reason to drop a layer; and re-resolving a filter property
    // hands the composed list to the transition driver and restarts its tween. Their string-keyed identity
    // (a registered custom-filter name, not an ArbitraryProperty) therefore never has to be modelled at all.
    internal static class StyleArbitraryLonghands
    {
        private static readonly StyleLonghandSet[] s_sets = Build();

        // What property writes, or the empty set for a property held out of the cascade comparison.
        public static StyleLonghandSet Of(ArbitraryProperty property)
        {
            var index = (int)property;
            return index >= 0 && index < s_sets.Length ? s_sets[index] : StyleLonghandSet.Empty;
        }

        private static StyleLonghandSet[] Build()
        {
            var sets = new StyleLonghandSet[System.Enum.GetValues(typeof(ArbitraryProperty)).Length];

            Set(sets, ArbitraryProperty.Width, StyleLonghand.Width);
            Set(sets, ArbitraryProperty.Height, StyleLonghand.Height);
            Set(sets, ArbitraryProperty.MinWidth, StyleLonghand.MinWidth);
            Set(sets, ArbitraryProperty.MinHeight, StyleLonghand.MinHeight);
            Set(sets, ArbitraryProperty.MaxWidth, StyleLonghand.MaxWidth);
            Set(sets, ArbitraryProperty.MaxHeight, StyleLonghand.MaxHeight);
            Set(sets, ArbitraryProperty.Size, StyleLonghand.Width, StyleLonghand.Height);
            Set(sets, ArbitraryProperty.FlexBasis, StyleLonghand.FlexBasis);
            Set(sets, ArbitraryProperty.FlexGrow, StyleLonghand.FlexGrow);
            Set(sets, ArbitraryProperty.FlexShrink, StyleLonghand.FlexShrink);

            Set(sets, ArbitraryProperty.Top, StyleLonghand.Top);
            Set(sets, ArbitraryProperty.Right, StyleLonghand.Right);
            Set(sets, ArbitraryProperty.Bottom, StyleLonghand.Bottom);
            Set(sets, ArbitraryProperty.Left, StyleLonghand.Left);
            Set(sets, ArbitraryProperty.Inset,
                StyleLonghand.Top, StyleLonghand.Right, StyleLonghand.Bottom, StyleLonghand.Left);
            Set(sets, ArbitraryProperty.InsetX, StyleLonghand.Left, StyleLonghand.Right);
            Set(sets, ArbitraryProperty.InsetY, StyleLonghand.Top, StyleLonghand.Bottom);

            Set(sets, ArbitraryProperty.PaddingTop, StyleLonghand.PaddingTop);
            Set(sets, ArbitraryProperty.PaddingRight, StyleLonghand.PaddingRight);
            Set(sets, ArbitraryProperty.PaddingBottom, StyleLonghand.PaddingBottom);
            Set(sets, ArbitraryProperty.PaddingLeft, StyleLonghand.PaddingLeft);
            Set(sets, ArbitraryProperty.Padding, StyleLonghand.PaddingTop, StyleLonghand.PaddingRight,
                StyleLonghand.PaddingBottom, StyleLonghand.PaddingLeft);
            Set(sets, ArbitraryProperty.PaddingX, StyleLonghand.PaddingLeft, StyleLonghand.PaddingRight);
            Set(sets, ArbitraryProperty.PaddingY, StyleLonghand.PaddingTop, StyleLonghand.PaddingBottom);

            Set(sets, ArbitraryProperty.MarginTop, StyleLonghand.MarginTop);
            Set(sets, ArbitraryProperty.MarginRight, StyleLonghand.MarginRight);
            Set(sets, ArbitraryProperty.MarginBottom, StyleLonghand.MarginBottom);
            Set(sets, ArbitraryProperty.MarginLeft, StyleLonghand.MarginLeft);
            Set(sets, ArbitraryProperty.Margin, StyleLonghand.MarginTop, StyleLonghand.MarginRight,
                StyleLonghand.MarginBottom, StyleLonghand.MarginLeft);
            Set(sets, ArbitraryProperty.MarginX, StyleLonghand.MarginLeft, StyleLonghand.MarginRight);
            Set(sets, ArbitraryProperty.MarginY, StyleLonghand.MarginTop, StyleLonghand.MarginBottom);

            Set(sets, ArbitraryProperty.BorderRadius,
                StyleLonghand.BorderTopLeftRadius, StyleLonghand.BorderTopRightRadius,
                StyleLonghand.BorderBottomLeftRadius, StyleLonghand.BorderBottomRightRadius);
            Set(sets, ArbitraryProperty.BorderTopRadius,
                StyleLonghand.BorderTopLeftRadius, StyleLonghand.BorderTopRightRadius);
            Set(sets, ArbitraryProperty.BorderRightRadius,
                StyleLonghand.BorderTopRightRadius, StyleLonghand.BorderBottomRightRadius);
            Set(sets, ArbitraryProperty.BorderBottomRadius,
                StyleLonghand.BorderBottomLeftRadius, StyleLonghand.BorderBottomRightRadius);
            Set(sets, ArbitraryProperty.BorderLeftRadius,
                StyleLonghand.BorderTopLeftRadius, StyleLonghand.BorderBottomLeftRadius);
            Set(sets, ArbitraryProperty.BorderTopLeftRadius, StyleLonghand.BorderTopLeftRadius);
            Set(sets, ArbitraryProperty.BorderTopRightRadius, StyleLonghand.BorderTopRightRadius);
            Set(sets, ArbitraryProperty.BorderBottomLeftRadius, StyleLonghand.BorderBottomLeftRadius);
            Set(sets, ArbitraryProperty.BorderBottomRightRadius, StyleLonghand.BorderBottomRightRadius);

            Set(sets, ArbitraryProperty.BorderWidth, StyleLonghand.BorderTopWidth, StyleLonghand.BorderRightWidth,
                StyleLonghand.BorderBottomWidth, StyleLonghand.BorderLeftWidth);
            Set(sets, ArbitraryProperty.BorderTopWidth, StyleLonghand.BorderTopWidth);
            Set(sets, ArbitraryProperty.BorderRightWidth, StyleLonghand.BorderRightWidth);
            Set(sets, ArbitraryProperty.BorderBottomWidth, StyleLonghand.BorderBottomWidth);
            Set(sets, ArbitraryProperty.BorderLeftWidth, StyleLonghand.BorderLeftWidth);
            Set(sets, ArbitraryProperty.BorderColor, StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor,
                StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor);

            Set(sets, ArbitraryProperty.FontSize, StyleLonghand.FontSize);
            Set(sets, ArbitraryProperty.LetterSpacing, StyleLonghand.LetterSpacing);
            Set(sets, ArbitraryProperty.TextColor, StyleLonghand.Color);
            Set(sets, ArbitraryProperty.BackgroundColor, StyleLonghand.BackgroundColor);

            // The three scale layers and the two translate axes each compose into one inline slot, so they
            // share the slot's longhand and are masked or kept together.
            Set(sets, ArbitraryProperty.Scale, StyleLonghand.Scale);
            Set(sets, ArbitraryProperty.ScaleX, StyleLonghand.Scale);
            Set(sets, ArbitraryProperty.ScaleY, StyleLonghand.Scale);
            Set(sets, ArbitraryProperty.TranslateX, StyleLonghand.Translate);
            Set(sets, ArbitraryProperty.TranslateY, StyleLonghand.Translate);
            Set(sets, ArbitraryProperty.Rotate, StyleLonghand.Rotate);
            Set(sets, ArbitraryProperty.TransformOrigin, StyleLonghand.TransformOrigin);

            Set(sets, ArbitraryProperty.Opacity, StyleLonghand.Opacity);
            Set(sets, ArbitraryProperty.AspectRatio, StyleLonghand.AspectRatio);
            Set(sets, ArbitraryProperty.TransitionDuration, StyleLonghand.TransitionDuration);

            return sets;
        }

        private static void Set(StyleLonghandSet[] sets, ArbitraryProperty property, params StyleLonghand[] longhands)
        {
            var set = StyleLonghandSet.Empty;
            foreach (var longhand in longhands)
            {
                set = set.Union(StyleLonghandSet.Of(longhand));
            }
            sets[(int)property] = set;
        }
    }
}
