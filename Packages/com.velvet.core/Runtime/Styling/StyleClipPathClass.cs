using System;
using UnityEngine.UIElements;

namespace Velvet
{
    // Which CSS <basic-shape> function a clip-path-[…] utility resolved to.
    internal enum ClipPathKind
    {
        Polygon,
        Circle,
        Ellipse,
        Inset,
    }

    // circle()/ellipse() radius: an explicit <length-percentage> or one of the CSS radial extent keywords.
    internal enum ClipPathExtent
    {
        Length,
        ClosestSide,
        FarthestSide,
    }

    // A CSS <length-percentage>: px values resolve as-is, % values resolve against a caller-supplied
    // basis (the reference-box axis the CSS spec prescribes for that slot — width for x, height for y,
    // the rounded diagonal for a circle radius, …).
    internal readonly struct ClipPathLength
    {
        public readonly float Value;
        public readonly bool IsPercent;

        public ClipPathLength(float value, bool isPercent)
        {
            Value = value;
            IsPercent = isPercent;
        }

        public float Resolve(float basis) => IsPercent ? Value * 0.01f * basis : Value;

        public static ClipPathLength Percent(float value) => new(value, true);
        public static ClipPathLength Px(float value) => new(value, false);
    }

    // A resolved clip-path-[…] utility: the parsed CSS basic-shape, still in <length-percentage>
    // form (resolution against the element's laid-out size happens at bake time, so one spec serves
    // every size the element takes). Source keeps the raw utility string as the
    // identity the patch path diffs — two renders carrying the same class string share one bake.
    internal sealed class ClipPathSpec
    {
        public ClipPathKind Kind;
        public string Source;

        // polygon(): optional leading fill-rule, then the vertex list as x,y interleaved pairs.
        public FillRule FillRule = FillRule.NonZero;
        public ClipPathLength[] PolygonPoints;

        // circle() / ellipse(): per-axis radius (circle uses the X slot for its single radius) and
        // the center position (CSS default: center ⇒ 50% 50%).
        public ClipPathExtent RadiusXExtent = ClipPathExtent.ClosestSide;
        public ClipPathExtent RadiusYExtent = ClipPathExtent.ClosestSide;
        public ClipPathLength RadiusX;
        public ClipPathLength RadiusY;
        public ClipPathLength CenterX = ClipPathLength.Percent(50f);
        public ClipPathLength CenterY = ClipPathLength.Percent(50f);

        // inset(): edge offsets, plus the optional `round` corner radii (tl, tr, br, bl) — null when
        // no `round` clause was given.
        public ClipPathLength InsetTop;
        public ClipPathLength InsetRight;
        public ClipPathLength InsetBottom;
        public ClipPathLength InsetLeft;
        public ClipPathLength[] CornerRadii;

        // True when the shape scales linearly per axis with the element box (every coordinate is a
        // percentage of its own axis), so a size change can reuse the existing bake stretched by
        // background-size instead of re-tessellating. circle() is never stretch-invariant (its %
        // radius resolves against the diagonal and its side keywords couple both axes); px lengths
        // and inset round radii (min-axis basis) also pin the shape to absolute pixels.
        public bool StretchInvariant;
    }

    // Parses Velvet's clip-path-[…] utility into a ClipPathSpec.
    // Same shape as StyleShadowClass: a cheap prefix gate (HasClipPathClass) plus a
    // cascade-correct extractor (TryExtract, last matching class wins; clip-path-none
    // resolves to "no clip" so it can override an earlier clip in the cascade).
    //
    // The value follows the arbitrary-value convention — underscores stand in for spaces —
    // and the CSS clip-path <basic-shape> grammar (the subset UI Toolkit can honor):
    //   clip-path-[polygon(50%_0%,100%_100%,0%_100%)]
    //   clip-path-[polygon(evenodd,50%_0%,100%_100%,0%_100%)]
    //   clip-path-[circle(40%_at_center)]      clip-path-[circle(closest-side)]
    //   clip-path-[ellipse(50%_35%_at_50%_50%)]
    //   clip-path-[inset(10px_20%_round_12px)]
    //   clip-path-none
    // UI Toolkit (6000.3) has no USS clip-path; Velvet reproduces the semantics by wrapping the
    // element in a stencil-masking container (see FiberNodePatcher's Clip Path region). State variants
    // (hover:clip-path-[…], dark:clip-path-[…], …) ARE supported: the wrapper is created when a clip
    // appears in the base OR any variant (HasAnyClipToken), persists across state changes, and the active
    // shape is resolved from the element's live class list (TryExtractLive) — the per-binding bake cache
    // makes the state toggle re-tessellation-free.
    internal static class StyleClipPathClass
    {
        private const string NoneClass = "clip-path-none";
        private const string ArbitraryPrefix = "clip-path-[";

        // True when cls is a clip-path utility this layer owns (recognized or not-yet-valid).
        public static bool IsClipPathClass(string cls)
            => cls == NoneClass || (cls != null && cls.StartsWith(ArbitraryPrefix, StringComparison.Ordinal));

        // Cheap early-out gate: true when ANY class is a clip-path utility. Used to skip the full
        // parse on the ~99% of elements that carry no clip-path class and no binding.
        public static bool HasClipPathClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls) && IsClipPathClass(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // True when the class list resolves to an ACTIVE clip (a parseable clip-path-[…] not
        // overridden by a later clip-path-none). Used by the Motion create-path warning; the patch
        // layers resolve clip state once in ApplyClipPathOnPatch and pass it along instead.
        public static bool WantsClipPath(string[] classNames)
            => TryExtract(classNames, out _);

        // Returns the resolved spec for the last recognized clip-path utility in classNames
        // (CSS cascade: later classes win). Returns false when no clip is wanted — either no
        // clip-path class at all, an unparseable value, or the winning class is clip-path-none.
        public static bool TryExtract(string[] classNames, out ClipPathSpec spec)
        {
            spec = null;
            if (classNames == null)
            {
                return false;
            }

            var found = false;
            foreach (var cls in classNames)
            {
                if (TryParse(cls, out var parsed, out var wantClip))
                {
                    // A recognized utility overrides earlier ones; clip-path-none flips found back to false.
                    found = wantClip;
                    spec = wantClip ? parsed : null;
                }
            }
            return found;
        }

        // Drives the WRAP decision: true when the element could EVER show a clip — its base classes resolve to
        // an active clip (TryExtract, so a clip-path-none override correctly yields no wrap), OR some variant
        // carries an ACTIVE clip payload (hover:clip-path-[…]; a clip-path-none payload only REMOVES a clip, so
        // it never forces a wrap by itself). The stencil wrapper must be created up-front whenever a clip can
        // apply, because wrapping/unwrapping inside a hover/focus event callback would mutate the parent
        // (forbidden outside reconcile). At rest a variant-only clip shows no mask; the live cascade
        // (TryExtractLive) applies the shape when the variant's state turns on.
        public static bool WantsClipWrapper(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            if (TryExtract(classNames, out _))
            {
                return true; // base classes resolve to an active clip (cascade incl. clip-path-none honored)
            }
            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }
                // A variant carrying an ACTIVE clip (the arbitrary clip-path-[…] form) needs the wrapper so the
                // shape can light up on its state. Peel any STACKED variant layers (dark:hover:clip-path-[…])
                // down to the leaf utility before testing it. The clip-path-none payload is excluded — it only
                // clears, so it never forces a wrapper by itself.
                var leaf = cls;
                var peeled = false;
                while (StyleVariantClass.TryParse(leaf, out _, out var payload))
                {
                    leaf = payload;
                    peeled = true;
                }
                if (peeled && leaf != null && leaf.StartsWith(ArbitraryPrefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // Resolves the active clip from an element's LIVE USS class list — the base clip plus whichever
        // variant clip payloads a manipulator has toggled on for the current state. Last-wins cascade (same as
        // TryExtract): a hover:clip payload, appended after the base clip while hovering, wins; removed on
        // hover-out, the base (or no clip) resolves again. Returns false when nothing active.
        public static bool TryExtractLive(VisualElement element, out ClipPathSpec spec)
        {
            spec = null;
            if (element == null)
            {
                return false;
            }
            var found = false;
            foreach (var cls in element.GetClasses())
            {
                if (TryParse(cls, out var parsed, out var wantClip))
                {
                    found = wantClip;
                    spec = wantClip ? parsed : null;
                }
            }
            return found;
        }

        private static bool TryParse(string cls, out ClipPathSpec spec, out bool wantClip)
        {
            spec = null;
            wantClip = false;
            if (string.IsNullOrEmpty(cls))
            {
                return false;
            }

            if (cls == NoneClass)
            {
                return true;
            }
            if (!cls.StartsWith(ArbitraryPrefix, StringComparison.Ordinal) ||
                !cls.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            // Arbitrary-value convention: underscores stand in for the spaces CSS needs.
            var css = cls.Substring(ArbitraryPrefix.Length, cls.Length - ArbitraryPrefix.Length - 1)
                .Replace('_', ' ')
                .Trim();
            if (css == "none")
            {
                return true;
            }

            if (!TryParseShape(css, out spec))
            {
                // Unparseable value: not a recognized utility (the cascade ignores it).
                return false;
            }
            spec.Source = cls;
            wantClip = true;
            return true;
        }

        // Parses a CSS <basic-shape> function (spaces already restored). Internal for the parser tests.
        internal static bool TryParseShape(string css, out ClipPathSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(css))
            {
                return false;
            }

            var open = css.IndexOf('(');
            if (open < 0 || !css.EndsWith(")", StringComparison.Ordinal))
            {
                return false;
            }
            var name = css.Substring(0, open).Trim();
            var inner = css.Substring(open + 1, css.Length - open - 2).Trim();

            switch (name)
            {
                case "polygon": return TryParsePolygon(inner, out spec);
                case "circle": return TryParseCircle(inner, out spec);
                case "ellipse": return TryParseEllipse(inner, out spec);
                case "inset": return TryParseInset(inner, out spec);
                default: return false;
            }
        }

        private static bool TryParsePolygon(string inner, out ClipPathSpec spec)
        {
            spec = null;
            var args = inner.Split(',');
            var start = 0;
            var fillRule = FillRule.NonZero;
            var first = args.Length > 0 ? args[0].Trim() : string.Empty;
            if (first == "nonzero")
            {
                start = 1;
            }
            else if (first == "evenodd")
            {
                fillRule = FillRule.OddEven;
                start = 1;
            }

            var count = args.Length - start;
            if (count < 3)
            {
                return false;
            }

            var points = new ClipPathLength[count * 2];
            var stretchInvariant = true;
            for (var i = 0; i < count; i++)
            {
                var pair = SplitWhitespace(args[start + i]);
                if (pair.Length != 2 ||
                    !TryParseLength(pair[0], out points[i * 2]) ||
                    !TryParseLength(pair[1], out points[(i * 2) + 1]))
                {
                    return false;
                }
                stretchInvariant &= points[i * 2].IsPercent && points[(i * 2) + 1].IsPercent;
            }

            spec = new ClipPathSpec
            {
                Kind = ClipPathKind.Polygon,
                FillRule = fillRule,
                PolygonPoints = points,
                StretchInvariant = stretchInvariant,
            };
            return true;
        }

        private static bool TryParseCircle(string inner, out ClipPathSpec spec)
        {
            spec = new ClipPathSpec { Kind = ClipPathKind.Circle };
            SplitAtPosition(inner, out var radiusPart, out var positionPart);

            // circle() has ONE radius, carried in the X slot (the baker's circle branch reads only
            // RadiusX/RadiusXExtent). Never stretch-invariant — see ClipPathSpec.StretchInvariant.
            if (radiusPart.Length > 0 &&
                !TryParseRadius(radiusPart, spec, isX: true))
            {
                spec = null;
                return false;
            }

            if (positionPart != null && !TryParsePositionInto(positionPart, spec))
            {
                spec = null;
                return false;
            }
            return true;
        }

        private static bool TryParseEllipse(string inner, out ClipPathSpec spec)
        {
            spec = new ClipPathSpec { Kind = ClipPathKind.Ellipse };
            SplitAtPosition(inner, out var radiusPart, out var positionPart);

            if (radiusPart.Length > 0)
            {
                var radii = SplitWhitespace(radiusPart);
                if (radii.Length != 2 ||
                    !TryParseRadius(radii[0], spec, isX: true) ||
                    !TryParseRadius(radii[1], spec, isX: false))
                {
                    spec = null;
                    return false;
                }
            }

            if (positionPart != null && !TryParsePositionInto(positionPart, spec))
            {
                spec = null;
                return false;
            }
            // ellipse() resolves per axis, so it stretches linearly when the center is % and each
            // radius is either a % length or a side keyword (min/max of %-center distances).
            spec.StretchInvariant = spec.CenterX.IsPercent && spec.CenterY.IsPercent
                && (spec.RadiusXExtent != ClipPathExtent.Length || spec.RadiusX.IsPercent)
                && (spec.RadiusYExtent != ClipPathExtent.Length || spec.RadiusY.IsPercent);
            return true;
        }

        private static bool TryParseInset(string inner, out ClipPathSpec spec)
        {
            spec = null;

            string edgePart = inner;
            string roundPart = null;
            var round = inner.IndexOf(" round ", StringComparison.Ordinal);
            if (round >= 0)
            {
                edgePart = inner.Substring(0, round);
                roundPart = inner.Substring(round + " round ".Length);
            }

            var edges = SplitWhitespace(edgePart);
            if (edges.Length < 1 || edges.Length > 4)
            {
                return false;
            }
            var edgeValues = new ClipPathLength[edges.Length];
            var stretchInvariant = true;
            for (var i = 0; i < edges.Length; i++)
            {
                if (!TryParseLength(edges[i], out edgeValues[i]))
                {
                    return false;
                }
                stretchInvariant &= edgeValues[i].IsPercent;
            }
            // CSS 1-4 value shorthand: top, right, bottom, left.
            ExpandShorthand(edgeValues, out var top, out var right, out var bottom, out var left);

            ClipPathLength[] cornerRadii = null;
            if (roundPart != null)
            {
                var radii = SplitWhitespace(roundPart);
                if (radii.Length < 1 || radii.Length > 4)
                {
                    return false;
                }
                var radiusValues = new ClipPathLength[radii.Length];
                for (var i = 0; i < radii.Length; i++)
                {
                    if (!TryParseLength(radii[i], out radiusValues[i]))
                    {
                        return false;
                    }
                }
                // CSS border-radius 1-4 value shorthand: tl, tr, br, bl.
                ExpandShorthand(radiusValues, out var tl, out var tr, out var br, out var bl);
                cornerRadii = new[] { tl, tr, br, bl };
            }

            spec = new ClipPathSpec
            {
                Kind = ClipPathKind.Inset,
                InsetTop = top,
                InsetRight = right,
                InsetBottom = bottom,
                InsetLeft = left,
                CornerRadii = cornerRadii,
                // round radii resolve against the min box axis (not per-axis), so any radius pins
                // the shape to absolute geometry.
                StretchInvariant = stretchInvariant && cornerRadii == null,
            };
            return true;
        }

        // CSS 1-4 value box shorthand: 1 ⇒ all; 2 ⇒ (a, c) = v0, (b, d) = v1; 3 ⇒ a = v0,
        // (b, d) = v1, c = v2; 4 ⇒ a b c d. Works for both edge offsets (t r b l) and corner
        // radii (tl tr br bl) — the pairing rule is identical.
        private static void ExpandShorthand(ClipPathLength[] values,
            out ClipPathLength a, out ClipPathLength b, out ClipPathLength c, out ClipPathLength d)
        {
            a = values[0];
            b = values.Length >= 2 ? values[1] : values[0];
            c = values.Length >= 3 ? values[2] : values[0];
            d = values.Length >= 4 ? values[3] : b;
        }

        // Splits "radius-part at position-part"; positionPart is null when no `at` clause exists.
        private static void SplitAtPosition(string inner, out string radiusPart, out string positionPart)
        {
            var at = inner.IndexOf(" at ", StringComparison.Ordinal);
            if (at >= 0)
            {
                radiusPart = inner.Substring(0, at).Trim();
                positionPart = inner.Substring(at + " at ".Length).Trim();
                return;
            }
            // A bare leading "at …" (no radius) is also valid CSS.
            if (inner.StartsWith("at ", StringComparison.Ordinal))
            {
                radiusPart = string.Empty;
                positionPart = inner.Substring("at ".Length).Trim();
                return;
            }
            radiusPart = inner;
            positionPart = null;
        }

        private static bool TryParseRadius(string token, ClipPathSpec spec, bool isX)
        {
            token = token.Trim();
            ClipPathExtent extent;
            var length = default(ClipPathLength);
            switch (token)
            {
                case "closest-side":
                    extent = ClipPathExtent.ClosestSide;
                    break;
                case "farthest-side":
                    extent = ClipPathExtent.FarthestSide;
                    break;
                default:
                    if (!TryParseLength(token, out length))
                    {
                        return false;
                    }
                    extent = ClipPathExtent.Length;
                    break;
            }
            if (isX)
            {
                spec.RadiusXExtent = extent;
                spec.RadiusX = length;
            }
            else
            {
                spec.RadiusYExtent = extent;
                spec.RadiusY = length;
            }
            return true;
        }

        // CSS <position>, the 1-2 value form: keywords pin their own axis, plain lengths fill
        // x-then-y positionally. An omitted axis keeps the 50% default.
        private static bool TryParsePositionInto(string text, ClipPathSpec spec)
        {
            var tokens = SplitWhitespace(text);
            if (tokens.Length < 1 || tokens.Length > 2)
            {
                return false;
            }

            var xSet = false;
            var ySet = false;
            foreach (var token in tokens)
            {
                switch (token)
                {
                    case "center":
                        // Positional: the first center fills the unfilled x slot, the second y.
                        if (!xSet) { spec.CenterX = ClipPathLength.Percent(50f); xSet = true; }
                        else { spec.CenterY = ClipPathLength.Percent(50f); ySet = true; }
                        break;
                    case "left": spec.CenterX = ClipPathLength.Percent(0f); xSet = true; break;
                    case "right": spec.CenterX = ClipPathLength.Percent(100f); xSet = true; break;
                    case "top": spec.CenterY = ClipPathLength.Percent(0f); ySet = true; break;
                    case "bottom": spec.CenterY = ClipPathLength.Percent(100f); ySet = true; break;
                    default:
                        if (!TryParseLength(token, out var len))
                        {
                            return false;
                        }
                        if (!xSet) { spec.CenterX = len; xSet = true; }
                        else if (!ySet) { spec.CenterY = len; ySet = true; }
                        else { return false; }
                        break;
                }
            }
            return true;
        }

        // Delegates to the shared utility length grammar (px/%/bare-number, InvariantCulture,
        // finite only) so clip-path values can never drift from w-[…]-style arbitrary values.
        private static bool TryParseLength(string token, out ClipPathLength length)
        {
            length = default;
            var span = token.AsSpan().Trim();
            if (span.Length == 0 ||
                !StyleArbitraryValueResolver.TryParseValue(span, out var value, out var unit))
            {
                return false;
            }
            length = new ClipPathLength(value, unit == LengthUnit.Percent);
            return true;
        }

        private static string[] SplitWhitespace(string text)
            => text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    }
}
