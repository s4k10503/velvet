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

        // CSS keywords ignore ASCII case. flex is not inherited and carries no user-agent value, so unset and
        // revert both land on initial.
        private static ArbitraryStyle? FlexKeyword(ReadOnlySpan<char> value)
        {
            if (IsKeyword(value, "none"))
            {
                return ArbitraryStyle.Flex(0f, 0f, float.NaN, LengthUnit.Pixel);
            }
            if (IsKeyword(value, "auto"))
            {
                return ArbitraryStyle.Flex(1f, 1f, float.NaN, LengthUnit.Pixel);
            }
            if (IsKeyword(value, "initial") || IsKeyword(value, "unset") || IsKeyword(value, "revert"))
            {
                return ArbitraryStyle.Flex(0f, 1f, float.NaN, LengthUnit.Pixel);
            }
            return null;
        }

        private static bool IsKeyword(ReadOnlySpan<char> value, string keyword)
            => value.Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase);

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
            if (IsKeyword(part, "auto"))
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

        // CSS transform-origin, with the underscore standing for the space: one component, or an x and a
        // y followed by an optional z length. The negation prefix is refused because Tailwind declares no
        // negative variant of this utility, and nothing is lost by it: a minus inside the brackets reaches
        // the value grammar intact, so origin-[-10px] and origin-[-10px_-20px] both parse.
        private static bool TryParseTransformOrigin(ReadOnlySpan<char> valueSpan, bool negate,
            out ArbitraryStyle result)
        {
            result = default;
            if (negate) return false;

            var first = valueSpan.IndexOf('_');
            if (first == -1)
            {
                if (!TryParseOriginComponent(valueSpan, out var only)) return false;
                // A lone vertical keyword is the y, and CSS leaves the other axis at 50% — not at the stated
                // value, which would make origin-[0px] the top-left corner instead of the left edge's middle.
                result = only.Kind == OriginKind.Vertical
                    ? new ArbitraryStyle(ArbitraryProperty.TransformOrigin, 50f, LengthUnit.Percent, only.Value, only.Unit)
                    : new ArbitraryStyle(ArbitraryProperty.TransformOrigin, only.Value, only.Unit, 50f, LengthUnit.Percent);
                return true;
            }

            var rest = valueSpan[(first + 1)..];
            var second = rest.IndexOf('_');
            var z = 0f;
            if (second != -1)
            {
                // The z is a length and never a percentage or a keyword; a fourth component fails its parse.
                if (!StyleArbitraryValueResolver.TryParseValue(rest[(second + 1)..], out z, out var zUnit))
                {
                    return false;
                }
                if (zUnit == LengthUnit.Percent)
                {
                    return false;
                }
                rest = rest[..second];
            }

            if (!TryParseOriginComponent(valueSpan[..first], out var a))
            {
                return false;
            }
            if (!TryParseOriginComponent(rest, out var b))
            {
                return false;
            }

            if (a.Kind != OriginKind.Vertical && b.Kind != OriginKind.Horizontal)
            {
                result = new ArbitraryStyle(ArbitraryProperty.TransformOrigin, a.Value, a.Unit, b.Value, b.Unit, z);
                return true;
            }

            // The swapped order is CSS's keyword-only pair (top left); a length on either side of it is not.
            if (a.Kind == OriginKind.Length || b.Kind == OriginKind.Length
                || a.Kind == OriginKind.Horizontal || b.Kind == OriginKind.Vertical)
            {
                return false;
            }
            result = new ArbitraryStyle(ArbitraryProperty.TransformOrigin, b.Value, b.Unit, a.Value, a.Unit, z);
            return true;
        }

        private enum OriginKind
        {
            Length,
            Center,
            Horizontal,
            Vertical,
        }

        private readonly struct OriginComponent
        {
            public OriginComponent(OriginKind kind, float value, LengthUnit unit)
            {
                Kind = kind;
                Value = value;
                Unit = unit;
            }

            public OriginKind Kind { get; }
            public float Value { get; }
            public LengthUnit Unit { get; }
        }

        private static readonly (string Keyword, OriginKind Kind, float Percent)[] s_originKeywords =
        {
            ("left", OriginKind.Horizontal, 0f),
            ("right", OriginKind.Horizontal, 100f),
            ("top", OriginKind.Vertical, 0f),
            ("bottom", OriginKind.Vertical, 100f),
            ("center", OriginKind.Center, 50f),
        };

        private static bool TryParseOriginComponent(ReadOnlySpan<char> span, out OriginComponent component)
        {
            foreach (var (keyword, kind, percent) in s_originKeywords)
            {
                if (span.SequenceEqual(keyword.AsSpan()))
                {
                    component = new OriginComponent(kind, percent, LengthUnit.Percent);
                    return true;
                }
            }
            if (!StyleArbitraryValueResolver.TryParseValue(span, out var value, out var unit))
            {
                component = default;
                return false;
            }
            component = new OriginComponent(OriginKind.Length, value, unit);
            return true;
        }
    }
}
