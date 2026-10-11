#nullable enable
namespace Velvet
{
    // Tailwind's per-side border colors (border-b-red-500, border-x-[#fff], border-s-white/50). The logical
    // families read inline-start as left and block-start as top, as StyleLogicalUtilities does.
    //
    // They resolve inline for the reason StyleLogicalUtilities gives for its own families, so a palette name
    // comes from VelvetPalette rather than from the --color-* token border-{color} reads.
    // PaletteStyleSheetMirrorTests holds the two tables to one value.
    internal static class StyleBorderSideColor
    {
        private readonly struct Family
        {
            public readonly string Name;
            public readonly ArbitraryProperty Property;
            // The property Tailwind declares, by which StyleRuleOrder places the token: a logical family's is
            // not the physical edge it resolves to.
            public readonly string SortProperty;

            public Family(string name, ArbitraryProperty property, string sortProperty)
            {
                Name = name;
                Property = property;
                SortProperty = sortProperty;
            }
        }

        private static readonly Family[] s_families =
        {
            new("border-t", ArbitraryProperty.BorderTopColor, "border-top-color"),
            new("border-r", ArbitraryProperty.BorderRightColor, "border-right-color"),
            new("border-b", ArbitraryProperty.BorderBottomColor, "border-bottom-color"),
            new("border-l", ArbitraryProperty.BorderLeftColor, "border-left-color"),
            new("border-x", ArbitraryProperty.BorderXColor, "border-inline-color"),
            new("border-y", ArbitraryProperty.BorderYColor, "border-block-color"),
            new("border-s", ArbitraryProperty.BorderInlineStartColor, "border-inline-start-color"),
            new("border-e", ArbitraryProperty.BorderInlineEndColor, "border-inline-end-color"),
            new("border-bs", ArbitraryProperty.BorderBlockStartColor, "border-block-start-color"),
            new("border-be", ArbitraryProperty.BorderBlockEndColor, "border-block-end-color"),
        };

        // The shorthand border-[#fff] and border-red-500/50 declare border-color too; they reach CompareCascade
        // through ArbitraryProperty.BorderColor, which no family here resolves to.
        private const string ShorthandSortProperty = "border-color";

        // False for anything that is not a per-side color, including a per-side width (border-t-2,
        // border-t-[3px]), which is the physical utility's to resolve.
        internal static bool TryParse(string? cls, out ArbitraryStyle result)
            => TryResolve(cls, out _, out result);

        internal static string? SortPropertyOf(string core)
            => TryResolve(core, out var family, out _) ? family.SortProperty : null;

        // Two writers of one border side at one key order as Tailwind emits them, by the property each declares;
        // 0 where either is not a border color, which leaves them to arrival.
        internal static int CompareCascade(ArbitraryProperty a, ArbitraryProperty z)
        {
            var aRank = CascadeRankOf(a);
            var zRank = CascadeRankOf(z);
            // MUTANT_SURVIVES(equivalent): a rank of 0 would need a border color property first in Tailwind's order, so
            // `< 0` and `<= 0` agree. BorderSideColorOrderTests holds every family's index above 0.
            return aRank < 0 || zRank < 0 ? 0 : aRank.CompareTo(zRank);
        }

        private static int CascadeRankOf(ArbitraryProperty property)
        {
            if (property == ArbitraryProperty.BorderColor)
            {
                return StyleRuleOrder.TailwindIndexOf(ShorthandSortProperty);
            }
            foreach (var family in s_families)
            {
                if (family.Property == property)
                {
                    return StyleRuleOrder.TailwindIndexOf(family.SortProperty);
                }
            }
            return -1;
        }

        private static bool TryResolve(string? cls, out Family family, out ArbitraryStyle result)
        {
            result = default;
            if (cls != null)
            {
                foreach (var candidate in s_families)
                {
                    var name = candidate.Name;
                    // The delimiter is what keeps border-b from claiming border-bs-red-500.
                    if (cls.Length > name.Length && cls[name.Length] == '-'
                        && cls.StartsWith(name, System.StringComparison.Ordinal))
                    {
                        family = candidate;
                        if (!StyleColorValueParser.TryParseColorValue(cls, name.Length + 1, out var color))
                        {
                            return false;
                        }
                        result = new ArbitraryStyle(candidate.Property, color);
                        return true;
                    }
                }
            }
            family = default;
            return false;
        }
    }
}
