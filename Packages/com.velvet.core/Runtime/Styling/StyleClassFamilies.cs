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
        FilterTransition = 1u << 2,
        EaseCurve = 1u << 3,
        Skew = 1u << 4,
        Shadow = 1u << 5,
        BorderStyle = 1u << 6,
        BorderWidth = 1u << 7,
        Ring = 1u << 8,
        Backdrop = 1u << 9,
        ChildVariant = 1u << 10,
        Gap = 1u << 11,
        Grid = 1u << 12,
        Divide = 1u << 13,
        PointerEvents = 1u << 14,
        Stacking = 1u << 15,
        ZIndex = 1u << 16,
        Order = 1u << 17,
    }

    // The families a class array's tokens feed, read once per array and kept by its identity, so the passes a
    // patch runs over one array answer their gates with a bit test instead of a scan each. A bit is set where its
    // family's own token predicate holds for some token, so a gate reading it answers what a scan would.
    // StyleClassFamiliesTests fails where a family's parse reads a token its bit does not flag.
    //
    // Keyed by identity, a reading holds only while the array's contents do, which the reconciler already
    // depends on: ClassNameParseCache hands the same array back for the same string, and DiffClassList returns
    // on an identical array without reading it.
    //
    // Direct-mapped, so the table is bounded without an eviction pass and a miss allocates nothing of its own. Two
    // arrays sharing a slot each read again when the other displaced it, and the table keeps the last array of
    // each slot alive.
    //
    // Main thread only, like the rest of the class reads (Documentation~/async.md owns the rule).
    internal static class StyleClassFamilies
    {
        private const int SlotCount = 1024;

        private static readonly (StyleClassFamily Family, Func<string, bool> Feeds)[] s_predicates =
        {
            (StyleClassFamily.Gradient, StyleGradientClass.IsActivatorToken),
            (StyleClassFamily.Animate, StyleAnimateClass.IsAnimateClass),
            (StyleClassFamily.FilterTransition, FiberFilterTransitionApplier.IsFilterTransitionToken),
            (StyleClassFamily.EaseCurve, StyleEaseCurve.IsTimingToken),
            (StyleClassFamily.Skew, StyleSkewClass.IsSkewClass),
            (StyleClassFamily.Shadow, StyleShadowClass.IsShadowToken),
            (StyleClassFamily.BorderStyle, StyleBorderStyleClass.IsBorderStyleClass),
            (StyleClassFamily.BorderWidth, BorderPreflight.IsBorderWidthToken),
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
