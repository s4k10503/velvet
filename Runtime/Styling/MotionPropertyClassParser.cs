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
    /// padding, margin, flex-basis, with <c>-full</c> as 100%), the <c>rounded-*</c> radius scale
    /// (<c>rounded-full</c> as 9999px), the <c>border-*</c> width scale, the <c>text-*</c> font-size scale and
    /// <c>tracking-normal</c>, and every bracket form <see cref="StyleArbitraryValueResolver.TryParse"/> already
    /// resolves onto one of those properties. Deliberately NOT recognized, each because the class alone cannot
    /// yield a number to interpolate: semantic theme tokens (<c>bg-primary</c>, <c>text-current</c>) have no C#
    /// mirror of their <c>--color-*</c> value; <c>-auto</c> lengths (<c>w-auto</c>, <c>basis-auto</c>,
    /// <c>top-auto</c>) are not magnitudes; a bracketed length only an element can measure (<c>w-[2em]</c>,
    /// <c>w-[50vw]</c>, <c>w-[calc(50%-1rem)]</c>) is a length of no fixed size. An unrecognized class is simply
    /// skipped — it still applies as a plain class, it just is not interpolated.
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

        // The aspect-ratio rules in _state_variants.uss; MotionPresetScaleTests pins them.
        private static readonly Dictionary<string, float> s_aspectPresets = new(StringComparer.Ordinal)
        {
            ["aspect-square"] = 1f, ["aspect-video"] = 1.7778f,
        };

        // The families whose `-full` is 100% of the containing block; padding and margin have no such utility.
        private static readonly HashSet<string> s_fullFamilies = new(StringComparer.Ordinal)
        {
            "w-", "h-", "min-w-", "min-h-", "max-w-", "max-h-", "size-", "basis-",
            "top-", "right-", "bottom-", "left-", "inset-", "inset-x-", "inset-y-",
        };

        // --radius-full in _tokens.uss; MotionPresetScaleTests pins it.
        internal const float FullRadiusPx = 9999f;

        // --text-* in _tokens.uss; MotionPresetScaleTests pins it.
        private static readonly Dictionary<string, float> s_fontSizeScale = new(StringComparer.Ordinal)
        {
            ["xs"] = 12f, ["sm"] = 14f, ["base"] = 16f, ["lg"] = 18f, ["xl"] = 20f, ["2xl"] = 24f,
            ["3xl"] = 30f, ["4xl"] = 36f, ["5xl"] = 48f, ["6xl"] = 60f, ["7xl"] = 72f, ["8xl"] = 96f,
            ["9xl"] = 128f,
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
            if (StyleArbitraryValueResolver.TryParse(core, out var arbitrary) && !arbitrary.Auto
                && arbitrary.Expression == null && IsDrivable(PhysicalSide(arbitrary.Property)))
            {
                var physical = PhysicalSide(arbitrary.Property);
                style = physical == arbitrary.Property ? arbitrary : new ArbitraryStyle(physical, arbitrary.Color);
                return true;
            }

            if (s_aspectPresets.TryGetValue(core, out var ratio))
            {
                style = new ArbitraryStyle(ArbitraryProperty.AspectRatio, ratio, LengthUnit.Pixel);
                return true;
            }

            return TryParsePaletteColor(core, out style)
                || TryParsePresetLength(core, out style);
        }

        // A logical side's color drives the physical side it writes (StyleBorderSideColor), whose channel it shares.
        private static ArbitraryProperty PhysicalSide(ArbitraryProperty property)
            => s_physicalSides.TryGetValue(property, out var physical) ? physical : property;

        private static readonly Dictionary<ArbitraryProperty, ArbitraryProperty> s_physicalSides = new()
        {
            [ArbitraryProperty.BorderInlineStartColor] = ArbitraryProperty.BorderLeftColor,
            [ArbitraryProperty.BorderInlineEndColor] = ArbitraryProperty.BorderRightColor,
            [ArbitraryProperty.BorderBlockStartColor] = ArbitraryProperty.BorderTopColor,
            [ArbitraryProperty.BorderBlockEndColor] = ArbitraryProperty.BorderBottomColor,
        };

        /// <summary>True for the color-valued properties, whose channel interpolates a <see cref="UnityEngine.Color"/>.</summary>
        internal static bool IsColor(ArbitraryProperty property)
            => property is ArbitraryProperty.TextColor or ArbitraryProperty.BackgroundColor or ArbitraryProperty.BorderColor
                or ArbitraryProperty.BorderTopColor or ArbitraryProperty.BorderRightColor
                or ArbitraryProperty.BorderBottomColor or ArbitraryProperty.BorderLeftColor
                or ArbitraryProperty.BorderXColor or ArbitraryProperty.BorderYColor;

        /// <summary>
        /// True when the magnitude of <paramref name="property"/> can legitimately go negative (a pulled-in
        /// margin, an offset past its edge, tightened tracking). Every other length is a non-negative extent, so
        /// a driver overshooting below zero has to saturate rather than emit a value nothing can render.
        /// </summary>
        // Why the discard stays is in ExhaustiveSwitchSeverityTests' KnownValueAnsweringSites, with the
        // two Router.cs switches it lists beside this one.
        internal static bool AllowsNegativeLength(ArbitraryProperty property) => property switch
        {
            ArbitraryProperty.MarginTop or ArbitraryProperty.MarginRight or ArbitraryProperty.MarginBottom
                or ArbitraryProperty.MarginLeft or ArbitraryProperty.Margin or ArbitraryProperty.MarginX
                or ArbitraryProperty.MarginY => true,
            ArbitraryProperty.Top or ArbitraryProperty.Right or ArbitraryProperty.Bottom or ArbitraryProperty.Left
                or ArbitraryProperty.Inset or ArbitraryProperty.InsetX or ArbitraryProperty.InsetY => true,
            ArbitraryProperty.LetterSpacing => true,
            _ => false,
        };

        // The properties a per-frame driver may own. Opacity and the transform quartet are excluded because
        // MotionSpringClassParser's own axes already drive them (with their identity-value fallbacks and the
        // translate/scale composition their shared inline styles need); filter-* is excluded because
        // StyleFilterTransitionDriver owns the composed inline filter list; transition-duration is excluded as
        // configuration rather than a visual magnitude.
        private static readonly bool[] s_drivable = BuildDrivable();

        private static bool[] BuildDrivable()
        {
            var drivable = new[]
            {
                ArbitraryProperty.TextColor, ArbitraryProperty.BackgroundColor, ArbitraryProperty.BorderColor,
                ArbitraryProperty.BorderTopColor, ArbitraryProperty.BorderRightColor,
                ArbitraryProperty.BorderBottomColor, ArbitraryProperty.BorderLeftColor,
                ArbitraryProperty.BorderXColor, ArbitraryProperty.BorderYColor,

                ArbitraryProperty.Width, ArbitraryProperty.Height, ArbitraryProperty.MinWidth,
                ArbitraryProperty.MinHeight, ArbitraryProperty.MaxWidth, ArbitraryProperty.MaxHeight,
                ArbitraryProperty.Size, ArbitraryProperty.FlexBasis,

                ArbitraryProperty.Top, ArbitraryProperty.Right, ArbitraryProperty.Bottom, ArbitraryProperty.Left,
                ArbitraryProperty.Inset, ArbitraryProperty.InsetX, ArbitraryProperty.InsetY,

                ArbitraryProperty.Padding, ArbitraryProperty.PaddingX, ArbitraryProperty.PaddingY,
                ArbitraryProperty.PaddingTop, ArbitraryProperty.PaddingRight,
                ArbitraryProperty.PaddingBottom, ArbitraryProperty.PaddingLeft,

                ArbitraryProperty.Margin, ArbitraryProperty.MarginX, ArbitraryProperty.MarginY,
                ArbitraryProperty.MarginTop, ArbitraryProperty.MarginRight,
                ArbitraryProperty.MarginBottom, ArbitraryProperty.MarginLeft,

                ArbitraryProperty.BorderRadius, ArbitraryProperty.BorderTopRadius, ArbitraryProperty.BorderRightRadius,
                ArbitraryProperty.BorderBottomRadius, ArbitraryProperty.BorderLeftRadius,
                ArbitraryProperty.BorderTopLeftRadius, ArbitraryProperty.BorderTopRightRadius,
                ArbitraryProperty.BorderBottomLeftRadius, ArbitraryProperty.BorderBottomRightRadius,

                ArbitraryProperty.BorderWidth, ArbitraryProperty.BorderTopWidth, ArbitraryProperty.BorderRightWidth,
                ArbitraryProperty.BorderBottomWidth, ArbitraryProperty.BorderLeftWidth,

                ArbitraryProperty.FontSize, ArbitraryProperty.LetterSpacing, ArbitraryProperty.AspectRatio,
            };

            var table = new bool[Enum.GetValues(typeof(ArbitraryProperty)).Length];
            foreach (var property in drivable)
            {
                table[(int)property] = true;
            }
            return table;
        }

        internal static bool IsDrivable(ArbitraryProperty property)
        {
            var index = (int)property;
            return index >= 0 && index < s_drivable.Length && s_drivable[index];
        }

        /// <summary>
        /// Resolves <c>w-auto</c>, <c>top-auto</c> and the rest of the <c>-auto</c> lengths to the property they leave
        /// unset: a mode with no magnitude, whose value a drive reads off the element once it has had its layout.
        /// </summary>
        internal static bool TryParseAuto(string className, out ArbitraryProperty property)
        {
            property = default;
            var core = StyleArbitraryValueResolver.StripImportant(className, out _);
            if (!core.EndsWith("-auto", StringComparison.Ordinal))
            {
                return false;
            }
            var prefix = core.Substring(0, core.Length - "auto".Length);
            return s_fullFamilies.Contains(prefix) && !prefix.StartsWith("max-", StringComparison.Ordinal)
                && StyleArbitraryValueResolver.TryGetProperty(prefix, out property);
        }

        // The semantic colour utilities, each with the custom property its sheet rule reads. Those properties change
        // with the theme, so a drive reads them off the live element (MotionSlotContext.TryReadThemeColor) rather than
        // mirroring a value; MotionPresetScaleTests pins this table against _palette.uss.
        private static readonly Dictionary<string, string> s_themeColors = BuildThemeColors();

        private static Dictionary<string, string> BuildThemeColors()
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[]
                {
                    "background", "danger", "highlight", "neutral", "overlay", "primary", "primary-faint", "primary-soft",
                    "success", "surface", "surface-strong", "white-soft",
                })
            {
                table["bg-" + name] = "--color-" + name;
            }
            table["text-accent"] = "--color-accent-strong";
            table["text-strong"] = "--color-text";
            table["text-subtle"] = "--color-text-subtle";
            table["border-accent"] = "--color-accent-strong";
            table["border-default"] = "--color-border";
            table["border-strong"] = "--color-border-strong";
            return table;
        }

        internal static IReadOnlyDictionary<string, string> ThemeColors => s_themeColors;

        /// <summary>
        /// Resolves a semantic colour utility (<c>bg-primary</c>, <c>border-default</c>) to the colour its custom
        /// property holds on the element <paramref name="context"/> reads, which a theme change moves; false off a
        /// panel, where the element resolves none.
        /// </summary>
        internal static bool TryParseThemeColor(string className, MotionSlotContext context, out ArbitraryStyle style)
        {
            style = default;
            var core = StyleArbitraryValueResolver.StripImportant(className, out _);
            if (!s_themeColors.TryGetValue(core, out var variable) || !context.TryReadThemeColor(variable, out var color))
            {
                return false;
            }
            var property = core.StartsWith("bg-", StringComparison.Ordinal) ? ArbitraryProperty.BackgroundColor
                : core.StartsWith("text-", StringComparison.Ordinal) ? ArbitraryProperty.TextColor
                : ArbitraryProperty.BorderColor;
            style = new ArbitraryStyle(property, color);
            return true;
        }

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
            var unit = LengthUnit.Pixel;
            if (s_spacingScaleFamilies.Contains(prefix))
            {
                if (suffix == "full" && s_fullFamilies.Contains(prefix))
                {
                    px = 100f;
                    unit = LengthUnit.Percent;
                }
                else if (!StyleArbitraryValueResolver.TryGetSpacingPx(suffix, out px))
                {
                    return false;
                }
            }
            else if (prefix.StartsWith("rounded", StringComparison.Ordinal))
            {
                if (suffix == "full")
                {
                    px = FullRadiusPx;
                }
                else if (!StyleShadowClass.TryGetRadiusPx(suffix, out px))
                {
                    return false;
                }
            }
            else if (prefix == "text-")
            {
                if (!s_fontSizeScale.TryGetValue(suffix, out px))
                {
                    return false;
                }
            }
            else if (prefix == "tracking-")
            {
                // The other presets are em lengths measured on the element, which a play does not carry.
                if (suffix != "normal")
                {
                    return false;
                }
                px = 0f;
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

            style = new ArbitraryStyle(property, px, unit);
            return true;
        }
    }
}
