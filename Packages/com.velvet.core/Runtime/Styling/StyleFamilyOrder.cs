#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Velvet
{
    // The families whose tokens a C# resolver folds from the whole class list take the last token of a slot, whichever
    // Tailwind emits first. Their base tokens are therefore put in the order Tailwind emits them, in the positions
    // they already hold, so the token the fold ends on is the one Tailwind emits last. A variant payload is appended
    // after the base tokens by the variant gate and keeps its place there. The tokens of one family and importance
    // are ordered together, because a resolver reads them as one list, and tokens of another family keep theirs.
    //
    // Tailwind orders two utilities by the first property they differ on, then the one declaring more properties,
    // then the candidate name. The first two ranks are a family's lane where its tokens declare different
    // properties (outline-* after ring-*) or a different number of declarations (a
    // gradient shape), and the candidate name ranks the rest. A utility Tailwind has no counterpart for (the
    // nine-slice insets, bold-italic, clip-path-*, bg-[addr:...], animate-hue, animate-gradient and animate-shimmer)
    // takes the same rule, so its order is deterministic rather than the order it was written in.
    internal static class StyleFamilyOrder
    {
        // Marks the families ordered by the sort key, and stands for the lane of those with only one.
        private static readonly Func<string, int> s_byKey = _ => 0;
        private static readonly Func<string, int> s_noLane = _ => 0;

        // A token belongs to the first family that claims it. The families on s_byKey carry declarations the sort key counts
        // and are ordered by StyleRuleOrder.CompareUtilities: a slash size declares a line height beside its font size,
        // a nine-slice inset writes as many longhands as the edges it names, and a skew-<n> declares --tw-skew-y, which
        // property-order.ts lists ahead of the transform a skew-x-* declares next. The others are ordered by their
        // lane, then by candidate name, where Tailwind's sort ends for two utilities that declare the same properties.
        private static readonly (Func<string, bool> Claims, Func<string, int> Lane)[] s_families =
        {
            (IsTextToken, s_byKey),
            (IsSliceToken, s_byKey),
            (StyleFontClass.IsFontToken, s_noLane),
            (IsZIndexToken, s_noLane),
            (StyleGapClass.IsGapToken, s_noLane),
            (StyleGridClass.IsGridToken, s_noLane),
            (StyleDivideClass.IsDivideToken, s_noLane),
            (StyleShadowClass.IsShadowClass, s_noLane),
            (StyleShadowClass.IsInsetBoxShadowClass, s_noLane),
            (StyleRingClass.IsRingClass, RingLane),
            (token => StyleClipPathClass.IsClipPathClass(token), s_noLane),
            (token => StyleSkewClass.IsSkewClass(token), s_byKey),
            (IsBackgroundImageToken, s_noLane),
            (token => StyleBorderStyleClass.IsBorderStyleClass(token), s_noLane),
            (StyleAnimateClass.IsAnimateClass, s_noLane),
            (StyleGradientClass.IsGradientClass, GradientLane),
            (StylePointerEventsClass.IsPointerEventsToken, s_noLane),
        };

        // tokens with those of each family and importance put in Tailwind's order; tokens itself when they are.
        internal static string[] Sorted(string[] tokens)
        {
            var groups = GroupsOf(tokens);
            if (groups == null)
            {
                return tokens;
            }
            string[]? sorted = null;
            for (var slot = 0; slot < groups.Length; slot++)
            {
                if (groups[slot] is { } members)
                {
                    sorted = Placed(sorted, tokens, members, slot / 2) ?? sorted;
                }
            }
            return sorted ?? tokens;
        }

        // The positions of the tokens of each family and importance, indexed by SlotOf; null when no token belongs to one.
        private static List<int>?[]? GroupsOf(string[] tokens)
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
            return groups;
        }

        // Puts the base tokens of each family on element's class list in the order of tokens, and the variant payloads
        // a manipulator has appended after them, so a reader that folds the live list ends on the same token a fresh
        // mount would. DiffClassList appends a token it adds, which leaves the live order to the order of the patches.
        // A token the class list does not hold (one resolved inline) takes no part. The set of classes on the element is
        // the same afterwards, and neither StyleClassProjection.Model nor ClipPathLayoutBox.SyncClasses reads the
        // order of the list, so nothing that mirrors it needs telling.
        internal static void RestoreLiveOrder(VisualElement element, string[] tokens)
        {
            var sorted = Sorted(tokens);
            var groups = GroupsOf(sorted);
            if (groups == null)
            {
                return;
            }
            for (var slot = 0; slot < groups.Length; slot++)
            {
                if (groups[slot] is not { } members)
                {
                    continue;
                }
                var live = new List<string>();
                foreach (var cls in element.GetClasses())
                {
                    live.Add(cls);
                }
                var wanted = new List<string>();
                foreach (var member in members)
                {
                    if (live.Contains(sorted[member]))
                    {
                        wanted.Add(sorted[member]);
                    }
                }
                foreach (var cls in live)
                {
                    if (!wanted.Contains(cls) && SlotOf(cls) == slot)
                    {
                        wanted.Add(cls);
                    }
                }
                var actual = live.FindAll(wanted.Contains);
                if (actual.Count < 2 || actual.SequenceEqual(wanted))
                {
                    continue;
                }
                foreach (var cls in wanted)
                {
                    element.RemoveFromClassList(cls);
                }
                foreach (var cls in wanted)
                {
                    element.AddToClassList(cls);
                }
            }
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
                if (s_families[family].Claims(core))
                {
                    return family * 2 + (important ? 1 : 0);
                }
            }
            return -1;
        }

        // into, or a copy of tokens when into is null, with the tokens at members put in Tailwind's order in the
        // positions members name; null when they already are. The sort is stable and bounded, so two tokens
        // that compare equal keep the order they were written in.
        private static string[]? Placed(string[]? into, string[] tokens, List<int> members, int family)
        {
            var comparer = Comparer<string>.Create((a, z) => Compare(a, z, family));
            var ordered = members.Select(member => tokens[member]).OrderBy(token => token, comparer).ToList();
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

        private static int Compare(string a, string z, int family)
        {
            var lane = s_families[family].Lane;
            if (lane == s_byKey)
            {
                return StyleRuleOrder.CompareUtilities(a, z);
            }
            var byLane = lane(Core(a)).CompareTo(lane(Core(z)));
            return byLane != 0 ? byLane : StyleRuleOrder.CompareCandidates(a, z);
        }

        private static string Core(string token) => StyleArbitraryValueResolver.StripImportant(token, out _);

        // Same list: the ring's box-shadow properties come before the outline ones.
        private static int RingLane(string core)
            => core == "outline" || core.StartsWith("outline-", StringComparison.Ordinal)
                || core.StartsWith("-outline-", StringComparison.Ordinal) ? 1 : 0;

        // A colour stop declares its own variables and is ordered apart. A shape that declares its position variable
        // once inside @supports as well (a keyword or integer bg-linear-*) counts three declarations, and
        // the rest two (bg-linear-[...], bg-radial*, bg-conic*, bg-gradient-to-*); the one with more declarations
        // is emitted first.
        private static int GradientLane(string core)
        {
            if (core.StartsWith("from-", StringComparison.Ordinal) || core.StartsWith("via-", StringComparison.Ordinal)
                || core.StartsWith("to-", StringComparison.Ordinal))
            {
                return 0;
            }
            var linear = core.StartsWith("bg-linear-", StringComparison.Ordinal)
                || core.StartsWith("-bg-linear-", StringComparison.Ordinal);
            return linear && !core.StartsWith("bg-linear-[", StringComparison.Ordinal) ? 1 : 2;
        }

        // The text-effect tokens a resolver folds (transform, decoration, wrapping, leading, the named and slash sizes),
        // and the inline font size beside them, which a slash size also writes, so that the two are ordered in one list.
        private static bool IsTextToken(string core)
            => StyleTextEffectClass.IsTextEffectToken(core) || Parses(core, ArbitraryProperty.FontSize);

        private static bool IsSliceToken(string core)
            => StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParseLayerQuietly(core, out var style)
                && style.Property is ArbitraryProperty.Slice or ArbitraryProperty.SliceX or ArbitraryProperty.SliceY
                    or ArbitraryProperty.SliceTop or ArbitraryProperty.SliceRight or ArbitraryProperty.SliceBottom
                    or ArbitraryProperty.SliceLeft;

        private static bool Parses(string core, ArbitraryProperty property)
            => StyleArbitraryValueResolver.IsInlineResolved(core)
                && StyleArbitraryValueResolver.TryParseLayerQuietly(core, out var style) && style.Property == property;

        private static bool IsZIndexToken(string core)
            => core.StartsWith("z-", StringComparison.Ordinal) || core.StartsWith("-z-", StringComparison.Ordinal);

        private static bool IsBackgroundImageToken(string core)
            => core.StartsWith("bg-[addr:", StringComparison.Ordinal);
    }
}
