#nullable enable
using System;

namespace Velvet
{
    // Tailwind writes translate-*, scale-x-* / scale-y-*, skew-x-* / skew-y-* and the filter
    // utilities as custom properties that one declaration per family composes (translate: var(--tw-translate-x)
    // var(--tw-translate-y), filter: var(--tw-blur,) var(--tw-brightness,) …). A value the browser rejects in one
    // of them makes that declaration invalid at computed-value time once it holds its property, so the family draws
    // nothing. Which value holds is the cascade's question, answered where Velvet answers it for every value: a
    // rejected value is read as a layer of its own (ArbitraryStyle.Rejected), ranked as any value of its property,
    // and the family's composer draws nothing while one holds (StyleArbitraryValueResolver's translate, scale and
    // filter composers, and StyleSkewClass for skew). RejectedCompositeTests pins the families.
    internal static class StyleCompositeRejection
    {
        // The utilities whose bracket a family composes, by prefix: the property its layer holds, and the CSS
        // function or longhand the value is written into. A sign is read where Tailwind negates the utility.
        private static readonly (string Prefix, ArbitraryProperty Property, string Function, bool Negates)[] s_composed =
        {
            ("translate-x-", ArbitraryProperty.TranslateX, "translate", true),
            ("translate-y-", ArbitraryProperty.TranslateY, "translate", true),
            ("translate-", ArbitraryProperty.Translate, "translate", true),
            ("scale-x-", ArbitraryProperty.ScaleX, "scale", true),
            ("scale-y-", ArbitraryProperty.ScaleY, "scale", true),
            ("blur-", ArbitraryProperty.FilterBlur, "blur", false),
            ("brightness-", ArbitraryProperty.FilterBrightness, "brightness", false),
            ("contrast-", ArbitraryProperty.FilterContrast, "contrast", false),
            ("grayscale-", ArbitraryProperty.FilterGrayscale, "grayscale", false),
            ("hue-rotate-", ArbitraryProperty.FilterHueRotate, "hue-rotate", true),
            ("invert-", ArbitraryProperty.FilterInvert, "invert", false),
            ("saturate-", ArbitraryProperty.FilterSaturate, "saturate", false),
            ("sepia-", ArbitraryProperty.FilterSepia, "sepia", false),
            ("drop-shadow-", ArbitraryProperty.FilterDropShadow, "drop-shadow", false),
        };

        // Whether core, a utility without variants or important modifier, writes a bracket the browser rejects
        // into a family Tailwind composes, and the property it holds. Read on every class a layer is parsed for,
        // so nothing is allocated before a prefix matches.
        internal static bool TryReadRejected(string core, out ArbitraryProperty property)
        {
            property = default;
            var negated = core.Length > 0 && core[0] == '-';
            foreach (var (prefix, composed, function, negates) in s_composed)
            {
                if (OpensBracket(core, negated ? 1 : 0, prefix))
                {
                    property = composed;
                    return (negates || !negated) && TryReadValue(core, negated, out var value) && !Accepts(function, value);
                }
            }
            return false;
        }

        // Whether cls is a skew-x-[…] or skew-y-[…] whose angle the browser rejects, and which axis it holds.
        internal static bool RejectsSkew(string cls, out bool isX)
        {
            var negated = cls.Length > 0 && cls[0] == '-';
            isX = OpensBracket(cls, negated ? 1 : 0, "skew-x-");
            return (isX || OpensBracket(cls, negated ? 1 : 0, "skew-y-"))
                && TryReadValue(cls, negated, out var value) && !Accepts("angle", value);
        }

        // Whether core holds prefix and a bracket from at.
        private static bool OpensBracket(string core, int at, string prefix)
            => core.Length > at + prefix.Length && core[at + prefix.Length] == '['
                && string.CompareOrdinal(core, at, prefix, 0, prefix.Length) == 0;

        // The bracket's value as CSS reads it, without the type hint.
        private static bool TryReadValue(string core, bool negated, out string value)
        {
            value = string.Empty;
            if (core[core.Length - 1] != ']'
                || !StyleArbitraryValueResolver.TryDropTypeHint(negated ? core.Substring(1) : core, out var body))
            {
                return false;
            }
            var open = body.IndexOf('[');
            value = body.Substring(open + 1, body.Length - open - 2).Replace('_', ' ').Trim();
            return true;
        }

        // Whether the browser takes value where Tailwind writes it for function. A value only a math function or a
        // var() spells is taken, since nothing here reads one to the end.
        private static bool Accepts(string function, string value)
        {
            if (value.IndexOf('(') >= 0)
            {
                return true;
            }
            return function switch
            {
                "translate" => TakesLengthPercentage(value),
                "scale" => IsNumber(value) || IsPercentage(value),
                "angle" or "hue-rotate" => IsZero(value) || IsDimension(value, s_angleUnits),
                "blur" => !value.StartsWith("-", StringComparison.Ordinal) && (IsZero(value) || IsLength(value)),
                // A colour is the drop-shadow-color utility rather than a shadow (Tailwind's inferDataType).
                "drop-shadow" => TailwindDataTypes.IsColor(value.AsSpan()) || IsDropShadowList(value),
                _ => !value.StartsWith("-", StringComparison.Ordinal) && (IsNumber(value) || IsPercentage(value)),
            };
        }

        // Whether the browser takes value inside drop-shadow(), one shadow per comma: two or three lengths in a run, the
        // third not negative, and at most one colour before or after them.
        private static bool IsDropShadowList(string value)
        {
            foreach (var shadow in value.Split(','))
            {
                var tokens = shadow.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var colourFirst = tokens.Length > 0 && !IsShadowLength(tokens[0]);
                var start = colourFirst ? 1 : 0;
                var end = start;
                while (end < tokens.Length && IsShadowLength(tokens[end]))
                {
                    end++;
                }
                var lengths = end - start;
                var trailing = tokens.Length - end;
                if (lengths is < 2 or > 3 || trailing + (colourFirst ? 1 : 0) > 1
                    || (lengths == 3 && tokens[start + 2].StartsWith("-", StringComparison.Ordinal))
                    || (colourFirst && !TailwindDataTypes.IsColor(tokens[0].AsSpan()))
                    || (trailing == 1 && !TailwindDataTypes.IsColor(tokens[end].AsSpan())))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsShadowLength(string token) => IsZero(token) || IsLength(token);

        // Whether the browser takes value as a <length-percentage>, a function taken as Accepts takes one.
        internal static bool TakesLengthPercentage(string value)
            => value.IndexOf('(') >= 0 || IsZero(value) || IsLength(value) || IsPercentage(value);

        internal static bool IsLength(string value) => IsDimension(value, TailwindDataTypes.s_lengthUnits);

        private static readonly string[] s_angleUnits = { "deg", "grad", "rad", "turn" };

        private static bool IsNumber(string value)
            => TailwindDataTypes.NumberEnd(value, withExponent: true) == value.Length
                || TailwindDataTypes.NumberEnd(value, withExponent: false) == value.Length;

        internal static bool IsZero(string value) => IsNumber(value) && float.TryParse(value,
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && n == 0f;

        private static bool IsPercentage(string value)
            => value.Length > 1 && value[value.Length - 1] == '%' && IsNumber(value.Substring(0, value.Length - 1));

        // A number followed by one of units, in any case, as CSS reads a unit.
        private static bool IsDimension(string value, string[] units)
            => EndsInUnit(value, units, withExponent: true) || EndsInUnit(value, units, withExponent: false);

        private static bool EndsInUnit(string value, string[] units, bool withExponent)
        {
            var end = TailwindDataTypes.NumberEnd(value, withExponent);
            return end > 0
                && Array.Exists(units, unit => string.Equals(value.Substring(end), unit, StringComparison.OrdinalIgnoreCase));
        }
    }
}
