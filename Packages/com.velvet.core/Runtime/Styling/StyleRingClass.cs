using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A resolved ring / outline preset. UI Toolkit has no CSS box-shadow or outline, so Velvet draws the
    // outset hard border these utilities describe as a native rounded-border OVERLAY around the element
    // (hardware-rendered, follows rounded-* corners, no custom material / draw-order hazard — unlike the
    // soft, blurred DropShadow which needs an SDF shader). Corner radius is NOT part of the spec: the overlay
    // follows the target's own rounded-* radius (plus the ring width + offset), like ShadowSpec.
    //
    // ring and outline collapse onto ONE spec because they render identically — an outset band of Width at
    // Offset distance, in Color. Their only modelled differences are defaults (ring defaults to blue-500 at
    // 3px, the default ring; outline defaults to currentColor-ish at 1px) and that ring may be INSET
    // (ring-inset draws the band inside the element edge instead of outside).
    internal readonly struct RingSpec
    {
        public readonly float Width;
        public readonly Color Color;
        // Gap (px) between the element edge and the band — ring-offset-* / outline-offset-*. The gap is
        // transparent (the element's own background / parent shows through), approximating ring-offset without
        // a dedicated offset-color fill.
        public readonly float Offset;
        // ring-inset: the band is drawn just INSIDE the element edge rather than outset around it.
        public readonly bool Inset;

        public RingSpec(float width, Color color, float offset, bool inset)
        {
            Width = width;
            Color = color;
            Offset = offset;
            Inset = inset;
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
        // The DEFAULT ring is 3px blue-500; the bare `ring` and a width-only `ring-2` use it (color applied at
        // 0.5 alpha, see DefaultRingColor). An explicit `ring-<color>` is opaque and does not go through here.
        private const float DefaultRingWidth = 3f;
        // The default ring's alpha. A ring with no explicit color renders at blue-500 / 0.5, not full
        // opacity; an explicit ring color stays opaque, and the outline default (below) is unaffected.
        private const float DefaultRingAlpha = 0.5f;
        // Bare `outline` (outline-style: solid) — Velvet picks a thin default width; outline-{N} overrides.
        private const float DefaultOutlineWidth = 1f;

        private static bool s_blue500Resolved;
        private static Color s_blue500 = new(59f / 255f, 130f / 255f, 246f / 255f, 1f); // opaque fallback

        // The default band color, opaque, shared by the ring and outline defaults. The ring applies its own
        // 0.5 alpha on top (DefaultRingColor); bare `outline` keeps this opaque (a color-less outline is a
        // pre-existing currentColor approximation, out of scope, so its alpha must not change with the ring).
        private static Color DefaultBandColor()
        {
            if (!s_blue500Resolved)
            {
                if (VelvetPalette.TryResolveColorToken("blue-500", out var c))
                {
                    s_blue500 = c;
                }
                s_blue500Resolved = true;
            }
            return s_blue500;
        }

        // A color-less ring: blue-500 at 0.5 alpha.
        private static Color DefaultRingColor()
        {
            var c = DefaultBandColor();
            c.a = DefaultRingAlpha;
            return c;
        }

        // suffix → width (px). Shared by ring-{N} and outline-{N}; the ring/outline width scale.
        private static readonly Dictionary<string, float> WidthScale = new()
        {
            ["0"] = 0f, ["1"] = 1f, ["2"] = 2f, ["4"] = 4f, ["8"] = 8f,
        };

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
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                if (cls == "ring" || cls == "outline"
                    || cls.StartsWith("ring-", StringComparison.Ordinal)
                    || cls.StartsWith("outline-", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
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

            var hasIntent = false;        // any width/color/bare ring-or-outline class establishes a ring
            var widthSet = false;
            var width = 0f;
            var isOutline = false;        // tracks which family established intent, for the default width
            var colorSet = false;
            var color = default(Color);
            var offset = 0f;
            var inset = false;

            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                // Inset modifier (ring only).
                if (cls == "ring-inset")
                {
                    inset = true;
                    continue;
                }

                // Offset: ring-offset-{N} / outline-offset-{N}. (A color suffix here is the offset-color fill,
                // which Velvet does not model yet; an unrecognized offset suffix is ignored.)
                if (TryMatchPrefix(cls, "ring-offset-", out var ringOffSuffix)
                    || TryMatchPrefix(cls, "outline-offset-", out ringOffSuffix))
                {
                    if (TryParseWidthValue(ringOffSuffix, out var off))
                    {
                        offset = off;
                    }
                    continue;
                }

                // ring / ring-{N|[..]|color}.
                if (cls == "ring")
                {
                    hasIntent = true;
                    isOutline = false;
                    continue;
                }
                if (cls == "outline")
                {
                    hasIntent = true;
                    isOutline = true;
                    continue;
                }

                var isRingFamily = TryMatchPrefix(cls, "ring-", out var ringSuffix);
                var isOutlineFamily = !isRingFamily && TryMatchPrefix(cls, "outline-", out ringSuffix);
                if (!isRingFamily && !isOutlineFamily)
                {
                    continue;
                }

                // outline-none cancels (width 0).
                if (ringSuffix == "none")
                {
                    hasIntent = true;
                    isOutline = isOutlineFamily;
                    widthSet = true;
                    width = 0f;
                    continue;
                }

                if (TryParseWidthValue(ringSuffix, out var w))
                {
                    hasIntent = true;
                    isOutline = isOutlineFamily;
                    widthSet = true;
                    width = w;
                    continue;
                }

                // Otherwise a color token (ring-red-500 / outline-white / ring-[#abc]).
                if (TryParseColorValue(ringSuffix, out var c))
                {
                    hasIntent = true;
                    isOutline = isOutlineFamily;
                    colorSet = true;
                    color = c;
                    continue;
                }
                // An unrecognized ring-*/outline-* suffix is ignored (not a known utility).
            }

            if (!hasIntent)
            {
                return false;
            }

            var effectiveWidth = widthSet ? width : (isOutline ? DefaultOutlineWidth : DefaultRingWidth);
            if (effectiveWidth <= 0f)
            {
                return false; // ring-0 / outline-none: no ring
            }

            // A color-less ring uses the 0.5-alpha default; a color-less outline keeps the opaque band color.
            var effectiveColor = colorSet ? color : (isOutline ? DefaultBandColor() : DefaultRingColor());
            spec = new RingSpec(effectiveWidth, effectiveColor, Mathf.Max(0f, offset), inset);
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
        private static bool TryParseWidthValue(string? suffix, out float width)
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
            return StyleArbitraryValueResolver.TryParseArbitraryPixels(suffix.AsSpan(), out width);
        }

        // A color is a palette token (red-500 / white / black) or an arbitrary [#hex] / [rgb(...)].
        private static bool TryParseColorValue(string? suffix, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(suffix))
            {
                return false;
            }
            if (suffix.Length >= 2 && suffix[0] == '[' && suffix[suffix.Length - 1] == ']')
            {
                return StyleColorValueParser.TryParseColor(suffix.AsSpan(1, suffix.Length - 2), out color);
            }
            return VelvetPalette.TryResolveColorToken(suffix, out color);
        }

        // Resolves the top-left corner radius (px) the ring overlay follows, mirroring StyleShadowClass —
        // the band's outer radius is this plus the ring width (+ offset). Returns false for rounded-full /
        // arbitrary radii (resolved from resolvedStyle once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
            => StyleShadowClass.TryResolveCornerRadius(classNames, out radius);
    }

    // Reconciler-side bookkeeping for one ringed element, keyed in ReconcilerContext.RingBindings by the
    // INNER (real) element. Mirrors ShadowBinding: holds the structural wrapper ([inner, ring overlay], also
    // in WrapperToInnerMap), the absolutely-positioned native-border Overlay element that paints the ring,
    // the geometry callback on the inner (so the band tracks the inner's size + resolved corner radius), the
    // latest class list, and the resolved spec (so the geometry sync knows the width/offset/inset to lay out).
    internal sealed class RingBinding
    {
        public readonly VisualElement Wrapper;
        public readonly VisualElement Overlay;
        public EventCallback<GeometryChangedEvent> OnGeometry = null!;
        public string[] ClassNames = null!;
        public RingSpec Spec;

        public RingBinding(VisualElement wrapper, VisualElement overlay)
        {
            Wrapper = wrapper;
            Overlay = overlay;
        }
    }
}
