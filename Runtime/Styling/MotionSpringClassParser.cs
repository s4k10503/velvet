using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>The style channel a spring transition can drive — see <see cref="MotionSpringClassParser"/>.</summary>
    internal enum SpringAxis
    {
        Opacity,
        TranslateX,
        TranslateY,
        Scale,
        Rotate,
    }

    /// <summary>
    /// Resolves the numeric value a single utility class contributes to a spring-animated channel, and combines
    /// a variant's from/to class arrays into a <see cref="SpringPlan"/> — WITHOUT reading <c>resolvedStyle</c> or
    /// touching a panel: <c>StyleAnimationScheduler</c>'s from/to class swap for a spring lands the classes at
    /// rest immediately (see <see cref="MotionSpringDriver"/>), so there is no "before/after" style-resolution
    /// window to read values from even if a panel were available — the numeric values have to come from the
    /// classes' own known definitions instead.
    /// </summary>
    /// <remarks>
    /// Scope: only <see cref="SpringAxis.Opacity"/> and the transform trio (translate x/y in PIXELS, uniform
    /// scale, rotate degrees) are recognized — matching the utilities <c>_effects.uss</c> / <c>_transforms.uss</c>
    /// define plus their arbitrary-value/spacing-scale equivalents in <see cref="StyleArbitraryValueResolver"/>.
    /// A class the parser does not recognize (a color, an arbitrary length on an unrelated property, a
    /// percentage-based translate like <c>translate-x-1/2</c>/<c>translate-x-full</c>, or a per-axis
    /// <c>scale-x-</c>/<c>scale-y-</c>) is simply skipped: it still applies as a plain class (untouched by the
    /// class-swap step), it just is not animated by the spring.
    /// </remarks>
    internal static class MotionSpringClassParser
    {
        /// <summary>
        /// A resolved (from, to) pair per channel; null when neither side of the swap named that channel (out of
        /// scope for this play, or simply unchanged).
        /// </summary>
        internal struct SpringPlan
        {
            public (float from, float to)? Opacity;
            public (float from, float to)? TranslateX;
            public (float from, float to)? TranslateY;
            public (float from, float to)? Scale;
            public (float from, float to)? Rotate;

            public bool IsEmpty => Opacity == null && TranslateX == null && TranslateY == null
                && Scale == null && Rotate == null;
        }

        // Mirrors _effects.uss's fixed opacity scale exactly (a class outside this exact set has no matching
        // USS rule, so accepting it here would let the spring settle on a value the cleared inline style would
        // then NOT reproduce from the cascade).
        private static readonly Dictionary<string, float> s_opacity = new()
        {
            ["opacity-0"] = 0f, ["opacity-5"] = 0.05f, ["opacity-10"] = 0.1f, ["opacity-15"] = 0.15f,
            ["opacity-20"] = 0.2f, ["opacity-25"] = 0.25f, ["opacity-30"] = 0.3f, ["opacity-35"] = 0.35f,
            ["opacity-40"] = 0.4f, ["opacity-45"] = 0.45f, ["opacity-50"] = 0.5f, ["opacity-55"] = 0.55f,
            ["opacity-60"] = 0.6f, ["opacity-65"] = 0.65f, ["opacity-70"] = 0.7f, ["opacity-75"] = 0.75f,
            ["opacity-80"] = 0.8f, ["opacity-85"] = 0.85f, ["opacity-90"] = 0.9f, ["opacity-95"] = 0.95f,
            ["opacity-100"] = 1f,
        };

        /// <summary>
        /// Resolves a single class token to the spring channel it touches. Tries the static literal tables
        /// first (classes with a REAL static USS rule, so <see cref="StyleArbitraryValueResolver"/>
        /// deliberately does not parse them) — the opacity scale here, and the uniform scale / rotate
        /// magnitude shared from <see cref="StyleArbitraryValueResolver"/>'s own preset tables (single-sourced
        /// rather than a second hand-copied dictionary) — then falls back to
        /// <see cref="StyleArbitraryValueResolver.TryParse"/> for the bracket/spacing-scale forms that have no
        /// USS class at all (<c>translate-x-4</c>, <c>-rotate-6</c>, <c>opacity-[.5]</c>, …). Percentage-based
        /// translate and per-axis scale-x-/scale-y- are recognized by that resolver but rejected here (out of
        /// scope — see the type doc).
        /// </summary>
        internal static bool TryParseAxisValue(string className, out SpringAxis axis, out float value)
        {
            axis = default;
            value = 0f;
            if (string.IsNullOrEmpty(className))
            {
                return false;
            }

            var core = StyleArbitraryValueResolver.StripImportant(className, out _);

            if (s_opacity.TryGetValue(core, out value))
            {
                axis = SpringAxis.Opacity;
                return true;
            }
            // Uniform scale-N: the bare suffix mirrors the per-axis scale-x-/scale-y- preset's own numeric
            // scale exactly, so it is looked up in that same table rather than a duplicate one.
            if (core.StartsWith("scale-", System.StringComparison.Ordinal)
                && StyleArbitraryValueResolver.TryGetAxisScale(core.Substring("scale-".Length), out value))
            {
                axis = SpringAxis.Scale;
                return true;
            }
            // rotate-N / rotate-nN: the magnitude table is shared with the resolver's own negative-rotate
            // preset (which only ever stores the unsigned form, negating the negative "-rotate-N" spelling
            // itself); the sign here is decided by which of the two class spellings this token used.
            if (core.StartsWith("rotate-", System.StringComparison.Ordinal))
            {
                var suffix = core.Substring("rotate-".Length);
                var negated = suffix.StartsWith("n", System.StringComparison.Ordinal);
                var magnitude = negated ? suffix.Substring(1) : suffix;
                if (StyleArbitraryValueResolver.TryGetRotateScale(magnitude, out var degrees))
                {
                    axis = SpringAxis.Rotate;
                    value = negated ? -degrees : degrees;
                    return true;
                }
            }

            if (StyleArbitraryValueResolver.TryParse(core, out var arbitrary))
            {
                switch (arbitrary.Property)
                {
                    case ArbitraryProperty.Opacity:
                        axis = SpringAxis.Opacity;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.Scale:
                        axis = SpringAxis.Scale;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.Rotate:
                        axis = SpringAxis.Rotate;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.TranslateX when arbitrary.Unit == LengthUnit.Pixel:
                        axis = SpringAxis.TranslateX;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.TranslateY when arbitrary.Unit == LengthUnit.Pixel:
                        axis = SpringAxis.TranslateY;
                        value = arbitrary.Value;
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the spring plan for a from/to class-array swap: each channel named by EITHER side is in
        /// scope, with the un-naming side falling back to that channel's identity value (opacity 1, translate 0,
        /// scale 1, rotate 0deg) — the common "declare only what changes" authoring style (e.g. a `visible`
        /// variant that only sets `opacity-100` and relies on the default scale/rotate/position). A resting
        /// baseline set by some OTHER, unrelated class on the element is not accounted for (undocumented — see
        /// the type doc's scope note), EXCEPT for translate: since translate x/y always compose onto one inline
        /// style (see below), naming only one axis still forces a channel for the other, and <paramref
        /// name="restingTranslateX"/> / <paramref name="restingTranslateY"/> — the element's own current inline
        /// translate, read by the caller before the swap lands — let that forced channel sit at wherever the
        /// element's OWN (unrelated) classes already put it instead of snapping it to identity.
        /// </summary>
        internal static SpringPlan Resolve(string[]? fromClasses, string[]? toClasses,
            float restingTranslateX = 0f, float restingTranslateY = 0f)
        {
            float? fromOpacity = null, toOpacity = null;
            float? fromX = null, toX = null;
            float? fromY = null, toY = null;
            float? fromScale = null, toScale = null;
            float? fromRotate = null, toRotate = null;

            Scan(fromClasses, ref fromOpacity, ref fromX, ref fromY, ref fromScale, ref fromRotate);
            Scan(toClasses, ref toOpacity, ref toX, ref toY, ref toScale, ref toRotate);

            var plan = new SpringPlan();
            if (fromOpacity.HasValue || toOpacity.HasValue)
            {
                plan.Opacity = (fromOpacity ?? 1f, toOpacity ?? 1f);
            }
            if (fromX.HasValue || toX.HasValue || fromY.HasValue || toY.HasValue)
            {
                // Translate x/y are independent springs but always compose onto ONE inline `translate`
                // (UI Toolkit has no separate translateX/translateY style), so once either axis is in scope the
                // other gets a channel too. An axis actually named by either side still falls back to identity
                // on its own un-naming side (the "declare only what changes" rule above); an axis named by
                // NEITHER side — forced into the plan only because its sibling needed one — pins at the
                // element's own resting value instead, so a base translate-y-* class the swap never touches
                // does not get stomped to 0 for the swap's duration.
                var xNamed = fromX.HasValue || toX.HasValue;
                plan.TranslateX = xNamed ? (fromX ?? 0f, toX ?? 0f) : (restingTranslateX, restingTranslateX);
                var yNamed = fromY.HasValue || toY.HasValue;
                plan.TranslateY = yNamed ? (fromY ?? 0f, toY ?? 0f) : (restingTranslateY, restingTranslateY);
            }
            if (fromScale.HasValue || toScale.HasValue)
            {
                plan.Scale = (fromScale ?? 1f, toScale ?? 1f);
            }
            if (fromRotate.HasValue || toRotate.HasValue)
            {
                plan.Rotate = (fromRotate ?? 0f, toRotate ?? 0f);
            }
            return plan;
        }

        private static void Scan(string[]? classes, ref float? opacity, ref float? translateX, ref float? translateY,
            ref float? scale, ref float? rotate)
        {
            if (classes == null)
            {
                return;
            }
            foreach (var cls in classes)
            {
                if (!TryParseAxisValue(cls, out var axis, out var value))
                {
                    continue;
                }
                switch (axis)
                {
                    case SpringAxis.Opacity: opacity = value; break;
                    case SpringAxis.TranslateX: translateX = value; break;
                    case SpringAxis.TranslateY: translateY = value; break;
                    case SpringAxis.Scale: scale = value; break;
                    case SpringAxis.Rotate: rotate = value; break;
                }
            }
        }
    }
}
