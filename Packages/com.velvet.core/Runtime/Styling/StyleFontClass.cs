using System;
using System.Collections.Generic;
using System.Globalization;

namespace Velvet
{
    /// <summary>
    /// The combined font request extracted from an element's class list: a family, a weight, and an
    /// italic flag, each tracked with a "was it specified?" companion. Because all three facets are
    /// gathered together (rather than applied class-by-class), <c>font-bold italic</c> composes into a
    /// single <c>bold-and-italic</c> result — something the raw <c>-unity-font-style</c> property cannot
    /// express from two independent classes.
    /// </summary>
    public readonly struct FontIntent
    {
        public readonly string? Family;       // null when no font-<name> / font-[name] class was present.
        public readonly bool HasFamily;
        public readonly VelvetFontWeight Weight;
        public readonly bool HasWeight;
        public readonly bool Italic;
        public readonly bool HasItalic;

        public FontIntent(string? family, bool hasFamily, VelvetFontWeight weight, bool hasWeight, bool italic, bool hasItalic)
        {
            Family = family;
            HasFamily = hasFamily;
            Weight = weight;
            HasWeight = hasWeight;
            Italic = italic;
            HasItalic = hasItalic;
        }
    }

    /// <summary>
    /// Parses Velvet's font utility classes — <c>font-&lt;name&gt;</c> (family), <c>font-thin</c> …
    /// <c>font-black</c> (weight), <c>italic</c> / <c>not-italic</c> / <c>bold-italic</c> (style), plus
    /// the arbitrary <c>font-[…]</c> forms — into a single <see cref="FontIntent"/>. Mirrors
    /// <see cref="StyleGapClass"/>'s whole-array, last-wins extraction so it slots into the reconciler
    /// the same way. Resolution to actual assets/styles is done by <see cref="StyleFontResolver"/>.
    /// </summary>
    public static class StyleFontClass
    {
        // font-<keyword> → weight. Keys are the class suffix after "font-".
        private static readonly Dictionary<string, VelvetFontWeight> WeightKeywords = new(StringComparer.Ordinal)
        {
            ["thin"] = VelvetFontWeight.Thin,
            ["extralight"] = VelvetFontWeight.ExtraLight,
            ["light"] = VelvetFontWeight.Light,
            ["normal"] = VelvetFontWeight.Normal,
            ["medium"] = VelvetFontWeight.Medium,
            ["semibold"] = VelvetFontWeight.SemiBold,
            ["bold"] = VelvetFontWeight.Bold,
            ["extrabold"] = VelvetFontWeight.ExtraBold,
            ["black"] = VelvetFontWeight.Black,
        };

        /// <summary>
        /// Cheap early-out: true when ANY class is a font utility. Avoids the full <see cref="TryExtract"/>
        /// scan on the elements (the vast majority) that carry no font class.
        /// </summary>
        public static bool HasFontClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }

            foreach (var cls in classNames)
            {
                if (IsFontClass(cls))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFontClass(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            return cls.StartsWith("font-", StringComparison.Ordinal)
                   || cls == "italic"
                   || cls == "not-italic"
                   || cls == "bold-italic";
        }

        /// <summary>
        /// True when <paramref name="cls"/> is an arbitrary font class (<c>font-[…]</c>). These are owned
        /// by <see cref="StyleFontResolver"/> (resolved from the whole class array and applied as inline
        /// style), so the reconciler must keep them out of the USS class list — unlike the non-bracket
        /// font classes (<c>font-bold</c>, <c>font-sans</c>, …) which stay in the list as the USS fallback.
        /// Single source of truth for the AddClass / RemoveClass / ApplyClassNames exclusion checks.
        /// </summary>
        public static bool IsArbitraryFontClass(string cls) =>
            !string.IsNullOrEmpty(cls) && cls.StartsWith("font-[", StringComparison.Ordinal);

        /// <summary>
        /// Scans <paramref name="classNames"/> and folds every font utility into one
        /// <see cref="FontIntent"/>. Each facet (family / weight / italic) follows CSS cascade order:
        /// a later class of the same facet overrides an earlier one. Returns false when no font class is
        /// present.
        /// </summary>
        public static bool TryExtract(string[] classNames, out FontIntent intent)
        {
            intent = default;
            if (classNames == null)
            {
                return false;
            }

            string? family = null;
            var hasFamily = false;
            var weight = VelvetFontWeight.Normal;
            var hasWeight = false;
            var italic = false;
            var hasItalic = false;
            var any = false;

            foreach (var cls in classNames)
            {
                if (!ParseClass(cls, ref family, ref hasFamily, ref weight, ref hasWeight, ref italic, ref hasItalic))
                {
                    continue;
                }

                any = true;
            }

            if (!any)
            {
                return false;
            }

            intent = new FontIntent(family, hasFamily, weight, hasWeight, italic, hasItalic);
            return true;
        }

        // Returns true when cls was recognized as a font utility (and mutated the facets accordingly).
        private static bool ParseClass(
            string cls,
            ref string? family, ref bool hasFamily,
            ref VelvetFontWeight weight, ref bool hasWeight,
            ref bool italic, ref bool hasItalic)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            switch (cls)
            {
                case "italic":
                    italic = true;
                    hasItalic = true;
                    return true;
                case "not-italic":
                    italic = false;
                    hasItalic = true;
                    return true;
                case "bold-italic":
                    weight = VelvetFontWeight.Bold;
                    hasWeight = true;
                    italic = true;
                    hasItalic = true;
                    return true;
            }

            if (!cls.StartsWith("font-", StringComparison.Ordinal))
            {
                return false;
            }

            var rest = cls.Substring("font-".Length);
            if (rest.Length == 0)
            {
                return false;
            }

            // Arbitrary form: font-[<value>].
            if (rest[0] == '[' && rest[rest.Length - 1] == ']')
            {
                var value = rest.Substring(1, rest.Length - 2);
                return ParseArbitrary(value, ref family, ref hasFamily, ref weight, ref hasWeight);
            }

            // Deprecated alias of `italic` (kept in sync with _typography.uss's .font-italic).
            if (rest == "italic")
            {
                italic = true;
                hasItalic = true;
                return true;
            }

            // font-<weight-keyword>.
            if (WeightKeywords.TryGetValue(rest, out var kw))
            {
                weight = kw;
                hasWeight = true;
                return true;
            }

            // Otherwise a family name: font-sans, font-mono, font-display, …
            family = rest;
            hasFamily = true;
            return true;
        }

        // font-[weight:550] / font-[550] → weight; font-[addr:key] / font-[Inter] → family.
        private static bool ParseArbitrary(
            string value,
            ref string? family, ref bool hasFamily,
            ref VelvetFontWeight weight, ref bool hasWeight)
        {
            if (value.Length == 0)
            {
                return false;
            }

            if (value.StartsWith("weight:", StringComparison.Ordinal))
            {
                return TryParseWeight(value.Substring("weight:".Length), ref weight, ref hasWeight);
            }

            // A bare number is a weight (font-[550]); anything else (incl. addr:key) is a family.
            if (char.IsDigit(value[0]) && TryParseWeight(value, ref weight, ref hasWeight))
            {
                return true;
            }

            family = value;
            hasFamily = true;
            return true;
        }

        private static bool TryParseWeight(string raw, ref VelvetFontWeight weight, ref bool hasWeight)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return false;
            }

            weight = (VelvetFontWeight)value;
            hasWeight = true;
            return true;
        }
    }
}
