using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Filter value parsers for the arbitrary-value dispatch (StyleArbitraryValueResolver): the filter-function
    // bracket prefixes (blur / grayscale / invert / sepia / contrast / hue-rotate / brightness / saturate) and
    // the non-bracket named presets (blur-sm, contrast-125, hue-rotate-90, ...), plus the dispatch gate that
    // claims them. All resolve to the same Filter* properties that the resolver's compose-and-apply machinery
    // (ApplyCombinedFilter) writes into a single list. The dispatch calls in; this group calls only the
    // resolver's shared scalar grammar (TryParseValue / TryParseFloat / TryParseAngleDegrees) and its own
    // exclusive preset tables, never back into the dispatch or another parser.
    internal static class StyleFilterValueParser
    {
        // The canonical built-in filter family names — the utility prefixes this parser owns. Single
        // source for VelvetFilters' reserved-name check, so filter-[blur:..] can never be registered to
        // mean something different from the blur-* utilities defined here.
        internal static readonly string[] BuiltInFamilyNames =
        {
            "blur", "brightness", "contrast", "grayscale", "hue-rotate", "invert", "saturate", "sepia",
        };

        // The named filter presets (the suffix after the filter prefix). These have no USS class — they
        // route to the same compose-and-apply filter path as the bracket forms (blur-[6px]). blur is px; the
        // bare grayscale/invert/sepia mean 100% and are handled inline (no key). hue-rotate is degrees and is
        // the only filter with a negative preset (-hue-rotate-90).
        private static readonly Dictionary<string, float> s_blurPreset = new()
        {
            ["none"] = 0f, ["sm"] = 4f, ["md"] = 12f, ["lg"] = 16f, ["xl"] = 24f, ["2xl"] = 40f, ["3xl"] = 64f,
        };

        private static readonly Dictionary<string, float> s_contrastPreset = new()
        {
            ["0"] = 0f, ["50"] = 0.5f, ["75"] = 0.75f, ["100"] = 1f, ["125"] = 1.25f, ["150"] = 1.5f, ["200"] = 2f,
        };

        private static readonly Dictionary<string, float> s_hueRotatePreset = new()
        {
            ["0"] = 0f, ["15"] = 15f, ["30"] = 30f, ["60"] = 60f, ["90"] = 90f, ["180"] = 180f,
        };

        // brightness multiplier presets (rendered via Tint). UI Toolkit's Tint clamps the per-channel multiply
        // to [0,1] (the color-matrix factor is Clamp01(channel*alpha)), so a factor above 1 collapses to
        // identity and CANNOT brighten. The over-bright values (brightness-105/110/125/150/200) are
        // therefore intentionally absent — only the darken/identity range 0..1 is representable, the same
        // reason saturate>1 is rejected.
        private static readonly Dictionary<string, float> s_brightnessPreset = new()
        {
            ["0"] = 0f, ["50"] = 0.5f, ["75"] = 0.75f, ["90"] = 0.9f, ["95"] = 0.95f, ["100"] = 1f,
        };

        // saturate presets as a 0..1 saturation fraction (rendered via grayscale(1-N)). The over-saturate
        // values (saturate-150 / -200) are intentionally absent: UI Toolkit has no over-saturation filter.
        private static readonly Dictionary<string, float> s_saturatePreset = new()
        {
            ["0"] = 0f, ["50"] = 0.5f, ["100"] = 1f,
        };

        // Filter-function bracket prefixes, routed here so all filter-* utilities share the one compose-and-
        // apply path (ApplyCombinedFilter writes a single list). true (result set) on success; false to
        // reject a matched-but-invalid value; null when not a filter prefix. Most filters reject the negated
        // form (no '-' guard would let them fall through to the length path) — hue-rotate- accepts negation.
        internal static bool? TryParseFilterValue(string prefix, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;

            if (!negate && prefix == "blur-")
            {
                if (!StyleArbitraryValueResolver.TryParseValue(valueSpan, out var blurPx, out var blurUnit) || blurUnit != LengthUnit.Pixel) return false;
                result = new ArbitraryStyle(ArbitraryProperty.FilterBlur, blurPx, LengthUnit.Pixel);
                return true;
            }
            if (!negate && (prefix == "grayscale-" || prefix == "invert-" || prefix == "sepia-" || prefix == "contrast-"))
            {
                if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var amount) || amount < 0f) return false;
                var fp = prefix == "grayscale-" ? ArbitraryProperty.FilterGrayscale
                    : prefix == "invert-" ? ArbitraryProperty.FilterInvert
                    : prefix == "sepia-" ? ArbitraryProperty.FilterSepia
                    : ArbitraryProperty.FilterContrast;
                result = new ArbitraryStyle(fp, amount, LengthUnit.Pixel);
                return true;
            }
            if (prefix == "hue-rotate-")
            {
                if (!StyleArbitraryValueResolver.TryParseAngleDegrees(valueSpan, out var deg)) return false;
                result = new ArbitraryStyle(ArbitraryProperty.FilterHueRotate, negate ? -deg : deg, LengthUnit.Pixel);
                return true;
            }
            // brightness-[N] is a 0..1 multiplier rendered via the built-in Tint filter. N>1 is rejected:
            // UITK's Tint clamps the per-channel factor to [0,1], so over-brightening is not representable.
            if (!negate && prefix == "brightness-")
            {
                if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var b) || b < 0f || b > 1f) return false;
                result = new ArbitraryStyle(ArbitraryProperty.FilterBrightness, b, LengthUnit.Pixel);
                return true;
            }
            // saturate-[N] is a 0..1 saturation fraction (over-saturation N>1 has no UITK filter), rendered
            // via the built-in Grayscale filter as grayscale(1-N).
            if (!negate && prefix == "saturate-")
            {
                if (!StyleArbitraryValueResolver.TryParseFloat(valueSpan, out var s) || s < 0f || s > 1f) return false;
                result = new ArbitraryStyle(ArbitraryProperty.FilterSaturate, s, LengthUnit.Pixel);
                return true;
            }

            return null;
        }

        // Warn-once bookkeeping for a filter-[name:...] token whose name was never registered with
        // VelvetFilters: logging once per NAME (not once per resolve) keeps a class re-resolved on every
        // re-render (e.g. toggled by a hover variant) from spamming the console. Reset on the same
        // subsystem-registration hook that clears the registry itself, so the set starts empty whenever
        // the registrations do (domain reload / play-mode entry); within one loaded domain an
        // already-warned name stays silent, even if it is registered and unregistered in between.
        private static readonly HashSet<string> s_warnedUnregistered = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarnOnceState() => s_warnedUnregistered.Clear();
#endif

        // filter-[name] / filter-[name:arg(:arg)*] resolves a name registered via VelvetFilters.Register.
        // The name is everything up to the first ':'; the definition's parameter DECLARATION drives the
        // rest. Each colon-separated segment fills the next declared slot and is parsed by that slot's
        // declared type — a float slot takes a signed float, a color slot the shared color grammar — and
        // the missing tail is padded from the declared defaults, so the resolved argument list always
        // carries the full declared parameter count. Rejects (the whole token falls through as an inert
        // class): more segments than declared slots, a segment failing its slot's grammar, or an empty
        // segment — a registered name must not half-apply. Never negated: "-filter-[..]" has no meaning
        // for a named reference.
        internal static bool? TryParseCustomFilter(string prefix, ReadOnlySpan<char> valueSpan, bool negate, out ArbitraryStyle result)
        {
            result = default;
            if (negate || prefix != "filter-")
            {
                return null;
            }

            var colon = valueSpan.IndexOf(':');
            var nameSpan = colon < 0 ? valueSpan : valueSpan.Slice(0, colon);
            if (nameSpan.Length == 0)
            {
                return false;
            }
            var name = nameSpan.ToString();

            if (!VelvetFilters.TryGet(name, out var definition))
            {
                if (s_warnedUnregistered.Add(name))
                {
                    Debug.LogWarning($"[VelvetFilters] \"{name}\" is not registered; the filter-[{name}:...] class is inert.");
                }
                return false;
            }

            var declarations = definition.parameters;
            var declaredCount = declarations?.Length ?? 0;
            // Registration rejects a declaration of more than the 4 parameters a filter function can
            // carry, but `parameters` is a mutable array property on a live ScriptableObject, so re-guard
            // here: composing past the cap would throw during style resolution.
            if (declaredCount > 4)
            {
                return false;
            }

            // Count the supplied segments up front: over-supply rejects before any parsing (a segment
            // with no declared slot has no type to parse against), and the count sizes the argument
            // array exactly.
            var suppliedCount = 0;
            if (colon >= 0)
            {
                suppliedCount = 1;
                for (var i = colon + 1; i < valueSpan.Length; i++)
                {
                    if (valueSpan[i] == ':') suppliedCount++;
                }
            }
            if (suppliedCount > declaredCount)
            {
                return false;
            }

            var args = declaredCount == 0 ? Array.Empty<FilterParameter>() : new FilterParameter[declaredCount];
            var remaining = colon < 0 ? ReadOnlySpan<char>.Empty : valueSpan.Slice(colon + 1);
            for (var slot = 0; slot < suppliedCount; slot++)
            {
                var next = remaining.IndexOf(':');
                var segment = next < 0 ? remaining : remaining.Slice(0, next);
                if (!TryParseFilterArg(segment, declarations![slot].interpolationDefaultValue.type, out args[slot]))
                {
                    return false;
                }
                remaining = next < 0 ? ReadOnlySpan<char>.Empty : remaining.Slice(next + 1);
            }
            // Pad the unsupplied tail from the declared defaults — the same values the engine's USS
            // parser pads with. The public FilterFunction construction path performs no padding of its
            // own, and an under-filled function stops binding at its parameterCount at render time,
            // leaving stale material-property state where the declared default should be.
            for (var slot = suppliedCount; slot < declaredCount; slot++)
            {
                args[slot] = declarations![slot].interpolationDefaultValue;
            }

            result = new ArbitraryStyle(ArbitraryProperty.FilterCustom, new CustomFilterValue(name, definition, args));
            return true;
        }

        // A single filter-[name:...] argument segment, parsed against its declared slot type: a float
        // slot takes a signed float (invariant culture), a color slot the shared color grammar (#hex /
        // rgb() / named — the same grammar bg-[#fff] uses). A segment failing its slot's grammar (or an
        // empty segment — a double colon or trailing colon) rejects: binding a float where the shader
        // declared a color (or vice versa) would only make the engine reset the parameter to its default
        // with a warning at render time.
        private static bool TryParseFilterArg(ReadOnlySpan<char> segment, FilterParameterType slotType, out FilterParameter parameter)
        {
            parameter = default;
            if (segment.Length == 0)
            {
                return false;
            }
            if (slotType == FilterParameterType.Float)
            {
                if (!StyleArbitraryValueResolver.TryParseFloat(segment, out var f))
                {
                    return false;
                }
                parameter = new FilterParameter(f);
                return true;
            }
            if (!StyleColorValueParser.TryParseColor(segment, out var color))
            {
                return false;
            }
            parameter = new FilterParameter(color);
            return true;
        }

        // Extracts the NAME from a token of the filter-[name(:args)] shape — purely syntactic: no
        // registry lookup, no warning, no argument validation. The CLEAR paths resolve names through
        // this instead of TryParseCustomFilter: a layer applied while its name was registered must stay
        // locatable (and removable) after the name is unregistered, and resolution-for-apply is
        // registry-gated while resolution-for-clear must not be.
        internal static bool TryExtractCustomFilterName(string cls, out string name)
        {
            name = null!;
            const string shape = "filter-[";
            if (cls == null || cls.Length < shape.Length + 2
                || !cls.StartsWith(shape, StringComparison.Ordinal)
                || cls[cls.Length - 1] != ']')
            {
                return false;
            }
            var body = cls.AsSpan(shape.Length, cls.Length - shape.Length - 1);
            var colon = body.IndexOf(':');
            var nameSpan = colon < 0 ? body : body.Slice(0, colon);
            if (nameSpan.Length == 0)
            {
                return false;
            }
            name = nameSpan.ToString();
            return true;
        }

        // Cheap dispatch gate (prefix-only): true when cls MIGHT be a named filter preset, so the reconciler
        // routes it to TryParseFilterPreset instead of the class list. Loose by design — an unrecognized
        // suffix (blur-foo) fails the precise parse and falls through to the class list, same as any unknown
        // utility. Only consulted for non-bracket tokens (the bracket form blur-[6px] is claimed by the
        // dispatch's '[' check).
        internal static bool IsFilterPreset(string cls)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }
            if (cls == "blur" || cls == "grayscale" || cls == "invert" || cls == "sepia")
            {
                return true;
            }
            return cls.StartsWith("blur-", StringComparison.Ordinal)
                || cls.StartsWith("contrast-", StringComparison.Ordinal)
                || cls.StartsWith("hue-rotate-", StringComparison.Ordinal)
                || cls.StartsWith("-hue-rotate-", StringComparison.Ordinal)
                || cls.StartsWith("grayscale-", StringComparison.Ordinal)
                || cls.StartsWith("invert-", StringComparison.Ordinal)
                || cls.StartsWith("sepia-", StringComparison.Ordinal)
                || cls.StartsWith("brightness-", StringComparison.Ordinal)
                || cls.StartsWith("saturate-", StringComparison.Ordinal);
        }

        // True when the leaf token is a filter utility — a named preset (blur-sm, -hue-rotate-90, bare
        // blur/grayscale/…), a bracket form (blur-[6px], -hue-rotate-[30deg]), or a custom filter-[name:..].
        // IsFilterPreset already matches every bracket form (each is "<family>-[..]" and its
        // StartsWith("<family>-") check fires on the "<family>-" prefix), so the only case it misses is the
        // custom filter-[..] token.
        internal static bool IsFilterLeaf(string? leaf)
            => leaf != null && (IsFilterPreset(leaf) || leaf.StartsWith("filter-[", StringComparison.Ordinal));

        // True when ANY class resolves to a filter utility, INCLUDING one carried by a state variant
        // (hover:blur-sm, dark:hue-rotate-90, sm:blur-md, group-hover:filter-[…], and stacked forms). A variant
        // filter is applied by a manipulator at state time — outside the reconcile path that decides the
        // bounds-spacer — so the spacer must exist whenever a filter COULD apply, not only when it is active;
        // peeling the variant layers to the leaf utility mirrors StyleClipPathClass.WantsClipWrapper, which
        // keeps its wrapper alive for a variant-only clip for the same reason. Loose like IsFilterPreset: a
        // matched-but-invalid suffix still gates true, which is harmless for the consumer (the bounds-spacer
        // reserves render bounds — over-reserving is invisible, under-reserving would clip the paint).
        internal static bool HasFilterClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                var leaf = cls;
                while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                {
                    leaf = payload;
                }
                if (IsFilterLeaf(leaf))
                {
                    return true;
                }
            }
            return false;
        }

        // Parses the non-bracket named filter presets (blur-sm, blur, grayscale, contrast-125,
        // hue-rotate-90, grayscale-0, ...) to the same Filter* properties as the bracket forms, so they merge
        // through ApplyCombinedFilter. hue-rotate is the only filter with a negative preset (-hue-rotate-90).
        internal static bool TryParseFilterPreset(string className, out ArbitraryStyle result)
        {
            result = default;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            var negate = className[0] == '-';
            var body = negate ? className.Substring(1) : className;

            // hue-rotate first: it is the one filter that accepts the negated form.
            if (body.StartsWith("hue-rotate-", StringComparison.Ordinal))
            {
                if (!s_hueRotatePreset.TryGetValue(body.Substring("hue-rotate-".Length), out var deg))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.FilterHueRotate, negate ? -deg : deg, LengthUnit.Pixel);
                return true;
            }
            if (negate)
            {
                return false; // no other filter has a negative preset
            }

            switch (body)
            {
                case "blur": result = new ArbitraryStyle(ArbitraryProperty.FilterBlur, 8f, LengthUnit.Pixel); return true;
                case "grayscale": result = new ArbitraryStyle(ArbitraryProperty.FilterGrayscale, 1f, LengthUnit.Pixel); return true;
                case "invert": result = new ArbitraryStyle(ArbitraryProperty.FilterInvert, 1f, LengthUnit.Pixel); return true;
                case "sepia": result = new ArbitraryStyle(ArbitraryProperty.FilterSepia, 1f, LengthUnit.Pixel); return true;
                case "grayscale-0": result = new ArbitraryStyle(ArbitraryProperty.FilterGrayscale, 0f, LengthUnit.Pixel); return true;
                case "invert-0": result = new ArbitraryStyle(ArbitraryProperty.FilterInvert, 0f, LengthUnit.Pixel); return true;
                case "sepia-0": result = new ArbitraryStyle(ArbitraryProperty.FilterSepia, 0f, LengthUnit.Pixel); return true;
            }

            if (body.StartsWith("blur-", StringComparison.Ordinal))
            {
                if (!s_blurPreset.TryGetValue(body.Substring("blur-".Length), out var px))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.FilterBlur, px, LengthUnit.Pixel);
                return true;
            }
            if (body.StartsWith("contrast-", StringComparison.Ordinal))
            {
                if (!s_contrastPreset.TryGetValue(body.Substring("contrast-".Length), out var amount))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.FilterContrast, amount, LengthUnit.Pixel);
                return true;
            }
            if (body.StartsWith("brightness-", StringComparison.Ordinal))
            {
                if (!s_brightnessPreset.TryGetValue(body.Substring("brightness-".Length), out var b))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.FilterBrightness, b, LengthUnit.Pixel);
                return true;
            }
            if (body.StartsWith("saturate-", StringComparison.Ordinal))
            {
                // Only the 0..1 saturation presets resolve; saturate-150 / -200 are absent (no UITK over-saturate).
                if (!s_saturatePreset.TryGetValue(body.Substring("saturate-".Length), out var s))
                {
                    return false;
                }
                result = new ArbitraryStyle(ArbitraryProperty.FilterSaturate, s, LengthUnit.Pixel);
                return true;
            }
            return false;
        }
    }
}
