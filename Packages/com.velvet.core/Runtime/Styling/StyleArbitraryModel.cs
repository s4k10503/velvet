using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Priorities for arbitrary-value layers (see StyleArbitraryValueResolver) and for the class projection.
    // When several sources set the same property (e.g. w-[80px] hover:w-[200px] active:w-[100px]), the
    // highest priority wins; when it is cleared (the state turns off) the next-highest is re-applied —
    // mirroring the CSS cascade where a state rule layers over the base rather than replacing it.
    //
    // A priority is the rank Tailwind's generated CSS gives the rule, packed so that comparing two longs
    // compares the ranks: an important flag, then the rule's specificity, then the set of variants it carries
    // compared highest bit first — compile.ts's sort. Each bit is a variant family's place in Tailwind's variant
    // order among the ones Velvet supports; only the relative order of the bits matters to that comparison.
    // The lowest bits are left clear here for the rule's place among its element's rules (WithRule), which a
    // variant payload's key carries; a base layer's does not. StyleLayerOrderTests pins the order.
    internal static class StyleLayerPriority
    {
        // The rule's place among its element's rules, in a layer's key (see WithRule).
        private const int RuleBits = 12;
        private const long RuleMask = (1L << RuleBits) - 1;
        // The variant bit set: bit n is at 1L << (RuleBits + n).
        private const long SetMask = ((1L << 44) - 1) << RuleBits;
        // Pseudo-class and attribute selectors the rule adds beyond the utility's own class.
        private const int ClassShift = 56;
        private const long OneClass = 1L << ClassShift;

        public const long Base = 0;

        #region One selector the utility carries alone: a media or feature query adds nothing
        // supports-[…]: is STATIC in UI Toolkit (always-applied when well-formed; see StyleSupportsVariantClass),
        // so this layer never toggles off at runtime; the priority only orders it against other layers.
        public const long Supports = 1L << 39;
        // Responsive breakpoints: a larger min-width wins while active.
        public const long ResponsiveSm = 1L << 40;
        public const long ResponsiveMd = 1L << 41;
        public const long ResponsiveLg = 1L << 42;
        public const long ResponsiveXl = 1L << 43;
        public const long Responsive2xl = 1L << 44;
        public const long Dark = 1L << 45;
        // [&>*]: — `.x > *` on the child, one class, and an arbitrary variant, which Tailwind orders after every
        // named one: it wins over the child's base utility and its md:/dark:, and loses to any pseudo-class
        // variant the child declares. Its selector sorts after [&:…]:'s, ':' before '>'.
        public const long ChildVariant = 1L << 47;
        #endregion

        #region One pseudo-class or attribute selector on top of the utility's class
        public const long GroupFocusWithin = OneClass | 1L << 12;
        public const long GroupHover = OneClass | 1L << 13;
        public const long GroupFocus = OneClass | 1L << 14;
        public const long GroupActive = OneClass | 1L << 15;
        public const long GroupDisabled = OneClass | 1L << 16;
        public const long PeerChecked = OneClass | 1L << 17;
        public const long PeerFocusWithin = OneClass | 1L << 18;
        public const long PeerHover = OneClass | 1L << 19;
        public const long PeerFocus = OneClass | 1L << 20;
        public const long PeerActive = OneClass | 1L << 21;
        public const long PeerDisabled = OneClass | 1L << 22;
        public const long First = OneClass | 1L << 23;
        public const long Last = OneClass | 1L << 24;
        public const long Only = OneClass | 1L << 25;
        public const long Odd = OneClass | 1L << 26;
        public const long Even = OneClass | 1L << 27;
        public const long Checked = OneClass | 1L << 28;
        public const long Hover = OneClass | 1L << 29;
        public const long Focus = OneClass | 1L << 30;
        public const long FocusVisible = OneClass | 1L << 31;
        public const long Active = OneClass | 1L << 32;
        public const long Disabled = OneClass | 1L << 33;
        // has-[.class]: / has-[:checked]: — `:has()` takes its argument's specificity, one class here.
        public const long Has = OneClass | 1L << 34;
        public const long Aria = OneClass | 1L << 35;
        public const long Data = OneClass | 1L << 36;
        public const long Nth = OneClass | 1L << 37;
        public const long NthLast = OneClass | 1L << 38;
        // [&:nth-child(N)]: and the other [&:…]: structural forms: an arbitrary variant.
        public const long ArbitrarySelector = OneClass | 1L << 46;
        #endregion

        public static long AttributeOf(StyleAttributeNamespace ns) => ns == StyleAttributeNamespace.Aria ? Aria : Data;

        // The variant bit set a rank carries, without its specificity or important flag.
        public static long VariantSetOf(long rank) => rank & SetMask;

        // A stacked variant (dark:hover:, hover:focus:) is one rule carrying every part: the selectors its parts
        // add sum, and its variant set is the union of theirs.
        public static long Stack(long outer, long inner)
            => ((outer >> ClassShift) + (inner >> ClassShift) << ClassShift) | ((outer | inner) & SetMask);

        // The key of a layer: the rank, then the rule's place among its element's rules (StyleRuleOrder), so two
        // rules at one rank hold separate slots and order as Tailwind orders them. A place past the field's range
        // shares its top.
        public static long WithRule(long priority, int declaration)
            => priority | System.Math.Min(System.Math.Max(declaration + 1, 0), RuleMask);

        #region Important
        // The important band (!utility / utility!) sits above every ordinary rank while important-versus-
        // important keeps the ordinary order: hover:!w-[10px] beats !w-[20px], the same way hover:w-[10px] beats
        // w-[20px].
        public const long Important = 1L << 62;

        // The layer an important payload occupies given the priority it would otherwise have had.
        public static long ImportantOf(long priority) => Important | priority;
        #endregion

        internal static long ForVariant(StyleVariantKind kind) => s_forVariant[kind];

        private static readonly VariantKindTable<long> s_forVariant = new(
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
        // origin-[33%_75%] -> transform-origin: <x> <y> <z>. One class carries every component, so unlike the
        // axis pairs above it is one property with a pair payload (Value/Unit + Value2/Unit2, z in Value3).
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

        // Flex grow / shrink factors (StyleFloat, unitless). The USS vocabulary stops at 0 and 1; grow-<N> /
        // shrink-<N> and the bracket form take the rest. Parsed by StyleTransformValueParser rather than
        // through the prefix table, which is the length grammar — see TryParseFlexFactor.
        FlexGrow,     // grow-[2]         -> flex-grow
        FlexShrink,   // shrink-[2]       -> flex-shrink

        // Transition (StyleList<TimeValue>; handled out-of-band like the filter list)
        TransitionDuration,   // duration-[400ms] -> transition-duration. Value carries SECONDS.

        #region Nine-slice (the background image's slice insets and their scale)
        // The insets are written as StyleInt, so their values are whole numbers.
        Slice,        // slice-[12_8_4_2] -> -unity-slice-top/right/bottom/left (one to four values, see Value4)
        SliceX,       // slice-x-[12]     -> left + right
        SliceY,       // slice-y-[12]     -> top + bottom
        SliceTop,     // slice-t-[12]
        SliceRight,   // slice-r-[12]
        SliceBottom,  // slice-b-[12]
        SliceLeft,    // slice-l-[12]
        SliceScale,   // slice-scale-[2]  -> -unity-slice-scale (Value = unitless factor)
        #endregion
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
        // TransformOrigin's z, a length in pixels; Slice's bottom inset; 0 for every other property.
        public float Value3 { get; }
        // Slice's left inset; 0 for every other property. Slice carries its four edges in Value (top), Value2
        // (right), Value3 and Value4, because one slice-[…] class sets all four in the border-image-slice order.
        public float Value4 { get; }
        // Color payload for color properties; default for length/angle/custom properties.
        public Color Color { get; }
        // Payload for FilterCustom (the registered name, its definition, and the resolved arguments);
        // null for every other property.
        public CustomFilterValue? Custom { get; }
        // True for the `auto` keyword of a length property; Value and Unit then carry nothing.
        public bool Auto { get; }

        // The `auto` keyword for a length property.
        public static ArbitraryStyle AutoLength(ArbitraryProperty property) => new ArbitraryStyle(property, true);

        // The inline length this result writes.
        public StyleLength ToStyleLength()
            => Auto ? new StyleLength(StyleKeyword.Auto) : new StyleLength(new Length(Value, Unit));

        private ArbitraryStyle(ArbitraryProperty property, bool auto)
        {
            Property = property;
            Auto = auto;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Value3 = 0f;
            Value4 = 0f;
            Color = default;
            Custom = null;
        }

        // Creates a length/angle result.
        public ArbitraryStyle(ArbitraryProperty property, float value, LengthUnit unit)
        {
            Property = property;
            Auto = false;
            Value = value;
            Unit = unit;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Value3 = 0f;
            Value4 = 0f;
            Color = default;
            Custom = null;
        }

        // Creates a pair-valued length result.
        public ArbitraryStyle(ArbitraryProperty property, float value, LengthUnit unit,
            float value2, LengthUnit unit2, float value3 = 0f)
        {
            Property = property;
            Auto = false;
            Value = value;
            Unit = unit;
            Value2 = value2;
            Unit2 = unit2;
            Value3 = value3;
            Value4 = 0f;
            Color = default;
            Custom = null;
        }

        // Creates a four-edge result (Slice), top, right, bottom, left.
        public ArbitraryStyle(ArbitraryProperty property, float top, float right, float bottom, float left)
        {
            Property = property;
            // MUTANT_SURVIVES(equivalent): nothing a four-edge result reaches reads Auto to a different end.
            // Slice's write does not read it, MotionPropertyClassParser declines Slice as undrivable either way,
            // and every four-edge result would carry the same flag into FiberNodePatcher.ValueKey.
            Auto = false;
            Value = top;
            Unit = LengthUnit.Pixel;
            Value2 = right;
            Unit2 = LengthUnit.Pixel;
            Value3 = bottom;
            Value4 = left;
            Color = default;
            Custom = null;
        }

        // Creates a color result.
        public ArbitraryStyle(ArbitraryProperty property, Color color)
        {
            Property = property;
            Auto = false;
            Color = color;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Value3 = 0f;
            Value4 = 0f;
            Custom = null;
        }

        // Creates a FilterCustom result.
        public ArbitraryStyle(ArbitraryProperty property, CustomFilterValue custom)
        {
            Property = property;
            Auto = false;
            Custom = custom;
            Value = 0f;
            Unit = LengthUnit.Pixel;
            Value2 = 0f;
            Unit2 = LengthUnit.Pixel;
            Value3 = 0f;
            Value4 = 0f;
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
