using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Transform value parser for the arbitrary-value dispatch (StyleArbitraryValueResolver).
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

            if (prefix == "flex-")
            {
                return TryParseFlexShorthand(valueSpan, negate, out result);
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

        // A unitless factor, so the float grammar rather than the length one: that grammar converts a
        // suffix instead of rejecting it, and scale-[2rem] would arrive as a 32x scale.
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

        // An angle, which the length grammar rejects outright rather than converting: deg / rad / grad /
        // turn all normalise to degrees here.
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

        // flex-[..] is Tailwind's `flex: <value>`, so it takes the CSS flex shorthand, `_` spelling the spaces:
        // none / auto / initial, or `<grow> <shrink>? || <basis>`. A grow given without a basis takes 0%, a
        // basis given alone takes a grow and shrink of 1, and a missing shrink is 1.
        private static bool TryParseFlexShorthand(ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return false;
            }
            var keyword = FlexKeyword(valueSpan);
            if (keyword != null)
            {
                result = keyword.Value;
                return true;
            }
            var parts = new FlexParts { Grow = float.NaN, Shrink = float.NaN, Unit = LengthUnit.Percent };
            foreach (var part in valueSpan.ToString().Split('_'))
            {
                if (!TakeFlexPart(part.AsSpan(), ref parts))
                {
                    return false;
                }
            }
            result = ArbitraryStyle.Flex(float.IsNaN(parts.Grow) ? 1f : parts.Grow,
                float.IsNaN(parts.Shrink) ? 1f : parts.Shrink, parts.Basis, parts.Unit);
            return true;
        }

        private static ArbitraryStyle? FlexKeyword(ReadOnlySpan<char> value)
        {
            if (value.SequenceEqual("none".AsSpan()))
            {
                return ArbitraryStyle.Flex(0f, 0f, float.NaN, LengthUnit.Pixel);
            }
            if (value.SequenceEqual("auto".AsSpan()))
            {
                return ArbitraryStyle.Flex(1f, 1f, float.NaN, LengthUnit.Pixel);
            }
            if (value.SequenceEqual("initial".AsSpan()))
            {
                return ArbitraryStyle.Flex(0f, 1f, float.NaN, LengthUnit.Pixel);
            }
            return null;
        }

        // The components read so far. Grow and Shrink are NaN until given; PairClosed is set by a basis that
        // follows the grow, after which no shrink may come.
        private struct FlexParts
        {
            public float Grow;
            public float Shrink;
            public float Basis;
            public LengthUnit Unit;
            public bool HasBasis;
            public bool PairClosed;
        }

        // One space-separated component: a number is the grow, or the shrink straight after it; a unitless zero
        // after the pair is the basis, as CSS reads `flex: 1 1 0`; anything else is the basis, which may not be
        // negative.
        private static bool TakeFlexPart(ReadOnlySpan<char> part, ref FlexParts parts)
        {
            if (StyleArbitraryValueResolver.TryParseFloat(part, out var number))
            {
                return TakeFlexNumber(number, ref parts);
            }
            if (parts.HasBasis)
            {
                return false;
            }
            parts.HasBasis = true;
            parts.PairClosed = !float.IsNaN(parts.Grow);
            if (part.SequenceEqual("auto".AsSpan()))
            {
                parts.Basis = float.NaN;
                return true;
            }
            return StyleArbitraryValueResolver.TryParseValue(part, out parts.Basis, out parts.Unit)
                && parts.Basis >= 0f;
        }

        private static bool TakeFlexNumber(float number, ref FlexParts parts)
        {
            if (number < 0f)
            {
                return false;
            }
            if (float.IsNaN(parts.Grow))
            {
                parts.Grow = number;
                return true;
            }
            if (float.IsNaN(parts.Shrink) && !parts.PairClosed)
            {
                parts.Shrink = number;
                return true;
            }
            if (number != 0f || parts.HasBasis)
            {
                return false;
            }
            parts.HasBasis = true;
            parts.Basis = 0f;
            parts.Unit = LengthUnit.Pixel;
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
        // Here rather than in the prefix table because one class writes both components, and what the
        // table builds carries one.
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
