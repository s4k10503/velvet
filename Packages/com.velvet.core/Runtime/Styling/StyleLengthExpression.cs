using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;

namespace Velvet
{
    // A bracketed CSS <length-percentage>: a single dimension, or calc() / min() / max() / clamp() over
    // dimensions, numbers and nested parentheses, with `_` read as a space. CSS wants a space on both sides of
    // a binary + or -; Tailwind writes calc(50%-1rem) and spaces it itself, so a space on neither side is
    // read the same, and one on a single side is declined: `1px -2px` is two values. px, rem (at the fixed 16px the rest of the
    // bracket grammar uses) and the absolute units are pixels; a percentage stays symbolic until Evaluate is
    // handed the pixels 100% is.
    internal abstract class StyleLengthExpression
    {
        // What a length reads, for TryFoldConstant.
        private const int ReadsPixels = 1;
        private const int ReadsPercent = 2;

        // Nesting, and a run of terms or arguments, past this depth is declined rather than recursed into.
        private const int MaxDepth = 64;

        private static readonly Regex s_token = new(
            @"\G(?<space>[ _]+)?(?:(?<number>[-+]?(?:[0-9]*\.)?[0-9]+(?:[eE][-+]?[0-9]+)?)(?<unit>%|[A-Za-z]+)?|(?<function>[A-Za-z]+)\(|(?<symbol>[-+*/(),]))",
            RegexOptions.CultureInvariant);

        // Unit names match case-sensitively, as StyleArbitraryValueResolver.TryParseValue's do.
        private static readonly Dictionary<string, Leaf> s_units = new(StringComparer.Ordinal)
        {
            ["px"] = new Leaf(_ => 1f, ReadsPixels),
            ["rem"] = new Leaf(_ => 16f, ReadsPixels),
            ["%"] = new Leaf(percent => percent / 100f, ReadsPercent),
        };

        private readonly int _reads;

        private StyleLengthExpression(int reads) => _reads = reads;

        // What this comes to in pixels where 100% is percent pixels.
        public abstract float Evaluate(float percent);

        // The pixel length or percentage this is whatever 100% is, or false when it reads a percentage beside a
        // pixel length. A length reading percentages alone is a multiple of 100%, because min() and max() of
        // multiples of one non-negative basis are themselves one.
        public bool TryFoldConstant(out float value, out LengthUnit unit)
        {
            unit = _reads == ReadsPercent ? LengthUnit.Percent : LengthUnit.Pixel;
            value = Evaluate(100f);
            return _reads != (ReadsPixels | ReadsPercent);
        }

        // Parses a single dimension (1rem, 25%) or a math function. A bare number, and anything left over after
        // the dimension or the function's closing parenthesis, is declined.
        public static bool TryParse(ReadOnlySpan<char> text, out StyleLengthExpression? expression)
        {
            var parser = new Parser(text.ToString());
            expression = parser.TryTop(out var top) ? top : null;
            return expression != null;
        }

        private static StyleLengthExpression? Unit(string name)
        {
            if (s_units.TryGetValue(name, out var leaf))
            {
                return leaf;
            }
            return StyleArbitraryValueResolver.TryGetAbsoluteUnitPixels(name.AsSpan(), out var pixels)
                ? new Scaled(s_units["px"], pixels)
                : null;
        }

        private sealed class Leaf : StyleLengthExpression
        {
            private readonly Func<float, float> _perUnit;

            public Leaf(Func<float, float> perUnit, int reads) : base(reads) => _perUnit = perUnit;

            public override float Evaluate(float percent) => _perUnit(percent);
        }

        private sealed class Scaled : StyleLengthExpression
        {
            private readonly StyleLengthExpression _inner;
            private readonly float _factor;

            public Scaled(StyleLengthExpression inner, float factor) : base(inner._reads)
            {
                _inner = inner;
                _factor = factor;
            }

            public override float Evaluate(float percent) => _factor * _inner.Evaluate(percent);
        }

        private sealed class Sum : StyleLengthExpression
        {
            private readonly StyleLengthExpression _left;
            private readonly StyleLengthExpression _right;

            public Sum(StyleLengthExpression left, StyleLengthExpression right) : base(left._reads | right._reads)
            {
                _left = left;
                _right = right;
            }

            public override float Evaluate(float percent) => _left.Evaluate(percent) + _right.Evaluate(percent);
        }

        private sealed class Extremum : StyleLengthExpression
        {
            private readonly List<StyleLengthExpression> _args;
            private readonly bool _isMax;

            public Extremum(List<StyleLengthExpression> args, bool isMax) : base(ReadsOf(args))
            {
                _args = args;
                _isMax = isMax;
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

            public override float Evaluate(float percent)
            {
                var extreme = _args[0].Evaluate(percent);
                foreach (var arg in _args)
                {
                    extreme = _isMax ? Math.Max(extreme, arg.Evaluate(percent)) : Math.Min(extreme, arg.Evaluate(percent));
                }
                return extreme;
            }
        }

        // A plain number or a length; neither is set where the text broke a rule, and that carries upward.
        private readonly struct Operand
        {
            public readonly float? Number;
            public readonly StyleLengthExpression? Length;

            private Operand(float? number, StyleLengthExpression? length)
            {
                Number = number;
                Length = length;
            }

            public static Operand Of(float number) => new(number, null);

            public static Operand Of(StyleLengthExpression? length) => new(null, length);

            // CSS's type rules: + and - take two numbers or two lengths, * a number on at least one side.
            public static Operand Add(Operand a, Operand b, float sign)
                => a.Number is { } x && b.Number is { } y ? Of(x + (sign * y))
                    : a.Length != null && b.Length != null ? Of(new Sum(a.Length, new Scaled(b.Length, sign)))
                    : default;

            public static Operand Multiply(Operand a, Operand b)
                => a.Number is { } x && b.Number is { } y ? Of(x * y)
                    : a.Number is { } k && b.Length != null ? Of(new Scaled(b.Length, k))
                    : b.Number is { } m && a.Length != null ? Of(new Scaled(a.Length, m))
                    : default;

            public static Operand Divide(Operand a, Operand b)
                => b.Number is { } d && d != 0f ? Multiply(a, Of(1f / d)) : default;

            public static Operand Extreme(List<Operand> args, bool isMax)
            {
                var lengths = new List<StyleLengthExpression>();
                float? number = null;
                foreach (var arg in args)
                {
                    if (arg.Length != null)
                    {
                        lengths.Add(arg.Length);
                    }
                    else if (arg.Number is { } n)
                    {
                        number = number == null ? n : isMax ? Math.Max(number.Value, n) : Math.Min(number.Value, n);
                    }
                    else
                    {
                        return default;
                    }
                }
                return lengths.Count == args.Count ? Of(new Extremum(lengths, isMax))
                    : lengths.Count == 0 ? Of(number!.Value)
                    : default;
            }
        }

        private sealed class Parser
        {
            private readonly List<Match> _tokens = new();
            private readonly bool _tokenized;
            private int _at;
            private int _depth;

            public Parser(string text)
            {
                var end = 0;
                for (var match = s_token.Match(text); match.Success; match = match.NextMatch())
                {
                    _tokens.Add(match);
                    end = match.Index + match.Length;
                }
                _tokenized = end == text.Length;
            }

            // A function or a single dimension, with no space before it and nothing after it.
            public bool TryTop(out StyleLengthExpression? top)
            {
                top = _tokenized && !Spaced() && !Peek("(") ? Value().Length : null;
                return _at == _tokens.Count;
            }

            private bool Is(string group) => _at < _tokens.Count && _tokens[_at].Groups[group].Success;

            private bool Spaced() => Is("space");

            private bool Peek(string symbol) => Is("symbol") && _tokens[_at].Groups["symbol"].Value == symbol;

            private bool Accept(string symbol)
            {
                var accepted = Peek(symbol);
                _at += accepted ? 1 : 0;
                return accepted;
            }

            // Repetition in the grammar recurses through Enter rather than looping, so an Accept that stops consuming
            // fails at MaxDepth instead of spinning.
            private bool Enter() => ++_depth <= MaxDepth;

            private T Leave<T>(T value)
            {
                _depth--;
                return value;
            }

            private Operand Sum() => MoreTerms(Product());

            // A sign glued to the number after an operand is the operator: the term is that signed number.
            private Operand MoreTerms(Operand left)
            {
                if (!Enter())
                {
                    return default;
                }
                if (Is("number") && !Spaced() && "+-".IndexOf(_tokens[_at].Groups["number"].Value[0]) >= 0)
                {
                    return Leave(MoreTerms(Operand.Add(left, Product(), 1f)));
                }
                var sign = Peek("+") ? 1f : Peek("-") ? -1f : 0f;
                if (sign == 0f)
                {
                    return Leave(left);
                }
                var spaced = Spaced();
                _at++;
                return Leave(spaced == Spaced() ? MoreTerms(Operand.Add(left, Product(), sign)) : default);
            }

            private Operand Product() => MoreFactors(Value());

            private Operand MoreFactors(Operand left)
                => !Enter() ? default
                    : Accept("*") ? Leave(MoreFactors(Operand.Multiply(left, Value())))
                    : Accept("/") ? Leave(MoreFactors(Operand.Divide(left, Value())))
                    : Leave(left);

            private Operand Value()
                => !Enter() ? default
                    : Leave(Accept("(") ? Closed(Sum())
                        : Is("function") ? Function()
                        : Dimension());

            private Operand Closed(Operand inner) => Accept(")") ? inner : default;

            private List<Operand>? Arguments(List<Operand> args)
            {
                args.Add(Sum());
                return !Enter() ? null : Leave(Accept(",") ? Arguments(args) : args);
            }

            private Operand Function()
            {
                var name = _tokens[_at++].Groups["function"].Value;
                var args = Arguments(new List<Operand>());
                if (args == null || !Accept(")"))
                {
                    return default;
                }
                // clamp(MIN, VAL, MAX) is max(MIN, min(VAL, MAX)).
                return name == "calc" && args.Count == 1 ? args[0]
                    : name == "min" ? Operand.Extreme(args, false)
                    : name == "max" ? Operand.Extreme(args, true)
                    : name == "clamp" && args.Count == 3
                        ? Operand.Extreme(new List<Operand> { args[0], Operand.Extreme(args.GetRange(1, 2), false) }, true)
                    : default;
            }

            private Operand Dimension()
            {
                if (!Is("number"))
                {
                    return default;
                }
                var token = _tokens[_at++];
                if (!float.TryParse(token.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var number))
                {
                    return default;
                }
                var unit = token.Groups["unit"];
                return unit.Success ? Operand.Of(Unit(unit.Value) is { } leaf ? new Scaled(leaf, number) : null)
                    : Operand.Of(number);
            }
        }
    }
}
