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
        // [&>*]: child-combinator variant — an ambient blanket rule the PARENT imposes on every direct child.
        // Ranked just above the base utility and below every condition the child declares itself (even mere
        // sibling position), so the child's own layers win. A distinct priority is also required for
        // correctness — the arbitrary-value LayerMap is a per-property SortedList set by an INDEXER, so reusing
        // Base for a [&>*]: arbitrary payload would let a child's own base arbitrary utility and this inherited
        // one clobber the same slot.
        public const int ChildVariant = 5;

        #region Relational
        // group-*/peer-* states get DISTINCT priorities so two on the same property (e.g. group-hover +
        // group-active) occupy separate layers — clearing one must not remove the other.
        public const int GroupHover = 27;
        public const int GroupFocus = 28;
        public const int GroupActive = 29;
        public const int PeerHover = 30;
        public const int PeerFocus = 31;
        public const int PeerActive = 32;
        public const int GroupFocusWithin = 33;
        public const int PeerFocusWithin = 34;
        public const int PeerChecked = 35;
        // The disabled states top the band, as disabled: tops the element-state one.
        public const int GroupDisabled = 36;
        public const int PeerDisabled = 37;
        #endregion

        #region Structural
        // first:/last:/only:/odd:/even: — Tailwind registers them right after group/peer and before checked.
        // The nth-* and arbitrary [&:…]: forms sit further up; StyleStructuralVariantClass.PriorityOf picks.
        public const int Structural = 38;
        #endregion

        #region Element state
        // Element-local interaction states. checked: ranks BELOW hover/focus/active on a same-property tie
        // — the reference cascade emits checked before the interaction states, and later-emitted wins a
        // tie — so a checked control's `checked:` value never beats a concurrent `hover:` / `focus:` /
        // `active:` value (but still above the relational group-/peer- layers).
        public const int Checked = 39;
        public const int Hover = 40;
        public const int Focus = 50;
        public const int FocusVisible = 55;
        public const int Active = 60;
        // disabled: ranks above active on a same-property tie — Tailwind emits it after active.
        public const int Disabled = 65;
        #endregion

        #region Has and Attribute
        // has-[...] (a DESCENDANT condition) and data-[...] / aria-[...] (the element's OWN carried attribute)
        // rank above every element state on a same-property tie: Tailwind registers has, aria and data after
        // disabled, and their selectors carry the same specificity as a pseudo-class, so the later one wins.
        public const int Has = 70;
        public const int Attribute = 71;
        // nth-N: / nth-last-N: — Tailwind registers the functional nth variants right after data.
        public const int Nth = 72;
        #endregion

        #region Supports, Responsive and Theme
        // Tailwind registers supports, then the breakpoints, then dark after data, and a media or feature query
        // adds no specificity to the rule it wraps, so these rank above every layer declared before them here:
        // md:w-[10px] beats hover:w-[20px] on a hovered element wider than md.
        // supports-[prop:value] is STATIC in UI Toolkit (always-applied when well-formed; see
        // StyleSupportsVariantClass), so this layer never toggles off at runtime; the priority only orders it
        // against other layers on the same property.
        public const int Supports = 75;
        // Responsive breakpoints: a larger min-width wins while active.
        public const int ResponsiveSm = 76;
        public const int ResponsiveMd = 77;
        public const int ResponsiveLg = 78;
        public const int ResponsiveXl = 79;
        public const int Responsive2xl = 80;
        public const int Dark = 85;
        #endregion

        #region Arbitrary selector
        // [&:nth-child(N)]: and the other [&:…]: structural forms. Tailwind orders every arbitrary variant after
        // all the registered ones, so this is the highest ordinary layer.
        public const int ArbitrarySelector = 90;
        #endregion

        #region Important
        // Floor of the important band (!utility / utility!). An important payload layers at Important plus
        // its own variant priority, so the whole band sits above every ordinary layer (ArbitrarySelector, the highest,
        // is 90) while important-versus-important keeps the ordinary ladder: dark:!w-[10px] beats
        // !w-[20px], the same way dark:w-[10px] beats w-[20px].
        public const int Important = 100;

        // The layer an important payload occupies given the priority it would otherwise have had.
        public static int ImportantOf(int priority) => Important + priority;
        #endregion

        internal static int ForVariant(StyleVariantKind kind) => s_forVariant[kind];

        private static readonly VariantKindTable<int> s_forVariant = new(
            (StyleVariantKind.Hover, Hover),
            (StyleVariantKind.Sm, ResponsiveSm),
            (StyleVariantKind.Md, ResponsiveMd),
            (StyleVariantKind.Lg, ResponsiveLg),
            (StyleVariantKind.Xl, ResponsiveXl),
            (StyleVariantKind.Xxl, Responsive2xl),
            (StyleVariantKind.Dark, Dark),
            (StyleVariantKind.GroupHover, GroupHover),
            (StyleVariantKind.GroupFocus, GroupFocus),
            (StyleVariantKind.GroupFocusWithin, GroupFocusWithin),
            (StyleVariantKind.GroupActive, GroupActive),
            (StyleVariantKind.PeerHover, PeerHover),
            (StyleVariantKind.PeerFocus, PeerFocus),
            (StyleVariantKind.PeerFocusWithin, PeerFocusWithin),
            (StyleVariantKind.PeerActive, PeerActive),
            (StyleVariantKind.PeerChecked, PeerChecked),
            (StyleVariantKind.Focus, Focus),
            (StyleVariantKind.FocusVisible, FocusVisible),
            (StyleVariantKind.Active, Active),
            (StyleVariantKind.Checked, Checked),
            (StyleVariantKind.Disabled, Disabled),
            (StyleVariantKind.GroupDisabled, GroupDisabled),
            (StyleVariantKind.PeerDisabled, PeerDisabled));
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
        // origin-[33%_75%] -> transform-origin: <x> <y>. One class carries both components, so unlike the
        // axis pairs above it is one property with a pair payload (Value/Unit + Value2/Unit2). A single
        // component means the x alone, and the y is the 50% CSS leaves it at.
        TransformOrigin,
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
        // brightness has no UITK filter type; it renders through a first-party custom-filter shader
        // (BuiltInFilterDefinitions.Brightness) as a FilterFunctionType.Custom function. The shader multiplies
        // unclamped, so the full CSS range (N >= 0, including over-bright N > 1) is representable. Value = the
        // multiplier N (brightness-50 -> 0.5, brightness-150 -> 1.5).
        FilterBrightness, // brightness-[.5]    -> filter: <custom brightness>(.5) (Value = multiplier N >= 0)
        // saturate has no UITK filter type either; it renders through a first-party custom-filter shader
        // (BuiltInFilterDefinitions.Saturate) as a FilterFunctionType.Custom function that lerps toward
        // luminance natively, so Value is the RAW saturation fraction N (no 1-N complement) and over-saturation
        // (N > 1) is supported.
        FilterSaturate,   // saturate-[.5]      -> filter: <custom saturate>(.5)   (Value = saturation N >= 0)
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

        // Flex grow / shrink factors (StyleFloat, unitless). The USS vocabulary stops at 0 and 1, so no
        // class expresses any other ratio. Parsed by StyleTransformValueParser rather than through the
        // prefix table, which is the length grammar — see TryParseFlexFactor.
        FlexGrow,     // grow-[2]         -> flex-grow
        FlexShrink,   // shrink-[2]       -> flex-shrink

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
        // The second component of a pair-valued property (TransformOrigin's y), paired with Unit2. The axis
        // pairs above are two properties over one engine property instead, because each half is written by
        // its own class; a pair here arrives from ONE class and has no spelling that sets half of it.
        public float Value2 { get; }
        public LengthUnit Unit2 { get; }
        // Color payload for color properties; default for length/angle/custom properties.
        public Color Color { get; }
        // Payload for FilterCustom (the registered name, its definition, and the resolved arguments);
        // null for every other property.
        public CustomFilterValue? Custom { get; }

        // Creates a length/angle result.
        public ArbitraryStyle(ArbitraryProperty property, float value, LengthUnit unit)
        {
            Property = property;
            Value = value;
            Unit = unit;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Color = default;
            Custom = null;
        }

        // Creates a pair-valued length result.
        public ArbitraryStyle(ArbitraryProperty property, float value, LengthUnit unit,
            float value2, LengthUnit unit2)
        {
            Property = property;
            Value = value;
            Unit = unit;
            Value2 = value2;
            Unit2 = unit2;
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
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Custom = null;
        }

        // Creates a FilterCustom result.
        public ArbitraryStyle(ArbitraryProperty property, CustomFilterValue custom)
        {
            Property = property;
            Custom = custom;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Color = default;
        }
    }

    // The resolved payload for a filter-[name:args] custom filter token: the registered NAME (the
    // per-name layer-stack key, and the ONLY field the clear path reads — a clear synthesized for a
    // no-longer-registered name carries a null Definition and empty Args), the FilterFunctionDefinition
    // VelvetFilters.Register stored under that name, and the arguments in declaration order — the
    // explicitly supplied segments followed by a tail padded from the declaration's defaults, so the
    // composed function always carries the full declared parameter count (an under-filled function
    // reads stale material-property state at render time instead of the declared defaults).
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
