#nullable enable
using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Tailwind's logical-direction utilities (ms-4, start-1/2, border-s-2, rounded-ss-lg), each resolved as
    // the physical utility it equals with the inline axis read left to right and the block axis top to
    // bottom: inline-start is left, inline-end right, block-start top, block-end bottom. Nothing here reads a
    // direction, so a right-to-left language does not flip them.
    //
    // They resolve inline, as the physical property they stand for. A USS class was rejected: it needs a rule
    // in the bundled sheets and a regenerated StyleUtilityProperties, and an inline layer already takes part
    // in the cascade beside the classes (StyleClassProjection). A bracket value is handed to the physical
    // utility's own parser with the prefix swapped, so it accepts exactly what the physical one does.
    //
    // Between two rules of one variant rank the position is the logical property's, not the physical one's:
    // Tailwind emits ms-4 ahead of ml-8 whatever order they are written in, because margin-inline-start sorts
    // before margin-left, so StyleRuleOrder asks SortPropertiesOf for the names to place a logical token by.
    // Base utilities are not ranked by that order (see StyleClassProjection).
    internal static class StyleLogicalUtilities
    {
        private enum Scale
        {
            Margin,
            Padding,
            Inset,
            BorderWidth,
            Radius,
        }

        private readonly struct Family
        {
            public readonly string Name;
            // The physical utility's name without the trailing '-', swapped in for a bracket value.
            public readonly string Physical;
            public readonly ArbitraryProperty Property;
            public readonly Scale Scale;
            // Tailwind's names for the properties the utility writes, in property-order.ts.
            public readonly string[] SortProperties;

            public Family(string name, string physical, ArbitraryProperty property, Scale scale, params string[] sortProperties)
            {
                Name = name;
                Physical = physical;
                Property = property;
                Scale = scale;
                SortProperties = sortProperties;
            }

            // border-s and rounded-s are utilities on their own, with the default width and radius.
            public bool IsBareAllowed => Scale == Scale.BorderWidth || Scale == Scale.Radius;

            // Of these families, Tailwind negates the margin and inset ones.
            public bool IsNegatable => Scale == Scale.Margin || Scale == Scale.Inset;
        }

        private static readonly Family[] s_families =
        {
            new("ms", "ml", ArbitraryProperty.MarginLeft, Scale.Margin, "margin-inline-start"),
            new("me", "mr", ArbitraryProperty.MarginRight, Scale.Margin, "margin-inline-end"),
            new("mbs", "mt", ArbitraryProperty.MarginTop, Scale.Margin, "margin-block-start"),
            new("mbe", "mb", ArbitraryProperty.MarginBottom, Scale.Margin, "margin-block-end"),

            new("ps", "pl", ArbitraryProperty.PaddingLeft, Scale.Padding, "padding-inline-start"),
            new("pe", "pr", ArbitraryProperty.PaddingRight, Scale.Padding, "padding-inline-end"),
            new("pbs", "pt", ArbitraryProperty.PaddingTop, Scale.Padding, "padding-block-start"),
            new("pbe", "pb", ArbitraryProperty.PaddingBottom, Scale.Padding, "padding-block-end"),

            new("start", "left", ArbitraryProperty.Left, Scale.Inset, "inset-inline-start"),
            new("end", "right", ArbitraryProperty.Right, Scale.Inset, "inset-inline-end"),
            new("inset-s", "left", ArbitraryProperty.Left, Scale.Inset, "inset-inline-start"),
            new("inset-e", "right", ArbitraryProperty.Right, Scale.Inset, "inset-inline-end"),
            new("inset-bs", "top", ArbitraryProperty.Top, Scale.Inset, "inset-block-start"),
            new("inset-be", "bottom", ArbitraryProperty.Bottom, Scale.Inset, "inset-block-end"),

            new("border-s", "border-l", ArbitraryProperty.BorderLeftWidth, Scale.BorderWidth, "border-inline-start-width"),
            new("border-e", "border-r", ArbitraryProperty.BorderRightWidth, Scale.BorderWidth, "border-inline-end-width"),
            new("border-bs", "border-t", ArbitraryProperty.BorderTopWidth, Scale.BorderWidth, "border-block-start-width"),
            new("border-be", "border-b", ArbitraryProperty.BorderBottomWidth, Scale.BorderWidth, "border-block-end-width"),

            new("rounded-s", "rounded-l", ArbitraryProperty.BorderLeftRadius, Scale.Radius,
                "border-start-start-radius", "border-end-start-radius"),
            new("rounded-e", "rounded-r", ArbitraryProperty.BorderRightRadius, Scale.Radius,
                "border-start-end-radius", "border-end-end-radius"),
            new("rounded-ss", "rounded-tl", ArbitraryProperty.BorderTopLeftRadius, Scale.Radius,
                "border-start-start-radius"),
            new("rounded-se", "rounded-tr", ArbitraryProperty.BorderTopRightRadius, Scale.Radius,
                "border-start-end-radius"),
            new("rounded-es", "rounded-bl", ArbitraryProperty.BorderBottomLeftRadius, Scale.Radius,
                "border-end-start-radius"),
            new("rounded-ee", "rounded-br", ArbitraryProperty.BorderBottomRightRadius, Scale.Radius,
                "border-end-end-radius"),
        };

        // Mirrors --radius-full in _tokens.uss, which StyleShadowClass.TryGetRadiusPx leaves out as a sentinel
        // rather than a magnitude; LogicalUtilityStyleSheetMirrorTests pins it.
        private const float FullRadiusPx = 9999f;

        // The physical property a logical token writes and its value, for the forms the physical utility
        // takes (scale, bracket, and for an inset also fraction, full and auto, negated where Tailwind
        // negates). False for anything else, including a token that is not a logical utility at all.
        internal static bool TryParse(string cls, out ArbitraryStyle result)
        {
            result = default;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            var negate = cls[0] == '-';
            if (!TryFindFamily(cls.AsSpan(negate ? 1 : 0), out var family, out var suffix)
                || (negate && !family.IsNegatable))
            {
                return false;
            }
            if (suffix.Length > 0 && suffix[0] == '[')
            {
                return StyleArbitraryValueResolver.TryParse(
                    (negate ? "-" : string.Empty) + family.Physical + "-" + suffix.ToString(), out result);
            }
            return family.Scale switch
            {
                Scale.Margin => TryParseSpacing(family.Property, suffix.ToString(), negate, allowAuto: true, out result),
                Scale.Padding => TryParseSpacing(family.Property, suffix.ToString(), negate, allowAuto: false, out result),
                Scale.Inset => TryParseInset(family.Property, suffix.ToString(), negate, out result),
                Scale.BorderWidth => TryParseBorderWidth(family.Property, suffix, out result),
                Scale.Radius => TryParseRadius(family.Property, suffix.ToString(), out result),
                _ => throw new ArgumentOutOfRangeException(nameof(family), family.Scale, null),
            };
        }

        // Tailwind's names for the properties core writes, or null when core is not a logical utility.
        internal static string[]? SortPropertiesOf(string core)
        {
            var body = core.AsSpan(core.Length > 0 && core[0] == '-' ? 1 : 0);
            return TryFindFamily(body, out var family, out _) ? family.SortProperties : null;
        }

        // A name followed by '-' and a value, or alone where the family has a default. The delimiter is what
        // keeps rounded-s from claiming rounded-sm and border-s from claiming border-solid.
        private static bool TryFindFamily(ReadOnlySpan<char> body, out Family family, out ReadOnlySpan<char> suffix)
        {
            foreach (var candidate in s_families)
            {
                var name = candidate.Name;
                if (body.Length < name.Length || body[0] != name[0] || !body.StartsWith(name.AsSpan()))
                {
                    continue;
                }
                if (body.Length == name.Length)
                {
                    if (candidate.IsBareAllowed)
                    {
                        family = candidate;
                        suffix = ReadOnlySpan<char>.Empty;
                        return true;
                    }
                }
                else if (body[name.Length] == '-')
                {
                    family = candidate;
                    suffix = body.Slice(name.Length + 1);
                    return true;
                }
            }
            family = default;
            suffix = default;
            return false;
        }

        private static bool TryParseSpacing(
            ArbitraryProperty property, string suffix, bool negate, bool allowAuto, out ArbitraryStyle result)
        {
            result = default;
            if (allowAuto && suffix == "auto")
            {
                if (negate)
                {
                    return false;
                }
                result = ArbitraryStyle.AutoLength(property);
                return true;
            }
            if (!StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out var px))
            {
                return false;
            }
            result = new ArbitraryStyle(property, negate ? -px : px, LengthUnit.Pixel);
            return true;
        }

        // Full and the fractions are an inset's own: a margin takes neither, as in Tailwind.
        private static bool TryParseInset(ArbitraryProperty property, string suffix, bool negate, out ArbitraryStyle result)
        {
            result = default;
            float percent;
            if (suffix == "full")
            {
                percent = 100f;
            }
            else if (!StyleArbitraryValueResolver.TryParseFractionPercent(suffix, out percent))
            {
                return TryParseSpacing(property, suffix, negate, allowAuto: true, out result);
            }
            result = new ArbitraryStyle(property, negate ? -percent : percent, LengthUnit.Percent);
            return true;
        }

        // The widths _borders.uss declares for border-l (LogicalUtilityStyleSheetMirrorTests pins them), plus 0,
        // which border-0 declares for all four sides.
        private static bool TryParseBorderWidth(ArbitraryProperty property, ReadOnlySpan<char> suffix, out ArbitraryStyle result)
        {
            result = default;
            float width;
            if (suffix.Length == 0) { width = 1f; }
            else if (suffix.SequenceEqual("0".AsSpan())) { width = 0f; }
            else if (suffix.SequenceEqual("2".AsSpan())) { width = 2f; }
            else if (suffix.SequenceEqual("4".AsSpan())) { width = 4f; }
            else if (suffix.SequenceEqual("8".AsSpan())) { width = 8f; }
            else { return false; }
            result = new ArbitraryStyle(property, width, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseRadius(ArbitraryProperty property, string suffix, out ArbitraryStyle result)
        {
            result = default;
            float px;
            if (suffix == "full")
            {
                px = FullRadiusPx;
            }
            else if (!StyleShadowClass.TryGetRadiusPx(suffix, out px))
            {
                return false;
            }
            result = new ArbitraryStyle(property, px, LengthUnit.Pixel);
            return true;
        }
    }
}
