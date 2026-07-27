using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Resolves a single utility class to the color- or length-valued style property it sets, for the
    /// non-transform half of a spring/bezier variant delta (see <see cref="MotionSpringClassParser"/> for the
    /// opacity/translate/scale/rotate half and for the from/to pairing that consumes this).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value is derived from the class STRING; <c>resolvedStyle</c> is never consulted. The class swap and
    /// the plan construction happen in one synchronous call with no style resolution in between, and the path
    /// runs off-panel (a standalone Motion's mount enter resolves during element creation), so a resolved value
    /// simply does not exist to read yet.
    /// </para>
    /// <para>
    /// Scope. Colors: <c>bg-</c> / <c>text-</c> / <c>border-</c> against the palette (<see cref="VelvetPalette"/>),
    /// the <c>/alpha</c> modifier, and the bracket forms. Lengths: the spacing-scale families (sizing, position,
    /// padding, margin, flex-basis), the <c>rounded-*</c> radius scale, the <c>border-*</c> width scale, and every
    /// bracket form <see cref="StyleArbitraryValueResolver.TryParse"/> already resolves onto one of those
    /// properties. Deliberately NOT recognized, each because the class alone cannot yield a number to
    /// interpolate: semantic theme tokens (<c>bg-primary</c>, <c>text-current</c>) have no C# mirror of their
    /// <c>--color-*</c> value; keyword lengths (<c>w-auto</c>, <c>w-full</c>, <c>basis-auto</c>) are not
    /// magnitudes; <c>rounded-full</c> is a saturating pill sentinel; preset font sizes (<c>text-lg</c>) resolve
    /// through <c>--text-*</c> tokens with no C# mirror, so only the bracket form (<c>text-[20px]</c>) is
    /// claimed. An unrecognized class is simply skipped — it still applies as a plain class, it just is not
    /// interpolated.
    /// </para>
    /// </remarks>
    internal static class MotionPropertyClassParser
    {
        // The utility families whose numeric suffix reads against the --space-* scale. The prefix → property
        // mapping itself comes from StyleArbitraryValueResolver.TryGetProperty (single-sourced with the
        // bracket-form dispatch); this set adds the fact that prefix's USS family is spacing-scaled, which is
        // exactly what distinguishes it from the rounded-*/border-* families below that share that same
        // prefix table but read different scales.
        private static readonly HashSet<string> s_spacingScaleFamilies = new(StringComparer.Ordinal)
        {
            "w-", "h-", "min-w-", "min-h-", "max-w-", "max-h-", "size-", "basis-",
            "top-", "right-", "bottom-", "left-", "inset-", "inset-x-", "inset-y-",
            "p-", "px-", "py-", "pt-", "pr-", "pb-", "pl-",
            "m-", "mx-", "my-", "mt-", "mr-", "mb-", "ml-",
        };

        // Mirrors the literal border-width declarations in _borders.uss (no token indirection there, so the
        // pixel values live directly in this table). The bare `border` / `border-t` / … forms carry no suffix
        // and mean 1px, which is what the empty key covers.
        private static readonly Dictionary<string, float> s_borderWidthScale = new(StringComparer.Ordinal)
        {
            [""] = 1f, ["0"] = 0f, ["2"] = 2f, ["4"] = 4f, ["8"] = 8f,
        };

        // The suffix-less utilities in the two non-spacing families, mapped to the prefix form
        // TryGetProperty understands so the whole prefix → property mapping stays in that one table.
        private static readonly Dictionary<string, string> s_bareForms = new(StringComparer.Ordinal)
        {
            ["rounded"] = "rounded-", ["rounded-t"] = "rounded-t-", ["rounded-r"] = "rounded-r-",
            ["rounded-b"] = "rounded-b-", ["rounded-l"] = "rounded-l-", ["rounded-tl"] = "rounded-tl-",
            ["rounded-tr"] = "rounded-tr-", ["rounded-bl"] = "rounded-bl-", ["rounded-br"] = "rounded-br-",
            ["border"] = "border-", ["border-t"] = "border-t-", ["border-r"] = "border-r-",
            ["border-b"] = "border-b-", ["border-l"] = "border-l-",
        };

        /// <summary>
        /// Resolves <paramref name="className"/> to the property it sets and the value it sets it to, or returns
        /// false when the class names nothing this parser drives (see the type doc's scope).
        /// </summary>
        internal static bool TryParse(string className, out ArbitraryStyle style)
        {
            style = default;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            var core = StyleArbitraryValueResolver.StripImportant(className, out _);

            // Bracket forms, the color-opacity modifier, negative margins and sizing fractions all already
            // resolve through the arbitrary-value dispatch; only the in-scope subset of its properties is
            // claimed here, so an opacity/transform/filter token stays with its own owner.
            if (StyleArbitraryValueResolver.TryParse(core, out var arbitrary) && IsDrivable(arbitrary.Property))
            {
                style = arbitrary;
                return true;
            }

            return TryParsePaletteColor(core, out style)
                || TryParsePresetLength(core, out style);
        }

        /// <summary>True for the color-valued properties, whose channel interpolates a <see cref="UnityEngine.Color"/>.</summary>
        internal static bool IsColor(ArbitraryProperty property)
            => property is ArbitraryProperty.TextColor or ArbitraryProperty.BackgroundColor or ArbitraryProperty.BorderColor;

        // The properties a per-frame driver may own. Opacity and the transform quartet are excluded because
        // MotionSpringClassParser's own axes already drive them (with their identity-value fallbacks and the
        // translate/scale composition their shared inline styles need); filter-* is excluded because
        // StyleFilterTransitionDriver owns the composed inline filter list; aspect-ratio and
        // transition-duration are excluded as configuration rather than visual magnitudes.
        private static bool IsDrivable(ArbitraryProperty property) => property switch
        {
            ArbitraryProperty.TextColor or ArbitraryProperty.BackgroundColor or ArbitraryProperty.BorderColor => true,
            ArbitraryProperty.Width or ArbitraryProperty.Height or ArbitraryProperty.MinWidth
                or ArbitraryProperty.MinHeight or ArbitraryProperty.MaxWidth or ArbitraryProperty.MaxHeight
                or ArbitraryProperty.Size or ArbitraryProperty.FlexBasis => true,
            ArbitraryProperty.Top or ArbitraryProperty.Right or ArbitraryProperty.Bottom or ArbitraryProperty.Left
                or ArbitraryProperty.Inset or ArbitraryProperty.InsetX or ArbitraryProperty.InsetY => true,
            ArbitraryProperty.Padding or ArbitraryProperty.PaddingX or ArbitraryProperty.PaddingY
                or ArbitraryProperty.PaddingTop or ArbitraryProperty.PaddingRight
                or ArbitraryProperty.PaddingBottom or ArbitraryProperty.PaddingLeft => true,
            ArbitraryProperty.Margin or ArbitraryProperty.MarginX or ArbitraryProperty.MarginY
                or ArbitraryProperty.MarginTop or ArbitraryProperty.MarginRight
                or ArbitraryProperty.MarginBottom or ArbitraryProperty.MarginLeft => true,
            ArbitraryProperty.BorderRadius or ArbitraryProperty.BorderTopRadius or ArbitraryProperty.BorderRightRadius
                or ArbitraryProperty.BorderBottomRadius or ArbitraryProperty.BorderLeftRadius
                or ArbitraryProperty.BorderTopLeftRadius or ArbitraryProperty.BorderTopRightRadius
                or ArbitraryProperty.BorderBottomLeftRadius or ArbitraryProperty.BorderBottomRightRadius => true,
            ArbitraryProperty.BorderWidth or ArbitraryProperty.BorderTopWidth or ArbitraryProperty.BorderRightWidth
                or ArbitraryProperty.BorderBottomWidth or ArbitraryProperty.BorderLeftWidth => true,
            ArbitraryProperty.FontSize or ArbitraryProperty.LetterSpacing => true,
            _ => false,
        };

        // bg-/text-/border- against a palette NAME (bg-red-500, text-white, border-black). The bracket and
        // /alpha spellings never reach here — the arbitrary-value dispatch above already claimed them. A
        // semantic token (bg-primary, border-default) resolves to nothing, which is what keeps it out of scope.
        private static bool TryParsePaletteColor(string core, out ArbitraryStyle style)
        {
            style = default;
            ArbitraryProperty property;
            int prefixLength;
            if (core.StartsWith("bg-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.BackgroundColor;
                prefixLength = 3;
            }
            else if (core.StartsWith("text-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.TextColor;
                prefixLength = 5;
            }
            else if (core.StartsWith("border-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.BorderColor;
                prefixLength = 7;
            }
            else
            {
                return false;
            }

            if (!VelvetPalette.TryResolveColorToken(core.Substring(prefixLength), out var color))
            {
                return false;
            }
            style = new ArbitraryStyle(property, color);
            return true;
        }

        // The non-bracket preset names, whose magnitude comes from the numeric scale their USS family uses.
        // A candidate split is accepted only when the prefix names a family AND the suffix resolves on that
        // family's scale, so a longer prefix still wins (`inset-` + `x-4` fails the suffix check and the loop
        // goes on to `inset-x-` + `4`) without a separate longest-first ordering to keep in sync.
        private static bool TryParsePresetLength(string core, out ArbitraryStyle style)
        {
            style = default;
            if (s_bareForms.TryGetValue(core, out var barePrefix))
            {
                return TryBuildPreset(barePrefix, string.Empty, out style);
            }

            for (var i = core.IndexOf('-'); i >= 0; i = core.IndexOf('-', i + 1))
            {
                if (TryBuildPreset(core.Substring(0, i + 1), core.Substring(i + 1), out style))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryBuildPreset(string prefix, string suffix, out ArbitraryStyle style)
        {
            style = default;
            if (!StyleArbitraryValueResolver.TryGetProperty(prefix, out var property))
            {
                return false;
            }

            float px;
            if (s_spacingScaleFamilies.Contains(prefix))
            {
                if (!StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out px))
                {
                    return false;
                }
            }
            else if (prefix.StartsWith("rounded", StringComparison.Ordinal))
            {
                if (!StyleShadowClass.TryGetRadiusPx(suffix, out px))
                {
                    return false;
                }
            }
            else if (prefix.StartsWith("border", StringComparison.Ordinal))
            {
                if (!s_borderWidthScale.TryGetValue(suffix, out px))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            style = new ArbitraryStyle(property, px, LengthUnit.Pixel);
            return true;
        }
    }
}
