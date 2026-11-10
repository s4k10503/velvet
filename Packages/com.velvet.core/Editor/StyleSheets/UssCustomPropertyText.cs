#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet.Editor
{
    /// <summary>
    /// Rewrites the custom properties of a USS file that UI Toolkit's importer cannot hold: a value with a unit
    /// outside the ones it keeps (<c>rem</c>, <c>em</c>, …) fails the whole sheet, and one with a CSS math function, <c>cubic-bezier()</c> or
    /// <c>steps()</c> is dropped. Such a value becomes a quoted string of its own text, which the importer stores as written and the
    /// theme readers take back as CSS (<see cref="StyleCustomProperties.Spell"/>).
    /// </summary>
    internal static class UssCustomPropertyText
    {
        // The units StyleValueImporter's unit table maps; every other one is an error that leaves the sheet empty.
        private static readonly HashSet<string> s_importerUnits = new(StringComparer.Ordinal)
        {
            "px", "s", "ms", "deg", "grad", "rad", "turn",
        };

        internal static string Rewrite(string uss)
        {
            var output = new StringBuilder(uss.Length);
            var depth = 0;
            var atStart = false;
            var i = 0;
            while (i < uss.Length)
            {
                var c = uss[i];
                if (SkipTrivia(uss, ref i, output))
                {
                    continue;
                }
                if (c == '{')
                {
                    depth++;
                    atStart = true;
                }
                else if (c == '}')
                {
                    depth = Math.Max(0, depth - 1);
                    atStart = depth > 0;
                }
                else if (c == ';')
                {
                    atStart = depth > 0;
                }
                else if (!char.IsWhiteSpace(c))
                {
                    var start = i;
                    if (atStart && c == '-' && i + 1 < uss.Length && uss[i + 1] == '-')
                    {
                        RewriteDeclaration(uss, ref i, output);
                    }
                    atStart = false;
                    if (i != start)
                    {
                        continue;
                    }
                }
                output.Append(c);
                i++;
            }
            return output.ToString();
        }

        // A comment or a string, copied through whole.
        private static bool SkipTrivia(string text, ref int i, StringBuilder? output)
        {
            var end = TriviaEnd(text, i);
            if (end == i)
            {
                return false;
            }
            output?.Append(text, i, end - i);
            i = end;
            return true;
        }

        private static int TriviaEnd(string text, int i)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                // MUTANT_SURVIVES(equivalent, boundary): the search starts at i + 2, so a match is never at 0.
                return close < 0 ? text.Length : close + 2;
            }
            if (c != '"' && c != '\'')
            {
                return i;
            }
            for (var j = i + 1; j < text.Length; j++)
            {
                if (text[j] == '\\')
                {
                    j++;
                }
                else if (text[j] == c)
                {
                    return j + 1;
                }
            }
            return text.Length;
        }

        // Moves i past the declaration only where it rewrites it, so a declaration left as written is copied by the caller.
        private static void RewriteDeclaration(string uss, ref int i, StringBuilder output)
        {
            var nameEnd = i;
            while (nameEnd < uss.Length && IsIdentChar(uss[nameEnd]))
            {
                nameEnd++;
            }
            var colon = nameEnd;
            while (colon < uss.Length)
            {
                if (char.IsWhiteSpace(uss[colon]))
                {
                    colon++;
                }
                else if (!SkipTrivia(uss, ref colon, null))
                {
                    break;
                }
            }
            if (colon >= uss.Length || uss[colon] != ':')
            {
                return;
            }
            var valueStart = colon + 1;
            var valueEnd = ValueEnd(uss, valueStart);
            var value = StripComments(uss.Substring(valueStart, valueEnd - valueStart));
            var important = SplitImportant(ref value);
            value = value.Trim();
            if (!NeedsQuoting(value))
            {
                return;
            }
            var newlines = 0;
            for (var k = valueStart; k < valueEnd; k++)
            {
                newlines += uss[k] == '\n' ? 1 : 0;
            }
            output.Append(uss, i, valueStart - i).Append(" \"");
            foreach (var ch in value)
            {
                output.Append(ch is '\r' or '\n' ? ' ' : ch);
            }
            // The line breaks the value held, so an importer message names the line the author wrote.
            output.Append('"').Append(important).Append('\n', newlines);
            i = valueEnd;
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' || c > 127;

        // The `;` or `}` that ends the declaration whose value begins at start, outside any parenthesis.
        private static int ValueEnd(string text, int start)
        {
            var parens = 0;
            var i = start;
            while (i < text.Length)
            {
                if (SkipTrivia(text, ref i, null))
                {
                    continue;
                }
                var c = text[i];
                if (c == '(')
                {
                    parens++;
                }
                else if (c == ')')
                {
                    parens = Math.Max(0, parens - 1);
                }
                else if (parens == 0 && (c == ';' || c == '}'))
                {
                    return i;
                }
                i++;
            }
            return text.Length;
        }

        private static string StripComments(string value)
        {
            var text = new StringBuilder(value.Length);
            var i = 0;
            while (i < value.Length)
            {
                if (value[i] == '/' && i + 1 < value.Length && value[i + 1] == '*')
                {
                    i = TriviaEnd(value, i);
                    text.Append(' ');
                    continue;
                }
                text.Append(value[i++]);
            }
            return text.ToString();
        }

        // The `!important` that closes a declaration, which stays outside the quotes.
        private static string SplitImportant(ref string value)
        {
            var bang = value.LastIndexOf('!');
            // MUTANT_SURVIVES(equivalent, boundary): a bang at 0 leaves NeedsQuoting either an empty value or `!important`, declining both.
            if (bang < 0 || !string.Equals(value.Substring(bang + 1).Trim(), "important", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            value = value.Substring(0, bang);
            return " !important";
        }

        // Whether the importer would fail or drop value: a unit it does not keep, or a math function. A value holding a
        // quote or a backslash is left as written, so a unit in it still fails the importer.
        internal static bool NeedsQuoting(string value)
        {
            if (value.IndexOfAny(new[] { '"', '\'', '\\' }) >= 0)
            {
                return false;
            }
            return TailwindMathSpacing.HasMathFunction(value) || HasEasingFunction(value) || HasUnkeptUnit(value);
        }

        // cubic-bezier() and steps(), which the importer drops from a custom property as it does a math function.
        private static bool HasEasingFunction(string value)
            => HasFunction(value, "cubic-bezier") || HasFunction(value, "steps");

        private static bool HasFunction(string value, string name)
        {
            var open = name + "(";
            var at = value.IndexOf(open, StringComparison.OrdinalIgnoreCase);
            while (at > 0 && IsIdentChar(value[at - 1]))
            {
                at = value.IndexOf(open, at + 1, StringComparison.OrdinalIgnoreCase);
            }
            return at >= 0;
        }

        private static bool HasUnkeptUnit(string value)
        {
            var i = 0;
            while (i < value.Length)
            {
                if (StartsIdentifier(value, i))
                {
                    // An identifier, a function name or a hex colour: digits inside it are no number.
                    i++;
                    while (i < value.Length && IsIdentChar(value[i]))
                    {
                        i++;
                    }
                }
                else if (StartsNumber(value, i))
                {
                    i = NumberEnd(value, i);
                    var unitEnd = i;
                    while (unitEnd < value.Length && char.IsLetter(value[unitEnd]))
                    {
                        unitEnd++;
                    }
                    if (unitEnd > i && !s_importerUnits.Contains(value.Substring(i, unitEnd - i)))
                    {
                        return true;
                    }
                    i = unitEnd;
                }
                else
                {
                    i++;
                }
            }
            return false;
        }

        private static bool StartsIdentifier(string value, int i)
        {
            var c = value[i];
            if (c == '-')
            {
                return i + 1 < value.Length && (char.IsLetter(value[i + 1]) || value[i + 1] is '-' or '_');
            }
            return c == '#' || c == '_' || char.IsLetter(c) || c > 127;
        }

        // A sign is not read: the number it would open ends where the one after it does, so the unit is the same.
        private static bool StartsNumber(string value, int i)
        {
            if (value[i] == '.')
            {
                i++;
            }
            return i < value.Length && char.IsDigit(value[i]);
        }

        private static int NumberEnd(string value, int i)
        {
            while (i < value.Length && char.IsDigit(value[i]))
            {
                i++;
            }
            if (i < value.Length && value[i] == '.')
            {
                i++;
                while (i < value.Length && char.IsDigit(value[i]))
                {
                    i++;
                }
            }
            // An exponent only where digits follow, so `1em` keeps its unit.
            if (i < value.Length && value[i] is 'e' or 'E')
            {
                var j = i + 1;
                if (j < value.Length && value[j] is '+' or '-')
                {
                    j++;
                }
                if (j < value.Length && char.IsDigit(value[j]))
                {
                    while (j < value.Length && char.IsDigit(value[j]))
                    {
                        j++;
                    }
                    i = j;
                }
            }
            return i;
        }
    }
}
