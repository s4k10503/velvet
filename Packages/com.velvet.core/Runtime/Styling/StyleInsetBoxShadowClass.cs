using System;
using System.Collections.Generic;
using UnityEngine;

namespace Velvet
{
    // inset-ring-* and inset-shadow-*, the two inset layers Tailwind v4 composes into an element's box-shadow above
    // its ring: a hard band inside the padding box (--tw-inset-ring-shadow) and one blurred inset shadow
    // (--tw-inset-shadow), each with its own colour property. StyleRingClass.TryExtract reads them beside the ring.
    internal static class StyleInsetBoxShadowClass
    {
        private const string RingPrefix = "inset-ring-";
        private const string ShadowPrefix = "inset-shadow-";

        // Tailwind v4's --inset-shadow-* theme, one layer each in rgb(0 0 0 / 0.05).
        private static readonly Dictionary<string, ShadowSpec> Presets = new()
        {
            ["2xs"] = new ShadowSpec(new Color(0f, 0f, 0f, 0.05f), blur: 0f, offsetY: 1f, spread: 0f),
            ["xs"] = new ShadowSpec(new Color(0f, 0f, 0f, 0.05f), blur: 1f, offsetY: 1f, spread: 0f),
            ["sm"] = new ShadowSpec(new Color(0f, 0f, 0f, 0.05f), blur: 4f, offsetY: 2f, spread: 0f),
        };

        internal struct Slots
        {
            public bool RingDeclared;
            public bool RingWidthSet;
            public float RingWidth;
            public bool RingColorSet;
            public Color RingColor;
            // An inset-shadow size, inset-shadow-none included: the class that writes --tw-inset-shadow.
            public bool ShadowDeclared;
            public bool WantShadow;
            public ShadowSpec Shadow;
            public float ShadowAlpha;
            public bool ShadowColorSet;
            public ShadowColorSource ShadowColorKind;
            public Color ShadowColor;
        }

        internal static bool IsInsetBoxShadowClass(string cls)
            => cls == "inset-ring" || cls.StartsWith(RingPrefix, StringComparison.Ordinal)
                || cls.StartsWith(ShadowPrefix, StringComparison.Ordinal);

        // Reads cls into slots when it is one of these two families, the last of each slot winning.
        internal static bool TryAccumulate(string cls, ref Slots slots)
        {
            if (cls == "inset-ring")
            {
                slots.RingDeclared = true;
                return true;
            }
            if (StyleRingClass.TryMatchPrefix(cls, RingPrefix, out var ringSuffix))
            {
                AccumulateRing(ringSuffix!, ref slots);
                return true;
            }
            if (!StyleRingClass.TryMatchPrefix(cls, ShadowPrefix, out var shadowSuffix))
            {
                return false;
            }
            if (StyleShadowClass.TryParseShadowColor(cls, ShadowPrefix, out var kind, out var color))
            {
                slots.ShadowColorSet = true;
                slots.ShadowColorKind = kind;
                slots.ShadowColor = color;
                return true;
            }
            AccumulateShadow(shadowSuffix!, ref slots);
            return true;
        }

        private static void AccumulateRing(string suffix, ref Slots slots)
        {
            if (StyleRingClass.TryParseWidthValue(suffix, out var width))
            {
                slots.RingDeclared = true;
                slots.RingWidthSet = true;
                slots.RingWidth = width;
            }
            else if (StyleRingClass.TryParseColorValue("inset-ring", suffix, out var color))
            {
                slots.RingColorSet = true;
                slots.RingColor = color;
            }
        }

        private static void AccumulateShadow(string suffix, ref Slots slots)
        {
            if (!StyleShadowClass.TrySplitSizeModifier(suffix, out var size, out var modifier))
            {
                return;
            }
            if (size == "none")
            {
                slots.ShadowDeclared = true;
                slots.WantShadow = false;
                return;
            }
            ShadowSpec spec;
            if (Presets.TryGetValue(size, out spec))
            {
                if (modifier is { } alpha)
                {
                    var tint = spec.Color;
                    tint.a = alpha;
                    spec = new ShadowSpec(tint, spec.Blur, spec.OffsetY, spec.Spread);
                }
            }
            else if (!StyleArbitraryValueResolver.TryStripBrackets(size, 0, out var inner)
                || !StyleShadowClass.TryParseArbitrary(inner.ToString(), modifier, out spec))
            {
                return;
            }
            slots.ShadowDeclared = true;
            slots.WantShadow = true;
            slots.Shadow = spec;
            slots.ShadowAlpha = modifier ?? 1f;
        }

        // The inset ring: Tailwind v4's inset-ring is 1px in var(--tw-inset-ring-color, currentcolor).
        internal static RingBand RingOf(in Slots slots)
            => slots.RingDeclared
                ? new RingBand(slots.RingWidthSet ? slots.RingWidth : 1f, 0f, slots.RingColor, currentColor: !slots.RingColorSet)
                : default;

        // The inset shadow in the colour an inset-shadow colour class names, scaled by the size's modifier as
        // --tw-inset-shadow-alpha scales it. shadow-inherit's ancestor lookup reads --tw-shadow-color, a property
        // this one is not, so inset-shadow-inherit keeps the size's own colour.
        internal static bool TryShadowOf(in Slots slots, out ShadowSpec shadow)
        {
            shadow = slots.Shadow;
            if (!slots.WantShadow)
            {
                return false;
            }
            if (slots.ShadowColorSet && slots.ShadowColorKind != ShadowColorSource.Inherit)
            {
                var color = slots.ShadowColor;
                color.a *= slots.ShadowAlpha;
                shadow = shadow.WithColor(slots.ShadowColorKind, color);
            }
            return true;
        }
    }
}
