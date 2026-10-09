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
    /// neither parser reads a magnitude from (a percentage-based translate like <c>translate-x-1/2</c>, a per-axis
    /// <c>scale-x-</c>, <c>rounded-full</c>, or anything outside the property parser's own documented scope) is
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
            public (float from, float to)? Rotate;
            public List<ColorChannelPlan>? Colors;
            public List<LengthChannelPlan>? Lengths;
            // The one translate axis the swap names, and where it lands, when it names one alone.
            public (SpringAxis Axis, float To)? LoneTranslate;

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
        /// Builds the spring plan for a from/to class-array swap — see <see cref="PairAxis"/> for the five fixed
        /// axes and <see cref="PairProperties"/> for the property channels. <paramref name="context"/>, when the
        /// element is attached to a panel, includes resting holders for the delta's slots and supplies the
        /// start where no explicit from-side value wins. Translate x/y compose onto one
        /// inline style, so naming one axis forces a channel for the other, which rests at <paramref
        /// name="restingTranslateX"/> / <paramref name="restingTranslateY"/> — the element's own inline translate,
        /// read by the caller before the swap lands.
        /// </summary>
        internal static SpringPlan Resolve(string[]? fromClasses, string[]? toClasses,
            float restingTranslateX = 0f, float restingTranslateY = 0f, MotionSlotContext? context = null)
        {
            var written = WrittenBy(fromClasses).Union(WrittenBy(toClasses));
            var from = Scan(fromClasses, context, written);
            var to = Scan(toClasses, context, written);

            var plan = new SpringPlan();
            PairProperties(in from, in to, context, ref plan);
            plan.Opacity = PairAxis(in from, in to, ArbitraryProperty.Opacity, 1f, context);
            PairTranslate(in from, in to, restingTranslateX, restingTranslateY, context, ref plan);
            plan.Scale = PairAxis(in from, in to, ArbitraryProperty.Scale, 1f, context);
            plan.Rotate = PairAxis(in from, in to, ArbitraryProperty.Rotate, 0f, context);
            return plan;
        }

        // `plan`, each channel `start` also holds starting from `start`'s value rather than its own from-side.
        internal static SpringPlan StartingFrom(SpringPlan plan, SpringPlan start)
        {
            plan.Opacity = StartingFrom(plan.Opacity, start.Opacity);
            plan.TranslateX = StartingFrom(plan.TranslateX, start.TranslateX);
            plan.TranslateY = StartingFrom(plan.TranslateY, start.TranslateY);
            plan.Scale = StartingFrom(plan.Scale, start.Scale);
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
            ArbitraryProperty axis, float identity, MotionSlotContext? context)
        {
            var fromValue = from.ValueOf(axis);
            var toValue = to.ValueOf(axis);
            if ((!fromValue.HasValue && !toValue.HasValue) || to.HeldUnread(axis))
            {
                return null;
            }
            if (fromValue.HasValue && (context == null || from.WasSwapped(axis)))
            {
                return (fromValue.Value, toValue ?? identity);
            }
            if (context != null)
            {
                return context.TryReadCurrent(axis, LengthUnit.Pixel, out var current)
                    ? (current.Value, toValue ?? identity)
                    : null;
            }
            return from.HeldUnread(axis) ? null : (identity, toValue ?? identity);
        }

        private readonly struct SideScan
        {
            public float? Opacity { get; init; }
            public float? TranslateX { get; init; }
            public float? TranslateY { get; init; }
            public float? Scale { get; init; }
            public float? Rotate { get; init; }
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

            public bool HeldUnread(ArbitraryProperty axis) => (UnreadAxes & (1 << s_slotIndex[(int)axis])) != 0;
        }

        // Translate x/y are independent springs but always compose onto ONE inline `translate` (UI Toolkit has
        // no separate translateX/translateY style), so once either axis is in scope the other gets a channel
        // too. An axis with no channel of its own — forced into the plan only because its sibling needed one —
        // pins at the element's own resting value, so a base translate-y-* class the swap never touches does not
        // get stomped to 0 for the swap's duration.
        private static void PairTranslate(in SideScan from, in SideScan to,
            float restingTranslateX, float restingTranslateY, MotionSlotContext? context, ref SpringPlan plan)
        {
            var x = PairAxis(in from, in to, ArbitraryProperty.TranslateX, 0f, context);
            var y = PairAxis(in from, in to, ArbitraryProperty.TranslateY, 0f, context);
            if (x == null && y == null)
            {
                return;
            }
            plan.TranslateX = x ?? (restingTranslateX, restingTranslateX);
            plan.TranslateY = y ?? (restingTranslateY, restingTranslateY);
            plan.LoneTranslate = x == null ? (SpringAxis.TranslateY, y!.Value.to)
                : y == null ? (SpringAxis.TranslateX, x.Value.to) : null;
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
                    && (context == null || fromSide.SwappedProperties.Overlaps(StyleArbitraryLonghands.Of(property))) && (isColor || from.Unit == to.Unit);
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
        private const long AxisScaleRank = 1L << 50;

        // The token holding one slot on one side: its precedence, and what it reads as when a magnitude can be
        // read from it at all. An unreadable holder still holds the slot, so a token it outranks is not used.
        private struct SlotClaim
        {
            public bool Claimed;
            public long Precedence;
            public bool Readable;
            public bool Swapped;
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
                for (var i = 0; i < classes.Length; i++)
                {
                    Claim(classes[i], i, claims, in importantClasses, swapped: true);
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
                Properties = properties,
                SwappedAxes = swappedAxes,
                SwappedProperties = swappedProperties,
                UnreadAxes = unreadAxes,
            };
        }

        // MotionSlotContextTests pins resting holders against swapped stylesheet and inline tokens.
        private static void ClaimResting(MotionSlotContext context, SlotClaim[] claims, in StyleLonghandSet importantClasses)
        {
            foreach (var cls in context.RestingClasses())
            {
                Claim(cls, 0, claims, in importantClasses);
            }
            for (var i = 0; i < claims.Length; i++)
            {
                if (context.HasImportantInlineOutsideSwap(s_slots[i]))
                {
                    claims[i] = new SlotClaim { Claimed = true, Precedence = long.MaxValue };
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
            }
        }

        private const long RestingInlineRank = InlineRank - 1;

        // The five axes lead s_slots.
        private const int AxisSlotCount = 5;

        private static float? AxisValue(SlotClaim[] claims, ArbitraryProperty axis)
        {
            var claim = claims[s_slotIndex[(int)axis]];
            return claim.Readable ? claim.Value.Value : null;
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
            in StyleLonghandSet importantClasses, bool swapped = false)
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
            var hasAxisValue = TryParseAxisValue(className, out var readAxis, out var axisValue);
            var property = default(ArbitraryStyle);
            var hasProperty = !hasAxisValue && MotionPropertyClassParser.TryParse(className, out property);
            for (var i = 0; i < claims.Length; i++)
            {
                var slot = s_slots[i];
                var slotLonghands = StyleArbitraryLonghands.Of(slot);
                var writes = token.TranslateAxis is { } sole ? slot == sole : slotLonghands.Overlaps(token.Written);
                if (!writes || (claims[i].Claimed && claims[i].Precedence > token.Precedence))
                {
                    continue;
                }
                claims[i] = new SlotClaim { Claimed = true, Precedence = token.Precedence, Swapped = swapped };
                // MUTANT_SURVIVES(equivalent, clause removed): deleting the slot comparison changes no admitted write.
                // Axis-readable opacity/scale/rotate tokens write one axis; translates set TranslateAxis.
                if (hasAxisValue && slot == AxisSlot(readAxis))
                {
                    claims[i].Readable = true;
                    claims[i].Value = new ArbitraryStyle(slot, axisValue, LengthUnit.Pixel);
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
        }

        private static bool TryRank(string className, int index, out RankedToken token)
        {
            var core = StyleArbitraryValueResolver.StripImportant(className, out var important);
            if (StyleArbitraryValueResolver.IsInlineResolved(core) && StyleArbitraryValueResolver.TryParse(core, out var inline))
            {
                var perAxisScale = inline.Property is ArbitraryProperty.ScaleX or ArbitraryProperty.ScaleY;
                token = new RankedToken
                {
                    Precedence = (perAxisScale ? AxisScaleRank : InlineRank) + (important ? ImportantRank : 0) + index,
                    Written = StyleArbitraryLonghands.Of(inline.Property),
                    TranslateAxis = inline.Property is ArbitraryProperty.TranslateX or ArbitraryProperty.TranslateY
                        ? inline.Property : null,
                    Important = important,
                    Inline = true,
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
