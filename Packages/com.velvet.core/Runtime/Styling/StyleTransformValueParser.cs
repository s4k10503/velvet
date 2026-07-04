using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Transform value parser for the arbitrary-value dispatch (StyleArbitraryValueResolver): the
    // transform-and-merge bracket prefixes (scale / scale-x / scale-y / rotate / opacity / translate-x /
    // translate-y). The dispatch calls in; this group calls only the resolver's shared scalar grammar
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
                if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var scaleValue)) return false;
                result = new ArbitraryStyle(ArbitraryProperty.Scale, negate ? -scaleValue : scaleValue, LengthUnit.Pixel);
                return true;
            }

            // scale-x-/scale-y- are unitless factors routed (like translate-x-/-y-) through the merge path so
            // the two axes compose onto the single inline `scale` instead of last-write-wins.
            if (prefix == "scale-x-" || prefix == "scale-y-")
            {
                if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var axisScale)) return false;
                result = new ArbitraryStyle(
                    prefix == "scale-x-" ? ArbitraryProperty.ScaleX : ArbitraryProperty.ScaleY,
                    negate ? -axisScale : axisScale, LengthUnit.Pixel);
                return true;
            }

            if (prefix == "rotate-")
            {
                if (!StyleArbitraryValueResolver.TryParseAngleDegrees(valueSpan, out var degrees)) return false;
                result = new ArbitraryStyle(ArbitraryProperty.Rotate, negate ? -degrees : degrees, LengthUnit.Pixel);
                return true;
            }

            // opacity-[..] is a unitless StyleFloat (0..1). Out-of-range or negated values are rejected
            // (UITK does not clamp style.opacity), so opacity-[2] / -opacity-[.5] is not a recognized utility.
            if (prefix == "opacity-")
            {
                if (negate || !StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var opacityValue)
                    || opacityValue < 0f || opacityValue > 1f)
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.Opacity, opacityValue, LengthUnit.Pixel);
                return true;
            }

            // translate-x-/translate-y- are lengths (px/%) routed here (not through TryGetProperty) so all
            // four transform properties share one parse-and-apply path (the Apply/Clear transform switch).
            if (prefix == "translate-x-" || prefix == "translate-y-")
            {
                if (!StyleArbitraryValueResolver.TryParseValue(valueSpan, out var tValue, out var tUnit)) return false;
                if (negate) tValue = -tValue;
                result = new ArbitraryStyle(
                    prefix == "translate-x-" ? ArbitraryProperty.TranslateX : ArbitraryProperty.TranslateY,
                    tValue, tUnit);
                return true;
            }

            return null;
        }
    }
}
