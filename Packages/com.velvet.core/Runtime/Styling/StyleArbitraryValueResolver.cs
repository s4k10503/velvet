using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{

    // Parses utility-class arbitrary-value syntax (e.g. h-[15%], min-w-[60px], -mt-[20px],
    // text-[#fff], bg-[#1e1e1e]) and applies the result as an inline style. Resolved to inline (not USS
    // classes) because an arbitrary bracket value is unbounded — there is no USS selector that could be
    // pre-declared for it. text-[...] is overloaded: a color value sets the text color, otherwise the
    // value is a font size. bg-[#color] sets the background color (the bg-[addr:...] image
    // form is handled by StyleBackgroundImageResolver).
    internal static class StyleArbitraryValueResolver
    {
        // Strips the important modifier and reports whether it was present: a leading '!' (!bg-red-500)
        // or a trailing '!' (bg-red-500!). The bare core is returned so the caller routes
        // it normally; when important, the caller elevates the (inline-resolvable) utility to the Important
        // layer so it wins conflicts. A class-only utility (no inline form) cannot be elevated in UI Toolkit,
        // so its '!' is accepted but inert. Returns the input unchanged when no modifier is present.
        //
        // Scope: this is wired into the per-class dispatch (USS-class + inline-layer utilities). Of the
        // array-scanned subsystem utilities, the font, text-effect, z-*, gap-* and divide-* families strip
        // the bang themselves and let an important token win over the element's plain ones
        // (StyleFontClass.TryExtract, StyleTextEffectClass.Parse, StyleZIndexClass.TryExtract,
        // StyleGridClass.ExtractGaps, StyleDivideClass.TryExtract); shadow-* and clip-path-* do not
        // recognize the bang at all.
        public static string StripImportant(string className, out bool important)
        {
            important = false;
            if (string.IsNullOrEmpty(className))
            {
                return className;
            }

            if (className[0] == '!')
            {
                important = true;
                return className.Substring(1);
            }

            if (className[className.Length - 1] == '!')
            {
                important = true;
                return className.Substring(0, className.Length - 1);
            }

            return className;
        }

        // True when a class token encodes an inline value (resolved to inline style) rather than a plain
        // USS class: a bracketed arbitrary value (w-[120px]), the color-opacity modifier (bg-black/50),
        // or a static-scale name a USS selector cannot spell (-mt-2, translate-x-1/2). Plain classes — the
        // overwhelming majority — are added to the USS class list verbatim and skip the resolvers. The
        // caller strips the important bang first; an empty token resolves to false.
        public static bool IsInlineResolved(string core)
            => core.IndexOf('[') >= 0 || StyleColorValueParser.HasColorOpacityModifier(core) || MayBeStaticScale(core);

        // Parses using IndexOf + string operations rather than regex — a per-class hot path.
        public static bool TryParse(string className, out ArbitraryStyle result)
        {
            result = default;
            if (className == null)
            {
                return false;
            }

            if (StyleBorderSideColor.TryParse(className, out result)
                || StyleLogicalUtilities.TryParse(className, out result))
            {
                return true;
            }

            // Color opacity modifier: {bg|text|border}-<color>/<N> applies alpha N% to the resolved base
            // color (bg-red-500/50, text-black/75, border-white/10, bg-[#fff]/50). Detected before the
            // bracket parsing below because the palette form carries no '[' at all. A leading '-' never
            // applies to a color, so the negated form is skipped.
            if (className.Length > 0 && className[0] != '-'
                && StyleColorValueParser.TryParseColorOpacityModifier(className, out result))
            {
                return true;
            }

            var negate = className.Length > 0 && className[0] == '-';
            var offset = negate ? 1 : 0;

            // Non-bracket static-scale names that have no USS class (-mt-2, -rotate-6,
            // translate-x-1/2, -translate-x-6) resolve to the same property machinery as the bracket forms.
            // Named filter presets (blur-sm, contrast-125, hue-rotate-90, ...) are non-bracket too, parsed first.
            if (className.IndexOf('[') < 0)
            {
                if (StyleFilterValueParser.TryParseFilterPreset(className, out result))
                {
                    return true;
                }
                return TryParseStaticScale(className, out result);
            }

            var bracketStart = className.IndexOf('[', offset);
            if (bracketStart < offset + 2)
            {
                return false; // prefix is at least 2 chars (e.g. "h-").
            }

            if (className.Length < bracketStart + 2 || className[className.Length - 1] != ']')
            {
                return false;
            }

            var prefix = className.Substring(offset, bracketStart - offset);

            // Zero-alloc via Span.
            var valueSpan = className.AsSpan(bracketStart + 1, className.Length - bracketStart - 2);
            if (valueSpan.Length == 0)
            {
                return false;
            }

            return TryParseBracketValue(prefix, valueSpan, negate, out result);
        }

        private static bool TryParseBracketValue(
            string prefix, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;

            // duration-[<time>] (transition-duration) carries a TIME value, not a length, so it is parsed here
            // before the generic length path and applied out-of-band (a StyleList<TimeValue>, like the filter list).
            if (!negate && prefix == "duration-")
            {
                if (TryParseDurationSeconds(valueSpan, out var seconds))
                {
                    result = new ArbitraryStyle(ArbitraryProperty.TransitionDuration, seconds, LengthUnit.Pixel);
                    return true;
                }
                return false;
            }

            // Color-capable prefixes (text-/bg-/border-): a non-null result is returned as-is; null means
            // not claimed as a color → fall through to the length-based path (text-/border- with a non-color
            // value). bg- is color-only, so a non-color bg- value rejects (false) and the call site falls
            // through to StyleBackgroundImageResolver.
            {
                var color = StyleColorValueParser.TryParseColorPrefix(prefix, valueSpan, negate, out result);
                if (color.HasValue) return color.Value;
            }

            // StyleTransformValueParser owns which prefixes it claims and why; they resolve before the
            // length path so it can answer for them.
            {
                var transform = StyleTransformValueParser.TryParseTransformValue(prefix, valueSpan, negate, out result);
                if (transform.HasValue) return transform.Value;
            }

            // aspect-[w/h] (or a bare decimal) — a ratio, not a length. A negative ratio is meaningless and
            // a zero denominator is rejected so the class falls through as unrecognized.
            if (!negate && prefix == "aspect-")
            {
                if (!TryParseRatio(valueSpan, out var ratio))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.AspectRatio, ratio, LengthUnit.Pixel);
                return true;
            }

            // Filter functions (blur / grayscale / invert / sepia / contrast / hue-rotate / brightness /
            // saturate), routed here (not through TryGetProperty) so all filter-* utilities share the one
            // compose-and-apply path (ApplyCombinedFilter writes a single list).
            {
                var filter = StyleFilterValueParser.TryParseFilterValue(prefix, valueSpan, negate, out result);
                if (filter.HasValue) return filter.Value;
            }

            // filter-[name:args] resolves a VelvetFilters-registered custom filter; it shares the same
            // ApplyCombinedFilter compose-and-apply path as the built-ins above, appended after them.
            {
                var custom = StyleFilterValueParser.TryParseCustomFilter(prefix, valueSpan, negate, out result);
                if (custom.HasValue) return custom.Value;
            }

            if (!TryGetProperty(prefix, out var property))
            {
                return false;
            }

            if (!TryParseValue(valueSpan, out var value, out var unit))
            {
                return false;
            }

            if (negate)
            {
                value = -value;
            }

            result = new ArbitraryStyle(property, value, unit);
            return true;
        }

        #region Static-scale utility names (no bracket)

        // The preset spacing scale, mirroring --space-* in _tokens.uss (1 unit = 4px; the suffix
        // uses '-' where CSS writes '.', e.g. mt-2-5 -> 10px). Negative margins (-mt-2) and negative
        // px translates (-translate-x-6) route here because a USS selector cannot start with '-'.
        private static readonly Dictionary<string, float> s_spacingScale = new()
        {
            ["0"] = 0f, ["px"] = 1f, ["0-5"] = 2f, ["1"] = 4f, ["1-5"] = 6f, ["2"] = 8f,
            ["2-5"] = 10f, ["3"] = 12f, ["3-5"] = 14f, ["4"] = 16f, ["5"] = 20f, ["6"] = 24f,
            ["7"] = 28f, ["8"] = 32f, ["9"] = 36f, ["10"] = 40f, ["11"] = 44f, ["12"] = 48f, ["14"] = 56f,
            ["16"] = 64f, ["20"] = 80f, ["24"] = 96f, ["28"] = 112f, ["32"] = 128f, ["36"] = 144f, ["40"] = 160f,
            ["44"] = 176f, ["48"] = 192f, ["52"] = 208f, ["56"] = 224f, ["60"] = 240f, ["64"] = 256f,
            ["72"] = 288f, ["80"] = 320f, ["96"] = 384f,
        };

        // Single source for the --space-* spacing scale (1 unit = 4px). Shared so gap-* / space-* parsing
        // (StyleGapClass) resolves the same preset table as mt-* / p-* here, instead of holding a second copy
        // that could drift.
        internal static bool TryGetSpacingPx(string suffix, out float px) => s_spacingScale.TryGetValue(suffix, out px);

        // The rotate preset (degrees). Only the NEGATIVE form routes here: positive rotate-N has a
        // static USS class, while the -rotate-N name has none (USS spells negatives as .rotate-nN).
        private static readonly Dictionary<string, float> s_rotateScale = new()
        {
            ["0"] = 0f, ["1"] = 1f, ["2"] = 2f, ["3"] = 3f, ["6"] = 6f,
            ["12"] = 12f, ["45"] = 45f, ["90"] = 90f, ["180"] = 180f,
        };

        // The per-axis scale presets (scale-x-50 -> 0.5), mirroring the uniform .scale-N USS classes in
        // _transforms.uss. These have no standalone USS class because a separate .scale-x-N / .scale-y-N rule
        // would write the whole `scale: x y` and clobber the other axis; instead they route through the
        // ScaleX/ScaleY merge path (like scale-x-[..]) so the two axes compose onto one inline `scale`.
        private static readonly Dictionary<string, float> s_axisScale = new()
        {
            ["0"] = 0f, ["50"] = 0.5f, ["75"] = 0.75f, ["90"] = 0.9f, ["95"] = 0.95f,
            ["100"] = 1f, ["105"] = 1.05f, ["110"] = 1.1f, ["125"] = 1.25f, ["150"] = 1.5f,
        };

        // Single source for the uniform .scale-N USS class's own numeric scale (identical mapping to
        // s_axisScale above, just keyed by the bare suffix rather than the "scale-x-"/"scale-y-" prefixed
        // form) — shared so MotionSpringClassParser's uniform-scale recognition resolves the SAME table
        // instead of holding a second copy that could drift, mirroring TryGetSpacingPx's precedent.
        internal static bool TryGetAxisScale(string suffix, out float scale) => s_axisScale.TryGetValue(suffix, out scale);

        // Single source for the rotate preset's magnitude (degrees), keyed by the UNSIGNED suffix — shared so
        // MotionSpringClassParser's rotate-N / rotate-nN recognition resolves the SAME magnitude table instead
        // of hand-expanding a second ±copy, mirroring TryGetSpacingPx's precedent. The caller negates the
        // result itself for the "-n"-suffixed (negative) form.
        internal static bool TryGetRotateScale(string suffix, out float degrees) => s_rotateScale.TryGetValue(suffix, out degrees);

        // Maps a margin utility prefix (without the leading '-') to its shorthand ArbitraryProperty.
        private static readonly Dictionary<string, ArbitraryProperty> s_marginPrefix = new()
        {
            ["m-"] = ArbitraryProperty.Margin,
            ["mx-"] = ArbitraryProperty.MarginX,
            ["my-"] = ArbitraryProperty.MarginY,
            ["mt-"] = ArbitraryProperty.MarginTop,
            ["mr-"] = ArbitraryProperty.MarginRight,
            ["mb-"] = ArbitraryProperty.MarginBottom,
            ["ml-"] = ArbitraryProperty.MarginLeft,
        };

        // Cheap dispatch gate: true when cls is a static-scale utility with NO static USS class, so
        // the reconciler must route it to TryParseStaticScale instead of the class list. That set is exactly
        // the names a USS selector cannot spell — any '-'-prefixed margin/rotate/translate name, the
        // '/'-bearing translate, sizing and position fractions, and the positive per-axis translate/scale
        // presets (no USS class — a per-axis rule would clobber the other axis via the shorthand). Other
        // positive non-slash names (mt-2, rotate-6) keep their USS classes and are intentionally NOT claimed here.
        internal static bool MayBeStaticScale(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            // Named filter presets (blur-sm, contrast-125, hue-rotate-90, ...) are non-bracket resolver tokens
            // too, so the one dispatch gate also claims them (incl. the negated -hue-rotate-N).
            if (StyleFilterValueParser.IsFilterPreset(cls))
            {
                return true;
            }
            // Logical-direction utilities (ms-4, start-1/2, rounded-ss-lg) and per-side border colors
            // (border-b-red-500) have no USS class at all.
            if (IsFractionToken(cls) || StyleLogicalUtilities.TryParse(cls, out _)
                || StyleBorderSideColor.TryParse(cls, out _))
            {
                return true;
            }
            if (cls[0] == '-')
            {
                var body = cls.AsSpan(1);
                if (body.StartsWith("skew-"))
                {
                    return false; // -skew-x-6 is owned by StyleSkewClass, not this resolver.
                }
                return body.StartsWith("m") || body.StartsWith("rotate-") || body.StartsWith("translate-");
            }
            // Positive per-axis translate presets (translate-x-4, translate-y-2, translate-x-1/2, translate-x-full)
            // have no USS class: a .translate-x-N rule writes the whole `translate: x y` shorthand and clobbers
            // the other axis, so they route to the TranslateX/Y merge (like scale-x/y). The negative forms
            // (-translate-x-4) are already claimed by the '-' branch above.
            if (cls.StartsWith("translate-x-", StringComparison.Ordinal)
                || cls.StartsWith("translate-y-", StringComparison.Ordinal))
            {
                if (cls.IndexOf('/') >= 0)
                {
                    return true;
                }
                var suffix = cls.AsSpan("translate-x-".Length); // the x and y prefixes share a length
                return suffix.SequenceEqual("full".AsSpan()) || SuffixInKeys(suffix, s_spacingScale);
            }
            // Per-axis scale presets (scale-x-50 / scale-y-110) have no USS class; route them to the merge path.
            // The bracket form (scale-x-[..]) is claimed by the dispatch's '[' check, not here. Span-based to
            // keep this per-class hot gate allocation-free (matching the AsSpan style above).
            if (cls.StartsWith("scale-x-", StringComparison.Ordinal) || cls.StartsWith("scale-y-", StringComparison.Ordinal))
            {
                return SuffixInKeys(cls.AsSpan("scale-x-".Length), s_axisScale); // "scale-x-" and "scale-y-" share a length
            }
            return TryGetFlexFactorPreset(cls, out _, out _);
        }

        // grow-<N> / shrink-<N>: Tailwind's bare factor, a whole number spelled without a sign or a leading
        // zero. grow-0 and shrink-0 are USS classes, so zero is left to the class list.
        private static bool TryGetFlexFactorPreset(string cls, out ArbitraryProperty property, out float factor)
        {
            factor = 0f;
            int prefixLength;
            if (cls.StartsWith("grow-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.FlexGrow;
                prefixLength = "grow-".Length;
            }
            else if (cls.StartsWith("shrink-", StringComparison.Ordinal))
            {
                property = ArbitraryProperty.FlexShrink;
                prefixLength = "shrink-".Length;
            }
            else
            {
                property = default;
                return false;
            }
            if (!TryParseWhole(cls.AsSpan(prefixLength), out var whole))
            {
                return false;
            }
            factor = whole;
            return whole != 0;
        }

        // A whole number spelled as Tailwind's bare values are: digits only, no sign, no leading zero.
        private static bool TryParseWhole(ReadOnlySpan<char> digits, out int whole)
        {
            whole = 0;
            // MUTANT_SURVIVES(equivalent): the boundary moves only a lone "0", whose whole of 0 the factor
            // preset declines with `whole != 0` either way.
            if (digits.Length > 1 && digits[0] == '0')
            {
                // MUTANT_SURVIVES(equivalent): a leading zero leaves whole at 0, which the factor preset
                // declines with `whole != 0` whatever this returns.
                return false;
            }
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out whole);
        }

        // True when the suffix span matches one of the preset table's keys. Allocation-free: the Dictionary
        // KeyCollection enumerates as a struct and the compare is span-based, so this stays usable on the
        // per-class hot gates that claim translate/scale presets for the per-axis merge path.
        private static bool SuffixInKeys(ReadOnlySpan<char> suffix, Dictionary<string, float> table)
        {
            foreach (var key in table.Keys)
            {
                if (suffix.SequenceEqual(key.AsSpan()))
                {
                    return true;
                }
            }
            return false;
        }

        // Parses the non-bracket static-scale forms that have no USS class: the sizing and position fractions
        // (w-1/2, -left-1/2), negative margins on the preset scale (-mt-2 -> MarginTop -8px), the negative
        // rotate preset (-rotate-6 -> -6deg), and the translate fraction / negative px translate
        // (translate-x-1/2 -> 50%, -translate-x-6 -> -24px).
        // Returns false for anything else, so positive USS-backed names fall through to the class list.
        private static bool TryParseStaticScale(string className, out ArbitraryStyle result)
        {
            result = default;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            if (TryParseFraction(className, out result))
            {
                return true;
            }
            if (TryGetFlexFactorPreset(className, out var factorProperty, out var factor))
            {
                result = new ArbitraryStyle(factorProperty, factor, LengthUnit.Pixel);
                return true;
            }
            var negate = className[0] == '-';
            var body = negate ? className.Substring(1) : className;

            if (negate)
            {
                var negated = TryParseNegatedPreset(body, out result);
                if (negated.HasValue)
                {
                    return negated.Value;
                }
            }

            var scale = TryParseAxisScalePreset(body, negate, out result);
            if (scale.HasValue)
            {
                return scale.Value;
            }

            return TryParseTranslatePreset(body, negate, out result);
        }

        // The margin and rotate presets, whose tables carry the magnitude and take their sign from the name
        // (see s_rotateScale for why only the negated name routes here). Null when body names neither family.
        private static bool? TryParseNegatedPreset(string body, out ArbitraryStyle result)
        {
            result = default;
            foreach (var kvp in s_marginPrefix)
            {
                if (!body.StartsWith(kvp.Key, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!s_spacingScale.TryGetValue(body.Substring(kvp.Key.Length), out var px))
                {
                    return false;
                }
                result = new ArbitraryStyle(kvp.Value, -px, LengthUnit.Pixel);
                return true;
            }

            if (body.StartsWith("rotate-", StringComparison.Ordinal))
            {
                if (!s_rotateScale.TryGetValue(body.Substring("rotate-".Length), out var deg))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.Rotate, -deg, LengthUnit.Pixel);
                return true;
            }
            return null;
        }

        // Per-axis scale presets (positive only; a flip is the arbitrary scale-x-[-1]). Routed to the same
        // ScaleX/ScaleY merge as the bracket form so the axes compose onto one inline `scale`. Null when body
        // names neither axis.
        private static bool? TryParseAxisScalePreset(string body, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate)
            {
                return null;
            }
            var isScaleX = body.StartsWith("scale-x-", StringComparison.Ordinal);
            if (!isScaleX && !body.StartsWith("scale-y-", StringComparison.Ordinal))
            {
                return null;
            }
            if (!s_axisScale.TryGetValue(body.Substring("scale-x-".Length), out var factor))
            {
                return false;
            }
            result = new ArbitraryStyle(
                isScaleX ? ArbitraryProperty.ScaleX : ArbitraryProperty.ScaleY, factor, LengthUnit.Pixel);
            return true;
        }

        private static bool TryParseTranslatePreset(string body, bool negate, out ArbitraryStyle result)
        {
            result = default;
            var isX = body.StartsWith("translate-x-", StringComparison.Ordinal);
            var isY = !isX && body.StartsWith("translate-y-", StringComparison.Ordinal);
            if (!isX && !isY)
            {
                return false;
            }

            var suffix = body.Substring("translate-x-".Length); // the x and y prefixes share a length
            var property = isX ? ArbitraryProperty.TranslateX : ArbitraryProperty.TranslateY;
            // The slash form (percent of the element's own size), translate-x-full (100%) and the spacing-scale
            // presets route to the same TranslateX/Y merge so an x and a y preset compose instead of clobbering
            // via the `translate` shorthand.
            if (TryParseFractionPercent(suffix, out var pct))
            {
                result = new ArbitraryStyle(property, negate ? -pct : pct, LengthUnit.Percent);
                return true;
            }
            if (suffix == "full")
            {
                result = new ArbitraryStyle(property, negate ? -100f : 100f, LengthUnit.Percent);
                return true;
            }
            // Both signs route here now: a positive .translate-x-N USS class would write the whole
            // `translate` shorthand and clobber the other axis, so positives merge through TranslateX/Y too.
            if (s_spacingScale.TryGetValue(suffix, out var px))
            {
                result = new ArbitraryStyle(property, negate ? -px : px, LengthUnit.Pixel);
                return true;
            }
            return false;
        }

        // Parses a duration-[..] time value to SECONDS. Accepts "<n>ms" / "<n>s" (duration-[400ms] /
        // duration-[.4s]); a bare number is rejected (a unit is required on arbitrary durations).
        private static bool TryParseDurationSeconds(ReadOnlySpan<char> value, out float seconds)
        {
            seconds = 0f;
            float scale;
            ReadOnlySpan<char> num;
            if (value.EndsWith("ms".AsSpan()))
            {
                num = value.Slice(0, value.Length - 2);
                scale = 0.001f;
            }
            else if (value.EndsWith("s".AsSpan()))
            {
                num = value.Slice(0, value.Length - 1);
                scale = 1f;
            }
            else
            {
                return false;
            }
            if (!float.TryParse(num.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                || float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
            {
                return false;
            }
            seconds = v * scale;
            return true;
        }

        // The fraction families (w-1/2, min-h-1/3, basis-3/4, left-1/2, inset-x-1/4) resolve to an inline percent,
        // for any a/b of non-negative integers as in Tailwind (left-3/2 is 150%). Only a position offset takes a
        // sign (-left-1/2) and the `full` (left-full, -left-full) and `auto` keywords, as in Tailwind.
        // The inset-x-/inset-y- rows precede inset-, because the first row whose prefix matches decides.
        private static readonly (string Prefix, ArbitraryProperty Property, bool Position)[] s_fractionFamilies =
        {
            ("w-", ArbitraryProperty.Width, false),
            ("h-", ArbitraryProperty.Height, false),
            ("size-", ArbitraryProperty.Size, false),
            ("min-w-", ArbitraryProperty.MinWidth, false),
            ("min-h-", ArbitraryProperty.MinHeight, false),
            ("max-w-", ArbitraryProperty.MaxWidth, false),
            ("max-h-", ArbitraryProperty.MaxHeight, false),
            ("basis-", ArbitraryProperty.FlexBasis, false),
            ("top-", ArbitraryProperty.Top, true),
            ("right-", ArbitraryProperty.Right, true),
            ("bottom-", ArbitraryProperty.Bottom, true),
            ("left-", ArbitraryProperty.Left, true),
            ("inset-x-", ArbitraryProperty.InsetX, true),
            ("inset-y-", ArbitraryProperty.InsetY, true),
            ("inset-", ArbitraryProperty.Inset, true),
        };

        // -1 when cls names no fraction family, or negates one that takes no sign.
        private static int FractionFamilyOf(string cls)
        {
            var negate = cls[0] == '-';
            var body = cls.AsSpan(negate ? 1 : 0);
            for (var i = 0; i < s_fractionFamilies.Length; i++)
            {
                if (body.StartsWith(s_fractionFamilies[i].Prefix.AsSpan()))
                {
                    return negate && !s_fractionFamilies[i].Position ? -1 : i;
                }
            }
            return -1;
        }

        // The sizing `*-full` and `*-auto` classes are USS rules, so only a position family's keywords are claimed,
        // and `-auto` only unnegated, as TryParseFraction reads it.
        private static bool IsFractionToken(string cls)
        {
            var family = FractionFamilyOf(cls);
            return family >= 0
                && (cls.IndexOf('/') >= 0
                    || (s_fractionFamilies[family].Position
                        && (cls.EndsWith("-full", StringComparison.Ordinal)
                            || (cls[0] != '-' && cls.EndsWith("-auto", StringComparison.Ordinal)))));
        }

        // Digits only with no sign or leading zero, as Tailwind's isPositiveInteger reads a fraction's halves.
        private static bool TryParseFractionPart(string text, out int value)
        {
            value = 0;
            if (text.Length == 0 || text.Length > 9 || (text.Length > 1 && text[0] == '0'))
            {
                return false;
            }
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
                value = value * 10 + (text[i] - '0');
            }
            return true;
        }

        // A zero denominator does not parse: no percent stands for it.
        internal static bool TryParseFractionPercent(string frac, out float percent)
        {
            percent = 0f;
            var slash = frac.IndexOf('/');
            if (slash < 0
                || !TryParseFractionPart(frac.Substring(0, slash), out var n)
                || !TryParseFractionPart(frac.Substring(slash + 1), out var d)
                || d == 0)
            {
                return false;
            }
            percent = 100f * n / d;
            return true;
        }

        // `auto` is never negated, as in Tailwind, which has no -top-auto.
        private static bool TryParseFraction(string className, out ArbitraryStyle result)
        {
            result = default;
            var family = FractionFamilyOf(className);
            if (family < 0)
            {
                return false;
            }
            var (prefix, property, position) = s_fractionFamilies[family];
            var negate = className[0] == '-';
            var frac = className.Substring((negate ? 1 : 0) + prefix.Length);
            float percent;
            if (position && frac == "auto" && !negate)
            {
                result = ArbitraryStyle.AutoLength(property);
                return true;
            }
            if (position && frac == "full")
            {
                percent = 100f;
            }
            else if (!TryParseFractionPercent(frac, out percent))
            {
                return false;
            }
            result = new ArbitraryStyle(property, negate ? -percent : percent, LengthUnit.Percent);
            return true;
        }

        #endregion

        #region Delegate Table

        // Single source of truth for Apply / Clear. Shorthands hold multiple setters.
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleLength>[]> PropertySetters = new()
        {
            [ArbitraryProperty.Width] = new Action<IStyle, StyleLength>[] { (s, v) => s.width = v },
            [ArbitraryProperty.Height] = new Action<IStyle, StyleLength>[] { (s, v) => s.height = v },
            [ArbitraryProperty.MinWidth] = new Action<IStyle, StyleLength>[] { (s, v) => s.minWidth = v },
            [ArbitraryProperty.MinHeight] = new Action<IStyle, StyleLength>[] { (s, v) => s.minHeight = v },
            [ArbitraryProperty.MaxWidth] = new Action<IStyle, StyleLength>[] { (s, v) => s.maxWidth = v },
            [ArbitraryProperty.MaxHeight] = new Action<IStyle, StyleLength>[] { (s, v) => s.maxHeight = v },
            [ArbitraryProperty.Top] = new Action<IStyle, StyleLength>[] { (s, v) => s.top = v },
            [ArbitraryProperty.Right] = new Action<IStyle, StyleLength>[] { (s, v) => s.right = v },
            [ArbitraryProperty.Bottom] = new Action<IStyle, StyleLength>[] { (s, v) => s.bottom = v },
            [ArbitraryProperty.Left] = new Action<IStyle, StyleLength>[] { (s, v) => s.left = v },
            [ArbitraryProperty.Inset] = new Action<IStyle, StyleLength>[]
            {
                (s, v) => s.top = v, (s, v) => s.right = v,
                (s, v) => s.bottom = v, (s, v) => s.left = v,
            },
            [ArbitraryProperty.InsetX] = new Action<IStyle, StyleLength>[] { (s, v) => s.left = v, (s, v) => s.right = v },
            [ArbitraryProperty.InsetY] = new Action<IStyle, StyleLength>[] { (s, v) => s.top = v, (s, v) => s.bottom = v },
            [ArbitraryProperty.PaddingTop] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingTop = v },
            [ArbitraryProperty.PaddingRight] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingRight = v },
            [ArbitraryProperty.PaddingBottom] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingBottom = v },
            [ArbitraryProperty.PaddingLeft] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingLeft = v },
            [ArbitraryProperty.Padding] = new Action<IStyle, StyleLength>[]
            {
                (s, v) => s.paddingTop = v, (s, v) => s.paddingRight = v,
                (s, v) => s.paddingBottom = v, (s, v) => s.paddingLeft = v,
            },
            [ArbitraryProperty.PaddingX] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingLeft = v, (s, v) => s.paddingRight = v },
            [ArbitraryProperty.PaddingY] = new Action<IStyle, StyleLength>[] { (s, v) => s.paddingTop = v, (s, v) => s.paddingBottom = v },
            [ArbitraryProperty.MarginTop] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginTop = v },
            [ArbitraryProperty.MarginRight] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginRight = v },
            [ArbitraryProperty.MarginBottom] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginBottom = v },
            [ArbitraryProperty.MarginLeft] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginLeft = v },
            [ArbitraryProperty.Margin] = new Action<IStyle, StyleLength>[]
            {
                (s, v) => s.marginTop = v, (s, v) => s.marginRight = v,
                (s, v) => s.marginBottom = v, (s, v) => s.marginLeft = v,
            },
            [ArbitraryProperty.MarginX] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginLeft = v, (s, v) => s.marginRight = v },
            [ArbitraryProperty.MarginY] = new Action<IStyle, StyleLength>[] { (s, v) => s.marginTop = v, (s, v) => s.marginBottom = v },
            [ArbitraryProperty.FontSize] = new Action<IStyle, StyleLength>[] { (s, v) => s.fontSize = v },
            [ArbitraryProperty.LetterSpacing] = new Action<IStyle, StyleLength>[] { (s, v) => s.letterSpacing = v },
            // size-[..] fans out to width + height (same dual-setter shape as Inset).
            [ArbitraryProperty.Size] = new Action<IStyle, StyleLength>[] { (s, v) => s.width = v, (s, v) => s.height = v },
            [ArbitraryProperty.FlexBasis] = new Action<IStyle, StyleLength>[] { (s, v) => s.flexBasis = v },
        };

        // The corner radii are not written here but handed to CornerRadiusFit, which owns those four slots.
        private static readonly Dictionary<ArbitraryProperty, RadiusCorners> RadiusCornersOf = new()
        {
            [ArbitraryProperty.BorderRadius] = RadiusCorners.All,
            [ArbitraryProperty.BorderTopRadius] = RadiusCorners.TopLeft | RadiusCorners.TopRight,
            [ArbitraryProperty.BorderRightRadius] = RadiusCorners.TopRight | RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderBottomRadius] = RadiusCorners.BottomLeft | RadiusCorners.BottomRight,
            [ArbitraryProperty.BorderLeftRadius] = RadiusCorners.TopLeft | RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderTopLeftRadius] = RadiusCorners.TopLeft,
            [ArbitraryProperty.BorderTopRightRadius] = RadiusCorners.TopRight,
            [ArbitraryProperty.BorderBottomLeftRadius] = RadiusCorners.BottomLeft,
            [ArbitraryProperty.BorderBottomRightRadius] = RadiusCorners.BottomRight,
        };

        // Color-valued counterpart to PropertySetters. Color properties take a
        // StyleColor rather than a StyleLength.
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleColor>[]> ColorSetters = new()
        {
            [ArbitraryProperty.TextColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.color = v },
            [ArbitraryProperty.BackgroundColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.backgroundColor = v },
            [ArbitraryProperty.BorderColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderTopColor = v, (s, v) => s.borderRightColor = v,
                (s, v) => s.borderBottomColor = v, (s, v) => s.borderLeftColor = v,
            },
            [ArbitraryProperty.BorderTopColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderTopColor = v },
            [ArbitraryProperty.BorderRightColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderRightColor = v },
            [ArbitraryProperty.BorderBottomColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderBottomColor = v },
            [ArbitraryProperty.BorderLeftColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderLeftColor = v },
            [ArbitraryProperty.BorderXColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderLeftColor = v, (s, v) => s.borderRightColor = v,
            },
            [ArbitraryProperty.BorderYColor] = new Action<IStyle, StyleColor>[]
            {
                (s, v) => s.borderTopColor = v, (s, v) => s.borderBottomColor = v,
            },
            [ArbitraryProperty.BorderInlineStartColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderLeftColor = v },
            [ArbitraryProperty.BorderInlineEndColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderRightColor = v },
            [ArbitraryProperty.BorderBlockStartColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderTopColor = v },
            [ArbitraryProperty.BorderBlockEndColor] = new Action<IStyle, StyleColor>[] { (s, v) => s.borderBottomColor = v },
        };

        // Float-valued counterpart to PropertySetters. Border widths are
        // StyleFloat (pixels) rather than StyleLength; the value's unit
        // is ignored (percent border widths are not meaningful).
        private static readonly Dictionary<ArbitraryProperty, Action<IStyle, StyleFloat>[]> FloatSetters = new()
        {
            [ArbitraryProperty.BorderWidth] = new Action<IStyle, StyleFloat>[]
            {
                (s, v) => s.borderTopWidth = v, (s, v) => s.borderRightWidth = v,
                (s, v) => s.borderBottomWidth = v, (s, v) => s.borderLeftWidth = v,
            },
            [ArbitraryProperty.BorderTopWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderTopWidth = v },
            [ArbitraryProperty.BorderRightWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderRightWidth = v },
            [ArbitraryProperty.BorderBottomWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderBottomWidth = v },
            [ArbitraryProperty.BorderLeftWidth] = new Action<IStyle, StyleFloat>[] { (s, v) => s.borderLeftWidth = v },
            // grow-[..] / shrink-[..] are unitless factors.
            [ArbitraryProperty.FlexGrow] = new Action<IStyle, StyleFloat>[] { (s, v) => s.flexGrow = v },
            [ArbitraryProperty.FlexShrink] = new Action<IStyle, StyleFloat>[] { (s, v) => s.flexShrink = v },
        };

        #endregion

        // Per-element, per-property stack of arbitrary-value layers keyed by priority (ascending). A
        // ConditionalWeakTable auto-drops entries when an element is GC'd; pooled (reused) elements are scrubbed
        // explicitly via ClearAll so no layer ghosts across reuse.
        private sealed class LayerMap : Dictionary<ArbitraryProperty, SortedList<long, ArbitraryStyle>>,
            StyleClassProjection.ILayerHost
        {
            // Per-NAME priority stacks for filter-[name:args] custom filters, in first-application
            // order. Unlike every other arbitrary property, a custom filter cannot share the single
            // FilterCustom slot in the base dictionary above — "dissolve" and "glow" (or a base and a
            // hover layer of the SAME name) would clobber each other. A LIST of (name, stack) entries
            // rather than a dictionary: the entry index IS the compose slot, and lookups are linear (an
            // element realistically carries a handful of names). An entry whose stack has EMPTIED is
            // kept as a tombstone rather than removed — the class-diff path updates a changed token by
            // clearing the old value and applying the new one, and dropping the entry in between would
            // re-slot the name to the end, visibly reordering two co-applied custom filters on the first
            // argument change. Compose skips empty stacks; ClearAll drops the whole map, so tombstones
            // die with the rest of the element's layer state. Lazily allocated: most elements never
            // apply a custom filter.
            public List<(string Name, SortedList<long, ArbitraryStyle> Stack)>? Customs;

            // The class-list half of the same cascade (StyleClassProjection). It lives here because the two
            // halves decide one outcome together — an inline layer a USS class outranks has to stop
            // painting, and a class an inline layer outranks has to stop counting — and because sharing this
            // map's lifetime means ClearAll scrubs both at once, so no element can reach the pool carrying
            // half a verdict. Null until some payload above the base priority needs one.
            public StyleClassProjection.Model? Projection;

            // Per property, the priority below which every layer is fully overridden by a USS class and must
            // not be written inline. Absent means nothing is masked. Lazily allocated: only an element mixing
            // a bracket value with a variant-applied class ever gets one.
            public Dictionary<ArbitraryProperty, long>? Floors;

            // Per property, the longhands its winning layer writes that a class claims at a key above the layer's.
            // Absent means no class does.
            public Dictionary<ArbitraryProperty, StyleLonghandSet>? Claims;

            // The slots a gap, grid or divide manipulator holds on this element — see Hold. Lazily
            // allocated: only an element one of them writes to gets one.
            public StyleHeldSlots? Holds;

            // The containers that re-apply when a class of this element's own changes — see
            // StyleChildOwnership. Lazily allocated: only a claimed child gets one.
            public List<IChildClassWatcher>? Watchers;

            private readonly Dictionary<(ArbitraryProperty Property, long Priority), long> _arrivals = new();
            private long _nextArrival;

            public void RecordArrival(ArbitraryProperty property, long priority)
            {
                if (_nextArrival == long.MaxValue)
                {
                    // Keep every live layer's relative age, including layers hidden below a winner.
                    // Compact only at the boundary so ordinary Apply never scans the other layers.
                    var arrivals = new List<KeyValuePair<(ArbitraryProperty, long), long>>(_arrivals);
                    arrivals.Sort((a, z) => a.Value.CompareTo(z.Value));
                    _nextArrival = 0;
                    foreach (var arrival in arrivals)
                    {
                        _arrivals[arrival.Key] = ++_nextArrival;
                    }
                }
                _arrivals[(property, priority)] = ++_nextArrival;
            }

            public long ArrivalOf(ArbitraryProperty property, long priority)
                => _arrivals[(property, priority)];

            public void RemoveArrival(ArbitraryProperty property, long priority)
                => _arrivals.Remove((property, priority));

            public bool HasLayers => Count > 0;

            public void CollectLayers(List<StyleClassProjection.InlineLayer> into)
                => CollectLayerPriorities(this, into);

            public void ApplyFloors(VisualElement element, Dictionary<ArbitraryProperty, long> floors)
                => ApplyProjectionFloors(element, this, floors);
        }

        private static readonly ConditionalWeakTable<VisualElement, LayerMap> s_layers = new();

        // The element's class projection, created (with its layer map) on demand.
        internal static StyleClassProjection.Model GetOrCreateProjection(VisualElement element)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            return map.Projection ??= new StyleClassProjection.Model(map);
        }

        // The element's class projection, or null when nothing has needed one.
        internal static StyleClassProjection.Model? TryGetProjection(VisualElement element)
            => element != null && s_layers.TryGetValue(element, out var map) ? map.Projection : null;

        internal static bool HasImportantInlineOutside(VisualElement element, StyleLonghandSet written,
            HashSet<ArbitraryProperty> swapped)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return false;
            }
            foreach (var pair in map)
            {
                if (!StyleArbitraryLonghands.Of(pair.Key).Overlaps(written))
                {
                    continue;
                }
                foreach (var priority in pair.Value.Keys)
                {
                    var swappedBase = swapped.Contains(pair.Key)
                        && priority <= StyleLayerPriority.WithRule(
                            StyleLayerPriority.ImportantOf(StyleLayerPriority.Base), int.MaxValue);
                    if ((priority & StyleLayerPriority.Important) != 0 && !swappedBase)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Reports every registered layer as (property, priority) so the projection can rank inline layers
        // against USS classes. The name-keyed custom-filter stacks are not reported: a registered name is
        // not an ArbitraryProperty, and the whole filter family is held out of the comparison anyway (see
        // StyleArbitraryLonghands), so there would be nothing to rank it against.
        private static void CollectLayerPriorities(LayerMap map, List<StyleClassProjection.InlineLayer> into)
        {
            foreach (var pair in map)
            {
                var priorities = pair.Value.Keys;
                for (var i = 0; i < priorities.Count; i++)
                {
                    into.Add(new StyleClassProjection.InlineLayer(pair.Key, priorities[i]));
                }
            }
        }

        // Installs the projection's verdict about which inline layers a USS class has overridden, and
        // re-resolves the properties whose verdict changed. Re-resolving is what actually removes the inline
        // value that was hiding the winning class, or restores it once the class stops winning.
        private static void ApplyProjectionFloors(
            VisualElement element, LayerMap map, Dictionary<ArbitraryProperty, long> floors)
        {
            var previous = map.Floors;
            if (!SameFloors(previous, floors))
            {
                map.Floors = floors.Count == 0 ? null : new Dictionary<ArbitraryProperty, long>(floors);
                ResolveMoved(element, map, previous, map.Floors);
            }
            // After the floors, which decide the winning layers the claims are measured against.
            var previousClaims = map.Claims;
            map.Claims = ClaimsAbove(map);
            ResolveMoved(element, map, previousClaims, map.Claims);
        }

        // Re-resolves every property whose entry differs between previous and current, an absent map
        // standing for an empty one.
        private static void ResolveMoved<T>(VisualElement element, LayerMap map,
            Dictionary<ArbitraryProperty, T>? previous, Dictionary<ArbitraryProperty, T>? current)
            where T : IEquatable<T>
        {
            if (current != null)
            {
                foreach (var pair in current)
                {
                    if (previous == null || !previous.TryGetValue(pair.Key, out var was) || !was.Equals(pair.Value))
                    {
                        ResolveAndApply(element, pair.Key, map);
                    }
                }
            }
            if (previous == null)
            {
                return;
            }
            foreach (var property in previous.Keys)
            {
                if (current == null || !current.ContainsKey(property))
                {
                    ResolveAndApply(element, property, map);
                }
            }
        }

        // A class claiming only some of what a winning layer writes changes no floor, so the floors alone would
        // never re-resolve that layer for ClearClaimedLonghands to give the class its longhands; a change in
        // which longhands are claimed does. Null when no class claims anything above a winning layer.
        private static Dictionary<ArbitraryProperty, StyleLonghandSet>? ClaimsAbove(LayerMap map)
        {
            if (map.Projection == null)
            {
                return null;
            }
            Dictionary<ArbitraryProperty, StyleLonghandSet>? claims = null;
            foreach (var pair in map)
            {
                if (!TryWinningLayer(map, pair.Key, out _))
                {
                    continue;
                }
                var top = pair.Value.Keys[pair.Value.Count - 1];
                var written = StyleArbitraryLonghands.Of(pair.Key);
                var claimed = StyleLonghandSet.Empty;
                foreach (var longhand in s_longhands)
                {
                    if (written.Contains(longhand) && map.Projection.ClaimOf(longhand) > top)
                    {
                        claimed = claimed.Union(StyleLonghandSet.Of(longhand));
                    }
                }
                if (!claimed.IsEmpty)
                {
                    (claims ??= new Dictionary<ArbitraryProperty, StyleLonghandSet>())[pair.Key] = claimed;
                }
            }
            return claims;
        }

        private static bool SameFloors(Dictionary<ArbitraryProperty, long>? previous, Dictionary<ArbitraryProperty, long> floors)
        {
            var previousCount = previous?.Count ?? 0;
            if (previousCount != floors.Count)
            {
                return false;
            }
            foreach (var pair in floors)
            {
                if (!previous!.TryGetValue(pair.Key, out var was) || was != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        // The highest-priority layer for property, unless a USS class has overridden it. A floor records the
        // highest priority a class beat, and layers die from the bottom up, so the top layer sitting at or
        // below the floor means every layer does.
        private static bool TryWinningLayer(LayerMap map, ArbitraryProperty property, out ArbitraryStyle style)
        {
            style = default;
            if (!map.TryGetValue(property, out var layers) || layers.Count == 0)
            {
                return false;
            }
            var top = layers.Count - 1;
            if (map.Floors != null && map.Floors.TryGetValue(property, out var floor) && layers.Keys[top] <= floor)
            {
                return false;
            }
            style = layers.Values[top];
            return true;
        }

        // Registers style at priority for its property and applies the
        // winning (highest-priority) layer inline. Base utilities use StyleLayerPriority.Base
        // (the default); variant manipulators pass their state's priority so a variant layers OVER the base.
        public static void Apply(VisualElement element, in ArbitraryStyle style, long priority = StyleLayerPriority.Base)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            if (style.Property == ArbitraryProperty.FilterCustom)
            {
                ApplyCustomFilterLayer(map, style, priority);
            }
            else
            {
                if (!map.TryGetValue(style.Property, out var layers))
                {
                    layers = new SortedList<long, ArbitraryStyle>();
                    map[style.Property] = layers;
                }
                layers[priority] = style;
                map.RecordArrival(style.Property, priority);
            }
            ResolveAndApply(element, style.Property, map);
            Reproject(element, map);
        }

        // Re-runs the class verdict after a layer changed. A new layer can outrank a class that was winning,
        // and a departing one can hand a property back to a class that was suppressed, so the two halves have
        // to be re-derived together. Nothing to do on the vast majority of elements, which never build a
        // projection at all.
        private static void Reproject(VisualElement element, LayerMap map)
        {
            if (map.Projection != null)
            {
                var previousUniform = UniformScaleClassFallback(element);
                StyleClassProjection.OnInlineLayersChanged(element, map.Projection);
                RefreshScaleClassFallback(element, map, previousUniform);
            }
        }

        // Registers a filter-[name:args] layer at priority under its own per-name stack (LayerMap.Customs),
        // appending a new entry on the name's first application so the compose order in
        // ApplyCombinedFilter is stable first-application order. A later re-apply — including one after
        // the stack emptied (its tombstone keeps the entry) — reuses the existing entry, preserving the
        // name's original compose slot.
        private static void ApplyCustomFilterLayer(LayerMap map, in ArbitraryStyle style, long priority)
        {
            var name = style.Custom!.Name;
            var stack = FindCustomStack(map, name);
            if (stack == null)
            {
                stack = new SortedList<long, ArbitraryStyle>();
                (map.Customs ??= new List<(string, SortedList<long, ArbitraryStyle>)>()).Add((name, stack));
            }
            stack[priority] = style;
        }

        // The custom filter layer stack registered under name, or null when the name has never been
        // applied to this element. Linear scan by design: see LayerMap.Customs.
        private static SortedList<long, ArbitraryStyle>? FindCustomStack(LayerMap map, string name)
        {
            var customs = map.Customs;
            if (customs == null)
            {
                return null;
            }
            for (var i = 0; i < customs.Count; i++)
            {
                if (customs[i].Name == name)
                {
                    return customs[i].Stack;
                }
            }
            return null;
        }

        // Removes the layer at priority for property and re-applies the
        // next-highest surviving layer (or clears the inline style when none remain). This is what makes a
        // variant turning off fall back to a still-active variant or the base value instead of wiping the
        // property — and what keeps the two translate axes independent.
        // NB FilterCustom layers are name-keyed (LayerMap.Customs) and only clearable through the
        // ArbitraryStyle-aware overload below, which carries the name; the property dictionary this
        // overload operates on never holds them.
        public static void Clear(VisualElement element, ArbitraryProperty property, long priority = StyleLayerPriority.Base)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                ClearInline(element, property);
                return;
            }
            if (map.TryGetValue(property, out var layers))
            {
                layers.Remove(priority);
                map.RemoveArrival(property, priority);
                if (layers.Count == 0) map.Remove(property);
            }
            ResolveAndApply(element, property, map);
            Reproject(element, map);
        }

        // Clears the layer an ArbitraryStyle applied, using the parsed value itself rather than just its
        // property. The only case this matters today is FilterCustom, whose value carries the NAME that
        // keys the layer stack to remove — and the name is ALL it reads (never the definition or the
        // arguments), which is what lets the unregistered-name clear fallback synthesize a name-only
        // style; every other property clears exactly like the (property, priority) overload.
        public static void Clear(VisualElement element, in ArbitraryStyle style, long priority = StyleLayerPriority.Base)
        {
            if (style.Property != ArbitraryProperty.FilterCustom)
            {
                Clear(element, style.Property, priority);
                return;
            }
            if (!s_layers.TryGetValue(element, out var map))
            {
                ClearInline(element, style.Property);
                return;
            }
            // The entry is intentionally KEPT when its stack empties (a tombstone holding the name's
            // compose slot): see LayerMap.Customs.
            FindCustomStack(map, style.Custom!.Name)?.Remove(priority);
            ResolveAndApply(element, ArbitraryProperty.FilterCustom, map);
            Reproject(element, map);
        }

        // Re-asserts the winning layer for EVERY property this element still has layers registered for.
        //
        // The release path of a per-frame driver (MotionSpringDriver / BezierTweenDriver) needs this because
        // ClearInline nulls whole shorthand fan-outs — a driven `padding` channel owns all four edges — while an
        // authored longhand (pt-[2px]) is registered against ONE of those edges. Nulling the four and stopping
        // would leave that edge on whatever the driver last wrote, permanently: the layer map still records the
        // authored value, so nothing else re-applies it, and the next unrelated variant release on that property
        // would resurrect it out of nowhere. Re-asserting the whole map instead of the driven properties alone is
        // what makes that correct regardless of which shorthand overlapped which longhand.
        //
        // Reads the map without mutating it, so a driver that never registered a layer leaves it untouched and
        // the recorded cascade stays exactly what the class diff put there. A slot another live driver currently
        // owns can be taken back for a frame here, and that driver's next tick re-asserts it.
        //
        // The filter family is exempt, for the same reason the class-diff reapply exempts it: re-resolving a
        // filter property runs the combined-filter path, whose tail hands the composed list to the filter
        // transition driver and restarts its tween — so a spring settling beside an in-flight blur would reset
        // that blur to a fresh full duration, and a settle during teardown would start a tick on an element on
        // its way to the pool. No per-frame driver here ever writes `filter` (StyleFilterTransitionDriver owns
        // it), so this family holds nothing of theirs to restore.
        internal static void ReapplyLayeredValues(VisualElement element)
        {
            if (element == null || !s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            foreach (var property in map.Keys)
            {
                if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
                {
                    continue;
                }
                ResolveAndApply(element, property, map);
            }
        }

        // The form of ReapplyLayeredValues above for the properties writing one of longhands, for a driver that
        // hands back some of its slots and keeps writing the rest, which a whole-map pass would take back for a
        // frame.
        internal static void ReapplyLayeredValues(VisualElement element, StyleLonghandSet longhands)
        {
            if (element == null || !s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            foreach (var property in map.Keys)
            {
                if (property != ArbitraryProperty.FilterCustom && !IsFilter(property)
                    && StyleArbitraryLonghands.Of(property).Overlaps(longhands))
                {
                    ResolveAndApply(element, property, map);
                }
            }
        }

        // A gap, grid or divide manipulator's own write to a slot it owns. Not a layer — HasLayer and the class
        // projection see none of it — but every layer resolve that writes a held slot writes the held value
        // back after it, so a layer that changes once the manipulator has written — a [&>*]: payload turned
        // off — cannot take the slot from it. Yield makes the exception.
        internal static void Hold(VisualElement element, HeldSlot slot, StyleLength value)
        {
            var holds = HoldsOf(element);
            holds.Set(slot, value);
            holds.Reassert(element.style, StyleHeldSlots.Bit(slot));
        }

        internal static void Hold(VisualElement element, HeldSlot slot, StyleFloat value)
        {
            var holds = HoldsOf(element);
            holds.Set(slot, value);
            holds.Reassert(element.style, StyleHeldSlots.Bit(slot));
        }

        internal static void Hold(VisualElement element, HeldSlot slot, StyleColor value)
        {
            var holds = HoldsOf(element);
            holds.Set(slot, value);
            holds.Reassert(element.style, StyleHeldSlots.Bit(slot));
        }

        // Makes the hold just taken on slot give way to a layer of the element's own: while one writes the slot,
        // that layer's value stands, and the held one returns once the last such layer goes. That is how a
        // zero-specificity Tailwind write (space, divide) gives way to the element's own arbitrary value for as
        // long as it has one. With importantOnly it gives way to an important layer alone, which is how an
        // important divide still yields to a child's own important declaration. The next Hold on the slot takes
        // the yield back.
        internal static void Yield(VisualElement element, HeldSlot slot, bool importantOnly = false)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            map.Holds?.SetYield(slot, importantOnly);
            ReassertHolds(element, map, StyleHeldSlots.Bit(slot));
        }

        // Writes every held slot among slots back onto the element, except a yielding one a layer of the
        // element's own writes, which takes that layer's value instead.
        private static void ReassertHolds(VisualElement element, LayerMap map, int slots)
        {
            var holds = map.Holds;
            if (holds == null)
            {
                return;
            }
            var yielded = 0;
            foreach (var slot in HeldSlotGroups.EverySlot)
            {
                if (!holds.Yields(slot))
                {
                    continue;
                }
                if (!TryLayeredWinner(map, slot, out var winner, out var rank)
                    || (holds.YieldsOnlyToImportant(slot) && (rank & StyleLayerPriority.Important) == 0))
                {
                    continue;
                }
                StyleHeldSlots.WriteLayered(element.style, slot, winner);
                yielded |= StyleHeldSlots.Bit(slot);
            }
            holds.Reassert(element.style, slots & ~yielded);
        }

        private static StyleHeldSlots HoldsOf(VisualElement element)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            return map.Holds ??= new StyleHeldSlots();
        }

        // Gives a slot back to the cascade: drops any hold on it and writes what the element's own layers
        // resolve to there, or nothing when none does. Gap, grid and divide stop owning a slot through here
        // rather than by writing Null, which would erase a layer's value along with their own. Writes the slot
        // alone rather than re-resolving the winner's property.
        internal static void HandBack(VisualElement element, HeldSlot slot)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                StyleHeldSlots.WriteNull(element.style, slot);
                return;
            }
            map.Holds?.Drop(slot);
            if (TryLayeredWinner(map, slot, out var winner))
            {
                StyleHeldSlots.WriteLayered(element.style, slot, winner);
            }
            else
            {
                StyleHeldSlots.WriteNull(element.style, slot);
            }
        }

        // Hands slot back when a gap, grid or divide manipulator holds it on element, and leaves it alone
        // otherwise: an inline value nothing here wrote is not this layer's to clear.
        internal static void HandBackIfHeld(VisualElement element, HeldSlot slot)
        {
            s_layers.TryGetValue(element, out var map);
            var holds = map?.Holds;
            if (holds != null && holds.IsHeld(slot))
            {
                HandBack(element, slot);
            }
        }

        // Whether the highest priority an ungated class of the element's own claims on slot's longhand lies in
        // the important band. Inline layers are not consulted.
        internal static bool DeclaresImportantOwn(VisualElement element, HeldSlot slot)
            => TryGetProjection(element) is { } model
                && (model.ClaimOf(HeldSlotGroups.LonghandOf(slot)) & StyleLayerPriority.Important) != 0;

        // Whether a utility on element's class list sets slot through an ungated bundled USS rule. Tailwind
        // writes space and divide at zero specificity, so such a class of the element's own wins over them
        // there; an arbitrary value's layer does the same through a yielding Hold.
        internal static bool DeclaresOwn(VisualElement element, HeldSlot slot)
        {
            var longhand = HeldSlotGroups.LonghandOf(slot);
            foreach (var cls in element.GetClasses())
            {
                if (!StyleUtilityProperties.TryGet(cls, out var rule))
                {
                    continue;
                }
                if (rule.Gate == StyleUtilityGate.None && rule.Properties.Contains(longhand))
                {
                    return true;
                }
            }
            return false;
        }

        internal static void Watch(VisualElement element, IChildClassWatcher watcher)
        {
            var watchers = s_layers.GetValue(element, static _ => new LayerMap()).Watchers ??= new List<IChildClassWatcher>();
            if (!watchers.Contains(watcher))
            {
                watchers.Add(watcher);
            }
        }

        internal static void Unwatch(VisualElement element, IChildClassWatcher watcher)
        {
            s_layers.TryGetValue(element, out var map);
            map?.Watchers?.Remove(watcher);
        }

        internal static float? UniformScaleClassFallback(VisualElement element)
        {
            return s_layers.TryGetValue(element, out var map)
                // MUTANT_SURVIVES(equivalent, clause removed): removing the outer axis-presence clause leaves writes unchanged: the three current callers add no axis before refresh, which rejects that capture.
                && (map.ContainsKey(ArbitraryProperty.ScaleX) || map.ContainsKey(ArbitraryProperty.ScaleY))
                ? UniformScaleFromClasses(element)
                : null;
        }

        // Re-applies every container watching element after its class list changed. A copy is walked, because a
        // re-apply can claim or release the element and so edit the list.
        internal static void NotifyClassesChanged(VisualElement element, float? previousUniform = null)
        {
            s_layers.TryGetValue(element, out var map);
            if (map != null)
            {
                RefreshScaleClassFallback(element, map, previousUniform);
            }
            var watchers = map?.Watchers;
            if (watchers == null)
            {
                return;
            }
            foreach (var watcher in watchers.ToArray())
            {
                watcher.Reapply();
            }
        }

        // Hands back every slot held on element. For an element the reconciler removes: its claim is dropped
        // with the rest of its side tables, so the container that holds a slot on it cannot release it later.
        internal static void HandBackAll(VisualElement element)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return;
            }
            var holds = map.Holds;
            if (holds == null)
            {
                return;
            }
            foreach (var slot in HeldSlotGroups.EverySlot)
            {
                if (holds.IsHeld(slot))
                {
                    HandBack(element, slot);
                }
            }
        }

        // What the element's own layers resolve slot to, ignoring any hold on it: the value HandBack would
        // leave there. Null when no layer writes it.
        internal static ArbitraryStyle? ResolveLayered(VisualElement element, HeldSlot slot)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                return null;
            }
            return TryLayeredWinner(map, slot, out var winner) ? winner : null;
        }

        private static bool TryLayeredWinner(LayerMap map, HeldSlot slot, out ArbitraryStyle winner)
            => TryLayeredWinner(map, slot, out winner, out _);

        private static bool TryLayeredWinner(LayerMap map, HeldSlot slot, out ArbitraryStyle winner, out long rank)
        {
            winner = default;
            rank = long.MinValue;
            var best = long.MinValue;
            var latest = long.MinValue;
            var found = false;
            foreach (var writer in HeldSlotGroups.WritersOf(slot))
            {
                if (!TryWinningLayer(map, writer, out var style))
                {
                    continue;
                }
                var layers = map[writer];
                var priority = layers.Keys[layers.Count - 1];
                var arrival = map.ArrivalOf(writer, priority);
                // MUTANT_SURVIVES(equivalent, boundary): live arrivals are positive and unique, including after compaction; distinct writers cannot tie each other or the initial MinValue sentinel.
                var isNewerArrival = arrival > latest;
                var byCascade = found ? StyleBorderSideColor.CompareCascade(writer, winner.Property) : 0;
                if (priority > best || priority == best && (byCascade > 0 || byCascade == 0 && isNewerArrival))
                {
                    winner = style;
                    rank = priority;
                    best = priority;
                    latest = arrival;
                    found = true;
                }
            }
            return found;
        }

        // Whether any layer is registered for property. Uncontaminated for a caller asking about a slot it
        // writes directly rather than through Apply — no manipulator registers a layer.
        internal static bool HasLayer(VisualElement element, ArbitraryProperty property)
            => element != null
                && s_layers.TryGetValue(element, out var map)
                && map.TryGetValue(property, out var layers)
                && layers.Count > 0;

        // Drops all arbitrary-value layers tracked for element. Called when the element is
        // cleaned up / returned to a pool so a later reuse does not inherit a prior consumer's layers.
        public static void ClearAll(VisualElement element)
        {
            if (element != null) s_layers.Remove(element);
            // What the fit records the radius layers as declaring goes with them.
            if (element != null) CornerRadiusFit.Release(element);
        }

        // Resolves an inline-value class token to inline style — arbitrary value first, then background
        // image. When neither resolver claims it, the token is added to the USS class list unless
        // addToClassListFallback is false (the reapply path passes false: an inline-classified token that
        // no resolver owns — e.g. font-[..], owned by StyleFontResolver — must never enter the class list).
        // The caller must have confirmed IsInlineResolved(core) and stripped the important bang; priority
        // is the layer the bang selects (Important when present, Base otherwise). Mirror of ClearClassToken.
        public static void ApplyClassToken(VisualElement element, string core, long priority, bool addToClassListFallback = true)
        {
            if (TryParse(core, out var style))
            {
                Apply(element, in style, priority);
            }
            else if (StyleBackgroundImageResolver.TryParse(core, out var texture))
            {
                StyleBackgroundImageResolver.Apply(element, texture);
            }
            else if (addToClassListFallback)
            {
                element.AddToClassList(core);
            }
        }

        // Clears the inline style an inline-value class token applied (see ApplyClassToken), falling back
        // to removing it from the USS class list when neither resolver claims it.
        public static void ClearClassToken(VisualElement element, string core, long priority)
        {
            if (TryParse(core, out var style))
            {
                Clear(element, in style, priority);
            }
            else if (!TryClearUnregisteredFilterToken(element, core, priority))
            {
                if (StyleBackgroundImageResolver.TryParse(core, out _))
                {
                    StyleBackgroundImageResolver.Clear(element);
                }
                else
                {
                    element.RemoveFromClassList(core);
                }
            }
        }

        // Clears a filter-[name:args] token whose name is not (or no longer) registered. The
        // registry-gated parse does not claim such a token, but a layer applied while the name WAS
        // registered is still composed and must leave; the name alone identifies the layer, so it is
        // resolved syntactically. The class-list removal mirrors the never-registered apply, which
        // fell through to the class list — each action is a no-op in the other's scenario. Returns
        // false when the token is not a custom-filter shape at all.
        internal static bool TryClearUnregisteredFilterToken(VisualElement element, string core, long priority)
        {
            if (!TryResolveUnregisteredFilterClear(core, out var style))
            {
                return false;
            }
            Clear(element, in style, priority);
            element.RemoveFromClassList(core);
            return true;
        }

        // Fallback clear resolution for a filter-[name:args] token whose name is NOT (or no longer)
        // registered. Apply-side resolution (TryParse) is registry-gated — an unregistered name is not
        // claimed — but the layer a previous apply registered must stay clearable after an unregister,
        // or it would ghost in the composed filter forever. The token's shape alone carries everything a
        // clear needs: the ArbitraryStyle-aware Clear reads only the NAME, so a name-only synthetic style
        // (null definition, no arguments) suffices. Purely syntactic — no registry lookup, no warning.
        internal static bool TryResolveUnregisteredFilterClear(string core, out ArbitraryStyle style)
        {
            if (StyleFilterValueParser.TryExtractCustomFilterName(core, out var name))
            {
                style = new ArbitraryStyle(ArbitraryProperty.FilterCustom,
                    new CustomFilterValue(name, null!, Array.Empty<FilterParameter>()));
                return true;
            }
            style = default;
            return false;
        }

        // Prefixes classifying a core token as part of the composed filter family (the built-in filter
        // utilities' bracket forms plus filter-[name:…] customs) without resolving it — the class-diff
        // reapply's skip decision must not itself pay the parse it is avoiding.
        private static readonly string[] s_filterFamilyTokenPrefixes = BuildFilterFamilyTokenPrefixes();

        private static string[] BuildFilterFamilyTokenPrefixes()
        {
            var families = StyleFilterValueParser.BuiltInFamilyNames;
            var prefixes = new string[families.Length + 1];
            for (var i = 0; i < families.Length; i++)
            {
                prefixes[i] = families[i] + "-[";
            }
            prefixes[families.Length] = "filter-[";
            return prefixes;
        }

        // True for tokens whose resolved property lands in the composed filter family. Purely
        // syntactic on the bracket prefix: cheap enough to gate a skip without parsing the value.
        internal static bool IsFilterFamilyToken(string core)
        {
            foreach (var prefix in s_filterFamilyTokenPrefixes)
            {
                if (core.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Applies the winning layer for a property. Translate and scale are special: their axis layers share
        // one inline `translate` / `scale`, so their winners must be composed before that write.
        private static void ResolveAndApply(VisualElement element, ArbitraryProperty property, LayerMap map)
        {
            if (property == ArbitraryProperty.TranslateX || property == ArbitraryProperty.TranslateY)
            {
                ApplyCombinedTranslate(element, map);
                return;
            }
            if (property == ArbitraryProperty.Scale
                || property == ArbitraryProperty.ScaleX || property == ArbitraryProperty.ScaleY)
            {
                ApplyCombinedScale(element, map);
                return;
            }
            if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
            {
                ApplyCombinedFilter(element, map);
                return;
            }
            if (property == ArbitraryProperty.TransitionDuration)
            {
                ApplyTransitionDuration(element, map);
                return;
            }
            if (!TryWinningLayer(map, property, out _))
            {
                ClearInline(element, property);
            }
            var rewritten = ResolveSharedLonghands(element, property, map);
            ReassertHolds(element, map, HeldSlotGroups.SlotsOf(property) | rewritten);
        }

        // Settles every longhand property shares with another property's layers (m-[4px] beside mt-[8px] and
        // mx-[8px]): each ends on its highest-keyed writer, or on the class claiming it above that writer.
        // Returns the held slots written, which the caller reasserts with its own.
        private static int ResolveSharedLonghands(VisualElement element, ArbitraryProperty property, LayerMap map)
        {
            var writers = ConnectedWriters(property, map);
            if (writers == null)
            {
                // Nothing else writes what this property writes, the common case: only its own winner is left.
                if (!TryWinningLayer(map, property, out var own))
                {
                    return 0;
                }
                ApplyInline(element, own);
                ClearClaimedLonghands(element, map, own.Property, map[property].Keys[map[property].Count - 1]);
                return 0;
            }

            writers.Sort((a, z) =>
            {
                var byKey = a.Key.CompareTo(z.Key);
                if (byKey != 0)
                {
                    return byKey;
                }
                var byCascade = StyleBorderSideColor.CompareCascade(a.Style.Property, z.Style.Property);
                return byCascade != 0 ? byCascade : map.ArrivalOf(a.Style.Property, a.Key)
                    .CompareTo(map.ArrivalOf(z.Style.Property, z.Key));
            });
            var slots = 0;
            foreach (var (_, style) in writers)
            {
                ApplyInline(element, style);
                slots |= HeldSlotGroups.SlotsOf(style.Property);
            }
            foreach (var (key, style) in writers)
            {
                ClearClaimedLonghands(element, map, style.Property, key, writers);
            }
            return slots;
        }

        // The winning layer of every property connected to property through a shared longhand, however many
        // steps away — a writer re-written can reach a longhand property never touches — with its key; null
        // when nothing else shares one.
        private static List<(long Key, ArbitraryStyle Style)>? ConnectedWriters(ArbitraryProperty property,
            LayerMap map)
        {
            var reached = StyleArbitraryLonghands.Of(property);
            var remaining = new HashSet<ArbitraryProperty>(map.Keys);
            remaining.Remove(property);
            HashSet<ArbitraryProperty>? taken = null;
            // MUTANT_SURVIVES(equivalent, boundary): each productive pass consumes a candidate;
            // an extra pass cannot add a writer after the map-sized closure has finished.
            for (var pass = 0; pass < map.Count; pass++)
            {
                var previousCount = remaining.Count;
                foreach (var pair in map)
                {
                    var longhands = StyleArbitraryLonghands.Of(pair.Key);
                    if (!longhands.Overlaps(reached) || !remaining.Remove(pair.Key))
                    {
                        continue;
                    }
                    (taken ??= new HashSet<ArbitraryProperty>()).Add(pair.Key);
                    reached = reached.Union(longhands);
                }
                if (remaining.Count == previousCount)
                {
                    break;
                }
            }
            if (taken == null)
            {
                return null;
            }
            taken.Add(property);
            var writers = new List<(long Key, ArbitraryStyle Style)>();
            foreach (var connected in taken)
            {
                if (TryWinningLayer(map, connected, out var style))
                {
                    writers.Add((map[connected].Keys[map[connected].Count - 1], style));
                }
            }
            return writers;
        }

        // A class the projection could not floor a writer under, because it claims only some of what the writer
        // writes (md:pt-6 beside p-[12px]), still outranks that writer on the longhands it claims. Each such
        // longhand the writer ended up owning is cleared, so the class shows through.
        private static void ClearClaimedLonghands(VisualElement element, LayerMap map, ArbitraryProperty writer,
            long key, List<(long Key, ArbitraryStyle Style)>? others = null)
        {
            if (map.Projection == null)
            {
                return;
            }
            var written = StyleArbitraryLonghands.Of(writer);
            foreach (var longhand in s_longhands)
            {
                if (written.Contains(longhand) && map.Projection.ClaimOf(longhand) > key
                    && !OutrankedOn(longhand, key, others) && s_soleWriters.TryGetValue(longhand, out var sole))
                {
                    ClearInline(element, sole);
                }
            }
        }

        // Whether another writer holds longhand above key, in which case the longhand is that one's to answer for.
        private static bool OutrankedOn(StyleLonghand longhand, long key, List<(long Key, ArbitraryStyle Style)>? others)
        {
            if (others == null)
            {
                return false;
            }
            foreach (var (otherKey, style) in others)
            {
                if (otherKey > key && StyleArbitraryLonghands.Of(style.Property).Contains(longhand))
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly StyleLonghand[] s_longhands = (StyleLonghand[])Enum.GetValues(typeof(StyleLonghand));

        // The property writing a longhand and nothing else, where one does: clearing that longhand alone goes
        // through it.
        private static readonly Dictionary<StyleLonghand, ArbitraryProperty> s_soleWriters = BuildSoleWriters();

        private static Dictionary<StyleLonghand, ArbitraryProperty> BuildSoleWriters()
        {
            var sole = new Dictionary<StyleLonghand, ArbitraryProperty>();
            foreach (ArbitraryProperty property in Enum.GetValues(typeof(ArbitraryProperty)))
            {
                var longhands = StyleArbitraryLonghands.Of(property);
                foreach (var longhand in s_longhands)
                {
                    if (longhands == StyleLonghandSet.Of(longhand) && !sole.ContainsKey(longhand))
                    {
                        sole.Add(longhand, property);
                    }
                }
            }
            return sole;
        }

        private static void ApplyCombinedTranslate(VisualElement element, LayerMap map)
        {
            var hasX = TryWinningLayer(map, ArbitraryProperty.TranslateX, out var xw);
            var hasY = TryWinningLayer(map, ArbitraryProperty.TranslateY, out var yw);
            if (!hasX && !hasY)
            {
                element.style.translate = StyleKeyword.Null;
                return;
            }
            var x = hasX ? new Length(xw.Value, xw.Unit) : new Length(0f);
            var y = hasY ? new Length(yw.Value, yw.Unit) : new Length(0f);
            element.style.translate = new Translate(x, y);
        }

        private static void ApplyCombinedScale(VisualElement element, LayerMap map)
        {
            var hasX = TryWinningLayer(map, ArbitraryProperty.ScaleX, out var xw);
            var hasY = TryWinningLayer(map, ArbitraryProperty.ScaleY, out var yw);
            var hasUniform = TryWinningLayer(map, ArbitraryProperty.Scale, out var uw);
            if (!hasX && !hasY && !hasUniform)
            {
                element.style.scale = StyleKeyword.Null;
                return;
            }
            var uniform = hasUniform ? uw.Value : UniformScaleFromClasses(element);
            var x = hasX ? xw.Value : uniform;
            var y = hasY ? yw.Value : uniform;
            element.style.scale = new Scale(new Vector2(x, y));
        }

        private static void RefreshScaleClassFallback(VisualElement element, LayerMap map, float? previousUniform)
        {
            if (previousUniform.HasValue && !TryWinningLayer(map, ArbitraryProperty.Scale, out _)
                && TryWinningLayer(map, ArbitraryProperty.ScaleX, out _) != TryWinningLayer(map, ArbitraryProperty.ScaleY, out _)
                && previousUniform.Value != UniformScaleFromClasses(element))
            {
                ApplyCombinedScale(element, map);
            }
        }

        private static float UniformScaleFromClasses(VisualElement element)
        {
            var uniform = 1f;
            var position = -1;
            foreach (var cls in element.GetClasses())
            {
                if (TryGetUniformScalePreset(cls, out var scale)
                    // MUTANT_SURVIVES(equivalent, boundary): each recognized uniform preset has a distinct generated cascade position; a tie repeats the same factor.
                    && StyleUtilityProperties.TryGet(cls, out var rule) && rule.CascadePosition > position)
                {
                    uniform = scale;
                    position = rule.CascadePosition;
                }
            }
            return uniform;
        }

        private static bool TryGetUniformScalePreset(string cls, out float scale)
        {
            scale = 1f;
            return cls.StartsWith("scale-", StringComparison.Ordinal)
                && TryGetAxisScale(cls.Substring("scale-".Length), out scale);
        }

        // transition-duration is a StyleList<TimeValue> (not a StyleLength), so the winning duration-[..] layer
        // is written out-of-band as a single-element list. The stored Value is in SECONDS. No null layer -> clear.
        private static void ApplyTransitionDuration(VisualElement element, LayerMap map)
        {
            StyleList<TimeValue> duration = StyleKeyword.Null;
            if (TryWinningLayer(map, ArbitraryProperty.TransitionDuration, out var winner))
            {
                duration = new List<TimeValue> { new TimeValue(winner.Value, TimeUnit.Second) };
            }
            element.style.transitionDuration = duration;
            var box = ClipPathLayoutBox.Of(element);
            if (box != element)
            {
                box.style.transitionDuration = duration;
            }
        }

        // The filter members share one source of truth with the compose order (s_filterOrder), so a new
        // filter added there is automatically recognized here — no parallel membership list to keep in sync.
        private static bool IsFilter(ArbitraryProperty p) => s_filterSet.Contains(p);

        // CSS composes filter functions left-to-right into one `filter` property; UITK matches (one
        // StyleList<FilterFunction>). Each filter-* utility is its own layer, so they are gathered here in a
        // fixed canonical order (the standard CSS filter order) into a single list — a missing one is just
        // skipped. An empty result clears the inline filter.
        private static readonly ArbitraryProperty[] s_filterOrder =
        {
            ArbitraryProperty.FilterBlur,
            ArbitraryProperty.FilterBrightness,
            ArbitraryProperty.FilterContrast,
            ArbitraryProperty.FilterGrayscale,
            ArbitraryProperty.FilterHueRotate,
            ArbitraryProperty.FilterInvert,
            ArbitraryProperty.FilterSaturate,
            ArbitraryProperty.FilterSepia,
        };

        // Built from s_filterOrder (declared after it so the textual static-init order is satisfied) so the
        // filter set is single-sourced — see IsFilter.
        private static readonly HashSet<ArbitraryProperty> s_filterSet = new(s_filterOrder);

        // Writes the filter the element's layers compose, variant layers included, or clears it where none remain.
        internal static void RecomposeFilter(VisualElement element)
        {
            if (s_layers.TryGetValue(element, out var map))
            {
                ApplyCombinedFilter(element, map);
            }
            else
            {
                StyleFilterEngineWrite.Write(element, null);
            }
        }

        private static void ApplyCombinedFilter(VisualElement element, LayerMap map)
        {
            List<FilterFunction>? functions = null;
            foreach (var prop in s_filterOrder)
            {
                if (!map.TryGetValue(prop, out var layers) || layers.Count == 0)
                {
                    continue;
                }
                if (BuildFilter(prop, layers.Values[layers.Count - 1].Value) is { } fn)
                {
                    (functions ??= new List<FilterFunction>()).Add(fn);
                }
            }
            // Customs compose AFTER every built-in, in first-application order — each entry contributing
            // its own highest-priority (winning) layer, the same "last entry in the ascending-by-priority
            // SortedList wins" rule the built-ins use above, just keyed by name instead of by
            // ArbitraryProperty. An empty stack is a tombstone holding its name's compose slot (see
            // LayerMap.Customs). A winning layer whose definition has been DESTROYED since it was applied
            // compares equal to null (a dead asset) and is skipped: the engine's FilterFunction
            // constructor throws on a dead definition, and a function bound to one could not render
            // anything anyway.
            if (map.Customs != null)
            {
                foreach (var (_, stack) in map.Customs)
                {
                    if (stack.Count == 0)
                    {
                        continue;
                    }
                    var custom = stack.Values[stack.Count - 1].Custom!;
                    if (custom.Definition == null)
                    {
                        continue;
                    }
                    (functions ??= new List<FilterFunction>()).Add(BuildCustomFilter(custom));
                }
            }
            // Velvet's filter tween owns the write when it runs; it reads the current inline list as its
            // from-side, so it must run BEFORE the instant write below (never observing its own write).
            if (!StyleFilterTransitionDriver.TryStartOrRedirect(element, functions))
            {
                StyleFilterEngineWrite.Write(element, functions);
            }
        }

        // Builds the FilterFunction for a filter-[name:args] custom filter. The public
        // FilterFunctionDefinition ctor sets type = Custom and customDefinition in one step. Args always
        // carries the FULL declared parameter count — the explicit segments plus a tail padded from the
        // declaration's defaults at parse time — because this public construction path performs none of
        // the padding the engine's USS parser does: an under-filled function stops binding at its
        // parameterCount at render time, leaving whatever value the shared material-property state still
        // holds from a previous draw where the declared default should be.
        private static FilterFunction BuildCustomFilter(CustomFilterValue custom)
        {
            var fn = new FilterFunction(custom.Definition);
            foreach (var arg in custom.Args)
            {
                fn.AddParameter(arg);
            }
            return fn;
        }

        // Null only when a built-in custom-filter shader (brightness/saturate) is unavailable in the build;
        // the caller drops that layer. Every other branch always returns a value.
        private static FilterFunction? BuildFilter(ArbitraryProperty prop, float value)
        {
            // brightness and saturate have no UITK filter type; each renders through a first-party
            // custom-filter shader (BuiltInFilterDefinitions) as a FilterFunctionType.Custom function. The
            // shaders take the full CSS range (over-brighten and over-saturate, N>1) and do the multiply/lerp
            // on the encoded pixel before the Linear-output conversion, matching browser semantics exactly.
            // The stored Value is the raw CSS factor N — the
            // shader implements saturate's lerp-toward-luminance natively, so there is no 1-N complement to
            // pre-compute. A null definition (shader stripped from the build) drops the layer, the same degrade
            // the bake shaders take when their shader is missing.
            if (prop == ArbitraryProperty.FilterBrightness)
            {
                var def = BuiltInFilterDefinitions.Brightness;
                if (def == null)
                {
                    return null;
                }
                var brightness = new FilterFunction(def!);
                brightness.AddParameter(new FilterParameter(value));
                return brightness;
            }
            if (prop == ArbitraryProperty.FilterSaturate)
            {
                var def = BuiltInFilterDefinitions.Saturate;
                if (def == null)
                {
                    return null;
                }
                var saturate = new FilterFunction(def!);
                saturate.AddParameter(new FilterParameter(value));
                return saturate;
            }

            // This method's domain is s_filterOrder, which ApplyCombinedFilter — its only caller —
            // iterates, so naming the ArbitraryProperty members outside that array would map each to a
            // filter type it is not. What the throw buys over the Sepia it replaces: a filter added to
            // s_filterOrder with no arm here reports, rather than rendering as sepia.
            var type = prop switch
            {
                ArbitraryProperty.FilterBlur => FilterFunctionType.Blur,
                ArbitraryProperty.FilterContrast => FilterFunctionType.Contrast,
                ArbitraryProperty.FilterGrayscale => FilterFunctionType.Grayscale,
                ArbitraryProperty.FilterHueRotate => FilterFunctionType.HueRotate,
                ArbitraryProperty.FilterInvert => FilterFunctionType.Invert,
                ArbitraryProperty.FilterSepia => FilterFunctionType.Sepia,
                _ => throw new ArgumentOutOfRangeException(nameof(prop), prop, "not a built-in filter"),
            };
            // Only the single-arg ctor + AddParameter are public (the (type,value) ctors are internal).
            var fn = new FilterFunction(type);
            fn.AddParameter(new FilterParameter(value));
            return fn;
        }

        // Writes a single ArbitraryStyle to the element's inline style (no layering), fanning a shorthand out to
        // every slot it owns (padding → four edges, border-color → four sides, size → width + height). A value
        // that places or sizes a clipped element goes to its clip wrapper instead (ClipPathLayoutBox.StyleFor).
        // Class-diff callers must go through Apply / Clear instead so per-property layering is respected; the
        // layer-bypassing form is for a per-frame driver that OWNS the slot for the duration of its play and
        // hands it back through ClearInline (see MotionSpringDriver / BezierTweenDriver), where registering and
        // unregistering a layer on every tick would only churn the layer map.
        internal static void ApplyInline(VisualElement element, in ArbitraryStyle style)
        {
            // Transform properties (scale / translate / rotate / transform-origin) are not StyleLength and
            // are written through their dedicated UITK style properties.
            switch (style.Property)
            {
                // Among transform properties, scale (uniform + per-axis) and both translate axes are composed
                // by ResolveAndApply's combined appliers and never reach here; what lands is every transform
                // property written in one go — rotate, transform-origin — plus aspect-ratio.
                case ArbitraryProperty.Rotate:
                    element.style.rotate = new Rotate(new Angle(style.Value, AngleUnit.Degree));
                    return;
                case ArbitraryProperty.TransformOrigin:
                    element.style.transformOrigin = new TransformOrigin(
                        new Length(style.Value, style.Unit), new Length(style.Value2, style.Unit2), style.Value3);
                    return;
                // opacity-[..] is a unitless factor (0..1), written through the slot a layoutId crossfade shares.
                case ArbitraryProperty.Opacity:
                    MotionOpacity.WriteTransitioned(element, style.Value);
                    return;
                case ArbitraryProperty.AspectRatio:
                {
                    Ratio ratio = style.Value;          // float -> Ratio (implicit)
                    ClipPathLayoutBox.StyleFor(element, style.Property).aspectRatio = ratio;  // Ratio -> StyleRatio (implicit)
                    return;
                }
            }

            if (ColorSetters.TryGetValue(style.Property, out var colorSetters))
            {
                var color = new StyleColor(style.Color);
                var cs = element.style;
                foreach (var setter in colorSetters)
                {
                    setter(cs, color);
                }
                return;
            }

            if (FloatSetters.TryGetValue(style.Property, out var floatSetters))
            {
                var width = new StyleFloat(style.Value);
                var fs = ClipPathLayoutBox.StyleFor(element, style.Property);
                foreach (var setter in floatSetters)
                {
                    setter(fs, width);
                }
                return;
            }

            if (RadiusCornersOf.TryGetValue(style.Property, out var corners))
            {
                CornerRadiusFit.SetInline(element, corners, new Length(style.Value, style.Unit));
                return;
            }

            if (!PropertySetters.TryGetValue(style.Property, out var setters))
            {
                return;
            }

            var length = style.ToStyleLength();
            var s = ClipPathLayoutBox.StyleFor(element, style.Property);
            foreach (var setter in setters)
            {
                setter(s, length);
            }
        }

        // Clears the inline style for the given property (StyleKeyword.Null reverts to the USS default), fanning
        // out to the same slot set ApplyInline writes. Class-diff callers go through Clear so a surviving
        // lower-priority layer is re-applied; the layer-bypassing form pairs with the ApplyInline above.
        internal static void ClearInline(VisualElement element, ArbitraryProperty property)
        {
            if (TryClearDedicatedSlot(element, property))
            {
                return;
            }

            if (ColorSetters.TryGetValue(property, out var colorSetters))
            {
                var nullColor = new StyleColor(StyleKeyword.Null);
                var cs = element.style;
                foreach (var setter in colorSetters)
                {
                    setter(cs, nullColor);
                }
                return;
            }

            if (FloatSetters.TryGetValue(property, out var floatSetters))
            {
                var nullFloat = new StyleFloat(StyleKeyword.Null);
                var fs = ClipPathLayoutBox.StyleFor(element, property);
                foreach (var setter in floatSetters)
                {
                    setter(fs, nullFloat);
                }
                return;
            }

            if (RadiusCornersOf.TryGetValue(property, out var corners))
            {
                CornerRadiusFit.ClearInline(element, corners);
                return;
            }

            if (!PropertySetters.TryGetValue(property, out var setters))
            {
                return;
            }

            var nullStyle = new StyleLength(StyleKeyword.Null);
            var s = ClipPathLayoutBox.StyleFor(element, property);
            foreach (var setter in setters)
            {
                setter(s, nullStyle);
            }
        }

        // The properties written through a UITK style property of their own rather than through the setter
        // tables.
        private static bool TryClearDedicatedSlot(VisualElement element, ArbitraryProperty property)
        {
            switch (property)
            {
                case ArbitraryProperty.Rotate:
                    element.style.rotate = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.TransformOrigin:
                    element.style.transformOrigin = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.Opacity:
                    MotionOpacity.WriteTransitioned(element, StyleKeyword.Null);
                    // MUTANT_SURVIVES(equivalent): opacity has no entry in the fallback setter tables ClearInline reads.
                    return true;
                case ArbitraryProperty.AspectRatio:
                    ClipPathLayoutBox.StyleFor(element, property).aspectRatio = StyleKeyword.Null;
                    return true;
                // translate and scale are each a single shorthand for both axes (and scale composes the uniform
                // + per-axis layers), so clearing any one reverts the whole property. In the class-diff reconcile
                // path the survivors are restored by FiberNodePatcher.ReapplyArbitraryValues; a direct Clear (or a
                // variant-payload toggle) of one drops the others until the next full re-apply — combine them in a
                // single combined translate/scale when that matters.
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY:
                    element.style.translate = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.Scale:
                case ArbitraryProperty.ScaleX:
                case ArbitraryProperty.ScaleY:
                    element.style.scale = StyleKeyword.Null;
                    return true;
                case ArbitraryProperty.TransitionDuration:
                    element.style.transitionDuration = StyleKeyword.Null;
                    return true;
            }

            // Like the axes above, all filter-* utilities share the one inline `filter` list, so clearing any
            // reverts the whole property; the surviving filters are restored by ReapplyArbitraryValues.
            if (property == ArbitraryProperty.FilterCustom || IsFilter(property))
            {
                if (!StyleFilterTransitionDriver.TryStartOrRedirect(element, null))
                {
                    StyleFilterEngineWrite.Write(element, null);
                }
                return true;
            }
            return false;
        }

        // prefix includes the trailing '-' (e.g. "h-", "min-h-", "mt-", "rounded-").
        private static readonly Dictionary<string, ArbitraryProperty> s_prefixProperties = new()
        {
            ["w-"] = ArbitraryProperty.Width,
            ["h-"] = ArbitraryProperty.Height,
            ["min-w-"] = ArbitraryProperty.MinWidth,
            ["min-h-"] = ArbitraryProperty.MinHeight,
            ["max-w-"] = ArbitraryProperty.MaxWidth,
            ["max-h-"] = ArbitraryProperty.MaxHeight,
            ["size-"] = ArbitraryProperty.Size,
            ["basis-"] = ArbitraryProperty.FlexBasis,

            ["top-"] = ArbitraryProperty.Top,
            ["right-"] = ArbitraryProperty.Right,
            ["bottom-"] = ArbitraryProperty.Bottom,
            ["left-"] = ArbitraryProperty.Left,
            ["inset-"] = ArbitraryProperty.Inset,
            ["inset-x-"] = ArbitraryProperty.InsetX,
            ["inset-y-"] = ArbitraryProperty.InsetY,

            ["pt-"] = ArbitraryProperty.PaddingTop,
            ["pr-"] = ArbitraryProperty.PaddingRight,
            ["pb-"] = ArbitraryProperty.PaddingBottom,
            ["pl-"] = ArbitraryProperty.PaddingLeft,
            ["p-"] = ArbitraryProperty.Padding,
            ["px-"] = ArbitraryProperty.PaddingX,
            ["py-"] = ArbitraryProperty.PaddingY,

            ["mt-"] = ArbitraryProperty.MarginTop,
            ["mr-"] = ArbitraryProperty.MarginRight,
            ["mb-"] = ArbitraryProperty.MarginBottom,
            ["ml-"] = ArbitraryProperty.MarginLeft,
            ["m-"] = ArbitraryProperty.Margin,
            ["mx-"] = ArbitraryProperty.MarginX,
            ["my-"] = ArbitraryProperty.MarginY,

            ["rounded-"] = ArbitraryProperty.BorderRadius,
            ["rounded-t-"] = ArbitraryProperty.BorderTopRadius,
            ["rounded-r-"] = ArbitraryProperty.BorderRightRadius,
            ["rounded-b-"] = ArbitraryProperty.BorderBottomRadius,
            ["rounded-l-"] = ArbitraryProperty.BorderLeftRadius,
            ["rounded-tl-"] = ArbitraryProperty.BorderTopLeftRadius,
            ["rounded-tr-"] = ArbitraryProperty.BorderTopRightRadius,
            ["rounded-bl-"] = ArbitraryProperty.BorderBottomLeftRadius,
            ["rounded-br-"] = ArbitraryProperty.BorderBottomRightRadius,

            // A color value is claimed earlier, for `border-` in TryParseBracketValue and for a side in
            // StyleBorderSideColor; here it is the width form.
            ["border-"] = ArbitraryProperty.BorderWidth,
            ["border-t-"] = ArbitraryProperty.BorderTopWidth,
            ["border-r-"] = ArbitraryProperty.BorderRightWidth,
            ["border-b-"] = ArbitraryProperty.BorderBottomWidth,
            ["border-l-"] = ArbitraryProperty.BorderLeftWidth,

            ["text-"] = ArbitraryProperty.FontSize,
            ["tracking-"] = ArbitraryProperty.LetterSpacing,

            // The transform prefixes (scale- / rotate- / translate-x- / translate-y-) are absent on purpose:
            // TryParseBracketValue intercepts them before this table so all four share the Apply/Clear switch.
        };

        // Single source for the utility-prefix → target-property mapping, shared so a non-bracket preset
        // recognizer (MotionPropertyClassParser, which pairs a prefix with the numeric scale its USS family
        // uses) resolves the SAME prefix table instead of holding a second copy that could drift — mirroring
        // TryGetSpacingPx's precedent. Which numeric SCALE a prefix's suffix is read against is deliberately
        // not encoded here: this table is shared by families on different scales (spacing, radius, border
        // width), and only the caller knows which family it is claiming.
        internal static bool TryGetProperty(string prefix, out ArbitraryProperty property)
        {
            property = default;
            return prefix != null && s_prefixProperties.TryGetValue(prefix, out property);
        }

        // Strips the enclosing brackets off a bracketed JIT arbitrary-value token (`[value]`), optionally
        // after a fixed literal prefix that precedes the '[' (prefixLength chars — e.g. 2 for "z-" in
        // "z-[10]", 0 for a bare "[value]" token). Rejects an empty value ("z-[]" / "[]"): every call
        // site's downstream parse (a color, a number, a unit-suffixed length) already fails on an empty
        // string, so accepting it here would only defer the identical rejection to each call site instead
        // of making it once, here. Span-based so a caller already holding a slice (the common shape on
        // this per-class hot path) pays no extra allocation; a caller holding a string gets the same for
        // free via the implicit span conversion.
        internal static bool TryStripBrackets(ReadOnlySpan<char> token, int prefixLength, out ReadOnlySpan<char> inner)
        {
            if (token.Length < prefixLength + 3 || token[prefixLength] != '[' || token[token.Length - 1] != ']')
            {
                inner = default;
                return false;
            }
            inner = token.Slice(prefixLength + 1, token.Length - prefixLength - 2);
            return true;
        }

        // Parses a bracketed JIT arbitrary value (`[..]`) that must resolve to a non-negative pixel length —
        // the contract shared by gap-[..], divide-*-[..], and ring-[..], where a percentage is meaningless.
        // Takes the whole suffix incl. brackets; returns false for a non-`[..]` token or any non-px / negative
        // value. Span-based so callers pass an existing slice with no extra allocation.
        internal static bool TryParseArbitraryPixels(ReadOnlySpan<char> suffix, out float px)
        {
            px = 0f;
            if (!TryStripBrackets(suffix, 0, out var inner))
            {
                return false;
            }
            if (TryParseValue(inner, out var value, out var unit)
                && unit == LengthUnit.Pixel && value >= 0f)
            {
                px = value;
                return true;
            }
            return false;
        }

        // Parses a <length-percentage> token: a '%' suffix is percent; a 'px', 'rem', an absolute unit (in, cm,
        // mm, pt, pc, Q) or no suffix is pixel (bare numbers default to px, and rem is converted at the fixed
        // 1rem = 16px scale because UI Toolkit has no rem unit and no document root to resolve a relative font
        // size against).
        // InvariantCulture, finite values only. Internal so other utility parsers (clip-path) share THE
        // length grammar instead of re-implementing it.
        internal static bool TryParseValue(ReadOnlySpan<char> valueStr, out float value, out LengthUnit unit)
        {
            value = 0;
            unit = LengthUnit.Pixel;

            bool parsed;
            if (valueStr.Length > 0 && valueStr[valueStr.Length - 1] == '%')
            {
                unit = LengthUnit.Percent;
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else if (valueStr.Length > 3
                     && valueStr[valueStr.Length - 3] == 'r'
                     && valueStr[valueStr.Length - 2] == 'e'
                     && valueStr[valueStr.Length - 1] == 'm')
            {
                // 1rem resolves to a fixed 16px (UI Toolkit has no rem unit), so the parsed head is
                // scaled and emitted as a pixel length rather than carried as a distinct unit.
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 3),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
                value *= 16f;
            }
            else if (valueStr.Length > 1
                     && valueStr[valueStr.Length - 2] == 'p'
                     && valueStr[valueStr.Length - 1] == 'x')
            {
                unit = LengthUnit.Pixel;
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - 2),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }
            else if (TryFindAbsoluteUnit(valueStr, out var unitLength, out var pixelsPerUnit))
            {
                parsed = float.TryParse(
                    valueStr.Slice(0, valueStr.Length - unitLength),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
                value *= pixelsPerUnit;
            }
            else
            {
                parsed = float.TryParse(
                    valueStr,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return parsed && float.IsFinite(value);
        }

        // CSS's absolute length units at their fixed ratio to a pixel (96 per inch); the match is
        // case-sensitive, as Tailwind's length test is, so Q is upper case and the rest lower.
        private static readonly (string Suffix, float Pixels)[] s_absoluteUnits =
        {
            ("in", 96f), ("cm", 96f / 2.54f), ("mm", 96f / 25.4f), ("pt", 96f / 72f), ("pc", 16f), ("Q", 96f / 101.6f),
        };

        private static bool TryFindAbsoluteUnit(ReadOnlySpan<char> valueStr, out int length, out float pixels)
        {
            foreach (var (suffix, perUnit) in s_absoluteUnits)
            {
                if (valueStr.Length > suffix.Length && valueStr.EndsWith(suffix.AsSpan(), StringComparison.Ordinal))
                {
                    length = suffix.Length;
                    pixels = perUnit;
                    return true;
                }
            }
            length = 0;
            pixels = 0f;
            return false;
        }

        // Parses a unitless finite float (used by scale-[..]). A trailing unit is rejected.
        // Internal so the extracted transform/filter parsers share the one float grammar.
        internal static bool TryParseFloat(ReadOnlySpan<char> valueStr, out float value)
        {
            var parsed = float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && float.IsFinite(value);
        }

        // Parses an aspect ratio (used by aspect-[..]): either a "w/h" fraction or a bare
        // decimal. A zero/negative denominator or a non-finite result is rejected (returns false).
        private static bool TryParseRatio(ReadOnlySpan<char> valueStr, out float ratio)
        {
            ratio = 0f;
            var slash = valueStr.IndexOf('/');
            if (slash < 0)
            {
                return TryParseFloat(valueStr, out ratio) && ratio > 0f;
            }
            if (!float.TryParse(valueStr.Slice(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                || !float.TryParse(valueStr.Slice(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                || h == 0f)
            {
                return false;
            }
            ratio = w / h;
            return float.IsFinite(ratio) && ratio > 0f;
        }

        // Parses an angle (used by rotate-[..]) and normalizes it to degrees. Accepts the
        // deg / rad / grad / turn suffixes; a bare number is degrees.
        // Internal so the extracted transform/filter parsers share the one angle grammar.
        internal static bool TryParseAngleDegrees(ReadOnlySpan<char> valueStr, out float degrees)
        {
            degrees = 0f;

            var factor = 1f;
            var numeric = valueStr;
            if (valueStr.EndsWith("deg"))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 3);
            }
            else if (valueStr.EndsWith("grad"))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 4);
                factor = 0.9f; // 400 grad = 360 deg
            }
            else if (valueStr.EndsWith("rad"))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 3);
                factor = Mathf.Rad2Deg;
            }
            else if (valueStr.EndsWith("turn"))
            {
                numeric = valueStr.Slice(0, valueStr.Length - 4);
                factor = 360f;
            }

            if (!float.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var raw) || !float.IsFinite(raw))
            {
                return false;
            }

            degrees = raw * factor;
            return true;
        }
    }
}
