using System.Collections.Generic;
using UnityEngine;
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
    /// Scope: <see cref="SpringAxis.Opacity"/> and the transform trio (translate x/y in PIXELS, uniform scale,
    /// rotate degrees) are recognized here — matching the utilities <c>_effects.uss</c> / <c>_transforms.uss</c>
    /// define plus their arbitrary-value/spacing-scale equivalents in <see cref="StyleArbitraryValueResolver"/>.
    /// The color- and length-valued properties are recognized by <see cref="MotionPropertyClassParser"/> and
    /// carried in the same plan (see <see cref="SpringPlan.Colors"/> / <see cref="SpringPlan.Lengths"/>). A class
    /// neither parser recognizes (a percentage-based translate like <c>translate-x-1/2</c>/<c>translate-x-full</c>,
    /// a per-axis <c>scale-x-</c>/<c>scale-y-</c>, or anything outside the property parser's own documented
    /// scope) is simply skipped: it still applies as a plain class (untouched by the class-swap step), it just is
    /// not animated by the spring.
    /// </remarks>
    internal static class MotionSpringClassParser
    {
        /// <summary>One property-valued channel: which property to write, and the color it interpolates between.</summary>
        internal readonly struct ColorChannelPlan
        {
            public readonly ArbitraryProperty Property;
            public readonly Color From;
            public readonly Color To;

            public ColorChannelPlan(ArbitraryProperty property, Color from, Color to)
            {
                Property = property;
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// One length-valued channel: which property to write, the magnitudes it interpolates between, and the
        /// unit BOTH sides carry (a mixed-unit pair never becomes a channel — see <see cref="Resolve"/>).
        /// </summary>
        internal readonly struct LengthChannelPlan
        {
            public readonly ArbitraryProperty Property;
            public readonly float From;
            public readonly float To;
            public readonly LengthUnit Unit;

            public LengthChannelPlan(ArbitraryProperty property, float from, float to, LengthUnit unit)
            {
                Property = property;
                From = from;
                To = to;
                Unit = unit;
            }
        }

        /// <summary>
        /// A resolved (from, to) pair per channel; null when neither side of the swap named that channel (out of
        /// scope for this play, or simply unchanged). The five fixed axes have identity values to fall back on,
        /// so one side naming an axis is enough; the property channels have none and are collected in the two
        /// lists instead, populated only when BOTH sides name the property.
        /// </summary>
        internal struct SpringPlan
        {
            public (float from, float to)? Opacity;
            public (float from, float to)? TranslateX;
            public (float from, float to)? TranslateY;
            public (float from, float to)? Scale;
            public (float from, float to)? Rotate;
            public List<ColorChannelPlan>? Colors;
            public List<LengthChannelPlan>? Lengths;

            public bool IsEmpty => Opacity == null && TranslateX == null && TranslateY == null
                && Scale == null && Rotate == null && Colors == null && Lengths == null;
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
        /// Builds the spring plan for a from/to class-array swap. Each of the five fixed AXES named by EITHER
        /// side is in scope, with the un-naming side falling back to that axis's identity value (opacity 1,
        /// translate 0, scale 1, rotate 0deg) — the common "declare only what changes" authoring style (e.g. a
        /// `visible` variant that only sets `opacity-100` and relies on the default scale/rotate/position). A
        /// resting baseline set by some OTHER, unrelated class on the element is not accounted for (undocumented
        /// — see the type doc's scope note), EXCEPT for translate: since translate x/y always compose onto one
        /// inline style (see below), naming only one axis still forces a channel for the other, and <paramref
        /// name="restingTranslateX"/> / <paramref name="restingTranslateY"/> — the element's own current inline
        /// translate, read by the caller before the swap lands — let that forced channel sit at wherever the
        /// element's OWN (unrelated) classes already put it instead of snapping it to identity.
        /// The color- and length-valued PROPERTY channels follow the stricter both-sides rule instead — see
        /// <see cref="PairProperties"/>.
        /// </summary>
        internal static SpringPlan Resolve(string[]? fromClasses, string[]? toClasses,
            float restingTranslateX = 0f, float restingTranslateY = 0f)
        {
            float? fromOpacity = null, toOpacity = null;
            float? fromX = null, toX = null;
            float? fromY = null, toY = null;
            float? fromScale = null, toScale = null;
            float? fromRotate = null, toRotate = null;
            Dictionary<ArbitraryProperty, ArbitraryStyle>? fromProperties = null, toProperties = null;

            Scan(fromClasses, ref fromOpacity, ref fromX, ref fromY, ref fromScale, ref fromRotate, ref fromProperties);
            Scan(toClasses, ref toOpacity, ref toX, ref toY, ref toScale, ref toRotate, ref toProperties);

            var plan = new SpringPlan();
            PairProperties(fromProperties, toProperties, ref plan);
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

        /// <summary>
        /// Turns the two sides' property tables into channels. A property BOTH sides name becomes a channel; a
        /// property only one side names does not. Unlike the five fixed axes there is no identity value to
        /// substitute for the silent side — "no background color declared" is not the same statement as
        /// "transparent", and a length has no neutral magnitude at all — so a one-sided property falls back to
        /// the plain class swap, which lands it instantly. A length pair whose two sides carry DIFFERENT units
        /// falls back the same way: a percentage resolves against a laid-out parent this path cannot consult, so
        /// there is no common space to interpolate a px↔% pair in.
        /// A shorthand and one of its own longhands in the same delta drop BOTH — see
        /// <see cref="DropOverlappingProperties"/>.
        /// </summary>
        private static void PairProperties(Dictionary<ArbitraryProperty, ArbitraryStyle>? fromProperties,
            Dictionary<ArbitraryProperty, ArbitraryStyle>? toProperties, ref SpringPlan plan)
        {
            if (fromProperties == null || toProperties == null)
            {
                return;
            }
            HashSet<ArbitraryProperty>? paired = null;
            foreach (var (property, from) in fromProperties)
            {
                if (!toProperties.TryGetValue(property, out var to))
                {
                    continue;
                }
                // A length pair whose sides disagree on unit cannot animate, so it never counts as paired —
                // which is also what makes an overlapping partner fall out below instead of half-driving a slot.
                if (!MotionPropertyClassParser.IsColor(property) && from.Unit != to.Unit)
                {
                    continue;
                }
                (paired ??= new HashSet<ArbitraryProperty>()).Add(property);
            }
            if (paired == null)
            {
                return;
            }
            DropOverlappingProperties(paired, fromProperties, toProperties);

            foreach (var property in paired)
            {
                var from = fromProperties[property];
                var to = toProperties[property];
                if (MotionPropertyClassParser.IsColor(property))
                {
                    (plan.Colors ??= new List<ColorChannelPlan>())
                        .Add(new ColorChannelPlan(property, from.Color, to.Color));
                    continue;
                }
                (plan.Lengths ??= new List<LengthChannelPlan>())
                    .Add(new LengthChannelPlan(property, from.Value, to.Value, from.Unit));
            }
        }

        /// <summary>
        /// Removes from <paramref name="paired"/> every property sharing a style slot with another property the
        /// delta names — a shorthand meeting one of its own longhands (<c>p-8</c> beside <c>pt-2</c>).
        /// </summary>
        /// <remarks>
        /// Channels are keyed by property, so a shorthand and a longhand are otherwise independent, and both
        /// would drive the slot they share: one side naming only the longhand leaves the shorthand animating
        /// toward a value the cascade overrules at the end of every play, and both sides naming both leaves the
        /// in-flight winner decided by the order the channels are visited.
        /// <para>
        /// Ordering the writes cannot fix this, because the winner they would have to agree with is not
        /// derivable from the properties alone. For preset utilities it is stylesheet declaration order, which
        /// is NOT a function of how many slots a utility writes: <c>.size-*</c> is declared after
        /// <c>.w-*</c>/<c>.h-*</c>, so the two-slot shorthand wins width at rest while a one-slot longhand wins
        /// it everywhere else. For bracket-form tokens it is not declaration order at all but class-array
        /// position, since inline-resolved tokens apply in sequence and the last one holds the slot. And the
        /// four radius half-shorthands write two slots each AND overlap pairwise, so a slot-count key cannot
        /// even order them against each other. Reproducing all three rules means a hand-maintained declaration
        /// table that would itself drift against the stylesheets.
        /// </para>
        /// <para>
        /// The whole overlapping group is therefore dropped and lands with the class swap, which is always the
        /// value the cascade resolves. Only properties this parser RECOGNIZES take part: a longhand it cannot
        /// read a magnitude from (<c>rounded-tl-full</c> beside <c>rounded-3xl</c>) is invisible here, so the
        /// shorthand still drives the slot the longhand owns at rest — documented in the motion guide rather
        /// than guessed at.
        /// </para>
        /// </remarks>
        private static void DropOverlappingProperties(HashSet<ArbitraryProperty> paired,
            Dictionary<ArbitraryProperty, ArbitraryStyle> fromProperties,
            Dictionary<ArbitraryProperty, ArbitraryStyle> toProperties)
        {
            List<ArbitraryProperty>? overlapping = null;
            foreach (var property in paired)
            {
                if (OverlapsAnotherNamedProperty(property, fromProperties)
                    || OverlapsAnotherNamedProperty(property, toProperties))
                {
                    (overlapping ??= new List<ArbitraryProperty>()).Add(property);
                }
            }
            if (overlapping == null)
            {
                return;
            }
            foreach (var property in overlapping)
            {
                paired.Remove(property);
            }
        }

        private static bool OverlapsAnotherNamedProperty(ArbitraryProperty property,
            Dictionary<ArbitraryProperty, ArbitraryStyle> named)
        {
            foreach (var other in named.Keys)
            {
                if (MotionPropertyClassParser.WritesOverlappingSlots(property, other))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Scan(string[]? classes, ref float? opacity, ref float? translateX, ref float? translateY,
            ref float? scale, ref float? rotate, ref Dictionary<ArbitraryProperty, ArbitraryStyle>? properties)
        {
            if (classes == null)
            {
                return;
            }
            foreach (var cls in classes)
            {
                if (!TryParseAxisValue(cls, out var axis, out var value))
                {
                    // Later classes win, mirroring the CSS cascade: two utilities on the same property in one
                    // variant resolve to the last one, exactly as the class list itself would.
                    if (MotionPropertyClassParser.TryParse(cls, out var style))
                    {
                        (properties ??= new Dictionary<ArbitraryProperty, ArbitraryStyle>())[style.Property] = style;
                    }
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
