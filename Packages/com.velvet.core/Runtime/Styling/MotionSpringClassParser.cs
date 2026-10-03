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
            var from = Scan(fromClasses);
            var to = Scan(toClasses);

            var plan = new SpringPlan();
            PairProperties(from.Properties, to.Properties, ref plan);
            if (Names(in from, in to, from.Opacity, to.Opacity, ArbitraryProperty.Opacity))
            {
                plan.Opacity = (from.Opacity ?? 1f, to.Opacity ?? 1f);
            }
            PairTranslate(in from, in to, restingTranslateX, restingTranslateY, ref plan);
            if (Names(in from, in to, from.Scale, to.Scale, ArbitraryProperty.Scale))
            {
                plan.Scale = (from.Scale ?? 1f, to.Scale ?? 1f);
            }
            if (Names(in from, in to, from.Rotate, to.Rotate, ArbitraryProperty.Rotate))
            {
                plan.Rotate = (from.Rotate ?? 0f, to.Rotate ?? 0f);
            }
            return plan;
        }

        // Whether an axis takes a channel: some side names it, and neither side's holder of it is a token no
        // magnitude can be read from, whose value the identity fallback would only stand in for.
        private static bool Names(in SideScan from, in SideScan to, float? fromValue, float? toValue,
            ArbitraryProperty axis)
            => (fromValue.HasValue || toValue.HasValue) && !from.HeldUnread(axis) && !to.HeldUnread(axis);

        // Resolve runs on every variant play, so this stays a struct passed by in: neither per-call scan may
        // reach the heap.
        private readonly struct SideScan
        {
            public float? Opacity { get; init; }
            public float? TranslateX { get; init; }
            public float? TranslateY { get; init; }
            public float? Scale { get; init; }
            public float? Rotate { get; init; }
            public Dictionary<ArbitraryProperty, ArbitraryStyle>? Properties { get; init; }

            // One bit per axis, at the axis's position in s_slots, for an axis held by an unreadable token.
            public int UnreadAxes { get; init; }

            public bool HeldUnread(ArbitraryProperty axis) => (UnreadAxes & (1 << s_slotIndex[(int)axis])) != 0;
        }

        // Translate x/y are independent springs but always compose onto ONE inline `translate` (UI Toolkit has
        // no separate translateX/translateY style), so once either axis is in scope the other gets a channel
        // too. An axis actually named by either side still falls back to identity on its own un-naming side
        // (the "declare only what changes" rule on Resolve); an axis named by NEITHER side — forced into the
        // plan only because its sibling needed one — pins at the element's own resting value instead, so a base
        // translate-y-* class the swap never touches does not get stomped to 0 for the swap's duration.
        private static void PairTranslate(in SideScan from, in SideScan to,
            float restingTranslateX, float restingTranslateY, ref SpringPlan plan)
        {
            var xNamed = Names(in from, in to, from.TranslateX, to.TranslateX, ArbitraryProperty.TranslateX);
            var yNamed = Names(in from, in to, from.TranslateY, to.TranslateY, ArbitraryProperty.TranslateY);
            if (!xNamed && !yNamed)
            {
                return;
            }
            plan.TranslateX = xNamed
                ? (from.TranslateX ?? 0f, to.TranslateX ?? 0f)
                : (restingTranslateX, restingTranslateX);
            plan.TranslateY = yNamed
                ? (from.TranslateY ?? 0f, to.TranslateY ?? 0f)
                : (restingTranslateY, restingTranslateY);
        }

        /// <summary>
        /// Turns the two sides' property tables into channels. A slot BOTH sides name becomes a channel; a slot
        /// only one side names does not. Unlike the five fixed axes there is no identity value to substitute for
        /// the silent side — "no background color declared" is not the same statement as "transparent", and a
        /// length has no neutral magnitude at all — so a one-sided slot falls back to the plain class swap, which
        /// lands it instantly. A length pair whose two sides carry DIFFERENT units falls back the same way: a
        /// percentage resolves against a laid-out parent this path cannot consult, so there is no common space to
        /// interpolate a px↔% pair in.
        /// </summary>
        private static void PairProperties(Dictionary<ArbitraryProperty, ArbitraryStyle>? fromProperties,
            Dictionary<ArbitraryProperty, ArbitraryStyle>? toProperties, ref SpringPlan plan)
        {
            if (fromProperties == null || toProperties == null)
            {
                return;
            }
            foreach (var (property, from) in fromProperties)
            {
                if (!toProperties.TryGetValue(property, out var to))
                {
                    continue;
                }
                if (MotionPropertyClassParser.IsColor(property))
                {
                    (plan.Colors ??= new List<ColorChannelPlan>())
                        .Add(new ColorChannelPlan(property, from.Color, to.Color));
                    continue;
                }
                if (from.Unit != to.Unit)
                {
                    continue;
                }
                (plan.Lengths ??= new List<LengthChannelPlan>())
                    .Add(new LengthChannelPlan(property, from.Value, to.Value, from.Unit));
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
            // MUTANT_SURVIVES(equivalent, line removed): deleting this fill changes only non-slot entries. Both readers
            // (HeldUnread and AxisValue) receive the five axes, whose entries the loop overwrites.
            Array.Fill(index, -1);
            for (var i = 0; i < s_slots.Length; i++)
            {
                index[(int)s_slots[i]] = i;
            }
            return index;
        }

        private static bool IsSubset(StyleLonghandSet part, StyleLonghandSet whole) => part.Union(whole) == whole;

        private static SideScan Scan(string[]? classes)
        {
            var claims = s_claims;
            Array.Clear(claims, 0, claims.Length);
            if (classes != null)
            {
                var importantClasses = CollectImportantClasses(classes);
                for (var i = 0; i < classes.Length; i++)
                {
                    Claim(classes[i], i, claims, in importantClasses);
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
                UnreadAxes = unreadAxes,
            };
        }

        // The five axes lead s_slots.
        private const int AxisSlotCount = 5;

        private static float? AxisValue(SlotClaim[] claims, ArbitraryProperty axis)
        {
            var claim = claims[s_slotIndex[(int)axis]];
            return claim.Readable ? claim.Value.Value : null;
        }

        private static StyleLonghandSet CollectImportantClasses(string[] classes)
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
            return written;
        }

        private static void Claim(string className, int index, SlotClaim[] claims,
            in StyleLonghandSet importantClasses)
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
                claims[i] = new SlotClaim { Claimed = true, Precedence = token.Precedence };
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
            // MUTANT_SURVIVES(equivalent, literal): true still returns an empty Written mask and no TranslateAxis.
            // Claim cannot write any slot from that empty mask.
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
