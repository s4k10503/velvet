using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // One hard band of a ring or an outline: Width px wide, Offset px from the element's edge. UI Toolkit has no CSS
    // box-shadow or outline, so Velvet paints each band itself as a rounded rect with a rounded hole
    // (RingBandShape); RingOverlay owns where. Corner radius is NOT part of the spec: a band follows the target's own
    // rounded-* radius, like ShadowSpec.
    internal readonly struct RingBand
    {
        public readonly float Width;
        public readonly float Offset;
        // A width or an offset only the element can measure, and whether the browser snaps the width.
        public readonly RingMeasure Measure;
        // The band's colour where a class named one; unused where CurrentColor or InheritColor is set.
        public readonly Color Color;
        // No class named the colour, so the band takes the element's text colour, as Tailwind v4's
        // var(--tw-ring-color, currentcolor) and outline-color's initial value do.
        public readonly bool CurrentColor;
        // `-inherit`: the parent's value of the family's colour property (BandColourInheritance).
        public readonly bool InheritColor;

        public RingBand(float width, float offset, Color color, bool currentColor, bool inheritColor = false,
            RingMeasure measure = default)
        {
            Width = width;
            Offset = offset;
            Measure = measure;
            Color = color;
            CurrentColor = currentColor;
            InheritColor = inheritColor;
        }

        // The band with its width and offset replaced by what RingOverlay.Measure read for them.
        public RingBand Resolved(float width, float offset)
            => new(width, offset, Color, CurrentColor, InheritColor);
    }

    // What a band's width and offset need the element for: a length in a font-relative or viewport unit
    // (outline-[2em], ring-offset-[1ch]), which stands in for Width or Offset, and the device-pixel snap an outline's
    // width takes (a ring is a box-shadow, which the browser does not snap).
    internal readonly struct RingMeasure
    {
        public readonly StyleLengthExpression? WidthExpression;
        public readonly StyleLengthExpression? OffsetExpression;
        public readonly bool Snaps;

        public RingMeasure(StyleLengthExpression? widthExpression, StyleLengthExpression? offsetExpression, bool snaps)
        {
            WidthExpression = widthExpression;
            OffsetExpression = offsetExpression;
            Snaps = snaps;
        }

        public bool Any => Snaps || WidthExpression != null || OffsetExpression != null;
    }

    // --tw-ring-offset-shadow: a Ring.Offset-wide spread in Color painted over the ring. Every box-shadow Tailwind
    // writes lists it, so a shadow-* or an inset-ring draws it as well.
    internal readonly struct RingOffsetFill
    {
        public readonly bool Declared;
        public readonly Color Color;
        // ring-offset-current and ring-offset-inherit; None where Color is the fill's colour.
        public readonly ColorReference Source;

        public RingOffsetFill(bool declared, Color color, ColorReference source)
        {
            Declared = declared;
            Color = color;
            Source = source;
        }
    }

    // What the ring-* and outline-* utilities draw on one element. The ring is Tailwind's box-shadow and the outline
    // is outline, which the browser draws together, so each is its own band with its own width, colour and offset.
    internal readonly struct RingSpec
    {
        // Whether a ring utility declared --tw-ring-shadow. Ring.Offset is the ring-offset width, which Tailwind
        // adds to the ring's spread: the ring shadow covers Offset + Width under the offset fill.
        public readonly bool HasRing;
        public readonly RingBand Ring;
        public readonly RingOffsetFill Fill;
        // ring-inset: the ring and its offset fill are drawn inside the element edge rather than outset around it.
        public readonly bool Inset;
        // Width 0 is no outline.
        public readonly RingBand Outline;

        public RingSpec(bool hasRing, RingBand ring, RingOffsetFill fill, bool inset, RingBand outline)
        {
            HasRing = hasRing;
            Ring = ring;
            Fill = fill;
            Inset = inset;
            Outline = outline;
        }

        public bool HasOffsetFill => Fill.Declared;

        public RingSpec Resolved(RingBand ring, RingBand outline) => new(HasRing, ring, Fill, Inset, outline);

        // Whether a width or an offset waits on the element's font or panel, so what is drawn is known only once measured
        // (RingOverlay.Measure).
        public bool IsMeasured => Ring.Measure.WidthExpression != null || Ring.Measure.OffsetExpression != null
            || Outline.Measure.WidthExpression != null;

        public Color OffsetColor => Fill.Color;

        public ColorReference OffsetSource => Fill.Source;

        // The spread the ring's own box-shadow covers, and the offset fill's.
        public float RingSpread => HasRing ? Ring.Offset + Ring.Width : 0f;

        public float OffsetSpread => HasOffsetFill ? Ring.Offset : 0f;

        // Whether the ring and its fill are drawn inside the element, over its background and under its children.
        public bool PaintsInside => Inset && (RingSpread > 0f || OffsetSpread > 0f);
    }

    // Parses Velvet's ring-* and outline-* utilities into a RingSpec. Same shape as
    // StyleShadowClass: a cheap prefix gate (HasRingClass) plus a cascade-correct extractor (TryExtract).
    // Unlike shadow (whole-spec last-wins), a ring is COMPOSITE — width, color, offset and inset are
    // independent slots set by separate utilities (ring-2 ring-red-500 ring-offset-2 ring-inset), so each
    // slot takes its own last-matching class. outline-none resolves width 0 (no outline), which can cancel an
    // earlier outline in the cascade.
    internal static class StyleRingClass
    {
        // Tailwind v4's --default-ring-width, which the bare `ring` takes.
        private const float DefaultRingWidth = 1f;
        // Tailwind v4's --default-outline-width, which the bare `outline` declares.
        private const float DefaultOutlineWidth = 1f;
        // outline-width's initial value, medium: the width of an outline whose style a class set and whose width
        // no class did.
        private const float MediumOutlineWidth = 3f;

        // True when cls belongs to either family this layer owns (recognized or not-yet-valid). Both the
        // array gate below and the variant-payload gate answer through this one predicate, so which tokens
        // count as "a ring utility" is defined in exactly one place — mirroring StyleShadowClass.IsShadowClass.
        public static bool IsRingClass(string cls)
            => !string.IsNullOrEmpty(cls)
                && (cls == "ring" || cls == "outline"
                    || cls.StartsWith("ring-", StringComparison.Ordinal)
                    || cls.StartsWith("outline-", StringComparison.Ordinal)
                    || cls.StartsWith("-outline-offset-", StringComparison.Ordinal));

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
            public StyleLengthExpression? RingWidthExpression;
            public StyleLengthExpression? RingOffsetExpression;
            public StyleLengthExpression? OutlineWidthExpression;
            public StyleLengthExpression? OutlineOffsetExpression;
            public bool RingColorSet;
            public Color RingColor;
            // current or inherit where a class named one in place of a colour; RingColorSet is then false.
            public ColorReference RingColorRef;
            public float RingOffset;
            public bool RingOffsetColorSet;
            public Color RingOffsetColor;
            public ColorReference RingOffsetColorRef;
            public bool Inset;

            public bool OutlineDeclared;   // a class that sets outline-style or outline-width
            public bool OutlineWidthSet;
            public float OutlineWidth;
            public bool OutlineBare;       // bare `outline`, which declares its 1px width
            public bool OutlineColorSet;
            public Color OutlineColor;
            public ColorReference OutlineColorRef;
            public float OutlineOffset;
        }

        // --tw-ring-offset-color's initial value.
        private static readonly Color DefaultRingOffsetColor = Color.white;

        // Resolves the composite ring/outline spec from classNames. Returns false when nothing is drawn — no
        // ring/outline class, or every band resolved to width 0 (ring-0 alone, outline-none).
        public static bool TryExtract(string[] classNames, out RingSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var slots = default(RingSlots);
            var boxShadow = ReadBoxShadow(classNames);
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls) || (boxShadow.Discards && cls.StartsWith("ring", StringComparison.Ordinal)))
                {
                    continue;
                }
                Accumulate(cls, ref slots);
            }

            var ring = new RingBand(
                slots.RingWidthSet ? slots.RingWidth : DefaultRingWidth,
                Mathf.Max(0f, slots.RingOffset),
                slots.RingColor,
                currentColor: !slots.RingColorSet && slots.RingColorRef != ColorReference.Inherit,
                inheritColor: !slots.RingColorSet && slots.RingColorRef == ColorReference.Inherit,
                measure: new RingMeasure(slots.RingWidthExpression, slots.RingOffsetExpression, snaps: false));
            var outlineWidth = !slots.OutlineDeclared
                ? 0f
                : slots.OutlineWidthSet
                    ? slots.OutlineWidth
                    : (slots.OutlineBare ? DefaultOutlineWidth : MediumOutlineWidth);
            var outline = new RingBand(
                outlineWidth,
                slots.OutlineOffset,
                slots.OutlineColor,
                currentColor: !slots.OutlineColorSet && slots.OutlineColorRef != ColorReference.Inherit,
                inheritColor: !slots.OutlineColorSet && slots.OutlineColorRef == ColorReference.Inherit,
                measure: new RingMeasure(slots.OutlineWidthExpression, slots.OutlineOffsetExpression, snaps: true));
            spec = new RingSpec(
                slots.RingDeclared,
                ring,
                new RingOffsetFill(
                    // Every box-shadow Tailwind writes lists var(--tw-ring-offset-shadow).
                    boxShadow.Declared && !boxShadow.Discards,
                    slots.RingOffsetColorSet ? slots.RingOffsetColor : DefaultRingOffsetColor,
                    slots.RingOffsetColorSet ? ColorReference.None : slots.RingOffsetColorRef),
                slots.Inset,
                outline);
            if (spec.RingSpread > 0f || spec.OffsetSpread > 0f || outline.Width > 0f || spec.IsMeasured)
            {
                return true;
            }
            spec = default;
            return false;
        }

        // Records element's own ring, ring-offset and outline colours for a descendant's -inherit
        // (BandColourInheritance).
        internal static void RecordOwnColours(VisualElement element, string[] classNames)
        {
            var slots = default(RingSlots);
            foreach (var cls in classNames)
            {
                Accumulate(cls, ref slots);
            }
            BandColourInheritance.Record(element,
                new(slots.RingColorSet, slots.RingColorRef, slots.RingColor),
                new(slots.RingOffsetColorSet, slots.RingOffsetColorRef, slots.RingOffsetColor),
                new(slots.OutlineColorSet, slots.OutlineColorRef, slots.OutlineColor));
        }

        // `current` and `inherit` in place of a colour.
        internal static bool TryParseColourReference(string? suffix, out ColorReference reference)
        {
            reference = suffix == "current" ? ColorReference.Current : suffix == "inherit" ? ColorReference.Inherit : ColorReference.None;
            return reference != ColorReference.None;
        }

        // Whether the box-shadow Tailwind composes for these classes is one the browser discards, which takes the
        // ring and every shadow-* with it: a ring or inset ring whose colour or width the browser rejects, a ring
        // offset colour it rejects beside an offset width, or a shadow-* or inset-shadow-* layer it rejects or whose
        // colour it rejects (StyleShadowClass.TryReadBoxShadowClass). Each value is the one of the class that holds
        // its property (StyleShadowClass.BoxShadowSlot), which the composed class source puts last.
        internal static bool DiscardsBoxShadow(string[]? classNames) => ReadBoxShadow(classNames).Discards;

        // What the box-shadow Tailwind composes for classNames holds: whether a class declares one, whose layers a ring
        // offset fill is listed in, and whether the browser discards it.
        internal readonly struct BoxShadowReading
        {
            public BoxShadowReading(bool declared, bool discards)
            {
                Declared = declared;
                Discards = discards;
            }

            public bool Declared { get; }

            public bool Discards { get; }
        }

        internal static BoxShadowReading ReadBoxShadow(string[]? classNames)
        {
            if (classNames == null)
            {
                return default;
            }
            var ring = default(BandSlots);
            var insetRing = default(BandSlots);
            var offset = default(BandSlots);
            var shadow = default(StyleShadowClass.BoxShadowSlot);
            var shadowColour = default(StyleShadowClass.BoxShadowSlot);
            var insetShadow = default(StyleShadowClass.BoxShadowSlot);
            var insetShadowColour = default(StyleShadowClass.BoxShadowSlot);
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                var core = StyleArbitraryValueResolver.StripImportant(cls, out var important);
                if (core == "ring")
                {
                    ring.Width.Offer(important, false);
                }
                else if (core == "inset-ring")
                {
                    insetRing.Width.Offer(important, false);
                }
                else if (TryMatchPrefix(core, "ring-offset-", out var suffix))
                {
                    ReadBoxShadowPart(suffix!, "ring-offset", important, ref offset);
                }
                else if (core != "ring-inset" && TryMatchPrefix(core, "ring-", out suffix))
                {
                    ReadBoxShadowPart(suffix!, "ring", important, ref ring);
                }
                else if (TryMatchPrefix(core, "inset-ring-", out suffix))
                {
                    ReadBoxShadowPart(suffix!, "inset-ring", important, ref insetRing);
                }
                else if (!StyleShadowClass.TryReadBoxShadowClass(core, "shadow", important, ref shadow, ref shadowColour))
                {
                    StyleShadowClass.TryReadBoxShadowClass(core, "inset-shadow", important, ref insetShadow,
                        ref insetShadowColour);
                }
            }
            var declared = ring.Width.Set || insetRing.Width.Set || shadow.Set || insetShadow.Set;
            return new BoxShadowReading(declared, declared && (ring.Discards || insetRing.Discards
                || (offset.Width.Set && offset.Colour.Rejected) || StyleShadowClass.Discards(shadow, shadowColour)
                || StyleShadowClass.Discards(insetShadow, insetShadowColour)));
        }

        // The width and colour slots of ring-*, inset-ring-* or ring-offset-*.
        private struct BandSlots
        {
            public StyleShadowClass.BoxShadowSlot Width;
            public StyleShadowClass.BoxShadowSlot Colour;

            public bool Discards => Width.Set && (Width.Rejected || Colour.Rejected);
        }

        // Offers the suffix of one ring-, inset-ring- or ring-offset- class to the width or colour slot Tailwind types
        // it into, rejected only where the browser rejects it, so a function or a unit Velvet does not read is
        // not; a palette name it does not know is offered to neither. A ring's width is written into calc() beside
        // the offset width, where a bare 0 or a percentage is rejected, and an inset ring's as it is, where a bare 0
        // is a length; RejectedValueTests pins both.
        private static void ReadBoxShadowPart(string suffix, string root, bool important, ref BandSlots slots)
        {
            if (IsPositiveInteger(suffix))
            {
                slots.Width.Offer(important, false);
                return;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                // A modifier outside 0..100% makes the colour's color-mix() invalid; a palette name Velvet does not know
                // is offered to neither slot.
                var verdict = StyleColorValueParser.VerdictOfColourValue(suffix, 0);
                if (verdict != StyleColorValueParser.ColourVerdict.None)
                {
                    slots.Colour.Offer(important, verdict == StyleColorValueParser.ColourVerdict.Rejected);
                }
                else if (suffix is "current" or "inherit")
                {
                    slots.Colour.Offer(important, false);
                }
                return;
            }
            if (!StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner))
            {
                return;
            }
            var value = inner.ToString();
            if (hint != null ? StyleColorValueParser.HintSelectsNonColour(root, hint) : TailwindDataTypes.IsLength(inner))
            {
                slots.Width.Offer(important, value.IndexOf('(') < 0 && !StyleCompositeRejection.IsLength(value)
                    && !(root == "inset-ring" && StyleCompositeRejection.IsZero(value)));
                return;
            }
            slots.Colour.Offer(important, StyleColorValueParser.RejectsAsColour(value));
        }

        // Tailwind's isPositiveInteger: String(Number(suffix)) === suffix, so digits without a leading zero, naming
        // an integer a double holds exactly and prints without an exponent.
        private static bool IsPositiveInteger(string suffix)
        {
            if (suffix.Length == 0 || (suffix.Length > 1 && suffix[0] == '0'))
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
            var integer = System.Numerics.BigInteger.Parse(suffix, System.Globalization.CultureInfo.InvariantCulture);
            var number = (double)integer;
            return number < 1e21 && new System.Numerics.BigInteger(number) == integer;
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

            var isRingFamily = TryMatchPrefix(cls, "ring-", out var suffix);
            var isOutlineFamily = !isRingFamily && TryMatchPrefix(cls, "outline-", out suffix);
            if (!isRingFamily && !isOutlineFamily)
            {
                return;
            }

            // outline-none cancels (width 0). ring-none is no Tailwind utility, so it cancels nothing.
            if (isOutlineFamily && suffix == "none")
            {
                slots.OutlineDeclared = true;
                slots.OutlineWidthSet = true;
                slots.OutlineWidth = 0f;
                slots.OutlineWidthExpression = null;
                return;
            }

            if (isOutlineFamily && TryParseOutlineWidth(suffix!, ref slots))
            {
                return;
            }

            if (TryParseWidthValue(suffix, isRingFamily ? "ring" : "outline", out var width))
            {
                if (isRingFamily)
                {
                    slots.RingDeclared = true;
                    slots.RingWidthSet = true;
                    slots.RingWidth = width;
                    slots.RingWidthExpression = null;
                }
                else
                {
                    slots.OutlineDeclared = true;
                    slots.OutlineWidthSet = true;
                    slots.OutlineWidth = width;
                    slots.OutlineWidthExpression = null;
                }
                return;
            }
            if (isRingFamily && TryParseMeasuredWidth(suffix, inferred: true, floored: true, out var measuredWidth))
            {
                slots.RingDeclared = true;
                slots.RingWidthSet = true;
                slots.RingWidth = 0f;
                slots.RingWidthExpression = measuredWidth;
                return;
            }

            AccumulateColour(suffix!, isRingFamily, ref slots);
        }

        // ring-offset-{N|[..]|color} is the offset width or the colour of the fill drawn across it; outline-offset-*
        // and its negation -outline-offset-* are the outline's own offset. False for a class of neither prefix.
        private static bool TryAccumulateOffset(string cls, ref RingSlots slots)
        {
            if (TryMatchPrefix(cls, "ring-offset-", out var ringOffSuffix))
            {
                if (TryParseWidthValue(ringOffSuffix, "ring-offset", out var off))
                {
                    slots.RingOffset = off;
                    slots.RingOffsetExpression = null;
                }
                else if (TryParseMeasuredWidth(ringOffSuffix, inferred: true, floored: true, out var measured))
                {
                    slots.RingOffset = 0f;
                    slots.RingOffsetExpression = measured;
                }
                else if (TryParseColourReference(ringOffSuffix, out var offReference))
                {
                    slots.RingOffsetColorSet = false;
                    slots.RingOffsetColorRef = offReference;
                }
                else if (TryParseColorValue("ring-offset", ringOffSuffix, false, out var offColor))
                {
                    slots.RingOffsetColorSet = true;
                    slots.RingOffsetColor = offColor;
                }
                return true;
            }
            var negative = TryMatchPrefix(cls, "-outline-offset-", out var outlineOffSuffix);
            if (!negative && !TryMatchPrefix(cls, "outline-offset-", out outlineOffSuffix))
            {
                return false;
            }
            if (TryParseOutlineOffset(outlineOffSuffix, out var outlineOff))
            {
                slots.OutlineOffset = negative ? -outlineOff : outlineOff;
                slots.OutlineOffsetExpression = null;
            }
            else if (TryParseMeasuredWidth(outlineOffSuffix, inferred: false, floored: false, out var measuredOffset))
            {
                slots.OutlineOffset = 0f;
                slots.OutlineOffsetExpression = negative ? measuredOffset!.Negated() : measuredOffset;
            }
            return true;
        }

        // A colour token (ring-red-500 / outline-white / ring-[#abc] / ring-current / ring-inherit) sets its own
        // family's colour and names no band: in Tailwind v4 it writes only the colour property. An unrecognized
        // ring-*/outline-* suffix is ignored (not a known utility).
        private static void AccumulateColour(string suffix, bool isRingFamily, ref RingSlots slots)
        {
            if (TryParseColourReference(suffix, out var reference))
            {
                if (isRingFamily)
                {
                    (slots.RingColorSet, slots.RingColorRef) = (false, reference);
                }
                else
                {
                    (slots.OutlineColorSet, slots.OutlineColorRef) = (false, reference);
                }
            }
            else if (TryParseColorValue(isRingFamily ? "ring" : "outline", suffix, !isRingFamily, out var color))
            {
                if (isRingFamily)
                {
                    (slots.RingColorSet, slots.RingColor, slots.RingColorRef) = (true, color, ColorReference.None);
                }
                else
                {
                    (slots.OutlineColorSet, slots.OutlineColor, slots.OutlineColorRef) = (true, color, ColorReference.None);
                }
            }
            else if (!isRingFamily && StyleColorValueParser.RejectedAtComputedTime(suffix, 0))
            {
                // outline-red-500/150: an outline-color the browser rejects once computed, which leaves it at its initial
                // currentcolor (outline-[#f00]/150 is dropped at parse time instead). A rejected ring colour discards the box-shadow instead (ReadBoxShadow).
                (slots.OutlineColorSet, slots.OutlineColorRef) = (false, ColorReference.Current);
            }
        }

        // An outline-[…] that Tailwind's type tests (or a length, number or percentage hint) take for a width, which
        // it writes as outline-width beside outline-style: solid. A width the browser drops, a negative literal among
        // them, leaves the outline at the width the rest of the cascade gives it; a math function coming below zero
        // is raised to zero instead.
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
                slots.OutlineWidthExpression = null;
            }
            else if (StyleLengthExpression.TryParse(inner, out var expression) && !expression!.ReadsPercentage
                && (StyleLengthExpression.OpensMathFunction(inner.Trim("_ ".AsSpan())) || inner.Trim("_ ".AsSpan())[0] != '-'))
            {
                slots.OutlineWidthSet = true;
                slots.OutlineWidth = 0f;
                slots.OutlineWidthExpression = expression.AtLeast(0f);
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

        // A width is a whole number of pixels written bare, as Tailwind's isPositiveInteger reads one, or an arbitrary
        // [Npx] (px only — a ring width is pixels).
        // inferredRoot: the prefix's root where Tailwind reads its bracket as a length or a colour
        // (StyleArbitraryValueResolver.TryParseInferredArbitraryPixels); null for outline-offset-, which takes a length alone.
        internal static bool TryParseWidthValue(string? suffix, string? inferredRoot, out float width)
        {
            width = 0f;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            if (IsPositiveInteger(suffix))
            {
                width = float.Parse(suffix, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            if (inferredRoot != null)
            {
                return StyleArbitraryValueResolver.TryParseInferredArbitraryPixels(inferredRoot, suffix.AsSpan(), out width);
            }
            var written = StyleArbitraryValueResolver.WithoutTypeHint(suffix);
            return written != null && StyleArbitraryValueResolver.TryParseArbitraryPixels(written.AsSpan(), out width);
        }

        // outline-offset-<n>, or outline-offset-[…] holding any pixel length, a negative one included, as CSS's
        // outline-offset takes one.
        private static bool TryParseOutlineOffset(string? suffix, out float offset)
        {
            if (TryParseWidthValue(suffix, null, out offset))
            {
                return true;
            }
            var written = suffix == null ? null : StyleArbitraryValueResolver.WithoutTypeHint(suffix);
            offset = 0f;
            if (written == null)
            {
                return false;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(written, 0, out var inner))
            {
                return false;
            }
            if (!StyleArbitraryValueResolver.TryParseValue(inner, out offset, out var unit))
            {
                return false;
            }
            return unit == LengthUnit.Pixel;
        }

        // A bracketed width or offset only the element can measure (a font-relative or viewport unit), read where
        // TryParseWidthValue reads a length: never a percentage, which neither takes. A width or a ring offset is
        // not below zero; an outline offset, which CSS lets be negative, is not floored.
        private static bool TryParseMeasuredWidth(string? suffix, bool inferred, bool floored, out StyleLengthExpression? expression)
        {
            expression = null;
            if (suffix == null || !StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                || !StyleArbitraryValueResolver.TrySplitTypeHint(inner, out var hint, out inner)
                || (inferred && !(hint != null ? hint == "length" : TailwindDataTypes.IsLength(inner)))
                || !StyleLengthExpression.TryParse(inner, out var parsed) || parsed!.ReadsPercentage)
            {
                return false;
            }
            expression = floored ? parsed.AtLeast(0f) : parsed;
            return true;
        }

        // A color is a palette token (red-500 / white / black) or an arbitrary [<color>], whose type hint is the
        // colour branch's unless HintSelectsNonColour says otherwise for root, either one followed by an opacity
        // modifier (ring-red-500/50, ring-offset-[#fff]/50) where the bracket is not the whole suffix.
        private static bool TryParseColorValue(string root, string? suffix, bool longhand, out Color color)
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
                    && StyleColorValueParser.TryParseColor(inner, longhand, out color);
            }
            return StyleColorValueParser.TryParseColorValue(suffix, 0, longhand, out color);
        }

        // Resolves the top-left corner radius (px) the ring overlay follows, mirroring StyleShadowClass —
        // the band's outer radius is this plus the ring width (+ offset). Returns false for rounded-full /
        // arbitrary radii (resolved from resolvedStyle once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
            => StyleShadowClass.TryResolveCornerRadius(classNames, out radius);
    }
}
