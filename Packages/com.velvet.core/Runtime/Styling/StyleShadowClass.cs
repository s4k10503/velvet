using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// One layer of a CSS box-shadow list. Corner radius is deliberately NOT part of the spec — it follows the
    /// target's own <c>rounded-*</c> radius (resolved at layout time, or from the class scale off-panel), so a
    /// card and a pill get the right shadow silhouette without restating the radius on the shadow.
    /// </summary>
    internal readonly struct ShadowSpec
    {
        public readonly Color Color;
        public readonly float Blur;
        public readonly float OffsetY;
        public readonly float Spread;
        // Horizontal offset (px). The presets are centered (0); only an arbitrary shadow-[x y …] sets it.
        public readonly float OffsetX;
        // An inner shadow (CSS's `inset`), drawn inside the padding box rather than outside the border box.
        public readonly bool Inset;
        // The family whose colour the layer takes from its parent (shadow-inherit, inset-shadow-inherit,
        // inset-ring-inherit); Color is what it is drawn in where the parent declares none.
        public readonly ShadowColourFamily InheritFrom;

        // The colour of a layer drawn in the element's text colour, currentcolor; its alpha scales that colour's.
        public static readonly Color CurrentColor = new(float.NaN, 0f, 0f, 1f);

        public ShadowSpec(Color color, float blur, float offsetY, float spread)
            : this(color, blur, offsetY, spread, 0f)
        {
        }

        public ShadowSpec(Color color, float blur, float offsetY, float spread, float offsetX, bool inset = false,
            ShadowColourFamily inheritFrom = ShadowColourFamily.None)
        {
            Color = color;
            Blur = blur;
            OffsetY = offsetY;
            Spread = spread;
            OffsetX = offsetX;
            Inset = inset;
            InheritFrom = inheritFrom;
        }
    }

    // The box-shadow families with a colour of their own: --tw-shadow-color, --tw-inset-shadow-color and
    // --tw-inset-ring-color.
    internal enum ShadowColourFamily : byte
    {
        None,
        Shadow,
        InsetShadow,
        InsetRing,
    }

    // The colour one family's classes declare on an element: inherit, a colour, or neither.
    internal readonly struct DeclaredShadowColour
    {
        public DeclaredShadowColour(bool inherit, Color? colour)
        {
            Inherit = inherit;
            Colour = colour;
        }

        public bool Inherit { get; }

        public Color? Colour { get; }

        public bool SameAs(DeclaredShadowColour other) => Inherit == other.Inherit && Colour == other.Colour;
    }

    // The colours an element's classes declare for each family, which a child's -inherit class takes.
    internal sealed class DeclaredShadowColours
    {
        public DeclaredShadowColours(DeclaredShadowColour shadow, DeclaredShadowColour insetShadow,
            DeclaredShadowColour insetRing)
        {
            Shadow = shadow;
            InsetShadow = insetShadow;
            InsetRing = insetRing;
        }

        public bool SameAs(DeclaredShadowColours other)
            => Shadow.SameAs(other.Shadow) && InsetShadow.SameAs(other.InsetShadow) && InsetRing.SameAs(other.InsetRing);

        public DeclaredShadowColour Shadow { get; }

        public DeclaredShadowColour InsetShadow { get; }

        public DeclaredShadowColour InsetRing { get; }

#pragma warning disable CS8524 // no discard arm: a new family has to say what it declares
        public DeclaredShadowColour Of(ShadowColourFamily family) => family switch
        {
            ShadowColourFamily.None => default,
            ShadowColourFamily.Shadow => Shadow,
            ShadowColourFamily.InsetShadow => InsetShadow,
            ShadowColourFamily.InsetRing => InsetRing,
        };
#pragma warning restore CS8524
    }

    // Parses Velvet's shadow-* utility classes into the layers of a box-shadow list, and resolves the companion
    // rounded-* corner radius the shadow follows. Same shape as StyleGapClass / StyleVariantClass: a cheap
    // prefix gate (HasShadowClass) plus a cascade-correct extractor (TryExtract, last matching class wins).
    // The presets are Tailwind v4's --shadow-* theme values. shadow-none is a recognized utility that resolves
    // to "no shadow" (so it can override an earlier shadow-lg in the cascade). Arbitrary values
    // (shadow-[x_y_blur_spread_color], underscores for spaces, commas between layers) are parsed per layer
    // positionally as CSS box-shadow lengths plus an optional color.
    internal static class StyleShadowClass
    {
        private static Color Black(float alpha) => new(0f, 0f, 0f, alpha);

        // The colour an arbitrary layer without one takes.
        private static readonly Color ArbitraryDefaultColor = new(4f / 255f, 12f / 255f, 24f / 255f, 0.20f);

        // suffix → layers, first on top, as theme.css declares --shadow-* (and --shadow for the bare `shadow`).
        private static readonly Dictionary<string, ShadowSpec[]> Presets = new()
        {
            ["2xs"] = new[] { new ShadowSpec(Black(0.05f), 0f, 1f, 0f) },
            ["xs"] = new[] { new ShadowSpec(Black(0.05f), 2f, 1f, 0f) },
            [""] = new[] { new ShadowSpec(Black(0.1f), 3f, 1f, 0f), new ShadowSpec(Black(0.1f), 2f, 1f, -1f) },
            ["sm"] = new[] { new ShadowSpec(Black(0.1f), 3f, 1f, 0f), new ShadowSpec(Black(0.1f), 2f, 1f, -1f) },
            ["md"] = new[] { new ShadowSpec(Black(0.1f), 6f, 4f, -1f), new ShadowSpec(Black(0.1f), 4f, 2f, -2f) },
            ["lg"] = new[] { new ShadowSpec(Black(0.1f), 15f, 10f, -3f), new ShadowSpec(Black(0.1f), 6f, 4f, -4f) },
            ["xl"] = new[] { new ShadowSpec(Black(0.1f), 25f, 20f, -5f), new ShadowSpec(Black(0.1f), 10f, 8f, -6f) },
            ["2xl"] = new[] { new ShadowSpec(Black(0.25f), 50f, 25f, -12f) },
            ["inner"] = new[] { new ShadowSpec(Black(0.05f), 4f, 2f, 0f, 0f, inset: true) },
        };

        // suffix → layers of inset-shadow-*, Tailwind v4's --inset-shadow-* theme values. There is no bare inset-shadow.
        private static readonly Dictionary<string, ShadowSpec[]> InsetPresets = new()
        {
            ["2xs"] = new[] { new ShadowSpec(Black(0.05f), 0f, 1f, 0f, 0f, inset: true) },
            ["xs"] = new[] { new ShadowSpec(Black(0.05f), 1f, 1f, 0f, 0f, inset: true) },
            ["sm"] = new[] { new ShadowSpec(Black(0.05f), 4f, 2f, 0f, 0f, inset: true) },
        };

        // Mirrors --radius-* in _tokens.uss. rounded-full / arbitrary radii are left to the
        // geometry-driven resolvedStyle path (TryResolveCornerRadius returns false for them).
        private static readonly Dictionary<string, float> RadiusScale = new()
        {
            ["none"] = 0f,
            [""] = 4f,      // bare `rounded` == --radius-default
            ["xs"] = 2f,
            ["sm"] = 4f,
            ["md"] = 6f,
            ["lg"] = 8f,
            ["xl"] = 12f,
            ["2xl"] = 16f,
            ["3xl"] = 24f,
            ["4xl"] = 32f,
        };

        // Single source for the --radius-* scale, keyed by the suffix after a rounded-* prefix (the empty
        // string being bare `rounded`) — shared so a per-class radius recognizer (MotionPropertyClassParser)
        // resolves the SAME magnitudes instead of holding a second copy that could drift, mirroring
        // StyleArbitraryValueResolver.TryGetSpacingPx's precedent. `full` is not on this scale: shadows
        // resolve it from the geometry, and MotionPropertyClassParser.FullRadiusPx carries it for a drive.
        internal static bool TryGetRadiusPx(string suffix, out float px) => RadiusScale.TryGetValue(suffix, out px);

        // True when cls belongs to the shadow family this layer owns (recognized or not-yet-valid).
        // Both the array gate below and the variant-payload gate answer through this one predicate, so
        // which tokens count as "a shadow utility" is defined in exactly one place. drop-shadow-* is a
        // filter (StyleFilterValueParser.TryParseDropShadow), composed apart from this paint as CSS composes
        // filter apart from box-shadow.
        public static bool IsShadowClass(string cls)
        {
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            return !string.IsNullOrEmpty(core) && (core == "shadow" || core.StartsWith("shadow-", StringComparison.Ordinal));
        }

        // inset-shadow-* and inset-ring-* write layers of the same box-shadow, so they are drawn by this paint.
        private static bool IsInsetLayerClass(string cls)
        {
            var core = StyleArbitraryValueResolver.StripImportant(cls, out _);
            return !string.IsNullOrEmpty(core) && (core == "inset-ring" || core.StartsWith("inset-shadow-", StringComparison.Ordinal)
                || core.StartsWith("inset-ring-", StringComparison.Ordinal));
        }

        // Cheap early-out gate: true when ANY class is shadow or inset-ring, or begins with shadow-, inset-shadow- or
        // inset-ring-. Used to skip the full parse on the ~99% of elements that carry no shadow class and no binding.
        public static bool HasShadowClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (IsShadowClass(cls) || IsInsetLayerClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // What one of shadow-* and inset-shadow-* holds: its size's layers as written, the size's /N, which replaces
        // the alpha of each layer's colour, and the family's colour class, which takes the place of each layer's
        // colour, its own /N folded in. Each is the last class of the family writing it.
        private struct ShadowFamily
        {
            public ShadowSpec[] Layers;
            // The size's /N as its own colour takes it, clamped to 0..1 as oklab(from <colour> l a b / N) clamps it.
            public float? SizeAlpha;
            // The size's /N as --tw-shadow-alpha scales a colour class beside it (TrySizeColourScale).
            public float SizeScale;
            public Color? Colour;
            // -inherit, which takes the parent's colour for this family and keeps each layer's own where it has none.
            public bool Inherit;
            // Whether an important class holds the layers (with their /N), or the colour.
            public bool LayersImportant;
            public bool ColourImportant;

            public ShadowSpec[] Resolved(ShadowColourFamily family)
            {
                if (Layers == null || Layers.Length == 0 || (SizeAlpha == null && Colour == null && !Inherit))
                {
                    return Layers ?? Array.Empty<ShadowSpec>();
                }
                var resolved = new ShadowSpec[Layers.Length];
                for (var i = 0; i < Layers.Length; i++)
                {
                    var layer = Layers[i];
                    var colour = Colour ?? layer.Color;
                    colour.a = Colour != null ? colour.a * SizeScale : SizeAlpha ?? colour.a;
                    resolved[i] = new ShadowSpec(colour, layer.Blur, layer.OffsetY, layer.Spread, layer.OffsetX, layer.Inset,
                        Inherit ? family : ShadowColourFamily.None);
                }
                return resolved;
            }

            // The colour a child's -inherit takes: this one, its alpha scaled by the size's /N, as the browser
            // substitutes --tw-shadow-alpha on this element.
            public DeclaredShadowColour Declared()
            {
                if (Inherit || Colour == null)
                {
                    return new DeclaredShadowColour(Inherit, null);
                }
                var colour = Colour.Value;
                colour.a *= SizeScale;
                return new DeclaredShadowColour(false, colour);
            }
        }

        // What inset-ring-* holds: the band's width, and its colour, the element's text colour where none is set.
        private struct InsetRing
        {
            public float Width;
            public Color? Colour;
            public bool Inherit;
            public bool WidthImportant;
            public bool ColourImportant;

            public ShadowSpec[] Resolved()
                => Width > 0f
                    ? new[]
                    {
                        new ShadowSpec(Colour ?? ShadowSpec.CurrentColor, 0f, 0f, Width, 0f, inset: true,
                            Inherit ? ShadowColourFamily.InsetRing : ShadowColourFamily.None),
                    }
                    : Array.Empty<ShadowSpec>();
        }

        // Returns the layers Tailwind lists in one box-shadow, first on top: the inset-shadow-* layers, the inset-ring-*
        // band, then the shadow-* layers, each family resolved from its last classes (CSS cascade: later classes win).
        // Returns false when that leaves no layer.
        public static bool TryExtract(string[] classNames, out ShadowSpec[] spec)
        {
            spec = Array.Empty<ShadowSpec>();
            // A box-shadow the browser discards takes every shadow-*, inset-shadow-* and inset-ring-* with it.
            if (classNames == null || StyleRingClass.DiscardsBoxShadow(classNames))
            {
                return false;
            }

            Read(classNames, out var outer, out var inset, out var ring);
            var layers = new List<ShadowSpec>(inset.Resolved(ShadowColourFamily.InsetShadow));
            layers.AddRange(ring.Resolved());
            layers.AddRange(outer.Resolved(ShadowColourFamily.Shadow));
            spec = layers.ToArray();
            return spec.Length > 0;
        }

        // The colours classNames declares for each family, which a discarded box-shadow keeps: they are declarations
        // of their own.
        internal static DeclaredShadowColours DeclaredColours(string[] classNames)
        {
            Read(classNames, out var outer, out var inset, out var ring);
            return new DeclaredShadowColours(outer.Declared(), inset.Declared(),
                new DeclaredShadowColour(ring.Inherit, ring.Inherit ? null : ring.Colour));
        }

        private static void Read(string[] classNames, out ShadowFamily outer, out ShadowFamily inset, out InsetRing ring)
        {
            outer = new ShadowFamily { SizeScale = 1f };
            inset = new ShadowFamily { SizeScale = 1f };
            ring = new InsetRing();
            foreach (var cls in classNames)
            {
                if (!TryRead(cls!, "shadow", Presets, ref outer) && !TryRead(cls!, "inset-shadow", InsetPresets, ref inset))
                {
                    TryReadInsetRing(cls!, ref ring);
                }
            }
        }

        // Reads cls into the family root names when it is one of its utilities: a size (the bare root is the empty
        // suffix, a preset of shadow's and of none of inset-shadow's), its -none, a bracketed shadow list, or a colour.
        // An important class takes the slot it writes from a plain one, as `box-shadow: … !important` does.
        private static bool TryRead(string cls, string root, Dictionary<string, ShadowSpec[]> presets, ref ShadowFamily family)
        {
            cls = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (string.IsNullOrEmpty(cls) || !cls.StartsWith(root, StringComparison.Ordinal))
            {
                return false;
            }
            if (!TrySplitModifier(cls, root, out var suffix, out var alpha, out var scale))
            {
                return false;
            }
            if (suffix == "inherit")
            {
                // inherit takes no modifier.
                if (alpha != null)
                {
                    return false;
                }
                if (Takes(ref family.ColourImportant, important))
                {
                    family.Inherit = true;
                    family.Colour = null;
                }
                return true;
            }
            if (TryReadColour(suffix, out var colour))
            {
                if (Takes(ref family.ColourImportant, important))
                {
                    colour.a *= alpha ?? 1f;
                    family.Colour = colour;
                    family.Inherit = false;
                }
                return true;
            }
            // A color hint makes the bracket the family's colour whether or not it holds one: a hinted value that is no
            // colour sets nothing here (BoxShadowSlot reads it as a rejected colour), and is never a size.
            if (StyleArbitraryValueResolver.TypeHintOf(cls) == "color")
            {
                return true;
            }
            if (!TryReadSize(suffix, presets, alpha, out var layers))
            {
                return false;
            }
            if (Takes(ref family.LayersImportant, important))
            {
                family.Layers = layers;
                family.SizeAlpha = alpha;
                family.SizeScale = scale ?? 1f;
            }
            return true;
        }

        // Whether a class of this importance takes a slot whose holder's importance is held, which it then holds.
        private static bool Takes(ref bool held, bool important)
        {
            if (held && !important)
            {
                return false;
            }
            held = important;
            return true;
        }

        // A size of the family: its -none (which takes no modifier), a preset, or a bracketed shadow list. Tailwind
        // writes `inset` ahead of each layer of an inset-shadow-[…].
        private static bool TryReadSize(string suffix, Dictionary<string, ShadowSpec[]> presets, float? alpha,
            out ShadowSpec[] layers)
        {
            layers = Array.Empty<ShadowSpec>();
            if (suffix == "none")
            {
                return alpha == null;
            }
            if (presets.TryGetValue(suffix, out var preset))
            {
                layers = preset;
                return true;
            }
            return StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner)
                && TryParseArbitraryList(inner.ToString(), presets == InsetPresets, out layers);
        }

        // inset-ring (1px), inset-ring-<n>, inset-ring-[<length>] and inset-ring-<colour>, as Tailwind reads them.
        private static void TryReadInsetRing(string cls, ref InsetRing ring)
        {
            cls = StyleArbitraryValueResolver.StripImportant(cls, out var important);
            if (cls == "inset-ring")
            {
                if (Takes(ref ring.WidthImportant, important))
                {
                    ring.Width = 1f;
                }
                return;
            }
            if (string.IsNullOrEmpty(cls) || !cls.StartsWith("inset-ring-", StringComparison.Ordinal))
            {
                return;
            }
            if (!TrySplitModifier(cls, "inset-ring", out var suffix, out var alpha, out _))
            {
                return;
            }
            // A width takes no modifier.
            var width = alpha == null ? InsetRingWidth(suffix) : null;
            if (width != null)
            {
                if (Takes(ref ring.WidthImportant, important))
                {
                    ring.Width = width.Value;
                }
            }
            else if (suffix == "inherit")
            {
                if (alpha == null && Takes(ref ring.ColourImportant, important))
                {
                    ring.Inherit = true;
                    ring.Colour = null;
                }
            }
            else if (TryReadColour(suffix, out var colour) && Takes(ref ring.ColourImportant, important))
            {
                colour.a *= alpha ?? 1f;
                ring.Colour = colour;
                ring.Inherit = false;
            }
        }

        private static float? InsetRingWidth(string suffix)
        {
            if (StyleRingClass.TryParseWidthValue(suffix, "inset-ring", out var width))
            {
                return width;
            }
            return null;
        }

        // The suffix of a root-* class with its type hint dropped, and the /N after it, clamped to 0..1 as the alpha of
        // a colour (alpha) and as the factor --tw-shadow-alpha scales a colour beside it by (scale, TrySizeColourScale);
        // false for a class of another root, a malformed hint or a modifier that is no opacity modifier.
        private static bool TrySplitModifier(string cls, string root, out string suffix, out float? alpha, out float? scale)
        {
            suffix = string.Empty;
            alpha = null;
            scale = null;
            var slash = StyleColorValueParser.ColorModifierSlashIndex(cls, root.Length);
            var body = slash < 0 ? cls : cls.Substring(0, slash);
            if (slash >= 0)
            {
                if (!StyleColorValueParser.TryParseModifierPercent(cls.AsSpan(slash + 1), out var percent))
                {
                    return false;
                }
                alpha = Mathf.Clamp01(percent / 100f);
                scale = SizeColourScale(cls.AsSpan(slash + 1), percent);
            }
            if (!StyleArbitraryValueResolver.TryDropTypeHint(body, out body))
            {
                return false;
            }
            if (body == root)
            {
                return true;
            }
            if (body[root.Length] != '-')
            {
                return false;
            }
            suffix = body.Substring(root.Length + 1);
            return true;
        }

        // The factor a size's /N scales a colour class beside it by, as --tw-shadow-alpha does. That property is
        // registered as a percentage, so a bracketed number is invalid for it and leaves its initial 100%; a
        // percentage outside 0..100 is valid and makes the colour's color-mix() invalid (SizeScaleInvalid).
        private static float SizeColourScale(ReadOnlySpan<char> modifier, float percent)
            => IsBareBracketNumber(modifier) || SizeScaleInvalid(modifier, percent) ? 1f : percent / 100f;

        private static bool IsBareBracketNumber(ReadOnlySpan<char> modifier)
            => modifier.Length > 1 && modifier[0] == '[' && modifier[modifier.Length - 2] != '%';

        private static bool SizeScaleInvalid(ReadOnlySpan<char> modifier, float percent)
            => !IsBareBracketNumber(modifier) && (percent < 0f || percent > 100f);

        // Whether cls's /N is a valid --tw-shadow-alpha outside 0..100%, which makes a shadow colour's color-mix()
        // invalid.
        private static bool SizeScaleInvalid(string cls, int from)
        {
            var slash = StyleColorValueParser.ColorModifierSlashIndex(cls, from);
            return slash >= 0 && StyleColorValueParser.TryParseModifierPercent(cls.AsSpan(slash + 1), out var percent)
                && SizeScaleInvalid(cls.AsSpan(slash + 1), percent);
        }

        // A colour a shadow-, inset-shadow- or inset-ring- suffix names: current (ShadowSpec.CurrentColor), a palette
        // name or transparent, or a bracket holding a colour.
        private static bool TryReadColour(string suffix, out Color colour)
        {
            colour = default;
            if (suffix == "current" || suffix == "[currentcolor]")
            {
                colour = ShadowSpec.CurrentColor;
                return true;
            }
            if (!StyleArbitraryValueResolver.TryStripBrackets(suffix, 0, out var inner))
            {
                return VelvetPalette.TryResolveColorToken(suffix, out colour);
            }
            return TailwindDataTypes.IsColor(inner) && StyleColorValueParser.TryParseColor(inner, out colour);
        }

        // One custom property of the element's box-shadow and the class holding it: the last of those writing it in
        // the order the class passes read them, which is Tailwind's for the reconciled classes (StyleFamilyOrder) with
        // the variant payloads that apply after them, weakest first (VariantGateState); an important one holds over
        // any other. Rejected and ReadsColour describe the value it holds.
        internal struct BoxShadowSlot
        {
            private bool _important;

            public bool Set { get; private set; }

            public bool Rejected { get; private set; }

            public bool ReadsColour { get; private set; }

            // A layer's size carries an opacity modifier outside 0..100%, which makes the colour beside it invalid.
            public bool ScaleInvalid { get; private set; }

            // Takes the slot for a class read after the one holding it, unless that one is important and this is not.
            public void Offer(bool important, bool rejected, bool readsColour = false, bool scaleInvalid = false)
            {
                if (Set && _important && !important)
                {
                    return;
                }
                Set = true;
                _important = important;
                Rejected = rejected;
                ReadsColour = readsColour;
                ScaleInvalid = scaleInvalid;
            }
        }

        // Whether cls is an inset-shadow-* or inset-ring-* class, whose values take part in the box-shadow the others
        // share (StyleRingClass.DiscardsBoxShadow). Velvet draws inset-shadow-* and nothing for inset-ring-*.
        internal static bool IsInsetBoxShadowClass(string cls)
            => cls.StartsWith("inset-shadow", StringComparison.Ordinal) || cls.StartsWith("inset-ring", StringComparison.Ordinal);

        // Whether a family's layer and colour discard the box-shadow: a layer the browser rejects, or one reading
        // the family's colour variable where the colour is one the browser rejects.
        internal static bool Discards(in BoxShadowSlot layer, in BoxShadowSlot colour)
            => layer.Set && (layer.Rejected || (layer.ReadsColour && (colour.Rejected || (colour.Set && layer.ScaleInvalid))));

        // The theme sizes Tailwind gives inset-shadow-*, which, unlike shadow-*, has no bare utility.
        private static readonly string[] s_insetPresets = { "2xs", "xs", "sm" };

        // Offers core, a class without its important modifier, to the slot of root's family it writes, root being
        // "shadow" or "inset-shadow", as Tailwind writes it: a size, none or a bracket it does not type as a colour
        // writes the layer, and a palette name, a keyword or a bracket it types as a colour writes the colour
        // variable alone. False for any other class.
        internal static bool TryReadBoxShadowClass(string core, string root, bool important, ref BoxShadowSlot layer,
            ref BoxShadowSlot colour)
        {
            if (!core.StartsWith(root, StringComparison.Ordinal))
            {
                return false;
            }
            var inset = root == "inset-shadow";
            var slash = StyleColorValueParser.ColorModifierSlashIndex(core, 0);
            var body = slash < 0 ? core : core.Substring(0, slash);
            if (slash >= 0 && !StyleColorValueParser.TryParseModifierPercent(core.AsSpan(slash + 1), out _))
            {
                return false;
            }
            var scaleInvalid = SizeScaleInvalid(core, 0);
            if (body == root && !inset)
            {
                layer.Offer(important, false, readsColour: true, scaleInvalid: scaleInvalid);
                return true;
            }
            if (!body.StartsWith(root + "-", StringComparison.Ordinal))
            {
                return false;
            }
            var suffix = body.Substring(root.Length + 1);
            if (suffix == "none" || (inset ? Array.IndexOf(s_insetPresets, suffix) >= 0 : Presets.ContainsKey(suffix)))
            {
                layer.Offer(important, false, readsColour: suffix != "none", scaleInvalid: scaleInvalid);
                return true;
            }
            if (TryReadBracket(core, root, out var hint, out var value))
            {
                if (hint != null ? hint == "color" : TailwindDataTypes.IsColor(value.AsSpan()))
                {
                    colour.Offer(important, StyleColorValueParser.RejectsAsColour(value) || ModifierOutside(core, 0));
                }
                else
                {
                    layer.Offer(important, RejectsAsBoxShadow(value, inset), ReadsShadowColour(value), scaleInvalid);
                }
                return true;
            }
            if (suffix is "inherit" or "initial" or "current" or "transparent" || VelvetPalette.TryResolveColorToken(suffix, out _))
            {
                colour.Offer(important, ModifierOutside(core, 0));
                return true;
            }
            return false;
        }

        // Whether a colour class's opacity modifier lies outside 0..100%, which leaves the color-mix() Tailwind writes
        // invalid.
        private static bool ModifierOutside(string cls, int from)
        {
            var slash = StyleColorValueParser.ColorModifierSlashIndex(cls, from);
            return slash >= 0 && StyleColorValueParser.TryParseModifierPercent(cls.AsSpan(slash + 1), out var percent)
                && (percent < 0f || percent > 100f);
        }

        // Whether the browser rejects value as a box-shadow, each layer behind an inset where inset says Tailwind
        // writes one there. Each layer is two to four lengths in a row, the third not negative, with at most one
        // colour and one inset around them; a layer holding a var() is not judged, nor is a function or a word as a
        // colour (StyleColorValueParser.RejectsAsColour).
        private static bool RejectsAsBoxShadow(string value, bool inset)
        {
            foreach (var layer in StyleGradientClass.SplitTopLevel(value.Replace('_', ' '), ','))
            {
                if (layer.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0
                    && RejectsAsLayer(inset ? "inset " + layer : layer))
                {
                    return true;
                }
            }
            return false;
        }

        // Whether Tailwind writes the family's colour variable into a bracket's layers (replace-shadow-colors.ts): a
        // layer does where two of its space-separated parts open as a length does, a sign then a digit or a point and
        // a digit, whatever follows; it then takes the first other part for the colour, or appends one.
        private static bool ReadsShadowColour(string value)
        {
            foreach (var layer in StyleGradientClass.SplitTopLevel(value.Replace('_', ' '), ','))
            {
                var offsets = 0;
                foreach (var part in StyleGradientClass.SplitTopLevel(layer.Trim(), ' '))
                {
                    if (OpensAsLength(part))
                    {
                        offsets++;
                    }
                }
                if (offsets >= 2)
                {
                    return true;
                }
            }
            return false;
        }

        // Tailwind's /^-?(\d+|\.\d+)/.
        private static bool OpensAsLength(string part)
        {
            var at = part.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
            var digit = at < part.Length && part[at] >= '0' && part[at] <= '9';
            return digit || (at + 1 < part.Length && part[at] == '.' && part[at + 1] >= '0' && part[at + 1] <= '9');
        }

        private static bool RejectsAsLayer(string layer)
        {
            var lengths = 0;
            var lengthsEnded = false;
            var colours = 0;
            var insets = 0;
            foreach (var token in StyleGradientClass.SplitTopLevel(layer, ' '))
            {
                if (token.Length == 0)
                {
                    continue;
                }
                if (!TailwindDataTypes.IsColor(token.AsSpan()) && (token == "0" || StyleCompositeRejection.IsLength(token)
                    || TailwindMathSpacing.HasMathFunction(token)))
                {
                    if (lengthsEnded || (lengths == 2 && token[0] == '-'))
                    {
                        return true;
                    }
                    lengths++;
                    continue;
                }
                lengthsEnded = lengths > 0;
                if (string.Equals(token, "inset", StringComparison.OrdinalIgnoreCase))
                {
                    insets++;
                }
                else if (StyleColorValueParser.RejectsAsColour(token))
                {
                    return true;
                }
                else
                {
                    colours++;
                }
            }
            return lengths < 2 || lengths > 4 || colours > 1 || insets > 1;
        }

        // The bracket of a root-[…] class with its type hint split off, whatever /modifier follows it.
        private static bool TryReadBracket(string cls, string root, out string? hint, out string value)
        {
            hint = null;
            value = string.Empty;
            var open = root.Length + 2;
            var close = cls.LastIndexOf(']');
            if (!cls.StartsWith(root + "-[", StringComparison.Ordinal) || close <= open
                || !StyleArbitraryValueResolver.TrySplitTypeHint(cls.AsSpan(open, close - open), out hint, out var span))
            {
                return false;
            }
            value = span.ToString();
            return true;
        }

        // Parses the inner body of a shadow-[…] arbitrary value: comma-separated layers, first on top. One
        // malformed layer rejects the whole list, as the browser drops a box-shadow value it cannot read.
        // inset: every layer is drawn as an inset one, as an inset-shadow-[…] writes it.
        private static bool TryParseArbitraryList(string body, bool inset, out ShadowSpec[] spec)
        {
            spec = Array.Empty<ShadowSpec>();
            var layers = StyleGradientClass.SplitTopLevel(body, ',');
            var parsed = new ShadowSpec[layers.Count];
            for (var i = 0; i < layers.Count; i++)
            {
                if (!TryParseArbitrary(inset ? "inset " + layers[i] : layers[i], out parsed[i]))
                {
                    return false;
                }
            }
            spec = parsed;
            return parsed.Length > 0;
        }

        // Parses one layer. Tokens are whitespace-separated (the className uses '_' for spaces); each is
        // classified as a length (CSS box-shadow offset/blur/spread, assigned positionally as x, y, blur, spread)
        // or a color (the shared StyleColorValueParser grammar). At least the x+y offsets are required (matching
        // CSS); blur/spread default to 0 and the color defaults to ArbitraryDefaultColor. Percent lengths are
        // rejected, as CSS rejects them in a box-shadow.
        private static bool TryParseArbitrary(string body, out ShadowSpec spec)
        {
            spec = default;
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }

            // Split outside parentheses, so a colour function's own spaces (rgb(0_0_0_/_25%)) stay in its token.
            var tokens = StyleGradientClass.SplitTopLevel(body.Replace('_', ' '), ' ');
            tokens.RemoveAll(t => t.Length == 0);
            var lengths = new float[4]; // x, y, blur, spread
            var lengthCount = 0;
            var color = ArbitraryDefaultColor;
            var hasColor = false;
            var inset = false;

            foreach (var token in tokens)
            {
                if (token == "inset" && !inset)
                {
                    inset = true;
                    continue;
                }
                if (StyleArbitraryValueResolver.TryParseValue(token.AsSpan(), out var value, out var unit)
                    && unit == LengthUnit.Pixel)
                {
                    if (lengthCount >= 4)
                    {
                        return false; // more than four box-shadow lengths is malformed
                    }
                    lengths[lengthCount++] = value;
                }
                else if (!hasColor && StyleColorValueParser.TryParseColor(token.AsSpan(), out var parsedColor))
                {
                    color = parsedColor;
                    hasColor = true;
                }
                else
                {
                    return false; // an unclassifiable token (e.g. rgba(), a percent length) is not recognized
                }
            }

            if (lengthCount < 2)
            {
                return false; // a box-shadow needs at least x and y offsets
            }

            if (lengths[2] < 0f)
            {
                return false; // CSS rejects a negative blur radius
            }
            spec = new ShadowSpec(color, lengths[2], lengths[1], lengths[3], lengths[0], inset);
            return true;
        }

        // Resolves the top-left corner radius (px) from the rounded-* / rounded-t-* /
        // rounded-l-* / rounded-tl-* classes that affect the top-left corner, mirroring the
        // --radius-* token scale. BoxShadowSilhouette uses it as the pre-layout stand-in for all four corners;
        // once laid out, each corner's own resolved radius replaces it. Of the matching classes the one Tailwind emits last wins, as
        // StyleSpacingPrecedence ranks them and StyleRuleOrder.CompareUtilities orders two that set the same corners. Returns false for
        // rounded-full / arbitrary radii (those resolve via resolvedStyle.borderTopLeftRadius
        // once laid out) and when no rounding class is present.
        public static bool TryResolveCornerRadius(string[]? classNames, out float radius)
        {
            radius = 0f;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            var heldRank = int.MinValue;
            var heldClass = string.Empty;
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                string suffix;
                ArbitraryProperty property;
                // Longest prefix first so rounded-tl-/rounded-t-/rounded-l- are not swallowed by rounded-.
                if (cls.StartsWith("rounded-tl-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-tl-".Length);
                    property = ArbitraryProperty.BorderTopLeftRadius;
                }
                else if (cls.StartsWith("rounded-ss-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-ss-".Length);
                    property = ArbitraryProperty.BorderStartStartRadius;
                }
                else if (cls.StartsWith("rounded-s-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-s-".Length);
                    property = ArbitraryProperty.BorderStartRadius;
                }
                else if (cls.StartsWith("rounded-t-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-t-".Length);
                    property = ArbitraryProperty.BorderTopRadius;
                }
                else if (cls.StartsWith("rounded-l-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-l-".Length);
                    property = ArbitraryProperty.BorderLeftRadius;
                }
                else if (cls.StartsWith("rounded-", StringComparison.Ordinal))
                {
                    suffix = cls.Substring("rounded-".Length);
                    property = ArbitraryProperty.BorderRadius;
                }
                else if (cls == "rounded")
                {
                    suffix = ""; // bare `rounded` == --radius-default
                    property = ArbitraryProperty.BorderRadius;
                }
                else
                {
                    continue;
                }

                if (!RadiusScale.TryGetValue(suffix, out var r))
                {
                    continue;
                }
                var rank = StyleSpacingPrecedence.RankOf(property);
                if (rank > heldRank || (rank == heldRank && StyleRuleOrder.CompareUtilities(heldClass, cls) <= 0))
                {
                    radius = r;
                    heldRank = rank;
                    heldClass = cls;
                    found = true;
                }
            }
            return found;
        }
    }
}
