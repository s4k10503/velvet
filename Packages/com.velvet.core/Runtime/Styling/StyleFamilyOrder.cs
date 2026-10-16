#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // The families whose tokens a C# resolver folds from the whole class list take the last token of a slot, whichever
    // Tailwind emits first. Their base tokens are therefore put in the order Tailwind emits them, in the positions
    // they already hold, so the token the fold ends on is the one Tailwind emits last. A variant payload is appended
    // after the base tokens by the variant gate and keeps its place there. The tokens of one family and importance
    // are ordered together, because a resolver reads them as one list, and tokens of another family keep theirs.
    //
    // The nine-slice insets are no resolver's, but their properties overlap without Tailwind having a utility for
    // them, so their order is the sort key's: more longhands first, then the candidate name.
    internal static class StyleFamilyOrder
    {
        // The families whose tokens carry longhands the sort key counts, and so are ordered by
        // StyleRuleOrder.CompareUtilities: a slash size declares a line height beside its font size, and a nine-slice
        // inset writes as many longhands as the edges it names.
        private const int KeyFamilies = 2;

        // A token belongs to the first family that claims it. The families after KeyFamilies are ordered by candidate
        // name alone, where Tailwind's sort ends for two utilities that declare the same properties.
        private static readonly Func<string, bool>[] s_families =
        {
            IsTextToken,
            IsSliceToken,
            StyleFontClass.IsFontToken,
            IsZIndexToken,
            StyleGapClass.IsGapToken,
            StyleGridClass.IsGridToken,
            StyleDivideClass.IsDivideToken,
            StyleShadowClass.IsShadowClass,
            StyleRingClass.IsRingClass,
            token => StyleClipPathClass.IsClipPathClass(token),
            token => StyleSkewClass.IsSkewClass(token),
            IsBackgroundImageToken,
            token => StyleBorderStyleClass.IsBorderStyleClass(token),
            StyleAnimateClass.IsAnimateClass,
            StyleGradientClass.IsGradientClass,
            StylePointerEventsClass.IsPointerEventsToken,
        };

        // tokens with those of each family and importance put in Tailwind's order; tokens itself when they are.
        internal static string[] Sorted(string[] tokens)
        {
            List<int>?[]? groups = null;
            for (var i = 0; i < tokens.Length; i++)
            {
                var slot = SlotOf(tokens[i]);
                if (slot >= 0)
                {
                    ((groups ??= new List<int>?[s_families.Length * 2])[slot] ??= new List<int>()).Add(i);
                }
            }
            if (groups == null)
            {
                return tokens;
            }
            string[]? sorted = null;
            for (var slot = 0; slot < groups.Length; slot++)
            {
                if (groups[slot] is { } members)
                {
                    sorted = Placed(sorted, tokens, members, slot / 2 >= KeyFamilies) ?? sorted;
                }
            }
            return sorted ?? tokens;
        }

        // The family and importance of a variantless token, as the index of its group; -1 for any other.
        private static int SlotOf(string token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(token, out var important);
            if (string.IsNullOrEmpty(core) || StyleSpacingPrecedence.VariantsLength(core) != 0)
            {
                return -1;
            }
            for (var family = 0; family < s_families.Length; family++)
            {
                if (s_families[family](core))
                {
                    return family * 2 + (important ? 1 : 0);
                }
            }
            return -1;
        }

        // into, or a copy of tokens when into is null, with the tokens at members put in Tailwind's order in the
        // positions members name; null when they already are.
        private static string[]? Placed(string[]? into, string[] tokens, List<int> members, bool byCandidate)
        {
            var ordered = new List<string>(members.Count);
            foreach (var member in members)
            {
                ordered.Add(tokens[member]);
            }
            for (var i = 1; i < ordered.Count; i++)
            {
                var token = ordered[i];
                var at = i - 1;
                while (at >= 0 && Compare(ordered[at], token, byCandidate) > 0)
                {
                    ordered[at + 1] = ordered[at];
                    at--;
                }
                ordered[at + 1] = token;
            }
            string[]? placed = null;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i] != tokens[members[i]])
                {
                    (placed ??= into ?? (string[])tokens.Clone())[members[i]] = ordered[i];
                }
            }
            return placed;
        }

        private static int Compare(string a, string z, bool byCandidate)
            => byCandidate ? StyleRuleOrder.CompareCandidates(a, z) : StyleRuleOrder.CompareUtilities(a, z);

        // The text-effect tokens a resolver folds (transform, decoration, wrapping, leading, the named and slash sizes),
        // and the inline font size beside them, which a slash size also writes, so that the two are ordered in one list.
        private static bool IsTextToken(string core)
            => StyleTextEffectClass.IsTextEffectToken(core) || Parses(core, ArbitraryProperty.FontSize);

        private static bool IsSliceToken(string core)
            => StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParseLayer(core, out var style)
                && style.Property is ArbitraryProperty.Slice or ArbitraryProperty.SliceX or ArbitraryProperty.SliceY
                    or ArbitraryProperty.SliceTop or ArbitraryProperty.SliceRight or ArbitraryProperty.SliceBottom
                    or ArbitraryProperty.SliceLeft;

        private static bool Parses(string core, ArbitraryProperty property)
            => StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParseLayer(core, out var style) && style.Property == property;

        private static bool IsZIndexToken(string core)
            => core.StartsWith("z-", StringComparison.Ordinal) || core.StartsWith("-z-", StringComparison.Ordinal);

        private static bool IsBackgroundImageToken(string core)
            => core.StartsWith("bg-[addr:", StringComparison.Ordinal);
    }
}
