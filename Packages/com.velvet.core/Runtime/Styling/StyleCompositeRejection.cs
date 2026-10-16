#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Velvet
{
    // Tailwind writes translate-*, scale-x-* / scale-y-* and the uniform scale-*, skew-*, and the filter
    // utilities as custom properties that one declaration per family composes (translate: var(--tw-translate-x)
    // var(--tw-translate-y), filter: var(--tw-blur,) var(--tw-brightness,) …). A value the browser rejects in one
    // of them makes that declaration invalid at computed-value time, and every rule of the family reads the same
    // custom property, so the family is gone wherever the rejecting rule applies, as measured in Chromium against
    // Tailwind's own CSS; RejectedCompositeTests pins it. A rejection without a variant applies always, so it
    // takes the family under every variant too. One behind a variant applies while that variant does, so the
    // family's tokens there are replaced by the family's identity values, which outrank the base tokens while the
    // variant holds. A list normalized on its own — a V.Motion pose — reads the families its element's own list
    // rejected without a variant through WithoutFamiliesRejectedIn. The gesture, presence and drag channels add
    // stylesheet classes only, which the inline identity values hold against; RejectedCompositeLayerTests pins
    // both layers.
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

        // The tokens that write each family's identity: what the family's declaration computes to when no
        // utility of it applies.
        private static readonly string[][] s_identity =
        {
            Array.Empty<string>(),
            new[] { "translate-x-[0]", "translate-y-[0]" },
            new[] { "scale-x-[1]", "scale-y-[1]" },
            new[] { "skew-x-[0]", "skew-y-[0]" },
            new[]
            {
                "blur-[0]", "brightness-[1]", "contrast-[1]", "grayscale-[0]", "hue-rotate-[0]", "invert-[0]",
                "saturate-[1]", "sepia-[0]",
            },
        };

        // Each normalized list's families rejected without a variant, as a mask of 1 << Family, for the lists
        // layered over it. Keyed by the array the parse cache hands out for the list, which it keeps stable.
        private static readonly ConditionalWeakTable<string[], StrongBox<int>> s_rejectedEverywhere = new();

        // The mask of families tokens rejects without a variant.
        internal static int RejectedEverywhere(string[] tokens)
        {
            var mask = 0;
            foreach (var token in tokens)
            {
                if (TryRead(token, out var variants, out var family, out _, out var isRejected) && isRejected
                    && variants.Length == 0)
                {
                    mask |= 1 << (int)family;
                }
            }
            return mask;
        }

        // Records mask against normalized, the list the normalizer handed back for tokens it read it from.
        internal static void Remember(string[] normalized, int mask)
        {
            if (mask != 0)
            {
                s_rejectedEverywhere.AddOrUpdate(normalized, new StrongBox<int>(mask));
            }
        }

        // layer without its tokens of a family owner, the list it is layered over, rejected without a variant:
        // that rejection holds the family at its identity under every layer, as a base rule's does in the browser.
        internal static string[] WithoutFamiliesRejectedIn(string[] owner, string[] layer)
        {
            if (!s_rejectedEverywhere.TryGetValue(owner, out var box))
            {
                return layer;
            }
            var mask = box.Value;
            return Array.FindAll(layer, token => !(TryRead(token, out _, out var family, out _, out _)
                && (mask & (1 << (int)family)) != 0));
        }

        // tokens with each family whose tokens hold a value the browser rejects written as that family's identity at
        // the variants where the rejection applies; tokens itself when there is none.
        internal static string[] WithoutRejectedComposites(string[] tokens)
        {
            HashSet<(string Variants, Family Family)>? rejected = null;
            foreach (var token in tokens)
            {
                if (TryRead(token, out var variants, out var family, out _, out var isRejected) && isRejected)
                {
                    (rejected ??= new HashSet<(string, Family)>()).Add((variants, family));
                }
            }
            if (rejected == null)
            {
                return tokens;
            }
            var kept = new List<string>(tokens.Length);
            var written = new HashSet<(string, Family)>();
            foreach (var token in tokens)
            {
                if (!TryRead(token, out var variants, out var family, out var important, out _))
                {
                    kept.Add(token);
                    continue;
                }
                // A base rejection takes the family under every variant; the identity is written once, at the base.
                var at = rejected.Contains((string.Empty, family)) ? string.Empty
                    : rejected.Contains((variants, family)) ? variants
                    : null;
                if (at == null)
                {
                    kept.Add(token);
                }
                else if (written.Add((at, family)))
                {
                    var prefix = (important ? "!" : string.Empty) + at;
                    foreach (var identity in s_identity[(int)family])
                    {
                        kept.Add(prefix + identity);
                    }
                    // A drop shadow is part of the filter, so one elsewhere in the list goes with it.
                    if (family == Family.Filter && Array.Exists(tokens, IsDropShadow))
                    {
                        kept.Add(prefix + "drop-shadow-none");
                    }
                }
            }
            return kept.ToArray();
        }

        private static bool IsDropShadow(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out _);
            var payload = core.Substring(StyleSpacingPrecedence.VariantsLength(core));
            return payload == "drop-shadow" || payload.StartsWith("drop-shadow-", StringComparison.Ordinal);
        }

        private static bool TryRead(string token, out string variants, out Family family, out bool important,
            out bool rejected)
        {
            variants = string.Empty;
            family = Family.None;
            rejected = false;
            important = false;
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }
            var core = StyleArbitraryValueResolver.StripImportant(token, out important);
            var length = StyleSpacingPrecedence.VariantsLength(core);
            var payload = core.Substring(length);
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
            variants = core.Substring(0, length);
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
