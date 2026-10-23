using System;

namespace Velvet
{
    // Parses Velvet's order-* utility classes into the flex order FiberZLayerCoordinator arranges a parent's
    // children by. Follows Tailwind v4's order utility: order-first and order-last (calc(-infinity) and
    // calc(infinity), which CSS clamps to the ends of the integer range), order-none (0), a bare non-negative
    // integer (order-3), a bracketed integer (order-[-2]), a leading "-" negating either of the last two
    // (-order-3, -order-[2]), and either bracket reading a custom property (order-[var(--o)], order-(--o)).
    internal static class StyleOrderClass
    {
        private const string Prefix = "order-";

        // Whether core, with its important bang stripped, is an order-* utility of any spelling, parsed or not.
        internal static bool IsOrderToken(string core)
            => core.StartsWith(Prefix, StringComparison.Ordinal) || core.StartsWith("-" + Prefix, StringComparison.Ordinal);

        // Cheap early-out gate ahead of TryExtract, routed through StripImportant for the reason
        // StyleZIndexClass.HasZIndexClass gives.
        public static bool HasOrderClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls) && IsOrderToken(StyleArbitraryValueResolver.StripImportant(cls, out _)))
                {
                    return true;
                }
            }
            return false;
        }

        // The order the cascade would give: an important token beats every plain one wherever it sits, and within
        // either group the later class wins. False when no order utility parses. winnerClass is the class the
        // value came from.
        public static bool TryExtract(string[] classNames, out int order, out string? winnerClass)
        {
            order = 0;
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
                if (!TryParse(cls, out var parsed, out var bang))
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
            order = winner.GetValueOrDefault();
            return winner.HasValue;
        }

        // The utility an order-* class reading a custom property is read as (StyleCustomPropertyClass.Consumer), for
        // a class with its important bang stripped; null for any other class.
        public static string? VariableUtility(string core)
            => StyleCustomPropertyClass.Consumer(core) is { } utility
                && utility.StartsWith(utility[0] == '-' ? "-order-[" : "order-[", StringComparison.Ordinal)
                ? utility
                : null;

        public static bool TryParse(string cls, out int order) => TryParse(cls, out order, out _);

        // -order-first, -order-last and -order-none are rejected: Tailwind generates nothing for them.
        private static bool TryParse(string cls, out int order, out bool important)
        {
            order = 0;
            cls = StyleArbitraryValueResolver.StripImportant(cls, out important);
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            // Its value is the custom property's, which StyleCustomProperties.TryVariableOrder reads on the element.
            if (VariableUtility(cls) != null)
            {
                return true;
            }
            cls = StyleArbitraryValueResolver.WithoutTypeHint(cls) ?? string.Empty;
            switch (cls)
            {
                case "order-first":
                    order = int.MinValue;
                    return true;
                case "order-last":
                    order = int.MaxValue;
                    return true;
                case "order-none":
                    return true;
            }
            var negate = cls.Length > 0 && cls[0] == '-';
            var offset = negate ? 1 : 0;
            if (cls.Length <= offset + Prefix.Length || string.CompareOrdinal(cls, offset, Prefix, 0, Prefix.Length) != 0)
            {
                return false;
            }
            long value;
            if (StyleArbitraryValueResolver.TryStripBrackets(cls, offset + Prefix.Length, out var inner))
            {
                if (!StyleZIndexClass.TryParseSignedDigits(inner, out value))
                {
                    return false;
                }
            }
            else if (!StyleZIndexClass.TryParseBare(cls.AsSpan(offset + Prefix.Length), out value))
            {
                return false;
            }
            order = (int)Math.Clamp(negate ? -value : value, int.MinValue, int.MaxValue);
            return true;
        }
    }
}
