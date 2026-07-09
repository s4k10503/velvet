using System;
using System.Globalization;
using UnityEngine;

namespace Velvet
{
    // The gradient shape. Linear runs along an angled axis; Radial runs out from a centre to the farthest
    // box corner; Conic sweeps the hue around a centre.
    internal enum GradientType
    {
        Linear,
        Radial,
        Conic,
    }

    // The colour space the stops interpolate in. Srgb is a plain channel lerp; Oklab is the
    // perceptually-uniform OKLab lerp (avoids the muddy midpoint of opposing sRGB hues). Velvet defaults to
    // Srgb deliberately, even though the common CSS default for gradients is OKLab — this default preserves
    // existing visuals; opt into OKLab with the /oklch or /oklab modifier.
    internal enum GradientInterp
    {
        Srgb,
        Oklab,
    }

    // A resolved gradient: shape (linear angle / radial-or-conic centre), interpolation space, and the
    // from / (optional) via / to colour stops with their positions (0..1; defaults 0 / 0.5 / 1).
    // AngleDeg is the linear axis angle (CSS degrees, 0 = to top, clockwise) and doubles as the conic start
    // angle. Equality is value-based (quantized) so the baked-texture cache and the reconciler binding skip
    // redundant work when an unchanged class list re-resolves to the same gradient.
    internal readonly struct GradientSpec : IEquatable<GradientSpec>
    {
        public readonly GradientType Type;
        public readonly float AngleDeg;
        public readonly float CenterX;
        public readonly float CenterY;
        public readonly GradientInterp Interp;
        public readonly Color From;
        public readonly Color To;
        public readonly Color Via;
        public readonly bool HasVia;
        public readonly float FromPos;
        public readonly float ViaPos;
        public readonly float ToPos;

        public GradientSpec(GradientType type, float angleDeg, float centerX, float centerY, GradientInterp interp,
            Color from, Color to, bool hasVia, Color via, float fromPos, float viaPos, float toPos)
        {
            Type = type;
            AngleDeg = angleDeg;
            CenterX = centerX;
            CenterY = centerY;
            Interp = interp;
            From = from;
            To = to;
            HasVia = hasVia;
            Via = via;
            FromPos = fromPos;
            ViaPos = viaPos;
            ToPos = toPos;
        }

        // 8-bit RGBA key. Equality AND hashing both go through this so the Equals/GetHashCode contract holds
        // (Color's == is epsilon-approximate while Color.GetHashCode is exact-bit — mixing them would let
        // two .Equals-equal specs hash to different cache buckets). Quantizing to Color32 also matches the
        // baked texture's 8-bit precision, so two colors that round to the same byte dedupe.
        private static int ColorKey(Color c)
        {
            var c32 = (Color32)c;
            return (c32.r << 24) | (c32.g << 16) | (c32.b << 8) | c32.a;
        }

        // Angle key at 0.25° precision, normalized mod 360 so -45° and 315° (the same axis — sin/cos are
        // periodic) share one cache entry. Position / centre keys at 0.1% precision.
        private static int AngleKey(float deg) => Mathf.RoundToInt((((deg % 360f) + 360f) % 360f) * 4f);
        private static int PosKey(float p) => Mathf.RoundToInt(Mathf.Clamp01(p) * 1000f);

        public bool Equals(GradientSpec other)
        {
            // Only the fields that affect THIS type's render participate in equality, so two specs that
            // differ only in a field the type ignores (e.g. a radial's angle) still share one bake.
            if (Type != other.Type || Interp != other.Interp)
            {
                return false;
            }
            if (Type != GradientType.Radial && AngleKey(AngleDeg) != AngleKey(other.AngleDeg))
            {
                return false;
            }
            if (Type != GradientType.Linear
                && (PosKey(CenterX) != PosKey(other.CenterX) || PosKey(CenterY) != PosKey(other.CenterY)))
            {
                return false;
            }
            return ColorKey(From) == ColorKey(other.From)
                && ColorKey(To) == ColorKey(other.To)
                && HasVia == other.HasVia
                && (!HasVia || ColorKey(Via) == ColorKey(other.Via))
                && PosKey(FromPos) == PosKey(other.FromPos)
                && (!HasVia || PosKey(ViaPos) == PosKey(other.ViaPos))
                && PosKey(ToPos) == PosKey(other.ToPos);
        }

        public override bool Equals(object obj) => obj is GradientSpec o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (int)Type;
                h = h * 31 + (int)Interp;
                h = h * 31 + (Type != GradientType.Radial ? AngleKey(AngleDeg) : 0);
                h = h * 31 + (Type != GradientType.Linear ? PosKey(CenterX) : 0);
                h = h * 31 + (Type != GradientType.Linear ? PosKey(CenterY) : 0);
                h = h * 31 + ColorKey(From);
                h = h * 31 + ColorKey(To);
                h = h * 31 + (HasVia ? ColorKey(Via) : 0);
                h = h * 31 + PosKey(FromPos);
                h = h * 31 + (HasVia ? PosKey(ViaPos) : 0);
                h = h * 31 + PosKey(ToPos);
                return h;
            }
        }
    }

    // Parses Velvet's gradient utilities into a GradientSpec:
    //   - shape: bg-gradient-to-{dir} / bg-linear-{deg|[deg]} / bg-linear-to-{dir} (linear); bg-radial /
    //     bg-radial-[at_{position}] (radial); bg-conic / bg-conic-{deg} / bg-conic-[from_{deg}] (conic);
    //   - interpolation: an optional /srgb or /oklch (== /oklab) modifier on the shape activator;
    //   - stops: from-/via-/to-{color} (named palette or arbitrary [#hex]) and from-/via-/to-{N%} positions
    //     (a percentage value is a POSITION, anything else a COLOR — independent utilities).
    // A lone stop with no shape activator is inert. Cheap prefix gate + a cascade-correct
    // extractor (last shape wins; last from/via/to colour and position win).
    //
    // CSS-spec coverage (Images L3/L4, Color 4): linear with arbitrary angle + stop positions, radial-
    // gradient, conic-gradient, and OKLab interpolation. Deviations: USS has no gradient, so it bakes into
    // a texture stretched to the box, which makes a non-axis-aligned linear angle (and the radial circle)
    // box-normalized rather than physical-aspect; the OKLCH cylindrical hue-arc is approximated by OKLab
    // (cartesian) interpolation; continuously-animated gradients (no CSS gradient type) are out of scope.
    internal static class StyleGradientClass
    {
        private const string DirActivator = "bg-gradient-to-";
        private const string LinearActivator = "bg-linear-";
        private const string RadialActivator = "bg-radial";
        private const string ConicActivator = "bg-conic";
        private const string FromPrefix = "from-";
        private const string ViaPrefix = "via-";
        private const string ToPrefix = "to-";

        // The one activator shape that accepts a leading '-' for a negative numeric angle
        // (TryParseAngle strips it before matching LinearActivator; the -to- alias never accepts it).
        private const string NegativeLinearActivator = "-" + LinearActivator;
        private const string RadialArbitraryActivator = RadialActivator + "-[";
        private const string ConicSuffixActivator = ConicActivator + "-";

        // Cheap prefix/equality table for gradient shape activators, shared by the gate and the parser
        // so the two can never drift apart: TryParseActivator consults this SAME table as its first
        // check, which makes "parser-accepts implies gate-accepts" hold by construction rather than by
        // the two being kept in sync by hand. Radial/conic must stay plain StartsWith: the bare
        // activator may carry a trailing /interp modifier ("bg-radial/oklab"), which an equality check
        // would miss.
        private static bool MatchesActivatorPrefix(string cls)
        {
            return cls.StartsWith(DirActivator, StringComparison.Ordinal)
                || cls.StartsWith(LinearActivator, StringComparison.Ordinal)
                || cls.StartsWith(NegativeLinearActivator, StringComparison.Ordinal)
                || cls.StartsWith(RadialActivator, StringComparison.Ordinal)
                || cls.StartsWith(ConicActivator, StringComparison.Ordinal);
        }

        // Cheap early-out gate: true when ANY class LOOKS LIKE a gradient shape activator. A pure
        // prefix scan — no substring allocation, no angle/position parsing — so it never
        // duplicates TryExtract's parse cost. May false-positive on a malformed activator (e.g. an
        // unrecognized /modifier or an out-of-range value), which TryExtract then correctly rejects.
        // It never false-negatives on anything TryExtract can actually resolve BY CONSTRUCTION:
        // TryParseActivator's first check is this same MatchesActivatorPrefix, so a class TryExtract
        // can parse always already looked like an activator here. Used to skip the full TryExtract on
        // the ~99% of elements with no gradient.
        public static bool HasGradientClass(string[] classNames)
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
                if (MatchesActivatorPrefix(cls))
                {
                    return true;
                }
            }
            return false;
        }

        // Resolves the gradient: last shape activator wins, last from/via/to colour and position each win.
        // Returns false when no shape activator is present, or when neither a from nor a to COLOR is given
        // (positions alone draw nothing). A missing from/to colour defaults to the transparent version of
        // the other stop (the default behavior).
        public static bool TryExtract(string[] classNames, out GradientSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var hasAxis = false;
            var type = GradientType.Linear;
            var angle = 0f;
            float centerX = 0.5f, centerY = 0.5f;
            var interp = GradientInterp.Srgb;
            bool hasFrom = false, hasVia = false, hasTo = false;
            Color from = default, via = default, to = default;
            float fromPos = 0f, viaPos = 0.5f, toPos = 1f;

            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                if (TryParseActivator(cls, out var t, out var a, out var cx, out var cy, out var ip))
                {
                    type = t;
                    angle = a;
                    centerX = cx;
                    centerY = cy;
                    interp = ip;
                    hasAxis = true;
                }
                else if (cls.StartsWith(FromPrefix, StringComparison.Ordinal))
                {
                    ParseStop(cls.Substring(FromPrefix.Length), ref from, ref hasFrom, ref fromPos);
                }
                else if (cls.StartsWith(ViaPrefix, StringComparison.Ordinal))
                {
                    ParseStop(cls.Substring(ViaPrefix.Length), ref via, ref hasVia, ref viaPos);
                }
                else if (cls.StartsWith(ToPrefix, StringComparison.Ordinal))
                {
                    ParseStop(cls.Substring(ToPrefix.Length), ref to, ref hasTo, ref toPos);
                }
            }

            if (!hasAxis || (!hasFrom && !hasTo))
            {
                return false;
            }
            if (!hasFrom)
            {
                from = new Color(to.r, to.g, to.b, 0f);
            }
            if (!hasTo)
            {
                to = new Color(from.r, from.g, from.b, 0f);
            }

            spec = new GradientSpec(type, angle, centerX, centerY, interp, from, to, hasVia, via,
                fromPos, viaPos, toPos);
            return true;
        }

        // Parses a gradient shape activator, including an optional /interp modifier. Out params:
        // type, angle (linear axis / conic start; 0 for radial), centre (radial/conic; 0.5,0.5 default),
        // interp. False when the class is not a recognized activator (incl. an unknown /modifier).
        private static bool TryParseActivator(string cls, out GradientType type, out float angle,
            out float centerX, out float centerY, out GradientInterp interp)
        {
            type = GradientType.Linear;
            angle = 0f;
            centerX = 0.5f;
            centerY = 0.5f;
            interp = GradientInterp.Srgb;

            // Must pass the gate's own prefix table before any parsing: this is what makes the shapes
            // this method accepts a structural SUBSET of what HasGradientClass matches, so a shape added
            // here without also widening MatchesActivatorPrefix simply cannot parse (loud), instead of
            // parsing while the gate silently skips it (the false-negative this guards against).
            if (!MatchesActivatorPrefix(cls))
            {
                return false;
            }

            // Split off a trailing /interp modifier (the gradient interpolation modifier).
            var baseTok = cls;
            var slash = cls.IndexOf('/');
            if (slash >= 0)
            {
                switch (cls.Substring(slash + 1))
                {
                    case "oklch":
                    case "oklab": interp = GradientInterp.Oklab; break;
                    case "srgb": interp = GradientInterp.Srgb; break;
                    default: return false; // unknown modifier → not a valid activator
                }
                baseTok = cls.Substring(0, slash);
            }

            if (baseTok == RadialActivator)
            {
                type = GradientType.Radial;
                return true;
            }
            if (baseTok.StartsWith(RadialArbitraryActivator, StringComparison.Ordinal) && baseTok[baseTok.Length - 1] == ']')
            {
                type = GradientType.Radial;
                ParseRadialPosition(baseTok.Substring(RadialActivator.Length + 2, baseTok.Length - RadialActivator.Length - 3),
                    ref centerX, ref centerY);
                return true;
            }
            if (baseTok == ConicActivator)
            {
                type = GradientType.Conic;
                return true;
            }
            if (baseTok.StartsWith(ConicSuffixActivator, StringComparison.Ordinal))
            {
                type = GradientType.Conic;
                return TryParseConicStart(baseTok.Substring(ConicActivator.Length + 1), out angle);
            }
            if (TryParseAngle(baseTok, out angle))
            {
                type = GradientType.Linear;
                return true;
            }
            return false;
        }

        // bg-conic-{int} (start degrees) or bg-conic-[from_{deg}] / bg-conic-[{deg}].
        private static bool TryParseConicStart(string s, out float deg)
        {
            if (s.StartsWith("[from_", StringComparison.Ordinal) && s[s.Length - 1] == ']')
            {
                return TryParseAngleValue("[" + s.Substring(6), out deg);
            }
            return TryParseAngleValue(s, out deg);
        }

        // Parses a bg-radial-[at_...] position into a UV centre. Keywords (top/bottom/left/right/center)
        // pin their own axis; a {N}% value fills the next axis not already pinned by a keyword (CSS position
        // order: x then y). Tracking which axes are set with explicit flags (not a value sentinel on
        // centerX) is what lets `at_50%_75%` and `at_left_50%` resolve correctly. center / unknown tokens
        // are no-ops (the centre defaults to 0.5,0.5).
        private static void ParseRadialPosition(string content, ref float centerX, ref float centerY)
        {
            bool xSet = false, ySet = false;
            foreach (var tok in content.Split('_'))
            {
                switch (tok)
                {
                    case "at":
                    case "center": break;
                    case "left": centerX = 0f; xSet = true; break;
                    case "right": centerX = 1f; xSet = true; break;
                    case "top": centerY = 0f; ySet = true; break;
                    case "bottom": centerY = 1f; ySet = true; break;
                    default:
                        if (tok.Length >= 2 && tok[tok.Length - 1] == '%'
                            && float.TryParse(tok.Substring(0, tok.Length - 1), NumberStyles.Float,
                                CultureInfo.InvariantCulture, out var pct))
                        {
                            var frac = Mathf.Clamp01(pct / 100f);
                            if (!xSet) { centerX = frac; xSet = true; }
                            else if (!ySet) { centerY = frac; ySet = true; }
                        }
                        break;
                }
            }
        }

        // A from-/via-/to- remainder is EITHER a stop position (a percentage) or a colour — independent
        // utilities. A percentage sets only the position; a recognized colour sets the colour (and
        // marks the stop present); anything else is ignored (leaves the accumulated values untouched).
        private static void ParseStop(string suffix, ref Color color, ref bool hasColor, ref float pos)
        {
            if (TryParsePercent(suffix, out var p))
            {
                pos = p;
                return;
            }
            if (VelvetPalette.TryResolveColorToken(suffix, out var c))
            {
                color = c;
                hasColor = true;
            }
        }

        // "10%" or "[12.5%]" → 0.10 / 0.125 (clamped to 0..1). False when not a percentage.
        private static bool TryParsePercent(string s, out float frac)
        {
            frac = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            var body = s;
            if (body.Length >= 2 && body[0] == '[' && body[body.Length - 1] == ']')
            {
                body = body.Substring(1, body.Length - 2);
            }
            if (body.Length < 2 || body[body.Length - 1] != '%')
            {
                return false;
            }
            var num = body.Substring(0, body.Length - 1);
            if (!float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                || float.IsNaN(v) || float.IsInfinity(v))
            {
                return false;
            }
            frac = Mathf.Clamp01(v / 100f);
            return true;
        }

        // Resolves a linear gradient axis activator to a CSS angle (degrees): bg-gradient-to-{dir},
        // bg-linear-to-{dir} (v4 alias), bg-linear-{deg} / -bg-linear-{deg}, bg-linear-[{deg}deg].
        private static bool TryParseAngle(string cls, out float angleDeg)
        {
            angleDeg = 0f;
            var negative = cls.Length > 0 && cls[0] == '-';
            var body = negative ? cls.Substring(1) : cls;

            if (body.StartsWith(DirActivator, StringComparison.Ordinal))
            {
                return !negative && TryDirectionAngle(body.Substring(DirActivator.Length), out angleDeg);
            }
            if (body.StartsWith(LinearActivator, StringComparison.Ordinal))
            {
                var rest = body.Substring(LinearActivator.Length);
                if (rest.StartsWith("to-", StringComparison.Ordinal))
                {
                    return !negative && TryDirectionAngle(rest.Substring(3), out angleDeg);
                }
                if (TryParseAngleValue(rest, out var deg))
                {
                    angleDeg = negative ? -deg : deg;
                    return true;
                }
            }
            return false;
        }

        // One of the 8 named directions → its CSS angle (0 = to top, clockwise).
        private static bool TryDirectionAngle(string dir, out float angleDeg)
        {
            switch (dir)
            {
                case "t": angleDeg = 0f; return true;
                case "tr": angleDeg = 45f; return true;
                case "r": angleDeg = 90f; return true;
                case "br": angleDeg = 135f; return true;
                case "b": angleDeg = 180f; return true;
                case "bl": angleDeg = 225f; return true;
                case "l": angleDeg = 270f; return true;
                case "tl": angleDeg = 315f; return true;
                default: angleDeg = 0f; return false;
            }
        }

        // {int} (degrees) or [{float}deg] / [{float}] (arbitrary).
        private static bool TryParseAngleValue(string s, out float deg)
        {
            deg = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            if (s.Length >= 3 && s[0] == '[' && s[s.Length - 1] == ']')
            {
                var inner = s.Substring(1, s.Length - 2);
                if (inner.EndsWith("deg", StringComparison.Ordinal))
                {
                    inner = inner.Substring(0, inner.Length - 3);
                }
                return float.TryParse(inner, NumberStyles.Float, CultureInfo.InvariantCulture, out deg)
                    && !float.IsNaN(deg) && !float.IsInfinity(deg);
            }
            if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
            {
                deg = whole;
                return true;
            }
            return false;
        }
    }
}
