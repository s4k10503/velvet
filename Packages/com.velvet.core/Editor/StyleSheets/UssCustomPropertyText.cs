#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet.Editor
{
    /// <summary>
    /// Rewrites the custom properties of a USS file that UI Toolkit's importer cannot hold: a value with a unit
    /// outside the ones it keeps (<c>rem</c>, <c>em</c>, …), in most values a delimiter (the <c>/</c> of
    /// <c>16 / 9</c>), or a hex colour it cannot parse fails the whole sheet, one with a function it does not know (a
    /// CSS math function, <c>cubic-bezier()</c>, <c>steps()</c>) is dropped, and one with a number its lexer reads
    /// otherwise than CSS does fails the sheet or is stored as other tokens (<c>3.px</c> as <c>3 px</c>). Such a value
    /// becomes a quoted string of its own text, which the importer stores as written and the theme readers take back
    /// as CSS (<see cref="StyleCustomProperties.Spell"/>).
    /// </summary>
    internal static class UssCustomPropertyText
    {
        // The functions StyleValueImporter reads besides resource(), matched as written; it drops every other one but
        // url() with a warning.
        private static readonly HashSet<string> s_importerFunctions = new(StringComparer.Ordinal)
        {
            "rgb", "rgba", "none", "filter", "var", "env", "linear-gradient", "tint", "opacity", "invert", "grayscale",
            "sepia", "blur", "contrast", "hue-rotate", "prop",
        };

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

        // Whether the importer would fail, drop or split value: a token it rejects or drops, or a number its lexer reads
        // otherwise than CSS does or with a unit it does not keep. A value holding a quote or a backslash is left as
        // written, so such a token in it still fails the importer.
        internal static bool NeedsQuoting(string value)
            => value.IndexOfAny(new[] { '"', '\'', '\\' }) < 0 && HasUnheldToken(value);

        // Reads value token by token as UI Toolkit's lexer does, and answers whether one is a token the importer fails
        // the sheet on or drops: a delimiter, a bracket no function name opens, a colon, a function it does not know
        // (a CSS math function, cubic-bezier(), steps()), a hex colour it cannot parse or CSS reads further, a number it
        // misreads, or a full stop no digit follows, which it drops after an integer and fails the sheet on elsewhere.
        // A value of identifiers and delimiters alone, which the importer keeps as one string instead, is quoted all
        // the same. What resource() encloses is left unread, as the importer takes it as a path whatever it holds.
        // UssValueImportTests holds each of those. What url() encloses is left unread too.
        private static bool HasUnheldToken(string value)
        {
            var functions = 0;
            var i = 0;
            var held = true;
            while (held && i < value.Length)
            {
                var c = value[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                }
                else if (c == ')' && functions > 0)
                {
                    functions--;
                    i++;
                }
                else
                {
                    held = ReadsHeldToken(value, ref i, ref functions);
                }
            }
            return !held;
        }

        // Whether the importer holds the token at i as CSS reads it, moving i past the token where it does.
        private static bool ReadsHeldToken(string value, ref int i, ref int functions)
        {
            if (value[i] == '#')
            {
                var digits = HexEnd(value, i + 1) - (i + 1);
                i += 1 + digits;
                return digits is 3 or 4 or 6 or 8 && (i == value.Length || !IsIdentChar(value[i]));
            }
            if (StartsIdentifier(value, i))
            {
                return ReadsHeldName(value, ref i, ref functions);
            }
            if (!StartsNumber(value, i))
            {
                return false;
            }
            if (value[i] is '+' or '-')
            {
                i++;
            }
            return !MisreadsNumber(value, ref i);
        }

        // An identifier or a function name: digits inside it are no number.
        private static bool ReadsHeldName(string value, ref int i, ref int functions)
        {
            var start = i;
            i = NameEnd(value, i + 1);
            if (i == value.Length || value[i] != '(')
            {
                return true;
            }
            var name = value.Substring(start, i - start);
            if (EnclosesAPath(name))
            {
                var close = value.IndexOf(')', i);
                i = close < 0 ? value.Length : close + 1;
                return true;
            }
            functions++;
            i++;
            return s_importerFunctions.Contains(name);
        }

        private static bool EnclosesAPath(string name)
            => string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "resource", StringComparison.Ordinal);

        private static bool StartsIdentifier(string value, int i)
        {
            var c = value[i];
            if (c == '-')
            {
                return i + 1 < value.Length && IsNameStart(value[i + 1]);
            }
            return IsNameStart(c);
        }

        private static int HexEnd(string value, int i)
        {
            while (i < value.Length && (IsDigit(value[i]) || value[i] is >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            {
                i++;
            }
            return i;
        }

        private static int NameEnd(string value, int i)
        {
            while (i < value.Length && IsIdentChar(value[i]))
            {
                i++;
            }
            return i;
        }

        private static bool StartsNumber(string value, int i)
        {
            if (value[i] is '+' or '-')
            {
                i++;
            }
            if (i < value.Length && value[i] == '.')
            {
                i++;
            }
            return i < value.Length && IsDigit(value[i]);
        }

        // Reads the number at i as UI Toolkit's lexer does, moving i past it, and answers whether that reading is not
        // CSS's or its unit is not one the importer keeps. The lexer reads no exponent, starts a unit at a `-`, a `_`
        // or a non-ASCII character as well as at an ASCII letter, and ends one at its first character that is not an
        // ASCII letter, where CSS reads on through digits, `-`, `_` and non-ASCII characters. UssValueImportTests
        // holds each of those.
        private static bool MisreadsNumber(string value, ref int i)
        {
            i = DigitsEnd(value, i);
            if (i + 1 < value.Length && value[i] == '.' && IsDigit(value[i + 1]))
            {
                i = DigitsEnd(value, i + 1);
            }
            if (i < value.Length && value[i] == '%')
            {
                i++;
                return false;
            }
            if (i == value.Length || !IsNameStart(value[i]))
            {
                return false;
            }
            var unit = i++;
            while (i < value.Length && IsAsciiLetter(value[i]))
            {
                i++;
            }
            return !s_importerUnits.Contains(value.Substring(unit, i - unit)) || (i < value.Length && IsIdentChar(value[i]));
        }

        private static int DigitsEnd(string value, int i)
        {
            while (i < value.Length && IsDigit(value[i]))
            {
                i++;
            }
            return i;
        }

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

        private static bool IsNameStart(char c) => IsAsciiLetter(c) || c is '-' or '_' || c > 127;
    }
}
