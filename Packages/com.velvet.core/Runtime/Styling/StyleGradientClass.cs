using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The gradient shape. Linear runs along an angled axis; Radial runs out from a centre to its radii
    // (by default the farthest box corner); Conic sweeps around a centre.
    internal enum GradientType
    {
        Linear,
        Radial,
        Conic,
    }

    // The colour space the stops interpolate in. Srgb is a plain channel lerp, SrgbLinear the same on linear
    // light, Oklab and Lab the perceptually-uniform lerps (avoiding the muddy midpoint of opposing sRGB
    // hues), and Oklch, Lch and Hsl the polar forms, whose hue travels along the arc a HueMethod picks.
    // A utility class with no modifier interpolates in Oklab, as Tailwind's do; a gradient written in an
    // arbitrary bracket names no space and is Srgb, which is what CSS does for the legacy colours it carries.
    internal enum GradientInterp
    {
        Srgb,
        Oklab,
        SrgbLinear,
        Oklch,
        Lab,
        Lch,
        Hsl,
    }

    // Which way round the hue circle a polar interpolation goes.
    internal enum HueMethod
    {
        Shorter,
        Longer,
        Increasing,
        Decreasing,
    }

    // How far a radial gradient reaches: the extent keywords CSS names, or sizes given explicitly.
    internal enum RadialExtent
    {
        FarthestCorner,
        ClosestSide,
        ClosestCorner,
        FarthestSide,
        Explicit,
    }

    // A radial gradient's shape and size as radial-gradient() writes them. The default is CSS's: an ellipse
    // reaching the farthest corner. Explicit sizes are X and Y: pixels, or fractions of the box where the
    // matching Percent flag is set (a circle's radius is X, in pixels).
    internal readonly struct RadialSizing : IEquatable<RadialSizing>
    {
        public bool Circle { get; init; }
        public RadialExtent Extent { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public bool XPercent { get; init; }
        public bool YPercent { get; init; }

        public bool Equals(RadialSizing other)
            => Circle == other.Circle && Extent == other.Extent && XPercent == other.XPercent
                && YPercent == other.YPercent && Mathf.RoundToInt(X * 100f) == Mathf.RoundToInt(other.X * 100f)
                && Mathf.RoundToInt(Y * 100f) == Mathf.RoundToInt(other.Y * 100f);

        public override bool Equals(object obj) => obj is RadialSizing o && Equals(o);

        public override int GetHashCode()
            => HashCode.Combine(Circle, (int)Extent, XPercent, YPercent, Mathf.RoundToInt(X * 100f), Mathf.RoundToInt(Y * 100f));
    }

    // One colour stop of a resolved gradient: its colour and its position along the gradient line (0..1,
    // or beyond), and a colour hint between this stop and the next, if one was written there. A position
    // written in pixels is PositionPx, with Position NaN until the box is known (the same for HintPx), and
    // a position the list left out is NaN until the colour-stop fix-up places it.
    internal readonly struct GradientStop
    {
        public GradientStop(Color color, float position, float positionPx = float.NaN, float hint = float.NaN,
            float hintPx = float.NaN)
        {
            Color = color;
            Position = position;
            PositionPx = positionPx;
            Hint = hint;
            HintPx = hintPx;
        }

        public Color Color { get; }
        public float Position { get; }
        public float PositionPx { get; }
        public float Hint { get; }
        public float HintPx { get; }

        public GradientStop WithHint(float hint, float hintPx) => new(Color, Position, PositionPx, hint, hintPx);

        public bool HasLength => !float.IsNaN(PositionPx) || !float.IsNaN(HintPx);
    }

    // A resolved gradient: shape (linear angle / radial-or-conic centre), interpolation space, and its ordered
    // colour stops (positions 0..1, fixed up into a non-decreasing run).
    // Stops arrays are shared between specs through StyleGradientClass's memos and are never written.
    // AngleDeg is the linear axis angle (CSS degrees, 0 = to top, clockwise) and doubles as the conic start
    // angle. ToCorner marks a linear gradient written as a corner direction (to top right), whose angle
    // AngleDeg only gives the quadrant of: GradientBackground lays a corner direction out over a square and
    // an angle written as a number over the box's aspect. Equality is value-based (quantized) so the
    // baked-texture cache and the reconciler binding skip redundant work when an unchanged class list
    // re-resolves to the same gradient.
    internal readonly struct GradientSpec : IEquatable<GradientSpec>
    {
        // The skew silhouette shader declares its stop arrays at this length, so the parser rejects a longer
        // list rather than let the skewed paint drop stops the straight paint draws. GradientStopCapacityTests
        // holds the shader's length to this one.
        public const int MaxStops = 64;

        public GradientType Type { get; init; }
        public float AngleDeg { get; init; }
        public bool ToCorner { get; init; }
        public RadialSizing Radial { get; init; }
        public float CenterX { get; init; }
        public float CenterY { get; init; }
        public GradientInterp Interp { get; init; }
        public HueMethod Hue { get; init; }
        public GradientStop[] Stops { get; init; }

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
        private static int PosKey(float p) => float.IsNaN(p) ? int.MinValue : Mathf.RoundToInt(p * 1000f);

        public bool Equals(GradientSpec other)
        {
            // Only the fields that affect THIS type's render participate in equality, so two specs that
            // differ only in a field the type ignores (e.g. a radial's angle) still share one bake.
            if (Type != other.Type || Interp != other.Interp)
            {
                return false;
            }
            if (IsPolarSpace(Interp) && Hue != other.Hue)
            {
                return false;
            }
            if (Type != GradientType.Radial && AngleKey(AngleDeg) != AngleKey(other.AngleDeg))
            {
                return false;
            }
            if (Type == GradientType.Linear && ToCorner != other.ToCorner)
            {
                return false;
            }
            if (Type == GradientType.Radial && !Radial.Equals(other.Radial))
            {
                return false;
            }
            if (Type != GradientType.Linear
                && (PosKey(CenterX) != PosKey(other.CenterX) || PosKey(CenterY) != PosKey(other.CenterY)))
            {
                return false;
            }
            return StopsEqual(Stops ?? Array.Empty<GradientStop>(), other.Stops ?? Array.Empty<GradientStop>());
        }

        // Whether the space has a hue, which travels along an arc rather than being averaged.
        internal static bool IsPolarSpace(GradientInterp interp)
            => interp == GradientInterp.Oklch || interp == GradientInterp.Lch || interp == GradientInterp.Hsl;

        private static bool StopsEqual(GradientStop[] a, GradientStop[] b)
        {
            var same = a.Length == b.Length;
            for (var i = 0; same && i < a.Length; i++)
            {
                same = ColorKey(a[i].Color) == ColorKey(b[i].Color) && PosKey(a[i].Position) == PosKey(b[i].Position)
                    && PosKey(a[i].PositionPx) == PosKey(b[i].PositionPx) && PosKey(a[i].Hint) == PosKey(b[i].Hint)
                    && PosKey(a[i].HintPx) == PosKey(b[i].HintPx);
            }
            return same;
        }

        // True when a stop or hint is written in pixels, so the stops cannot be placed before the box is known.
        public bool HasLengths
        {
            get
            {
                foreach (var stop in Stops ?? Array.Empty<GradientStop>())
                {
                    if (stop.HasLength)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override bool Equals(object obj) => obj is GradientSpec o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (int)Type;
                h = h * 31 + (int)Interp;
                h = h * 31 + (IsPolarSpace(Interp) ? (int)Hue : 0);
                h = h * 31 + (Type != GradientType.Radial ? AngleKey(AngleDeg) : 0);
                h = h * 31 + (Type == GradientType.Linear && ToCorner ? 1 : 0);
                h = h * 31 + (Type == GradientType.Radial ? Radial.GetHashCode() : 0);
                h = h * 31 + (Type != GradientType.Linear ? PosKey(CenterX) : 0);
                h = h * 31 + (Type != GradientType.Linear ? PosKey(CenterY) : 0);
                foreach (var stop in Stops ?? Array.Empty<GradientStop>())
                {
                    h = HashCode.Combine(h, ColorKey(stop.Color), PosKey(stop.Position), PosKey(stop.PositionPx), PosKey(stop.Hint));
                }
                return h;
            }
        }
    }

    // Parses Velvet's gradient utilities into a GradientSpec:
    //   - shape: bg-gradient-to-{dir} / bg-linear-{deg|[deg]} / bg-linear-to-{dir} (linear); bg-radial /
    //     bg-radial-[at_{position}] (radial); bg-conic / bg-conic-{deg} / bg-conic-[from_{deg}] (conic);
    //   - interpolation: an optional /srgb or /oklch (== /oklab) modifier on the shape activator;
    //   - stops: from-/via-/to-{color} (named palette or arbitrary [#hex]) and from-/via-/to-{N%} positions
    //     (a percentage value is a POSITION, anything else a COLOR — independent utilities);
    //   - or a CSS argument list in the shape's brackets, its stops carried with it:
    //     bg-linear-[90deg,red_0%,blue_100%], bg-linear-[to_right_in_oklab,…], bg-radial-[at_top,…],
    //     bg-conic-[from_90deg_at_25%_75%,…].
    // A lone stop with no shape activator is inert. Cheap prefix gate + a cascade-correct
    // extractor (last shape wins; last from/via/to colour and position win).
    //
    // CSS-spec coverage (Images L3/L4, Color 4): linear with arbitrary angle + stop positions, radial-
    // gradient, conic-gradient, and OKLab interpolation. Deviations: the OKLCH cylindrical hue-arc is
    // approximated by OKLab (cartesian) interpolation; continuously-animated gradients (no CSS gradient
    // type) are out of scope.
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
        private const string LinearListActivator = LinearActivator + "[";
        private const string ConicListActivator = ConicSuffixActivator + "[";

        // A shape activator's reading. Stops and Raw are set only by an activator whose brackets carry a stop
        // list: Raw is the list as written, a stop's position NaN where it gave none, and Stops is Raw
        // fixed up. The from-/via-/to- utilities extend Raw rather than Stops, since CSS places an
        // unpositioned stop among the stops on both sides of it.
        private struct Shape
        {
            public GradientType Type;
            public float Angle;
            public bool ToCorner;
            public RadialSizing Radial;
            public float CenterX;
            public float CenterY;
            public GradientInterp Interp;
            public HueMethod Hue;
            public GradientStop[]? Stops;
            public GradientStop[]? Raw;
        }

        // A class string's reading as a shape activator, Ok false when it is not one.
        private struct ActivatorReading
        {
            public bool Ok;
            public Shape Shape;
        }

        // The accumulated reading of one of the from-/via-/to- utilities.
        private struct UtilityStop
        {
            public bool HasColor;
            public Color Color;
            public float Position;
            public float PositionPx;
        }

        // A from-/via-/to- suffix's reading: a position, a colour, or neither.
        private struct StopToken
        {
            public bool IsPosition;
            public float Position;
            public float PositionPx;
            public bool IsColor;
            public Color Color;
        }

        // ApplyGradientOnPatch re-extracts on every patch of a gradient element, so each reading of a class
        // string, and each stop array the from-/via-/to- utilities build, is kept and handed out again.
        // Rejected: ClassNameParseCache's admission scheme, which keeps moving values from pushing out a
        // render's stable strings because a string parsed again after leaving that cache is a new array the
        // reconciler reads as a change. A spec is compared by value, so an entry cleared here costs only a
        // re-parse to an equal one.
        private sealed class BoundedMemo<TKey, TValue> where TKey : notnull
        {
            private const int Capacity = 256;
            private readonly Dictionary<TKey, TValue> _entries = new();

            public bool TryGet(TKey key, out TValue value)
            {
                if (_entries.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }
                value = default!;
                return false;
            }

            public void Add(TKey key, TValue value)
            {
                if (_entries.Count >= Capacity)
                {
                    _entries.Clear();
                }
                _entries.Add(key, value);
            }
        }

        private static readonly BoundedMemo<string, ActivatorReading> s_activators = new();
        private static readonly BoundedMemo<string, StopToken> s_stopTokens = new();
        private static readonly BoundedMemo<(GradientStop[]?, ((bool, Color, float, float), (bool, Color, float, float),
            (bool, Color, float, float))),
            GradientStop[]> s_utilityStops = new();

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

        // True when cls is any token TryExtract reads — a shape activator OR a from-/via-/to- colour stop.
        // Deliberately wider than the activator gate below: that one answers "could this element have a
        // gradient at all", while this answers "would this token change the resolved gradient", which is
        // the question a variant payload has to be judged by — `bg-gradient-to-r from-blue-500
        // hover:from-red-500` changes the gradient through a stop, with no activator in the payload.
        public static bool IsGradientClass(string cls)
            => !string.IsNullOrEmpty(cls)
                && (MatchesActivatorPrefix(cls)
                    || cls.StartsWith(FromPrefix, StringComparison.Ordinal)
                    || cls.StartsWith(ViaPrefix, StringComparison.Ordinal)
                    || cls.StartsWith(ToPrefix, StringComparison.Ordinal));

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
        // Returns false when no from, via or to COLOR is given and the winning activator carries no stop
        // list (positions alone draw nothing), and a missing from/to colour defaults to the transparent
        // version of the other end, or of via when it is the only one (the default behavior). Returns false when no shape activator is present.
        // A stop list's stops are followed by the from-/via-/to- stops when one of them names a colour (see
        // ResolveStops).
        public static bool TryExtract(string[] classNames, out GradientSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var hasShape = false;
            var shape = default(Shape);
            var fromStop = new UtilityStop { Position = 0f, PositionPx = float.NaN };
            var viaStop = new UtilityStop { Position = 0.5f, PositionPx = float.NaN };
            var toStop = new UtilityStop { Position = 1f, PositionPx = float.NaN };

            foreach (var cls in classNames)
            {
                if (string.IsNullOrEmpty(cls))
                {
                    continue;
                }

                if (TryReadActivator(cls, out var parsed))
                {
                    shape = parsed;
                    hasShape = true;
                }
                else if (cls.StartsWith(FromPrefix, StringComparison.Ordinal))
                {
                    ReadStop(cls, FromPrefix.Length, ref fromStop);
                }
                else if (cls.StartsWith(ViaPrefix, StringComparison.Ordinal))
                {
                    ReadStop(cls, ViaPrefix.Length, ref viaStop);
                }
                else if (cls.StartsWith(ToPrefix, StringComparison.Ordinal))
                {
                    ReadStop(cls, ToPrefix.Length, ref toStop);
                }
            }

            var stops = hasShape ? ResolveStops(in shape, in fromStop, in viaStop, in toStop) : null;
            if (stops == null)
            {
                return false;
            }

            spec = new GradientSpec
            {
                Type = shape.Type,
                AngleDeg = shape.Angle,
                ToCorner = shape.ToCorner,
                Radial = shape.Radial,
                CenterX = shape.CenterX,
                CenterY = shape.CenterY,
                Interp = shape.Interp,
                Hue = shape.Hue,
                Stops = stops,
            };
            return true;
        }

        // The winning shape's stops: its list, extended by the from-/via-/to- utilities that name a colour —
        // after the list's stops, as Tailwind places them — or, for a shape with no list, the utilities
        // alone. Null when no stop results, or when a list and the utilities together exceed
        // GradientSpec.MaxStops.
        private static GradientStop[]? ResolveStops(in Shape shape, in UtilityStop fromStop, in UtilityStop viaStop,
            in UtilityStop toStop)
        {
            if (!fromStop.HasColor && !viaStop.HasColor && !toStop.HasColor)
            {
                return shape.Stops;
            }
            var extra = viaStop.HasColor ? 3 : 2;
            if ((shape.Raw?.Length ?? 0) + extra > GradientSpec.MaxStops)
            {
                return null;
            }
            var key = (shape.Raw, ((fromStop.HasColor, fromStop.Color, fromStop.Position, fromStop.PositionPx),
                (viaStop.HasColor, viaStop.Color, viaStop.Position, viaStop.PositionPx),
                (toStop.HasColor, toStop.Color, toStop.Position, toStop.PositionPx)));
            if (s_utilityStops.TryGet(key, out var cached))
            {
                return cached;
            }
            var first = new GradientStop(fromStop.HasColor ? fromStop.Color : Transparent(Neighbour(toStop, viaStop)),
                fromStop.Position, fromStop.PositionPx);
            var last = new GradientStop(toStop.HasColor ? toStop.Color : Transparent(Neighbour(fromStop, viaStop)),
                toStop.Position, toStop.PositionPx);
            var utilities = viaStop.HasColor
                ? new[] { first, new GradientStop(viaStop.Color, viaStop.Position, viaStop.PositionPx), last }
                : new[] { first, last };
            var raw = shape.Raw == null ? utilities : shape.Raw.Concat(utilities).ToArray();
            var stops = Place(raw);
            s_utilityStops.Add(key, stops);
            return stops;
        }

        // The colour an absent end fades from or to: the far end's when it has one, else the via stop's.
        private static Color Neighbour(in UtilityStop far, in UtilityStop via) => far.HasColor ? far.Color : via.Color;

        private static Color Transparent(Color c) => new Color(c.r, c.g, c.b, 0f);

        // The memoised TryParseActivator. The prefix gate runs first, so a class that is no activator is
        // turned away without a lookup or an entry.
        private static bool TryReadActivator(string cls, out Shape shape)
        {
            shape = default;
            if (!MatchesActivatorPrefix(cls))
            {
                return false;
            }
            if (!s_activators.TryGet(cls, out var reading))
            {
                reading.Ok = TryParseActivator(cls, out reading.Shape);
                s_activators.Add(cls, reading);
            }
            shape = reading.Shape;
            return reading.Ok;
        }

        // A from-/via-/to- remainder is EITHER a stop position (a percentage, or a bracketed pixel length) or a colour — independent
        // utilities. A percentage sets only the position; a recognized colour sets the colour (and marks
        // the stop present); anything else is ignored (leaves the accumulated values untouched).
        private static void ReadStop(string cls, int prefixLength, ref UtilityStop stop)
        {
            if (!s_stopTokens.TryGet(cls, out var token))
            {
                token = ParseStopToken(cls.Substring(prefixLength));
                s_stopTokens.Add(cls, token);
            }
            if (token.IsPosition)
            {
                stop.Position = token.Position;
                stop.PositionPx = token.PositionPx;
            }
            else if (token.IsColor)
            {
                stop.Color = token.Color;
                stop.HasColor = true;
            }
        }

        // A /modifier as Tailwind reads it: an interpolation space, or a hue method alone, which is oklch's.
        private static bool TryParseModifier(string modifier, out GradientInterp interp, out HueMethod hue)
        {
            if (TryParseHueMethod(modifier, out hue))
            {
                interp = GradientInterp.Oklch;
                return true;
            }
            hue = HueMethod.Shorter;
            return TryParseInterp(modifier, out interp);
        }

        // An interpolation space as the /modifier and a list's in_{space} both name it.
        private static bool TryParseInterp(string space, out GradientInterp interp)
        {
            switch (space)
            {
                case "srgb": interp = GradientInterp.Srgb; return true;
                case "srgb-linear": interp = GradientInterp.SrgbLinear; return true;
                case "oklab": interp = GradientInterp.Oklab; return true;
                case "oklch": interp = GradientInterp.Oklch; return true;
                case "lab": interp = GradientInterp.Lab; return true;
                case "lch": interp = GradientInterp.Lch; return true;
                case "hsl": interp = GradientInterp.Hsl; return true;
                default: interp = GradientInterp.Srgb; return false;
            }
        }

        private static bool TryParseHueMethod(string token, out HueMethod hue)
        {
            switch (token)
            {
                case "shorter": hue = HueMethod.Shorter; return true;
                case "longer": hue = HueMethod.Longer; return true;
                case "increasing": hue = HueMethod.Increasing; return true;
                case "decreasing": hue = HueMethod.Decreasing; return true;
                default: hue = HueMethod.Shorter; return false;
            }
        }

        // Tailwind's bare position: a non-negative integer written without sign, leading zero or fraction,
        // then '%'. Anything else has to be written in brackets.
        private static bool IsBarePercent(string suffix)
        {
            var digits = suffix.Length - 1;
            if (digits < 1 || suffix[digits] != '%' || (suffix[0] == '0' && digits > 1))
            {
                return false;
            }
            for (var i = 0; i < digits; i++)
            {
                if (suffix[i] < '0' || suffix[i] > '9')
                {
                    return false;
                }
            }
            return true;
        }

        // A bracketed length for a from-/via-/to- position, read as a list-bracket position is. A bare 0 is
        // declined: Tailwind's length test requires a unit.
        private static bool TryParseBracketedPixels(string suffix, out float px)
        {
            px = 0f;
            return suffix.Length > 2 && suffix[0] == '[' && suffix[suffix.Length - 1] == ']'
                && suffix != "[0]" && TryParseStopLength(suffix.Substring(1, suffix.Length - 2), out px);
        }

        // A stop position's length, with the unit StyleArbitraryValueResolver.TryParseValue reads (a bare
        // number is no length) converted to pixels, and signed: a stop may sit before the start
        // of the line, which the percentage forms allow too. A bare 0 is the start of the line.
        private static bool TryParseStopLength(string token, out float px)
        {
            px = 0f;
            if (token == "0")
            {
                return true;
            }
            return token.Length > 0 && char.IsLetter(token[token.Length - 1])
                && StyleArbitraryValueResolver.TryParseValue(token.AsSpan(), out px, out var unit)
                && unit == LengthUnit.Pixel;
        }

        private static StopToken ParseStopToken(string suffix)
        {
            if ((suffix.Length > 0 && suffix[0] == '[' || IsBarePercent(suffix)) && TryParsePercent(suffix, out var p))
            {
                return new StopToken { IsPosition = true, Position = p, PositionPx = float.NaN };
            }
            if (TryParseBracketedPixels(suffix, out var px))
            {
                return new StopToken { IsPosition = true, Position = float.NaN, PositionPx = px };
            }
            return VelvetPalette.TryResolveColorToken(suffix, out var c)
                ? new StopToken { IsColor = true, Color = c }
                : default;
        }

        // Parses a gradient shape activator, including an optional /interp modifier: type, angle (linear
        // axis / conic start; 0 for radial), centre (radial/conic; 0.5,0.5 default), interp, and the stop
        // list its brackets carry, if any. False when the class is not a recognized activator (incl. an
        // unknown /modifier, a malformed stop list, or a modifier after a stop list).
        private static bool TryParseActivator(string cls, out Shape shape)
        {
            shape = new Shape { CenterX = 0.5f, CenterY = 0.5f };

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
            var slash = ModifierSlash(cls);
            if (slash >= 0)
            {
                if (!TryParseModifier(cls.Substring(slash + 1), out shape.Interp, out shape.Hue))
                {
                    return false; // unknown modifier → not a valid activator
                }
                baseTok = cls.Substring(0, slash);
            }
            else if (cls.IndexOf('[') < 0)
            {
                // Tailwind's utilities name OKLab unless a modifier says otherwise; an arbitrary bracket names no
                // space, which CSS reads as sRGB for the legacy colours a bracket carries.
                shape.Interp = GradientInterp.Oklab;
            }

            if (TryGetStopListBody(baseTok, out var listType, out var listBody))
            {
                shape.Type = listType;
                var listShape = shape;
                if (TryParseStopList(listBody, ref listShape))
                {
                    // A bracketed shape takes no modifier: the interpolation space is named inside it.
                    shape = listShape;
                    return slash < 0;
                }
                return false;
            }
            if (baseTok == RadialActivator)
            {
                shape.Type = GradientType.Radial;
                return true;
            }
            if (baseTok.StartsWith(RadialArbitraryActivator, StringComparison.Ordinal) && baseTok[baseTok.Length - 1] == ']')
            {
                shape.Type = GradientType.Radial;
                return TryParseRadialLine(
                    baseTok.Substring(RadialActivator.Length + 2, baseTok.Length - RadialActivator.Length - 3), ref shape);
            }
            if (baseTok == ConicActivator)
            {
                shape.Type = GradientType.Conic;
                return true;
            }
            if (baseTok.StartsWith(ConicSuffixActivator, StringComparison.Ordinal))
            {
                shape.Type = GradientType.Conic;
                return TryParseConicStart(baseTok.Substring(ConicActivator.Length + 1), out shape.Angle);
            }
            return TryParseAngle(baseTok, out shape.Angle, out shape.ToCorner);
        }

        // The index of the modifier's '/', or -1: only one after the closing bracket is a modifier, and one
        // inside the brackets belongs to the bracket's body.
        internal static int ModifierSlash(string cls)
        {
            var slash = cls.LastIndexOf('/');
            return slash < cls.LastIndexOf(']') ? -1 : slash;
        }

        // A bracket body holding a comma is a CSS gradient argument list, which this file reads as a stop
        // list; any other body keeps the angle / position grammar the shape had before stop lists existed.
        private static bool TryGetStopListBody(string baseTok, out GradientType type, out string body)
        {
            type = GradientType.Linear;
            body = string.Empty;
            if (baseTok[baseTok.Length - 1] != ']' || !baseTok.Contains(','))
            {
                return false;
            }
            int open;
            if (baseTok.StartsWith(LinearListActivator, StringComparison.Ordinal))
            {
                open = LinearListActivator.Length;
            }
            else if (baseTok.StartsWith(RadialArbitraryActivator, StringComparison.Ordinal))
            {
                type = GradientType.Radial;
                open = RadialArbitraryActivator.Length;
            }
            else if (baseTok.StartsWith(ConicListActivator, StringComparison.Ordinal))
            {
                type = GradientType.Conic;
                open = ConicListActivator.Length;
            }
            else
            {
                // MUTANT_SURVIVES(equivalent): a token reaching here is rejected whichever value this returns.
                // True hands TryParseStopList an empty body, which it rejects; false sends the token on to
                // the angle and position grammars, none of which parses a comma.
                return false;
            }
            body = baseTok.Substring(open, baseTok.Length - open - 1);
            return true;
        }

        // The argument list of linear-/radial-/conic-gradient(), underscores standing for spaces: an optional
        // leading line argument for the shape, then 2..MaxStops colour stops. Any argument that is neither
        // rejects the whole list.
        private static bool TryParseStopList(string body, ref Shape shape)
        {
            var args = SplitTopLevel(body, ',');
            for (var i = 0; i < args.Count; i++)
            {
                // Each _ is a space, so the ones around and doubled within an argument are padding:
                // pasted CSS writes a space after every comma.
                args[i] = string.Join("_", args[i].Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries));
            }
            var raw = new List<GradientStop>();
            if (shape.Type == GradientType.Linear)
            {
                // CSS's default direction, to bottom, for a list with no line argument.
                shape.Angle = 180f;
            }
            var afterStop = TryAddColorStop(args[0], shape.Type, raw);
            if (!afterStop && !TryParseLine(args[0], ref shape))
            {
                return false;
            }
            for (var i = 1; i < args.Count; i++)
            {
                if (TryAddColorStop(args[i], shape.Type, raw))
                {
                    afterStop = true;
                }
                else if (afterStop && i < args.Count - 1 && TryParseStopPosition(args[i], shape.Type, out var hint, out var hintPx))
                {
                    // A bare position between two stops is a colour hint on the stop before it.
                    raw[raw.Count - 1] = raw[raw.Count - 1].WithHint(hint, hintPx);
                    afterStop = false;
                }
                else
                {
                    return false;
                }
            }
            if (raw.Count < 2 || raw.Count > GradientSpec.MaxStops)
            {
                return false;
            }
            shape.Raw = raw.ToArray();
            shape.Stops = Place(shape.Raw);
            return true;
        }

        // Splits on separator wherever it sits outside a (...) group, so rgb(255,0,0) stays one argument.
        internal static List<string> SplitTopLevel(string s, char separator)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                }
                else if (c == separator && depth == 0)
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(s.Substring(start));
            return parts;
        }

        // One colour stop: a colour, then none, one or two positions (CSS Images 4's two-position form is two
        // stops of one colour). An unpositioned stop is recorded as NaN for FixUp to place. Adds nothing when
        // the argument is not a colour stop.
        private static bool TryAddColorStop(string arg, GradientType type, List<GradientStop> stops)
        {
            var tokens = SplitTopLevel(arg, '_');
            if (tokens.Count > 3)
            {
                return false;
            }
            if (!TryParseListColor(tokens[0], out var color))
            {
                return false;
            }
            float first = float.NaN, firstPx = float.NaN, second = float.NaN, secondPx = float.NaN;
            if (tokens.Count > 1 && !TryParseStopPosition(tokens[1], type, out first, out firstPx))
            {
                return false;
            }
            if (tokens.Count > 2 && !TryParseStopPosition(tokens[2], type, out second, out secondPx))
            {
                return false;
            }
            stops.Add(new GradientStop(color, first, firstPx));
            if (tokens.Count > 2)
            {
                stops.Add(new GradientStop(color, second, secondPx));
            }
            return true;
        }

        // A stop or hint position: a percentage, a conic's angle (a fraction of the turn), or any other
        // shape's length in pixels, which waits for the box. A bare 0 is the start of the line.
        private static bool TryParseStopPosition(string token, GradientType type, out float fraction, out float px)
        {
            px = float.NaN;
            if (token == "0")
            {
                fraction = 0f;
                return true;
            }
            if (TryParsePercent(token, out fraction))
            {
                return true;
            }
            if (type == GradientType.Conic)
            {
                var ok = TryParseCssAngle(token, out var deg);
                fraction = deg / 360f;
                return ok;
            }
            fraction = float.NaN;
            return TryParseStopLength(token, out px);
        }

        // A palette name or [bracketed] value as from-/via-/to- take it, or a bare CSS colour, which is how a
        // CSS argument list spells one.
        private static bool TryParseListColor(string token, out Color color)
        {
            if (VelvetPalette.TryResolveColorToken(token, out color))
            {
                return true;
            }
            return StyleColorValueParser.TryParseColor(token, out color);
        }

        // The leading line argument: an angle or to_{side}[_{side}] for linear, a shape, size and at_{position} for
        // radial, from_{angle} and/or at_{position} for conic, each optionally led or followed by
        // in_{space}[_{method}_hue].
        private static bool TryParseLine(string arg, ref Shape shape)
        {
            var tokens = arg.Split('_');
            var at = Array.IndexOf(tokens, "in");
            if (at < 0)
            {
                return TryParseShapeLine(arg, ref shape);
            }
            if (at + 1 >= tokens.Length || !TryParseInterp(tokens[at + 1], out shape.Interp))
            {
                return false;
            }
            // The space, and the hue method that may follow a polar one.
            var length = 2;
            shape.Hue = HueMethod.Shorter;
            if (at + 3 < tokens.Length && tokens[at + 3] == "hue")
            {
                if (!GradientSpec.IsPolarSpace(shape.Interp) || !TryParseHueMethod(tokens[at + 2], out shape.Hue))
                {
                    return false;
                }
                length = 4;
            }
            if (at != 0 && at + length != tokens.Length)
            {
                return false;
            }
            var line = at == 0
                ? string.Join("_", tokens, length, tokens.Length - length)
                : string.Join("_", tokens, 0, at);
            return line.Length == 0 || TryParseShapeLine(line, ref shape);
        }

        private static bool TryParseShapeLine(string line, ref Shape shape)
        {
            switch (shape.Type)
            {
                case GradientType.Radial:
                    return TryParseRadialLine(line, ref shape);
                case GradientType.Conic:
                    return TryParseConicLine(line, ref shape);
                default:
                    return line.StartsWith("to_", StringComparison.Ordinal)
                        ? TryParseSides(line.Substring(3), out shape.Angle, out shape.ToCorner)
                        : TryParseCssAngle(line, out shape.Angle);
            }
        }

        // to_{side} or to_{side}_{side} in either order → the angle of the matching bg-gradient-to-{dir}.
        private static bool TryParseSides(string sides, out float angleDeg, out bool toCorner)
        {
            angleDeg = 0f;
            var vertical = string.Empty;
            var horizontal = string.Empty;
            foreach (var side in sides.Split('_'))
            {
                switch (side)
                {
                    case "top" when vertical.Length == 0: vertical = "t"; break;
                    case "bottom" when vertical.Length == 0: vertical = "b"; break;
                    case "left" when horizontal.Length == 0: horizontal = "l"; break;
                    case "right" when horizontal.Length == 0: horizontal = "r"; break;
                    default: toCorner = false; return false;
                }
            }
            return TryDirectionAngle(vertical + horizontal, out angleDeg, out toCorner);
        }

        private static bool TryParseConicLine(string arg, ref Shape shape)
        {
            var rest = arg;
            if (rest.StartsWith("from_", StringComparison.Ordinal))
            {
                // "from", the angle, and whatever follows it.
                var parts = rest.Split(new[] { '_' }, 3);
                if (!TryParseCssAngle(parts[1], out shape.Angle))
                {
                    return false;
                }
                if (parts.Length < 3)
                {
                    return true;
                }
                rest = parts[2];
            }
            return TryParseListPosition(rest, out shape.CenterX, out shape.CenterY);
        }

        // The head of radial-gradient(): an optional shape and size, then an optional at_{position}, in any
        // order of shape and size. Writes the shape only when the whole line reads.
        private static bool TryParseRadialLine(string line, ref Shape shape)
        {
            var tokens = line.Split('_');
            var at = Array.IndexOf(tokens, "at");
            float centerX = shape.CenterX, centerY = shape.CenterY;
            if (at >= 0 && !TryParseListPosition(string.Join("_", tokens, at, tokens.Length - at), out centerX, out centerY))
            {
                return false;
            }
            var head = at < 0 ? tokens : tokens.Take(at).ToArray();
            if (!TryParseRadialSizing(head, out var sizing))
            {
                return false;
            }
            shape.Radial = sizing;
            shape.CenterX = centerX;
            shape.CenterY = centerY;
            return true;
        }

        private static bool TryParseRadialSizing(string[] head, out RadialSizing sizing)
        {
            sizing = default;
            string? shapeToken = null;
            var rest = new List<string>();
            foreach (var token in head)
            {
                if (token is "circle" or "ellipse")
                {
                    if (shapeToken != null)
                    {
                        return false;
                    }
                    shapeToken = token;
                }
                else
                {
                    rest.Add(token);
                }
            }
            var circle = shapeToken == "circle";
            switch (rest.Count)
            {
                case 0:
                    sizing = new RadialSizing { Circle = circle };
                    return true;
                case 1 when TryParseExtent(rest[0], out var extent):
                    sizing = new RadialSizing { Circle = circle, Extent = extent };
                    return true;
                case 1 when shapeToken != "ellipse" && TryParsePixelLength(rest[0], out var radius):
                    sizing = new RadialSizing { Circle = true, Extent = RadialExtent.Explicit, X = radius, Y = radius };
                    return true;
                case 2 when !circle && TryParseLengthPercentage(rest[0], out var x, out var xPercent)
                    && TryParseLengthPercentage(rest[1], out var y, out var yPercent):
                    sizing = new RadialSizing
                    {
                        Extent = RadialExtent.Explicit, X = x, Y = y, XPercent = xPercent, YPercent = yPercent,
                    };
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseExtent(string token, out RadialExtent extent)
        {
            switch (token)
            {
                case "closest-side": extent = RadialExtent.ClosestSide; return true;
                case "closest-corner": extent = RadialExtent.ClosestCorner; return true;
                case "farthest-side": extent = RadialExtent.FarthestSide; return true;
                case "farthest-corner": extent = RadialExtent.FarthestCorner; return true;
                default: extent = RadialExtent.FarthestCorner; return false;
            }
        }

        // A non-negative length, as a radius is: TryParseStopLength's grammar without the sign.
        private static bool TryParsePixelLength(string token, out float px)
            => TryParseStopLength(token, out px) && px >= 0f;

        // A non-negative length or percentage: pixels as written, a percentage as a fraction of the box.
        private static bool TryParseLengthPercentage(string token, out float value, out bool percent)
        {
            percent = token.EndsWith("%", StringComparison.Ordinal);
            if (percent)
            {
                var ok = TryParsePercent(token, out value) && value >= 0f;
                return ok;
            }
            return TryParsePixelLength(token, out value);
        }

        // An angle as CSS writes one: a number with a deg, grad, rad or turn unit, or a bare 0. Any other
        // bare number is no angle in CSS.
        private static bool TryParseCssAngle(string s, out float deg)
        {
            deg = 0f;
            foreach (var (unit, degrees) in AngleUnits)
            {
                if (s.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                {
                    var ok = float.TryParse(s.Substring(0, s.Length - unit.Length), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var value) && !float.IsNaN(value) && !float.IsInfinity(value);
                    deg = value * degrees;
                    return ok;
                }
            }
            return s == "0";
        }

        private static readonly (string Unit, float Degrees)[] AngleUnits =
        {
            ("deg", 1f), ("grad", 0.9f), ("rad", 180f / Mathf.PI), ("turn", 360f),
        };

        // An at_{position} the list reads: one or two of left, right, top, bottom, center and percentages,
        // after the at. A token it does not know rejects the list, as it does in CSS.
        private static bool TryParseListPosition(string arg, out float centerX, out float centerY)
        {
            centerX = 0.5f;
            centerY = 0.5f;
            if (!arg.StartsWith("at_", StringComparison.Ordinal))
            {
                return false;
            }
            var tokens = arg.Substring(3).Split('_');
            if (tokens.Length > 2)
            {
                return false;
            }
            foreach (var token in tokens)
            {
                if (!IsPositionToken(token))
                {
                    return false;
                }
            }
            ParseRadialPosition(arg, ref centerX, ref centerY);
            return true;
        }

        private static bool IsPositionToken(string token)
            => token is "left" or "right" or "top" or "bottom" or "center" || TryParseCentrePercent(token, out _);

        // The stops as the spec keeps them: fixed up when every position is known, and as written when one is
        // in pixels, which GradientBackground.ResolveStops places once it knows the box.
        private static GradientStop[] Place(GradientStop[] raw)
        {
            foreach (var stop in raw)
            {
                if (stop.HasLength)
                {
                    return raw;
                }
            }
            return FixUp(raw);
        }

        // CSS Images 3 colour-stop fix-up, over positions where NaN means unpositioned: an unpositioned first
        // or last stop sits at 0% or 100%; a position or hint behind an earlier one is raised to the largest
        // before it; then each run of unpositioned stops is spread evenly between its positioned neighbours.
        internal static GradientStop[] FixUp(GradientStop[] raw)
        {
            var n = raw.Length;
            var positions = new float[n];
            var hints = new float[n];
            for (var i = 0; i < n; i++)
            {
                positions[i] = raw[i].Position;
                hints[i] = raw[i].Hint;
            }
            if (float.IsNaN(positions[0]))
            {
                positions[0] = 0f;
            }
            if (float.IsNaN(positions[n - 1]))
            {
                positions[n - 1] = 1f;
            }
            var largest = positions[0];
            for (var i = 0; i < n; i++)
            {
                if (i > 0 && !float.IsNaN(positions[i]))
                {
                    largest = Mathf.Max(largest, positions[i]);
                    positions[i] = largest;
                }
                if (!float.IsNaN(hints[i]))
                {
                    largest = Mathf.Max(largest, hints[i]);
                    hints[i] = largest;
                }
            }
            SpreadUnpositioned(positions);

            var stops = new GradientStop[n];
            for (var i = 0; i < n; i++)
            {
                stops[i] = new GradientStop(raw[i].Color, positions[i], float.NaN, hints[i]);
            }
            return stops;
        }

        // Ordering constraint: runs after the first and last positions are set, so every run of NaN ends
        // at a positioned stop and the inner loop stops there.
        private static void SpreadUnpositioned(float[] positions)
        {
            var previous = 0;
            for (var i = 1; i < positions.Length; i++)
            {
                if (float.IsNaN(positions[i]))
                {
                    continue;
                }
                for (var k = previous + 1; float.IsNaN(positions[k]); k++)
                {
                    positions[k] = Mathf.Lerp(positions[previous], positions[i], (k - previous) / (float)(i - previous));
                }
                previous = i;
            }
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

        // Reads the position tokens of an at_ position into a UV centre, which may lie outside the box.
        // Keywords (top/bottom/left/right/center) pin their own axis; a {N}% value fills the next axis not
        // already pinned by a keyword (CSS position order: x then y). Tracking which axes are set with
        // explicit flags (not a value sentinel on centerX) is what lets `at_50%_75%` and `at_left_50%`
        // resolve correctly. Callers have already checked every token with IsPositionToken.
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
                        if (TryParseCentrePercent(tok, out var frac))
                        {
                            if (!xSet) { centerX = frac; xSet = true; }
                            else if (!ySet) { centerY = frac; ySet = true; }
                        }
                        break;
                }
            }
        }

        private static bool TryParseCentrePercent(string token, out float frac)
        {
            frac = 0f;
            if (token.Length < 2 || token[token.Length - 1] != '%'
                || !float.TryParse(token.Substring(0, token.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)
                || float.IsNaN(pct) || float.IsInfinity(pct))
            {
                return false;
            }
            frac = pct / 100f;
            return true;
        }

        // "10%" or "[12.5%]" → 0.10 / 0.125, beyond 0..1 as written. False when not a percentage. Delegates to
        // the shared utility length grammar (the same one w-[…]-style arbitrary values use) so the numeric
        // parse can never drift from it; the explicit LengthUnit.Percent check is what keeps this a
        // percentage-only grammar even though the shared parser also accepts px/rem/bare-number lengths.
        private static bool TryParsePercent(string s, out float frac)
        {
            frac = 0f;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            var body = StyleArbitraryValueResolver.TryStripBrackets(s, 0, out var inner) ? inner : s.AsSpan();
            if (!StyleArbitraryValueResolver.TryParseValue(body, out var v, out var unit)
                || unit != LengthUnit.Percent)
            {
                return false;
            }
            frac = v / 100f;
            return true;
        }

        // Resolves a linear gradient axis activator to a CSS angle (degrees): bg-gradient-to-{dir},
        // bg-linear-to-{dir} (v4 alias), bg-linear-{deg} / -bg-linear-{deg}, bg-linear-[{deg}deg].
        private static bool TryParseAngle(string cls, out float angleDeg, out bool toCorner)
        {
            angleDeg = 0f;
            toCorner = false;
            var negative = cls.Length > 0 && cls[0] == '-';
            var body = negative ? cls.Substring(1) : cls;

            if (body.StartsWith(DirActivator, StringComparison.Ordinal))
            {
                return !negative && TryDirectionAngle(body.Substring(DirActivator.Length), out angleDeg, out toCorner);
            }
            if (body.StartsWith(LinearActivator, StringComparison.Ordinal))
            {
                var rest = body.Substring(LinearActivator.Length);
                if (rest.StartsWith("to-", StringComparison.Ordinal))
                {
                    return !negative && TryDirectionAngle(rest.Substring(3), out angleDeg, out toCorner);
                }
                if (TryParseAngleValue(rest, out var deg))
                {
                    angleDeg = negative ? -deg : deg;
                    return true;
                }
            }
            return false;
        }

        // One of the 8 named directions → its CSS angle (0 = to top, clockwise), and whether it is a corner,
        // which CSS lays out over the physical box rather than at the angle.
        private static bool TryDirectionAngle(string dir, out float angleDeg, out bool toCorner)
        {
            toCorner = dir.Length == 2;
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
                default: angleDeg = 0f; toCorner = false; return false;
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
            if (StyleArbitraryValueResolver.TryStripBrackets(s, 0, out var inner))
            {
                if (inner.EndsWith("deg", StringComparison.Ordinal))
                {
                    inner = inner.Slice(0, inner.Length - 3);
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
