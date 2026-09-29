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
        // maps to, in that file's order. Tailwind leaves a property it does not list out of the sort, and so
        // does PropertySortOf.
        private static readonly string[] s_tailwindPropertyOrder =
        {
            "visibility", "position", "top", "right", "bottom", "left", "margin-top", "margin-right",
            "margin-bottom", "margin-left", "display", "aspect-ratio", "height", "max-height", "min-height", "width",
            "max-width", "min-width", "flex-shrink", "flex-grow", "flex-basis", "transform-origin", "translate",
            "scale", "rotate", "cursor", "flex-direction", "flex-wrap", "align-content", "align-items",
            "justify-content", "align-self", "overflow", "border-top-left-radius", "border-top-right-radius",
            "border-bottom-right-radius", "border-bottom-left-radius", "border-top-width", "border-right-width",
            "border-bottom-width", "border-left-width", "border-top-color", "border-right-color",
            "border-bottom-color", "border-left-color", "background-color", "background-image", "background-size",
            "background-position", "background-repeat", "padding-top", "padding-right", "padding-bottom",
            "padding-left", "text-align", "font-family", "font-size", "letter-spacing", "text-overflow",
            "white-space", "color", "opacity", "filter", "transition-property", "transition-delay",
            "transition-duration", "transition-timing-function",
        };

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
            if (a.Length != z.Length)
            {
                return false;
            }
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

        // compare() of Tailwind's utils/compare.ts: character by character, a run of digits against a run of
        // digits by number, and the shorter string first when one is a prefix of the other.
        internal static int NaturalCompare(string a, string z)
        {
            var minLength = Math.Min(a.Length, z.Length);
            for (var i = 0; i < minLength; i++)
            {
                if (IsDigit(a[i]) && IsDigit(z[i]))
                {
                    var aEnd = RunEnd(a, i);
                    var zEnd = RunEnd(z, i);
                    var aRun = a.Substring(i, aEnd - i);
                    var zRun = z.Substring(i, zEnd - i);
                    var byNumber = CompareDigitRuns(aRun, zRun);
                    if (byNumber != 0)
                    {
                        return byNumber;
                    }
                    var byText = string.CompareOrdinal(aRun, zRun);
                    if (byText != 0)
                    {
                        return byText;
                    }
                    continue;
                }
                if (a[i] != z[i])
                {
                    return a[i] - z[i];
                }
            }
            return a.Length - z.Length;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        // Two digit runs by the numbers they spell, of any length: past the leading zeros, the longer run is the
        // larger number, and runs of one length compare digit by digit.
        private static int CompareDigitRuns(string a, string z)
        {
            var aDigits = a.TrimStart('0');
            var zDigits = z.TrimStart('0');
            return aDigits.Length != zDigits.Length
                ? aDigits.Length.CompareTo(zDigits.Length)
                : string.CompareOrdinal(aDigits, zDigits);
        }

        private static int RunEnd(string s, int start)
        {
            var end = start + 1;
            while (end < s.Length && IsDigit(s[end]))
            {
                end++;
            }
            return end;
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
                // Highest family first, the order a bit set is compared in.
                values.Sort((a, z) => z.Rank.CompareTo(a.Rank));
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
                return byCount != 0 ? byCount : NaturalCompare(_candidate, other._candidate);
            }

            private static int CompareValues(List<(long Rank, string Value)> a, List<(long Rank, string Value)> z)
            {
                var length = Math.Min(a.Count, z.Count);
                for (var i = 0; i < length; i++)
                {
                    var byRank = z[i].Rank.CompareTo(a[i].Rank);
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

        // The Tailwind property-order indexes of what the utility writes, ascending, and how many longhands it
        // writes.
        private static (List<int> Order, int Count) PropertySortOf(string utility)
        {
            var core = StyleArbitraryValueResolver.StripImportant(utility, out _);
            var set = StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParse(core, out var style)
                    ? StyleArbitraryLonghands.Of(style.Property)
                    : StyleUtilityProperties.TryGet(core, out var rule) ? rule.Properties : StyleLonghandSet.Empty;
            var order = new List<int>();
            var count = 0;
            for (var i = 0; i < s_longhands.Length; i++)
            {
                if (!set.Contains(s_longhands[i]))
                {
                    continue;
                }
                count++;
                if (s_tailwindIndex[i] >= 0 && !order.Contains(s_tailwindIndex[i]))
                {
                    order.Add(s_tailwindIndex[i]);
                }
            }
            order.Sort();
            return (order, count);
        }
    }
}
