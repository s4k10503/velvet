using System.Globalization;

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
            int value;
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, offset + 2, out var inner))
            {
                if (!int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    return false;
                }
            }
            else if (!TryBareInteger(cls.Substring(offset + 2), out value))
            {
                return false;
            }
            z = negate ? -value : value;
            return true;
        }

        // Tailwind's isPositiveInteger: the digits must read back as the number they parse to, so z-07 is not a
        // bare value.
        private static bool TryBareInteger(string suffix, out int value)
        {
            value = 0;
            return !(suffix.Length > 1 && suffix[0] == '0')
                && int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
