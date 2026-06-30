using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{

    // Parses utility-class arbitrary-value syntax (e.g. h-[15%], min-w-[60px], -mt-[20px],
    // text-[#fff], bg-[#1e1e1e]) and applies the result as an inline style.
    // Uses inline styles instead of USS classes to reduce reliance on USS files.
    // text-[...] is overloaded: a color value sets the text color, otherwise the
    // value is a font size. bg-[#color] sets the background color (the bg-[addr:...] image
    // form is handled by StyleBackgroundImageResolver).
    internal static class StyleArbitraryValueResolver
    {
        // Determines whether className matches an arbitrary-value pattern (prefix-[value]) and returns the parsed result.
        // Negative values (-mt-[20px]) are also supported.
        // Parses using IndexOf + string operations rather than regex for speed.
        // Strips the important modifier and reports whether it was present: a leading '!' (!bg-red-500)
        // or a trailing '!' (bg-red-500!). The bare core is returned so the caller routes
        // it normally; when important, the caller elevates the (inline-resolvable) utility to the Important
        // layer so it wins conflicts. A class-only utility (no inline form) cannot be elevated in UI Toolkit,
        // so its '!' is accepted but inert. Returns the input unchanged when no modifier is present.
        //
        // Scope: this is wired into the per-class dispatch (USS-class + inline-layer utilities). The
        // array-scanned subsystem utilities (shadow-*, font-*, gap-*, divide-*, clip-path-*) do NOT
        // participate in the USS/inline cascade that !important arbitrates — they are custom-drawn or
        // resolved to inline that already wins — so the important form is not recognized for them; use the
        // plain form. (Adding the bang there would be a no-op elevation by definition.)
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

        public static bool TryParse(string className, out ArbitraryStyle result)
        {
            result = default;
            if (className == null)
            {
                return false;
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

            // The last character must be ']'.
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

            // Transform prefixes (scale / scale-x / scale-y / rotate / opacity / translate-x / translate-y)
            // need bespoke value parsing routed through the merge path, so they resolve before the length path.
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

        // The translate fraction suffixes (percent of the element's own size). The slash form routes
        // here; translate-x-full (100%) and the spacing-scale presets route to the same TranslateX/Y merge so
        // an x and a y preset compose instead of clobbering via the `translate` shorthand.
        private static readonly Dictionary<string, float> s_translateFraction = new()
        {
            ["1/2"] = 50f, ["1/3"] = 100f / 3f, ["2/3"] = 200f / 3f,
            ["1/4"] = 25f, ["2/4"] = 50f, ["3/4"] = 75f,
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
        // '/'-bearing translate fraction, and the positive per-axis translate/scale presets (no USS class — a
        // per-axis rule would clobber the other axis via the shorthand). Other positive non-slash names (mt-2,
        // rotate-6) keep their USS classes and are intentionally NOT claimed here.
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
            // Sizing fractions (w-1/2, h-2/3, size-1/4) have no USS class — USS selectors cannot spell '/' —
            // so the slash form routes to the resolver (the bracket form w-[50%] is claimed by the '[' check).
            if ((cls.StartsWith("w-", StringComparison.Ordinal)
                    || cls.StartsWith("h-", StringComparison.Ordinal)
                    || cls.StartsWith("size-", StringComparison.Ordinal))
                && cls.IndexOf('/') >= 0)
            {
                return true;
            }
            // Per-axis scale presets (scale-x-50 / scale-y-110) have no USS class; route them to the merge path.
            // The bracket form (scale-x-[..]) is claimed by the dispatch's '[' check, not here. Span-based to
            // keep this per-class hot gate allocation-free (matching the AsSpan style above).
            if (cls.StartsWith("scale-x-", StringComparison.Ordinal) || cls.StartsWith("scale-y-", StringComparison.Ordinal))
            {
                return SuffixInKeys(cls.AsSpan("scale-x-".Length), s_axisScale); // "scale-x-" and "scale-y-" share a length
            }
            return false;
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

        // Parses the non-bracket static-scale forms that have no USS class: negative margins on the
        // preset scale (-mt-2 -> MarginTop -8px), the negative rotate preset (-rotate-6 -> -6deg), and the
        // translate fraction / negative px translate (translate-x-1/2 -> 50%, -translate-x-6 -> -24px).
        // Returns false for anything else, so positive USS-backed names fall through to the class list.
        private static bool TryParseStaticScale(string className, out ArbitraryStyle result)
        {
            result = default;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }
            // Sizing fractions (w-1/2, h-2/3, size-1/4) are non-negated and resolve to a percent of the parent.
            if (TryParseSizingFraction(className, out result))
            {
                return true;
            }
            var negate = className[0] == '-';
            var body = negate ? className.Substring(1) : className;

            if (negate)
            {
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
            }

            // Per-axis scale presets (positive only; a flip is the arbitrary scale-x-[-1]). Routed to the same
            // ScaleX/ScaleY merge as the bracket form so the axes compose onto one inline `scale`.
            if (!negate
                && (body.StartsWith("scale-x-", StringComparison.Ordinal) || body.StartsWith("scale-y-", StringComparison.Ordinal)))
            {
                var isScaleX = body.StartsWith("scale-x-", StringComparison.Ordinal);
                if (!s_axisScale.TryGetValue(body.Substring("scale-x-".Length), out var factor))
                {
                    return false;
                }
                result = new ArbitraryStyle(
                    isScaleX ? ArbitraryProperty.ScaleX : ArbitraryProperty.ScaleY, factor, LengthUnit.Pixel);
                return true;
            }

            var isX = body.StartsWith("translate-x-", StringComparison.Ordinal);
            var isY = !isX && body.StartsWith("translate-y-", StringComparison.Ordinal);
            if (isX || isY)
            {
                var suffix = body.Substring("translate-x-".Length); // the x and y prefixes share a length
                var property = isX ? ArbitraryProperty.TranslateX : ArbitraryProperty.TranslateY;
                if (s_translateFraction.TryGetValue(suffix, out var pct))
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

        // Fractional sizing (w-1/2, h-2/3, size-3/4 → a percent of the parent). USS cannot spell '/',
        // so these resolve here as inline percent rather than via a USS class. Width/Height target their own
        // property; size-* fans out to both (the Size property). Only the denominators (2/3/4/5/6/12)
        // and a numerator in 1..d-1 are accepted (100% is the separate `*-full` utility; anything else falls
        // through and is not claimed).
        private static bool TryParseSizingFraction(string className, out ArbitraryStyle result)
        {
            result = default;
            ArbitraryProperty property;
            int prefixLen;
            if (className.StartsWith("w-", StringComparison.Ordinal)) { property = ArbitraryProperty.Width; prefixLen = 2; }
            else if (className.StartsWith("h-", StringComparison.Ordinal)) { property = ArbitraryProperty.Height; prefixLen = 2; }
            else if (className.StartsWith("size-", StringComparison.Ordinal)) { property = ArbitraryProperty.Size; prefixLen = 5; }
            else { return false; }

            var frac = className.Substring(prefixLen);
            var slash = frac.IndexOf('/');
            if (slash <= 0 || slash >= frac.Length - 1)
            {
                return false;
            }
            if (!int.TryParse(frac.Substring(0, slash), out var n)
                || !int.TryParse(frac.Substring(slash + 1), out var d))
            {
                return false;
            }
            if (d != 2 && d != 3 && d != 4 && d != 5 && d != 6 && d != 12)
            {
                return false;
            }
            if (n < 1 || n >= d)
            {
                return false;
            }
            result = new ArbitraryStyle(property, 100f * n / d, LengthUnit.Percent);
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
            [ArbitraryProperty.BorderRadius] = new Action<IStyle, StyleLength>[]
            {
                (s, v) => s.borderTopLeftRadius = v, (s, v) => s.borderTopRightRadius = v,
                (s, v) => s.borderBottomLeftRadius = v, (s, v) => s.borderBottomRightRadius = v,
            },
            [ArbitraryProperty.BorderTopRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderTopLeftRadius = v, (s, v) => s.borderTopRightRadius = v },
            [ArbitraryProperty.BorderRightRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderTopRightRadius = v, (s, v) => s.borderBottomRightRadius = v },
            [ArbitraryProperty.BorderBottomRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderBottomLeftRadius = v, (s, v) => s.borderBottomRightRadius = v },
            [ArbitraryProperty.BorderLeftRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderTopLeftRadius = v, (s, v) => s.borderBottomLeftRadius = v },
            [ArbitraryProperty.BorderTopLeftRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderTopLeftRadius = v },
            [ArbitraryProperty.BorderTopRightRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderTopRightRadius = v },
            [ArbitraryProperty.BorderBottomLeftRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderBottomLeftRadius = v },
            [ArbitraryProperty.BorderBottomRightRadius] = new Action<IStyle, StyleLength>[] { (s, v) => s.borderBottomRightRadius = v },
            [ArbitraryProperty.FontSize] = new Action<IStyle, StyleLength>[] { (s, v) => s.fontSize = v },
            [ArbitraryProperty.LetterSpacing] = new Action<IStyle, StyleLength>[] { (s, v) => s.letterSpacing = v },
            // size-[..] fans out to width + height (same dual-setter shape as Inset).
            [ArbitraryProperty.Size] = new Action<IStyle, StyleLength>[] { (s, v) => s.width = v, (s, v) => s.height = v },
            [ArbitraryProperty.FlexBasis] = new Action<IStyle, StyleLength>[] { (s, v) => s.flexBasis = v },
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
            // opacity-[..] is a unitless StyleFloat (0..1).
            [ArbitraryProperty.Opacity] = new Action<IStyle, StyleFloat>[] { (s, v) => s.opacity = v },
        };

        #endregion

        // Per-element, per-property stack of arbitrary-value layers keyed by priority (ascending). A
        // ConditionalWeakTable auto-drops entries when an element is GC'd; pooled (reused) elements are scrubbed
        // explicitly via ClearAll so no layer ghosts across reuse.
        private sealed class LayerMap : Dictionary<ArbitraryProperty, SortedList<int, ArbitraryStyle>> { }

        private static readonly ConditionalWeakTable<VisualElement, LayerMap> s_layers = new();

        // Registers style at priority for its property and applies the
        // winning (highest-priority) layer inline. Base utilities use StyleLayerPriority.Base
        // (the default); variant manipulators pass their state's priority so a variant layers OVER the base.
        public static void Apply(VisualElement element, in ArbitraryStyle style, int priority = StyleLayerPriority.Base)
        {
            var map = s_layers.GetValue(element, static _ => new LayerMap());
            if (!map.TryGetValue(style.Property, out var layers))
            {
                layers = new SortedList<int, ArbitraryStyle>();
                map[style.Property] = layers;
            }
            layers[priority] = style;
            ResolveAndApply(element, style.Property, map);
        }

        // Removes the layer at priority for property and re-applies the
        // next-highest surviving layer (or clears the inline style when none remain). This is what makes a
        // variant turning off fall back to a still-active variant or the base value instead of wiping the
        // property — and what keeps the two translate axes independent.
        public static void Clear(VisualElement element, ArbitraryProperty property, int priority = StyleLayerPriority.Base)
        {
            if (!s_layers.TryGetValue(element, out var map))
            {
                ClearInline(element, property);
                return;
            }
            if (map.TryGetValue(property, out var layers))
            {
                layers.Remove(priority);
                if (layers.Count == 0) map.Remove(property);
            }
            ResolveAndApply(element, property, map);
        }

        // Drops all arbitrary-value layers tracked for element. Called when the element is
        // cleaned up / returned to a pool so a later reuse does not inherit a prior consumer's layers.
        public static void ClearAll(VisualElement element)
        {
            if (element != null) s_layers.Remove(element);
        }

        // Resolves an inline-value class token to inline style — arbitrary value first, then background
        // image. When neither resolver claims it, the token is added to the USS class list unless
        // addToClassListFallback is false (the reapply path passes false: an inline-classified token that
        // no resolver owns — e.g. font-[..], owned by StyleFontResolver — must never enter the class list).
        // The caller must have confirmed IsInlineResolved(core) and stripped the important bang; priority
        // is the layer the bang selects (Important when present, Base otherwise). Mirror of ClearClassToken.
        public static void ApplyClassToken(VisualElement element, string core, int priority, bool addToClassListFallback = true)
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
        public static void ClearClassToken(VisualElement element, string core, int priority)
        {
            if (TryParse(core, out var style))
            {
                Clear(element, style.Property, priority);
            }
            else if (StyleBackgroundImageResolver.TryParse(core, out _))
            {
                StyleBackgroundImageResolver.Clear(element);
            }
            else
            {
                element.RemoveFromClassList(core);
            }
        }

        // Applies the winning layer for a property. Translate and scale are special: their axis layers share
        // one inline `translate` / `scale`, so they are resolved and composed (a missing translate axis is 0;
        // a missing scale axis falls back to the uniform scale-[..], else 1, the identity).
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
            if (IsFilter(property))
            {
                ApplyCombinedFilter(element, map);
                return;
            }
            if (property == ArbitraryProperty.TransitionDuration)
            {
                ApplyTransitionDuration(element, map);
                return;
            }
            if (map.TryGetValue(property, out var layers) && layers.Count > 0)
            {
                // SortedList is ascending by priority, so the last entry is the highest-priority winner.
                ApplyInline(element, layers.Values[layers.Count - 1]);
            }
            else
            {
                ClearInline(element, property);
            }
        }

        private static void ApplyCombinedTranslate(VisualElement element, LayerMap map)
        {
            var hasX = map.TryGetValue(ArbitraryProperty.TranslateX, out var xl) && xl.Count > 0;
            var hasY = map.TryGetValue(ArbitraryProperty.TranslateY, out var yl) && yl.Count > 0;
            if (!hasX && !hasY)
            {
                element.style.translate = StyleKeyword.Null;
                return;
            }
            var x = hasX ? new Length(xl.Values[xl.Count - 1].Value, xl.Values[xl.Count - 1].Unit) : new Length(0f);
            var y = hasY ? new Length(yl.Values[yl.Count - 1].Value, yl.Values[yl.Count - 1].Unit) : new Length(0f);
            element.style.translate = new Translate(x, y);
        }

        private static void ApplyCombinedScale(VisualElement element, LayerMap map)
        {
            var hasX = map.TryGetValue(ArbitraryProperty.ScaleX, out var xl) && xl.Count > 0;
            var hasY = map.TryGetValue(ArbitraryProperty.ScaleY, out var yl) && yl.Count > 0;
            var hasUniform = map.TryGetValue(ArbitraryProperty.Scale, out var ul) && ul.Count > 0;
            if (!hasX && !hasY && !hasUniform)
            {
                element.style.scale = StyleKeyword.Null;
                return;
            }
            // A per-axis value wins for its axis; the uniform scale-[v] is the fallback for an axis not set
            // explicitly; a wholly-missing axis defaults to 1 (identity), NOT 0 — scale-x-[.5] leaves y unscaled.
            var uniform = hasUniform ? ul.Values[ul.Count - 1].Value : 1f;
            var x = hasX ? xl.Values[xl.Count - 1].Value : uniform;
            var y = hasY ? yl.Values[yl.Count - 1].Value : uniform;
            element.style.scale = new Scale(new Vector2(x, y));
        }

        // transition-duration is a StyleList<TimeValue> (not a StyleLength), so the winning duration-[..] layer
        // is written out-of-band as a single-element list. The stored Value is in SECONDS. No null layer -> clear.
        private static void ApplyTransitionDuration(VisualElement element, LayerMap map)
        {
            if (map.TryGetValue(ArbitraryProperty.TransitionDuration, out var layers) && layers.Count > 0)
            {
                element.style.transitionDuration = new List<TimeValue>
                {
                    new TimeValue(layers.Values[layers.Count - 1].Value, TimeUnit.Second),
                };
            }
            else
            {
                element.style.transitionDuration = StyleKeyword.Null;
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
            // Canonical CSS filter order (blur, brightness, contrast, grayscale, hue-rotate, invert,
            // saturate, sepia) so stacked filters compose identically to the browser.
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

        private static void ApplyCombinedFilter(VisualElement element, LayerMap map)
        {
            List<FilterFunction> functions = null;
            foreach (var prop in s_filterOrder)
            {
                if (!map.TryGetValue(prop, out var layers) || layers.Count == 0)
                {
                    continue;
                }
                (functions ??= new List<FilterFunction>()).Add(
                    BuildFilter(prop, layers.Values[layers.Count - 1].Value));
            }
            if (functions == null)
            {
                element.style.filter = StyleKeyword.Null;
                return;
            }
            element.style.filter = functions;
        }

        private static FilterFunction BuildFilter(ArbitraryProperty prop, float value)
        {
            // brightness has no UITK filter type: Tint multiplies the rendered RGB by a color, so
            // brightness(N) == tint(N, N, N, 1). Tint takes a COLOR parameter, not a float. NB: in a Linear
            // color-space project UITK linearizes the tint before the multiply, so the darkening is stronger
            // than CSS brightness(N) (which multiplies in gamma space); the match is exact only in Gamma space.
            // Values are clamped to [0,1] by Tint, so the parser only admits N<=1 (over-brighten is rejected).
            if (prop == ArbitraryProperty.FilterBrightness)
            {
                var tint = new FilterFunction(FilterFunctionType.Tint);
                tint.AddParameter(new FilterParameter(new Color(value, value, value, 1f)));
                return tint;
            }
            // saturate has no UITK filter type either: saturate(N) == grayscale(1 - N) for N in 0..1 (both lerp
            // the color toward its luminance). The stored Value is the saturation fraction, so emit grayscale of
            // its complement.
            if (prop == ArbitraryProperty.FilterSaturate)
            {
                var gray = new FilterFunction(FilterFunctionType.Grayscale);
                gray.AddParameter(new FilterParameter(1f - value));
                return gray;
            }

            var type = prop switch
            {
                ArbitraryProperty.FilterBlur => FilterFunctionType.Blur,
                ArbitraryProperty.FilterContrast => FilterFunctionType.Contrast,
                ArbitraryProperty.FilterGrayscale => FilterFunctionType.Grayscale,
                ArbitraryProperty.FilterHueRotate => FilterFunctionType.HueRotate,
                ArbitraryProperty.FilterInvert => FilterFunctionType.Invert,
                _ => FilterFunctionType.Sepia,
            };
            // Only the single-arg ctor + AddParameter are public (the (type,value) ctors are internal).
            var fn = new FilterFunction(type);
            fn.AddParameter(new FilterParameter(value));
            return fn;
        }

        // Writes a single ArbitraryStyle to the element's inline style (no layering). Internal: callers go
        // through Apply / Clear so per-property layering is respected.
        private static void ApplyInline(VisualElement element, in ArbitraryStyle style)
        {
            // Transform properties (scale / translate / rotate) are not StyleLength and
            // are written through their dedicated UITK style properties.
            switch (style.Property)
            {
                // Among transform properties, scale (uniform + per-axis) and both translate axes are composed
                // by ResolveAndApply's combined appliers and never reach here; the only transform cases that
                // land are rotate and aspect-ratio.
                case ArbitraryProperty.Rotate:
                    element.style.rotate = new Rotate(new Angle(style.Value, AngleUnit.Degree));
                    return;
                case ArbitraryProperty.AspectRatio:
                {
                    Ratio ratio = style.Value;          // float -> Ratio (implicit)
                    element.style.aspectRatio = ratio;  // Ratio -> StyleRatio (implicit)
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
                var fs = element.style;
                foreach (var setter in floatSetters)
                {
                    setter(fs, width);
                }
                return;
            }

            if (!PropertySetters.TryGetValue(style.Property, out var setters))
            {
                return;
            }

            var length = new StyleLength(new Length(style.Value, style.Unit));
            var s = element.style;
            foreach (var setter in setters)
            {
                setter(s, length);
            }
        }

        // Clears the inline style for the given property (StyleKeyword.Null reverts to the USS default).
        // Internal: callers go through Clear so a surviving lower-priority layer is re-applied.
        private static void ClearInline(VisualElement element, ArbitraryProperty property)
        {
            switch (property)
            {
                case ArbitraryProperty.Rotate:
                    element.style.rotate = StyleKeyword.Null;
                    return;
                case ArbitraryProperty.AspectRatio:
                    element.style.aspectRatio = StyleKeyword.Null;
                    return;
                // translate and scale are each a single shorthand for both axes (and scale composes the uniform
                // + per-axis layers), so clearing any one reverts the whole property. In the class-diff reconcile
                // path the survivors are restored by FiberNodePatcher.ReapplyArbitraryValues; a direct Clear (or a
                // variant-payload toggle) of one drops the others until the next full re-apply — combine them in a
                // single combined translate/scale when that matters.
                case ArbitraryProperty.TranslateX:
                case ArbitraryProperty.TranslateY:
                    element.style.translate = StyleKeyword.Null;
                    return;
                case ArbitraryProperty.Scale:
                case ArbitraryProperty.ScaleX:
                case ArbitraryProperty.ScaleY:
                    element.style.scale = StyleKeyword.Null;
                    return;
                // Like the axes above, all filter-* utilities share the one inline `filter` list, so clearing any
                // reverts the whole property; the surviving filters are restored by ReapplyArbitraryValues.
                case ArbitraryProperty.FilterBlur:
                case ArbitraryProperty.FilterContrast:
                case ArbitraryProperty.FilterGrayscale:
                case ArbitraryProperty.FilterHueRotate:
                case ArbitraryProperty.FilterInvert:
                case ArbitraryProperty.FilterSepia:
                case ArbitraryProperty.FilterBrightness:
                case ArbitraryProperty.FilterSaturate:
                    element.style.filter = StyleKeyword.Null;
                    return;
                case ArbitraryProperty.TransitionDuration:
                    element.style.transitionDuration = StyleKeyword.Null;
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
                var fs = element.style;
                foreach (var setter in floatSetters)
                {
                    setter(fs, nullFloat);
                }
                return;
            }

            if (!PropertySetters.TryGetValue(property, out var setters))
            {
                return;
            }

            var nullStyle = new StyleLength(StyleKeyword.Null);
            var s = element.style;
            foreach (var setter in setters)
            {
                setter(s, nullStyle);
            }
        }

        private static bool TryGetProperty(string prefix, out ArbitraryProperty property)
        {
            // prefix includes the trailing '-' (e.g. "h-", "min-h-", "mt-", "rounded-").
            // No need to match shorthand prefixes longest-first; switch matches exactly.
            switch (prefix)
            {
                // Size
                case "w-":
                    property = ArbitraryProperty.Width;
                    return true;
                case "h-":
                    property = ArbitraryProperty.Height;
                    return true;
                case "min-w-":
                    property = ArbitraryProperty.MinWidth;
                    return true;
                case "min-h-":
                    property = ArbitraryProperty.MinHeight;
                    return true;
                case "max-w-":
                    property = ArbitraryProperty.MaxWidth;
                    return true;
                case "max-h-":
                    property = ArbitraryProperty.MaxHeight;
                    return true;
                case "size-":
                    property = ArbitraryProperty.Size;
                    return true;
                case "basis-":
                    property = ArbitraryProperty.FlexBasis;
                    return true;

                // Position (per-edge)
                case "top-":
                    property = ArbitraryProperty.Top;
                    return true;
                case "right-":
                    property = ArbitraryProperty.Right;
                    return true;
                case "bottom-":
                    property = ArbitraryProperty.Bottom;
                    return true;
                case "left-":
                    property = ArbitraryProperty.Left;
                    return true;

                // Position (shorthand)
                case "inset-":
                    property = ArbitraryProperty.Inset;
                    return true;
                case "inset-x-":
                    property = ArbitraryProperty.InsetX;
                    return true;
                case "inset-y-":
                    property = ArbitraryProperty.InsetY;
                    return true;

                // Padding (per-edge)
                case "pt-":
                    property = ArbitraryProperty.PaddingTop;
                    return true;
                case "pr-":
                    property = ArbitraryProperty.PaddingRight;
                    return true;
                case "pb-":
                    property = ArbitraryProperty.PaddingBottom;
                    return true;
                case "pl-":
                    property = ArbitraryProperty.PaddingLeft;
                    return true;

                // Padding (shorthand)
                case "p-":
                    property = ArbitraryProperty.Padding;
                    return true;
                case "px-":
                    property = ArbitraryProperty.PaddingX;
                    return true;
                case "py-":
                    property = ArbitraryProperty.PaddingY;
                    return true;

                // Margin (per-edge)
                case "mt-":
                    property = ArbitraryProperty.MarginTop;
                    return true;
                case "mr-":
                    property = ArbitraryProperty.MarginRight;
                    return true;
                case "mb-":
                    property = ArbitraryProperty.MarginBottom;
                    return true;
                case "ml-":
                    property = ArbitraryProperty.MarginLeft;
                    return true;

                // Margin (shorthand)
                case "m-":
                    property = ArbitraryProperty.Margin;
                    return true;
                case "mx-":
                    property = ArbitraryProperty.MarginX;
                    return true;
                case "my-":
                    property = ArbitraryProperty.MarginY;
                    return true;

                // Border Radius (all corners)
                case "rounded-":
                    property = ArbitraryProperty.BorderRadius;
                    return true;

                // Border Radius (per side)
                case "rounded-t-":
                    property = ArbitraryProperty.BorderTopRadius;
                    return true;
                case "rounded-r-":
                    property = ArbitraryProperty.BorderRightRadius;
                    return true;
                case "rounded-b-":
                    property = ArbitraryProperty.BorderBottomRadius;
                    return true;
                case "rounded-l-":
                    property = ArbitraryProperty.BorderLeftRadius;
                    return true;

                // Border Radius (per corner)
                case "rounded-tl-":
                    property = ArbitraryProperty.BorderTopLeftRadius;
                    return true;
                case "rounded-tr-":
                    property = ArbitraryProperty.BorderTopRightRadius;
                    return true;
                case "rounded-bl-":
                    property = ArbitraryProperty.BorderBottomLeftRadius;
                    return true;
                case "rounded-br-":
                    property = ArbitraryProperty.BorderBottomRightRadius;
                    return true;

                // Border Width (all sides + per side). A color value for `border-` is handled
                // earlier in TryParse; here `border-` is the width form.
                case "border-":
                    property = ArbitraryProperty.BorderWidth;
                    return true;
                case "border-t-":
                    property = ArbitraryProperty.BorderTopWidth;
                    return true;
                case "border-r-":
                    property = ArbitraryProperty.BorderRightWidth;
                    return true;
                case "border-b-":
                    property = ArbitraryProperty.BorderBottomWidth;
                    return true;
                case "border-l-":
                    property = ArbitraryProperty.BorderLeftWidth;
                    return true;

                // Font
                case "text-":
                    property = ArbitraryProperty.FontSize;
                    return true;

                // Letter spacing (the tracking-* utilities)
                case "tracking-":
                    property = ArbitraryProperty.LetterSpacing;
                    return true;

                // Note: the transform prefixes (scale- / rotate- / translate-x- / translate-y-) are
                // intercepted earlier in TryParse, not here, so all four share the Apply/Clear switch.

                default:
                    property = default;
                    return false;
            }
        }

        // Parses a bracketed JIT arbitrary value (`[..]`) that must resolve to a non-negative pixel length —
        // the contract shared by gap-[..], divide-*-[..], and ring-[..], where a percentage is meaningless.
        // Takes the whole suffix incl. brackets; returns false for a non-`[..]` token or any non-px / negative
        // value. Span-based so callers pass an existing slice with no extra allocation.
        internal static bool TryParseArbitraryPixels(ReadOnlySpan<char> suffix, out float px)
        {
            px = 0f;
            if (suffix.Length < 2 || suffix[0] != '[' || suffix[suffix.Length - 1] != ']')
            {
                return false;
            }
            if (TryParseValue(suffix.Slice(1, suffix.Length - 2), out var value, out var unit)
                && unit == LengthUnit.Pixel && value >= 0f)
            {
                px = value;
                return true;
            }
            return false;
        }

        // Parses a <length-percentage> token: a '%' suffix is percent; a 'px', 'rem', or no suffix is
        // pixel (bare numbers default to px, and rem is converted at the fixed 1rem = 16px scale because
        // UI Toolkit has no rem unit and no document root to resolve a relative font size against).
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
            else
            {
                // No unit suffix → Pixel.
                parsed = float.TryParse(
                    valueStr,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            }

            return parsed && float.IsFinite(value);
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
