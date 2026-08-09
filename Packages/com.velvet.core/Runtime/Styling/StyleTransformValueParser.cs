using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Transform value parser for the arbitrary-value dispatch (StyleArbitraryValueResolver): the bracket
    // prefixes the prefix table cannot carry, for either of two reasons — the axes and the pivot compose
    // onto one engine property and would last-write-wins through it, and the factors and the angle are
    // not lengths, which is the only value the table knows how to build.
    // The dispatch calls in; this group calls only the resolver's shared scalar grammar
    // (TryParseFloat / TryParseAngleDegrees / TryParseValue), never back into the dispatch or another parser.
    internal static class StyleTransformValueParser
    {
        // Transform-and-merge bracket prefixes (scale = unitless factor, rotate = angle, opacity = 0..1
        // float, translate = length). true (result set) on success; false to reject a matched-but-invalid
        // value; null when not a transform prefix (fall through to the length-based path).
        internal static bool? TryParseTransformValue(string prefix, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;

            if (prefix == "scale-")
            {
                return TryParseScale(valueSpan, negate, out result);
            }

            // scale-x-/scale-y- are unitless factors routed (like translate-x-/-y-) through the merge path so
            // the two axes compose onto the single inline `scale` instead of last-write-wins.
            if (prefix == "scale-x-" || prefix == "scale-y-")
            {
                return TryParseAxisScale(prefix == "scale-x-" ? ArbitraryProperty.ScaleX : ArbitraryProperty.ScaleY,
                    valueSpan, negate, out result);
            }

            if (prefix == "rotate-")
            {
                return TryParseRotate(valueSpan, negate, out result);
            }

            if (prefix == "opacity-")
            {
                return TryParseOpacity(valueSpan, negate, out result);
            }

            if (prefix == "grow-" || prefix == "shrink-")
            {
                return TryParseFlexFactor(
                    prefix == "grow-" ? ArbitraryProperty.FlexGrow : ArbitraryProperty.FlexShrink,
                    valueSpan, negate, out result);
            }

            // translate-x-/translate-y- are lengths (px/%) routed here (not through TryGetProperty) so all
            // four transform properties share one parse-and-apply path (the Apply/Clear transform switch).
            if (prefix == "translate-x-" || prefix == "translate-y-")
            {
                return TryParseTranslate(
                    prefix == "translate-x-" ? ArbitraryProperty.TranslateX : ArbitraryProperty.TranslateY,
                    valueSpan, negate, out result);
            }

            // origin-[33%_75%] is a pair, and the nine keyword spellings are USS classes rather than bracket
            // values, so nothing here parses a keyword: origin-[left_top] is rejected and origin-top-left is
            // the way to say it.
            if (prefix == "origin-")
            {
                return TryParseTransformOrigin(valueSpan, negate, out result);
            }

            return null;
        }

        private static bool TryParseScale(ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var scaleValue)) return false;
            result = new ArbitraryStyle(ArbitraryProperty.Scale, negate ? -scaleValue : scaleValue, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseAxisScale(ArbitraryProperty property, ReadOnlySpan<char> valueSpan, bool negate,
            out ArbitraryStyle result)
        {
            result = default;
            if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var axisScale)) return false;
            result = new ArbitraryStyle(property, negate ? -axisScale : axisScale, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseRotate(ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (!StyleArbitraryValueResolver.TryParseAngleDegrees(valueSpan, out var degrees)) return false;
            result = new ArbitraryStyle(ArbitraryProperty.Rotate, negate ? -degrees : degrees, LengthUnit.Pixel);
            return true;
        }

        // opacity-[..] is a unitless StyleFloat (0..1). Out-of-range or negated values are rejected
        // (UITK does not clamp style.opacity), so opacity-[2] / -opacity-[.5] is not a recognized utility.
        private static bool TryParseOpacity(ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate || !StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var opacityValue)
                || opacityValue < 0f || opacityValue > 1f)
            {
                return false;
            }
            result = new ArbitraryStyle(ArbitraryProperty.Opacity, opacityValue, LengthUnit.Pixel);
            return true;
        }

        // grow-[..] / shrink-[..] are unitless ratios, so they take the float grammar rather than the
        // length one every other prefix in s_prefixProperties shares: that grammar accepts a suffix and
        // converts it, which would turn grow-[2rem] into 32 and — since the float setters read only the
        // value — grow-[50%] into 50, a ratio fifty times what the author wrote. A negative factor is
        // rejected for the same reason a negative opacity is: CSS declares it invalid and UITK does not
        // clamp.
        private static bool TryParseFlexFactor(ArbitraryProperty property, ReadOnlySpan<char> valueSpan,
            bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate || !StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var factor) || factor < 0f)
            {
                return false;
            }
            result = new ArbitraryStyle(property, factor, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseTranslate(ArbitraryProperty property, ReadOnlySpan<char> valueSpan, bool negate,
            out ArbitraryStyle result)
        {
            result = default;
            if (!StyleArbitraryValueResolver.TryParseValue(valueSpan, out var tValue, out var tUnit)) return false;
            if (negate) tValue = -tValue;
            result = new ArbitraryStyle(property, tValue, tUnit);
            return true;
        }

        // A pivot: one length, or two separated by the underscore the bracket grammar spells a space with.
        // The negation prefix is refused because Tailwind declares no negative variant of this utility, and
        // nothing is lost by it: a minus inside the brackets reaches the value grammar intact, so
        // origin-[-10px] and origin-[-10px_-20px] both parse.
        private static bool TryParseTransformOrigin(ReadOnlySpan<char> valueSpan, bool negate,
            out ArbitraryStyle result)
        {
            result = default;
            if (negate) return false;

            var separator = valueSpan.IndexOf('_');
            var xSpan = separator < 0 ? valueSpan : valueSpan[..separator];
            if (!StyleArbitraryValueResolver.TryParseValue(xSpan, out var x, out var xUnit)) return false;

            // A single component is the x alone, and CSS leaves the y at 50% — not at the x, which would
            // make origin-[0px] the top-left corner instead of the left edge's middle.
            if (separator < 0)
            {
                result = new ArbitraryStyle(ArbitraryProperty.TransformOrigin, x, xUnit, 50f, LengthUnit.Percent);
                return true;
            }

            // A third component needs no rejection of its own: the value grammar rejects "20%_30%" whole.
            var ySpan = valueSpan[(separator + 1)..];
            if (!StyleArbitraryValueResolver.TryParseValue(ySpan, out var y, out var yUnit)) return false;

            result = new ArbitraryStyle(ArbitraryProperty.TransformOrigin, x, xUnit, y, yUnit);
            return true;
        }
    }
}
