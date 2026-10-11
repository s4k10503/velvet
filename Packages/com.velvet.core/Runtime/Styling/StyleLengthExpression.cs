using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Velvet
{
    // What a relative length is measured against, in pixels: one em, 100% and the panel's two sides.
    internal readonly struct RelativeLengthBasis
    {
        public readonly float Em;
        public readonly float Percent;
        public readonly float ViewportWidth;
        public readonly float ViewportHeight;

        public RelativeLengthBasis(float em, float percent, float viewportWidth, float viewportHeight)
        {
            Em = em;
            Percent = percent;
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
        }
    }

    // A bracketed CSS <length-percentage>: a single dimension, or calc() / min() / max() / clamp() over
    // dimensions, numbers and nested parentheses, with `_` read as a space. CSS wants a space on both sides of
    // a binary + or -; Tailwind writes calc(50%-1rem) and spaces it itself, so a space on neither side is
    // read the same, and one on a single side is declined: `1px -2px` is two values. px, rem (at the fixed 16px the rest of the
    // bracket grammar uses) and the absolute units are pixels; %, em and the viewport units stay symbolic until
    // Evaluate is handed a basis.
    internal abstract class StyleLengthExpression
    {
        // What a length reads, for TryFoldConstant.
        private const int ReadsPixels = 1;
        private const int ReadsPercent = 2;
        private const int ReadsElement = 4;

        // Nesting, and a run of terms or arguments, past this depth is declined rather than recursed into.
        private const int MaxDepth = 64;

        // Unit names match case-sensitively, as StyleArbitraryValueResolver.TryParseValue's do. The small,
        // large and dynamic viewport units read the panel as vw/vh/vmin/vmax do: a panel has no UA chrome to
        // show or hide, the case where CSS makes all three equal.
        private static readonly Dictionary<string, Leaf> s_units = BuildUnits();

        private readonly int _reads;

        private StyleLengthExpression(int reads) => _reads = reads;

        public abstract float Evaluate(in RelativeLengthBasis basis);

        public StyleLengthExpression Negated() => new Scaled(this, -1f);

        public bool ReadsPercentage => (_reads & ReadsPercent) != 0;

        // Whether a coefficient overflowed, which every basis then carries.
        public bool IsFinite() => float.IsFinite(Evaluate(new RelativeLengthBasis(1f, 100f, 100f, 100f)));

        // The pixel length or percentage this is whatever it is measured against, or false when it reads an em, a
        // viewport unit, or a percentage beside a pixel length. A length reading percentages alone is a multiple
        // of its basis, because min() and max() of multiples of one non-negative basis are themselves one.
        public bool TryFoldConstant(out float value, out LengthUnit unit)
        {
            unit = _reads == ReadsPercent ? LengthUnit.Percent : LengthUnit.Pixel;
            value = Evaluate(new RelativeLengthBasis(0f, 100f, 0f, 0f));
            return (_reads | ReadsPixels) == ReadsPixels || _reads == ReadsPercent;
        }

        // Parses a single dimension (2em, 50vw) or a math function. A bare number, and anything left over after
        // the dimension or the function's closing parenthesis, is declined.
        // Asked before TryParse, so that var(), rgb() and the other functions this does not read are declined
        // without being tokenized.
        public static bool OpensMathFunction(ReadOnlySpan<char> text)
            => text.StartsWith("calc(".AsSpan()) || text.StartsWith("min(".AsSpan())
                || text.StartsWith("max(".AsSpan()) || text.StartsWith("clamp(".AsSpan());

        public static bool TryParse(ReadOnlySpan<char> text, out StyleLengthExpression? expression)
        {
            var parser = new Parser(text.ToString());
            expression = parser.TryTop(out var top) ? top : null;
            return expression != null;
        }

        private static Dictionary<string, Leaf> BuildUnits()
        {
            var units = new Dictionary<string, Leaf>(StringComparer.Ordinal)
            {
                ["px"] = new Leaf(_ => 1f, ReadsPixels),
                ["rem"] = new Leaf(_ => 16f, ReadsPixels),
                ["%"] = new Leaf(b => b.Percent / 100f, ReadsPercent),
                ["em"] = new Leaf(b => b.Em, ReadsElement),
            };
            foreach (var prefix in new[] { "", "s", "l", "d" })
            {
                units[prefix + "vw"] = new Leaf(b => b.ViewportWidth / 100f, ReadsElement);
                units[prefix + "vh"] = new Leaf(b => b.ViewportHeight / 100f, ReadsElement);
                units[prefix + "vmin"] = new Leaf(b => Math.Min(b.ViewportWidth, b.ViewportHeight) / 100f, ReadsElement);
                units[prefix + "vmax"] = new Leaf(b => Math.Max(b.ViewportWidth, b.ViewportHeight) / 100f, ReadsElement);
            }
            return units;
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
            private readonly Func<RelativeLengthBasis, float> _perUnit;

            public Leaf(Func<RelativeLengthBasis, float> perUnit, int reads) : base(reads) => _perUnit = perUnit;

            public override float Evaluate(in RelativeLengthBasis basis) => _perUnit(basis);
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

            public override float Evaluate(in RelativeLengthBasis basis) => _factor * _inner.Evaluate(basis);
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

            public override float Evaluate(in RelativeLengthBasis basis) => _left.Evaluate(basis) + _right.Evaluate(basis);
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

            public override float Evaluate(in RelativeLengthBasis basis)
            {
                var extreme = _args[0].Evaluate(basis);
                foreach (var arg in _args)
                {
                    extreme = _isMax ? Math.Max(extreme, arg.Evaluate(basis)) : Math.Min(extreme, arg.Evaluate(basis));
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
            private readonly List<Token> _tokens = new();
            private readonly bool _tokenized;
            private int _at;
            private int _depth;

            public Parser(string text) => _tokenized = Tokenize(text, _tokens);

            // A function or a single dimension, with no space before it and nothing after it.
            public bool TryTop(out StyleLengthExpression? top)
            {
                top = _tokenized && !Spaced() && !Peek('(') ? Value().Length : null;
                return _at == _tokens.Count;
            }

            private bool Is(char kind) => _at < _tokens.Count && _tokens[_at].Kind == kind;

            private bool Spaced() => _at < _tokens.Count && _tokens[_at].Spaced;

            private bool Peek(char symbol) => Is(symbol);

            private bool Accept(char symbol)
            {
                var accepted = Peek(symbol);
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

            private Operand Sum() => MoreTerms(Product());

            // A sign glued to the number after an operand is the operator: the term is that signed number.
            private Operand MoreTerms(Operand left)
            {
                if (!Descend())
                {
                    return default;
                }
                if (Is(Token.Number) && !Spaced() && "+-".IndexOf(_tokens[_at].Text[0]) >= 0)
                {
                    return Ascend(MoreTerms(Operand.Add(left, Product(), 1f)));
                }
                var sign = Peek('+') ? 1f : Peek('-') ? -1f : 0f;
                if (sign == 0f)
                {
                    return Ascend(left);
                }
                var spaced = Spaced();
                _at++;
                return Ascend(spaced == Spaced() ? MoreTerms(Operand.Add(left, Product(), sign)) : default);
            }

            private Operand Product() => MoreFactors(Value());

            private Operand MoreFactors(Operand left)
                => !Descend() ? default
                    : Accept('*') ? Ascend(MoreFactors(Operand.Multiply(left, Value())))
                    : Accept('/') ? Ascend(MoreFactors(Operand.Divide(left, Value())))
                    : Ascend(left);

            private Operand Value()
                => !Descend() ? default
                    : Ascend(Accept('(') ? Closed(Sum())
                        : Is(Token.Function) ? Function()
                        : Dimension());

            private Operand Closed(Operand inner) => Accept(')') ? inner : default;

            private List<Operand>? Arguments(List<Operand> args)
            {
                args.Add(Sum());
                return !Descend() ? null : Ascend(Accept(',') ? Arguments(args) : args);
            }

            private Operand Function()
            {
                var name = _tokens[_at++].Text;
                var args = Arguments(new List<Operand>());
                if (args == null || !Accept(')'))
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
                if (!Is(Token.Number))
                {
                    return default;
                }
                var token = _tokens[_at++];
                if (!float.TryParse(token.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                        out var number))
                {
                    return default;
                }
                return token.Unit != null ? Operand.Of(Unit(token.Unit) is { } leaf ? new Scaled(leaf, number) : null)
                    : Operand.Of(number);
            }

            // Splits text into tokens, each after an optional run of spaces or underscores: a number with an
            // optional unit, a function name with its opening parenthesis, or one of - + * / ( ) ,. False when
            // something else, or a trailing run of spaces, is left over.
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
                var name = Span(text, number, Letters);
                if (number > start)
                {
                    var unit = Is(text, number, "%") ? number + 1 : name;
                    tokens.Add(new Token(Token.Number, start > at, text.Substring(start, number - start),
                        unit > number ? text.Substring(number, unit - number) : null));
                    return unit;
                }
                if (name > start && Is(text, name, "("))
                {
                    tokens.Add(new Token(Token.Function, start > at, text.Substring(start, name - start), null));
                    return name + 1;
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

        // A number (its text, and its unit when one is glued to it), a function's name, or a symbol, whose Kind
        // is the symbol itself.
        private readonly struct Token
        {
            public const char Number = 'n';
            public const char Function = 'f';

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
