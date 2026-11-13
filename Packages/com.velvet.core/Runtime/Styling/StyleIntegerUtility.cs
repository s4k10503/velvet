#nullable enable
using System;

namespace Velvet
{
    // What StyleZIndexClass and StyleOrderClass share: both read `<prefix><integer>`, optionally negated, the
    // integer bare or bracketed, with the cascade's important-beats-plain, later-beats-earlier winner.
    internal static class StyleIntegerUtility
    {
        // Whether core, with its important bang stripped, is `prefix` or `-prefix` followed by anything.
        internal static bool IsToken(string core, string prefix)
        {
            var offset = core.Length > 0 && core[0] == '-' ? 1 : 0;
            return core.Length >= offset + prefix.Length
                && string.CompareOrdinal(core, offset, prefix, 0, prefix.Length) == 0;
        }

        // The cheap early-out gate ahead of a class's TryExtract. Routed through StripImportant so it agrees with
        // the parse on what counts as the utility: otherwise "!z-10" would fail this gate and never be attempted.
        internal static bool HasToken(string[]? classNames, string prefix)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls) && IsToken(StyleArbitraryValueResolver.StripImportant(cls, out _), prefix))
                {
                    return true;
                }
            }
            return false;
        }

        // The utility a class reading a custom property is read as (StyleCustomPropertyClass.Consumer), for a class
        // with its important bang stripped; null for any other class. bracketed and negatedBracketed are the
        // utility's spelling up to its opening bracket, as "z-[" and "-z-[".
        internal static string? VariableUtility(string core, string bracketed, string negatedBracketed)
            => StyleCustomPropertyClass.Consumer(core) is { } utility
                && utility.StartsWith(utility[0] == '-' ? negatedBracketed : bracketed, StringComparison.Ordinal)
                ? utility
                : null;

        // The integer after `prefix`, bare or bracketed, negated by a leading "-". CSS clamps an integer beyond the
        // range it supports, so such a value lands at that end of it.
        internal static bool TryParse(string cls, string prefix, out int value)
        {
            value = 0;
            var negate = cls.Length > 0 && cls[0] == '-';
            var offset = negate ? 1 : 0;
            if (cls.Length <= offset + prefix.Length || string.CompareOrdinal(cls, offset, prefix, 0, prefix.Length) != 0)
            {
                return false;
            }
            long parsed;
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, offset + prefix.Length, out var inner))
            {
                if (!StyleZIndexClass.TryParseSignedDigits(inner, out parsed))
                {
                    return false;
                }
            }
            else if (!StyleZIndexClass.TryParseBare(cls.AsSpan(offset + prefix.Length), out parsed))
            {
                return false;
            }
            value = (int)Math.Clamp(negate ? -parsed : parsed, int.MinValue, int.MaxValue);
            return true;
        }

        internal delegate bool Parse(string cls, out int? value, out bool important);

        // The value the cascade gives a class list: an important token beats every plain one wherever it sits, and
        // within either group the later class wins. A token that parses to a null value (z-auto) still wins and
        // leaves nothing to stack, so this is false for it. winnerClass is the class the value came from, null where
        // nothing parsed.
        internal static bool TryExtract(string[]? classNames, Parse parse, out int value, out string? winnerClass)
        {
            value = 0;
            winnerClass = null;
            if (classNames == null)
            {
                return false;
            }
            int? plain = null;
            int? important = null;
            string? plainClass = null;
            string? importantClass = null;
            foreach (var cls in classNames)
            {
                if (!parse(cls, out var parsed, out var bang))
                {
                    continue;
                }
                if (bang)
                {
                    (important, importantClass) = (parsed, cls);
                }
                else
                {
                    (plain, plainClass) = (parsed, cls);
                }
            }
            var winner = importantClass != null ? important : plain;
            winnerClass = importantClass ?? plainClass;
            value = winner.GetValueOrDefault();
            return winner.HasValue;
        }
    }
}
