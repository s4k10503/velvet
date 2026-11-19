using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Velvet.StyleTable
{
    /// <summary>
    /// The UI Toolkit USS property vocabulary: every longhand a rule can set, and the shorthands that
    /// stand in for a group of them.
    /// </summary>
    /// <remarks>
    /// A longhand is a property with its own <c>ComputedStyle</c> / <c>IStyle</c> storage slot; a shorthand
    /// has none and only writes through to longhands. That distinction is what makes a single comparison
    /// vocabulary possible: two classes conflict exactly when they write the same storage slot, so
    /// <c>border-color</c> and <c>border-top-color</c> must not be compared as if they were different
    /// properties. The lists mirror <c>UnityEngine.UIElements.StyleSheets.StylePropertyId</c> and
    /// <c>ShorthandApplicator</c> for the Unity version this package targets.
    ///
    /// <c>all</c> is deliberately absent from both lists. It is a <c>StylePropertyId</c> member, but only as
    /// the <c>transition-property: all</c> sentinel — it has no storage and no declaration form — so a rule
    /// that declares it is a mistake worth reporting rather than expanding.
    /// </remarks>
    internal static class UssPropertyVocabulary
    {
        /// <summary>
        /// Longhand USS names paired with the identifier used for them in generated code. The identifier is
        /// the USS name in Pascal case, with the vendor prefix folded into the leading word.
        /// </summary>
        public static readonly ImmutableArray<UssLonghand> Longhands = ImmutableArray.Create(
            new UssLonghand("align-content", "AlignContent"),
            new UssLonghand("align-items", "AlignItems"),
            new UssLonghand("align-self", "AlignSelf"),
            new UssLonghand("aspect-ratio", "AspectRatio"),
            new UssLonghand("background-color", "BackgroundColor"),
            new UssLonghand("background-image", "BackgroundImage"),
            new UssLonghand("background-position-x", "BackgroundPositionX"),
            new UssLonghand("background-position-y", "BackgroundPositionY"),
            new UssLonghand("background-repeat", "BackgroundRepeat"),
            new UssLonghand("background-size", "BackgroundSize"),
            new UssLonghand("border-bottom-color", "BorderBottomColor"),
            new UssLonghand("border-bottom-left-radius", "BorderBottomLeftRadius"),
            new UssLonghand("border-bottom-right-radius", "BorderBottomRightRadius"),
            new UssLonghand("border-bottom-width", "BorderBottomWidth"),
            new UssLonghand("border-left-color", "BorderLeftColor"),
            new UssLonghand("border-left-width", "BorderLeftWidth"),
            new UssLonghand("border-right-color", "BorderRightColor"),
            new UssLonghand("border-right-width", "BorderRightWidth"),
            new UssLonghand("border-top-color", "BorderTopColor"),
            new UssLonghand("border-top-left-radius", "BorderTopLeftRadius"),
            new UssLonghand("border-top-right-radius", "BorderTopRightRadius"),
            new UssLonghand("border-top-width", "BorderTopWidth"),
            new UssLonghand("bottom", "Bottom"),
            new UssLonghand("color", "Color"),
            new UssLonghand("cursor", "Cursor"),
            new UssLonghand("display", "Display"),
            new UssLonghand("filter", "Filter"),
            new UssLonghand("flex-basis", "FlexBasis"),
            new UssLonghand("flex-direction", "FlexDirection"),
            new UssLonghand("flex-grow", "FlexGrow"),
            new UssLonghand("flex-shrink", "FlexShrink"),
            new UssLonghand("flex-wrap", "FlexWrap"),
            new UssLonghand("font-size", "FontSize"),
            new UssLonghand("height", "Height"),
            new UssLonghand("justify-content", "JustifyContent"),
            new UssLonghand("left", "Left"),
            new UssLonghand("letter-spacing", "LetterSpacing"),
            new UssLonghand("margin-bottom", "MarginBottom"),
            new UssLonghand("margin-left", "MarginLeft"),
            new UssLonghand("margin-right", "MarginRight"),
            new UssLonghand("margin-top", "MarginTop"),
            new UssLonghand("max-height", "MaxHeight"),
            new UssLonghand("max-width", "MaxWidth"),
            new UssLonghand("min-height", "MinHeight"),
            new UssLonghand("min-width", "MinWidth"),
            new UssLonghand("opacity", "Opacity"),
            new UssLonghand("overflow", "Overflow"),
            new UssLonghand("padding-bottom", "PaddingBottom"),
            new UssLonghand("padding-left", "PaddingLeft"),
            new UssLonghand("padding-right", "PaddingRight"),
            new UssLonghand("padding-top", "PaddingTop"),
            new UssLonghand("position", "Position"),
            new UssLonghand("right", "Right"),
            new UssLonghand("rotate", "Rotate"),
            new UssLonghand("scale", "Scale"),
            new UssLonghand("text-overflow", "TextOverflow"),
            new UssLonghand("text-shadow", "TextShadow"),
            new UssLonghand("top", "Top"),
            new UssLonghand("transform-origin", "TransformOrigin"),
            new UssLonghand("transition-delay", "TransitionDelay"),
            new UssLonghand("transition-duration", "TransitionDuration"),
            new UssLonghand("transition-property", "TransitionProperty"),
            new UssLonghand("transition-timing-function", "TransitionTimingFunction"),
            new UssLonghand("translate", "Translate"),
            new UssLonghand("visibility", "Visibility"),
            new UssLonghand("white-space", "WhiteSpace"),
            new UssLonghand("width", "Width"),
            new UssLonghand("word-spacing", "WordSpacing"),
            new UssLonghand("-unity-background-image-tint-color", "UnityBackgroundImageTintColor"),
            new UssLonghand("-unity-editor-text-rendering-mode", "UnityEditorTextRenderingMode"),
            new UssLonghand("-unity-font", "UnityFont"),
            new UssLonghand("-unity-font-definition", "UnityFontDefinition"),
            new UssLonghand("-unity-font-style", "UnityFontStyle"),
            new UssLonghand("-unity-material", "UnityMaterial"),
            new UssLonghand("-unity-overflow-clip-box", "UnityOverflowClipBox"),
            new UssLonghand("-unity-paragraph-spacing", "UnityParagraphSpacing"),
            new UssLonghand("-unity-slice-bottom", "UnitySliceBottom"),
            new UssLonghand("-unity-slice-left", "UnitySliceLeft"),
            new UssLonghand("-unity-slice-right", "UnitySliceRight"),
            new UssLonghand("-unity-slice-scale", "UnitySliceScale"),
            new UssLonghand("-unity-slice-top", "UnitySliceTop"),
            new UssLonghand("-unity-slice-type", "UnitySliceType"),
            new UssLonghand("-unity-text-align", "UnityTextAlign"),
            new UssLonghand("-unity-text-auto-size", "UnityTextAutoSize"),
            new UssLonghand("-unity-text-generator", "UnityTextGenerator"),
            new UssLonghand("-unity-text-outline-color", "UnityTextOutlineColor"),
            new UssLonghand("-unity-text-outline-width", "UnityTextOutlineWidth"),
            new UssLonghand("-unity-text-overflow-position", "UnityTextOverflowPosition"));

        private static readonly Dictionary<string, ImmutableArray<string>> Shorthands =
            new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal)
            {
                ["background-position"] = ImmutableArray.Create(
                    "background-position-x", "background-position-y"),
                ["border-color"] = ImmutableArray.Create(
                    "border-top-color", "border-right-color", "border-bottom-color", "border-left-color"),
                ["border-radius"] = ImmutableArray.Create(
                    "border-top-left-radius", "border-top-right-radius",
                    "border-bottom-right-radius", "border-bottom-left-radius"),
                ["border-width"] = ImmutableArray.Create(
                    "border-top-width", "border-right-width", "border-bottom-width", "border-left-width"),
                ["flex"] = ImmutableArray.Create("flex-grow", "flex-shrink", "flex-basis"),
                ["margin"] = ImmutableArray.Create(
                    "margin-top", "margin-right", "margin-bottom", "margin-left"),
                ["padding"] = ImmutableArray.Create(
                    "padding-top", "padding-right", "padding-bottom", "padding-left"),
                ["transition"] = ImmutableArray.Create(
                    "transition-delay", "transition-duration",
                    "transition-property", "transition-timing-function"),
                ["-unity-background-scale-mode"] = ImmutableArray.Create(
                    "background-position-x", "background-position-y",
                    "background-repeat", "background-size"),
                ["-unity-text-outline"] = ImmutableArray.Create(
                    "-unity-text-outline-color", "-unity-text-outline-width"),
            };

        private static readonly HashSet<string> LonghandNames = BuildLonghandNames();

        /// <summary>Bit index assigned to each longhand, ordered so the emitted table is stable.</summary>
        public static ImmutableArray<UssLonghand> OrderedLonghands { get; } = OrderLonghands();

        public static bool IsLonghand(string ussName) => LonghandNames.Contains(ussName);

        public static bool TryExpandShorthand(string ussName, out ImmutableArray<string> longhands) =>
            Shorthands.TryGetValue(ussName, out longhands);

        /// <summary>
        /// The longhands a declaration of <paramref name="ussName"/> writes, or an empty result when the name
        /// is neither a longhand nor a shorthand — a caller must report that rather than treat it as setting
        /// nothing.
        /// </summary>
        public static bool TryResolve(string ussName, out ImmutableArray<string> longhands)
        {
            if (IsLonghand(ussName))
            {
                longhands = ImmutableArray.Create(ussName);
                return true;
            }
            return TryExpandShorthand(ussName, out longhands);
        }

        private static HashSet<string> BuildLonghandNames()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var longhand in Longhands)
            {
                names.Add(longhand.UssName);
            }
            return names;
        }

        private static ImmutableArray<UssLonghand> OrderLonghands()
        {
            var ordered = new List<UssLonghand>(Longhands);
            ordered.Sort(static (a, b) => string.CompareOrdinal(a.UssName, b.UssName));
            return ordered.ToImmutableArray();
        }
    }

    /// <summary>A USS longhand property and the identifier generated code refers to it by.</summary>
    internal readonly struct UssLonghand
    {
        public UssLonghand(string ussName, string identifier)
        {
            UssName = ussName;
            Identifier = identifier;
        }

        public string UssName { get; }

        public string Identifier { get; }
    }
}
