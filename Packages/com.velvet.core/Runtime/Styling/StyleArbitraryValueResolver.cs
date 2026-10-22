using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{

    // Parses utility-class arbitrary-value syntax (e.g. h-[15%], min-w-[60px], -mt-[20px],
    // text-[#fff], bg-[#1e1e1e]) and applies the result as an inline style. Resolved to inline (not USS
    // classes) because an arbitrary bracket value is unbounded — there is no USS selector that could be
    // pre-declared for it. text-[...] is overloaded: a color value sets the text color, otherwise the
    // value is a font size. bg-[#color] sets the background color (the bg-[addr:...] image
    // form is handled by StyleBackgroundImageResolver).
    internal static class StyleArbitraryValueResolver
    {
        // Strips the important modifier and reports whether it was present: a leading '!' (!bg-red-500)
        // or a trailing '!' (bg-red-500!). The bare core is returned so the caller routes
        // it normally; when important, the caller elevates the (inline-resolvable) utility to the Important
        // layer so it wins conflicts. A class-only utility (no inline form) cannot be elevated in UI Toolkit,
        // so its '!' is accepted but inert. Returns the input unchanged when no modifier is present.
        //
        // Scope: this is wired into the per-class dispatch (USS-class + inline-layer utilities). Of the
        // array-scanned subsystem utilities, the font, text-effect, z-*, gap-* and divide-* families strip
        // the bang themselves and let an important token win over the element's plain ones
        // (StyleFontClass.TryExtract, StyleTextEffectClass.Parse, StyleZIndexClass.TryExtract,
        // StyleGridClass.ExtractGaps, StyleDivideClass.TryExtract); shadow-* and clip-path-* do not
        // recognize the bang at all.
        public static string StripImportant(string className, out bool important)
        {
            important = false;
            if (string.IsNullOrEmpty(className))
            {
                return className;
            }

            if (className[0] == '!')
            {
                important = true;
                return className.Substring(1);
            }

            if (className[className.Length - 1] == '!')
            {
                important = true;
                return className.Substring(0, className.Length - 1);
            }

            return className;
        }

        // True when a class token encodes an inline value (resolved to inline style) rather than a plain
        // USS class: a bracketed arbitrary value (w-[120px]), the color-opacity modifier (bg-black/50),
        // or a static-scale name a USS selector cannot spell (-mt-2, translate-x-1/2). Plain classes — the
        // overwhelming majority — are added to the USS class list verbatim and skip the resolvers. The
        // caller strips the important bang first; an empty token resolves to false.
        public static bool IsInlineResolved(string core)
            => core.IndexOf('[') >= 0 || StyleColorValueParser.HasColorOpacityModifier(core) || MayBeStaticScale(core);

        // TryParse for a token that becomes a layer: a logical radius takes the property of its own that Tailwind's
        // order ranks it by (StyleLogicalUtilities.AsLayer), where TryParse reads it as the physical property it
        // resolves to. Every other token is as TryParse leaves it.
        internal static bool TryParseLayer(string className, out ArbitraryStyle result)
        {
            if (!TryParse(className, out result))
            {
                return false;
            }
            result = StyleLogicalUtilities.AsLayer(className, in result);
            return true;
        }

        // TryParseLayer for a reader that orders tokens without applying them (StyleInlineSheetOrder, StyleFamilyOrder):
        // a filter-[name] naming nothing registered stays unwarned, for the pass that applies the token to report.
        internal static bool TryParseLayerQuietly(string className, out ArbitraryStyle result)
        {
            var was = StyleFilterValueParser.QuietUnregistered;
            StyleFilterValueParser.QuietUnregistered = true;
            try
            {
                return TryParseLayer(className, out result);
            }
            finally
            {
                StyleFilterValueParser.QuietUnregistered = was;
            }
        }

        // Parses using IndexOf + string operations rather than regex — a per-class hot path.
        public static bool TryParse(string className, out ArbitraryStyle result)
        {
            result = default;
            if (className == null)
            {
                return false;
            }

            if (StyleBorderSideColor.TryParse(className, out result)
                || StyleLogicalUtilities.TryParse(className, out result))
            {
                return true;
            }

            // Color opacity modifier: {bg|text|border}-<color>/<N> applies alpha N% to the resolved base
            // color (bg-red-500/50, text-black/75, border-white/10, bg-[#fff]/50). Detected before the
            // bracket parsing below because the palette form carries no '[' at all. A leading '-' never
            // applies to a color, so the negated form is skipped.
            if (className.Length > 0 && className[0] != '-'
                && StyleColorValueParser.TryParseColorOpacityModifier(className, out result))
            {
                return true;
            }

            // text-<size>/<leading> and text-[<length>]/<leading> set the font-size here because no USS rule can spell the suffix;
            // StyleTextEffectClass reads the same token for its line height.
            if (StyleNamedTextSize.TryParseSlashFontSize(className, out result))
            {
                return true;
            }

            // The drop-shadow family takes a modifier after its bracket, which the bracket path below rejects.
            if (StyleFilterValueParser.TryParseDropShadow(className, out result))
            {
                return true;
            }

            var negate = className.Length > 0 && className[0] == '-';
            var offset = negate ? 1 : 0;

            // Non-bracket static-scale names that have no USS class (-mt-2, -rotate-6,
            // translate-x-1/2, -translate-x-6) resolve to the same property machinery as the bracket forms.
            // Named filter presets (blur-sm, contrast-125, hue-rotate-90, ...) are non-bracket too, parsed first.
            if (className.IndexOf('[') < 0)
            {
                if (StyleFilterValueParser.TryParseFilterPreset(className, out result))
                {
                    return true;
                }
                return TryParseStaticScale(className, out result);
            }

            var bracketStart = className.IndexOf('[', offset);
            if (bracketStart < offset + 2)
            {
                return false; // prefix is at least 2 chars (e.g. "h-").
            }

            if (className.Length < bracketStart + 2 || className[className.Length - 1] != ']')
            {
                return false;
            }

            var prefix = className.Substring(offset, bracketStart - offset);

            // Zero-alloc via Span.
            var valueSpan = className.AsSpan(bracketStart + 1, className.Length - bracketStart - 2);
            if (valueSpan.Length == 0)
            {
                return false;
            }

            // filter-[name:args] and bg-[addr:key] are Velvet's own colon forms, not type hints.
            string? hint = null;
            var ownColon = prefix == "filter-" || (prefix == "bg-" && valueSpan.StartsWith("addr:".AsSpan(), StringComparison.Ordinal));
            if (!ownColon && !TrySplitTypeHint(valueSpan, out hint, out valueSpan))
            {
                return false;
            }

            return TryParseBracketValue(prefix, valueSpan, negate, hint, out result);
        }

        // Tailwind's type hint: a run of a-z and '-' closed by ':' at the start of a bracket (`[length:2PX]`). It
        // replaces the type tests of a utility that has them and is dropped by every other, which writes the value
        // after it. An empty run is no hint and the value after the colon is kept (`[:10px]`). False when no value
        // is left, which Tailwind reads as no utility.
        internal static bool TrySplitTypeHint(ReadOnlySpan<char> inner, out string? hint, out ReadOnlySpan<char> value)
        {
            hint = null;
            value = inner;
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (c == ':')
                {
                    hint = i > 0 ? inner.Slice(0, i).ToString() : null;
                    value = inner.Slice(i + 1);
                    return !value.Trim("_ ".AsSpan()).IsEmpty;
                }
                if (c != '-' && (c < 'a' || c > 'z'))
                {
                    break;
                }
            }
            return true;
        }

        // The type hint in a class's bracket, or null when it has none.
        internal static string? TypeHintOf(string cls)
        {
            var open = cls.IndexOf('[');
            var close = cls.LastIndexOf(']');
            return open >= 0 && close > open && TrySplitTypeHint(cls.AsSpan(open + 1, close - open - 1), out var hint, out _)
                ? hint
                : null;
        }

        // WithoutTypeHint as a Try: false where the hint is malformed.
        internal static bool TryDropTypeHint(string cls, out string plain)
        {
            var dropped = WithoutTypeHint(cls);
            plain = dropped ?? cls;
            return dropped != null;
        }

        // A class whose bracket is written through by Tailwind, with its type hint dropped: `gap-[length:8px]` is
        // `gap-[8px]`. Null when the hint is malformed, which Tailwind reads as no utility.
        internal static string? WithoutTypeHint(string cls)
        {
            var open = cls.IndexOf('[');
            var close = cls.LastIndexOf(']');
            if (open < 0 || close <= open)
            {
                return cls;
            }
            if (!TrySplitTypeHint(cls.AsSpan(open + 1, close - open - 1), out var hint, out var value))
            {
                return null;
            }
            return value.Length == close - open - 1 ? cls : string.Concat(cls.Substring(0, open + 1), value.ToString(), cls.Substring(close));
        }

        private static bool TryParseBracketValue(
            string prefix, ReadOnlySpan<char> valueSpan, bool negate, string? hint, out ArbitraryStyle result)
        {
            result = default;

            // duration-[<time>] (transition-duration) carries a TIME value, not a length, so it is parsed here
            // before the generic length path and applied out-of-band (a StyleList<TimeValue>, like the filter list).
            if (!negate && prefix == "duration-")
            {
                if (TryParseDurationSeconds(valueSpan, out var seconds))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.TransitionDuration, seconds, LengthUnit.Pixel);
                    return true;
                }
                return false;
            }

            // Color-capable prefixes (text-/bg-/border-): a non-null result is returned as-is; null means
            // not claimed as a color → fall through to the length-based path (text-/border- with a non-color
            // value). bg- is color-only, so a non-color bg- value rejects (false) and the call site falls
            // through to StyleBackgroundImageResolver.
            {
                var color = StyleColorValueParser.TryParseColorPrefix(prefix, valueSpan, negate, hint, out result);
                if (color.HasValue) return color.Value;
            }

            // StyleTransformValueParser owns which prefixes it claims and why; they resolve before the
            // length path so it can answer for them.
            {
                var transform = StyleTransformValueParser.TryParseTransformValue(prefix, valueSpan, negate, out result);
                if (transform.HasValue) return transform.Value;
            }

            // aspect-[w/h] (or a bare decimal) — a ratio, not a length. A negative ratio is meaningless and
            // a zero denominator is rejected so the class falls through as unrecognized.
            if (!negate && prefix == "aspect-")
            {
                if (!TryParseRatio(valueSpan, out var ratio))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.AspectRatio, ratio, LengthUnit.Pixel);
                return true;
            }

            // Filter functions (blur / grayscale / invert / sepia / contrast / hue-rotate / brightness /
            // saturate), routed here (not through TryGetProperty) so all filter-* utilities share the one
            // compose-and-apply path (ApplyCombinedFilter writes a single list).
            {
                var filter = StyleFilterValueParser.TryParseFilterValue(prefix, valueSpan, negate, out result);
                if (filter.HasValue) return filter.Value;
            }

            // filter-[name:args] resolves a VelvetFilters-registered custom filter; it shares the same
            // ApplyCombinedFilter compose-and-apply path as the built-ins above, appended after them.
            {
                var custom = StyleFilterValueParser.TryParseCustomFilter(prefix, valueSpan, negate, out result);
                if (custom.HasValue) return custom.Value;
            }

            {
                var slice = TryParseSliceValue(prefix, valueSpan, negate, out result);
                if (slice.HasValue) return slice.Value;
            }

            return TryParseLengthBracket(prefix, valueSpan, negate, hint, out result);
        }

        private static bool TryParseLengthBracket(
            string prefix, ReadOnlySpan<char> valueSpan, bool negate, string? hint, out ArbitraryStyle result)
        {
            result = default;
            if (!TryGetProperty(prefix, out var property)
                || !TailwindReadsAsLength(property, valueSpan, hint))
            {
                return false;
            }

            if (TryParseKeywordLength(property, valueSpan, negate, out result))
            {
                return true;
            }

            if (!negate && valueSpan.SequenceEqual("auto".AsSpan()) && TakesAuto(property))
            {
                result = ArbitraryStyle.AutoLength(property);
                return true;
            }

            if (!TryParseValue(valueSpan, out var value, out var unit))
            {
                return TryParseRelativeLength(property, valueSpan, negate, out result);
            }

            // FloatSetters write a StyleFloat, which carries no unit, and none of their longhands takes a
            // percentage in CSS.
            if (unit == LengthUnit.Percent && FloatSetters.ContainsKey(property))
            {
                return false;
            }

            if (negate)
            {
                value = -value;
            }

            result = new ArbitraryStyle(property, value, unit);
            return true;
        }

        // Where Tailwind reads a bracket as either a length or a colour, whether its type tests take this one for
        // the length; a value they do not is written as a colour, which a browser discards. Every other property
        // here takes the bracket as written.
        // A type hint replaces the tests: StyleColorValueParser.HintSelectsNonColour names the hints that pick
        // the length.
        private static bool TailwindReadsAsLength(ArbitraryProperty property, ReadOnlySpan<char> value, string? hint)
        {
            if (IsBorderWidth(property))
            {
                return hint != null
                    ? StyleColorValueParser.HintSelectsNonColour("border", hint)
                    : TailwindDataTypes.IsLength(value) || TailwindDataTypes.IsNumber(value)
                        || TailwindDataTypes.IsLineWidthKeyword(value);
            }
            if (property == ArbitraryProperty.FontSize)
            {
                return hint != null
                    ? StyleColorValueParser.HintSelectsNonColour("text", hint)
                    : TailwindDataTypes.IsLength(value) || TailwindDataTypes.IsPercentage(value)
                        || TailwindDataTypes.IsFontSizeKeyword(value);
            }
            return true;
        }

        private static bool IsBorderWidth(ArbitraryProperty property)
            => property is ArbitraryProperty.BorderWidth or ArbitraryProperty.BorderTopWidth
                or ArbitraryProperty.BorderRightWidth or ArbitraryProperty.BorderBottomWidth
                or ArbitraryProperty.BorderLeftWidth;

        // A <line-width> keyword (thin, medium, thick) or a font-size keyword, at the sizes Chromium computes,
        // where Tailwind's type tests hand one to border-width or font-size.
        private static bool TryParseKeywordLength(
            ArbitraryProperty property, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return false;
            }
            var keyword = valueSpan.ToString();
            if (IsBorderWidth(property) && s_lineWidthKeywords.TryGetValue(keyword, out var width))
            {
                result = new ArbitraryStyle(property, width, LengthUnit.Pixel);
                return true;
            }
            if (property != ArbitraryProperty.FontSize)
            {
                return false;
            }
            if (s_absoluteFontSizes.TryGetValue(keyword, out var size))
            {
                result = new ArbitraryStyle(property, size, LengthUnit.Pixel);
                return true;
            }
            return keyword switch
            {
                "larger" => TryParseRelativeLength(property, "1.2em".AsSpan(), false, out result),
                "smaller" => TryParseRelativeLength(property, "calc(1em/1.2)".AsSpan(), false, out result),
                _ => false,
            };
        }

        private static readonly Dictionary<string, float> s_lineWidthKeywords = new()
        {
            ["thin"] = 1f, ["medium"] = 3f, ["thick"] = 5f,
        };

        private static readonly Dictionary<string, float> s_absoluteFontSizes = new()
        {
            ["xx-small"] = 9f, ["x-small"] = 10f, ["small"] = 13f, ["medium"] = 16f, ["large"] = 18f,
            ["x-large"] = 24f, ["xx-large"] = 32f, ["xxx-large"] = 48f,
        };

        // A length only an element can measure — em, a viewport unit, or a math function mixing a percentage
        // with a pixel length — is carried unresolved to ApplyInline, and only for the properties PropertySetters
        // writes, whose setters name the axis their percentage is taken of.
        private static bool TryParseRelativeLength(
            ArbitraryProperty property, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (!PropertySetters.ContainsKey(property) || !StyleLengthExpression.TryParse(valueSpan, out var expression)
                || !expression!.IsFinite())
            {
                return false;
            }
            result = new ArbitraryStyle(property, negate ? expression!.Negated() : expression!);
            return true;
        }

        #region Static-scale utility names (no bracket)

        // The spacing steps the bundled stylesheets declare, mirroring --space-* in _tokens.uss (1 unit = 4px; the
        // suffix uses '-' where CSS writes '.', e.g. mt-2-5 -> 10px). Any other multiple of 0.25 is
        // StyleSpacingMultiplier's, through TryGetSpacingPx. Negative margins (-mt-2) and negative
        // px translates (-translate-x-6) route here because a USS selector cannot start with '-'.
        private static readonly Dictionary<string, float> s_spacingScale = new()
        {
            ["0"] = 0f, ["px"] = 1f, ["0-5"] = 2f, ["1"] = 4f, ["1-5"] = 6f, ["2"] = 8f,
            ["2-5"] = 10f, ["3"] = 12f, ["3-5"] = 14f, ["4"] = 16f, ["5"] = 20f, ["6"] = 24f,
            ["7"] = 28f, ["8"] = 32f, ["9"] = 36f, ["10"] = 40f, ["11"] = 44f, ["12"] = 48f, ["14"] = 56f,
            ["16"] = 64f, ["20"] = 80f, ["24"] = 96f, ["28"] = 112f, ["32"] = 128f, ["36"] = 144f, ["40"] = 160f,
            ["44"] = 176f, ["48"] = 192f, ["52"] = 208f, ["56"] = 224f, ["60"] = 240f, ["64"] = 256f,
            ["72"] = 288f, ["80"] = 320f, ["96"] = 384f,
        };

        // Single source for the spacing scale: the --space-* steps above, then any other canonical multiple of
        // 0.25 (StyleSpacingMultiplier). Shared so gap-* / space-* parsing (StyleGapClass) resolves the same
        // values as mt-* / p-* here, instead of holding a second copy that could drift.
        internal static bool TryGetSpacingPx(string suffix, out float px)
            => s_spacingScale.TryGetValue(suffix, out px) || StyleSpacingMultiplier.TryParse(suffix.AsSpan(), out px);

        // TryGetSpacingPx over a slice, for the per-class gates that must not allocate.
        private static bool TryGetSpacingPx(ReadOnlySpan<char> suffix, out float px)
        {
            if (StyleSpacingMultiplier.TryParse(suffix, out px))
            {
                return true;
            }
            foreach (var step in s_spacingScale)
            {
                if (suffix.SequenceEqual(step.Key.AsSpan()))
                {
                    px = step.Value;
                    return true;
                }
            }
            px = 0f;
            return false;
        }

        // The utilities that take a spacing step. A step with a USS rule (mt-2, w-4, mt-1-5) stays a class, so
        // only the others resolve inline here: mt-13, p-4.5, and every negated one, which no selector can spell.
        // Of these families Tailwind negates the margin and position ones. A prefix that could be taken for a
        // longer one (inset- for inset-x-) is not told apart by order: TryParseSpacingStep skips a row whose
        // suffix is not a step.
        private static readonly (string Prefix, ArbitraryProperty Property, bool Negatable)[] s_spacingFamilies =
        {
            ("p-", ArbitraryProperty.Padding, false),
            ("px-", ArbitraryProperty.PaddingX, false),
            ("py-", ArbitraryProperty.PaddingY, false),
            ("pt-", ArbitraryProperty.PaddingTop, false),
            ("pr-", ArbitraryProperty.PaddingRight, false),
            ("pb-", ArbitraryProperty.PaddingBottom, false),
            ("pl-", ArbitraryProperty.PaddingLeft, false),
            ("m-", ArbitraryProperty.Margin, true),
            ("mx-", ArbitraryProperty.MarginX, true),
            ("my-", ArbitraryProperty.MarginY, true),
            ("mt-", ArbitraryProperty.MarginTop, true),
            ("mr-", ArbitraryProperty.MarginRight, true),
            ("mb-", ArbitraryProperty.MarginBottom, true),
            ("ml-", ArbitraryProperty.MarginLeft, true),
            ("w-", ArbitraryProperty.Width, false),
            ("h-", ArbitraryProperty.Height, false),
            ("size-", ArbitraryProperty.Size, false),
            ("min-w-", ArbitraryProperty.MinWidth, false),
            ("min-h-", ArbitraryProperty.MinHeight, false),
            ("max-w-", ArbitraryProperty.MaxWidth, false),
            ("max-h-", ArbitraryProperty.MaxHeight, false),
            ("basis-", ArbitraryProperty.FlexBasis, false),
            ("top-", ArbitraryProperty.Top, true),
            ("right-", ArbitraryProperty.Right, true),
            ("bottom-", ArbitraryProperty.Bottom, true),
            ("left-", ArbitraryProperty.Left, true),
            ("inset-x-", ArbitraryProperty.InsetX, true),
            ("inset-y-", ArbitraryProperty.InsetY, true),
            ("inset-", ArbitraryProperty.Inset, true),
        };

        // The families' properties, to tell a spacing utility from another inline one.
        private static readonly HashSet<ArbitraryProperty> s_spacingProperties = BuildSpacingProperties();

        private static HashSet<ArbitraryProperty> BuildSpacingProperties()
        {
            var properties = new HashSet<ArbitraryProperty>();
            foreach (var (_, property, _) in s_spacingFamilies)
            {
                properties.Add(property);
            }
            return properties;
        }

        internal static bool IsSpacingProperty(ArbitraryProperty property) => s_spacingProperties.Contains(property);

        // The families whose longhand takes `auto`: CSS gives padding none, and a maximum's unbounded value is none,
        // which the setters write as StyleKeyword.None.
        private static bool TakesAuto(ArbitraryProperty property)
            => IsSpacingProperty(property) && !IsPaddingProperty(property)
                && property is not (ArbitraryProperty.MaxWidth or ArbitraryProperty.MaxHeight);

        private static bool IsPaddingProperty(ArbitraryProperty property)
            => property is ArbitraryProperty.Padding or ArbitraryProperty.PaddingX or ArbitraryProperty.PaddingY
                or ArbitraryProperty.PaddingTop or ArbitraryProperty.PaddingRight
                or ArbitraryProperty.PaddingBottom or ArbitraryProperty.PaddingLeft;

        // True when text begins with the prefix of a spacing family, ignoring a leading minus: the cheap test that
        // a token can be a spacing utility, which allocates nothing.
        internal static bool StartsWithSpacingPrefix(string text)
        {
            var body = text.AsSpan(text.Length > 0 && text[0] == '-' ? 1 : 0);
            foreach (var (prefix, _, _) in s_spacingFamilies)
            {
                if (body.StartsWith(prefix.AsSpan()))
                {
                    return true;
                }
            }
            return false;
        }

        // True when text begins with `rounded`, the start of every radius utility: the cheap test that a token can
        // be one, which allocates nothing.
        internal static bool StartsWithRadiusPrefix(string text)
            => text.StartsWith(RadiusUtilityName, StringComparison.Ordinal);

        // The families StyleSpacingPrecedence orders: spacing, size, inset, radius, and the flex shorthand with the
        // two factors it contains besides the basis.
        internal static bool IsSpacingRadiusOrFlexProperty(ArbitraryProperty property)
            => IsSpacingProperty(property) || RadiusCornersOf.ContainsKey(property)
                || property is ArbitraryProperty.Flex or ArbitraryProperty.FlexGrow or ArbitraryProperty.FlexShrink;

        // True when text begins with the prefix of a flex shorthand or factor utility: the cheap test that a token
        // can be one, which allocates nothing.
        internal static bool StartsWithFlexPrefix(string text)
            => text.StartsWith("flex-", StringComparison.Ordinal) || text.StartsWith("grow", StringComparison.Ordinal)
                || text.StartsWith("shrink", StringComparison.Ordinal);

        // The flex factor classes and the bracket token each equals. FlexFactorClassMirrorTests holds the values
        // against _layout.uss.
        private static readonly (string Class, ArbitraryProperty Property, string InlineForm)[] s_flexFactorClasses =
        {
            ("grow", ArbitraryProperty.FlexGrow, "grow-[1]"),
            ("grow-0", ArbitraryProperty.FlexGrow, "grow-[0]"),
            ("shrink", ArbitraryProperty.FlexShrink, "shrink-[1]"),
            ("shrink-0", ArbitraryProperty.FlexShrink, "shrink-[0]"),
        };

        internal static bool IsRadiusProperty(ArbitraryProperty property) => RadiusCornersOf.ContainsKey(property);

        private const string RadiusUtilityName = "rounded";

        // A stylesheet class of a spacing or radius family and the inline token that resolves to the same thing: a
        // step becomes its pixel bracket, `auto` the bracket keyword and `full` a hundred percent. Whether the
        // sheets declare a spacing class is read from StyleUtilityProperties, which is generated from them, so only
        // a name with a rule counts: the sheets declare `-auto` for margins, size, width, height and basis and
        // `-full` for sizes and basis, and nothing else in these families. A radius class is read off the radius
        // scale instead (TryReadRadiusClass); RadiusPrecedenceTests checks that the sheets declare a rule for every
        // step of every family, and the magnitudes are pinned by StyleUtilitiesUssTests and CornerRadiusFitPanelTests.
        // A flex factor class is read off s_flexFactorClasses. The flex shorthand's own classes (flex-1, flex-none)
        // are not read: each writes all three longhands, and Tailwind emits the shorthand ahead of the longhands, so
        // StyleSpacingPrecedence never finds one the narrower of a pair.
        internal static bool TryReadStylesheetClass(string cls, out ArbitraryProperty property, out string inlineForm)
        {
            property = default;
            inlineForm = string.Empty;
            if (TryReadSpacingStep(cls, out var step, out var backed) && backed)
            {
                property = step.Property;
                inlineForm = SpacingPrefixOf(property) + "[" + step.Value.ToString("R", CultureInfo.InvariantCulture) + "px]";
                return true;
            }
            if (TryReadRadiusClass(cls, out property, out inlineForm))
            {
                return true;
            }
            foreach (var (name, factorProperty, form) in s_flexFactorClasses)
            {
                if (cls == name)
                {
                    property = factorProperty;
                    inlineForm = form;
                    return true;
                }
            }
            // MUTANT_SURVIVES(equivalent, boundary): the shortest name read here is a family prefix of two characters and `auto` or `full`, six in all, so a bound of 4 turns away only names none of the loop's rows match.
            if (cls.Length < 4 || !StyleUtilityProperties.TryGet(cls, out _))
            {
                return false;
            }
            foreach (var (prefix, familyProperty, _) in s_spacingFamilies)
            {
                if (!cls.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var name = cls.AsSpan(prefix.Length);
                // MUTANT_SURVIVES(unreachable, clause removed): the guard above admits only a class the sheets declare, and they declare no padding or maximum `-auto` class, which SpacingPrecedenceTests pins.
                if (name.SequenceEqual("auto".AsSpan()) && TakesAuto(familyProperty))
                {
                    property = familyProperty;
                    inlineForm = prefix + "[auto]";
                    return true;
                }
                if (name.SequenceEqual("full".AsSpan()))
                {
                    property = familyProperty;
                    inlineForm = prefix + "[100%]";
                    return true;
                }
            }
            return false;
        }

        // A rounded-* class on the scale StyleShadowClass mirrors, read as the radius property it writes and the
        // bracket token that equals it. The prefix table is walked rather than a family list kept beside it, and a
        // class is told from a longer family's by its suffix, which only a scale step or full passes.
        private static bool TryReadRadiusClass(string cls, out ArbitraryProperty property, out string inlineForm)
        {
            property = default;
            inlineForm = string.Empty;
            if (!StartsWithRadiusPrefix(cls))
            {
                return false;
            }
            foreach (var entry in s_prefixProperties)
            {
                // The bare utility (rounded-t) is the prefix without its trailing '-'.
                var bare = cls.Length == entry.Key.Length - 1 && entry.Key.StartsWith(cls, StringComparison.Ordinal);
                if (!bare && !cls.StartsWith(entry.Key, StringComparison.Ordinal))
                {
                    continue;
                }
                var suffix = bare ? string.Empty : cls.Substring(entry.Key.Length);
                if (suffix.Length == 0 && !bare)
                {
                    continue;
                }
                if (StyleLogicalUtilities.TryGetRadiusPx(suffix, out var px))
                {
                    property = entry.Value;
                    inlineForm = entry.Key + "[" + px.ToString("R", CultureInfo.InvariantCulture) + "px]";
                    return true;
                }
            }
            return false;
        }

        // The prefix (with its trailing '-') whose utility writes property.
        private static string SpacingPrefixOf(ArbitraryProperty property)
        {
            foreach (var (familyPrefix, familyProperty, _) in s_spacingFamilies)
            {
                if (familyProperty == property)
                {
                    return familyPrefix;
                }
            }
            return string.Empty;
        }

        // A step the bundled stylesheets declare no rule for, which resolves inline.
        private static bool TryParseSpacingStep(string cls, out ArbitraryStyle result)
            => TryReadSpacingStep(cls, out result, out var stylesheetBacked) && !stylesheetBacked;

        // Any step of a spacing family, and whether the stylesheets declare a class for it: only a positive
        // step on the --space-* table is one.
        internal static bool TryReadSpacingStep(string cls, out ArbitraryStyle result, out bool stylesheetBacked)
        {
            result = default;
            // MUTANT_SURVIVES(equivalent, literal): both callers read stylesheetBacked only when this returns true, and every return of true assigns it first.
            stylesheetBacked = false;
            if (cls.Length < 3)
            {
                return false;
            }
            // A step ends in a digit, or in the x of px, which turns most class names away before any prefix is
            // compared.
            var last = cls[cls.Length - 1];
            if (last != 'x' && (last < '0' || last > '9'))
            {
                return false;
            }
            var negate = cls[0] == '-';
            var body = cls.AsSpan(negate ? 1 : 0);
            foreach (var (prefix, property, negatable) in s_spacingFamilies)
            {
                if (!body.StartsWith(prefix.AsSpan()) || (negate && !negatable))
                {
                    continue;
                }
                var suffix = body.Slice(prefix.Length);
                if (!TryGetSpacingPx(suffix, out var px))
                {
                    continue;
                }
                stylesheetBacked = !negate && SuffixInKeys(suffix, s_spacingScale);
                result = new ArbitraryStyle(property, negate ? -px : px, LengthUnit.Pixel);
                return true;
            }
            return false;
        }

        // The rotate preset (degrees). Only the NEGATIVE form routes here: positive rotate-N has a
        // static USS class, while the -rotate-N name has none (USS spells negatives as .rotate-nN).
        private static readonly Dictionary<string, float> s_rotateScale = new()
        {
            ["0"] = 0f, ["1"] = 1f, ["2"] = 2f, ["3"] = 3f, ["6"] = 6f,
            ["12"] = 12f, ["45"] = 45f, ["90"] = 90f, ["180"] = 180f,
        };

        // The per-axis scale presets (scale-x-50 -> 0.5), mirroring the uniform .scale-N USS classes in
        // _transforms.uss. These have no standalone USS class because a separate .scale-x-N / .scale-y-N rule
        // would write the whole `scale: x y` and clobber the other axis; instead they route through the
        // ScaleX/ScaleY merge path (like scale-x-[..]) so the two axes compose onto one inline `scale`.
        private static readonly Dictionary<string, float> s_axisScale = new()
        {
            ["0"] = 0f, ["50"] = 0.5f, ["75"] = 0.75f, ["90"] = 0.9f, ["95"] = 0.95f,
            ["100"] = 1f, ["105"] = 1.05f, ["110"] = 1.1f, ["125"] = 1.25f, ["150"] = 1.5f,
        };

        // Single source for the uniform .scale-N USS class's own numeric scale (identical mapping to
        // s_axisScale above, just keyed by the bare suffix rather than the "scale-x-"/"scale-y-" prefixed
        // form) — shared so MotionSpringClassParser's uniform-scale recognition resolves the SAME table
        // instead of holding a second copy that could drift, mirroring TryGetSpacingPx's precedent.
        internal static bool TryGetAxisScale(string suffix, out float scale) => s_axisScale.TryGetValue(suffix, out scale);

        // Single source for the rotate preset's magnitude (degrees), keyed by the UNSIGNED suffix — shared so
        // MotionSpringClassParser's rotate-N / rotate-nN recognition resolves the SAME magnitude table instead
        // of hand-expanding a second ±copy, mirroring TryGetSpacingPx's precedent. The caller negates the
        // result itself for the "-n"-suffixed (negative) form.
        internal static bool TryGetRotateScale(string suffix, out float degrees) => s_rotateScale.TryGetValue(suffix, out degrees);

        // Cheap dispatch gate: true when cls is a static-scale utility with NO static USS class, so
        // the reconciler must route it to TryParseStaticScale instead of the class list. That set is exactly
        // the names a USS selector cannot spell — any '-'-prefixed margin/rotate/translate name, the
        // '/'-bearing translate, sizing and position fractions, the positive per-axis translate/scale
        // presets (no USS class — a per-axis rule would clobber the other axis via the shorthand), and a
        // spacing step the bundled sheets declare no rule for (mt-13, p-4.5, -top-4). Other
        // positive non-slash names (mt-2, rotate-6) keep their USS classes and are intentionally NOT claimed here.
        internal static bool MayBeStaticScale(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            // Named filter presets (blur-sm, contrast-125, hue-rotate-90, ...) are non-bracket resolver tokens
            // too, so the one dispatch gate also claims them (incl. the negated -hue-rotate-N).
            if (StyleFilterValueParser.IsFilterPreset(cls))
            {
                return true;
            }
            // Logical-direction utilities (ms-4, start-1/2, rounded-ss-lg) and per-side border colors
            // (border-b-red-500) have no USS class at all.
            if (IsFractionToken(cls) || StyleLogicalUtilities.TryParse(cls, out _)
                || StyleBorderSideColor.TryParse(cls, out _) || TryParseSpacingStep(cls, out _))
            {
                return true;
            }
            if (cls[0] == '-')
            {
                var body = cls.AsSpan(1);
                if (body.StartsWith("skew-"))
                {
                    return false; // -skew-x-6 is owned by StyleSkewClass, not this resolver.
                }
                return IsMarginName(body) || body.StartsWith("rotate-") || body.StartsWith("translate-");
            }
            // Positive per-axis translate presets (translate-x-4, translate-y-2, translate-x-1/2, translate-x-full)
            // have no USS class: a .translate-x-N rule writes the whole `translate: x y` shorthand and clobbers
            // the other axis, so they route to the TranslateX/Y merge (like scale-x/y). The negative forms
            // (-translate-x-4) are already claimed by the '-' branch above.
            if (cls.StartsWith("translate-x-", StringComparison.Ordinal)
                || cls.StartsWith("translate-y-", StringComparison.Ordinal))
            {
                if (cls.IndexOf('/') >= 0)
                {
                    return true;
                }
                var suffix = cls.AsSpan("translate-x-".Length); // the x and y prefixes share a length
                return suffix.SequenceEqual("full".AsSpan()) || TryGetSpacingPx(suffix, out _);
            }
            // Per-axis scale presets (scale-x-50 / scale-y-110) have no USS class; route them to the merge path.
            // The bracket form (scale-x-[..]) is claimed by the dispatch's '[' check, not here. Span-based to
            // keep this per-class hot gate allocation-free (matching the AsSpan style above).
            if (cls.StartsWith("scale-x-", StringComparison.Ordinal) || cls.StartsWith("scale-y-", StringComparison.Ordinal))
            {
                return SuffixInKeys(cls.AsSpan("scale-x-".Length), s_axisScale); // "scale-x-" and "scale-y-" share a length
            }
            return TryGetFlexFactorPreset(cls, out _, out _) || TryGetFlexPreset(cls, out _);
        }

        // The physical margin utilities, whose negated names no selector can spell. -max-w-4 and -min-h-4 start
        // with the same letter and are not margins.
        private static bool IsMarginName(ReadOnlySpan<char> body)
            => body.StartsWith("m-") || body.StartsWith("mx-") || body.StartsWith("my-") || body.StartsWith("mt-")
                || body.StartsWith("mr-") || body.StartsWith("mb-") || body.StartsWith("ml-");

        // grow-<N> / shrink-<N>: Tailwind's bare factor, a whole number spelled without a sign or a leading
        // zero. grow-0 and shrink-0 are USS classes, so zero is left to the class list.
        private static bool TryGetFlexFactorPreset(string cls, out ArbitraryProperty property, out float factor)
        {
            factor = 0f;
            int prefixLength;
            if (cls.StartsWith("grow-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.FlexGrow;
                prefixLength = "grow-".Length;
            }
            else if (cls.StartsWith("shrink-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.FlexShrink;
                prefixLength = "shrink-".Length;
            }
            else
            {
                property = default;
                return false;
            }
            if (!TryParseWhole(cls.AsSpan(prefixLength), out var whole))
            {
                return false;
            }
            factor = whole;
            return whole != 0;
        }

        // flex-<N> / flex-<a>/<b>: Tailwind's `flex: <N>` (grow N, shrink 1, basis 0%) and
        // `flex: calc(<a>/<b> * 100%)` (grow 1, shrink 1, that basis). flex-1 is a USS class, so it is left to
        // the class list.
        private static bool TryGetFlexPreset(string cls, out ArbitraryStyle result)
        {
            result = default;
            if (!cls.StartsWith("flex-", StringComparison.Ordinal))
            {
                return false;
            }
            var body = cls.AsSpan("flex-".Length);
            var slash = body.IndexOf('/');
            if (slash == -1)
            {
                if (!TryParseWhole(body, out var grow))
                {
                    return false;
                }
                result = ArbitraryStyle.Flex(grow, 1f, 0f, LengthUnit.Percent);
                return grow != 1;
            }
            if (!TryParseWhole(body.Slice(0, slash), out var numerator))
            {
                return false;
            }
            if (!TryParseWhole(body.Slice(slash + 1), out var denominator))
            {
                return false;
            }
            result = ArbitraryStyle.Flex(1f, 1f, 100f * numerator / denominator, LengthUnit.Percent);
            return denominator != 0;
        }

        // A whole number spelled as Tailwind's bare values are: digits only, no sign, no leading zero. Shared by
        // the flex factors, the flex shorthand and z-<n>.
        internal static bool TryParseWhole(ReadOnlySpan<char> digits, out int whole)
        {
            whole = 0;
            if (digits.Length > 1 && digits[0] == '0')
            {
                return false;
            }
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out whole);
        }

        // True when the suffix span matches one of the preset table's keys. Allocation-free: the Dictionary
        // KeyCollection enumerates as a struct and the compare is span-based, so this stays usable on the
        // per-class hot gates that claim translate/scale presets for the per-axis merge path.
        private static bool SuffixInKeys(ReadOnlySpan<char> suffix, Dictionary<string, float> table)
        {
            foreach (var key in table.Keys)
            {
                if (suffix.SequenceEqual(key.AsSpan()))
                {
                    return true;
                }
            }
            return false;
        }

        // Parses the non-bracket static-scale forms that have no USS class: the sizing and position fractions
        // (w-1/2, -left-1/2), negative margins on the preset scale (-mt-2 -> MarginTop -8px), the negative
        // rotate preset (-rotate-6 -> -6deg), and the translate fraction / negative px translate
        // (translate-x-1/2 -> 50%, -translate-x-6 -> -24px).
        // Returns false for anything else, so positive USS-backed names fall through to the class list.
        private static bool TryParseStaticScale(string className, out ArbitraryStyle result)
        {
            result = default;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            if (TryParseFraction(className, out result))
            {
                return true;
            }
            if (TryGetFlexFactorPreset(className, out var factorProperty, out var factor))
            {
                result = new ArbitraryStyle(factorProperty, factor, LengthUnit.Pixel);
                return true;
            }
            if (TryGetFlexPreset(className, out result))
            {
                return true;
            }
            if (TryParseSpacingStep(className, out result))
            {
                return true;
            }
            var negate = className[0] == '-';
            var body = negate ? className.Substring(1) : className;

            if (negate)
            {
                var negated = TryParseNegatedPreset(body, out result);
                if (negated.HasValue)
                {
                    return negated.Value;
                }
            }

            var scale = TryParseAxisScalePreset(body, negate, out result);
            if (scale.HasValue)
            {
                return scale.Value;
            }

            return TryParseTranslatePreset(body, negate, out result);
        }

        // The negated rotate preset, whose table carries the magnitude and takes its sign from the name (see
        // s_rotateScale for why only the negated name routes here). Null when body is not a rotate.
        private static bool? TryParseNegatedPreset(string body, out ArbitraryStyle result)
        {
            result = default;
            if (body.StartsWith("rotate-", StringComparison.Ordinal))
            {
                if (!s_rotateScale.TryGetValue(body.Substring("rotate-".Length), out var deg))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.Rotate, -deg, LengthUnit.Pixel);
                return true;
            }
            return null;
        }

        // Per-axis scale presets (positive only; a flip is the arbitrary scale-x-[-1]). Routed to the same
        // ScaleX/ScaleY merge as the bracket form so the axes compose onto one inline `scale`. Null when body
        // names neither axis.
        private static bool? TryParseAxisScalePreset(string body, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return null;
            }
            var isScaleX = body.StartsWith("scale-x-", StringComparison.Ordinal);
            if (!isScaleX && !body.StartsWith("scale-y-", StringComparison.Ordinal))
            {
                return null;
            }
            if (!s_axisScale.TryGetValue(body.Substring("scale-x-".Length), out var factor))
            {
                return false;
            }
            result = new ArbitraryStyle(
                isScaleX ? ArbitraryProperty.ScaleX : ArbitraryProperty.ScaleY, factor, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseTranslatePreset(string body, bool negate, out ArbitraryStyle result)
        {
            result = default;
            var isX = body.StartsWith("translate-x-", StringComparison.Ordinal);
            var isY = !isX && body.StartsWith("translate-y-", StringComparison.Ordinal);
            if (!isX && !isY)
            {
                return false;
            }

            var suffix = body.Substring("translate-x-".Length); // the x and y prefixes share a length
            var property = isX ? ArbitraryProperty.TranslateX : ArbitraryProperty.TranslateY;
            // The slash form (percent of the element's own size), translate-x-full (100%) and the spacing-scale
            // presets route to the same TranslateX/Y merge so an x and a y preset compose instead of clobbering
            // via the `translate` shorthand.
            if (TryParseFractionPercent(suffix, out var pct))
            {
                result = new ArbitraryStyle(property, negate ? -pct : pct, LengthUnit.Percent);
                return true;
            }
            if (suffix == "full")
            {
                result = new ArbitraryStyle(property, negate ? -100f : 100f, LengthUnit.Percent);
                return true;
            }
            // Both signs route here now: a positive .translate-x-N USS class would write the whole
            // `translate` shorthand and clobber the other axis, so positives merge through TranslateX/Y too.
            if (TryGetSpacingPx(suffix, out var px))
            {
                result = new ArbitraryStyle(property, negate ? -px : px, LengthUnit.Pixel);
                return true;
            }
            return false;
        }

        private static readonly Dictionary<string, ArbitraryProperty> s_sliceInsetPrefixes = new()
        {
            ["slice-"] = ArbitraryProperty.Slice,
            ["slice-x-"] = ArbitraryProperty.SliceX,
            ["slice-y-"] = ArbitraryProperty.SliceY,
            ["slice-t-"] = ArbitraryProperty.SliceTop,
            ["slice-r-"] = ArbitraryProperty.SliceRight,
            ["slice-b-"] = ArbitraryProperty.SliceBottom,
            ["slice-l-"] = ArbitraryProperty.SliceLeft,
        };

        // Null when prefix names neither family.
        private static bool? TryParseSliceValue(
            string prefix, ReadOnlySpan<char> value, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (prefix == "slice-scale-")
            {
                if (negate || !TryParseFloat(value, out var factor) || factor < 0f)
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.SliceScale, factor, LengthUnit.Pixel);
                return true;
            }
            if (!s_sliceInsetPrefixes.TryGetValue(prefix, out var property))
            {
                return null;
            }
            if (negate)
            {
                return false;
            }
            if (property == ArbitraryProperty.Slice)
            {
                return TryParseSliceShorthand(value, out result);
            }
            if (!TryParseSliceInset(value, out var inset, out var percent))
            {
                return false;
            }
            result = new ArbitraryStyle(property, inset, 0f, 0f, 0f, percent ? 1 : 0);
            return true;
        }

        // border-image-slice's one to four values, '_' standing for the space between them: one sets every edge,
        // two the vertical then the horizontal pair, three the top, the horizontal pair and the bottom, and four
        // the top, right, bottom and left. The fill keyword may stand before or after them; UI Toolkit draws a
        // sliced background's centre whether or not it is given, so it is read and dropped.
        private static bool TryParseSliceShorthand(ReadOnlySpan<char> value, out ArbitraryStyle result)
        {
            result = default;
            value = WithoutFill(value);
            Span<float> insets = stackalloc float[4];
            Span<bool> percents = stackalloc bool[4];
            var count = 0;
            while (true)
            {
                var gap = value.IndexOf('_');
                // MUTANT_SURVIVES(equivalent, boundary): at a gap of 0 the part is empty or starts with '_', and
                // neither parses as an inset.
                var part = gap < 0 ? value : value.Slice(0, gap);
                if (count == insets.Length || !TryParseSliceInset(part, out insets[count], out percents[count]))
                {
                    return false;
                }
                count++;
                // MUTANT_SURVIVES(equivalent, boundary): a gap of 0 left an empty part, which returned above.
                if (gap < 0)
                {
                    break;
                }
                value = value.Slice(gap + 1);
            }
            // The given value each of the right, bottom and left edges takes when fewer than four are given.
            var right = count > 1 ? 1 : 0;
            var bottom = count > 2 ? 2 : 0;
            var left = count > 3 ? 3 : right;
            var percentEdges = (percents[0] ? 1 : 0) | (percents[right] ? 2 : 0) | (percents[bottom] ? 4 : 0)
                | (percents[left] ? 8 : 0);
            result = new ArbitraryStyle(ArbitraryProperty.Slice, insets[0], insets[right], insets[bottom], insets[left],
                percentEdges);
            return true;
        }

        private static ReadOnlySpan<char> WithoutFill(ReadOnlySpan<char> value)
        {
            if (value.StartsWith("fill_".AsSpan()))
            {
                return value.Slice("fill_".Length);
            }
            return value.EndsWith("_fill".AsSpan()) ? value.Slice(0, value.Length - "_fill".Length) : value;
        }

        // A whole, non-negative number of pixels, bare as border-image-slice writes it or with px, or a
        // non-negative percentage of the image's size.
        private static bool TryParseSliceInset(ReadOnlySpan<char> value, out float inset, out bool percent)
        {
            percent = value.EndsWith("%".AsSpan());
            if (percent)
            {
                return TryParseFloat(value.Slice(0, value.Length - 1), out inset) && inset >= 0f;
            }
            var digits = value.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase) ? value.Slice(0, value.Length - 2) : value;
            var whole = int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var pixels);
            inset = pixels;
            return whole;
        }

        // Parses a duration-[..] time value to SECONDS. Accepts "<n>ms" / "<n>s" (duration-[400ms] /
        // duration-[.4s]); a bare number is rejected (a unit is required on arbitrary durations).
        private static bool TryParseDurationSeconds(ReadOnlySpan<char> value, out float seconds)
        {
            seconds = 0f;
            float scale;
            ReadOnlySpan<char> num;
            if (value.EndsWith("ms".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                num = value.Slice(0, value.Length - 2);
                scale = 0.001f;
            }
            else if (value.EndsWith("s".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                num = value.Slice(0, value.Length - 1);
                scale = 1f;
            }
            else
            {
                return false;
            }
            if (!float.TryParse(num.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                || float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return false;
            }
            seconds = v * scale;
            return true;
        }

        // The fraction families (w-1/2, min-h-1/3, basis-3/4, left-1/2, inset-x-1/4) resolve to an inline percent,
        // for any a/b of non-negative integers as in Tailwind (left-3/2 is 150%). Only a position offset takes a
        // sign (-left-1/2) and the `full` (left-full, -left-full) and `auto` keywords, as in Tailwind.
        // The inset-x-/inset-y- rows precede inset-, because the first row whose prefix matches decides.
        private static readonly (string Prefix, ArbitraryProperty Property, bool Position)[] s_fractionFamilies =
        {
            ("w-", ArbitraryProperty.Width, false),
            ("h-", ArbitraryProperty.Height, false),
            ("size-", ArbitraryProperty.Size, false),
            ("min-w-", ArbitraryProperty.MinWidth, false),
            ("min-h-", ArbitraryProperty.MinHeight, false),
            ("max-w-", ArbitraryProperty.MaxWidth, false),
            ("max-h-", ArbitraryProperty.MaxHeight, false),
            ("basis-", ArbitraryProperty.FlexBasis, false),
            ("top-", ArbitraryProperty.Top, true),
            ("right-", ArbitraryProperty.Right, true),
            ("bottom-", ArbitraryProperty.Bottom, true),
            ("left-", ArbitraryProperty.Left, true),
            ("inset-x-", ArbitraryProperty.InsetX, true),
            ("inset-y-", ArbitraryProperty.InsetY, true),
            ("inset-", ArbitraryProperty.Inset, true),
        };

        // -1 when cls names no fraction family, or negates one that takes no sign.
        private static int FractionFamilyOf(string cls)
        {
            var negate = cls[0] == '-';
            var body = cls.AsSpan(negate ? 1 : 0);
            for (var i = 0; i < s_fractionFamilies.Length; i++)
            {
                if (body.StartsWith(s_fractionFamilies[i].Prefix.AsSpan()))
                {
                    return negate && !s_fractionFamilies[i].Position ? -1 : i;
                }
            }
            return -1;
        }

        // The sizing `*-full` and `*-auto` classes are USS rules, so only a position family's keywords are claimed,
        // and `-auto` only unnegated, as TryParseFraction reads it.
        private static bool IsFractionToken(string cls)
        {
            var family = FractionFamilyOf(cls);
            return family >= 0
                && (cls.IndexOf('/') >= 0
                    || (s_fractionFamilies[family].Position
                        && (cls.EndsWith("-full", StringComparison.Ordinal)
                            || (cls[0] != '-' && cls.EndsWith("-auto", StringComparison.Ordinal)))));
        }

        // Digits only with no sign or leading zero, as Tailwind's isPositiveInteger reads a fraction's halves.
        private static bool TryParseFractionPart(string text, out int value)
        {
            value = 0;
            if (text.Length == 0 || text.Length > 9 || (text.Length > 1 && text[0] == '0'))
            {
                return false;
            }
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
                value = value * 10 + (text[i] - '0');
            }
            return true;
        }

        // A zero denominator does not parse: no percent stands for it.
        internal static bool TryParseFractionPercent(string frac, out float percent)
        {
            percent = 0f;
            var slash = frac.IndexOf('/');
            if (slash < 0
                || !TryParseFractionPart(frac.Substring(0, slash), out var n)
                || !TryParseFractionPart(frac.Substring(slash + 1), out var d)
                || d == 0)
            {
                return false;
            }
            percent = 100f * n / d;
            return true;
        }

        // `auto` is never negated, as in Tailwind, which has no -top-auto.
        private static bool TryParseFraction(string className, out ArbitraryStyle result)
        {
            result = default;
            var family = FractionFamilyOf(className);
            if (family < 0)
            {
                return false;
            }
            var (prefix, property, position) = s_fractionFamilies[family];
            var negate = className[0] == '-';
            var frac = className.Substring((negate ? 1 : 0) + prefix.Length);
            float percent;
            if (position && frac == "auto" && !negate)
            {
                result = ArbitraryStyle.AutoLength(property);
                return true;
            }
            if (position && frac == "full")
            {
                percent = 100f;
            }
            else if (!TryParseFractionPercent(frac, out percent))
            {
                return false;
            }
            result = new ArbitraryStyle(property, negate ? -percent : percent, LengthUnit.Percent);
            return true;
        }

        #endregion

        #region Delegate Table

        // Single source of truth for Apply / Clear. Shorthands hold multiple setters. Each setter names the axis
        // CSS takes a percentage of for the longhand it writes, which a relative length (StyleLengthExpression)
        // resolves against — so a vertical padding or margin edge reads the parent's width, as in CSS.
        private static readonly Dictionary<ArbitraryProperty, LengthSetter[]> PropertySetters = new()
        {
            [ArbitraryProperty.Width] = new[] { Across((s, v) => s.width = v) },
            [ArbitraryProperty.Height] = new[] { Down((s, v) => s.height = v) },
            [ArbitraryProperty.MinWidth] = new[] { new LengthSetter((s, v) => s.minWidth = v, RelativeLengthAxis.Width, null) },
            [ArbitraryProperty.MinHeight] = new[] { new LengthSetter((s, v) => s.minHeight = v, RelativeLengthAxis.Height, null) },
            [ArbitraryProperty.MaxWidth] = new[] { new LengthSetter((s, v) => s.maxWidth = v, RelativeLengthAxis.Width, StyleKeyword.None) },
            [ArbitraryProperty.MaxHeight] = new[] { new LengthSetter((s, v) => s.maxHeight = v, RelativeLengthAxis.Height, StyleKeyword.None) },
            [ArbitraryProperty.Top] = new[] { Down((s, v) => s.top = v) },
            [ArbitraryProperty.Right] = new[] { Across((s, v) => s.right = v) },
            [ArbitraryProperty.Bottom] = new[] { Down((s, v) => s.bottom = v) },
            [ArbitraryProperty.Left] = new[] { Across((s, v) => s.left = v) },
            [ArbitraryProperty.Inset] = new[]
            {
                Down((s, v) => s.top = v), Across((s, v) => s.right = v),
                Down((s, v) => s.bottom = v), Across((s, v) => s.left = v),
            },
            [ArbitraryProperty.InsetX] = new[] { Across((s, v) => s.left = v), Across((s, v) => s.right = v) },
            [ArbitraryProperty.InsetY] = new[] { Down((s, v) => s.top = v), Down((s, v) => s.bottom = v) },
            [ArbitraryProperty.PaddingTop] = new[] { Edge((s, v) => s.paddingTop = v) },
            [ArbitraryProperty.PaddingRight] = new[] { Edge((s, v) => s.paddingRight = v) },
            [ArbitraryProperty.PaddingBottom] = new[] { Edge((s, v) => s.paddingBottom = v) },
            [ArbitraryProperty.PaddingLeft] = new[] { Edge((s, v) => s.paddingLeft = v) },
            [ArbitraryProperty.Padding] = new[]
            {
                Edge((s, v) => s.paddingTop = v), Edge((s, v) => s.paddingRight = v),
                Edge((s, v) => s.paddingBottom = v), Edge((s, v) => s.paddingLeft = v),
            },
            [ArbitraryProperty.PaddingX] = new[] { Edge((s, v) => s.paddingLeft = v), Edge((s, v) => s.paddingRight = v) },
            [ArbitraryProperty.PaddingY] = new[] { Edge((s, v) => s.paddingTop = v), Edge((s, v) => s.paddingBottom = v) },
            [ArbitraryProperty.MarginTop] = new[] { Edge((s, v) => s.marginTop = v) },
            [ArbitraryProperty.MarginRight] = new[] { Edge((s, v) => s.marginRight = v) },
            [ArbitraryProperty.MarginBottom] = new[] { Edge((s, v) => s.marginBottom = v) },
            [ArbitraryProperty.MarginLeft] = new[] { Edge((s, v) => s.marginLeft = v) },
            [ArbitraryProperty.Margin] = new[]
            {
                Edge((s, v) => s.marginTop = v), Edge((s, v) => s.marginRight = v),
                Edge((s, v) => s.marginBottom = v), Edge((s, v) => s.marginLeft = v),
            },
            [ArbitraryProperty.MarginX] = new[] { Edge((s, v) => s.marginLeft = v), Edge((s, v) => s.marginRight = v) },
            [ArbitraryProperty.MarginY] = new[] { Edge((s, v) => s.marginTop = v), Edge((s, v) => s.marginBottom = v) },
            [ArbitraryProperty.FontSize] = new[] { new LengthSetter((s, v) => s.fontSize = v, RelativeLengthAxis.ParentFont, StyleKeyword.Null) },
            [ArbitraryProperty.LetterSpacing] = new[] { new LengthSetter((s, v) => s.letterSpacing = v, RelativeLengthAxis.OwnFont, StyleKeyword.Null) },
            // size-[..] fans out to width + height (same dual-setter shape as Inset).
            [ArbitraryProperty.Size] = new[] { Across((s, v) => s.width = v), Down((s, v) => s.height = v) },
            [ArbitraryProperty.FlexBasis] = new[] { new LengthSetter((s, v) => s.flexBasis = v, RelativeLengthAxis.MainAxis, StyleKeyword.Auto) },
        };

        private readonly struct LengthSetter
        {
            public readonly Action<IStyle, StyleLength> Set;
            public readonly RelativeLengthAxis Axis;
            // What a percentage of an indefinite size writes: auto where the longhand takes it, none on a maximum.
            // Null on a minimum, a padding or a margin, whose percentage is then taken as zero and the rest of the
            // length kept, which is what CSS gives such a percentage while it sizes the parent.
            public readonly StyleKeyword? Indefinite;

            public LengthSetter(Action<IStyle, StyleLength> set, RelativeLengthAxis axis, StyleKeyword? indefinite)
            {
                Set = set;
                Axis = axis;
                Indefinite = indefinite;
            }
        }

        private static LengthSetter Across(Action<IStyle, StyleLength> set) => new(set, RelativeLengthAxis.Width, StyleKeyword.Auto);

        private static LengthSetter Down(Action<IStyle, StyleLength> set) => new(set, RelativeLengthAxis.Height, StyleKeyword.Auto);

        private static LengthSetter Edge(Action<IStyle, StyleLength> set) => new(set, RelativeLengthAxis.Width, null);

        // The corner radii are not written here but handed to CornerRadiusFit, which owns those four slots.
        private static readonly Dictionary<ArbitraryProperty, RadiusCorners> RadiusCornersOf = new()
        {
            [ArbitraryProperty.BorderRadius] = RadiusCorners.All,
            [ArbitraryProperty.BorderTopRadius] = RadiusCorners.TopLeft | RadiusCorners.TopRight,
            [ArbitraryProperty.BorderRightRadius] = RadiusCorners.TopRight | RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderBottomRadius] = RadiusCorners.BottomLeft | RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderLeftRadius] = RadiusCorners.TopLeft | RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = RadiusCorners.TopLeft,
            [ArbitraryProperty.BorderTopRightRadius] = RadiusCorners.TopRight,
            [ArbitraryProperty.BorderBottomLeftRadius] = RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderBottomRightRadius] = RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderStartRadius] = RadiusCorners.TopLeft | RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderEndRadius] = RadiusCorners.TopRight | RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderStartStartRadius] = RadiusCorners.TopLeft,
            [ArbitraryProperty.BorderStartEndRadius] = RadiusCorners.TopRight,
            [ArbitraryProperty.BorderEndStartRadius] = RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderEndEndRadius] = RadiusCorners.BottomRight,
        };

        // Color-valued counterpart to PropertySetters. Color properties take a
        // StyleColor rather than a StyleLength.
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleColor>[]> ColorSetters = new()
        {
            [ArbitraryProperty.TextColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.color = v },
            [ArbitraryProperty.BackgroundColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.backgroundColor = v },
            [ArbitraryProperty.BorderColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderTopColor = v, (s, v) => s.borderRightColor = v,
                (s, v) => s.borderBottomColor = v, (s, v) => s.borderLeftColor = v,
            },
            [ArbitraryProperty.BorderTopColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderTopColor = v },
            [ArbitraryProperty.BorderRightColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderRightColor = v },
            [ArbitraryProperty.BorderBottomColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderBottomColor = v },
            [ArbitraryProperty.BorderLeftColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderLeftColor = v },
            [ArbitraryProperty.BorderXColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderLeftColor = v, (s, v) => s.borderRightColor = v,
            },
            [ArbitraryProperty.BorderYColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderTopColor = v, (s, v) => s.borderBottomColor = v,
            },
            [ArbitraryProperty.BorderInlineStartColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderLeftColor = v },
            [ArbitraryProperty.BorderInlineEndColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderRightColor = v },
            [ArbitraryProperty.BorderBlockStartColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderTopColor = v },
            [ArbitraryProperty.BorderBlockEndColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderBottomColor = v },
        };

        // Float-valued counterpart to PropertySetters. Border widths are
        // StyleFloat (pixels) rather than StyleLength; the value's unit
        // is ignored (percent border widths are not meaningful).
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleFloat>[]> FloatSetters = new()
        {
            [ArbitraryProperty.BorderWidth] = new Action<IStyle, StyleFloat>[]
            {
                (s, v) => s.borderTopWidth = v, (s, v) => s.borderRightWidth = v,
                (s, v) => s.borderBottomWidth = v, (s, v) => s.borderLeftWidth = v,
            },
            [ArbitraryProperty.BorderTopWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderTopWidth = v },
            [ArbitraryProperty.BorderRightWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderRightWidth = v },
            [ArbitraryProperty.BorderBottomWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderBottomWidth = v },
            [ArbitraryProperty.BorderLeftWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderLeftWidth = v },
            // grow-[..] / shrink-[..] are unitless factors.
            [ArbitraryProperty.FlexGrow] = new Action<IStyle, StyleFloat>[] { (s, v) => s.flexGrow = v },
            [ArbitraryProperty.FlexShrink] = new Action<IStyle, StyleFloat>[] { (s, v) => s.flexShrink = v },
            [ArbitraryProperty.SliceScale] = new Action<IStyle, StyleFloat>[] { (s, v) => s.unitySliceScale = v },
        };

        // Integer-valued counterpart to PropertySetters: the nine-slice insets. Slice is here for ClearInline;
        // ApplyInline writes its four edges from the payload itself.
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleInt>[]> IntSetters = new()
        {
            [ArbitraryProperty.Slice] = new Action<IStyle, StyleInt>[]
            {
                (s, v) => s.unitySliceTop = v, (s, v) => s.unitySliceRight = v,
                (s, v) => s.unitySliceBottom = v, (s, v) => s.unitySliceLeft = v,
            },
            [ArbitraryProperty.SliceX] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceLeft = v, (s, v) => s.unitySliceRight = v },
            [ArbitraryProperty.SliceY] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceTop = v, (s, v) => s.unitySliceBottom = v },
            [ArbitraryProperty.SliceTop] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceTop = v },
            [ArbitraryProperty.SliceRight] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceRight = v },
            [ArbitraryProperty.SliceBottom] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceBottom = v },
            [ArbitraryProperty.SliceLeft] = new Action<IStyle, StyleInt>[] { (s, v) => s.unitySliceLeft = v },
        };

        #endregion

        // Per-element, per-property stack of arbitrary-value layers keyed by priority (ascending). A
        // ConditionalWeakTable auto-drops entries when an element is GC'd; pooled (reused) elements are scrubbed
        // explicitly via ClearAll so no layer ghosts across reuse.
        private sealed class LayerMap : Dictionary<ArbitraryProperty, SortedList<long, ArbitraryStyle>>,
            StyleClassProjection.ILayerHost
        {
            // Per-NAME priority stacks for filter-[name:args] custom filters, in first-application
            // order. Unlike every other arbitrary property, a custom filter cannot share the single
            // FilterCustom slot in the base dictionary above — "dissolve" and "glow" (or a base and a
            // hover layer of the SAME name) would clobber each other. A LIST of (name, stack) entries
            // rather than a dictionary: the entry index IS the compose slot, and lookups are linear (an
            // element realistically carries a handful of names). An entry whose stack has EMPTIED is
            // kept as a tombstone rather than removed — the class-diff path updates a changed token by
            // clearing the old value and applying the new one, and dropping the entry in between would
            // re-slot the name to the end, visibly reordering two co-applied custom filters on the first
            // argument change. Compose skips empty stacks; ClearAll drops the whole map, so tombstones
            // die with the rest of the element's layer state. Lazily allocated: most elements never
            // apply a custom filter.
            public List<(string Name, SortedList<long, ArbitraryStyle> Stack)>? Customs;

            // Whether the filter last composed from this map was dropped for a name no live definition answers.
            public bool FilterChainDropped;

            // The class-list half of the same cascade (StyleClassProjection). It lives here because the two
            // halves decide one outcome together — an inline layer a USS class outranks has to stop
            // painting, and a class an inline layer outranks has to stop counting — and because sharing this
            // map's lifetime means ClearAll scrubs both at once, so no element can reach the pool carrying
            // half a verdict. Null until some payload above the base priority needs one.
            public StyleClassProjection.Model? Projection;

            // Per property, the priority below which every layer is fully overridden by a USS class and must
            // not be written inline. Absent means nothing is masked. Lazily allocated: only an element mixing
            // a bracket value with a variant-applied class ever gets one.
            public Dictionary<ArbitraryProperty, long>? Floors;

            // Per property, the longhands its winning layer writes that a class claims at a key above the layer's.
            // Absent means no class does.
            public Dictionary<ArbitraryProperty, StyleLonghandSet>? Claims;

            // The slots a gap, grid or divide manipulator holds on this element — see Hold. Lazily
            // allocated: only an element one of them writes to gets one.
            public StyleHeldSlots? Holds;

            // What a per-frame driver last wrote, by property, until ReleaseDriven — see ApplyDriven. Lazily
            // allocated: only an element a driver writes through ApplyDriven gets one.
            public Dictionary<ArbitraryProperty, DrivenWrite>? Driven;

            // The containers that re-apply when a class of this element's own changes — see
            // StyleChildOwnership. Lazily allocated: only a claimed child gets one.
            public List<IChildClassWatcher>? Watchers;

            // Set while a winning layer holds a relative length (ArbitraryStyle.Expression) — see
            // ReresolveRelativeLengths.
            public RelativeLengthPoll? RelativePoll;

            // What each writer of the background image last asked for — see WriteBackgroundImage. Lazily
            // allocated: only an element something writes a background image to gets one.
            public BackgroundImageSources? BackgroundImage;

            // Watches the image a percentage inset was resolved against — see WatchShownImage. Lazily allocated:
            // only an element holding a percentage inset gets one.
            public ShownImageWatch? ImageWatch;

            private readonly Dictionary<(ArbitraryProperty Property, long Priority), long> _arrivals = new();
            private long _nextArrival;

            public void RecordArrival(ArbitraryProperty property, long priority)
            {
                if (_nextArrival == long.MaxValue)
                {
                    // Keep every live layer's relative age, including layers hidden below a winner.
                    // Compact only at the boundary so ordinary Apply never scans the other layers.
                    var arrivals = new List<KeyValuePair<(ArbitraryProperty, long), long>>(_arrivals);
                    arrivals.Sort((a, z) => a.Value.CompareTo(z.Value));
                    _nextArrival = 0;
                    foreach (var arrival in arrivals)
                    {
                        _arrivals[arrival.Key] = ++_nextArrival;
                    }
                }
                _arrivals[(property, priority)] = ++_nextArrival;
            }

            public long ArrivalOf(ArbitraryProperty property, long priority)
                => _arrivals[(property, priority)];

            public void RemoveArrival(ArbitraryProperty property, long priority)
                => _arrivals.Remove((property, priority));

            public bool HasLayers => Count > 0;

            public void CollectLayers(List<StyleClassProjection.InlineLayer> into)
                => CollectLayerPriorities(this, into);

            public void ApplyFloors(VisualElement element, Dictionary<ArbitraryProperty, long> floors)
                => ApplyProjectionFloors(element, this, floors);
        }

        private static readonly ConditionalWeakTable<VisualElement, LayerMap> s_layers = new();

        // The element's class projection, created (with its layer map) on demand.
        internal static StyleClassProjection.Model GetOrCreateProjection(VisualElement element)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            return map.Projection ??= new StyleClassProjection.Model(map);
        }

        // The element's class projection, or null when nothing has needed one.
        internal static StyleClassProjection.Model? TryGetProjection(VisualElement element)
            => element != null && s_layers.TryGetValue(element, out var map) ? map.Projection : null;

        internal static bool HasImportantInlineOutside(VisualElement element, StyleLonghandSet written,
            HashSet<ArbitraryProperty> swapped)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return false;
            }
            foreach (var pair in map)
            {
                if (!StyleArbitraryLonghands.Of(pair.Key).Overlaps(written))
                {
                    continue;
                }
                foreach (var priority in pair.Value.Keys)
                {
                    var swappedBase = swapped.Contains(pair.Key)
                        && priority <= StyleLayerPriority.WithRule(
                            StyleLayerPriority.ImportantOf(StyleLayerPriority.Base), int.MaxValue);
                    if ((priority & StyleLayerPriority.Important) != 0 && !swappedBase)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Reports every registered layer as (property, priority) so the projection can rank inline layers
        // against USS classes. The name-keyed custom-filter stacks are not reported: a registered name is
        // not an ArbitraryProperty, and the whole filter family is held out of the comparison anyway (see
        // StyleArbitraryLonghands), so there would be nothing to rank it against.
        private static void CollectLayerPriorities(LayerMap map, List<StyleClassProjection.InlineLayer> into)
        {
            foreach (var pair in map)
            {
                var priorities = pair.Value.Keys;
                for (var i = 0; i < priorities.Count; i++)
                {
                    into.Add(new StyleClassProjection.InlineLayer(pair.Key, priorities[i]));
                }
            }
        }

        // Installs the projection's verdict about which inline layers a USS class has overridden, and
        // re-resolves the properties whose verdict changed. Re-resolving is what actually removes the inline
        // value that was hiding the winning class, or restores it once the class stops winning.
        private static void ApplyProjectionFloors(
            VisualElement element, LayerMap map, Dictionary<ArbitraryProperty, long> floors)
        {
            var previous = map.Floors;
            if (!SameFloors(previous, floors))
            {
                map.Floors = floors.Count == 0 ? null : new Dictionary<ArbitraryProperty, long>(floors);
                ResolveMoved(element, map, previous, map.Floors);
            }
            // After the floors, which decide the winning layers the claims are measured against.
            var previousClaims = map.Claims;
            map.Claims = ClaimsAbove(map);
            ResolveMoved(element, map, previousClaims, map.Claims);
        }

        // Re-resolves every property whose entry differs between previous and current, an absent map
        // standing for an empty one.
        private static void ResolveMoved<T>(VisualElement element, LayerMap map,
            Dictionary<ArbitraryProperty, T>? previous, Dictionary<ArbitraryProperty, T>? current)
            where T : IEquatable<T>
        {
            if (current != null)
            {
                foreach (var pair in current)
                {
                    if (previous == null || !previous.TryGetValue(pair.Key, out var was) || !was.Equals(pair.Value))
                    {
                        ResolveAndApply(element, pair.Key, map);
                    }
                }
            }
            if (previous == null)
            {
                return;
            }
            foreach (var property in previous.Keys)
            {
                if (current == null || !current.ContainsKey(property))
                {
                    ResolveAndApply(element, property, map);
                }
            }
        }

        // A class claiming only some of what a winning layer writes changes no floor, so the floors alone would
        // never re-resolve that layer for ClearClaimedLonghands to give the class its longhands; a change in
        // which longhands are claimed does. Null when no class claims anything above a winning layer.
        private static Dictionary<ArbitraryProperty, StyleLonghandSet>? ClaimsAbove(LayerMap map)
        {
            if (map.Projection == null)
            {
                return null;
            }
            Dictionary<ArbitraryProperty, StyleLonghandSet>? claims = null;
            foreach (var pair in map)
            {
                if (!TryWinningLayer(map, pair.Key, out _))
                {
                    continue;
                }
                var top = pair.Value.Keys[pair.Value.Count - 1];
                var written = StyleArbitraryLonghands.Of(pair.Key);
                var claimed = StyleLonghandSet.Empty;
                foreach (var longhand in s_longhands)
                {
                    if (written.Contains(longhand) && map.Projection.ClaimOf(longhand) > top)
                    {
                        claimed = claimed.Union(StyleLonghandSet.Of(longhand));
                    }
                }
                if (!claimed.IsEmpty)
                {
                    (claims ??= new Dictionary<ArbitraryProperty, StyleLonghandSet>())[pair.Key] = claimed;
                }
            }
            return claims;
        }

        private static bool SameFloors(Dictionary<ArbitraryProperty, long>? previous, Dictionary<ArbitraryProperty, long> floors)
        {
            var previousCount = previous?.Count ?? 0;
            if (previousCount != floors.Count)
            {
                return false;
            }
            foreach (var pair in floors)
            {
                if (!previous!.TryGetValue(pair.Key, out var was) || was != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        // The highest-priority layer for property, unless a USS class has overridden it. A floor records the
        // highest priority a class beat, and layers die from the bottom up, so the top layer sitting at or
        // below the floor means every layer does.
        private static bool TryWinningLayer(LayerMap map, ArbitraryProperty property, out ArbitraryStyle style)
        {
            style = default;
            if (!map.TryGetValue(property, out var layers) || layers.Count == 0)
            {
                return false;
            }
            var top = layers.Count - 1;
            if (map.Floors != null && map.Floors.TryGetValue(property, out var floor) && layers.Keys[top] <= floor)
            {
                return false;
            }
            style = layers.Values[top];
            return true;
        }

        // Registers style at priority for its property and applies the
        // winning (highest-priority) layer inline. Base utilities use StyleLayerPriority.Base
        // (the default); variant manipulators pass their state's priority so a variant layers OVER the base.
        public static void Apply(VisualElement element, in ArbitraryStyle style, long priority = StyleLayerPriority.Base)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            if (style.Property == ArbitraryProperty.FilterCustom)
            {
                if (FindCustomStack(map, style.Custom!.Name) == null)
                {
                    AddCustomFilterHost(style.Custom.Name, element);
                }
                ApplyCustomFilterLayer(map, style, priority);
            }
            else
            {
                if (!map.TryGetValue(style.Property, out var layers))
                {
                    layers = new SortedList<long, ArbitraryStyle>();
                    map[style.Property] = layers;
                }
                layers[priority] = style;
                map.RecordArrival(style.Property, priority);
            }
            ResolveAndApply(element, style.Property, map);
            Reproject(element, map);
        }

        // Re-runs the class verdict after a layer changed. A new layer can outrank a class that was winning,
        // and a departing one can hand a property back to a class that was suppressed, so the two halves have
        // to be re-derived together. Nothing to do on the vast majority of elements, which never build a
        // projection at all.
        private static void Reproject(VisualElement element, LayerMap map)
        {
            if (map.Projection != null)
            {
                var previousUniform = UniformScaleClassFallback(element);
                StyleClassProjection.OnInlineLayersChanged(element, map.Projection);
                RefreshScaleClassFallback(element, map, previousUniform);
            }
        }

        // Per registered name, the elements holding a layer stack under it: a registration change re-resolves
        // those and no others. An element joins a name when its map first holds that name's stack and leaves when
        // ClearAll drops the map, so it is listed once per name. One the collector took without a ClearAll is
        // pruned when its name next grows past twice the size it had at the last prune, or is re-resolved.
        private static readonly Dictionary<string, (List<WeakReference<VisualElement>> Hosts, int Pruned)> s_customFilterHosts = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCustomFilterHosts() => s_customFilterHosts.Clear();
#endif

        private static void AddCustomFilterHost(string name, VisualElement element)
        {
            if (!s_customFilterHosts.TryGetValue(name, out var entry))
            {
                entry = (new List<WeakReference<VisualElement>>(), 0);
            }
            if (entry.Hosts.Count >= 2 * Math.Max(entry.Pruned, 8))
            {
                entry.Hosts.RemoveAll(host => !host.TryGetTarget(out _));
                entry.Pruned = entry.Hosts.Count;
            }
            entry.Hosts.Add(new WeakReference<VisualElement>(element));
            s_customFilterHosts[name] = entry;
        }

        private static void RemoveCustomFilterHosts(VisualElement element, LayerMap map)
        {
            if (map.Customs == null)
            {
                return;
            }
            foreach (var (name, _) in map.Customs)
            {
                if (s_customFilterHosts.TryGetValue(name, out var entry))
                {
                    entry.Hosts.RemoveAll(host => !host.TryGetTarget(out var held) || held == element);
                }
            }
        }

        // Parses every filter-[name:args] layer held under name again against what VelvetFilters now has
        // registered under it, and recomposes each element holding one.
        internal static void ReresolveCustomFilter(string name)
        {
            if (!s_customFilterHosts.TryGetValue(name, out var entry))
            {
                return;
            }
            var hosts = entry.Hosts;
            for (var i = hosts.Count - 1; i >= 0; i--)
            {
                if (!hosts[i].TryGetTarget(out var element) || !s_layers.TryGetValue(element, out var map))
                {
                    hosts.RemoveAt(i);
                    continue;
                }
                var stack = FindCustomStack(map, name);
                if (stack == null || stack.Count == 0)
                {
                    continue;
                }
                for (var k = 0; k < stack.Count; k++)
                {
                    StyleFilterValueParser.TryParseCustomFilter("filter-", stack.Values[k].Custom!.Token.AsSpan(), false, out var style);
                    stack[stack.Keys[k]] = style;
                }
                ApplyCombinedFilter(element, map);
            }
            entry.Pruned = hosts.Count;
            s_customFilterHosts[name] = entry;
        }

        // Registers a filter-[name:args] layer at priority under its own per-name stack (LayerMap.Customs),
        // appending a new entry on the name's first application so the compose order in
        // ApplyCombinedFilter is stable first-application order. A later re-apply — including one after
        // the stack emptied (its tombstone keeps the entry) — reuses the existing entry, preserving the
        // name's original compose slot.
        private static void ApplyCustomFilterLayer(LayerMap map, in ArbitraryStyle style, long priority)
        {
            var name = style.Custom!.Name;
            var stack = FindCustomStack(map, name);
            if (stack == null)
            {
                stack = new SortedList<long, ArbitraryStyle>();
                (map.Customs ??= new List<(string, SortedList<long, ArbitraryStyle>)>()).Add((name, stack));
            }
            stack[priority] = style;
        }

        // The custom filter layer stack registered under name, or null when the name has never been
        // applied to this element. Linear scan by design: see LayerMap.Customs.
        private static SortedList<long, ArbitraryStyle>? FindCustomStack(LayerMap map, string name)
        {
            var customs = map.Customs;
            if (customs == null)
            {
                return null;
            }
            for (var i = 0; i < customs.Count; i++)
            {
                if (customs[i].Name == name)
                {
                    return customs[i].Stack;
                }
            }
            return null;
        }

        // Removes the layer at priority for property and re-applies the
        // next-highest surviving layer (or clears the inline style when none remain). This is what makes a
        // variant turning off fall back to a still-active variant or the base value instead of wiping the
        // property — and what keeps the two translate axes independent.
        // NB FilterCustom layers are name-keyed (LayerMap.Customs) and only clearable through the
        // ArbitraryStyle-aware overload below, which carries the name; the property dictionary this
        // overload operates on never holds them.
        public static void Clear(VisualElement element, ArbitraryProperty property, long priority = StyleLayerPriority.Base)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                ClearInline(element, property);
                return;
            }
            RemoveLayer(map, property, priority);
            ResolveAndApply(element, property, map);
            ReleaseUnusedShownImageWatch(element, map);
            Reproject(element, map);
        }

        // Drops the layer at priority for property without resolving the property again, for a caller that
        // applies a layer for the same property next: the inline value stays what it was until that layer
        // lands. Not for FilterCustom, whose layers are name-keyed (see Clear).
        internal static void Withdraw(VisualElement element, ArbitraryProperty property, long priority)
        {
            if (s_layers.TryGetValue(element, out var map))
            {
                RemoveLayer(map, property, priority);
                ReleaseUnusedShownImageWatch(element, map);
            }
        }

        private static void RemoveLayer(LayerMap map, ArbitraryProperty property, long priority)
        {
            if (map.TryGetValue(property, out var layers))
            {
                layers.Remove(priority);
                map.RemoveArrival(property, priority);
                if (layers.Count == 0) map.Remove(property);
            }
        }

        // Clears the layer an ArbitraryStyle applied, using the parsed value itself rather than just its
        // property. The only case this matters today is FilterCustom, whose value carries the NAME that
        // keys the layer stack to remove — and the name is ALL it reads (never the definition or the
        // arguments); every other property clears exactly like the (property, priority) overload.
        public static void Clear(VisualElement element, in ArbitraryStyle style, long priority = StyleLayerPriority.Base)
        {
            if (style.Property != ArbitraryProperty.FilterCustom)
            {
                Clear(element, style.Property, priority);
                return;
            }
            if (!s_layers.TryGetValue(element, out var map))
            {
                ClearInline(element, style.Property);
                return;
            }
            // The entry is intentionally KEPT when its stack empties (a tombstone holding the name's
            // compose slot): see LayerMap.Customs.
            FindCustomStack(map, style.Custom!.Name)?.Remove(priority);
            ResolveAndApply(element, ArbitraryProperty.FilterCustom, map);
            Reproject(element, map);
        }

        // Re-asserts the winning layer for EVERY property this element still has layers registered for.
        //
        // The release path of a per-frame driver (MotionSpringDriver / BezierTweenDriver) needs this because
        // ClearInline nulls whole shorthand fan-outs — a driven `padding` channel owns all four edges — while an
        // authored longhand (pt-[2px]) is registered against ONE of those edges. Nulling the four and stopping
        // would leave that edge on whatever the driver last wrote, permanently: the layer map still records the
        // authored value, so nothing else re-applies it, and the next unrelated variant release on that property
        // would resurrect it out of nowhere. Re-asserting the whole map instead of the driven properties alone is
        // what makes that correct regardless of which shorthand overlapped which longhand.
        //
        // Reads the map without mutating it, so a driver that never registered a layer leaves it untouched and
        // the recorded cascade stays exactly what the class diff put there. A slot another live driver writes
        // other than through ApplyDriven can be taken back for a frame here, and that driver's next tick re-asserts
        // it; one written through ApplyDriven is written back by the resolve itself (RewriteDriven).
        //
        // The filter family is exempt, for the same reason the class-diff reapply exempts it: re-resolving a
        // filter property runs the combined-filter path, whose tail hands the composed list to the filter
        // transition driver and restarts its tween — so a spring settling beside an in-flight blur would reset
        // that blur to a fresh full duration, and a settle during teardown would start a tick on an element on
        // its way to the pool. No per-frame driver here ever writes `filter` (StyleFilterTransitionDriver owns
        // it), so this family holds nothing of theirs to restore.
        internal static void ReapplyLayeredValues(VisualElement element)
        {
            if (element == null || !s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            foreach (var property in map.Keys)
            {
                if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
                {
                    continue;
                }
                ResolveAndApply(element, property, map);
            }
            // A hold on a slot no layer writes is re-asserted here too: a driver's release nulled it.
            ReassertHolds(element, map, AllSlots);
        }

        private const int AllSlots = -1;

        // The form of ReapplyLayeredValues above for the properties writing one of longhands, for a driver that
        // hands back some of its slots and keeps writing the rest, which a whole-map pass would take back for a
        // frame.
        internal static void ReapplyLayeredValues(VisualElement element, StyleLonghandSet longhands)
        {
            if (element == null || !s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            foreach (var property in map.Keys)
            {
                if (property != ArbitraryProperty.FilterCustom && !IsFilter(property)
                    && StyleArbitraryLonghands.Of(property).Overlaps(longhands))
                {
                    ResolveAndApply(element, property, map);
                }
            }
            ReassertHolds(element, map, AllSlots);
        }

        // A gap, grid or divide manipulator's own write to a slot it owns. Not a layer — HasLayer and the class
        // projection see none of it — but every layer resolve that writes a held slot writes the held value
        // back after it, so a layer that changes once the manipulator has written — a [&>*]: payload turned
        // off — cannot take the slot from it. Yield makes the exception.
        internal static void Hold(VisualElement element, HeldSlot slot, StyleLength value) => Hold(element, slot, value, 0f);

        // The form for a gap's margin, gap being the part of value the gap contributes rather than a space rule:
        // CSS spaces a flex or grid item by its gap and its own margin together, so a driver's margin adds to
        // that part where it replaces the rest.
        internal static void Hold(VisualElement element, HeldSlot slot, StyleLength value, float gap)
        {
            var map = MapOf(element);
            (map.Holds ??= new StyleHeldSlots()).Set(slot, value, gap);
            Reassert(element, map, slot);
        }

        internal static void Hold(VisualElement element, HeldSlot slot, StyleFloat value)
        {
            var map = MapOf(element);
            (map.Holds ??= new StyleHeldSlots()).Set(slot, value);
            Reassert(element, map, slot);
        }

        internal static void Hold(VisualElement element, HeldSlot slot, StyleColor value)
        {
            var map = MapOf(element);
            (map.Holds ??= new StyleHeldSlots()).Set(slot, value);
            Reassert(element, map, slot);
        }

        // A slot a driver writes stays the driver's under a hold just taken, so the driver's value goes back over
        // what the previous hold left there: the driven margin without the new gap.
        private static void Reassert(VisualElement element, LayerMap map, HeldSlot slot)
        {
            map.Holds!.Reassert(element.style, StyleHeldSlots.Bit(slot));
            if (map.Holds.IsDriven(slot))
            {
                RewriteDriven(element, map, StyleLonghandSet.Of(HeldSlotGroups.LonghandOf(slot)));
            }
            FitHeldWidth(element, map);
        }

        // A per-frame driver's write, which owns what it writes until ReleaseDriven: inline, so every hold and every
        // declaration of the element's own stands down for it and comes back on the release, but the important
        // ones — an important layer or class DrivenWrite does not exempt, an important divide's hold — keep what
        // they win, as !important outranks an animation in CSS.
        internal static void ApplyDriven(VisualElement element, in ArbitraryStyle style)
        {
            // Recorded from the first write, holds or none: a mount's from-pose is written before the container
            // that holds the slot first applies.
            var map = MapOf(element);
            var driven = map.Driven ??= new Dictionary<ArbitraryProperty, DrivenWrite>();
            if (!driven.TryGetValue(style.Property, out var write))
            {
                write = new DrivenWrite(StyleArbitraryLonghands.Of(style.Property));
                write.Capture(map);
                driven.Add(style.Property, write);
            }
            else if (write.Reversing)
            {
                write.Capture(map);
                write.Reversing = false;
            }
            write.Style = style;
            var slots = HeldSlotGroups.SlotsOf(style.Property);
            if (slots != 0)
            {
                (map.Holds ??= new StyleHeldSlots()).Drive(slots);
            }
            WriteDriven(element, map, write);
        }

        // Ends a driver's ownership of property and nulls what it wrote. The caller re-asserts the element's own
        // layers and holds over the property afterwards (ReapplyLayeredValues).
        internal static void ReleaseDriven(VisualElement element, ArbitraryProperty property)
        {
            var slots = HeldSlotGroups.SlotsOf(property);
            if (s_layers.TryGetValue(element, out var map))
            {
                map.Holds?.Release(slots);
                map.Driven?.Remove(property);
            }
            ClearInline(element, property);
        }

        // A reversal hand-off: the play now heads back to the pose it left, so that pose's important declarations
        // are its own. The reconciler writes the pose's inline tokens back only after the scheduler hands the play
        // over (RestoreInlineAfterExit follows CancelExit), so every write re-reads them until the play's own next
        // frame (ApplyDriven).
        internal static void Redrive(VisualElement element)
        {
            s_layers.TryGetValue(element, out var map);
            if (map?.Driven == null)
            {
                return;
            }
            foreach (var write in map.Driven.Values)
            {
                write.Reversing = true;
                WriteDriven(element, map, write);
            }
        }

        // Writes what a driver last wrote back over any of longhands a layer or a hold has just written.
        private static void RewriteDriven(VisualElement element, LayerMap map, StyleLonghandSet longhands)
        {
            if (map.Driven == null)
            {
                return;
            }
            foreach (var write in map.Driven.Values)
            {
                if (StyleArbitraryLonghands.Of(write.Style.Property).Overlaps(longhands))
                {
                    WriteDriven(element, map, write);
                }
            }
        }

        private static void WriteDriven(VisualElement element, LayerMap map, DrivenWrite write)
        {
            if (write.Reversing)
            {
                write.Capture(map);
            }
            ApplyInline(element, write.Style);
            YieldToImportant(element, map, write);
            WriteOverHolds(element, map, write);
        }

        // A driver's write, and the important layers and class claims of the element's own it found on its
        // longhands when its play first wrote, or at a reversal's hand-off (Redrive), which YieldToImportant does
        // not give way to: a play's own important pose is among them, since StyleAnimationScheduler adds the pose's
        // classes before the play's first write, and giving way to it would hold the element at the pose's end
        // value for the whole play.
        private sealed class DrivenWrite
        {
            public ArbitraryStyle Style;

            // Set by Redrive until the play's next frame.
            public bool Reversing;

            private readonly StyleLonghandSet _longhands;
            private readonly List<(ArbitraryProperty Property, long Key)> _layers = new();
            private readonly Dictionary<StyleLonghand, long> _claims = new();

            public DrivenWrite(StyleLonghandSet longhands) => _longhands = longhands;

            public void Capture(LayerMap map)
            {
                var longhands = _longhands;
                _layers.Clear();
                _claims.Clear();
                foreach (var pair in map)
                {
                    if (!StyleArbitraryLonghands.Of(pair.Key).Overlaps(longhands))
                    {
                        continue;
                    }
                    foreach (var key in pair.Value.Keys)
                    {
                        if ((key & StyleLayerPriority.Important) != 0)
                        {
                            _layers.Add((pair.Key, key));
                        }
                    }
                }
                if (map.Projection == null)
                {
                    return;
                }
                foreach (var longhand in s_longhands)
                {
                    var claim = map.Projection.ClaimOf(longhand);
                    if (longhands.Contains(longhand) && (claim & StyleLayerPriority.Important) != 0)
                    {
                        _claims[longhand] = claim;
                    }
                }
            }

            public bool Outranks(ArbitraryProperty property, long key)
                => (key & StyleLayerPriority.Important) != 0 && !_layers.Contains((property, key));

            public bool Outranks(StyleLonghand longhand, long claim)
            {
                var known = _claims.TryGetValue(longhand, out var found);
                return (claim & StyleLayerPriority.Important) != 0 && (!known || claim > found);
            }
        }

        // Reused by every YieldToImportant: it resolves nothing re-entrantly.
        private static readonly List<(long Key, ArbitraryStyle Style)> s_importantWinners = new();

        // Writes back, over a driver's write, what an important declaration of the element's own outranking it wins
        // there (DrivenWrite.Outranks): an important layer's value, or the class an important claim names, by
        // clearing the inline value over it. Between the two the higher key wins, as ResolveSharedLonghands orders
        // them.
        private static void YieldToImportant(VisualElement element, LayerMap map, DrivenWrite write)
        {
            var longhands = StyleArbitraryLonghands.Of(write.Style.Property);
            s_importantWinners.Clear();
            foreach (var pair in map)
            {
                if (pair.Key == ArbitraryProperty.FilterCustom || IsFilter(pair.Key)
                    || !StyleArbitraryLonghands.Of(pair.Key).Overlaps(longhands))
                {
                    continue;
                }
                if (!TryWinningLayer(map, pair.Key, out var style))
                {
                    continue;
                }
                var key = pair.Value.Keys[pair.Value.Count - 1];
                if (write.Outranks(pair.Key, key))
                {
                    s_importantWinners.Add((key, style));
                }
            }
            s_importantWinners.Sort((a, z) => a.Key.CompareTo(z.Key));
            foreach (var (_, style) in s_importantWinners)
            {
                ApplyInline(element, style);
            }
            if (map.Projection == null)
            {
                return;
            }
            foreach (var longhand in s_longhands)
            {
                var claim = map.Projection.ClaimOf(longhand);
                if (!longhands.Contains(longhand) || !write.Outranks(longhand, claim)
                    || OutrankedOn(longhand, claim, s_importantWinners))
                {
                    continue;
                }
                if (s_soleWriters.TryGetValue(longhand, out var sole))
                {
                    ClearInline(element, sole);
                }
            }
        }

        // What the element shows on slot under write before a gap adds to it: the value of an important
        // declaration of the element's own outranking write (DrivenWrite.Outranks), the higher keyed of a layer
        // and a class claim, else the driven value. NaN where it cannot be measured.
        private static float OwnPixels(VisualElement element, LayerMap map, HeldSlot slot, DrivenWrite write)
        {
            var longhand = HeldSlotGroups.LonghandOf(slot);
            var found = TryLayeredWinner(map, slot, out var winner, out var rank);
            var layered = found && write.Outranks(winner.Property, rank);
            var projection = map.Projection;
            var claim = projection?.ClaimOf(longhand) ?? long.MinValue;
            if (projection != null && write.Outranks(longhand, claim) && (!layered || claim > rank))
            {
                return ClaimedPixels(element, projection, longhand);
            }
            return Pixels(element, layered ? winner : write.Style);
        }

        // The pixels the class claiming longhand writes, read as its inline form (TryReadStylesheetClass), so a
        // spacing step takes Velvet's scale as the other rewrites of a stylesheet class do.
        private static float ClaimedPixels(VisualElement element, StyleClassProjection.Model projection,
            StyleLonghand longhand)
        {
            var claimant = projection.ClaimantOf(longhand);
            if (claimant == null)
            {
                return float.NaN;
            }
            if (!TryReadStylesheetClass(StripImportant(claimant, out _), out _, out var inlineForm))
            {
                return float.NaN;
            }
            return TryParse(inlineForm, out var style) ? Pixels(element, style) : float.NaN;
        }

        // Puts back the important holds a driver's write covered, adds a gap to the value a driven margin shows
        // (Hold's gap form), and fits a held width around it (FitHeldWidth).
        private static void WriteOverHolds(VisualElement element, LayerMap map, DrivenWrite write)
        {
            var holds = map.Holds;
            if (holds == null)
            {
                return;
            }
            var slots = HeldSlotGroups.SlotsOf(write.Style.Property);
            ReassertHolds(element, map, slots);
            foreach (var slot in HeldSlotGroups.EverySlot)
            {
                var gap = (slots & StyleHeldSlots.Bit(slot)) != 0 ? holds.DrivenGap(slot) : 0f;
                var own = gap != 0f ? OwnPixels(element, map, slot, write) : float.NaN;
                if (!float.IsNaN(own))
                {
                    WriteLayered(element, slot, new ArbitraryStyle(write.Style.Property, own + gap, LengthUnit.Pixel));
                }
            }
            FitHeldWidth(element, map);
        }

        private static readonly HeldSlot[] s_rowMargins = { HeldSlot.MarginLeft, HeldSlot.MarginRight };

        // A held width is a grid column's, which already gives up the part of the child's held margins a space
        // rule writes (StyleHeldSlots.OwnPart): a driven margin replaces that part, so the width gives up what the
        // driven margin shows there instead and the child moves inside its column rather than wrapping out of it.
        private static void FitHeldWidth(VisualElement element, LayerMap map)
        {
            var holds = map.Holds;
            if (holds == null || map.Driven == null || !holds.IsHeld(HeldSlot.Width) || holds.IsDriven(HeldSlot.Width))
            {
                return;
            }
            var width = holds.Length(HeldSlot.Width);
            var fitted = false;
            foreach (var slot in s_rowMargins)
            {
                var write = holds.IsHeld(slot) ? DrivenWriteOn(map, slot) : null;
                var own = write != null ? OwnPixels(element, map, slot, write) : float.NaN;
                if (!float.IsNaN(own))
                {
                    width -= own - holds.OwnPart(slot);
                    fitted = true;
                }
            }
            if (fitted)
            {
                element.style.width = width;
            }
        }

        private static DrivenWrite? DrivenWriteOn(LayerMap map, HeldSlot slot)
        {
            foreach (var write in map.Driven!.Values)
            {
                if ((HeldSlotGroups.SlotsOf(write.Style.Property) & StyleHeldSlots.Bit(slot)) != 0)
                {
                    return write;
                }
            }
            return null;
        }

        // A percentage is taken of the parent's content width, which CompositeWidgetSpacingDirectionPanelTests
        // pins against the engine's own reading of a margin percentage. NaN until the parent is laid out, and for a
        // keyword or an expression, which name no number here.
        private static float Pixels(VisualElement element, in ArbitraryStyle length)
        {
            if (length.Auto || length.Expression != null)
            {
                return float.NaN;
            }
            if (length.Unit == LengthUnit.Pixel)
            {
                return length.Value;
            }
            var parent = element.hierarchy.parent;
            return parent == null ? float.NaN : length.Value / 100f * parent.contentRect.width;
        }

        // Makes the hold just taken on slot give way to a layer of the element's own: while one writes the slot,
        // that layer's value stands, and the held one returns once the last such layer goes. That is how a
        // zero-specificity Tailwind write (space, divide) gives way to the element's own arbitrary value for as
        // long as it has one. With importantOnly it gives way to an important layer alone, which is how an
        // important divide still yields to a child's own important declaration. The next Hold on the slot takes
        // the yield back.
        internal static void Yield(VisualElement element, HeldSlot slot, bool importantOnly = false)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            map.Holds?.SetYield(slot, importantOnly);
            ReassertHolds(element, map, StyleHeldSlots.Bit(slot));
        }

        // Writes every held slot among slots back onto the element, except a yielding one a layer of the
        // element's own writes, which takes that layer's value instead.
        private static void ReassertHolds(VisualElement element, LayerMap map, int slots)
        {
            var holds = map.Holds;
            if (holds == null)
            {
                return;
            }
            var yielded = 0;
            foreach (var slot in HeldSlotGroups.EverySlot)
            {
                if (!holds.Yields(slot))
                {
                    continue;
                }
                if (!TryLayeredWinner(map, slot, out var winner, out var rank)
                    || (holds.YieldsOnlyToImportant(slot) && (rank & StyleLayerPriority.Important) == 0))
                {
                    continue;
                }
                WriteLayered(element, slot, winner);
                yielded |= StyleHeldSlots.Bit(slot);
            }
            holds.Reassert(element.style, slots & ~yielded);
            FitHeldWidth(element, map);
        }

        private static LayerMap MapOf(VisualElement element) => s_layers.GetValue(element, static _ => new LayerMap());

        // Gives a slot back to the cascade: drops any hold on it and writes what the element's own layers
        // resolve to there, or nothing when none does. Gap, grid and divide stop owning a slot through here
        // rather than by writing Null, which would erase a layer's value along with their own. Writes the slot
        // alone rather than re-resolving the winner's property.
        internal static void HandBack(VisualElement element, HeldSlot slot)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                StyleHeldSlots.WriteNull(element.style, slot);
                return;
            }
            map.Holds?.Drop(slot);
            // A driver writing the slot keeps it; its release re-asserts what is left.
            if (map.Holds != null && map.Holds.IsDriven(slot))
            {
                RewriteDriven(element, map, StyleLonghandSet.Of(HeldSlotGroups.LonghandOf(slot)));
                return;
            }
            if (TryLayeredWinner(map, slot, out var winner))
            {
                WriteLayered(element, slot, winner);
            }
            else
            {
                StyleHeldSlots.WriteNull(element.style, slot);
            }
        }

        // Every held length slot is a margin edge or the width, each of which takes a percentage of the parent's
        // width and stands in for an indefinite one as PropertySetters' setter for it does. A length that cannot
        // be measured yet leaves the slot as it is, as ApplyInline does.
        private static void WriteLayered(VisualElement element, HeldSlot slot, in ArbitraryStyle winner)
        {
            var length = winner.ToStyleLength();
            if (winner.Expression == null || StyleRelativeLengths.TryResolve(element, winner.Expression,
                    RelativeLengthAxis.Width, slot == HeldSlot.Width ? StyleKeyword.Auto : null, out length))
            {
                StyleHeldSlots.WriteLayered(element.style, slot, length, winner);
            }
        }

        // Hands slot back when a gap, grid or divide manipulator holds it on element, and leaves it alone
        // otherwise: an inline value nothing here wrote is not this layer's to clear.
        internal static void HandBackIfHeld(VisualElement element, HeldSlot slot)
        {
            s_layers.TryGetValue(element, out var map);
            var holds = map?.Holds;
            if (holds != null && holds.IsHeld(slot))
            {
                HandBack(element, slot);
            }
        }

        // Whether the highest priority an ungated class of the element's own claims on slot's longhand lies in
        // the important band. Inline layers are not consulted.
        internal static bool DeclaresImportantOwn(VisualElement element, HeldSlot slot)
            => TryGetProjection(element) is { } model
                && (model.ClaimOf(HeldSlotGroups.LonghandOf(slot)) & StyleLayerPriority.Important) != 0;

        // Whether a utility on element's class list sets slot through an ungated bundled USS rule. Tailwind
        // writes space and divide at zero specificity, so such a class of the element's own wins over them
        // there; an arbitrary value's layer does the same through a yielding Hold.
        internal static bool DeclaresOwn(VisualElement element, HeldSlot slot)
        {
            var longhand = HeldSlotGroups.LonghandOf(slot);
            foreach (var cls in element.GetClasses())
            {
                if (!StyleUtilityProperties.TryGet(cls, out var rule))
                {
                    continue;
                }
                if (rule.Gate == StyleUtilityGate.None && rule.Properties.Contains(longhand))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void Watch(VisualElement element, IChildClassWatcher watcher)
        {
            var watchers = s_layers.GetValue(element, static _ => new LayerMap()).Watchers ??= new List<IChildClassWatcher>();
            if (!watchers.Contains(watcher))
            {
                watchers.Add(watcher);
            }
        }

        internal static void Unwatch(VisualElement element, IChildClassWatcher watcher)
        {
            s_layers.TryGetValue(element, out var map);
            map?.Watchers?.Remove(watcher);
        }

        internal static float? UniformScaleClassFallback(VisualElement element)
        {
            return s_layers.TryGetValue(element, out var map)
                // MUTANT_SURVIVES(equivalent, clause removed): removing the outer axis-presence clause leaves writes unchanged: the three current callers add no axis before refresh, which rejects that capture.
                && (map.ContainsKey(ArbitraryProperty.ScaleX) || map.ContainsKey(ArbitraryProperty.ScaleY))
                ? UniformScaleFromClasses(element)
                : null;
        }

        // Re-applies every container watching element after its class list changed. A copy is walked, because a
        // re-apply can claim or release the element and so edit the list.
        internal static void NotifyClassesChanged(VisualElement element, float? previousUniform = null)
        {
            s_layers.TryGetValue(element, out var map);
            if (map != null)
            {
                RefreshScaleClassFallback(element, map, previousUniform);
            }
            var watchers = map?.Watchers;
            if (watchers == null)
            {
                return;
            }
            foreach (var watcher in watchers.ToArray())
            {
                watcher.Reapply();
            }
        }

        // Hands back every slot held on element. For an element the reconciler removes: its claim is dropped
        // with the rest of its side tables, so the container that holds a slot on it cannot release it later.
        internal static void HandBackAll(VisualElement element)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            var holds = map.Holds;
            if (holds == null)
            {
                return;
            }
            foreach (var slot in HeldSlotGroups.EverySlot)
            {
                if (holds.IsHeld(slot))
                {
                    HandBack(element, slot);
                }
            }
        }

        // What the element's own layers resolve slot to, ignoring any hold on it: the value HandBack would
        // leave there. Null when no layer writes it.
        internal static ArbitraryStyle? ResolveLayered(VisualElement element, HeldSlot slot)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return null;
            }
            return TryLayeredWinner(map, slot, out var winner) ? winner : null;
        }

        private static bool TryLayeredWinner(LayerMap map, HeldSlot slot, out ArbitraryStyle winner)
            => TryLayeredWinner(map, slot, out winner, out _);

        private static bool TryLayeredWinner(LayerMap map, HeldSlot slot, out ArbitraryStyle winner, out long rank)
        {
            winner = default;
            rank = long.MinValue;
            var best = long.MinValue;
            var latest = long.MinValue;
            var found = false;
            foreach (var writer in HeldSlotGroups.WritersOf(slot))
            {
                if (!TryWinningLayer(map, writer, out var style))
                {
                    continue;
                }
                var layers = map[writer];
                var priority = layers.Keys[layers.Count - 1];
                var arrival = map.ArrivalOf(writer, priority);
                // MUTANT_SURVIVES(equivalent, boundary): live arrivals are positive and unique, including after compaction; distinct writers cannot tie each other or the initial MinValue sentinel.
                var isNewerArrival = arrival > latest;
                var byCascade = found ? StyleSpacingPrecedence.CompareCascade(writer, winner.Property) : 0;
                if (priority > best || priority == best && (byCascade > 0 || byCascade == 0 && isNewerArrival))
                {
                    winner = style;
                    rank = priority;
                    best = priority;
                    latest = arrival;
                    found = true;
                }
            }
            return found;
        }

        // Whether any layer is registered for property. Uncontaminated for a caller asking about a slot it
        // writes directly rather than through Apply — no manipulator registers a layer.
        internal static bool HasLayer(VisualElement element, ArbitraryProperty property)
            => element != null
                && s_layers.TryGetValue(element, out var map)
                && map.TryGetValue(property, out var layers)
                && layers.Count > 0;

        // The element's own writers of the background image: a StyleOverrides member and the utilities
        // (bg-[addr:…] and the gradients, which coordinate between themselves). Neither is an ArbitraryProperty
        // layer, since a StyleBackground is no ArbitraryStyle payload, so they are ranked here instead, as
        // InlineStyle ranks the layers: the override wins unless the utility is important.
        internal sealed class BackgroundImageSources
        {
            public StyleBackground? Override;
            public StyleBackground? Utility;
            public bool UtilityImportant;
            // What ResolveBackgroundImage last wrote, to tell an inline image a refCallback wrote since.
            public StyleBackground? Written;
        }

        // Whether the inline background image is still the one these writers last put there.
        internal static bool ShowsOwnBackground(VisualElement element)
            => s_layers.TryGetValue(element, out var map) && map.BackgroundImage?.Written is { } written
                && element.style.backgroundImage.Equals(written);

        // Whether a StyleOverrides.BackgroundImage is the image showing, over the utilities'.
        internal static bool OverrideBackgroundWins(VisualElement element)
            => s_layers.TryGetValue(element, out var map) && OverrideWins(map.BackgroundImage);

        private static bool OverrideWins(BackgroundImageSources? sources)
            => sources is { Override: not null, UtilityImportant: false };

        // A StyleOverrides.BackgroundImage, or null when the node declares none.
        internal static void WriteBackgroundImageOverride(VisualElement element, StyleBackground? value)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            var sources = map.BackgroundImage ??= new BackgroundImageSources();
            sources.Override = value is { keyword: not StyleKeyword.Null } ? value : null;
            ResolveBackgroundImage(element, map, sources);
        }

        // A utility's background image, or null when the utilities let go of it.
        internal static void WriteBackgroundImageUtility(VisualElement element, StyleBackground? value, bool important)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            var sources = map.BackgroundImage ??= new BackgroundImageSources();
            sources.Utility = value;
            sources.UtilityImportant = important;
            ResolveBackgroundImage(element, map, sources);
        }

        internal static void ClearBackgroundImageUtility(VisualElement element)
            => WriteBackgroundImageUtility(element, null, important: default);

        // The image the utilities last wrote, whether or not it is the one showing.
        internal static StyleBackground UtilityBackgroundImage(VisualElement element)
            => s_layers.TryGetValue(element, out var map) && map.BackgroundImage?.Utility is { } utility
                ? utility
                : new StyleBackground(StyleKeyword.Null);

        private static void ResolveBackgroundImage(VisualElement element, LayerMap map, BackgroundImageSources sources)
        {
            var winner = sources.Override is { } own && !sources.UtilityImportant
                ? own
                : sources.Utility ?? new StyleBackground(StyleKeyword.Null);
            SceneViewElement.WriteBackground(element, winner);
            sources.Written = winner;
            // Resolved now rather than after the next style pass: NineSlicePercentPanelTests pins that an element
            // the panel has styled lets go of a withdrawn inline image in its resolved style at once.
            ReresolvePercentSlices(element, map);
        }

        internal sealed class ShownImageWatch
        {
            public Background Seen;
        }

        // Static, so registering one again is a no-op; each looks the element's current layer map up rather than
        // holding one.
        private static readonly EventCallback<GeometryChangedEvent> s_recheckOnGeometry =
            evt => RecheckShownImage((VisualElement)evt.currentTarget);

        private static readonly EventCallback<CustomStyleResolvedEvent> s_recheckOnStyle =
            evt => RecheckShownImage((VisualElement)evt.currentTarget);

        internal const string ShownImageMarkerClass = "velvet-slice-percent";

        // An image Velvet does not write — a stylesheet's, or one a refCallback set — reaches the resolved style
        // or the inline slot without passing through ResolveBackgroundImage, so a percentage inset is checked
        // again whenever the element is restyled or its geometry changes. The restyle arrives only through the
        // marker class's custom property in _backgrounds.uss, the way LeadingLengthProbe's does.
        private static void WatchShownImage(VisualElement element)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            if (map.ImageWatch is { } watching)
            {
                watching.Seen = ShownImage(element);
                return;
            }
            map.ImageWatch = new ShownImageWatch { Seen = ShownImage(element) };
            element.AddToClassList(ShownImageMarkerClass);
            element.RegisterCallback(s_recheckOnGeometry);
            element.RegisterCallback(s_recheckOnStyle);
        }

        // The watch and its marker go with the last percentage inset layer, wherever it sat in the stack.
        private static void ReleaseUnusedShownImageWatch(VisualElement element, LayerMap map)
        {
            if (map.ImageWatch == null || Array.Exists(s_sliceInsetProperties,
                    property => map.TryGetValue(property, out var layers)
                        && System.Linq.Enumerable.Any(layers.Values, layer => layer.PercentEdges != 0)))
            {
                return;
            }
            map.ImageWatch = null;
            ReleaseShownImageWatch(element);
        }

        // Also the cleaner's, beside ClearAll, which drops the watch with the rest of the layer map.
        internal static void ReleaseShownImageWatch(VisualElement element)
        {
            element.RemoveFromClassList(ShownImageMarkerClass);
            // MUTANT_SURVIVES(equivalent, line removed): a callback left registered finds no watch and returns.
            element.UnregisterCallback(s_recheckOnGeometry);
            // MUTANT_SURVIVES(equivalent, line removed): the style callback returns the same way.
            element.UnregisterCallback(s_recheckOnStyle);
        }

        private static void RecheckShownImage(VisualElement element)
        {
            if (!s_layers.TryGetValue(element, out var map) || map.ImageWatch is not { } watch
                || ShownImage(element).Equals(watch.Seen))
            {
                return;
            }
            ReresolvePercentSlices(element, map);
        }

        // A percentage inset is of the background image's size, so the slice properties are written again once
        // the image changes; one holding no percentage writes what it wrote before.
        private static void ReresolvePercentSlices(VisualElement element, LayerMap map)
        {
            foreach (var property in s_sliceInsetProperties)
            {
                if (map.ContainsKey(property))
                {
                    ResolveAndApply(element, property, map);
                }
            }
        }

        // The single-value slice properties whose insets are of the image's width.
        private static readonly HashSet<ArbitraryProperty> s_horizontalSlices = new()
        {
            ArbitraryProperty.SliceX, ArbitraryProperty.SliceLeft, ArbitraryProperty.SliceRight,
        };

        private static readonly ArbitraryProperty[] s_sliceInsetProperties =
        {
            ArbitraryProperty.Slice, ArbitraryProperty.SliceX, ArbitraryProperty.SliceY, ArbitraryProperty.SliceTop,
            ArbitraryProperty.SliceRight, ArbitraryProperty.SliceBottom, ArbitraryProperty.SliceLeft,
        };

        // The background image's size, which a percentage inset is of: the inline image, else the resolved one.
        // Zero when there is none.
        private static Vector2 BackgroundImagePixels(VisualElement element)
        {
            var image = ShownImage(element);
            if (image.texture != null)
            {
                return new Vector2(image.texture.width, image.texture.height);
            }
            if (image.sprite != null)
            {
                return image.sprite.rect.size;
            }
            if (image.vectorImage != null)
            {
                return new Vector2(image.vectorImage.width, image.vectorImage.height);
            }
            return image.renderTexture != null
                ? new Vector2(image.renderTexture.width, image.renderTexture.height)
                : Vector2.zero;
        }

        private static Background ShownImage(VisualElement element)
            => element.style.backgroundImage.keyword == StyleKeyword.Undefined
                ? element.style.backgroundImage.value
                : element.resolvedStyle.backgroundImage;

        // One inset as written: the keyword of a keyword value, else the value, resolved against the image's
        // width (horizontal) or height when it is a percentage. A percentage past 100 is read as 100, as
        // border-image-slice reads an inset larger than the image.
        private static StyleInt SliceInset(VisualElement element, in ArbitraryStyle style, float value, int edge,
            bool horizontal)
        {
            if (style.Keyword != StyleKeyword.Undefined)
            {
                return new StyleInt(style.Keyword);
            }
            if ((style.PercentEdges & (1 << edge)) == 0)
            {
                return new StyleInt((int)value);
            }
            WatchShownImage(element);
            var pixels = BackgroundImagePixels(element);
            return new StyleInt(Mathf.RoundToInt(Mathf.Min(value, 100f) / 100f * (horizontal ? pixels.x : pixels.y)));
        }

        // Through the pan loop's gate, which holds the slot while a pan runs.
        private static void WriteRepeat(VisualElement element, in ArbitraryStyle style)
            => StyleAnimateDriver.WriteBackgroundRepeat(element, style.Keyword == StyleKeyword.Undefined
                ? new StyleBackgroundRepeat(new BackgroundRepeat((Repeat)style.Value, (Repeat)style.Value2))
                : new StyleBackgroundRepeat(style.Keyword));

        private static void WriteSliceType(VisualElement element, in ArbitraryStyle style)
            => element.style.unitySliceType = style.Keyword == StyleKeyword.Undefined
                ? new StyleEnum<SliceType>((SliceType)style.Value)
                : new StyleEnum<SliceType>(style.Keyword);

        // Drops all arbitrary-value layers tracked for element. Called when the element is
        // cleaned up / returned to a pool so a later reuse does not inherit a prior consumer's layers.
        public static void ClearAll(VisualElement element)
        {
            if (element != null && s_layers.TryGetValue(element, out var map))
            {
                map.RelativePoll?.Stop();
                RemoveCustomFilterHosts(element, map);
            }
            if (element != null) s_layers.Remove(element);
            // What the fit records the radius layers as declaring goes with them.
            if (element != null) CornerRadiusFit.Release(element);
        }

        // Resolves an inline-value class token to inline style — arbitrary value first, then background
        // image. When neither resolver claims it, the token is added to the USS class list unless
        // addToClassListFallback is false (the reapply path passes false: an inline-classified token that
        // no resolver owns — e.g. font-[..], owned by StyleFontResolver — must never enter the class list).
        // The caller must have confirmed IsInlineResolved(core) and stripped the important bang; priority
        // is the layer the bang selects (Important when present, Base otherwise). Mirror of ClearClassToken.
        public static void ApplyClassToken(VisualElement element, string core, long priority, bool addToClassListFallback = true)
        {
            if (TryParseLayer(core, out var style))
            {
                Apply(element, in style, priority);
            }
            else if (StyleBackgroundImageResolver.TryParse(core, out var texture))
            {
                StyleBackgroundImageResolver.Apply(element, texture, (priority & StyleLayerPriority.Important) != 0);
            }
            else if (addToClassListFallback)
            {
                element.AddToClassList(core);
            }
        }

        // Clears the inline style an inline-value class token applied (see ApplyClassToken), falling back
        // to removing it from the USS class list when neither resolver claims it.
        public static void ClearClassToken(VisualElement element, string core, long priority)
        {
            if (TryParseLayer(core, out var style))
            {
                Clear(element, in style, priority);
            }
            else if (StyleBackgroundImageResolver.TryParse(core, out _))
            {
                StyleBackgroundImageResolver.Clear(element);
            }
            else
            {
                element.RemoveFromClassList(core);
            }
        }

        // True for tokens whose resolved property lands in the composed filter family, named presets included.
        // Purely syntactic, so the class-diff reapply's skip decision does not pay the parse it is avoiding.
        internal static bool IsFilterFamilyToken(string core) => StyleFilterValueParser.IsFilterLeaf(core);

        // Applies the winning layer for a property. Translate and scale are special: their axis layers share
        // one inline `translate` / `scale`, so their winners must be composed before that write.
        private static void ResolveAndApply(VisualElement element, ArbitraryProperty property, LayerMap map)
        {
            if (property == ArbitraryProperty.TranslateX || property == ArbitraryProperty.TranslateY)
            {
                ApplyCombinedTranslate(element, map);
                return;
            }
            if (property == ArbitraryProperty.Scale
                || property == ArbitraryProperty.ScaleX || property == ArbitraryProperty.ScaleY)
            {
                ApplyCombinedScale(element, map);
                return;
            }
            if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
            {
                ApplyCombinedFilter(element, map);
                return;
            }
            if (property == ArbitraryProperty.TransitionDuration)
            {
                ApplyTransitionDuration(element, map);
                return;
            }
            if (!TryWinningLayer(map, property, out var winner))
            {
                ClearInline(element, property);
            }
            else if (winner.Expression != null)
            {
                map.RelativePoll ??= new RelativeLengthPoll(element, ReresolveRelativeLengths);
            }
            var rewritten = ResolveSharedLonghands(element, property, map, out var written);
            RewriteDriven(element, map, written);
            ReassertHolds(element, map, HeldSlotGroups.SlotsOf(property) | rewritten);
        }

        // Reused by every tick: a tick runs alone and resolves nothing re-entrantly.
        private static readonly List<ArbitraryProperty> s_relativeWinners = new();

        // The poll's tick: re-resolves every property a relative length wins once what it measures has moved, and
        // stops the poll once none wins.
        private static void ReresolveRelativeLengths(VisualElement element)
        {
            // MUTANT_SURVIVES(unreachable): a ticking poll always finds its element's map holding a poll.
            // Every poll is stopped where its map lets go of it: by ClearAll, and below once no relative length wins.
            if (!s_layers.TryGetValue(element, out var map) || map.RelativePoll == null)
            {
                return;
            }
            s_relativeWinners.Clear();
            foreach (var property in map.Keys)
            {
                if (TryWinningLayer(map, property, out var style) && style.Expression != null)
                {
                    s_relativeWinners.Add(property);
                }
            }
            if (s_relativeWinners.Count == 0)
            {
                map.RelativePoll.Stop();
                map.RelativePoll = null;
                return;
            }
            if (!map.RelativePoll.Moved())
            {
                return;
            }
            var hold = new StyleRelativeLengths.TransitionHold(element);
            try
            {
                foreach (var property in s_relativeWinners)
                {
                    ResolveAndApply(element, property, map);
                }
            }
            finally
            {
                hold.Release();
            }
        }

        // Settles every longhand property shares with another property's layers (m-[4px] beside mt-[8px] and
        // mx-[8px]): each ends on its highest-keyed writer, or on the class claiming it above that writer.
        // Returns the held slots written, which the caller reasserts with its own, and every longhand written.
        private static int ResolveSharedLonghands(VisualElement element, ArbitraryProperty property, LayerMap map,
            out StyleLonghandSet written)
        {
            written = StyleArbitraryLonghands.Of(property);
            var writers = ConnectedWriters(property, map);
            if (writers == null)
            {
                // Nothing else writes what this property writes, the common case: only its own winner is left.
                if (!TryWinningLayer(map, property, out var own))
                {
                    return 0;
                }
                ApplyInline(element, own);
                ClearClaimedLonghands(element, map, own.Property, map[property].Keys[map[property].Count - 1]);
                return 0;
            }

            writers.Sort((a, z) =>
            {
                var byKey = a.Key.CompareTo(z.Key);
                if (byKey != 0)
                {
                    return byKey;
                }
                var byCascade = StyleSpacingPrecedence.CompareCascade(a.Style.Property, z.Style.Property);
                return byCascade != 0 ? byCascade : map.ArrivalOf(a.Style.Property, a.Key)
                    .CompareTo(map.ArrivalOf(z.Style.Property, z.Key));
            });
            var slots = 0;
            foreach (var (_, style) in writers)
            {
                ApplyInline(element, style);
                slots |= HeldSlotGroups.SlotsOf(style.Property);
                written = written.Union(StyleArbitraryLonghands.Of(style.Property));
            }
            foreach (var (key, style) in writers)
            {
                ClearClaimedLonghands(element, map, style.Property, key, writers);
            }
            return slots;
        }

        // The winning layer of every property connected to property through a shared longhand, however many
        // steps away — a writer re-written can reach a longhand property never touches — with its key; null
        // when nothing else shares one.
        private static readonly HashSet<ArbitraryProperty> s_remainingWriters = new();

        private static List<(long Key, ArbitraryStyle Style)>? ConnectedWriters(ArbitraryProperty property,
            LayerMap map)
        {
            var reached = StyleArbitraryLonghands.Of(property);
            // One set reused across calls, filled through the key collection's own enumerator: a property nothing
            // else shares a longhand with — a StyleOverrides colour beside unrelated layers — allocates nothing.
            var remaining = s_remainingWriters;
            // MUTANT_SURVIVES(equivalent): a member left by an earlier call is either a key of this map, added again
            // here, or sits unremoved through every pass, and a pass compares the count only against its own start.
            remaining.Clear();
            foreach (var key in map.Keys)
            {
                remaining.Add(key);
            }
            remaining.Remove(property);
            HashSet<ArbitraryProperty>? taken = null;
            // MUTANT_SURVIVES(equivalent, boundary): each productive pass consumes a candidate;
            // an extra pass cannot add a writer after the map-sized closure has finished.
            for (var pass = 0; pass < map.Count; pass++)
            {
                var previousCount = remaining.Count;
                foreach (var pair in map)
                {
                    var longhands = StyleArbitraryLonghands.Of(pair.Key);
                    if (!longhands.Overlaps(reached) || !remaining.Remove(pair.Key))
                    {
                        continue;
                    }
                    (taken ??= new HashSet<ArbitraryProperty>()).Add(pair.Key);
                    reached = reached.Union(longhands);
                }
                if (remaining.Count == previousCount)
                {
                    break;
                }
            }
            if (taken == null)
            {
                return null;
            }
            taken.Add(property);
            var writers = new List<(long Key, ArbitraryStyle Style)>();
            foreach (var connected in taken)
            {
                if (TryWinningLayer(map, connected, out var style))
                {
                    writers.Add((map[connected].Keys[map[connected].Count - 1], style));
                }
            }
            return writers;
        }

        // A class the projection could not floor a writer under, because it claims only some of what the writer
        // writes (md:pt-6 beside p-[12px]), still outranks that writer on the longhands it claims. Each such
        // longhand the writer ended up owning is cleared, so the class shows through.
        private static void ClearClaimedLonghands(VisualElement element, LayerMap map, ArbitraryProperty writer,
            long key, List<(long Key, ArbitraryStyle Style)>? others = null)
        {
            if (map.Projection == null)
            {
                return;
            }
            var written = StyleArbitraryLonghands.Of(writer);
            foreach (var longhand in s_longhands)
            {
                if (written.Contains(longhand) && map.Projection.ClaimOf(longhand) > key
                    && !OutrankedOn(longhand, key, others) && s_soleWriters.TryGetValue(longhand, out var sole))
                {
                    ClearInline(element, sole);
                }
            }
        }

        // Whether another writer holds longhand above key, in which case the longhand is that one's to answer for.
        private static bool OutrankedOn(StyleLonghand longhand, long key, List<(long Key, ArbitraryStyle Style)>? others)
        {
            if (others == null)
            {
                return false;
            }
            foreach (var (otherKey, style) in others)
            {
                if (otherKey > key && StyleArbitraryLonghands.Of(style.Property).Contains(longhand))
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly StyleLonghand[] s_longhands = (StyleLonghand[])Enum.GetValues(typeof(StyleLonghand));

        // The property writing a longhand and nothing else, where one does: clearing that longhand alone goes
        // through it.
        private static readonly Dictionary<StyleLonghand, ArbitraryProperty> s_soleWriters = BuildSoleWriters();

        private static Dictionary<StyleLonghand, ArbitraryProperty> BuildSoleWriters()
        {
            var sole = new Dictionary<StyleLonghand, ArbitraryProperty>();
            foreach (ArbitraryProperty property in Enum.GetValues(typeof(ArbitraryProperty)))
            {
                var longhands = StyleArbitraryLonghands.Of(property);
                foreach (var longhand in s_longhands)
                {
                    if (longhands == StyleLonghandSet.Of(longhand) && !sole.ContainsKey(longhand))
                    {
                        sole.Add(longhand, property);
                    }
                }
            }
            return sole;
        }

        private static void ApplyCombinedTranslate(VisualElement element, LayerMap map)
        {
            var hasX = TryWinningLayer(map, ArbitraryProperty.TranslateX, out var xw);
            var hasY = TryWinningLayer(map, ArbitraryProperty.TranslateY, out var yw);
            if (!hasX && !hasY)
            {
                element.style.translate = StyleKeyword.Null;
                return;
            }
            var x = hasX ? new Length(xw.Value, xw.Unit) : new Length(0f);
            var y = hasY ? new Length(yw.Value, yw.Unit) : new Length(0f);
            element.style.translate = new Translate(x, y);
        }

        private static void ApplyCombinedScale(VisualElement element, LayerMap map)
        {
            var hasX = TryWinningLayer(map, ArbitraryProperty.ScaleX, out var xw);
            var hasY = TryWinningLayer(map, ArbitraryProperty.ScaleY, out var yw);
            var hasUniform = TryWinningLayer(map, ArbitraryProperty.Scale, out var uw);
            if (!hasX && !hasY && !hasUniform)
            {
                element.style.scale = StyleKeyword.Null;
                return;
            }
            var uniform = hasUniform ? uw.Value : UniformScaleFromClasses(element);
            var x = hasX ? xw.Value : uniform;
            var y = hasY ? yw.Value : uniform;
            element.style.scale = new Scale(new Vector2(x, y));
        }

        private static void RefreshScaleClassFallback(VisualElement element, LayerMap map, float? previousUniform)
        {
            if (previousUniform.HasValue && !TryWinningLayer(map, ArbitraryProperty.Scale, out _)
                && TryWinningLayer(map, ArbitraryProperty.ScaleX, out _) != TryWinningLayer(map, ArbitraryProperty.ScaleY, out _)
                && previousUniform.Value != UniformScaleFromClasses(element))
            {
                ApplyCombinedScale(element, map);
            }
        }

        private static float UniformScaleFromClasses(VisualElement element)
        {
            var uniform = 1f;
            var position = -1;
            foreach (var cls in element.GetClasses())
            {
                if (TryGetUniformScalePreset(cls, out var scale)
                    // MUTANT_SURVIVES(equivalent, boundary): each recognized uniform preset has a distinct generated cascade position; a tie repeats the same factor.
                    && StyleUtilityProperties.TryGet(cls, out var rule) && rule.CascadePosition > position)
                {
                    uniform = scale;
                    position = rule.CascadePosition;
                }
            }
            return uniform;
        }

        private static bool TryGetUniformScalePreset(string cls, out float scale)
        {
            scale = 1f;
            return cls.StartsWith("scale-", StringComparison.Ordinal)
                && TryGetAxisScale(cls.Substring("scale-".Length), out scale);
        }

        // transition-duration is a StyleList<TimeValue> (not a StyleLength), so the winning duration-[..] layer
        // is written out-of-band as a single-element list. The stored Value is in SECONDS. No null layer -> clear.
        private static void ApplyTransitionDuration(VisualElement element, LayerMap map)
        {
            StyleList<TimeValue> duration = StyleKeyword.Null;
            if (TryWinningLayer(map, ArbitraryProperty.TransitionDuration, out var winner))
            {
                duration = new List<TimeValue> { new TimeValue(winner.Value, TimeUnit.Second) };
            }
            element.style.transitionDuration = duration;
            var box = ClipPathLayoutBox.Of(element);
            if (box != element)
            {
                box.style.transitionDuration = duration;
            }
        }

        private static bool IsFilter(ArbitraryProperty p) => s_filterSet.Contains(p);

        // CSS composes filter functions left-to-right into one `filter` property; UITK matches (one
        // StyleList<FilterFunction>). Each filter-* utility is its own layer, so they are gathered here in a
        // fixed canonical order (the standard CSS filter order) into a single list — a missing one is just
        // skipped. An empty result clears the inline filter.
        private static readonly ArbitraryProperty[] s_filterOrder =
        {
            ArbitraryProperty.FilterBlur,
            ArbitraryProperty.FilterBrightness,
            ArbitraryProperty.FilterContrast,
            ArbitraryProperty.FilterGrayscale,
            ArbitraryProperty.FilterHueRotate,
            ArbitraryProperty.FilterInvert,
            ArbitraryProperty.FilterSaturate,
            ArbitraryProperty.FilterSepia,
        };

        // s_filterOrder's members plus the drop-shadow pair, whose two stacks AppendDropShadows reads together
        // rather than one function per property. Declared after s_filterOrder so the textual static-init order
        // is satisfied.
        private static readonly HashSet<ArbitraryProperty> s_filterSet = new(s_filterOrder)
        {
            ArbitraryProperty.FilterDropShadow,
            ArbitraryProperty.FilterDropShadowColor,
        };

        // Writes the filter the element's layers compose, variant layers included, or clears it where none remain.
        internal static void RecomposeFilter(VisualElement element)
        {
            if (s_layers.TryGetValue(element, out var map))
            {
                ApplyCombinedFilter(element, map);
            }
            else
            {
                StyleFilterEngineWrite.Write(element, null);
            }
        }

        // CSS interpolates a filter list holding a url() discretely, so a change into or out of a dropped chain is
        // written at once rather than handed to either animator.
        private static void ApplyCombinedFilter(VisualElement element, LayerMap map)
        {
            var wasDropped = map.FilterChainDropped;
            var functions = ComposeFilter(element, map, out var dropped);
            map.FilterChainDropped = dropped;
            if (wasDropped || dropped)
            {
                using (StyleFilterEngineWrite.WithoutTransition())
                {
                    WriteComposedFilter(element, functions);
                }
                return;
            }
            WriteComposedFilter(element, functions);
        }

        private static void WriteComposedFilter(VisualElement element, List<FilterFunction>? functions)
        {
            // Velvet's filter tween owns the write when it runs; it reads the current inline list as its
            // from-side, so it must run BEFORE the instant write below (never observing its own write).
            if (!StyleFilterTransitionDriver.TryStartOrRedirect(element, functions))
            {
                StyleFilterEngineWrite.Write(element, functions);
            }
        }

        private static List<FilterFunction>? ComposeFilter(VisualElement element, LayerMap map, out bool dropped)
        {
            dropped = false;
            List<FilterFunction>? functions = null;
            foreach (var prop in s_filterOrder)
            {
                if (!map.TryGetValue(prop, out var layers) || layers.Count == 0)
                {
                    continue;
                }
                if (BuildFilter(prop, layers.Values[layers.Count - 1].Value) is { } fn)
                {
                    (functions ??= new List<FilterFunction>()).Add(fn);
                }
            }
            AppendDropShadows(element, map, ref functions);
            // Customs compose AFTER every built-in, in first-application order — each entry contributing
            // its own highest-priority (winning) layer, the same "last entry in the ascending-by-priority
            // SortedList wins" rule the built-ins use above, just keyed by name instead of by
            // ArbitraryProperty. An empty stack is a tombstone holding its name's compose slot (see
            // LayerMap.Customs). A layer whose registration refuses its arguments is an invalid declaration's,
            // which CSS drops at parse time, so the layer under it wins. A winning layer with no definition
            // names no live filter — never registered, unregistered, or DESTROYED since it was applied, which
            // compares equal to null — and CSS ignores the whole chain when a url() names no filter.
            if (map.Customs == null)
            {
                return functions;
            }
            foreach (var (_, stack) in map.Customs)
            {
                var top = TopAcceptedLayer(stack);
                if (top < 0)
                {
                    continue;
                }
                var custom = stack.Values[top].Custom!;
                if (custom.Definition == null)
                {
                    dropped = true;
                    return null;
                }
                (functions ??= new List<FilterFunction>()).Add(BuildCustomFilter(custom));
            }
            return functions;
        }

        private static int TopAcceptedLayer(SortedList<long, ArbitraryStyle> stack)
        {
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (!stack.Values[i].Custom!.Rejected)
                {
                    return i;
                }
            }
            return -1;
        }

        // Tailwind's drop-shadow utilities write variables rather than the filter. A size writes
        // --tw-drop-shadow-size, its colours reading --tw-drop-shadow-color, and a colour writes that colour, scaled by
        // the --tw-drop-shadow-alpha a size's modifier writes. --tw-drop-shadow, which the filter reads, is written by
        // all of them: a literal size writes its own shadows, none writes an empty one, and every other token
        // var(--tw-drop-shadow-size), which none leaves to the size under it. At one rank the colour's rule sorts
        // after the others and wins, so the comparison reads ranks alone, never the rule's place in a key.
        private static void AppendDropShadows(VisualElement element, LayerMap map, ref List<FilterFunction>? functions)
        {
            if (!map.TryGetValue(ArbitraryProperty.FilterDropShadow, out var sizes) || sizes.Count == 0)
            {
                return;
            }
            var definition = BuiltInFilterDefinitions.DropShadow;
            if (definition == null || sizes.Values[sizes.Count - 1].DropShadow is not { } top)
            {
                return;
            }
            Color? variable = null;
            var colorWrites = false;
            if (map.TryGetValue(ArbitraryProperty.FilterDropShadowColor, out var colors) && colors.Count > 0)
            {
                var color = colors.Values[colors.Count - 1].Color;
                color.a *= AlphaVariable(sizes) ?? 1f;
                variable = color;
                colorWrites = StyleLayerPriority.RankOf(colors.Keys[colors.Count - 1])
                    >= StyleLayerPriority.RankOf(sizes.Keys[sizes.Count - 1]);
            }
            var literal = top.Literal && !colorWrites;
            var size = literal ? top : SizeVariable(sizes);
            if (size == null)
            {
                return;
            }
            foreach (var shadow in size.Shadows)
            {
                var color = shadow.Color;
                if (!literal)
                {
                    if (variable != null)
                    {
                        color = variable.Value;
                    }
                    else if (size.Alpha != null)
                    {
                        color.a = size.Alpha.Value;
                    }
                }
                if (float.IsNaN(color.r))
                {
                    var current = element.resolvedStyle.color;
                    current.a *= color.a;
                    color = current;
                }
                var function = new FilterFunction(definition!);
                function.AddParameter(new FilterParameter(shadow.X));
                function.AddParameter(new FilterParameter(shadow.Y));
                function.AddParameter(new FilterParameter(shadow.Deviation));
                function.AddParameter(new FilterParameter(color));
                (functions ??= new List<FilterFunction>()).Add(function);
            }
        }

        // The highest layer writing --tw-drop-shadow-size, which none does not.
        private static DropShadowValue? SizeVariable(SortedList<long, ArbitraryStyle> sizes)
        {
            for (var i = sizes.Count - 1; i >= 0; i--)
            {
                if (sizes.Values[i].DropShadow is { Shadows: { Length: > 0 } } size)
                {
                    return size;
                }
            }
            return null;
        }

        // The highest layer declaring a modifier: a size without one leaves --tw-drop-shadow-alpha alone.
        private static float? AlphaVariable(SortedList<long, ArbitraryStyle> sizes)
        {
            for (var i = sizes.Count - 1; i >= 0; i--)
            {
                if (sizes.Values[i].DropShadow?.Alpha is { } alpha)
                {
                    return alpha;
                }
            }
            return null;
        }

        // Builds the FilterFunction for a filter-[name:args] custom filter. The public
        // FilterFunctionDefinition ctor sets type = Custom and customDefinition in one step. Args always
        // carries the FULL declared parameter count — the explicit segments plus a tail padded from the
        // declaration's defaults at parse time — because this public construction path performs none of
        // the padding the engine's USS parser does: an under-filled function stops binding at its
        // parameterCount at render time, leaving whatever value the shared material-property state still
        // holds from a previous draw where the declared default should be.
        private static FilterFunction BuildCustomFilter(CustomFilterValue custom)
        {
            var fn = new FilterFunction(custom.Definition);
            foreach (var arg in custom.Args)
            {
                fn.AddParameter(arg);
            }
            return fn;
        }

        // Null only when a built-in custom-filter shader (brightness/saturate) is unavailable in the build;
        // the caller drops that layer. Every other branch always returns a value.
        private static FilterFunction? BuildFilter(ArbitraryProperty prop, float value)
        {
            // brightness and saturate have no UITK filter type; each renders through a first-party
            // custom-filter shader (BuiltInFilterDefinitions) as a FilterFunctionType.Custom function. The
            // shaders take the full CSS range (over-brighten and over-saturate, N>1) and do the multiply/lerp
            // on the encoded pixel before the Linear-output conversion, matching browser semantics exactly.
            // The stored Value is the raw CSS factor N — the
            // shader implements saturate's lerp-toward-luminance natively, so there is no 1-N complement to
            // pre-compute. A null definition (shader stripped from the build) drops the layer, the same degrade
            // the bake shaders take when their shader is missing.
            if (prop == ArbitraryProperty.FilterBrightness)
            {
                var def = BuiltInFilterDefinitions.Brightness;
                if (def == null)
                {
                    return null;
                }
                var brightness = new FilterFunction(def!);
                brightness.AddParameter(new FilterParameter(value));
                return brightness;
            }
            if (prop == ArbitraryProperty.FilterSaturate)
            {
                var def = BuiltInFilterDefinitions.Saturate;
                if (def == null)
                {
                    return null;
                }
                var saturate = new FilterFunction(def!);
                saturate.AddParameter(new FilterParameter(value));
                return saturate;
            }

            // This method's domain is s_filterOrder, which ApplyCombinedFilter — its only caller —
            // iterates, so naming the ArbitraryProperty members outside that array would map each to a
            // filter type it is not. What the throw buys over the Sepia it replaces: a filter added to
            // s_filterOrder with no arm here reports, rather than rendering as sepia.
            var type = prop switch
            {
                ArbitraryProperty.FilterBlur => FilterFunctionType.Blur,
                ArbitraryProperty.FilterContrast => FilterFunctionType.Contrast,
                ArbitraryProperty.FilterGrayscale => FilterFunctionType.Grayscale,
                ArbitraryProperty.FilterHueRotate => FilterFunctionType.HueRotate,
                ArbitraryProperty.FilterInvert => FilterFunctionType.Invert,
                ArbitraryProperty.FilterSepia => FilterFunctionType.Sepia,
                _ => throw new ArgumentOutOfRangeException(nameof(prop), prop, "not a built-in filter"),
            };
            // Only the single-arg ctor + AddParameter are public (the (type,value) ctors are internal).
            var fn = new FilterFunction(type);
            fn.AddParameter(new FilterParameter(value));
            return fn;
        }

        // Writes a single ArbitraryStyle to the element's inline style (no layering), fanning a shorthand out to
        // every slot it owns (padding → four edges, border-color → four sides, size → width + height). A value
        // that places or sizes a clipped element goes to its clip wrapper instead (ClipPathLayoutBox.StyleFor).
        // Class-diff callers must go through Apply / Clear instead so per-property layering is respected, and a
        // per-frame driver through ApplyDriven / ReleaseDriven.
        internal static void ApplyInline(VisualElement element, in ArbitraryStyle style)
        {
            // Transform properties (scale / translate / rotate / transform-origin) are not StyleLength and
            // are written through their dedicated UITK style properties.
            switch (style.Property)
            {
                // Among transform properties, scale (uniform + per-axis) and both translate axes are composed
                // by ResolveAndApply's combined appliers and never reach here; what lands is every transform
                // property written in one go — rotate, transform-origin — plus aspect-ratio, the four-edge Slice and
                // the flex shorthand's three longhands.
                case ArbitraryProperty.Rotate:
                    element.style.rotate = new Rotate(new Angle(style.Value, AngleUnit.Degree));
                    return;
                case ArbitraryProperty.TransformOrigin:
                    element.style.transformOrigin = new TransformOrigin(
                        new Length(style.Value, style.Unit), new Length(style.Value2, style.Unit2), style.Value3);
                    return;
                // opacity-[..] is a unitless factor (0..1), written through the slot a layoutId crossfade shares.
                case ArbitraryProperty.Opacity:
                    MotionOpacity.WriteTransitioned(element, style.Value);
                    return;
                case ArbitraryProperty.Slice:
                {
                    var slices = element.style;
                    slices.unitySliceTop = SliceInset(element, style, style.Value, 0, horizontal: false);
                    slices.unitySliceRight = SliceInset(element, style, style.Value2, 1, horizontal: true);
                    slices.unitySliceBottom = SliceInset(element, style, style.Value3, 2, horizontal: false);
                    slices.unitySliceLeft = SliceInset(element, style, style.Value4, 3, horizontal: true);
                    return;
                }
                case ArbitraryProperty.BackgroundRepeat:
                    WriteRepeat(element, style);
                    return;
                case ArbitraryProperty.SliceType:
                    WriteSliceType(element, style);
                    return;
                case ArbitraryProperty.Flex:
                {
                    var flex = ClipPathLayoutBox.StyleFor(element, style.Property);
                    flex.flexGrow = style.Value;
                    flex.flexShrink = style.Value3;
                    // The basis is written as basis-[…] writes its own, a measured length included.
                    WriteLength(element, flex, FlexBasisOf(style), PropertySetters[ArbitraryProperty.FlexBasis]);
                    return;
                }
                case ArbitraryProperty.AspectRatio:
                {
                    Ratio ratio = style.Value;          // float -> Ratio (implicit)
                    ClipPathLayoutBox.StyleFor(element, style.Property).aspectRatio = ratio;  // Ratio -> StyleRatio (implicit)
                    return;
                }
            }

            if (ColorSetters.TryGetValue(style.Property, out var colorSetters))
            {
                var color = style.Keyword == StyleKeyword.Undefined
                    ? new StyleColor(style.Color)
                    : new StyleColor(style.Keyword);
                var cs = element.style;
                foreach (var setter in colorSetters)
                {
                    setter(cs, color);
                }
                return;
            }

            if (IntSetters.TryGetValue(style.Property, out var intSetters))
            {
                var inset = SliceInset(element, style, style.Value, 0, s_horizontalSlices.Contains(style.Property));
                var ins = element.style;
                foreach (var setter in intSetters)
                {
                    setter(ins, inset);
                }
                return;
            }

            if (FloatSetters.TryGetValue(style.Property, out var floatSetters))
            {
                var width = style.Keyword == StyleKeyword.Undefined
                    ? new StyleFloat(style.Value)
                    : new StyleFloat(style.Keyword);
                var fs = ClipPathLayoutBox.StyleFor(element, style.Property);
                foreach (var setter in floatSetters)
                {
                    setter(fs, width);
                }
                return;
            }

            if (RadiusCornersOf.TryGetValue(style.Property, out var corners))
            {
                CornerRadiusFit.SetInline(element, corners, new Length(style.Value, style.Unit));
                return;
            }

            if (!PropertySetters.TryGetValue(style.Property, out var setters))
            {
                return;
            }

            WriteLength(element, ClipPathLayoutBox.BoxFor(element, style.Property).style, style, setters);
        }

        // A Flex result's basis as the FlexBasis result basis-[…] parses to.
        private static ArbitraryStyle FlexBasisOf(in ArbitraryStyle flex)
            => flex.Expression != null ? new ArbitraryStyle(ArbitraryProperty.FlexBasis, flex.Expression)
                : float.IsNaN(flex.Value2) ? ArbitraryStyle.AutoLength(ArbitraryProperty.FlexBasis)
                : new ArbitraryStyle(ArbitraryProperty.FlexBasis, flex.Value2, flex.Unit2);

        // A relative length is measured per setter, since each carries its own axis and its own stand-in for an
        // indefinite one; a length that cannot be measured yet leaves that slot as it is.
        private static void WriteLength(VisualElement element, IStyle target, in ArbitraryStyle style, LengthSetter[] setters)
        {
            if (style.Expression != null)
            {
                foreach (var setter in setters)
                {
                    if (StyleRelativeLengths.TryResolve(element, style.Expression, setter.Axis, setter.Indefinite, out var resolved))
                    {
                        setter.Set(target, resolved);
                    }
                }
                return;
            }
            var length = style.ToStyleLength();
            foreach (var setter in setters)
            {
                setter.Set(target, length);
            }
        }

        // Clears the inline style for the given property (StyleKeyword.Null reverts to the USS default), fanning
        // out to the same slot set ApplyInline writes. Class-diff callers go through Clear so a surviving
        // lower-priority layer is re-applied; the layer-bypassing form pairs with the ApplyInline above.
        internal static void ClearInline(VisualElement element, ArbitraryProperty property)
        {
            if (TryClearDedicatedSlot(element, property))
            {
                return;
            }

            if (ColorSetters.TryGetValue(property, out var colorSetters))
            {
                var nullColor = new StyleColor(StyleKeyword.Null);
                var cs = element.style;
                foreach (var setter in colorSetters)
                {
                    setter(cs, nullColor);
                }
                return;
            }

            if (IntSetters.TryGetValue(property, out var intSetters))
            {
                var nullInt = new StyleInt(StyleKeyword.Null);
                var ins = element.style;
                foreach (var setter in intSetters)
                {
                    setter(ins, nullInt);
                }
                return;
            }

            if (FloatSetters.TryGetValue(property, out var floatSetters))
            {
                var nullFloat = new StyleFloat(StyleKeyword.Null);
                var fs = ClipPathLayoutBox.StyleFor(element, property);
                foreach (var setter in floatSetters)
                {
                    setter(fs, nullFloat);
                }
                return;
            }

            if (RadiusCornersOf.TryGetValue(property, out var corners))
            {
                CornerRadiusFit.ClearInline(element, corners);
                return;
            }

            if (!PropertySetters.TryGetValue(property, out var setters))
            {
                return;
            }

            var nullStyle = new StyleLength(StyleKeyword.Null);
            var s = ClipPathLayoutBox.StyleFor(element, property);
            foreach (var setter in setters)
            {
                setter.Set(s, nullStyle);
            }
        }

        // The properties written through a UITK style property of their own rather than through the setter
        // tables.
        private static bool TryClearDedicatedSlot(VisualElement element, ArbitraryProperty property)
        {
            switch (property)
            {
                case ArbitraryProperty.Rotate:
                    element.style.rotate = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.TransformOrigin:
                    element.style.transformOrigin = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.Opacity:
                    MotionOpacity.WriteTransitioned(element, StyleKeyword.Null);
                    // MUTANT_SURVIVES(equivalent): opacity has no entry in the fallback setter tables ClearInline reads.
                    return true;
                case ArbitraryProperty.AspectRatio:
                    ClipPathLayoutBox.StyleFor(element, property).aspectRatio = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.Flex:
                {
                    var flex = ClipPathLayoutBox.StyleFor(element, property);
                    flex.flexGrow = StyleKeyword.Null;
                    flex.flexShrink = StyleKeyword.Null;
                    flex.flexBasis = StyleKeyword.Null;
                    // MUTANT_SURVIVES(equivalent, literal): the flex shorthand has no entry in the fallback setter tables ClearInline reads.
                    return true;
                }
                // translate and scale are each a single shorthand for both axes (and scale composes the uniform
                // + per-axis layers), so clearing any one reverts the whole property. In the class-diff reconcile
                // path the survivors are restored by FiberNodePatcher.ReapplyArbitraryValues; a direct Clear (or a
                // variant-payload toggle) of one drops the others until the next full re-apply — combine them in a
                // single combined translate/scale when that matters.
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY:
                    element.style.translate = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.Scale:
                case ArbitraryProperty.ScaleX:
                case ArbitraryProperty.ScaleY:
                    element.style.scale = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.TransitionDuration:
                    element.style.transitionDuration = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.BackgroundRepeat:
                    StyleAnimateDriver.WriteBackgroundRepeat(element, StyleKeyword.Null);
                    // MUTANT_SURVIVES(equivalent): background repeat has no entry in the fallback setter tables ClearInline reads.
                    return true;
                case ArbitraryProperty.SliceType:
                    element.style.unitySliceType = StyleKeyword.Null;
                    // MUTANT_SURVIVES(equivalent): slice type has no entry in the fallback setter tables ClearInline reads.
                    return true;
            }

            // Like the axes above, all filter-* utilities share the one inline `filter` list, so clearing any
            // reverts the whole property; the surviving filters are restored by ReapplyArbitraryValues.
            if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
            {
                if (!StyleFilterTransitionDriver.TryStartOrRedirect(element, null))
                {
                    StyleFilterEngineWrite.Write(element, null);
                }
                return true;
            }
            return false;
        }

        // prefix includes the trailing '-' (e.g. "h-", "min-h-", "mt-", "rounded-").
        private static readonly Dictionary<string, ArbitraryProperty> s_prefixProperties = new()
        {
            ["w-"] = ArbitraryProperty.Width,
            ["h-"] = ArbitraryProperty.Height,
            ["min-w-"] = ArbitraryProperty.MinWidth,
            ["min-h-"] = ArbitraryProperty.MinHeight,
            ["max-w-"] = ArbitraryProperty.MaxWidth,
            ["max-h-"] = ArbitraryProperty.MaxHeight,
            ["size-"] = ArbitraryProperty.Size,
            ["basis-"] = ArbitraryProperty.FlexBasis,

            ["top-"] = ArbitraryProperty.Top,
            ["right-"] = ArbitraryProperty.Right,
            ["bottom-"] = ArbitraryProperty.Bottom,
            ["left-"] = ArbitraryProperty.Left,
            ["inset-"] = ArbitraryProperty.Inset,
            ["inset-x-"] = ArbitraryProperty.InsetX,
            ["inset-y-"] = ArbitraryProperty.InsetY,

            ["pt-"] = ArbitraryProperty.PaddingTop,
            ["pr-"] = ArbitraryProperty.PaddingRight,
            ["pb-"] = ArbitraryProperty.PaddingBottom,
            ["pl-"] = ArbitraryProperty.PaddingLeft,
            ["p-"] = ArbitraryProperty.Padding,
            ["px-"] = ArbitraryProperty.PaddingX,
            ["py-"] = ArbitraryProperty.PaddingY,

            ["mt-"] = ArbitraryProperty.MarginTop,
            ["mr-"] = ArbitraryProperty.MarginRight,
            ["mb-"] = ArbitraryProperty.MarginBottom,
            ["ml-"] = ArbitraryProperty.MarginLeft,
            ["m-"] = ArbitraryProperty.Margin,
            ["mx-"] = ArbitraryProperty.MarginX,
            ["my-"] = ArbitraryProperty.MarginY,

            ["rounded-"] = ArbitraryProperty.BorderRadius,
            ["rounded-t-"] = ArbitraryProperty.BorderTopRadius,
            ["rounded-r-"] = ArbitraryProperty.BorderRightRadius,
            ["rounded-b-"] = ArbitraryProperty.BorderBottomRadius,
            ["rounded-l-"] = ArbitraryProperty.BorderLeftRadius,
            ["rounded-tl-"] = ArbitraryProperty.BorderTopLeftRadius,
            ["rounded-tr-"] = ArbitraryProperty.BorderTopRightRadius,
            ["rounded-bl-"] = ArbitraryProperty.BorderBottomLeftRadius,
            ["rounded-br-"] = ArbitraryProperty.BorderBottomRightRadius,

            // A color value is claimed earlier, for `border-` in TryParseBracketValue and for a side in
            // StyleBorderSideColor; here it is the width form.
            ["border-"] = ArbitraryProperty.BorderWidth,
            ["border-t-"] = ArbitraryProperty.BorderTopWidth,
            ["border-r-"] = ArbitraryProperty.BorderRightWidth,
            ["border-b-"] = ArbitraryProperty.BorderBottomWidth,
            ["border-l-"] = ArbitraryProperty.BorderLeftWidth,

            ["text-"] = ArbitraryProperty.FontSize,
            ["tracking-"] = ArbitraryProperty.LetterSpacing,

            // The transform prefixes (scale- / rotate- / translate-x- / translate-y-) are absent on purpose:
            // TryParseBracketValue intercepts them before this table so all four share the Apply/Clear switch.
        };

        // Single source for the utility-prefix → target-property mapping, shared so a non-bracket preset
        // recognizer (MotionPropertyClassParser, which pairs a prefix with the numeric scale its USS family
        // uses) resolves the SAME prefix table instead of holding a second copy that could drift — mirroring
        // TryGetSpacingPx's precedent. Which numeric SCALE a prefix's suffix is read against is deliberately
        // not encoded here: this table is shared by families on different scales (spacing, radius, border
        // width), and only the caller knows which family it is claiming.
        internal static bool TryGetProperty(string prefix, out ArbitraryProperty property)
        {
            property = default;
            return prefix != null && s_prefixProperties.TryGetValue(prefix, out property);
        }

        // Strips the enclosing brackets off a bracketed JIT arbitrary-value token (`[value]`), optionally
        // after a fixed literal prefix that precedes the '[' (prefixLength chars — e.g. 2 for "z-" in
        // "z-[10]", 0 for a bare "[value]" token). Rejects an empty value ("z-[]" / "[]"): every call
        // site's downstream parse (a color, a number, a unit-suffixed length) already fails on an empty
        // string, so accepting it here would only defer the identical rejection to each call site instead
        // of making it once, here. Span-based so a caller already holding a slice (the common shape on
        // this per-class hot path) pays no extra allocation; a caller holding a string gets the same for
        // free via the implicit span conversion.
        internal static bool TryStripBrackets(ReadOnlySpan<char> token, int prefixLength, out ReadOnlySpan<char> inner)
        {
            if (token.Length < prefixLength + 3 || token[prefixLength] != '[' || token[token.Length - 1] != ']')
            {
                inner = default;
                return false;
            }
            inner = token.Slice(prefixLength + 1, token.Length - prefixLength - 2);
            return true;
        }

        // Parses a bracketed JIT arbitrary value (`[..]`) that must resolve to a non-negative pixel length —
        // the contract shared by gap-[..], divide-*-[..], and ring-[..], where a percentage is meaningless.
        // Takes the whole suffix incl. brackets; returns false for a non-`[..]` token or any non-px / negative
        // value. Span-based so callers pass an existing slice with no extra allocation.
        internal static bool TryParseArbitraryPixels(ReadOnlySpan<char> suffix, out float px)
        {
            px = 0f;
            if (!TryStripBrackets(suffix, 0, out var inner))
            {
                return false;
            }
            if (TryParseValue(inner, out var value, out var unit)
                && unit == LengthUnit.Pixel && value >= 0f)
            {
                px = value;
                return true;
            }
            return false;
        }

        // Parses a <length-percentage> token: a '%' suffix is percent; a 'px', 'rem', an absolute unit (in, cm,
        // mm, pt, pc, Q) or no suffix is pixel (a bare number is a length only as 0, and rem is converted at the fixed
        // 1rem = 16px scale because UI Toolkit has no rem unit and no document root to resolve a relative font
        // size against). A calc() / min() / max() / clamp() is read when it comes to one pixel length or one
        // percentage whatever it is measured against; one that needs an element to measure against is declined
        // here, and TryParseBracketValue keeps it for the properties that can measure it.
        // InvariantCulture, finite values only. Internal so other utility parsers (clip-path) share THE
        // length grammar instead of re-implementing it.
        internal static bool TryParseValue(ReadOnlySpan<char> valueStr, out float value, out LengthUnit unit)
        {
            value = 0;
            unit = LengthUnit.Pixel;
            // A `_` at either end is the space a browser trims from the declaration Tailwind writes.
            valueStr = valueStr.Trim("_ ".AsSpan());

            bool parsed;
            if (valueStr.Length > 0 && valueStr[valueStr.Length - 1] == '%')
            {
                unit = LengthUnit.Percent;
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else if (valueStr.EndsWith("rem".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                // 1rem resolves to a fixed 16px (UI Toolkit has no rem unit), so the parsed head is
                // scaled and emitted as a pixel length rather than carried as a distinct unit.
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 3),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
                value *= 16f;
            }
            else if (valueStr.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                unit = LengthUnit.Pixel;
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 2),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else if (TryFindAbsoluteUnit(valueStr, out var unitLength, out var pixelsPerUnit))
            {
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - unitLength),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
                value *= pixelsPerUnit;
            }
            else
            {
                // A bare number is a length only as 0: the browser rejects the declaration Tailwind writes for
                // any other.
                parsed = (float.TryParse(
                    valueStr,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value) && value == 0f)
                    || (StyleLengthExpression.OpensMathFunction(valueStr) && StyleLengthExpression.TryParse(valueStr, out var expression)
                        && expression!.TryFoldConstant(out value, out unit));
            }

            return parsed && float.IsFinite(value);
        }

        // CSS's absolute length units at their fixed ratio to a pixel (96 per inch).
        private static readonly (string Suffix, float Pixels)[] s_absoluteUnits =
        {
            ("in", 96f), ("cm", 96f / 2.54f), ("mm", 96f / 25.4f), ("pt", 96f / 72f), ("pc", 16f), ("Q", 96f / 101.6f),
        };

        // TryParseArbitraryPixels where Tailwind reads the bracket as either a length or a colour (ring-, ring-offset-):
        // its length test, or a hint the table names for root, decides, and hands `2PX` or a bare `2` to the colour branch.
        internal static bool TryParseInferredArbitraryPixels(string root, ReadOnlySpan<char> suffix, out float px)
        {
            px = 0f;
            if (!TryStripBrackets(suffix, 0, out var inner) || !TrySplitTypeHint(inner, out var hint, out inner))
            {
                return false;
            }
            return (hint != null ? StyleColorValueParser.HintSelectsNonColour(root, hint) : TailwindDataTypes.IsLength(inner)) && TryParsePixels(inner, out px);
        }

        // A non-negative pixel length, the grammar TryParseArbitraryPixels reads inside its bracket.
        internal static bool TryParsePixels(ReadOnlySpan<char> inner, out float px)
        {
            var parsed = TryParseValue(inner, out px, out var unit) && unit == LengthUnit.Pixel && px >= 0f;
            px = parsed ? px : 0f;
            return parsed;
        }

        // A border-[…] width as TryParseBracketValue reads it, for a reader that has to agree with the width applied.
        internal static bool TryParseBorderWidthPixels(ReadOnlySpan<char> suffix, out float px)
        {
            px = 0f;
            if (!TryStripBrackets(suffix, 0, out var inner) || !TrySplitTypeHint(inner, out var hint, out inner)
                || !TailwindReadsAsLength(ArbitraryProperty.BorderWidth, inner, hint))
            {
                return false;
            }
            if (s_lineWidthKeywords.TryGetValue(inner.ToString(), out px))
            {
                return true;
            }
            return TryParsePixels(inner, out px);
        }

        // The pixels in one of the absolute units, for StyleLengthExpression's dimensions.
        internal static bool TryGetAbsoluteUnitPixels(ReadOnlySpan<char> unit, out float pixels)
        {
            foreach (var (suffix, perUnit) in s_absoluteUnits)
            {
                if (unit.Equals(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    pixels = perUnit;
                    return true;
                }
            }
            pixels = 0f;
            return false;
        }

        private static bool TryFindAbsoluteUnit(ReadOnlySpan<char> valueStr, out int length, out float pixels)
        {
            foreach (var (suffix, perUnit) in s_absoluteUnits)
            {
                if (valueStr.EndsWith(suffix.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    length = suffix.Length;
                    pixels = perUnit;
                    return true;
                }
            }
            length = 0;
            pixels = 0f;
            return false;
        }

        // Parses a unitless finite float (used by scale-[..]). A trailing unit is rejected.
        // Internal so the extracted transform/filter parsers share the one float grammar.
        internal static bool TryParseFloat(ReadOnlySpan<char> valueStr, out float value)
        {
            var parsed = float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && float.IsFinite(value);
        }

        // Parses an aspect ratio (used by aspect-[..]): either a "w/h" fraction or a bare
        // decimal. A zero/negative denominator or a non-finite result is rejected (returns false).
        private static bool TryParseRatio(ReadOnlySpan<char> valueStr, out float ratio)
        {
            ratio = 0f;
            var slash = valueStr.IndexOf('/');
            if (slash < 0)
            {
                return TryParseFloat(valueStr, out ratio) && ratio > 0f;
            }
            if (!float.TryParse(valueStr.Slice(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(valueStr.Slice(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                || h == 0f)
            {
                return false;
            }
            ratio = w / h;
            return float.IsFinite(ratio) && ratio > 0f;
        }

        // Parses an angle (used by rotate-[..]) and normalizes it to degrees. Accepts the
        // deg / rad / grad / turn suffixes in any case; a bare number is degrees, which each caller
        // narrows to what its CSS property takes (IsUnitlessAngle).
        // Internal so the extracted transform/filter parsers share the one angle grammar.
        internal static bool TryParseAngleDegrees(ReadOnlySpan<char> valueStr, out float degrees)
        {
            degrees = 0f;

            var factor = 1f;
            var numeric = valueStr;
            if (valueStr.EndsWith("deg".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 3);
            }
            else if (valueStr.EndsWith("grad".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 4);
                factor = 0.9f; // 400 grad = 360 deg
            }
            else if (valueStr.EndsWith("rad".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 3);
                factor = Mathf.Rad2Deg;
            }
            else if (valueStr.EndsWith("turn".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 4);
                factor = 360f;
            }

            if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw) || !float.IsFinite(raw))
            {
                return false;
            }

            degrees = raw * factor;
            return true;
        }

        // Whether an angle TryParseAngleDegrees accepted was a bare number, which is never empty.
        internal static bool IsUnitlessAngle(ReadOnlySpan<char> valueStr) => !char.IsLetter(valueStr[valueStr.Length - 1]);
    }
}
