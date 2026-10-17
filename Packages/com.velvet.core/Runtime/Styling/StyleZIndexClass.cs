using System;
using System.Globalization;
using System.Numerics;

namespace Velvet
{
    // Parses Velvet's z-* utility classes into a resolved stacking value for FiberZLayerCoordinator. Mirrors
    // StyleGapClass/StyleGridClass: a cheap prefix-scan gate (HasZIndexClass) before the full TryExtract parse.
    // Follows Tailwind v4's z utility: z-auto, a bare non-negative integer (z-15), a bracketed integer (z-[-5]),
    // and a leading "-" negating either of the last two (-z-15, -z-[5]).
    internal static class StyleZIndexClass
    {
        // Cheap early-out gate: true when ANY class looks like a z-* utility (after stripping a leading "-").
        // Routed through StripImportant first (mirrors StyleFontClass.IsArbitraryFontClass) so this gate agrees
        // with TryParse on what counts as "a z-* utility" — otherwise "!z-10" would fail this early-out and
        // TryExtract would never even attempt it, silently leaving an important-modified z-* class unclassified.
        public static bool HasZIndexClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                if (string.IsNullOrEmpty(core))
                {
                    continue;
                }
                var offset = core[0] == '-' ? 1 : 0;
                if (core.Length >= offset + 2 && core[offset] == 'z' && core[offset + 1] == '-')
                {
                    return true;
                }
            }
            return false;
        }

        // Resolves the z-* value the cascade would: an important token (!z-10 / z-10!) beats every plain one
        // wherever it sits, and within either group the later class wins. Returns false when no z utility is
        // present, when every z-looking token failed to parse (e.g. a user class that merely starts with
        // "z-"), or when the winner is z-auto, which leaves the element unstacked.
        public static bool TryExtract(string[] classNames, out int z)
        {
            z = 0;
            if (classNames == null)
            {
                return false;
            }

            // A null value is z-auto, so whether an important token was seen is tracked apart from its value.
            int? plain = null;
            int? important = null;
            var anyImportant = false;
            foreach (var cls in classNames)
            {
                if (!TryParse(cls, out var parsed, out var bang))
                {
                    continue;
                }
                if (bang)
                {
                    important = parsed;
                    anyImportant = true;
                }
                else
                {
                    plain = parsed;
                }
            }
            var winner = anyImportant ? important : plain;
            z = winner.GetValueOrDefault();
            return winner.HasValue;
        }

        // A null z is z-auto.
        public static bool TryParse(string cls, out int? z) => TryParse(cls, out z, out _);

        // -z-auto is rejected: Tailwind generates nothing for it.
        private static bool TryParse(string cls, out int? z, out bool important)
        {
            z = null;
            // A dash embedded AFTER a leading "-" (e.g. "-!z-10") is not a shape StripImportant recognizes (it
            // only strips a bang at the very first or very last character), so that stays rejected.
            cls = StyleArbitraryValueResolver.StripImportant(cls, out important);
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            if (cls == "z-auto")
            {
                return true;
            }

            var negate = cls[0] == '-';
            var offset = negate ? 1 : 0;
            if (cls.Length <= offset + 2 || cls[offset] != 'z' || cls[offset + 1] != '-')
            {
                return false;
            }
            long value;
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, offset + 2, out var inner))
            {
                if (!TryParseSignedDigits(inner, out value))
                {
                    return false;
                }
            }
            else if (!TryParseBare(cls.AsSpan(offset + 2), out value))
            {
                return false;
            }
            // CSS clamps an integer beyond the range it supports, so such a value stacks at that end of it.
            z = (int)Math.Clamp(negate ? -value : value, int.MinValue, int.MaxValue);
            return true;
        }

        // Tailwind's isPositiveInteger: the digits must print back unchanged through a JavaScript number, so a
        // leading zero, a sign and a fraction are declined, and so is a long run the number prints differently.
        private static bool TryParseBare(ReadOnlySpan<char> digits, out long value)
        {
            if ((digits.Length > 1 && digits[0] == '0') || !TryParseDigits(digits, out value))
            {
                value = 0;
                return false;
            }
            // Every integer of up to 15 digits is exact in a double, so only a longer run needs the round trip.
            // MUTANT_SURVIVES(equivalent, boundary): a 15-digit run prints back through a double unchanged.
            return digits.Length <= 15 || PrintsBackThroughADouble(digits);
        }

        // ECMA-262's Number::toString prints a number at or above 1e21 in exponent form, and one below it as the
        // shortest run of significant digits that reads back as the same double (the closer one, then the even
        // one, when two qualify), padded with zeros. Below 2^53 every integer is a double, so that run is the
        // integer itself.
        private static bool PrintsBackThroughADouble(ReadOnlySpan<char> digits)
        {
            if (!double.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number >= 1e21)
            {
                return false;
            }
            var exact = new BigInteger(number);
            var written = BigInteger.Parse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
            // MUTANT_SURVIVES(equivalent, boundary): 2^53 itself prints as its own digits too.
            if (number < 9007199254740992.0)
            {
                return exact == written;
            }
            var bits = BitConverter.DoubleToInt64Bits(number);
            var mantissa = (bits & 0xFFFFFFFFFFFFFL) | (1L << 52);
            var gapAbove = BigInteger.Pow(2, (int)((bits >> 52) & 0x7FF) - 1075);
            // The double below a power of two sits half as far away as the one above it.
            var gapBelow = mantissa == 1L << 52 ? gapAbove / 2 : gapAbove;
            var even = (mantissa & 1) == 0;
            for (var length = 1; ; length++)
            {
                var candidate = RoundToSignificantDigits(exact, length);
                var twiceOffset = 2 * (candidate - exact);
                // MUTANT_SURVIVES(equivalent, boundary): a zero offset reads back against either gap.
                var gap = twiceOffset.Sign >= 0 ? gapAbove : gapBelow;
                var distance = BigInteger.Abs(twiceOffset);
                if (distance < gap || (distance == gap && even))
                {
                    return candidate == written;
                }
            }
        }

        // Rounds half to even, as Number::toString picks the even run of two equally close ones.
        private static BigInteger RoundToSignificantDigits(BigInteger value, int length)
        {
            var excess = value.ToString(CultureInfo.InvariantCulture).Length - length;
            // MUTANT_SURVIVES(equivalent, boundary): at no excess the scale below is 1 and the value comes back whole.
            if (excess <= 0)
            {
                return value;
            }
            var scale = BigInteger.Pow(10, excess);
            var quotient = BigInteger.DivRem(value, scale, out var remainder);
            var twiceRemainder = 2 * remainder;
            // MUTANT_SURVIVES(equivalent, boundary): at a remainder of half the scale the gap is below it, so neither rounding reads back.
            if (twiceRemainder > scale || (twiceRemainder == scale && !quotient.IsEven))
            {
                quotient += 1;
            }
            return quotient * scale;
        }

        // A CSS <integer>: an optional sign, then digits.
        private static bool TryParseSignedDigits(ReadOnlySpan<char> text, out long value)
        {
            var sign = text.IsEmpty ? '\0' : text[0];
            var negative = sign == '-';
            var signed = negative || sign == '+';
            if (!TryParseDigits(signed ? text.Slice(1) : text, out value))
            {
                return false;
            }
            value = negative ? -value : value;
            return true;
        }

        // A non-empty run of ASCII digits, saturating once it leaves the 32-bit range.
        private static bool TryParseDigits(ReadOnlySpan<char> text, out long value)
        {
            value = 0;
            if (text.IsEmpty)
            {
                return false;
            }
            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                {
                    value = 0;
                    return false;
                }
                if (value <= int.MaxValue)
                {
                    value = (value * 10) + (c - '0');
                }
            }
            return true;
        }
    }
}
