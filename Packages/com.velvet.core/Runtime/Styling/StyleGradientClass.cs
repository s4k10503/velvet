using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

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

    // One colour stop of a resolved gradient: its colour and its position along the gradient line (0..1).
    internal readonly struct GradientStop
    {
        public GradientStop(Color color, float position)
        {
            Color = color;
            Position = position;
        }

        public Color Color { get; }
        public float Position { get; }
    }

    // A resolved gradient: shape (linear angle / radial-or-conic centre), interpolation space, and its ordered
    // colour stops (positions 0..1). A stop list's positions are fixed up into a non-decreasing run; the
    // from-/via-/to- utilities' positions are kept as written, and GradientBackground.ColorAt paints them
    // as it did before stop lists existed.
    // Stops arrays are shared between specs through StyleGradientClass's memos and are never written.
    // AngleDeg is the linear axis angle (CSS degrees, 0 = to top, clockwise) and doubles as the conic start
    // angle. Equality is value-based (quantized) so the baked-texture cache and the reconciler binding skip
    // redundant work when an unchanged class list re-resolves to the same gradient.
    internal readonly struct GradientSpec : IEquatable<GradientSpec>
    {
        // The skew silhouette shader declares its stop arrays at this length, so the parser rejects a longer
        // list rather than let the skewed paint drop stops the straight paint draws. GradientStopCapacityTests
        // holds the shader's length to this one.
        public const int MaxStops = 16;

        public GradientType Type { get; init; }
        public float AngleDeg { get; init; }
        public float CenterX { get; init; }
        public float CenterY { get; init; }
        public GradientInterp Interp { get; init; }
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
            return StopsEqual(Stops ?? Array.Empty<GradientStop>(), other.Stops ?? Array.Empty<GradientStop>());
        }

        private static bool StopsEqual(GradientStop[] a, GradientStop[] b)
        {
            var same = a.Length == b.Length;
            for (var i = 0; same && i < a.Length; i++)
            {
                same = ColorKey(a[i].Color) == ColorKey(b[i].Color) && PosKey(a[i].Position) == PosKey(b[i].Position);
            }
            return same;
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
                foreach (var stop in Stops ?? Array.Empty<GradientStop>())
                {
                    h = HashCode.Combine(h, ColorKey(stop.Color), PosKey(stop.Position));
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
        private const string LinearListActivator = LinearActivator + "[";
        private const string ConicListActivator = ConicSuffixActivator + "[";

        // A shape activator's reading. Stops is set only by an activator whose brackets carry a stop list.
        private struct Shape
        {
            public GradientType Type;
            public float Angle;
            public float CenterX;
            public float CenterY;
            public GradientInterp Interp;
            public GradientStop[]? Stops;
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
        }

        // A from-/via-/to- suffix's reading: a position, a colour, or neither.
        private struct StopToken
        {
            public bool IsPosition;
            public float Position;
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
        private static readonly BoundedMemo<((bool, Color, float), (bool, Color, float), (bool, Color, float)), GradientStop[]>
            s_utilityStops = new();

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
        // When the winning activator carries a stop list, that list is the gradient's stops and the
        // from-/via-/to- utilities are not read. Otherwise returns false when neither a from nor a to COLOR
        // is given (positions alone draw nothing), and a missing from/to colour defaults to the transparent
        // version of the other stop (the default behavior). Returns false when no shape activator is present.
        public static bool TryExtract(string[] classNames, out GradientSpec spec)
        {
            spec = default;
            if (classNames == null)
            {
                return false;
            }

            var hasShape = false;
            var shape = default(Shape);
            var fromStop = new UtilityStop { Position = 0f };
            var viaStop = new UtilityStop { Position = 0.5f };
            var toStop = new UtilityStop { Position = 1f };

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

            var stops = hasShape ? shape.Stops ?? StopsFromUtilities(in fromStop, in viaStop, in toStop) : null;
            if (stops == null)
            {
                return false;
            }

            spec = new GradientSpec
            {
                Type = shape.Type,
                AngleDeg = shape.Angle,
                CenterX = shape.CenterX,
                CenterY = shape.CenterY,
                Interp = shape.Interp,
                Stops = stops,
            };
            return true;
        }

        // The positions are kept as written, not fixed up: see GradientSpec.
        private static GradientStop[]? StopsFromUtilities(in UtilityStop fromStop, in UtilityStop viaStop, in UtilityStop toStop)
        {
            if (!fromStop.HasColor && !toStop.HasColor)
            {
                return null;
            }
            var key = ((fromStop.HasColor, fromStop.Color, fromStop.Position), (viaStop.HasColor, viaStop.Color,
                viaStop.Position), (toStop.HasColor, toStop.Color, toStop.Position));
            if (s_utilityStops.TryGet(key, out var cached))
            {
                return cached;
            }
            var first = new GradientStop(fromStop.HasColor ? fromStop.Color : Transparent(toStop.Color), fromStop.Position);
            var last = new GradientStop(toStop.HasColor ? toStop.Color : Transparent(fromStop.Color), toStop.Position);
            var stops = viaStop.HasColor
                ? new[] { first, new GradientStop(viaStop.Color, viaStop.Position), last }
                : new[] { first, last };
            s_utilityStops.Add(key, stops);
            return stops;
        }

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

        // A from-/via-/to- remainder is EITHER a stop position (a percentage) or a colour — independent
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
            }
            else if (token.IsColor)
            {
                stop.Color = token.Color;
                stop.HasColor = true;
            }
        }

        // An interpolation space as the /modifier and a list's in_{space} both name it.
        private static bool TryParseInterp(string space, out GradientInterp interp)
        {
            switch (space)
            {
                case "oklch":
                case "oklab": interp = GradientInterp.Oklab; return true;
                case "srgb": interp = GradientInterp.Srgb; return true;
                default: interp = GradientInterp.Srgb; return false;
            }
        }

        private static StopToken ParseStopToken(string suffix)
        {
            if (TryParsePercent(suffix, out var p))
            {
                return new StopToken { IsPosition = true, Position = p };
            }
            return VelvetPalette.TryResolveColorToken(suffix, out var c)
                ? new StopToken { IsColor = true, Color = c }
                : default;
        }

        // Parses a gradient shape activator, including an optional /interp modifier: type, angle (linear
        // axis / conic start; 0 for radial), centre (radial/conic; 0.5,0.5 default), interp, and the stop
        // list its brackets carry, if any. False when the class is not a recognized activator (incl. an
        // unknown /modifier or a malformed stop list).
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
            var slash = cls.IndexOf('/');
            if (slash >= 0)
            {
                if (!TryParseInterp(cls.Substring(slash + 1), out shape.Interp))
                {
                    return false; // unknown modifier → not a valid activator
                }
                baseTok = cls.Substring(0, slash);
            }

            if (TryGetStopListBody(baseTok, out var listType, out var listBody))
            {
                shape.Type = listType;
                var listShape = shape;
                if (TryParseStopList(listBody, ref listShape))
                {
                    shape = listShape;
                    return true;
                }
                if (listType != GradientType.Radial)
                {
                    return false;
                }
                // A radial body that is no stop list keeps the reading it had before stop lists existed —
                // a position, its unknown tokens ignored — so the from-/via-/to- gradient it drew still draws.
                ParseRadialPosition(listBody, ref shape.CenterX, ref shape.CenterY);
                return true;
            }
            if (baseTok == RadialActivator)
            {
                shape.Type = GradientType.Radial;
                return true;
            }
            // Deliberately NOT StyleArbitraryValueResolver.TryStripBrackets: unlike every other bracket
            // grammar in this file, an empty or unrecognized bg-radial-[...] body is not an error here — it
            // is exactly as meaningful as a plain `bg-radial` (ParseRadialPosition no-ops on any token it
            // does not recognize, leaving the centre at its 0.5,0.5 default) — so rejecting an empty body
            // would make bg-radial-[] behave differently from bg-radial-[bogus], which is not the boundary
            // this class draws anywhere else.
            if (baseTok.StartsWith(RadialArbitraryActivator, StringComparison.Ordinal) && baseTok[baseTok.Length - 1] == ']')
            {
                shape.Type = GradientType.Radial;
                ParseRadialPosition(baseTok.Substring(RadialActivator.Length + 2, baseTok.Length - RadialActivator.Length - 3),
                    ref shape.CenterX, ref shape.CenterY);
                return true;
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
            return TryParseAngle(baseTok, out shape.Angle);
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
            var colors = new List<Color>();
            var positions = new List<float>();
            if (shape.Type == GradientType.Linear)
            {
                // CSS's default direction, to bottom, for a list with no line argument.
                shape.Angle = 180f;
            }
            if (!TryAddColorStop(args[0], colors, positions) && !TryParseLine(args[0], ref shape))
            {
                return false;
            }
            for (var i = 1; i < args.Count; i++)
            {
                if (!TryAddColorStop(args[i], colors, positions))
                {
                    return false;
                }
            }
            if (colors.Count < 2 || colors.Count > GradientSpec.MaxStops)
            {
                return false;
            }
            shape.Stops = FixUp(colors, positions);
            return true;
        }

        // Splits on separator wherever it sits outside a (...) group, so rgb(255,0,0) stays one argument.
        private static List<string> SplitTopLevel(string s, char separator)
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

        // One colour stop: a colour, then none, one or two percentage positions (CSS Images 4's two-position
        // form is two stops of one colour). An unpositioned stop is recorded as NaN for FixUp to place.
        // Adds nothing when the argument is not a colour stop.
        private static bool TryAddColorStop(string arg, List<Color> colors, List<float> positions)
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
            var first = float.NaN;
            var second = float.NaN;
            if (tokens.Count > 1 && !TryParsePercent(tokens[1], out first))
            {
                return false;
            }
            if (tokens.Count > 2 && !TryParsePercent(tokens[2], out second))
            {
                return false;
            }
            colors.Add(color);
            positions.Add(first);
            if (tokens.Count > 2)
            {
                colors.Add(color);
                positions.Add(second);
            }
            return true;
        }

        // A palette name or [bracketed] value as from-/via-/to- take it, or a bare CSS colour (#hex, rgb(),
        // a named colour), which is how a CSS argument list spells one.
        private static bool TryParseListColor(string token, out Color color)
        {
            if (VelvetPalette.TryResolveColorToken(token, out color))
            {
                return true;
            }
            return StyleColorValueParser.TryParseColor(token, out color);
        }

        // The leading line argument: an angle or to_{side}[_{side}] for linear, at_{position} for radial,
        // from_{angle} and/or at_{position} for conic, each optionally led or followed by in_{space}. An
        // in_{space} overrides the activator's /modifier.
        private static bool TryParseLine(string arg, ref Shape shape)
        {
            var tokens = arg.Split('_');
            var at = Array.IndexOf(tokens, "in");
            if (at < 0)
            {
                return TryParseShapeLine(arg, ref shape);
            }
            if (at + 1 >= tokens.Length || (at != 0 && at != tokens.Length - 2)
                || !TryParseInterp(tokens[at + 1], out shape.Interp))
            {
                return false;
            }
            var line = at == 0 ? string.Join("_", tokens, 2, tokens.Length - 2) : string.Join("_", tokens, 0, at);
            return line.Length == 0 || TryParseShapeLine(line, ref shape);
        }

        private static bool TryParseShapeLine(string line, ref Shape shape)
        {
            switch (shape.Type)
            {
                case GradientType.Radial:
                    if (!line.StartsWith("at_", StringComparison.Ordinal))
                    {
                        return false;
                    }
                    ParseRadialPosition(line, ref shape.CenterX, ref shape.CenterY);
                    return true;
                case GradientType.Conic:
                    return TryParseConicLine(line, ref shape);
                default:
                    return line.StartsWith("to_", StringComparison.Ordinal)
                        ? TryParseSides(line.Substring(3), out shape.Angle)
                        : TryParseAngleValue("[" + line + "]", out shape.Angle);
            }
        }

        // to_{side} or to_{side}_{side} in either order → the angle of the matching bg-gradient-to-{dir}.
        private static bool TryParseSides(string sides, out float angleDeg)
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
                    default: return false;
                }
            }
            return TryDirectionAngle(vertical + horizontal, out angleDeg);
        }

        private static bool TryParseConicLine(string arg, ref Shape shape)
        {
            var rest = arg;
            if (rest.StartsWith("from_", StringComparison.Ordinal))
            {
                // "from", the angle, and whatever follows it.
                var parts = rest.Split(new[] { '_' }, 3);
                if (!TryParseAngleValue("[" + parts[1] + "]", out shape.Angle))
                {
                    return false;
                }
                if (parts.Length < 3)
                {
                    return true;
                }
                rest = parts[2];
            }
            if (!rest.StartsWith("at_", StringComparison.Ordinal))
            {
                return false;
            }
            ParseRadialPosition(rest, ref shape.CenterX, ref shape.CenterY);
            return true;
        }

        // CSS Images 3 colour-stop fix-up, over positions where NaN means unpositioned: an unpositioned first
        // or last stop sits at 0% or 100%; a position behind an earlier one is raised to the largest before
        // it; then each run of unpositioned stops is spread evenly between its positioned neighbours.
        private static GradientStop[] FixUp(List<Color> colors, List<float> positions)
        {
            var n = positions.Count;
            if (float.IsNaN(positions[0]))
            {
                positions[0] = 0f;
            }
            if (float.IsNaN(positions[n - 1]))
            {
                positions[n - 1] = 1f;
            }
            var largest = positions[0];
            for (var i = 1; i < n; i++)
            {
                if (float.IsNaN(positions[i]))
                {
                    continue;
                }
                largest = Mathf.Max(largest, positions[i]);
                positions[i] = largest;
            }
            SpreadUnpositioned(positions);

            var stops = new GradientStop[n];
            for (var i = 0; i < n; i++)
            {
                stops[i] = new GradientStop(colors[i], positions[i]);
            }
            return stops;
        }

        // Ordering constraint: runs after the first and last positions are set, so every run of NaN ends
        // at a positioned stop and the inner loop stops there.
        private static void SpreadUnpositioned(List<float> positions)
        {
            var previous = 0;
            for (var i = 1; i < positions.Count; i++)
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

        // "10%" or "[12.5%]" → 0.10 / 0.125 (clamped to 0..1). False when not a percentage. Delegates to
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
