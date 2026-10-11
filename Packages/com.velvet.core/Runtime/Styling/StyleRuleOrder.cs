#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // The order Tailwind's compile.ts gives two rules whose variant families are the same: the variants' own
    // values first (Variants.compare orders a data- value, a has- argument, a group/peer name or an arbitrary
    // selector before another), then the first property the two differ on in Tailwind's property order, then
    // the rule setting more properties, then compare() over the whole candidate. A family-level rank
    // (StyleLayerPriority) decides first; this is what the rule position a key carries (WithRule) is filled
    // with, so it only ever separates rules of one rank.
    internal static class StyleRuleOrder
    {
        // Tailwind v4's property-order.ts (tailwindcss@fa81d697), kept to the properties a UI Toolkit longhand
        // maps to, the shorthand and logical properties s_shorthandIndexes, StyleLogicalUtilities and
        // StyleBorderSideColor place tokens by, plus line-height (see s_lineHeightIndex), in that file's order. Tailwind leaves a property it does
        // not list out of the sort, and so does PropertySortOf.
        private static readonly string[] s_tailwindPropertyOrder =
        {
            "visibility", "position", "inset", "inset-inline", "inset-block", "inset-inline-start",
            "inset-inline-end", "inset-block-start", "inset-block-end", "top", "right", "bottom", "left", "margin",
            "margin-inline", "margin-block", "margin-inline-start", "margin-inline-end",
            "margin-block-start", "margin-block-end", "margin-top", "margin-right",
            "margin-bottom", "margin-left", "display", "aspect-ratio", "height", "max-height", "min-height", "width",
            "max-width", "min-width", "flex-shrink", "flex-grow", "flex-basis", "transform-origin", "translate",
            "scale", "rotate", "cursor", "flex-direction", "flex-wrap", "align-content", "align-items",
            "justify-content", "align-self", "overflow", "border-radius", "border-start-start-radius", "border-start-end-radius",
            "border-end-end-radius", "border-end-start-radius", "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius", "border-width", "border-inline-width",
            "border-block-width", "border-inline-start-width",
            "border-inline-end-width", "border-block-start-width", "border-block-end-width", "border-top-width",
            "border-right-width", "border-bottom-width", "border-left-width", "border-color", "border-inline-color",
            "border-block-color", "border-inline-start-color", "border-inline-end-color", "border-block-start-color",
            "border-block-end-color", "border-top-color",
            "border-right-color", "border-bottom-color", "border-left-color", "background-color", "background-image", "background-size",
            "background-position", "background-repeat", "padding", "padding-inline", "padding-block", "padding-inline-start", "padding-inline-end",
            "padding-block-start", "padding-block-end", "padding-top", "padding-right", "padding-bottom",
            "padding-left", "text-align", "font-family", "font-size", "line-height", "letter-spacing", "text-overflow",
            "white-space", "color", "opacity", "filter", "transition-property", "transition-delay",
            "transition-duration", "transition-timing-function",
        };

        // The Tailwind property a utility that writes exactly these longhands declares, where Tailwind declares
        // one shorthand for them (m-4 declares margin, mx-4 margin-inline). Sorted by its longhands, m-4 would
        // follow ms-4, since margin-inline-start precedes margin-top; Tailwind emits margin ahead of both.
        private static readonly (StyleLonghandSet Set, int Index)[] s_shorthandIndexes = BuildShorthandIndexes();

        private static (StyleLonghandSet, int)[] BuildShorthandIndexes()
        {
            var sets = new (StyleLonghand[] Longhands, string Name)[]
            {
                (new[] { StyleLonghand.MarginTop, StyleLonghand.MarginRight, StyleLonghand.MarginBottom, StyleLonghand.MarginLeft }, "margin"),
                (new[] { StyleLonghand.MarginLeft, StyleLonghand.MarginRight }, "margin-inline"),
                (new[] { StyleLonghand.MarginTop, StyleLonghand.MarginBottom }, "margin-block"),
                (new[] { StyleLonghand.PaddingTop, StyleLonghand.PaddingRight, StyleLonghand.PaddingBottom, StyleLonghand.PaddingLeft }, "padding"),
                (new[] { StyleLonghand.PaddingLeft, StyleLonghand.PaddingRight }, "padding-inline"),
                (new[] { StyleLonghand.PaddingTop, StyleLonghand.PaddingBottom }, "padding-block"),
                (new[] { StyleLonghand.Top, StyleLonghand.Right, StyleLonghand.Bottom, StyleLonghand.Left }, "inset"),
                (new[] { StyleLonghand.Left, StyleLonghand.Right }, "inset-inline"),
                (new[] { StyleLonghand.Top, StyleLonghand.Bottom }, "inset-block"),
                (new[] { StyleLonghand.BorderTopLeftRadius, StyleLonghand.BorderTopRightRadius, StyleLonghand.BorderBottomRightRadius, StyleLonghand.BorderBottomLeftRadius }, "border-radius"),
                (new[] { StyleLonghand.BorderTopWidth, StyleLonghand.BorderRightWidth, StyleLonghand.BorderBottomWidth, StyleLonghand.BorderLeftWidth }, "border-width"),
                (new[] { StyleLonghand.BorderLeftWidth, StyleLonghand.BorderRightWidth }, "border-inline-width"),
                (new[] { StyleLonghand.BorderTopWidth, StyleLonghand.BorderBottomWidth }, "border-block-width"),
                (new[] { StyleLonghand.BorderTopColor, StyleLonghand.BorderRightColor, StyleLonghand.BorderBottomColor, StyleLonghand.BorderLeftColor }, "border-color"),
            };
            var indexes = new (StyleLonghandSet, int)[sets.Length];
            for (var i = 0; i < sets.Length; i++)
            {
                var set = StyleLonghandSet.Empty;
                foreach (var longhand in sets[i].Longhands)
                {
                    set = set.Union(StyleLonghandSet.Of(longhand));
                }
                indexes[i] = (set, Array.IndexOf(s_tailwindPropertyOrder, sets[i].Name));
            }
            return indexes;
        }

        // The CSS property a UI Toolkit longhand stands for where the two names differ.
        private static readonly Dictionary<string, string> s_cssNames = new(StringComparer.Ordinal)
        {
            ["-unity-text-align"] = "text-align",
            ["-unity-font-definition"] = "font-family",
            ["background-position-x"] = "background-position",
            ["background-position-y"] = "background-position",
        };

        private static readonly StyleLonghand[] s_longhands = (StyleLonghand[])Enum.GetValues(typeof(StyleLonghand));

        private static readonly int[] s_tailwindIndex = BuildTailwindIndex();

        // A named text size (text-lg) emits line-height beside font-size in Tailwind, and its USS rule writes
        // font-size alone, UI Toolkit having no line-height; the sort adds it back for them.
        private static readonly int s_lineHeightIndex = Array.IndexOf(s_tailwindPropertyOrder, "line-height");

        private const string NamedTextSizePrefix = "text-";

        private static int[] BuildTailwindIndex()
        {
            var index = new int[s_longhands.Length];
            for (var i = 0; i < s_longhands.Length; i++)
            {
                var uss = StyleUtilityProperties.UssName(s_longhands[i]);
                var css = s_cssNames.TryGetValue(uss, out var mapped) ? mapped : uss;
                index[i] = Array.IndexOf(s_tailwindPropertyOrder, css);
            }
            return index;
        }

        // The last array OrdinalOf ordered, with a copy of its tokens: the reconciler asks once per variant rule
        // of one class list, and a buffer reused for another list shows as a changed token.
        private static string[]? s_lastNames;
        private static string[]? s_lastTokens;
        private static int[] s_lastOrdinals = Array.Empty<int>();

        // The place of classNames[index] in Tailwind's order among the tokens of classNames (see OrdinalsOf).
        public static int OrdinalOf(string[] classNames, int index)
        {
            if (!ReferenceEquals(classNames, s_lastNames) || !SameTokens(classNames, s_lastTokens!))
            {
                s_lastOrdinals = OrdinalsOf(classNames);
                s_lastNames = classNames;
                s_lastTokens = (string[])classNames.Clone();
            }
            return s_lastOrdinals[index];
        }

        private static bool SameTokens(string[] a, string[] z)
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (!ReferenceEquals(a[i], z[i]))
                {
                    return false;
                }
            }
            return true;
        }

        // Each className index's place in Tailwind's order among the tokens of classNames, from 0; tokens that
        // compare equal share a place.
        public static int[] OrdinalsOf(string[] classNames)
        {
            var sorted = new int[classNames.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                sorted[i] = i;
            }
            var keys = new SortKey[classNames.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = SortKey.Of(classNames[i] ?? string.Empty);
            }
            Array.Sort(sorted, (a, z) => keys[a].CompareTo(keys[z]));

            var ordinals = new int[classNames.Length];
            var place = 0;
            for (var i = 0; i < sorted.Length; i++)
            {
                if (i > 0 && keys[sorted[i - 1]].CompareTo(keys[sorted[i]]) != 0)
                {
                    place = i;
                }
                ordinals[sorted[i]] = place;
            }
            return ordinals;
        }

        private readonly struct SortKey : IComparable<SortKey>
        {
            private readonly List<(long Rank, string Value)> _values;
            private readonly List<int> _properties;
            private readonly int _count;
            private readonly string _candidate;

            private SortKey(List<(long, string)> values, List<int> properties, int count, string candidate)
            {
                _values = values;
                _properties = properties;
                _count = count;
                _candidate = candidate;
            }

            public static SortKey Of(string token)
            {
                var values = new List<(long Rank, string Value)>();
                var leaf = PeelVariants(token, values);
                // Highest variant first, the order a bit set is compared in: by family bit, and within one family by
                // value, a greater value being a variant Tailwind registers later. A variant written twice is one
                // bit.
                values.Sort((a, z) =>
                {
                    var byFamily = StyleLayerPriority.VariantSetOf(z.Rank).CompareTo(StyleLayerPriority.VariantSetOf(a.Rank));
                    return byFamily != 0 ? byFamily : string.CompareOrdinal(z.Value, a.Value);
                });
                for (var i = values.Count - 1; i > 0; i--)
                {
                    if (values[i] == values[i - 1])
                    {
                        values.RemoveAt(i);
                    }
                }
                var (properties, count) = PropertySortOf(leaf);
                return new SortKey(values, properties, count, token);
            }

            public int CompareTo(SortKey other)
            {
                var byValue = CompareValues(_values, other._values);
                if (byValue != 0)
                {
                    return byValue;
                }
                var byProperty = CompareProperties(_properties, other._properties);
                if (byProperty != 0)
                {
                    return byProperty;
                }
                var byCount = other._count.CompareTo(_count);
                return byCount != 0 ? byCount : StyleCandidateOrder.Compare(_candidate, other._candidate);
            }

            private static int CompareValues(List<(long Rank, string Value)> a, List<(long Rank, string Value)> z)
            {
                var length = Math.Min(a.Count, z.Count);
                for (var i = 0; i < length; i++)
                {
                    var byRank = StyleLayerPriority.VariantSetOf(a[i].Rank)
                        .CompareTo(StyleLayerPriority.VariantSetOf(z[i].Rank));
                    if (byRank != 0)
                    {
                        return byRank;
                    }
                    var byValue = string.CompareOrdinal(a[i].Value, z[i].Value);
                    if (byValue != 0)
                    {
                        return byValue;
                    }
                }
                return a.Count.CompareTo(z.Count);
            }

            // The first index the two differ on decides, the lower first; a list that runs out reads as past
            // every index.
            private static int CompareProperties(List<int> a, List<int> z)
            {
                var offset = 0;
                while (offset < a.Count && offset < z.Count && a[offset] == z[offset])
                {
                    offset++;
                }
                var aIndex = offset < a.Count ? a[offset] : int.MaxValue;
                var zIndex = offset < z.Count ? z[offset] : int.MaxValue;
                return aIndex.CompareTo(zIndex);
            }
        }

        // Strips every variant off token, recording each one's family rank and value, and returns the utility
        // left under them.
        private static string PeelVariants(string token, List<(long Rank, string Value)> values)
        {
            var rest = token;
            while (true)
            {
                if (StyleVariantClass.TryParse(rest, out var kind, out var name, out var payload))
                {
                    values.Add((StyleLayerPriority.ForVariant(kind), name ?? string.Empty));
                }
                else if (StyleHasVariantClass.TryParse(rest, out var hasKind, out var className, out payload))
                {
                    values.Add((StyleLayerPriority.Has, HasArgumentOf(hasKind, className)));
                }
                else if (StyleAttributeVariantClass.TryParse(rest, out var ns, out var key, out var value, out payload))
                {
                    values.Add((StyleLayerPriority.AttributeOf(ns), value == null ? key ?? string.Empty : key + "=" + value));
                }
                else if (StyleStructuralVariantClass.TryParse(rest, out var structural, out _, out payload))
                {
                    values.Add((StyleStructuralVariantClass.PriorityOf(rest, structural), SelectorOf(rest, payload)));
                }
                else if (StyleSupportsVariantClass.TryParse(rest, out var property, out var query, out payload))
                {
                    values.Add((StyleLayerPriority.Supports, property + ":" + query));
                }
                else if (StyleChildVariantClass.TryParse(rest, out payload))
                {
                    values.Add((StyleLayerPriority.ChildVariant, string.Empty));
                }
                else
                {
                    return rest;
                }
                rest = payload ?? string.Empty;
            }
        }

        // has-[.x] before has-[:checked] before has-[:focus]: the arguments as written, compared as strings.
#pragma warning disable CS8524 // no discard arm: a new has- kind has to state its argument
        private static string HasArgumentOf(StyleHasKind kind, string? className) => kind switch
        {
            StyleHasKind.Class => "." + className,
            StyleHasKind.Checked => ":checked",
            StyleHasKind.Focus => ":focus",
        };
#pragma warning restore CS8524

        // The text of the variant in front of payload: an arbitrary [&:…]: selector compares by that text, and
        // a named structural token's spelling orders the same way among its own kind.
        private static string SelectorOf(string token, string? payload)
            => token.Substring(0, Math.Max(0, token.Length - (payload?.Length ?? 0) - 1));

        internal static int TailwindIndexOf(string property) => Array.IndexOf(s_tailwindPropertyOrder, property);

        private static int? ShorthandIndexOf(StyleLonghandSet set)
        {
            foreach (var (shorthandSet, index) in s_shorthandIndexes)
            {
                if (shorthandSet == set)
                {
                    return index;
                }
            }
            return null;
        }

        // The Tailwind property-order indexes of what the utility writes, ascending, and how many longhands it
        // writes.
        private static (List<int> Order, int Count) PropertySortOf(string utility)
        {
            var core = StyleArbitraryValueResolver.StripImportant(utility, out _);
            var inline = false;
            StyleLonghandSet set;
            if (StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParse(core, out var style))
            {
                inline = true;
                set = StyleArbitraryLonghands.Of(style.Property);
            }
            else
            {
                set = StyleUtilityProperties.TryGet(core, out var rule) ? rule.Properties : StyleLonghandSet.Empty;
            }
            var order = new List<int>();
            var count = 0;
            if (!inline && set.Contains(StyleLonghand.FontSize)
                && core.StartsWith(NamedTextSizePrefix, StringComparison.Ordinal))
            {
                // MUTANT_SURVIVES(equivalent): without this index, count still puts named sizes before a lone
                // font-size. RuleOrderCascadeBoundaryTests pins the font-size-only sets and adjacent line-height index.
                order.Add(s_lineHeightIndex);
                count++;
            }
            for (var i = 0; i < s_longhands.Length; i++)
            {
                if (!set.Contains(s_longhands[i]))
                {
                    continue;
                }
                count++;
                // MUTANT_SURVIVES(equivalent): removing the duplicate-index check leaves the object-fit sets
                // equal to one another; comparisons with other sets resolve before the repeated index.
                // RuleOrderCascadeBoundaryTests pins these property sets and the index aliases.
                if (s_tailwindIndex[i] >= 0 && !order.Contains(s_tailwindIndex[i]))
                {
                    order.Add(s_tailwindIndex[i]);
                }
            }
            // Tailwind places a per-side color or a logical token by the property it declares, not by the
            // physical edges the longhands above name.
            var side = inline ? StyleBorderSideColor.SortPropertyOf(core) : null;
            if (side != null)
            {
                order.Clear();
                order.Add(Array.IndexOf(s_tailwindPropertyOrder, side));
            }
            else if (inline && StyleLogicalUtilities.SortPropertiesOf(core) is { } logical)
            {
                order.Clear();
                foreach (var name in logical)
                {
                    order.Add(Array.IndexOf(s_tailwindPropertyOrder, name));
                }
            }
            else if (ShorthandIndexOf(set) is { } shorthand)
            {
                order.Clear();
                order.Add(shorthand);
            }
            order.Sort();
            return (order, count);
        }
    }
}
