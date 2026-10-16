using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // The pixels one of each font-relative unit comes to on one element: its em, x-height, "0" advance, cap height,
    // "水" advance and line height.
    internal readonly record struct FontUnitPixels(float Em, float Ex, float Ch, float Cap, float Ic, float Lh)
    {
        // The values CSS Values 4 assigns where a font gives no metric: 0.5em for ex and ch and 1em for ic. Where
        // a font gives no ascent either, the cap height is the em, and a `normal` line height 1.2em.
        public static FontUnitPixels WithoutMetrics(float em) => new(em, 0.5f * em, 0.5f * em, em, em, 1.2f * em);
    }

    // What a relative length is measured against, in pixels: the element's font units, the root's, 100% and the
    // panel's two sides.
    internal readonly struct RelativeLengthBasis
    {
        public readonly FontUnitPixels Font;
        public readonly FontUnitPixels Root;
        public readonly float Percent;
        public readonly float ViewportWidth;
        public readonly float ViewportHeight;

        public RelativeLengthBasis(float em, float percent, float viewportWidth, float viewportHeight)
            : this(FontUnitPixels.WithoutMetrics(em), FontUnitPixels.WithoutMetrics(StyleLengthExpression.RootFontSize),
                percent, viewportWidth, viewportHeight)
        {
        }

        public RelativeLengthBasis(in FontUnitPixels font, in FontUnitPixels root, float percent, float viewportWidth,
            float viewportHeight)
        {
            Font = font;
            Root = root;
            Percent = percent;
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
        }

        public float Em => Font.Em;
    }

    // A bracketed CSS <length-percentage>: a single dimension, or a math function of CSS Values 4 over numbers,
    // dimensions, the constants and nested parentheses, with `_` read as a space. The text is first spaced as
    // TailwindMathSpacing writes it, and then read as CSS reads it: a binary + or - needs a space on both sides.
    // Every term carries its CSS type, a power of length, angle and time, and the result has to be a length (or,
    // through TryParseAngle and TryParseNumber, an angle or a number). px, rem (at the fixed 16px the rest of the
    // bracket grammar uses) and the absolute units are pixels; %, the font-relative and the viewport units stay
    // symbolic until Evaluate is handed a basis.
    internal abstract class StyleLengthExpression
    {
        // The font size a rem and the root-relative font units are taken at.
        public const float RootFontSize = 16f;

        // What a term reads, for TryFoldConstant.
        private const int ReadsPixels = 1;
        private const int ReadsPercent = 2;
        private const int ReadsElement = 4;

        // Nesting, and a run of terms or arguments, past this depth is declined rather than recursed into.
        private const int MaxDepth = 64;

        private static readonly Dictionary<string, (Func<RelativeLengthBasis, double> PerUnit, int Reads, CalcType Type)> s_units =
            BuildUnits();

        // Each math function, from its arguments and round()'s strategy to its result, or null where CSS's type
        // rules reject the arguments.
        private static readonly Dictionary<string, Func<List<StyleLengthExpression?>, string, StyleLengthExpression?>> s_functions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["calc"] = (args, _) => args.Count == 1 ? args[0] : null,
                ["min"] = (args, _) => Op.All(args, x => x, Math.Min, x => x),
                ["max"] = (args, _) => Op.All(args, x => x, Math.Max, x => x),
                // clamp(MIN, VAL, MAX) is max(MIN, min(VAL, MAX)).
                ["clamp"] = (args, _) => args.Count == 3
                    ? Op.All(new List<StyleLengthExpression?> { args[0], Op.All(args.GetRange(1, 2), x => x, Math.Min, x => x) },
                        x => x, Math.Max, x => x)
                    : null,
                ["hypot"] = (args, _) => Op.All(args, x => x * x, (acc, x) => acc + (x * x), Math.Sqrt),
                // B may be left out only where A is a number, and is then 1.
                ["round"] = (args, strategy) => Op.Same(
                    args.Count == 1 && args[0] is { } a && a._type.IsNumber
                        ? new List<StyleLengthExpression?> { a, new Constant(1d, 0, CalcType.Number) }
                        : args,
                    null, (x, y) => Op.Round(strategy, x, y)),
                ["mod"] = (args, _) => Op.Same(args, null, (x, y) => Op.Modulo(x, y, signOfB: true)),
                ["rem"] = (args, _) => Op.Same(args, null, (x, y) => Op.Modulo(x, y, signOfB: false)),
                ["atan2"] = (args, _) => Op.Same(args, CalcType.Angle, (y, x) => Math.Atan2(y, x) * Op.Degrees),
                ["pow"] = (args, _) => Op.Numbers(args) ? Op.Same(args, CalcType.Number, Math.Pow) : null,
                ["log"] = (args, _) => args.Count == 1 ? Op.One(args, Op.NumberOnly, Math.Log)
                    : Op.Numbers(args) ? Op.Same(args, CalcType.Number, (x, b) => Math.Log(x) / Math.Log(b))
                    : null,
                ["abs"] = (args, _) => Op.One(args, t => t, Math.Abs),
                ["sign"] = (args, _) => Op.One(args, _ => CalcType.Number, Op.Sign),
                ["sqrt"] = (args, _) => Op.One(args, Op.NumberOnly, Math.Sqrt),
                ["exp"] = (args, _) => Op.One(args, Op.NumberOnly, Math.Exp),
                ["sin"] = (args, _) => Op.Trig(args, Op.Sin),
                ["cos"] = (args, _) => Op.Trig(args, Op.Cos),
                ["tan"] = (args, _) => Op.Trig(args, Op.Tan),
                ["asin"] = (args, _) => Op.One(args, Op.ToAngle, x => Math.Asin(x) * Op.Degrees),
                ["acos"] = (args, _) => Op.One(args, Op.ToAngle, x => Math.Acos(x) * Op.Degrees),
                ["atan"] = (args, _) => Op.One(args, Op.ToAngle, x => Math.Atan(x) * Op.Degrees),
            };

        private readonly int _reads;
        private readonly CalcType _type;

        private StyleLengthExpression(int reads, CalcType type)
        {
            _reads = reads;
            _type = type;
        }

        // The value before CSS's top-level rules: a NaN or an infinity a term produced is carried as it is.
        private protected abstract double Raw(in RelativeLengthBasis basis);

        // What this comes to against basis, as the top level of a CSS calculation produces it: NaN as zero, and an
        // infinite or out-of-range result as the largest float of its sign, the range a UI Toolkit length holds.
        public float Evaluate(in RelativeLengthBasis basis) => Censor(Raw(basis));

        public StyleLengthExpression Negated() => new Unary(this, _type, x => -x);

        // This, raised to min where it measures below it; a NaN stays NaN for Evaluate to take as zero.
        public StyleLengthExpression AtLeast(float min)
            => min == -float.MaxValue ? this : new Unary(this, _type, x => double.IsNaN(x) ? x : Math.Max(min, x));

        public bool ReadsPercentage => (_reads & ReadsPercent) != 0;

        // The pixel length or percentage this is whatever it is measured against, or false when it reads a font-
        // relative or viewport unit, or a percentage beside a pixel length. A length reading percentages alone is
        // folded to a percentage, which UI Toolkit takes of the basis: each number in it is a literal or a ratio
        // of percentages, neither of which a positive basis changes.
        public bool TryFoldConstant(out float value, out LengthUnit unit)
        {
            unit = _reads == ReadsPercent ? LengthUnit.Percent : LengthUnit.Pixel;
            value = Evaluate(new RelativeLengthBasis(0f, 100f, 0f, 0f));
            return (_reads | ReadsPixels) == ReadsPixels || _reads == ReadsPercent;
        }

        // Whether text opens with a math function this reads, so that var(), rgb() and the other functions it does
        // not read are declined without being tokenized.
        public static bool OpensMathFunction(ReadOnlySpan<char> text)
        {
            foreach (var name in s_functions.Keys)
            {
                if (text.Length > name.Length && text[name.Length] == '('
                    && text.StartsWith(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // Parses a single dimension (2em, 50vw) or a math function coming to a length. A bare number, and anything
        // left over after the dimension or the function's closing parenthesis, is declined.
        public static bool TryParse(ReadOnlySpan<char> text, out StyleLengthExpression? expression)
        {
            expression = Parse(text, CalcType.Length);
            return expression != null;
        }

        // A math function coming to an angle that needs nothing measured, in degrees.
        public static bool TryParseAngle(ReadOnlySpan<char> text, out float degrees)
            => TryParseConstant(text, CalcType.Angle, out degrees);

        // A math function coming to a time that needs nothing measured, in milliseconds.
        public static bool TryParseTime(ReadOnlySpan<char> text, out float milliseconds)
            => TryParseConstant(text, CalcType.Time, out milliseconds);

        // A math function coming to a number that needs nothing measured.
        public static bool TryParseNumber(ReadOnlySpan<char> text, out float number)
            => TryParseConstant(text, CalcType.Number, out number);

        private static bool TryParseConstant(ReadOnlySpan<char> text, CalcType type, out float value)
        {
            var expression = OpensMathFunction(text.Trim("_ ".AsSpan())) ? Parse(text, type) : null;
            var constant = expression != null && (expression._reads | ReadsPixels) == ReadsPixels;
            value = constant ? expression!.Evaluate(default) : 0f;
            return constant;
        }

        private static StyleLengthExpression? Parse(ReadOnlySpan<char> text, CalcType type)
        {
            var parser = new Parser(TailwindMathSpacing.Format(text.ToString().Replace('_', ' ').Trim(' ')));
            return parser.TryTop(out var top) && top != null && top._type.Equals(type) ? top : null;
        }

        private static float Censor(double value)
            => double.IsNaN(value) ? 0f : (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);

        private static Dictionary<string, (Func<RelativeLengthBasis, double>, int, CalcType)> BuildUnits()
        {
            var units = new Dictionary<string, (Func<RelativeLengthBasis, double>, int, CalcType)>(StringComparer.OrdinalIgnoreCase)
            {
                ["px"] = (_ => 1d, ReadsPixels, CalcType.Length),
                ["rem"] = (_ => RootFontSize, ReadsPixels, CalcType.Length),
                ["%"] = (b => b.Percent / 100d, ReadsPercent, CalcType.Length),
                ["em"] = (b => b.Font.Em, ReadsElement, CalcType.Length),
                ["ex"] = (b => b.Font.Ex, ReadsElement, CalcType.Length),
                ["ch"] = (b => b.Font.Ch, ReadsElement, CalcType.Length),
                ["cap"] = (b => b.Font.Cap, ReadsElement, CalcType.Length),
                ["ic"] = (b => b.Font.Ic, ReadsElement, CalcType.Length),
                ["lh"] = (b => b.Font.Lh, ReadsElement, CalcType.Length),
                ["rex"] = (b => b.Root.Ex, ReadsElement, CalcType.Length),
                ["rch"] = (b => b.Root.Ch, ReadsElement, CalcType.Length),
                ["rcap"] = (b => b.Root.Cap, ReadsElement, CalcType.Length),
                ["ric"] = (b => b.Root.Ic, ReadsElement, CalcType.Length),
                ["rlh"] = (b => b.Root.Lh, ReadsElement, CalcType.Length),
                ["deg"] = (_ => 1d, 0, CalcType.Angle),
                ["grad"] = (_ => 0.9d, 0, CalcType.Angle),
                ["rad"] = (_ => 180d / Math.PI, 0, CalcType.Angle),
                ["turn"] = (_ => 360d, 0, CalcType.Angle),
                ["ms"] = (_ => 1d, 0, CalcType.Time),
                ["s"] = (_ => 1000d, 0, CalcType.Time),
            };
            // The small, large and dynamic viewport units read the panel as vw/vh/vmin/vmax do: a panel has no UA
            // chrome to show or hide, the case where CSS makes all three equal.
            foreach (var prefix in new[] { "", "s", "l", "d" })
            {
                units[prefix + "vw"] = (b => b.ViewportWidth / 100d, ReadsElement, CalcType.Length);
                units[prefix + "vh"] = (b => b.ViewportHeight / 100d, ReadsElement, CalcType.Length);
                units[prefix + "vmin"] = (b => Math.Min(b.ViewportWidth, b.ViewportHeight) / 100d, ReadsElement, CalcType.Length);
                units[prefix + "vmax"] = (b => Math.Max(b.ViewportWidth, b.ViewportHeight) / 100d, ReadsElement, CalcType.Length);
            }
            return units;
        }

        private static StyleLengthExpression? Dimension(double number, string unit)
        {
            if (s_units.TryGetValue(unit, out var leaf))
            {
                return new Unary(new Leaf(leaf.PerUnit, leaf.Reads, leaf.Type), leaf.Type, x => number * x);
            }
            return StyleArbitraryValueResolver.TryGetAbsoluteUnitPixels(unit.AsSpan(), out var pixels)
                ? new Constant(number * pixels, ReadsPixels, CalcType.Length)
                : null;
        }

        // A CSS type: the power of length, angle and time it carries. A percentage is a length here, since every
        // property this writes resolves its percentages against a length.
        private readonly struct CalcType : IEquatable<CalcType>
        {
            public static readonly CalcType Number = default;
            public static readonly CalcType Length = new(1, 0, 0);
            public static readonly CalcType Angle = new(0, 1, 0);
            public static readonly CalcType Time = new(0, 0, 1);

            private readonly int _length;
            private readonly int _angle;
            private readonly int _time;

            private CalcType(int length, int angle, int time)
            {
                _length = length;
                _angle = angle;
                _time = time;
            }

            public bool IsNumber => Equals(Number);

            public CalcType Times(CalcType other, int sign)
                => new(_length + (sign * other._length), _angle + (sign * other._angle), _time + (sign * other._time));

            public bool Equals(CalcType other) => _length == other._length && _angle == other._angle && _time == other._time;

            public override bool Equals(object? obj) => obj is CalcType other && Equals(other);

            public override int GetHashCode() => (_length * 31 * 31) + (_angle * 31) + _time;
        }

        private sealed class Constant : StyleLengthExpression
        {
            private readonly double _value;

            public Constant(double value, int reads, CalcType type) : base(reads, type) => _value = value;

            private protected override double Raw(in RelativeLengthBasis basis) => _value;
        }

        private sealed class Leaf : StyleLengthExpression
        {
            private readonly Func<RelativeLengthBasis, double> _perUnit;

            public Leaf(Func<RelativeLengthBasis, double> perUnit, int reads, CalcType type) : base(reads, type)
                => _perUnit = perUnit;

            private protected override double Raw(in RelativeLengthBasis basis) => _perUnit(basis);
        }

        private sealed class Unary : StyleLengthExpression
        {
            private readonly StyleLengthExpression _inner;
            private readonly Func<double, double> _f;

            public Unary(StyleLengthExpression inner, CalcType type, Func<double, double> f) : base(inner._reads, type)
            {
                _inner = inner;
                _f = f;
            }

            private protected override double Raw(in RelativeLengthBasis basis) => _f(_inner.Raw(basis));
        }

        private sealed class Binary : StyleLengthExpression
        {
            private readonly StyleLengthExpression _left;
            private readonly StyleLengthExpression _right;
            private readonly Func<double, double, double> _f;

            public Binary(StyleLengthExpression left, StyleLengthExpression right, CalcType type, Func<double, double, double> f)
                : base(left._reads | right._reads, type)
            {
                _left = left;
                _right = right;
                _f = f;
            }

            private protected override double Raw(in RelativeLengthBasis basis) => _f(_left.Raw(basis), _right.Raw(basis));
        }

        // min(), max() and hypot(): a fold over every argument, starting from the first.
        private sealed class Fold : StyleLengthExpression
        {
            private readonly List<StyleLengthExpression> _args;
            private readonly Func<double, double> _first;
            private readonly Func<double, double, double> _next;
            private readonly Func<double, double> _last;

            public Fold(List<StyleLengthExpression> args, Func<double, double> first, Func<double, double, double> next,
                Func<double, double> last) : base(ReadsOf(args), args[0]._type)
            {
                _args = args;
                _first = first;
                _next = next;
                _last = last;
            }

            private static int ReadsOf(List<StyleLengthExpression> args)
            {
                var reads = 0;
                foreach (var arg in args)
                {
                    reads |= arg._reads;
                }
                return reads;
            }

            private protected override double Raw(in RelativeLengthBasis basis)
            {
                var acc = _first(_args[0].Raw(basis));
                for (var i = 1; i < _args.Count; i++)
                {
                    acc = _next(acc, _args[i].Raw(basis));
                }
                return _last(acc);
            }
        }

        // CSS Values 4's arithmetic and functions, over IEEE doubles; each returns null where CSS's types do not
        // combine.
        private static class Op
        {
            public static StyleLengthExpression? Add(StyleLengthExpression? a, StyleLengthExpression? b, double sign)
                => a != null && b != null && a._type.Equals(b._type) ? Fold2(a, b, a._type, (x, y) => x + (sign * y)) : null;

            public static StyleLengthExpression? Multiply(StyleLengthExpression? a, StyleLengthExpression? b)
                => a != null && b != null ? Fold2(a, b, a._type.Times(b._type, 1), (x, y) => x * y) : null;

            public static StyleLengthExpression? Divide(StyleLengthExpression? a, StyleLengthExpression? b)
                => a != null && b != null ? Fold2(a, b, a._type.Times(b._type, -1), (x, y) => x / y) : null;

            // Two arguments of one type, giving a result of type, or of theirs where type is null.
            public static StyleLengthExpression? Same(List<StyleLengthExpression?> args, CalcType? type,
                Func<double, double, double> f)
                => args.Count == 2 && args[0] != null && args[1] != null && args[0]!._type.Equals(args[1]!._type)
                    ? Fold2(args[0]!, args[1]!, type ?? args[0]!._type, f)
                    : null;

            public static StyleLengthExpression? One(List<StyleLengthExpression?> args, Func<CalcType, CalcType?> type,
                Func<double, double> f)
                => args.Count == 1 && args[0] is { } arg && type(arg._type) is { } result ? Fold1(arg, result, f) : null;

            public static StyleLengthExpression? All(List<StyleLengthExpression?> args, Func<double, double> first,
                Func<double, double, double> next, Func<double, double> last)
            {
                var lengths = new List<StyleLengthExpression>(args.Count);
                foreach (var arg in args)
                {
                    if (arg == null || !arg._type.Equals(args[0]!._type))
                    {
                        return null;
                    }
                    lengths.Add(arg);
                }
                var fold = new Fold(lengths, first, next, last);
                return IsConstant(fold) ? new Constant(fold.Raw(default), fold._reads, fold._type) : fold;
            }

            private static StyleLengthExpression Fold1(StyleLengthExpression a, CalcType type, Func<double, double> f)
                => IsConstant(a) ? new Constant(f(a.Raw(default)), a._reads, type) : new Unary(a, type, f);

            private static StyleLengthExpression Fold2(StyleLengthExpression a, StyleLengthExpression b, CalcType type,
                Func<double, double, double> f)
                => IsConstant(a) && IsConstant(b)
                    ? new Constant(f(a.Raw(default), b.Raw(default)), a._reads | b._reads, type)
                    : new Binary(a, b, type, f);

            private static bool IsConstant(StyleLengthExpression e) => (e._reads | ReadsPixels) == ReadsPixels;

            public static double Round(string strategy, double a, double b)
            {
                if (b == 0d || double.IsNaN(a) || double.IsNaN(b))
                {
                    return double.NaN;
                }
                if (double.IsInfinity(a))
                {
                    return double.IsInfinity(b) ? double.NaN : a;
                }
                var negative = double.IsNegative(a);
                if (double.IsInfinity(b))
                {
                    return RoundToInfinity(strategy, a, negative);
                }
                var step = Math.Abs(b);
                var lower = Math.Floor(a / step) * step;
                var upper = Math.Ceiling(a / step) * step;
                var rounded = strategy switch
                {
                    "up" => upper,
                    "down" => lower,
                    "to-zero" => negative ? upper : lower,
                    // A tie goes to the upper multiple, toward positive infinity.
                    _ => a - lower < upper - a ? lower : upper,
                };
                return rounded == 0d && negative ? -0d : rounded;
            }

            // A finite a to the multiples of an infinite step, which are zero and the two infinities.
            private static double RoundToInfinity(string strategy, double a, bool negative)
                => strategy switch
                {
                    "up" => a > 0d ? double.PositiveInfinity : negative ? -0d : a,
                    "down" => a < 0d ? double.NegativeInfinity : negative ? a : 0d,
                    _ => negative ? -0d : 0d,
                };

            // mod() takes the sign of b, rem() the sign of a.
            public static double Modulo(double a, double b, bool signOfB)
            {
                if (b == 0d || double.IsInfinity(a) || double.IsNaN(a) || double.IsNaN(b))
                {
                    return double.NaN;
                }
                if (double.IsInfinity(b))
                {
                    return !signOfB || a == 0d || (a > 0d) == (b > 0d) ? a : double.NaN;
                }
                var r = a % b;
                return signOfB && r != 0d && (r < 0d) != (b < 0d) ? r + b : r;
            }

            public static double Sign(double a) => double.IsNaN(a) || a == 0d ? a : Math.Sign(a);

            // sin(), cos() or tan() of an angle, or of a number of radians. An argument on a multiple of a quarter
            // turn gives the exact value there, the infinities of tan() included, as Chromium computes them
            // (MathFunctionTests pins them); the function takes radians everywhere else.
            public static StyleLengthExpression? Trig(List<StyleLengthExpression?> args, int function)
            {
                if (args.Count != 1 || args[0] is not { } arg || !(arg._type.IsNumber || arg._type.Equals(CalcType.Angle)))
                {
                    return null;
                }
                var radians = arg._type.IsNumber;
                return Fold1(arg, CalcType.Number, x =>
                {
                    var degrees = radians ? x * Degrees : x;
                    return degrees % 90d == 0d
                        ? s_quarterTurns[function][(int)(((degrees / 90d % 4d) + 4d) % 4d)]
                        : s_trig[function](degrees / Degrees);
                });
            }

            public const int Sin = 0;
            public const int Cos = 1;
            public const int Tan = 2;

            private static readonly Func<double, double>[] s_trig = { Math.Sin, Math.Cos, Math.Tan };

            // sin, cos and tan at 0, 90, 180 and 270 degrees.
            private static readonly double[][] s_quarterTurns =
            {
                new[] { 0d, 1d, 0d, -1d },
                new[] { 1d, 0d, -1d, 0d },
                new[] { 0d, double.PositiveInfinity, 0d, double.NegativeInfinity },
            };

            public static CalcType? NumberOnly(CalcType type) => type.IsNumber ? CalcType.Number : null;

            public static CalcType? ToAngle(CalcType type) => type.IsNumber ? CalcType.Angle : null;

            public static bool Numbers(List<StyleLengthExpression?> args) => args.TrueForAll(a => a != null && a._type.IsNumber);

            public const double Degrees = 180d / Math.PI;
        }

        private sealed class Parser
        {
            private readonly List<Token> _tokens = new();
            private readonly bool _tokenized;
            private int _at;
            private int _depth;

            public Parser(string text) => _tokenized = Tokenize(text, _tokens);

            // A function or a single dimension, with no space before it and nothing after it.
            public bool TryTop(out StyleLengthExpression? top)
            {
                top = _tokenized && !Spaced() && !Is('(') ? Value() : null;
                return _at == _tokens.Count;
            }

            private bool Is(char kind) => _at < _tokens.Count && _tokens[_at].Kind == kind;

            private bool Spaced() => _at < _tokens.Count && _tokens[_at].Spaced;

            private bool Accept(char symbol)
            {
                var accepted = Is(symbol);
                _at += accepted ? 1 : 0;
                return accepted;
            }

            // Repetition in the grammar recurses through Descend rather than looping, so an Accept that stops
            // consuming fails at MaxDepth instead of spinning.
            private bool Descend() => ++_depth <= MaxDepth;

            private T Ascend<T>(T value)
            {
                _depth--;
                return value;
            }

            private StyleLengthExpression? Sum() => MoreTerms(Product());

            // A signed number after an operand is a second value rather than a term, which leaves the function
            // unclosed.
            private StyleLengthExpression? MoreTerms(StyleLengthExpression? left)
            {
                if (!Descend())
                {
                    return null;
                }
                var sign = Is('+') ? 1d : Is('-') ? -1d : 0d;
                if (sign == 0d)
                {
                    return Ascend(left);
                }
                var spaced = Spaced();
                _at++;
                return Ascend(spaced && Spaced() ? MoreTerms(Op.Add(left, Product(), sign)) : null);
            }

            private StyleLengthExpression? Product() => MoreFactors(Value());

            private StyleLengthExpression? MoreFactors(StyleLengthExpression? left)
                => !Descend() ? null
                    : Accept('*') ? Ascend(MoreFactors(Op.Multiply(left, Value())))
                    : Accept('/') ? Ascend(MoreFactors(Op.Divide(left, Value())))
                    : Ascend(left);

            private StyleLengthExpression? Value()
                => !Descend() ? null
                    : Ascend(Accept('(') ? Closed(Sum())
                        : Is(Token.Function) ? Function()
                        : Is(Token.Ident) ? Keyword()
                        : Number());

            private StyleLengthExpression? Closed(StyleLengthExpression? inner) => Accept(')') ? inner : null;

            private List<StyleLengthExpression?>? Arguments(List<StyleLengthExpression?> args)
            {
                args.Add(Sum());
                return !Descend() ? null : Ascend(Accept(',') ? Arguments(args) : args);
            }

            // CSS's numeric constants, in any case.
            private StyleLengthExpression? Keyword()
            {
                var value = _tokens[_at++].Text.ToLowerInvariant() switch
                {
                    "e" => Math.E,
                    "pi" => Math.PI,
                    "infinity" => double.PositiveInfinity,
                    "-infinity" => double.NegativeInfinity,
                    "nan" => double.NaN,
                    _ => (double?)null,
                };
                return value is { } constant ? new Constant(constant, 0, CalcType.Number) : null;
            }

            private StyleLengthExpression? Function()
            {
                var name = _tokens[_at++].Text.ToLowerInvariant();
                var strategy = name == "round" ? RoundingStrategy() : "nearest";
                var args = Arguments(new List<StyleLengthExpression?>());
                return args != null && Accept(')') && s_functions.TryGetValue(name, out var build) ? build(args, strategy) : null;
            }

            // round()'s optional first argument, in any case.
            private string RoundingStrategy()
            {
                if (!Is(Token.Ident) || _at + 1 >= _tokens.Count || _tokens[_at + 1].Kind != ',')
                {
                    return "nearest";
                }
                // Any other keyword is A itself (round(pi, 1)).
                var strategy = _tokens[_at].Text.ToLowerInvariant();
                if (strategy is not ("nearest" or "up" or "down" or "to-zero"))
                {
                    return "nearest";
                }
                _at += 2;
                return strategy;
            }

            private StyleLengthExpression? Number()
            {
                if (!Is(Token.Number))
                {
                    return null;
                }
                var token = _tokens[_at++];
                if (!double.TryParse(token.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    return null;
                }
                return token.Unit != null ? Dimension(number, token.Unit) : new Constant(number, 0, CalcType.Number);
            }

            // Splits text into tokens, each after an optional run of spaces or underscores: a number with an
            // optional unit, a function name with its opening parenthesis, a keyword, or one of - + * / ( ) ,.
            // False when something else, or a trailing run of spaces, is left over.
            private static bool Tokenize(string text, List<Token> tokens)
            {
                var at = 0;
                while (at >= 0 && at < text.Length)
                {
                    at = ReadToken(text, at, tokens);
                }
                return at == text.Length;
            }

            // Where the token read from at ends, or -1 when none can be read there.
            private static int ReadToken(string text, int at, List<Token> tokens)
            {
                var start = Span(text, at, " _");
                var number = NumberEnd(text, start);
                if (number > start)
                {
                    var unit = Is(text, number, "%") ? number + 1 : Span(text, number, Letters);
                    tokens.Add(new Token(Token.Number, start > at, text.Substring(start, number - start),
                        unit > number ? text.Substring(number, unit - number) : null));
                    return unit;
                }
                var name = IdentEnd(text, start);
                if (name > start)
                {
                    var function = Is(text, name, "(");
                    tokens.Add(new Token(function ? Token.Function : Token.Ident, start > at, text.Substring(start, name - start), null));
                    return function ? name + 1 : name;
                }
                if (Is(text, start, "-+*/(),"))
                {
                    tokens.Add(new Token(text[start], start > at, string.Empty, null));
                    return start + 1;
                }
                return -1;
            }

            // Where a number starting at start ends, or start when none does: an optional sign, digits with an
            // optional fraction (or a fraction alone), then an optional exponent. Text ending in a dot is no number,
            // and an e with no digit after it is left to be read as a unit.
            private static int NumberEnd(string text, int start)
            {
                var whole = Span(text, Is(text, start, "+-") ? start + 1 : start, Digits);
                var end = Is(text, whole, ".") ? Span(text, whole + 1, Digits) : whole;
                if (end == start || !Is(text, end - 1, Digits))
                {
                    return start;
                }
                var exponent = Is(text, end, "eE") ? (Is(text, end + 1, "+-") ? end + 2 : end + 1) : end;
                return Is(text, exponent, Digits) ? Span(text, exponent, Digits) : end;
            }

            // Where an identifier starting at start ends, or start when none does: a letter, or a - and a letter,
            // then letters, digits and hyphens.
            private static int IdentEnd(string text, int start)
            {
                var first = Is(text, start, "-") ? start + 1 : start;
                return Is(text, first, Letters) ? Span(text, first, Letters + Digits + "-") : start;
            }

            private const string Digits = "0123456789";

            private const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            private static bool Is(string text, int at, string set) => at < text.Length && set.IndexOf(text[at]) >= 0;

            private static int Span(string text, int at, string set)
            {
                while (Is(text, at, set))
                {
                    at++;
                }
                return at;
            }
        }

        // A number (its text, and its unit when one is glued to it), a function's name, a keyword, or a symbol, whose
        // Kind is the symbol itself.
        private readonly struct Token
        {
            public const char Number = 'n';
            public const char Function = 'f';
            public const char Ident = 'i';

            public readonly char Kind;
            public readonly bool Spaced;
            public readonly string Text;
            public readonly string? Unit;

            public Token(char kind, bool spaced, string text, string? unit)
            {
                Kind = kind;
                Spaced = spaced;
                Text = text;
                Unit = unit;
            }
        }
    }
}
