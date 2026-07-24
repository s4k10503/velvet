using System.Globalization;

namespace Velvet
{
    // Parses Velvet's z-* utility classes into a resolved stacking value for FiberZLayerCoordinator. Mirrors
    // StyleGapClass/StyleGridClass: a cheap prefix-scan gate (HasZIndexClass) before the full TryExtract parse.
    // Supports the fixed named scale (z-0/10/20/30/40/50), its negated form (-z-10 … -z-50, the
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

        // Scans classNames for the last z-* utility (later classes win, matching CSS cascade order) and
        // returns its resolved value. Returns false when no z utility is present or every z-looking token
        // failed to parse (e.g. a user class that merely starts with "z-").
        public static bool TryExtract(string[] classNames, out int z)
        {
            z = 0;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var parsed))
                {
                    z = parsed;
                    found = true;
                }
            }
            return found;
        }

        // z-0/10/20/30/40/50 (the fixed named scale), -z-0/10/20/30/40/50 (the negated form), or
        // z-[<int>] (arbitrary, the bracket's own sign — z-[-5] is how a negative arbitrary value is spelled,
        // not -z-[5]). Anything else (including a non-numeric bracket, or a bare "z-" prefix that is not one
        // of these three shapes) returns false.
        public static bool TryParse(string cls, out int z)
        {
            z = 0;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            // Stripped first (house convention — see StyleFontClass.IsArbitraryFontClass / StyleTextEffectClass's
            // own leading-* parse / MotionSpringClassParser.TryParseAxisValue): "!z-10", "z-10!", "!z-[5]", and
            // "z-[5]!" all classify like their bare form. The bang itself stays a no-op here regardless — z-index
            // is a physical relocation, not a style-cascade layer !important arbitrates (StripImportant's own
            // Scope comment already documents this for the array-scanned utility families) — this only makes the
            // parser recognize the four bang'd spellings instead of silently dropping the element from z
            // management. A dash embedded AFTER a leading "-" (e.g. "-!z-10") is not a shape StripImportant
            // recognizes (it only strips a bang at the very first or very last character), so that stays rejected.
            cls = StyleArbitraryValueResolver.StripImportant(cls, out _);
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            if (cls.Length > 3 && cls[0] == 'z' && cls[1] == '-' && cls[2] == '['
                && cls[cls.Length - 1] == ']')
            {
                var inner = cls.Substring(3, cls.Length - 4);
                return inner.Length > 0
                    && int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out z);
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

        // The fixed non-arbitrary levels the z-index utility scale defines. Anything outside this set needs
        // the z-[N] bracket form (z-15 is not a thing, z-[15] is).
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
