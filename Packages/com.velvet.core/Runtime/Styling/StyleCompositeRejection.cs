#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // Tailwind writes translate-*, scale-x-* / scale-y-* and the uniform scale-*, skew-*, and the filter
    // utilities as custom properties that one declaration per family composes (translate: var(--tw-translate-x)
    // var(--tw-translate-y), filter: var(--tw-blur,) var(--tw-brightness,) …). A value the browser rejects in one
    // of them makes that declaration invalid at computed-value time, so the family's other utilities go with it, as
    // measured in Chromium against Tailwind's own CSS; RejectedCompositeTests pins the families. A family is judged
    // within the tokens of one variant prefix and importance, the scope its declaration is written at.
    internal static class StyleCompositeRejection
    {
        private enum Family
        {
            None,
            Translate,
            Scale,
            Transform,
            Filter,
        }

        // tokens without every token of a family one of whose tokens in the same variant set and importance holds a
        // value the browser rejects; tokens itself when there is none.
        internal static string[] WithoutRejectedComposites(string[] tokens)
        {
            HashSet<(string Scope, Family Family)>? rejected = null;
            foreach (var token in tokens)
            {
                if (TryRead(token, out var scope, out var family, out var isRejected) && isRejected)
                {
                    (rejected ??= new HashSet<(string, Family)>()).Add((scope, family));
                }
            }
            if (rejected == null)
            {
                return tokens;
            }
            var kept = new List<string>(tokens.Length);
            foreach (var token in tokens)
            {
                if (!(TryRead(token, out var scope, out var family, out _) && rejected.Contains((scope, family))))
                {
                    kept.Add(token);
                }
            }
            return kept.ToArray();
        }

        private static bool TryRead(string token, out string scope, out Family family, out bool rejected)
        {
            scope = string.Empty;
            family = Family.None;
            rejected = false;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            var variants = StyleSpacingPrecedence.VariantsLength(core);
            var payload = core.Substring(variants);
            if (!StyleArbitraryValueResolver.TryDropTypeHint(payload, out payload))
            {
                return false;
            }
            var body = payload.Length > 0 && payload[0] == '-' ? payload.Substring(1) : payload;
            family = FamilyOf(body, out var function);
            if (family == Family.None)
            {
                return false;
            }
            scope = (important ? "!" : string.Empty) + core.Substring(0, variants);
            var open = body.IndexOf('[');
            if (open > 0 && body[body.Length - 1] == ']')
            {
                var value = body.Substring(open + 1, body.Length - open - 2).Replace('_', ' ').Trim();
                rejected = !Accepts(function, value);
            }
            return true;
        }

        // The family a utility's body belongs to, and the CSS function or longhand its bracket is written into.
        private static Family FamilyOf(string body, out string function)
        {
            function = string.Empty;
            if (body.StartsWith("translate-", StringComparison.Ordinal))
            {
                function = "translate";
                return Family.Translate;
            }
            if (body.StartsWith("scale-x-", StringComparison.Ordinal) || body.StartsWith("scale-y-", StringComparison.Ordinal)
                || body.StartsWith("scale-z-", StringComparison.Ordinal))
            {
                function = "scale";
                return Family.Scale;
            }
            // A bracketed scale-[…] is written as scale itself, beside no other utility.
            if (body.StartsWith("scale-", StringComparison.Ordinal) && body[body.Length - 1] != ']')
            {
                function = "scale";
                return Family.Scale;
            }
            if (body.StartsWith("skew-", StringComparison.Ordinal) || body.StartsWith("rotate-x-", StringComparison.Ordinal)
                || body.StartsWith("rotate-y-", StringComparison.Ordinal) || body.StartsWith("rotate-z-", StringComparison.Ordinal))
            {
                function = "angle";
                return Family.Transform;
            }
            foreach (var filter in s_filters)
            {
                if (body == filter || body.StartsWith(filter + "-", StringComparison.Ordinal))
                {
                    function = filter;
                    return Family.Filter;
                }
            }
            return Family.None;
        }

        private static readonly string[] s_filters =
        {
            "blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "saturate", "sepia", "drop-shadow",
        };

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
                "translate" => IsZero(value) || IsDimension(value, TailwindDataTypes.s_lengthUnits) || IsPercentage(value),
                "scale" => IsNumber(value) || IsPercentage(value),
                "angle" or "hue-rotate" => IsZero(value) || IsDimension(value, s_angleUnits),
                "blur" => !value.StartsWith("-", StringComparison.Ordinal)
                    && (IsZero(value) || IsDimension(value, TailwindDataTypes.s_lengthUnits)),
                // A drop shadow is read by its own grammar, which this does not repeat.
                "drop-shadow" => true,
                _ => !value.StartsWith("-", StringComparison.Ordinal) && (IsNumber(value) || IsPercentage(value)),
            };
        }

        private static readonly string[] s_angleUnits = { "deg", "grad", "rad", "turn" };

        private static bool IsNumber(string value)
            => TailwindDataTypes.NumberEnd(value, withExponent: true) == value.Length
                || TailwindDataTypes.NumberEnd(value, withExponent: false) == value.Length;

        private static bool IsZero(string value) => IsNumber(value) && float.TryParse(value,
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
