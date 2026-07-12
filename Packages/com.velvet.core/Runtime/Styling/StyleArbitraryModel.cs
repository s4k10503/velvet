using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Priorities for arbitrary-value layers (see StyleArbitraryValueResolver). When several
    // sources set the same property (e.g. w-[80px] hover:w-[200px] active:w-[100px]), the highest
    // priority wins; when it is cleared (the state turns off) the next-highest is re-applied — mirroring the
    // CSS cascade where a state rule layers over the base rather than replacing it.
    internal static class StyleLayerPriority
    {
        public const int Base = 0;
        // Structural (child-position) variants first:/last:/odd:/even:/nth-child:. Velvet orders these inline
        // layers by how strong / intentional the activating condition is; a position in the sibling list is
        // the WEAKEST condition, so it sits just above the base utility and yields to every layer below it
        // (the context gates, the element's own has-/attribute conditions, and its interaction state).
        public const int Structural = 10;
        // Responsive breakpoints: a larger min-width wins while active.
        public const int ResponsiveSm = 11;
        public const int ResponsiveMd = 12;
        public const int ResponsiveLg = 13;
        public const int ResponsiveXl = 14;
        public const int Responsive2xl = 15;
        // supports-[prop:value] feature query. A feature query and a media query are sibling conditional
        // group rules in CSS (the wrapper adds no specificity), so it sits in the same band as the
        // responsive breakpoints — just above them, below theme/state. In UI Toolkit it is STATIC
        // (always-applied when well-formed; see StyleSupportsVariantClass), so this layer never toggles
        // off at runtime; the priority only orders it against other layers on the same property.
        public const int Supports = 16;
        public const int Dark = 20;
        // has-[...] (the element styled by a DESCENDANT condition — a descendant is checked / focused or
        // carries a class). A semantic condition on the element's own subtree, treated as a stronger intent
        // than the ambient context, so it wins over the base utility, the positional structural layer, and
        // the responsive / supports / theme context gates. It yields to the relational group-/peer- layers
        // (styled by ANOTHER element's interaction) and to the element's own interaction state below.
        public const int Has = 25;
        // data-[...] / aria-[...] (the element styled by its OWN carried attribute). The most direct element
        // condition — its own declared state — so it sits just above the has- (descendant) layer and
        // likewise wins over the context gates, while still yielding to the relational group-/peer- and the
        // element's interaction state layers below.
        public const int Attribute = 26;
        // group-*/peer-* states get DISTINCT priorities so two on the same property (e.g. group-hover +
        // group-active) occupy separate layers — clearing one must not remove the other.
        public const int GroupHover = 30;
        public const int GroupFocus = 31;
        public const int GroupActive = 32;
        public const int PeerHover = 33;
        public const int PeerFocus = 34;
        public const int PeerActive = 35;
        public const int GroupFocusWithin = 36;
        public const int PeerFocusWithin = 37;
        public const int PeerChecked = 38;
        public const int Hover = 40;
        public const int Focus = 50;
        public const int FocusVisible = 55;
        public const int Active = 60;
        public const int Checked = 65;

        // The important modifier (!utility / utility!): the highest layer, so an important utility
        // wins over the base and every state/variant layer — the inline-style stand-in for CSS !important
        // (inline styles already beat USS class rules in UI Toolkit). A single shared layer, so stacking
        // two important utilities on the same property is last-wins (a documented edge).
        public const int Important = 100;

        // The layer priority a stacked variant's inner kind contributes; a composed arbitrary leaf
        // (dark:hover:w-[200px]) layers at max(outer, inner) so it sits above either variant alone.
        internal static int ForVariant(StyleVariantKind kind) => kind switch
        {
            StyleVariantKind.Sm => ResponsiveSm,
            StyleVariantKind.Md => ResponsiveMd,
            StyleVariantKind.Lg => ResponsiveLg,
            StyleVariantKind.Xl => ResponsiveXl,
            StyleVariantKind.Xxl => Responsive2xl,
            StyleVariantKind.Dark => Dark,
            StyleVariantKind.GroupHover => GroupHover,
            StyleVariantKind.GroupFocus => GroupFocus,
            StyleVariantKind.GroupFocusWithin => GroupFocusWithin,
            StyleVariantKind.GroupActive => GroupActive,
            StyleVariantKind.PeerHover => PeerHover,
            StyleVariantKind.PeerFocus => PeerFocus,
            StyleVariantKind.PeerFocusWithin => PeerFocusWithin,
            StyleVariantKind.PeerActive => PeerActive,
            StyleVariantKind.PeerChecked => PeerChecked,
            StyleVariantKind.Focus => Focus,
            StyleVariantKind.FocusVisible => FocusVisible,
            StyleVariantKind.Active => Active,
            StyleVariantKind.Checked => Checked,
            _ => Hover,
        };
    }
    // The style property an arbitrary-value utility targets (e.g. w-[120px] → Width,
    // bg-[#fff] → background color, rotate-[45deg] → rotation). Shorthand members fan out to
    // several edges.
    internal enum ArbitraryProperty
    {
        #region Size
        Width,
        Height,
        MinWidth,
        MinHeight,
        MaxWidth,
        MaxHeight,
        #endregion

        #region Position
        Top,
        Right,
        Bottom,
        Left,
        Inset,    // top + right + bottom + left
        InsetX,   // left + right
        InsetY,   // top + bottom
        #endregion

        #region Padding
        PaddingTop,
        PaddingRight,
        PaddingBottom,
        PaddingLeft,
        Padding,  // all four edges
        PaddingX, // left + right
        PaddingY, // top + bottom
        #endregion

        #region Margin
        MarginTop,
        MarginRight,
        MarginBottom,
        MarginLeft,
        Margin,   // all four edges
        MarginX,  // left + right
        MarginY,  // top + bottom
        #endregion

        #region Border radius
        BorderRadius, // all four corners at once
        BorderTopRadius,    // top-left + top-right
        BorderRightRadius,  // top-right + bottom-right
        BorderBottomRadius, // bottom-left + bottom-right
        BorderLeftRadius,   // top-left + bottom-left
        BorderTopLeftRadius,
        BorderTopRightRadius,
        BorderBottomLeftRadius,
        BorderBottomRightRadius,
        #endregion

        #region Border width (StyleFloat)
        BorderWidth,       // all four sides
        BorderTopWidth,
        BorderRightWidth,
        BorderBottomWidth,
        BorderLeftWidth,
        #endregion

        #region Font
        FontSize,
        LetterSpacing,
        #endregion

        #region Color
        TextColor,
        BackgroundColor,
        BorderColor, // all four sides
        #endregion

        #region Transform (independent UITK properties; not StyleLength)
        // The uniform Scale and per-axis ScaleX/ScaleY all compose onto the single inline `scale` via
        // ApplyCombinedScale: a per-axis value wins for its axis, the uniform scale-[..] is the fallback for
        // an axis not set explicitly, and a wholly-missing axis defaults to 1 (identity). So scale-[1.4] alone
        // == (1.4,1.4), scale-x-[.5] alone == (.5,1), and scale-[1.4]+scale-x-[.5] == (.5,1.4).
        Scale,        // scale-[1.4]      -> scale: <v> <v>      (Value = unitless factor; per-axis fallback)
        ScaleX,       // scale-x-[.5]     -> scale x axis        (Value = unitless factor, merges with y)
        ScaleY,       // scale-y-[1.5]    -> scale y axis        (Value = unitless factor, merges with x)
        TranslateX,   // translate-x-[Np] -> translate x axis    (Value + Unit, merges with y)
        TranslateY,   // translate-y-[Np] -> translate y axis    (Value + Unit, merges with x)
        Rotate,       // rotate-[45deg]   -> rotate: <deg>       (Value = degrees)
        #endregion

        // Effects (unitless StyleFloat, routed via FloatSetters)
        Opacity,      // opacity-[.37]    -> opacity: <0..1>      (Value = unitless factor)

        // Aspect ratio (StyleRatio; not StyleLength/Color/Float, handled out-of-band like transforms)
        AspectRatio,  // aspect-[4/3]     -> aspect-ratio: <w/h>  (Value = the divided ratio)

        #region Filters (UITK USS filter:)
        // Each filter type is its own layer, composed into one StyleList<FilterFunction> by ApplyCombinedFilter
        // so several filter-* utilities on one element merge rather than overwrite.
        FilterBlur,       // blur-[6px]         -> filter: blur(6px)        (Value = px)
        FilterContrast,   // contrast-[1.4]     -> filter: contrast(1.4)    (Value = unitless)
        FilterGrayscale,  // grayscale-[.6]     -> filter: grayscale(.6)    (Value = 0..1)
        FilterHueRotate,  // hue-rotate-[90deg] -> filter: hue-rotate(90deg)(Value = degrees)
        FilterInvert,     // invert-[1]         -> filter: invert(1)        (Value = 0..1)
        FilterSepia,      // sepia-[1]          -> filter: sepia(1)         (Value = 0..1)
        // brightness has no UITK filter type, but the built-in Tint multiplies the rendered RGB by a color, so
        // brightness(N) == Tint(N,N,N). UITK clamps the Tint factor to [0,1], so only the darken range N<=1 is
        // representable (the parser rejects N>1). Value = the multiplier N (brightness-50 -> 0.5).
        FilterBrightness, // brightness-[.5]    -> filter: tint(.5,.5,.5)   (Value = multiplier 0..1)
        // saturate has no UITK filter type either, but saturate(N) == grayscale(1-N) for N in 0..1 (both lerp
        // toward luminance). Value = the saturation fraction N; BuildFilter emits grayscale(1-N). Over-saturation
        // (N>1) has no UITK filter and is not supported (the parser rejects saturate>100).
        FilterSaturate,   // saturate-[.5]      -> filter: grayscale(.5)    (Value = saturation 0..1)
        // filter-[name:args] resolves a VelvetFilters.Register-ed custom filter (VelvetFilters.cs). Unlike
        // every filter above, it is NOT composed via s_filterOrder / the property-keyed LayerMap: each
        // registered NAME gets its own priority stack (LayerMap.Customs) so two different custom filters,
        // or a base layer and a hover layer of the SAME name, never clobber each other. The Custom field
        // on ArbitraryStyle carries the resolved definition and parsed arguments; Value/Unit/Color are unused.
        FilterCustom,     // filter-[dissolve:0.4] -> filter: <custom function>(0.4) (payload = Custom)
        #endregion

        // Size shorthand (StyleLength; fans out to width + height, like Inset).
        // NB shorthand layers are independent of their longhand counterparts (the same as
        // Inset vs Top/Left, Padding vs PaddingX): mixing size-[..] with w-[..]/h-[..] on one
        // element and then removing the longhand does not re-resolve the surviving shorthand.
        // Use one or the other on a given axis.
        Size,         // size-[40px]      -> width + height

        // Flex basis (StyleLength)
        FlexBasis,    // basis-[120px]    -> flex-basis

        // Transition (StyleList<TimeValue>; handled out-of-band like the filter list)
        TransitionDuration,   // duration-[400ms] -> transition-duration. Value carries SECONDS.
    }

    // A parsed arbitrary-value result: the target Property plus its length payload
    // (Value + Unit), its Color, or (FilterCustom only) its Custom payload.
    internal readonly struct ArbitraryStyle
    {
        public ArbitraryProperty Property { get; }
        // Numeric magnitude for length/angle properties (paired with Unit); 0 for color/custom properties.
        public float Value { get; }
        public LengthUnit Unit { get; }
        // Color payload for color properties; default for length/angle/custom properties.
        public Color Color { get; }
        // Payload for FilterCustom (the registered name, its definition, and the parsed arguments);
        // null for every other property.
        public CustomFilterValue Custom { get; }

        // Creates a length/angle result.
        public ArbitraryStyle(ArbitraryProperty property, float value, LengthUnit unit)
        {
            Property = property;
            Value = value;
            Unit = unit;
            Color = default;
            Custom = null;
        }

        // Creates a color result.
        public ArbitraryStyle(ArbitraryProperty property, Color color)
        {
            Property = property;
            Color = color;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Custom = null;
        }

        // Creates a FilterCustom result.
        public ArbitraryStyle(ArbitraryProperty property, CustomFilterValue custom)
        {
            Property = property;
            Custom = custom;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Color = default;
        }
    }

    // The resolved payload for a filter-[name:args] custom filter token: the registered NAME (the
    // LayerMap.Customs stack key, and what a Clear needs to remove precisely this name's layer without
    // disturbing another custom filter stacked on the same element), the FilterFunctionDefinition
    // VelvetFilters.Register stored under that name, and the colon-separated arguments in token order
    // (empty when the token was a bare name, e.g. filter-[dissolve], so the definition's own declared
    // parameter defaults take effect at render time).
    internal sealed class CustomFilterValue
    {
        public readonly string Name;
        public readonly FilterFunctionDefinition Definition;
        public readonly FilterParameter[] Args;

        public CustomFilterValue(string name, FilterFunctionDefinition definition, FilterParameter[] args)
        {
            Name = name;
            Definition = definition;
            Args = args;
        }
    }
}
