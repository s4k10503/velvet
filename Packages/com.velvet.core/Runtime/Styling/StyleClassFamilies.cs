#nullable enable
using System;
using System.Runtime.CompilerServices;

namespace Velvet
{
    [Flags]
    internal enum StyleClassFamily : uint
    {
        None = 0,
        Gradient = 1u << 0,
        Animate = 1u << 1,
        Skew = 1u << 2,
        Shadow = 1u << 3,
        BorderStyle = 1u << 4,
        Ring = 1u << 5,
        Backdrop = 1u << 6,
        ChildVariant = 1u << 7,
        Gap = 1u << 8,
        Grid = 1u << 9,
        Divide = 1u << 10,
        PointerEvents = 1u << 11,
        Stacking = 1u << 12,
        ZIndex = 1u << 13,
        Order = 1u << 14,
    }

    // The families a class array's tokens feed, read once per array and kept by its identity, so the passes a
    // patch runs over one array answer their gates with a bit test instead of a scan each. A bit is set where its
    // family's own token predicate holds for some token. StyleClassFamiliesTests fails where a family's parse reads
    // a corpus token its bit does not flag.
    //
    // A reading kept by identity stands for every mount the array reaches — ClassNameParseCache hands one array to
    // all of them — and for as long as the array's contents stay as they are, which the reconciler already depends
    // on (DiffClassList returns on an identical array without reading it). So a family whose predicate can reach
    // anything beyond the token's text has no bit here, and its gate scans per call: the timing-keyword and
    // transition-filter predicates peel variants through the mount's theme scope, which decides whether `3xl:` is a
    // variant at all, and the border-width predicate parses a bracket through the arbitrary-value parser, which can
    // reach a theme-scope read. ClassGateThemeScopeTests fails where a family here reads a token differently inside a
    // sheet's scope.
    //
    // Direct-mapped, so the table is bounded without an eviction pass and a miss allocates nothing of its own. Two
    // arrays sharing a slot each read again when the other displaced it, and the table keeps the last array of
    // each slot alive.
    //
    // Main thread only, like ClassNameParseCache (Documentation~/async.md owns the rule).
    internal static class StyleClassFamilies
    {
        private const int SlotCount = 1024;

        private static readonly (StyleClassFamily Family, Func<string, bool> Feeds)[] s_predicates =
        {
            (StyleClassFamily.Gradient, StyleGradientClass.IsActivatorToken),
            (StyleClassFamily.Animate, StyleAnimateClass.IsAnimateClass),
            (StyleClassFamily.Skew, StyleSkewClass.IsSkewClass),
            (StyleClassFamily.Shadow, StyleShadowClass.IsShadowToken),
            (StyleClassFamily.BorderStyle, StyleBorderStyleClass.IsBorderStyleClass),
            (StyleClassFamily.Ring, StyleRingClass.IsRingClass),
            (StyleClassFamily.Backdrop, StyleBackdropClass.IsBackdropToken),
            (StyleClassFamily.ChildVariant, StyleChildVariantClass.IsChildVariant),
            (StyleClassFamily.Gap, StyleGapClass.IsGapToken),
            (StyleClassFamily.Grid, StyleGridClass.IsGridToken),
            (StyleClassFamily.Divide, StyleDivideClass.IsDivideToken),
            (StyleClassFamily.PointerEvents, StylePointerEventsClass.IsModeToken),
            (StyleClassFamily.Stacking, FiberZLayerCoordinator.IsStackingToken),
            (StyleClassFamily.ZIndex, StyleZIndexClass.IsZIndexToken),
            (StyleClassFamily.Order, StyleOrderClass.IsOrderClass),
        };

        private static readonly string[]?[] s_arrays = new string[]?[SlotCount];
        private static readonly StyleClassFamily[] s_families = new StyleClassFamily[SlotCount];

        // Whether the token feeds a family Velvet realises in C#.
        internal static bool Feeds(string cls) => Array.Exists(s_predicates, predicate => predicate.Feeds(cls));

        internal static bool Has(string[]? classNames, StyleClassFamily family) => (Of(classNames) & family) != 0;

        private static StyleClassFamily Of(string[]? classNames)
        {
            if (classNames == null || classNames.Length == 0)
            {
                return StyleClassFamily.None;
            }
            var slot = RuntimeHelpers.GetHashCode(classNames) & (SlotCount - 1);
            if (ReferenceEquals(s_arrays[slot], classNames))
            {
                return s_families[slot];
            }
            var families = Read(classNames);
            s_arrays[slot] = classNames;
            s_families[slot] = families;
            return families;
        }

        private static StyleClassFamily Read(string[] classNames)
        {
            var families = StyleClassFamily.None;
            foreach (var cls in classNames)
            {
                if (cls == null)
                {
                    continue;
                }
                foreach (var (family, feeds) in s_predicates)
                {
                    if ((families & family) == 0 && feeds(cls))
                    {
                        families |= family;
                    }
                }
            }
            return families;
        }
    }
}
