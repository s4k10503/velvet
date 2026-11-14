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
        internal static bool IsOrderToken(string core) => StyleIntegerUtility.IsToken(core, Prefix);

        // Cheap early-out gate ahead of TryExtract.
        public static bool HasOrderClass(string[] classNames) => StyleIntegerUtility.HasToken(classNames, Prefix);

        // The order the cascade would give: an important token beats every plain one wherever it sits, and within
        // either group the later class wins. False when no order utility parses. winnerClass is the class the
        // value came from.
        public static bool TryExtract(string[] classNames, out int order, out string? winnerClass)
            => StyleIntegerUtility.TryExtract(classNames, s_parse, out order, out winnerClass);

        private static readonly StyleIntegerUtility.Parse s_parse = Parse;

        private static bool Parse(string cls, out int? order, out bool important)
        {
            var parsed = TryParse(cls, out var value, out important);
            order = parsed ? value : null;
            return parsed;
        }

        // The utility an order-* class reading a custom property is read as, for a class with its important bang
        // stripped; null for any other class.
        public static string? VariableUtility(string core)
            => StyleIntegerUtility.VariableUtility(core, "order-[", "-order-[");

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
            return StyleIntegerUtility.TryParse(cls, Prefix, out order);
        }
    }
}
