using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A resolved ring / outline preset. UI Toolkit has no CSS box-shadow or outline, so Velvet draws the
    // outset hard border these utilities describe as a native rounded-border OVERLAY tracking the element
    // (hardware-rendered, follows rounded-* corners, no custom material / draw-order hazard — unlike the
    // soft, blurred box shadow which needs an SDF shader); RingOverlay owns where that overlay lives. Corner
    // radius is NOT part of the spec: the overlay follows the target's own rounded-* radius (plus the ring
    // width + offset), like ShadowSpec.
    //
    // ring and outline collapse onto ONE spec because they render identically — an outset band of Width at
    // Offset distance, in Color. Their only modelled differences are the default width (1px for both, medium
    // for an outline whose width the browser dropped) and that ring may be INSET (ring-inset draws the band
    // inside the element edge instead of outside).
    internal readonly struct RingSpec
    {
        public readonly float Width;
        // The band's colour where a class named one; unused where CurrentColor is set.
        public readonly Color Color;
        // No class named the colour, so the band takes the element's text colour, as Tailwind v4's
        // var(--tw-ring-color, currentcolor) and outline-color's initial value do.
        public readonly bool CurrentColor;
        // Gap (px) between the element edge and the band — ring-offset-* / outline-offset-*. The gap is
        // transparent (the element's own background / parent shows through), approximating ring-offset without
        // a dedicated offset-color fill.
        public readonly float Offset;
        // ring-inset: the band is drawn just INSIDE the element edge rather than outset around it.
        public readonly bool Inset;

        public RingSpec(float width, Color color, float offset, bool inset, bool currentColor = false)
        {
            Width = width;
            Color = color;
            Offset = offset;
            Inset = inset;
            CurrentColor = currentColor;
        }
    }

    // Parses Velvet's ring-* and outline-* utilities into a RingSpec. Same shape as
    // StyleShadowClass: a cheap prefix gate (HasRingClass) plus a cascade-correct extractor (TryExtract).
    // Unlike shadow (whole-spec last-wins), a ring is COMPOSITE — width, color, offset and inset are
    // independent slots set by separate utilities (ring-2 ring-red-500 ring-offset-2 ring-inset), so each
    // slot takes its own last-matching class. ring-0 / outline-none resolve width 0 (no ring), which can
    // cancel an earlier ring in the cascade.
    internal static class StyleRingClass
    {
        // Tailwind v4's --default-ring-width, which the bare `ring` takes.
        private const float DefaultRingWidth = 1f;
        // Tailwind v4's --default-outline-width, which the bare `outline` declares.
        private const float DefaultOutlineWidth = 1f;
        // outline-width's initial value, medium: the width of an outline whose style a class set and whose width
        // no class did.
        private const float MediumOutlineWidth = 3f;

        // suffix → width (px). Shared by ring-{N} and outline-{N}; the ring/outline width scale.
        private static readonly Dictionary<string, float> WidthScale = new()
        {
            ["0"] = 0f, ["1"] = 1f, ["2"] = 2f, ["4"] = 4f, ["8"] = 8f,
        };

        // True when cls belongs to either family this layer owns (recognized or not-yet-valid). Both the
        // array gate below and the variant-payload gate answer through this one predicate, so which tokens
        // count as "a ring utility" is defined in exactly one place — mirroring StyleShadowClass.IsShadowClass.
        public static bool IsRingClass(string cls)
            => !string.IsNullOrEmpty(cls)
                && (cls == "ring" || cls == "outline"
                    || cls.StartsWith("ring-", StringComparison.Ordinal)
                    || cls.StartsWith("outline-", StringComparison.Ordinal));

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

        private struct RingSlots
        {
            public bool HasIntent;   // a width-bearing class (bare ring or outline, a width) establishes a band
            public bool WidthSet;
            public float Width;
            public bool IsOutline;   // which family established intent, for the default width and the colour
            public bool OutlineDeclared; // bare `outline`, which declares its 1px width
            // Each family's colour is its own property (--tw-ring-color, outline-color), so a ring colour does
            // not colour an outline, and a colour alone draws nothing.
            public bool RingColorSet;
            public Color RingColor;
            public bool OutlineColorSet;
            public Color OutlineColor;
            public float Offset;
            public bool Inset;
        }

        // Resolves the composite ring/outline spec from classNames. Returns false when no ring is wanted —
        // no ring/outline class, or the resolved width is 0 (ring-0 / outline-none).
        public static bool TryExtract(string[] classNames, out RingSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var slots = default(RingSlots);
            var discarded = DiscardsBoxShadow(classNames);
            foreach (var cls in classNames)
            {
                if (discarded && cls != null && cls.StartsWith("ring", StringComparison.Ordinal))
                {
                    continue;
                }
                Accumulate(cls!, ref slots);
            }

            if (!slots.HasIntent)
            {
                return false;
            }

            var effectiveWidth = slots.WidthSet
                ? slots.Width
                : (slots.IsOutline ? (slots.OutlineDeclared ? DefaultOutlineWidth : MediumOutlineWidth) : DefaultRingWidth);
            if (effectiveWidth <= 0f)
            {
                return false; // ring-0 / outline-none: no ring
            }

            var colorSet = slots.IsOutline ? slots.OutlineColorSet : slots.RingColorSet;
            var color = slots.IsOutline ? slots.OutlineColor : slots.RingColor;
            spec = new RingSpec(effectiveWidth, color, Mathf.Max(0f, slots.Offset), slots.Inset, currentColor: !colorSet);
            return true;
        }

        // Whether the box-shadow Tailwind composes for these classes is one the browser discards, which takes the
        // ring and every shadow-* with it: a ring whose colour, width or offset colour the browser rejects, or a
        // shadow-[…] the browser rejects as a box-shadow (StyleShadowClass.HoldsRejectedValue). The ring colour and
        // width, and the shadow, are read last class winning, as Accumulate and StyleShadowClass.TryExtract read them.
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

        // One ring- or ring-offset- suffix as Tailwind types it: a width or a colour, each rejected only where the
        // browser rejects it, so a function or a unit Velvet does not read is not; a palette name it does not know
        // sets neither.
        private static void ReadBoxShadowPart(string suffix, string root, ref bool width, ref bool colourRejected,
            out bool widthRejected)
        {
            widthRejected = false;
            if (WidthScale.ContainsKey(suffix) || (root == "ring-offset" && int.TryParse(suffix, out var n) && n >= 0))
            {
                width = true;
                return;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                if (VelvetPalette.TryResolveColorToken(suffix, out _))
                {
                    colourRejected = false;
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
                width = true;
                widthRejected = value.IndexOf('(') < 0 && !StyleCompositeRejection.IsLength(value)
                    && !StyleArbitraryValueResolver.TryParseValue(inner, out _, out _);
                return;
            }
            colourRejected = StyleColorValueParser.RejectsAsColour(value);
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

            // Offset: ring-offset-{N} / outline-offset-{N}. (A color suffix here is the offset-color fill,
            // which Velvet does not model yet; an unrecognized offset suffix is ignored.)
            var isRingOffset = TryMatchPrefix(cls, "ring-offset-", out var ringOffSuffix);
            if (isRingOffset || TryMatchPrefix(cls, "outline-offset-", out ringOffSuffix))
            {
                if (TryParseWidthValue(ringOffSuffix, isRingOffset ? "ring-offset" : null, out var off))
                {
                    slots.Offset = off;
                }
                return;
            }

            // ring / ring-{N|[..]|color}.
            if (cls == "ring")
            {
                slots.HasIntent = true;
                slots.IsOutline = false;
                return;
            }
            if (cls == "outline")
            {
                slots.HasIntent = true;
                slots.IsOutline = true;
                slots.OutlineDeclared = true;
                return;
            }

            var isRingFamily = TryMatchPrefix(cls, "ring-", out var ringSuffix);
            var isOutlineFamily = !isRingFamily && TryMatchPrefix(cls, "outline-", out ringSuffix);
            if (!isRingFamily && !isOutlineFamily)
            {
                return;
            }

            // outline-none cancels (width 0).
            if (ringSuffix == "none")
            {
                slots.HasIntent = true;
                slots.IsOutline = isOutlineFamily;
                slots.WidthSet = true;
                slots.Width = 0f;
                return;
            }

            if (isOutlineFamily && TryParseOutlineWidth(ringSuffix!, ref slots))
            {
                return;
            }

            if (TryParseWidthValue(ringSuffix, isRingFamily ? "ring" : "outline", out var w))
            {
                slots.HasIntent = true;
                slots.IsOutline = isOutlineFamily;
                slots.WidthSet = true;
                slots.Width = w;
                return;
            }

            // Otherwise a color token (ring-red-500 / outline-white / ring-[#abc]), which sets its own family's
            // colour and names no band: in Tailwind v4 it writes only the colour property.
            if (TryParseColorValue(isRingFamily ? "ring" : "outline", ringSuffix, isOutlineFamily, out var c))
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
            slots.HasIntent = true;
            slots.IsOutline = true;
            if (StyleArbitraryValueResolver.TryParsePixels(inner, out var width))
            {
                slots.WidthSet = true;
                slots.Width = width;
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

        // A width is either a preset (0/1/2/4/8) or an arbitrary [Npx] (px only — a ring width is pixels).
        // inferredRoot: the prefix's root where Tailwind reads its bracket as a length or a colour
        // (StyleArbitraryValueResolver.TryParseInferredArbitraryPixels); null for outline-offset-, which takes a length alone.
        private static bool TryParseWidthValue(string? suffix, string? inferredRoot, out float width)
        {
            width = 0f;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            if (WidthScale.TryGetValue(suffix, out width))
            {
                return true;
            }
            if (inferredRoot != null)
            {
                return StyleArbitraryValueResolver.TryParseInferredArbitraryPixels(inferredRoot, suffix.AsSpan(), out width);
            }
            var written = StyleArbitraryValueResolver.WithoutTypeHint(suffix);
            return written != null && StyleArbitraryValueResolver.TryParseArbitraryPixels(written.AsSpan(), out width);
        }

        // A color is a palette token (red-500 / white / black) or an arbitrary [<color>], whose type hint is the
        // colour branch's unless HintSelectsNonColour says otherwise for root.
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
            return VelvetPalette.TryResolveColorToken(suffix, longhand, out color);
        }

        // Resolves the top-left corner radius (px) the ring overlay follows, mirroring StyleShadowClass —
        // the band's outer radius is this plus the ring width (+ offset). Returns false for rounded-full /
        // arbitrary radii (resolved from resolvedStyle once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
            => StyleShadowClass.TryResolveCornerRadius(classNames, out radius);
    }
}
