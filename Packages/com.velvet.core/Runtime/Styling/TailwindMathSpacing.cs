using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet
{
    // Tailwind v4's handling of a bracket's math functions, ported from its utils/math-operators.ts so a bracket
    // reaches the length grammar as the browser receives it. Both match function names in lower case only, so
    // `CALC(1px+2px)` is left unspaced, which CSS then declines, while `CALC(1px_+_2px)` is a length.
    internal static class TailwindMathSpacing
    {
        private static readonly string[] s_mathFunctions =
        {
            "calc", "min", "max", "clamp", "mod", "rem", "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
            "pow", "sqrt", "hypot", "log", "exp", "round",
        };

        // Tailwind's hasMathFn: whether a type test takes the value for a number, length or percentage.
        public static bool HasMathFunction(string value)
        {
            if (value.IndexOf('(') < 0)
            {
                return false;
            }
            foreach (var fn in s_mathFunctions)
            {
                if (value.IndexOf(fn + "(", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        // Tailwind's addWhitespaceAroundMathOperators, over a value whose underscores are already spaces.
        public static string Format(string input)
        {
            if (!MentionsMathFunction(input))
            {
                return input;
            }

            var result = new StringBuilder(input.Length + 8);
            var formattable = new List<bool>();
            int? valuePos = null;
            int? lastValuePos = null;

            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                TrackValue(c, i, ref valuePos, ref lastValuePos);

                if (c == '(')
                {
                    result.Append(c);
                    formattable.Insert(0, OpensMathContext(input, i, formattable));
                    continue;
                }

                if (c == ')')
                {
                    result.Append(c);
                    if (formattable.Count > 0)
                    {
                        formattable.RemoveAt(0);
                    }
                }
                else if (c == ',' && Top(formattable))
                {
                    result.Append(", ");
                }
                else if (c == ' ' && Top(formattable) && result.Length > 0 && result[result.Length - 1] == ' ')
                {
                }
                else if (IsOperator(c) && Top(formattable))
                {
                    AppendOperator(result, input, i, c, lastValuePos);
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        private static void TrackValue(char c, int i, ref int? valuePos, ref int? lastValuePos)
        {
            if (IsDigit(c) || (valuePos != null && (c == '%' || IsLower(c) || (c >= 'A' && c <= 'Z'))))
            {
                valuePos = i;
                return;
            }
            lastValuePos = valuePos;
            valuePos = null;
        }

        // Whether the parenthesis at open opens a math function, or a group inside one.
        private static bool OpensMathContext(string input, int open, List<bool> formattable)
        {
            var start = open;
            for (var j = open - 1; j >= 0 && (IsDigit(input[j]) || IsLower(input[j])); j--)
            {
                start = j;
            }
            var fn = input.Substring(start, open - start);
            return Array.IndexOf(s_mathFunctions, fn) >= 0 || (Top(formattable) && fn.Length == 0);
        }

        private static void AppendOperator(StringBuilder result, string input, int i, char c, int? lastValuePos)
        {
            var trimmed = result.ToString().TrimEnd();
            var prev = trimmed.Length > 0 ? trimmed[trimmed.Length - 1] : '\0';
            var prevPrev = trimmed.Length > 1 ? trimmed[trimmed.Length - 2] : '\0';
            var next = i + 1 < input.Length ? input[i + 1] : '\0';

            if (((prev == 'e' || prev == 'E') && IsDigit(prevPrev)) || IsOperator(prev) || prev == '(' || prev == ',')
            {
                result.Append(c);
            }
            else if (i > 0 && input[i - 1] == ' ')
            {
                result.Append(c).Append(' ');
            }
            else if (IsDigit(prev) || IsDigit(next) || prev == ')' || next == '(' || IsOperator(next)
                     || lastValuePos == i - 1)
            {
                result.Append(' ').Append(c).Append(' ');
            }
            else
            {
                result.Append(c);
            }
        }

        private static bool MentionsMathFunction(string input)
        {
            foreach (var fn in s_mathFunctions)
            {
                if (input.IndexOf(fn, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Top(List<bool> formattable) => formattable.Count > 0 && formattable[0];

        private static bool IsOperator(char c) => c == '+' || c == '-' || c == '*' || c == '/';

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsLower(char c) => c >= 'a' && c <= 'z';
    }
}
