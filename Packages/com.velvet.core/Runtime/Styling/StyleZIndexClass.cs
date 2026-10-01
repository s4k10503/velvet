using System.Globalization;

namespace Velvet
{
    // Parses Velvet's z-* utility classes into a resolved stacking value for FiberZLayerCoordinator. Mirrors
    // StyleGapClass/StyleGridClass: a cheap prefix-scan gate (HasZIndexClass) before the full TryExtract parse.
    // Supports z-auto, the fixed named scale (z-0/10/20/30/40/50), its negated form (-z-10 … -z-50, the
    // sign prefixes the whole class), and the arbitrary bracket form (z-[999],
    // z-[-5] — the bracket already carries a signed integer, so no outer "-" is recognized for it: z-index has
    // no separate magnitude/direction split the way a length utility does).
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

        // z-auto, z-0/10/20/30/40/50 (the fixed named scale), -z-0/10/20/30/40/50 (the negated form), or
        // z-[<int>] (arbitrary, the bracket's own sign — z-[-5] is how a negative arbitrary value is spelled,
        // not -z-[5]). Anything else (including a non-numeric bracket, or a bare "z-" prefix that is not one
        // of these shapes) returns false.
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

            // TryStripBrackets goes first (it self-guards on length, so it is safe to evaluate against any
            // cls) and its success implies cls.Length >= 5, which is what makes the cls[0]/cls[1] reads below
            // safe without a separate bounds check.
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, 2, out var inner) && cls[0] == 'z' && cls[1] == '-')
            {
                var parsed = int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
                z = value;
                return parsed;
            }

            var negate = cls[0] == '-';
            var offset = negate ? 1 : 0;
            if (cls.Length <= offset + 2 || cls[offset] != 'z' || cls[offset + 1] != '-')
            {
                return false;
            }
            if (!TryNamedLevel(cls.Substring(offset + 2), out var level))
            {
                return false;
            }
            z = negate ? -level : level;
            return true;
        }

        // Tailwind v3's fixed levels, the same fixed-scale choice rotate-* and the spacing utilities make; any
        // other integer is spelled z-[N].
        private static bool TryNamedLevel(string suffix, out int level)
        {
            switch (suffix)
            {
                case "0": level = 0; return true;
                case "10": level = 10; return true;
                case "20": level = 20; return true;
                case "30": level = 30; return true;
                case "40": level = 40; return true;
                case "50": level = 50; return true;
                default: level = 0; return false;
            }
        }
    }
}
