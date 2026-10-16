using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // One hard band of a ring or an outline: Width px wide, Offset px from the element's edge. UI Toolkit has no
    // CSS box-shadow or outline, so Velvet draws each band as a native rounded border on an overlay tracking the
    // element; RingOverlay owns where that overlay lives. Corner radius is NOT part of the spec: a band follows
    // the target's own rounded-* radius, like ShadowSpec.
    internal readonly struct RingBand
    {
        public readonly float Width;
        public readonly float Offset;
        // The band's colour where a class named one; unused where CurrentColor is set.
        public readonly Color Color;
        // No class named the colour, so the band takes the element's text colour, as Tailwind v4's
        // var(--tw-ring-color, currentcolor) and outline-color's initial value do.
        public readonly bool CurrentColor;

        public RingBand(float width, float offset, Color color, bool currentColor)
        {
            Width = width;
            Offset = offset;
            Color = color;
            CurrentColor = currentColor;
        }
    }

    // What the ring-* and outline-* utilities draw on one element. The ring and the outline are separate CSS
    // properties (box-shadow and outline), so each is its own band with its own width, colour and offset.
    internal readonly struct RingSpec
    {
        // Whether a ring utility declared --tw-ring-shadow. Ring.Offset is the ring-offset width, which Tailwind
        // adds to the ring's spread: the ring shadow covers Offset + Width under the offset fill.
        public readonly bool HasRing;
        public readonly RingBand Ring;
        // --tw-ring-offset-shadow, a Ring.Offset-wide spread in OffsetColor painted over the ring. The box-shadow
        // Tailwind writes for a shadow-* lists it as the ring's does, so a shadow-* draws it as well.
        public readonly bool HasOffsetFill;
        public readonly Color OffsetColor;
        // ring-inset: the ring and its offset fill are drawn inside the element edge rather than outset around it.
        public readonly bool Inset;
        // Width 0 is no outline.
        public readonly RingBand Outline;

        public RingSpec(bool hasRing, RingBand ring, bool hasOffsetFill, Color offsetColor, bool inset, RingBand outline)
        {
            HasRing = hasRing;
            Ring = ring;
            HasOffsetFill = hasOffsetFill;
            OffsetColor = offsetColor;
            Inset = inset;
            Outline = outline;
        }

        // The spread the ring's own box-shadow covers, and the offset fill's.
        public float RingSpread => HasRing ? Ring.Offset + Ring.Width : 0f;
        public float OffsetSpread => HasOffsetFill ? Ring.Offset : 0f;
    }

    // Parses Velvet's ring-* and outline-* utilities into a RingSpec. Same shape as
    // StyleShadowClass: a cheap prefix gate (HasRingClass) plus a cascade-correct extractor (TryExtract).
    // Unlike shadow (whole-spec last-wins), a ring is COMPOSITE — width, color, offset and inset are
    // independent slots set by separate utilities (ring-2 ring-red-500 ring-offset-2 ring-inset), so each
    // slot takes its own last-matching class. outline-none resolves width 0 (no outline), which can cancel an
    // earlier outline in the cascade.
    internal static class StyleRingClass
    {
        // --tw-ring-offset-color's initial value.
        private static readonly Color DefaultRingOffsetColor = Color.white;

        // Tailwind v4's --default-ring-width, which the bare `ring` takes.
        private const float DefaultRingWidth = 1f;
        // Tailwind v4's --default-outline-width, which the bare `outline` declares.
        private const float DefaultOutlineWidth = 1f;
        // outline-width's initial value, medium: the width of an outline whose style a class set and whose width
        // no class did.
        private const float MediumOutlineWidth = 3f;

        // ring-<n>, outline-<n>, ring-offset-<n> and outline-offset-<n> take any integer Tailwind writes as pixels: digits
        // with no leading zero but `0` itself, so `ring-3` is 3px and `ring-02` is no utility.
        private static bool TryParseInteger(string suffix, out float value)
        {
            value = 0f;
            if (suffix.Length == 0 || (suffix[0] == '0' && suffix.Length > 1))
            {
                return false;
            }
            foreach (var c in suffix)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            value = float.Parse(suffix, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        // True when cls belongs to either family this layer owns (recognized or not-yet-valid). Both the
        // array gate below and the variant-payload gate answer through this one predicate, so which tokens
        // count as "a ring utility" is defined in exactly one place — mirroring StyleShadowClass.IsShadowClass.
        public static bool IsRingClass(string cls)
            => !string.IsNullOrEmpty(cls)
                && (cls == "ring" || cls == "outline"
                    || cls.StartsWith("ring-", StringComparison.Ordinal)
                    || cls.StartsWith("outline-", StringComparison.Ordinal)
                    || cls.StartsWith(NegatedOutlineOffset, StringComparison.Ordinal));

        private const string NegatedOutlineOffset = "-outline-offset-";

        // Cheap early-out gate: true when ANY class is ring/outline or begins with ring-/outline-. Skips the
        // full parse on the ~99% of elements that carry no ring/outline class and no binding.
        public static bool HasRingClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsRingClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // The ring's slots and the outline's are kept apart, as Tailwind v4 writes each family's width, colour and
        // offset to its own property: a colour or an offset of one family never reaches the other, and a colour
        // alone draws nothing.
        private struct RingSlots
        {
            public bool RingDeclared;   // ring, ring-<n>, ring-[<length>]: the class that writes --tw-ring-shadow
            public bool RingWidthSet;
            public float RingWidth;
            public bool RingColorSet;
            public Color RingColor;
            public float RingOffset;
            public bool RingOffsetColorSet;
            public Color RingOffsetColor;
            public bool Inset;
            // A shadow-* other than a shadow colour, which writes a box-shadow carrying the ring-offset fill.
            public bool ShadowDeclared;

            public bool OutlineDeclared;   // a class that sets outline-style or outline-width
            public bool OutlineWidthSet;
            public float OutlineWidth;
            public bool OutlineBare;       // bare `outline`, which declares its 1px width
            public bool OutlineColorSet;
            public Color OutlineColor;
            public float OutlineOffset;
        }

        // Resolves the composite ring/outline spec from classNames. Returns false when nothing is drawn — no
        // ring/outline class, or every band resolved to width 0 (ring-0 alone, outline-none).
        public static bool TryExtract(string[] classNames, out RingSpec spec)
            => TryExtract(classNames, classNames?.Length ?? 0, out spec);

        // baseCount: how many leading entries are the element's own classes rather than variant tokens appended
        // after them (ReconcilerContext.ReconciledClassCount); InEmitOrder orders those as Tailwind emits them.
        public static bool TryExtract(string[] classNames, int baseCount, out RingSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            classNames = InEmitOrder(classNames, baseCount);
            var slots = default(RingSlots);
            var discarded = DiscardsBoxShadow(classNames);
            foreach (var cls in classNames)
            {
                if (discarded && cls != null && cls.StartsWith("ring", StringComparison.Ordinal))
                {
                    continue;
                }
                if (!discarded && cls != null && StyleShadowClass.DeclaresBoxShadow(cls))
                {
                    slots.ShadowDeclared = true;
                }
                Accumulate(cls!, ref slots);
            }

            var ring = new RingBand(
                slots.RingWidthSet ? slots.RingWidth : DefaultRingWidth,
                Mathf.Max(0f, slots.RingOffset),
                slots.RingColor,
                currentColor: !slots.RingColorSet);
            var outlineWidth = !slots.OutlineDeclared
                ? 0f
                : slots.OutlineWidthSet
                    ? slots.OutlineWidth
                    : (slots.OutlineBare ? DefaultOutlineWidth : MediumOutlineWidth);
            var outline = new RingBand(
                outlineWidth,
                slots.OutlineOffset,
                slots.OutlineColor,
                currentColor: !slots.OutlineColorSet);
            spec = new RingSpec(
                slots.RingDeclared,
                ring,
                hasOffsetFill: slots.RingDeclared || slots.ShadowDeclared,
                slots.RingOffsetColorSet ? slots.RingOffsetColor : DefaultRingOffsetColor,
                slots.Inset,
                outline);
            if (spec.RingSpread > 0f || spec.OffsetSpread > 0f || outline.Width > 0f)
            {
                return true;
            }
            spec = default;
            return false;
        }

        // The ring, outline and box-shadow utilities among the element's own classes, put in the order Tailwind
        // emits their rules, which is what decides between two of them writing one property whatever order the
        // classes were written in: `ring-4 ring-2` is 4px and `outline-none outline-2` draws no outline.
        internal static string[] InEmitOrder(string[] classNames, int baseCount)
            => StyleRuleOrder.WithMembersInEmitOrder(classNames, baseCount, s_emitOrdered);

        private static readonly Func<string, bool> s_emitOrdered =
            cls => IsRingClass(cls) || cls == "shadow" || cls.StartsWith("shadow-", StringComparison.Ordinal);

        // Whether the box-shadow Tailwind composes for these classes is one the browser discards, which takes the
        // ring and every shadow-* with it: a ring whose colour, width or offset colour the browser rejects, or a
        // shadow-[…] holding a bare non-zero number. classNames is in the order InEmitOrder gives, read last rule
        // winning, as Accumulate reads it.
        internal static bool DiscardsBoxShadow(string[]? classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            var declared = false;
            var ringShadow = false;
            var ringWidthRejected = false;
            var ringColourRejected = false;
            var offsetWidth = false;
            var offsetColourRejected = false;
            var shadowRejected = false;
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (cls == "ring")
                {
                    declared = ringShadow = true;
                    ringWidthRejected = false;
                }
                else if (TryMatchPrefix(cls, "ring-offset-", out var offset))
                {
                    ReadBoxShadowPart(offset!, "ring-offset", ref offsetWidth, ref offsetColourRejected, out _);
                }
                else if (cls != "ring-inset" && TryMatchPrefix(cls, "ring-", out var ring))
                {
                    var width = false;
                    ReadBoxShadowPart(ring!, "ring", ref width, ref ringColourRejected, out var widthRejected);
                    if (width)
                    {
                        declared = ringShadow = true;
                        ringWidthRejected = widthRejected;
                    }
                }
                else if (StyleShadowClass.DeclaresBoxShadow(cls))
                {
                    declared = true;
                    shadowRejected = StyleShadowClass.HoldsRejectedValue(cls);
                }
            }
            return declared
                && ((ringShadow && (ringWidthRejected || ringColourRejected)) || (offsetWidth && offsetColourRejected) || shadowRejected);
        }

        // One ring- or ring-offset- suffix as Tailwind types it: a width (rejected when the browser would reject
        // it) or a colour (rejected likewise); a palette name it does not know sets neither.
        private static void ReadBoxShadowPart(string suffix, string root, ref bool width, ref bool colourRejected,
            out bool widthRejected)
        {
            widthRejected = false;
            if (TryParseInteger(suffix, out _))
            {
                width = true;
                return;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                if (StyleColorValueParser.TryParseColorValue(suffix, 0, out _))
                {
                    colourRejected = false;
                }
                return;
            }
            if (!StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner))
            {
                return;
            }
            if (hint != null ? StyleColorValueParser.HintSelectsNonColour(root, hint) : TailwindDataTypes.IsLength(inner))
            {
                width = true;
                widthRejected = !StyleArbitraryValueResolver.TryParseValue(inner, out _, out _)
                    && !StyleLengthExpression.TryParse(inner, out _);
                return;
            }
            colourRejected = !StyleColorValueParser.TryParseColor(inner, out _);
        }

        // ring-offset-{N|[..]|color} is the offset width or the colour of the fill drawn across it; outline-offset-*
        // is the outline's own offset. False for a class of neither prefix.
        private static bool TryAccumulateOffset(string cls, ref RingSlots slots)
        {
            if (TryMatchPrefix(cls, "ring-offset-", out var ringOffSuffix))
            {
                if (TryParseWidthValue(ringOffSuffix, out var off))
                {
                    slots.RingOffset = off;
                }
                else if (TryParseColorValue("ring-offset", ringOffSuffix, out var offColor))
                {
                    slots.RingOffsetColorSet = true;
                    slots.RingOffsetColor = offColor;
                }
                return true;
            }
            var negated = TryMatchPrefix(cls, NegatedOutlineOffset, out var outlineOffSuffix);
            if (!negated && !TryMatchPrefix(cls, "outline-offset-", out outlineOffSuffix))
            {
                return false;
            }
            if (TryParseOutlineOffset(outlineOffSuffix!, out var outlineOff))
            {
                slots.OutlineOffset = negated ? -outlineOff : outlineOff;
            }
            return true;
        }

        // outline-offset takes a negative length, which draws the outline inside the element's edge: an integer, or
        // a bracketed pixel length of either sign.
        private static bool TryParseOutlineOffset(string suffix, out float offset)
        {
            if (TryParseInteger(suffix, out offset))
            {
                return true;
            }
            var written = StyleArbitraryValueResolver.WithoutTypeHint(suffix);
            offset = 0f;
            return written != null
                && StyleArbitraryValueResolver.TryStripBrackets(written, 0, out var inner)
                && StyleArbitraryValueResolver.TryParseValue(inner, out offset, out var unit)
                && unit == LengthUnit.Pixel;
        }

        private static void Accumulate(string cls, ref RingSlots slots)
        {
            if (string.IsNullOrEmpty(cls))
            {
                return;
            }

            // Inset modifier (ring only).
            if (cls == "ring-inset")
            {
                slots.Inset = true;
                return;
            }

            if (TryAccumulateOffset(cls, ref slots))
            {
                return;
            }

            if (cls == "ring")
            {
                slots.RingDeclared = true;
                return;
            }
            if (cls == "outline")
            {
                slots.OutlineDeclared = true;
                slots.OutlineBare = true;
                return;
            }

            var isRingFamily = TryMatchPrefix(cls, "ring-", out var ringSuffix);
            var isOutlineFamily = !isRingFamily && TryMatchPrefix(cls, "outline-", out ringSuffix);
            if (!isRingFamily && !isOutlineFamily)
            {
                return;
            }

            // outline-none cancels (width 0).
            if (isOutlineFamily && ringSuffix == "none")
            {
                slots.OutlineDeclared = true;
                slots.OutlineWidthSet = true;
                slots.OutlineWidth = 0f;
                return;
            }

            if (isOutlineFamily && TryParseOutlineWidth(ringSuffix!, ref slots))
            {
                return;
            }

            if (TryParseWidthValue(ringSuffix, out var w))
            {
                if (isRingFamily)
                {
                    slots.RingDeclared = true;
                    slots.RingWidthSet = true;
                    slots.RingWidth = w;
                }
                else
                {
                    slots.OutlineDeclared = true;
                    slots.OutlineWidthSet = true;
                    slots.OutlineWidth = w;
                }
                return;
            }

            // Otherwise a color token (ring-red-500 / outline-white / ring-[#abc]), which sets its own family's
            // colour and names no band: in Tailwind v4 it writes only the colour property.
            if (TryParseColorValue(isRingFamily ? "ring" : "outline", ringSuffix, out var c))
            {
                if (isRingFamily)
                {
                    slots.RingColorSet = true;
                    slots.RingColor = c;
                }
                else
                {
                    slots.OutlineColorSet = true;
                    slots.OutlineColor = c;
                }
            }
            // An unrecognized ring-*/outline-* suffix is ignored (not a known utility).
        }

        // An outline-[…] that Tailwind's type tests (or a length, number or percentage hint) take for a width, which
        // it writes as outline-width beside outline-style: solid. A width the browser drops leaves the outline at
        // the width the rest of the cascade gives it.
        private static bool TryParseOutlineWidth(string suffix, ref RingSlots slots)
        {
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                || !StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner)
                || !(hint != null
                    ? StyleColorValueParser.HintSelectsNonColour("outline", hint)
                    : TailwindDataTypes.IsLength(inner) || TailwindDataTypes.IsNumber(inner) || TailwindDataTypes.IsPercentage(inner)))
            {
                return false;
            }
            slots.OutlineDeclared = true;
            if (StyleArbitraryValueResolver.TryParsePixels(inner, out var width))
            {
                slots.OutlineWidthSet = true;
                slots.OutlineWidth = width;
            }
            return true;
        }

        private static bool TryMatchPrefix(string cls, string prefix, out string? suffix)
        {
            if (cls.StartsWith(prefix, StringComparison.Ordinal))
            {
                suffix = cls.Substring(prefix.Length);
                return true;
            }
            suffix = null;
            return false;
        }

        // A width is either an integer (TryParseInteger) or an arbitrary [Npx] that Tailwind reads as a length
        // rather than a colour (StyleArbitraryValueResolver.TryParseInferredArbitraryPixels).
        private static bool TryParseWidthValue(string? suffix, out float width)
        {
            width = 0f;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            return TryParseInteger(suffix, out width)
                || StyleArbitraryValueResolver.TryParseInferredArbitraryPixels(suffix.AsSpan(), out width);
        }

        // A color is a palette token (red-500 / white / black) or an arbitrary [<color>], whose type hint is the
        // colour branch's unless HintSelectsNonColour says otherwise for root, either one followed by an opacity
        // modifier (ring-red-500/50, ring-offset-[#fff]/50) where the bracket is not the whole suffix.
        private static bool TryParseColorValue(string root, string? suffix, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            if (StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                return StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner)
                    && !StyleColorValueParser.HintSelectsNonColour(root, hint)
                    && StyleColorValueParser.TryParseColor(inner, out color);
            }
            return StyleColorValueParser.TryParseColorValue(suffix, 0, out color);
        }

        // Resolves the top-left corner radius (px) the ring overlay follows, mirroring StyleShadowClass —
        // the band's outer radius is this plus the ring width (+ offset). Returns false for rounded-full /
        // arbitrary radii (resolved from resolvedStyle once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
            => StyleShadowClass.TryResolveCornerRadius(classNames, out radius);
    }
}
