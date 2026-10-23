using System;
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
    /// Resolves class-defined targets into a spring or bezier plan. A supplied <see cref="MotionSlotContext"/>
    /// provides the element's current value when the from-side has no compatible value and identifies
    /// resting inline holders the swap leaves in place.
    /// </summary>
    /// <remarks>
    /// Scope: <see cref="SpringAxis.Opacity"/> and the transform trio (translate x/y in PIXELS, uniform scale,
    /// rotate degrees) are recognized here — matching the utilities <c>_effects.uss</c> / <c>_transforms.uss</c>
    /// define plus their arbitrary-value/spacing-scale equivalents in <see cref="StyleArbitraryValueResolver"/>.
    /// The color- and length-valued properties are recognized by <see cref="MotionPropertyClassParser"/> and
    /// carried in the same plan (see <see cref="SpringPlan.Colors"/> / <see cref="SpringPlan.Lengths"/>). A class
    /// neither parser reads a magnitude from (anything outside the property parser's own documented scope) is
    /// not animated, and where it is the token the cascade lets hold a slot, that slot is not animated from the
    /// other tokens on its side either — see <see cref="Claim"/>.
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
        /// unit the target carries; <see cref="MotionSlotContext"/> converts compatible current values into it.
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
        /// scope for this play). The five fixed axes have identity values to fall back on off-panel; the property
        /// channels need a compatible explicit or current start and an effective to-side target.
        /// </summary>
        internal struct SpringPlan
        {
            public (float from, float to)? Opacity;
            public (float from, float to)? TranslateX;
            public (float from, float to)? TranslateY;
            public (float from, float to)? Scale;
            // The y factor of a scale that differs per axis, Scale being its x factor; null for a uniform scale.
            public (float from, float to)? ScaleY;
            public (float from, float to)? Rotate;
            public List<ColorChannelPlan>? Colors;
            public List<LengthChannelPlan>? Lengths;
            // The one translate axis the swap names, and where it lands, when it names one alone.
            public (SpringAxis Axis, float To)? LoneTranslate;
            // The unit each translate channel's values are in. A channel's two ends share it.
            public LengthUnit TranslateXUnit;
            public LengthUnit TranslateYUnit;

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
        /// USS class at all (<c>translate-x-4</c>, <c>-rotate-6</c>, <c>opacity-[.5]</c>, …). A per-axis
        /// scale-x-/scale-y- is read per axis by <c>PairScaleAxes</c> instead.
        /// </summary>
        internal static bool TryParseAxisValue(string className, out SpringAxis axis, out float value)
            => TryParseAxisReading(className, out axis, out value, out var unit) && unit == LengthUnit.Pixel;

        // TryParseAxisValue, with the unit a translate is written in: the fractions and the full keyword read as
        // percentages of the element's own size.
        private static bool TryParseAxisReading(string className, out SpringAxis axis, out float value,
            out LengthUnit unit)
        {
            axis = default;
            value = 0f;
            unit = LengthUnit.Pixel;
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
            // Uniform scale-N, its bare value read as the resolver reads a scale preset's.
            if (core.StartsWith("scale-", System.StringComparison.Ordinal)
                && StyleArbitraryValueResolver.TryGetAxisScale(core.AsSpan("scale-".Length), out value))
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
                    case ArbitraryProperty.ScaleBoth:
                        axis = SpringAxis.Scale;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.Rotate:
                        axis = SpringAxis.Rotate;
                        value = arbitrary.Value;
                        return true;
                    case ArbitraryProperty.TranslateX when ReadsAsTranslate(arbitrary):
                        axis = SpringAxis.TranslateX;
                        value = arbitrary.Value;
                        unit = arbitrary.Unit;
                        return true;
                    case ArbitraryProperty.TranslateY when ReadsAsTranslate(arbitrary):
                        axis = SpringAxis.TranslateY;
                        value = arbitrary.Value;
                        unit = arbitrary.Unit;
                        return true;
                }
            }

            return false;
        }

        private static bool ReadsAsTranslate(in ArbitraryStyle arbitrary)
            => arbitrary.Unit == LengthUnit.Pixel || (arbitrary.Unit == LengthUnit.Percent && arbitrary.Expression == null);

        private static bool TryParseBothTranslateAxes(string className, out float value, out LengthUnit unit)
        {
            var core = StyleArbitraryValueResolver.StripImportant(className, out _);
            var parsed = StyleArbitraryValueResolver.TryParse(core, out var arbitrary)
                && arbitrary.Property == ArbitraryProperty.Translate && ReadsAsTranslate(arbitrary);
            value = parsed ? arbitrary.Value : 0f;
            unit = parsed ? arbitrary.Unit : LengthUnit.Pixel;
            return parsed;
        }

        /// <summary>
        /// Builds the spring plan for a from/to class-array swap — see <see cref="PairAxis"/> for the five fixed
        /// axes and <see cref="PairProperties"/> for the property channels. <paramref name="context"/>, when the
        /// element is attached to a panel, includes resting holders for the delta's slots and supplies the
        /// start where no explicit from-side value wins; one read off a panel includes only the element's own
        /// important holders. Translate x/y compose onto one
        /// inline style, so naming one axis forces a channel for the other, which rests at <paramref
        /// name="restingTranslateX"/> / <paramref name="restingTranslateY"/> — the element's own inline translate,
        /// read by the caller before the swap lands.
        /// </summary>
        internal static SpringPlan Resolve(string[]? fromClasses, string[]? toClasses,
            float restingTranslateX = 0f, float restingTranslateY = 0f, MotionSlotContext? context = null)
            => ResolveResting(fromClasses, toClasses,
                new Translate(new Length(restingTranslateX), new Length(restingTranslateY)), context);

        /// <summary>Resolve with the resting translate in the units the element holds it in.</summary>
        internal static SpringPlan ResolveResting(string[]? fromClasses, string[]? toClasses, Translate restingTranslate,
            MotionSlotContext? context = null)
        {
            var written = WrittenBy(fromClasses).Union(WrittenBy(toClasses));
            var from = Scan(fromClasses, context, written);
            var to = Scan(toClasses, context, written);

            var reading = context is { Attached: true } ? context : null;
            var plan = new SpringPlan();
            PairProperties(in from, in to, reading, ref plan);
            plan.Opacity = PairAxis(in from, in to, ArbitraryProperty.Opacity, 1f, reading, out _);
            PairTranslate(in from, in to, restingTranslate, reading, ref plan);
            plan.Scale = PairAxis(in from, in to, ArbitraryProperty.Scale, 1f, reading, out _);
            plan.Rotate = PairAxis(in from, in to, ArbitraryProperty.Rotate, 0f, reading, out _);
            PairScaleAxes(fromClasses, toClasses, context, reading, ref plan);
            return plan;
        }

        // `plan`, each channel `start` also holds starting from `start`'s value rather than its own from-side.
        internal static SpringPlan StartingFrom(SpringPlan plan, SpringPlan start)
        {
            plan.Opacity = StartingFrom(plan.Opacity, start.Opacity);
            if (plan.TranslateXUnit == start.TranslateXUnit) plan.TranslateX = StartingFrom(plan.TranslateX, start.TranslateX);
            if (plan.TranslateYUnit == start.TranslateYUnit) plan.TranslateY = StartingFrom(plan.TranslateY, start.TranslateY);
            plan.Scale = StartingFrom(plan.Scale, start.Scale);
            plan.ScaleY = StartingFrom(plan.ScaleY, start.ScaleY ?? start.Scale);
            plan.Rotate = StartingFrom(plan.Rotate, start.Rotate);
            StartingFrom(plan.Colors, start.Colors, static (p, s) => p.Property == s.Property,
                static (p, s) => new ColorChannelPlan(p.Property, s.From, p.To));
            StartingFrom(plan.Lengths, start.Lengths, static (p, s) => p.Property == s.Property && p.Unit == s.Unit,
                static (p, s) => new LengthChannelPlan(p.Property, s.From, p.To, p.Unit));
            return plan;
        }

        private static (float from, float to)? StartingFrom((float from, float to)? channel, (float from, float to)? start)
            => channel == null || start == null ? channel : (start.Value.from, channel.Value.to);

        private static void StartingFrom<T>(List<T>? plan, List<T>? start, Func<T, T, bool> matches,
            Func<T, T, T> startingFrom)
        {
            if (plan == null || start == null)
            {
                return;
            }
            for (var i = 0; i < plan.Count; i++)
            {
                var channel = plan[i];
                var held = start.FindIndex(s => matches(channel, s));
                if (held >= 0) plan[i] = startingFrom(channel, start[held]);
            }
        }

        // A `start` for StartingFrom: each channel a driver's play has, holding the value given for it, so a play
        // whose channels have all been released reads as empty.
        internal static SpringPlan Holding<TColor, TLength>((float? opacity, float? translateX, float? translateY,
                float? scale, float? rotate) axes, List<TColor>? colors, Converter<TColor, ColorChannelPlan> color,
            List<TLength>? lengths, Converter<TLength, LengthChannelPlan> length)
            => new()
            {
                Opacity = Holding(axes.opacity),
                TranslateX = Holding(axes.translateX),
                TranslateY = Holding(axes.translateY),
                Scale = Holding(axes.scale),
                Rotate = Holding(axes.rotate),
                Colors = colors is { Count: > 0 } ? colors.ConvertAll(color) : null,
                Lengths = lengths is { Count: > 0 } ? lengths.ConvertAll(length) : null,
            };

        private static (float from, float to)? Holding(float? value) => value is { } v ? (v, v) : null;

        private static (float from, float to)? PairAxis(in SideScan from, in SideScan to,
            ArbitraryProperty axis, float identity, MotionSlotContext? context, out LengthUnit unit)
        {
            unit = LengthUnit.Pixel;
            var fromValue = from.ValueOf(axis);
            var toValue = to.ValueOf(axis);
            if ((!fromValue.HasValue && !toValue.HasValue) || to.HeldUnread(axis))
            {
                return null;
            }
            if (fromValue.HasValue && HoldsExplicitStart(context, from.WasSwapped(axis), axis))
            {
                if (!SharedUnit(fromValue, from.UnitOf(axis), toValue, to.UnitOf(axis), out unit))
                {
                    // A translate of 20px and one of 50% share no space to move through without the element's size,
                    // so the axis lands with the swap.
                    return (toValue!.Value, toValue.Value);
                }
                return (fromValue.Value, toValue ?? identity);
            }
            unit = toValue.HasValue ? to.UnitOf(axis) : from.UnitOf(axis);
            if (context != null)
            {
                return context.TryReadCurrent(axis, unit, out var current)
                    ? (current.Value, toValue ?? identity)
                    : null;
            }
            return from.HeldUnread(axis) ? null : (identity, toValue ?? identity);
        }

        // The unit two ends of a channel move in: the one they share, or the other side's where one end is zero,
        // which is zero in any unit. False for two nonzero ends in different units, which leaves unit at the target's.
        private static bool SharedUnit(float? fromValue, LengthUnit fromUnit, float? toValue, LengthUnit toUnit,
            out LengthUnit unit)
        {
            unit = toValue.HasValue ? toUnit : fromValue.HasValue ? fromUnit : LengthUnit.Pixel;
            if (!fromValue.HasValue || !toValue.HasValue || fromUnit == toUnit)
            {
                return true;
            }
            if (toValue.Value == 0f)
            {
                unit = fromUnit;
                return true;
            }
            return fromValue.Value == 0f;
        }

        // A slot a running play or a native transition is moving starts from the value on screen; the swapped-out
        // class value is where that slot rests, not where it is.
        private static bool HoldsExplicitStart(MotionSlotContext? context, bool swapped, ArbitraryProperty slot)
            => context == null
                || (swapped && !context.DrivenByLivePlay(slot) && !context.NativeTransitionRunning(slot));

        private readonly struct SideScan
        {
            public float? Opacity { get; init; }
            public float? TranslateX { get; init; }
            public float? TranslateY { get; init; }
            public float? Scale { get; init; }
            public float? Rotate { get; init; }
            public LengthUnit TranslateXUnit { get; init; }
            public LengthUnit TranslateYUnit { get; init; }
            public Dictionary<ArbitraryProperty, ArbitraryStyle>? Properties { get; init; }
            public int SwappedAxes { get; init; }
            public StyleLonghandSet SwappedProperties { get; init; }

            public bool WasSwapped(ArbitraryProperty axis) => (SwappedAxes & (1 << s_slotIndex[(int)axis])) != 0;

            // One bit per axis, at the axis's position in s_slots, for an axis held by an unreadable token.
            public int UnreadAxes { get; init; }

            public float? ValueOf(ArbitraryProperty axis) => axis switch
            {
                ArbitraryProperty.Opacity => Opacity,
                ArbitraryProperty.TranslateX => TranslateX,
                ArbitraryProperty.TranslateY => TranslateY,
                ArbitraryProperty.Scale => Scale,
                ArbitraryProperty.Rotate => Rotate,
                _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null),
            };

            public LengthUnit UnitOf(ArbitraryProperty axis) => axis switch
            {
                ArbitraryProperty.TranslateX => TranslateXUnit,
                ArbitraryProperty.TranslateY => TranslateYUnit,
                _ => LengthUnit.Pixel,
            };

            public bool HeldUnread(ArbitraryProperty axis) => (UnreadAxes & (1 << s_slotIndex[(int)axis])) != 0;

            // A rejected value holding either translate axis, which leaves the element no translation at all.
            public bool RejectedTranslate { get; init; }
        }

        // Translate x/y are independent springs but always compose onto ONE inline `translate` (UI Toolkit has
        // no separate translateX/translateY style), so once either axis is in scope the other gets a channel
        // too. An axis with no channel of its own — forced into the plan only because its sibling needed one —
        // pins at the element's own resting value, so a base translate-y-* class the swap never touches does not
        // get stomped to 0 for the swap's duration.
        private static void PairTranslate(in SideScan from, in SideScan to, Translate resting,
            MotionSlotContext? context, ref SpringPlan plan)
        {
            // The resolver draws no translation where the to-side holds a rejected value on either axis, so neither
            // axis gets a channel and the swap lands at once. A rejected scale-x-/scale-y- needs no such rule: it
            // holds the one scale channel unread.
            if (to.RejectedTranslate)
            {
                return;
            }
            var x = PairAxis(in from, in to, ArbitraryProperty.TranslateX, 0f, context, out var xUnit);
            var y = PairAxis(in from, in to, ArbitraryProperty.TranslateY, 0f, context, out var yUnit);
            if (x == null && y == null)
            {
                return;
            }
            plan.TranslateX = x ?? (resting.x.value, resting.x.value);
            plan.TranslateY = y ?? (resting.y.value, resting.y.value);
            plan.TranslateXUnit = x != null ? xUnit : resting.x.unit;
            plan.TranslateYUnit = y != null ? yUnit : resting.y.unit;
            // Read off the swap's classes rather than off x and y, which a resting translate class also fills.
            var swapped = new SideScan { SwappedAxes = from.SwappedAxes | to.SwappedAxes };
            var (swapsX, swapsY) = (swapped.WasSwapped(ArbitraryProperty.TranslateX),
                swapped.WasSwapped(ArbitraryProperty.TranslateY));
            plan.LoneTranslate = swapsX == swapsY ? null
                : swapsX ? (SpringAxis.TranslateX, plan.TranslateX.Value.to)
                : (SpringAxis.TranslateY, plan.TranslateY.Value.to);
        }

        /// <summary>
        /// Turns the two sides' property tables into channels. A slot the to-side reads a value for becomes a
        /// channel, starting from the from-side's value in the same unit, else from what the element shows now in
        /// that unit. Unlike the five fixed axes there is no identity value to stand in where neither exists —
        /// "no background color declared" is not the same statement as "transparent" — so such a slot falls back
        /// to the plain class swap, which lands it instantly, as does a slot only the from-side reads.
        /// </summary>
        private static void PairProperties(in SideScan fromSide, in SideScan toSide, MotionSlotContext? context,
            ref SpringPlan plan)
        {
            var fromProperties = fromSide.Properties;
            var toProperties = toSide.Properties;
            if (toProperties == null)
            {
                return;
            }
            foreach (var (property, to) in toProperties)
            {
                var isColor = MotionPropertyClassParser.IsColor(property);
                ArbitraryStyle from = default;
                var hasFrom = fromProperties != null && fromProperties.TryGetValue(property, out from)
                    && HoldsExplicitStart(context, fromSide.SwappedProperties.Overlaps(StyleArbitraryLonghands.Of(property)), property)
                    && (isColor || from.Unit == to.Unit);
                if (!hasFrom && (context == null || !context.TryReadCurrent(property, to.Unit, out from)))
                {
                    continue;
                }
                if (isColor)
                {
                    (plan.Colors ??= new List<ColorChannelPlan>())
                        .Add(new ColorChannelPlan(property, from.Color, to.Color));
                    continue;
                }
                (plan.Lengths ??= new List<LengthChannelPlan>())
                    .Add(new LengthChannelPlan(property, from.Value, to.Value, to.Unit));
            }
        }

        #region Per-axis scale

        // What one side's scale tokens give each axis, as the resolver's ApplyCombinedScale draws them: an axis
        // layer (scale-x-*, scale-y-*, a negated or unlisted scale-<n>) takes its axis over the sheet's scale-<n>
        // class, which the other axis falls back on; a scale-[…] at or above every axis layer is drawn alone.
        private struct ScaleReading
        {
            public float X;
            public float Y;
            public bool Swapped;
            public bool PerAxis;
            public bool Rejected;
        }

        private struct ScaleHolder
        {
            public long Key;
            public float Value;
            public bool Swapped;
        }

        private static bool TryReadScale(string[]? swapClasses, MotionSlotContext? context, out ScaleReading reading)
        {
            reading = default;
            ScaleHolder x = new() { Key = -1 }, y = new() { Key = -1 }, both = new() { Key = -1 },
                sheet = new() { Key = -1 }, bracket = new() { Key = -1 };
            var perAxis = false;
            var rejected = false;
            var importantClasses = CollectImportantClasses(swapClasses ?? Array.Empty<string>(), context);

            // A holder of equal rank replaces the one before it, resting classes being collected first, as Claim does.
            void Collect(string cls, int index, bool swapped)
            {
                if (string.IsNullOrEmpty(cls) || !TryRank(cls, index, out var token)
                    || !token.Written.Contains(StyleLonghand.Scale)
                    || (!token.Important && IsSubset(token.Written, importantClasses)))
                {
                    return;
                }
                var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
                if (!token.Inline)
                {
                    if (TryParseAxisReading(cls, out var axis, out var factor, out _) && axis == SpringAxis.Scale
                        && token.Precedence >= sheet.Key)
                    {
                        sheet = new ScaleHolder { Key = token.Precedence, Value = factor, Swapped = swapped };
                    }
                    return;
                }
                if (!StyleArbitraryValueResolver.TryParseLayer(core, out var layer))
                {
                    return;
                }
                if (layer.Rejected)
                {
                    rejected = true;
                    return;
                }
                var held = new ScaleHolder
                {
                    Key = token.Precedence, Value = layer.Keyword == StyleKeyword.Undefined ? layer.Value : 1f,
                    Swapped = swapped,
                };
                switch (layer.Property)
                {
                    case ArbitraryProperty.ScaleX:
                        perAxis = true;
                        if (held.Key >= x.Key) x = held;
                        break;
                    case ArbitraryProperty.ScaleY:
                        perAxis = true;
                        if (held.Key >= y.Key) y = held;
                        break;
                    case ArbitraryProperty.ScaleBoth:
                        if (held.Key >= both.Key) both = held;
                        break;
                    case ArbitraryProperty.Scale:
                        if (held.Key >= bracket.Key) bracket = held;
                        break;
                }
            }

            if (context != null)
            {
                foreach (var cls in context.RestingClasses())
                {
                    if (context.Attached || cls.StartsWith("!", StringComparison.Ordinal))
                    {
                        Collect(cls, 0, swapped: false);
                    }
                }
            }
            if (swapClasses != null)
            {
                var places = StyleRuleOrder.OrdinalsOf(swapClasses);
                for (var i = 0; i < swapClasses.Length; i++)
                {
                    Collect(swapClasses[i], places[i], swapped: true);
                }
            }
            if (x.Key < 0 && y.Key < 0 && both.Key < 0 && sheet.Key < 0 && bracket.Key < 0 && !rejected)
            {
                return false;
            }

            ScaleHolder AxisHolder(ScaleHolder axis) => both.Key > axis.Key ? both : axis;
            var xLayer = AxisHolder(x);
            var yLayer = AxisHolder(y);
            reading = new ScaleReading { PerAxis = perAxis, Rejected = rejected };
            if (bracket.Key >= 0 && (xLayer.Key < 0 || bracket.Key >= xLayer.Key)
                && (yLayer.Key < 0 || bracket.Key >= yLayer.Key))
            {
                (reading.X, reading.Y, reading.Swapped) = (bracket.Value, bracket.Value, bracket.Swapped);
                return true;
            }
            var xHolder = xLayer.Key >= 0 ? xLayer : sheet;
            var yHolder = yLayer.Key >= 0 ? yLayer : sheet;
            reading.X = xHolder.Key >= 0 ? xHolder.Value : 1f;
            reading.Y = yHolder.Key >= 0 ? yHolder.Value : 1f;
            reading.Swapped = (xHolder.Key >= 0 && xHolder.Swapped) || (yHolder.Key >= 0 && yHolder.Swapped);
            return true;
        }

        // Replaces the uniform scale channel with an x and a y one where a side names an axis of its own. The sides
        // are read here rather than through the slot claims, which keep one winner per slot.
        private static void PairScaleAxes(string[]? fromClasses, string[]? toClasses, MotionSlotContext? context,
            MotionSlotContext? reading, ref SpringPlan plan)
        {
            var hasFrom = TryReadScale(fromClasses, context, out var from);
            var hasTo = TryReadScale(toClasses, context, out var to);
            if (!(from.PerAxis || to.PerAxis))
            {
                return;
            }
            plan.Scale = null;
            plan.ScaleY = null;
            if (from.Rejected || to.Rejected
                || (context != null && (context.HoldsGesture(ArbitraryProperty.Scale)
                    || context.HasImportantInlineOutsideSwap(ArbitraryProperty.Scale)
                    || context.HoldsRestingVariant(ArbitraryProperty.Scale))))
            {
                return;
            }
            var target = hasTo ? new Vector2(to.X, to.Y) : Vector2.one;
            Vector2 start;
            if (hasFrom && HoldsExplicitStart(reading, from.Swapped, ArbitraryProperty.Scale))
            {
                start = new Vector2(from.X, from.Y);
            }
            else if (reading != null)
            {
                if (!reading.TryReadScaleAxes(out start))
                {
                    return;
                }
            }
            else
            {
                start = Vector2.one;
            }
            plan.Scale = (start.x, target.x);
            if (start.x != start.y || target.x != target.y)
            {
                plan.ScaleY = (start.y, target.y);
            }
        }

        #endregion

        #region Which token holds each slot

        // The slots a side's tokens contend for: the five axes, and every drivable property no other drivable
        // property writes a strict part of. A shorthand is spread over the longhand slots it writes, so a
        // shorthand and its own longhand contend slot by slot rather than as two channels driving one slot.
        private static readonly ArbitraryProperty[] s_slots = BuildSlots();

        // The position of each property in s_slots, or -1 for a property that is not a slot.
        private static readonly int[] s_slotIndex = BuildSlotIndex();

        // The claims of the side being scanned, indexed like s_slots. Shared across calls, so Resolve must not run
        // on two threads at once; it scans one side at a time and copies what it needs out before the next scan
        // clears this.
        private static readonly SlotClaim[] s_claims = new SlotClaim[s_slots.Length];

        // MotionSlotCascadeTests pins the ordering between inline, stylesheet and per-axis scale holders.
        private const long InlineRank = 1L << 40;
        private const long ImportantRank = 1L << 45;
        // scale-[…] shares the per-axis band, so it and the axis utilities order by importance and then Tailwind's
        // order, as the resolver's TryDrawnScaleBracket decides which scale is drawn.
        private const long AxisScaleRank = 1L << 50;

        // The token holding one slot on one side: its precedence, and what it reads as when a magnitude can be
        // read from it at all. An unreadable holder still holds the slot, so a token it outranks is not used.
        private struct SlotClaim
        {
            public bool Claimed;
            public long Precedence;
            public bool Readable;
            public bool Swapped;
            public bool Rejected;
            public ArbitraryStyle Value;
        }

        private static ArbitraryProperty[] BuildSlots()
        {
            var slots = new List<ArbitraryProperty>
            {
                ArbitraryProperty.Opacity, ArbitraryProperty.TranslateX, ArbitraryProperty.TranslateY,
                ArbitraryProperty.Scale, ArbitraryProperty.Rotate,
            };
            var drivable = new List<ArbitraryProperty>();
            foreach (ArbitraryProperty property in Enum.GetValues(typeof(ArbitraryProperty)))
            {
                if (MotionPropertyClassParser.IsDrivable(property))
                {
                    drivable.Add(property);
                }
            }
            foreach (var property in drivable)
            {
                var written = StyleArbitraryLonghands.Of(property);
                var minimal = true;
                foreach (var other in drivable)
                {
                    var part = StyleArbitraryLonghands.Of(other);
                    // MUTANT_SURVIVES(equivalent, clause removed): removing part != written cannot admit an equal mask;
                    // MotionSlotCascadeTests pins distinct nonempty masks for the drivable properties.
                    if (other != property && part != written && IsSubset(part, written))
                    {
                        minimal = false;
                        break;
                    }
                }
                if (minimal)
                {
                    slots.Add(property);
                }
            }
            return slots.ToArray();
        }

        private static int[] BuildSlotIndex()
        {
            var index = new int[Enum.GetValues(typeof(ArbitraryProperty)).Length];
            Array.Fill(index, -1);
            for (var i = 0; i < s_slots.Length; i++)
            {
                index[(int)s_slots[i]] = i;
            }
            return index;
        }

        private static bool IsSubset(StyleLonghandSet part, StyleLonghandSet whole) => part.Union(whole) == whole;

        private static StyleLonghandSet WrittenBy(string[]? classes)
        {
            var written = StyleLonghandSet.Empty;
            if (classes == null) return written;
            for (var i = 0; i < classes.Length; i++)
            {
                if (!string.IsNullOrEmpty(classes[i]) && TryRank(classes[i], i, out var token))
                {
                    written = written.Union(token.Written);
                }
            }
            return written;
        }

        private static SideScan Scan(string[]? classes, MotionSlotContext? context, StyleLonghandSet written)
        {
            var claims = s_claims;
            Array.Clear(claims, 0, claims.Length);
            var importantClasses = CollectImportantClasses(classes ?? Array.Empty<string>(), context);
            if (context != null)
            {
                ClaimResting(context, claims, in importantClasses);
            }
            if (classes != null)
            {
                // The place in Tailwind's order, not the position written: two inline tokens of one property settle as
                // the mounted element settles them (StyleInlineSheetOrder).
                var places = StyleRuleOrder.OrdinalsOf(classes);
                for (var i = 0; i < classes.Length; i++)
                {
                    Claim(classes[i], places[i], claims, in importantClasses, swapped: true, context);
                }
            }

            var swappedAxes = 0;
            var swappedProperties = StyleLonghandSet.Empty;
            for (var i = 0; i < claims.Length; i++)
            {
                var longhands = StyleArbitraryLonghands.Of(s_slots[i]);
                if (!longhands.Overlaps(written)) claims[i] = default;
                else if (claims[i].Readable && claims[i].Swapped)
                {
                    if (i < AxisSlotCount) swappedAxes |= 1 << i;
                    else swappedProperties = swappedProperties.Union(longhands);
                }
            }
            Dictionary<ArbitraryProperty, ArbitraryStyle>? properties = null;
            for (var i = AxisSlotCount; i < claims.Length; i++)
            {
                if (claims[i].Readable)
                {
                    (properties ??= new Dictionary<ArbitraryProperty, ArbitraryStyle>())[s_slots[i]] = claims[i].Value;
                }
            }
            var rejectedTranslate = (claims[s_slotIndex[(int)ArbitraryProperty.TranslateX]].Rejected
                || claims[s_slotIndex[(int)ArbitraryProperty.TranslateY]].Rejected);
            var unreadAxes = 0;
            // MUTANT_SURVIVES(equivalent, boundary): <= adds only bit 5, while HeldUnread reads axis bits 0..4.
            // The nonempty drivable map has a minimal property after those axes, so claims[5] exists.
            for (var i = 0; i < AxisSlotCount; i++)
            {
                if (claims[i].Claimed && !claims[i].Readable)
                {
                    unreadAxes |= 1 << i;
                }
            }
            return new SideScan
            {
                Opacity = AxisValue(claims, ArbitraryProperty.Opacity),
                TranslateX = AxisValue(claims, ArbitraryProperty.TranslateX),
                TranslateY = AxisValue(claims, ArbitraryProperty.TranslateY),
                Scale = AxisValue(claims, ArbitraryProperty.Scale),
                Rotate = AxisValue(claims, ArbitraryProperty.Rotate),
                TranslateXUnit = AxisUnit(claims, ArbitraryProperty.TranslateX),
                TranslateYUnit = AxisUnit(claims, ArbitraryProperty.TranslateY),
                Properties = properties,
                SwappedAxes = swappedAxes,
                SwappedProperties = swappedProperties,
                UnreadAxes = unreadAxes,
                RejectedTranslate = rejectedTranslate,
            };
        }

        // MotionSlotContextTests pins resting holders against swapped stylesheet and inline tokens.
        private static void ClaimResting(MotionSlotContext context, SlotClaim[] claims, in StyleLonghandSet importantClasses)
        {
            foreach (var cls in context.RestingClasses())
            {
                if (context.Attached || cls.StartsWith("!", StringComparison.Ordinal))
                {
                    Claim(cls, 0, claims, in importantClasses, context: context);
                }
            }
            for (var i = 0; i < claims.Length; i++)
            {
                if (context.HasImportantInlineOutsideSwap(s_slots[i]) || context.HoldsGesture(s_slots[i]))
                {
                    claims[i] = new SlotClaim { Claimed = true, Precedence = long.MaxValue };
                    continue;
                }
                if (!context.Attached)
                {
                    continue;
                }
                // MUTANT_SURVIVES(equivalent, boundary): resting claims never equal this lower inline rank before the comparison.
                // Scan clears the claims and visits each resting slot once, with token index zero.
                if (claims[i].Precedence < RestingInlineRank // MUTANT_SURVIVES(equivalent, clause removed): the mask contains only stylesheet contributors, whose higher ranks win the final claim. Resting contributors precede this pass; side-array contributors overwrite it before Scan copies the claims.
                    && !IsSubset(StyleArbitraryLonghands.Of(s_slots[i]), importantClasses)
                    && context.HoldsInlineOutsideSwap(s_slots[i]))
                {
                    claims[i] = new SlotClaim { Claimed = true, Precedence = RestingInlineRank };
                }
                if (claims[i].Precedence < RestingAxisTranslateRank
                    && !IsSubset(StyleArbitraryLonghands.Of(s_slots[i]), importantClasses)
                    && context.HoldsRestingAxisTranslate(s_slots[i]))
                {
                    claims[i] = new SlotClaim { Claimed = true, Precedence = RestingAxisTranslateRank };
                }
                var variantRank = RestingVariantRank(s_slots[i]);
                if (claims[i].Precedence < variantRank
                    && !IsSubset(StyleArbitraryLonghands.Of(s_slots[i]), importantClasses)
                    && context.HoldsRestingVariant(s_slots[i]))
                {
                    claims[i] = new SlotClaim { Claimed = true, Precedence = variantRank };
                }
                ClaimRestingRejected(context, claims, i, variantRank);
            }
        }

        // A resting holder of a value the browser rejects, claimed at the rank the claims above give its kind of holder,
        // so a token of the swap that outranks it still takes the slot.
        private static void ClaimRestingRejected(MotionSlotContext context, SlotClaim[] claims, int i, long variantRank)
        {
            var slot = s_slots[i];
            long key;
            long rank;
            if (slot == ArbitraryProperty.TranslateX || slot == ArbitraryProperty.TranslateY)
            {
                if (!context.TryRestingTranslateHolder(slot, out var holder, out key, out var axisLayer) || !holder.Rejected)
                {
                    return;
                }
                rank = axisLayer ? RestingAxisTranslateRank : RestingInlineRank;
            }
            else if (slot == ArbitraryProperty.Scale)
            {
                if (!context.TryRestingRejectedScaleAxis(out key))
                {
                    return;
                }
                rank = RestingInlineRank;
            }
            else
            {
                return;
            }
            rank = StyleLayerPriority.UnderVariant(key) ? variantRank : rank;
            if (claims[i].Precedence <= rank)
            {
                claims[i] = new SlotClaim { Claimed = true, Precedence = rank, Rejected = true };
            }
        }

        private const long RestingInlineRank = InlineRank - 1;

        // A resting translate-x-* or translate-y-* holds its axis over a both-axis translate the swap writes at the
        // same importance, as the resolver's TryWinningAxis gives the axis a tie; it still yields to an important one.
        private const long RestingAxisTranslateRank = InlineRank + (1L << 30);

        // A resting holder under a variant outranks every plain token of the swap, which sit in the scale band or
        // below the inline band's spacing and place offsets by slot, and yields to an important one.
        private static long RestingVariantRank(ArbitraryProperty slot)
            => (slot == ArbitraryProperty.Scale ? AxisScaleRank : InlineRank) + (1L << 31);

        // The five axes lead s_slots.
        private const int AxisSlotCount = 5;

        private static float? AxisValue(SlotClaim[] claims, ArbitraryProperty axis)
        {
            var claim = claims[s_slotIndex[(int)axis]];
            return claim.Readable ? claim.Value.Value : null;
        }

        private static LengthUnit AxisUnit(SlotClaim[] claims, ArbitraryProperty axis)
        {
            var claim = claims[s_slotIndex[(int)axis]];
            return claim.Readable ? claim.Value.Unit : LengthUnit.Pixel;
        }

        private static StyleLonghandSet CollectImportantClasses(string[] classes, MotionSlotContext? context)
        {
            var written = StyleLonghandSet.Empty;
            for (var i = 0; i < classes.Length; i++)
            {
                if (string.IsNullOrEmpty(classes[i]) || !TryRank(classes[i], i, out var token)
                    || !token.Important || token.Inline)
                {
                    continue;
                }
                written = written.Union(token.Written);
            }
            if (context != null)
            {
                foreach (var cls in context.RestingClasses())
                {
                    if (TryRank(cls, 0, out var token) && token.Important && !token.Inline)
                    {
                        written = written.Union(token.Written);
                    }
                }
            }
            return written;
        }

        private static void Claim(string className, int index, SlotClaim[] claims,
            in StyleLonghandSet importantClasses, bool swapped = false, MotionSlotContext? context = null)
        {
            if (string.IsNullOrEmpty(className)
                || !TryRank(className, index, out var token))
            {
                return;
            }
            if (!token.Important && IsSubset(token.Written, importantClasses))
            {
                return;
            }
            var hasAxisValue = TryParseAxisReading(className, out var readAxis, out var axisValue, out var axisUnit);
            // TryRank leaves a both-axis translate's TranslateAxis unset, so the token writes exactly the two translate slots.
            var bothTranslateAxes = TryParseBothTranslateAxes(className, out var bothAxesValue, out var bothAxesUnit);
            var property = default(ArbitraryStyle);
            var hasProperty = !hasAxisValue && (MotionPropertyClassParser.TryParse(className, out property)
                || (context != null && MotionPropertyClassParser.TryParseThemeColor(className, context, out property)));
            for (var i = 0; i < claims.Length; i++)
            {
                var slot = s_slots[i];
                var slotLonghands = StyleArbitraryLonghands.Of(slot);
                var writes = token.TranslateAxis is { } sole ? slot == sole : slotLonghands.Overlaps(token.Written);
                if (!writes || (claims[i].Claimed && claims[i].Precedence > token.Precedence))
                {
                    continue;
                }
                claims[i] = new SlotClaim
                {
                    Claimed = true, Precedence = token.Precedence, Swapped = swapped, Rejected = token.Rejected,
                };
                // MUTANT_SURVIVES(equivalent, clause removed): deleting the slot comparison changes no admitted write.
                // Axis-readable opacity/scale/rotate tokens write one axis; per-axis translates set TranslateAxis.
                if ((hasAxisValue && slot == AxisSlot(readAxis)) || bothTranslateAxes)
                {
                    claims[i].Readable = true;
                    claims[i].Value = bothTranslateAxes
                        ? new ArbitraryStyle(slot, bothAxesValue, bothAxesUnit)
                        : new ArbitraryStyle(slot, axisValue, axisUnit);
                }
                else if (hasProperty && i >= AxisSlotCount
                    && IsSubset(slotLonghands, StyleArbitraryLonghands.Of(property.Property)))
                {
                    claims[i].Readable = true;
                    claims[i].Value = MotionPropertyClassParser.IsColor(slot)
                        ? new ArbitraryStyle(slot, property.Color)
                        : new ArbitraryStyle(slot, property.Value, property.Unit);
                }
            }
        }

        private readonly struct RankedToken
        {
            public long Precedence { get; init; }
            public StyleLonghandSet Written { get; init; }
            public ArbitraryProperty? TranslateAxis { get; init; }
            public bool Important { get; init; }
            public bool Inline { get; init; }
            public bool Rejected { get; init; }
        }

        // Two inline spacing utilities tie as the resolver orders them, shorthand before longhand, whichever is
        // written last (StyleSpacingPrecedence); a class list holds far fewer tokens than the shift leaves room for.
        private static long SpacingOrder(ArbitraryProperty property)
            => (StyleSpacingPrecedence.RankOf(property) + 1L) << 20;

        private static bool TryRank(string className, int index, out RankedToken token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(className, out var important);
            if (StyleArbitraryValueResolver.IsInlineResolved(core) && StyleArbitraryValueResolver.TryParseLayer(core, out var inline))
            {
                var declaresScale = inline.Property
                    is ArbitraryProperty.Scale or ArbitraryProperty.ScaleBoth or ArbitraryProperty.ScaleX
                        or ArbitraryProperty.ScaleY;
                token = new RankedToken
                {
                    Precedence = (declaresScale ? AxisScaleRank : InlineRank) + (important ? ImportantRank : 0)
                        + SpacingOrder(inline.Property) + index,
                    Written = StyleArbitraryLonghands.Of(inline.Property),
                    TranslateAxis = inline.Property is ArbitraryProperty.TranslateX or ArbitraryProperty.TranslateY
                        ? inline.Property : null,
                    Important = important,
                    Inline = true,
                    Rejected = inline.Rejected,
                };
                return true;
            }
            if (StyleUtilityProperties.TryGet(core, out var rule) && rule.Gate == StyleUtilityGate.None)
            {
                token = new RankedToken
                {
                    Precedence = rule.CascadePosition + (important ? ImportantRank : 0),
                    Written = rule.Properties,
                    Important = important,
                };
                return true;
            }
            token = default;
            return false;
        }

#pragma warning disable CS8524 // no discard arm: a new axis has to name its slot
        private static ArbitraryProperty AxisSlot(SpringAxis axis) => axis switch
        {
            SpringAxis.Opacity => ArbitraryProperty.Opacity,
            SpringAxis.TranslateX => ArbitraryProperty.TranslateX,
            SpringAxis.TranslateY => ArbitraryProperty.TranslateY,
            SpringAxis.Scale => ArbitraryProperty.Scale,
            SpringAxis.Rotate => ArbitraryProperty.Rotate,
        };
#pragma warning restore CS8524

        #endregion
    }
}
