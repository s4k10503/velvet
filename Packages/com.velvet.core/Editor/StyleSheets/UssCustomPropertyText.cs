#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Velvet.Editor
{
    /// <summary>
    /// Rewrites the custom properties of a USS file that UI Toolkit's importer cannot hold: a value with a unit
    /// outside the ones it keeps (<c>rem</c>, <c>em</c>, …), in most values a delimiter (the <c>/</c> of
    /// <c>16 / 9</c>), a hex colour it cannot parse, or a URL its lexer cannot read (<c>url(a b)</c>) fails the whole
    /// sheet, one with a function it does not know (a CSS math function, <c>cubic-bezier()</c>, <c>steps()</c>) is
    /// dropped, and one with a number its lexer reads
    /// otherwise than CSS does fails the sheet or is stored as other tokens (<c>3.px</c> as <c>3 px</c>), as an
    /// <c>rgb()</c> holding a percentage is stored as another colour (<c>rgb(0 0 0 / 50%)</c> as opaque). Such a value
    /// becomes a quoted string of its own text, which the importer stores as written and the theme readers take back
    /// as CSS (<see cref="StyleCustomProperties.Spell"/>).
    /// </summary>
    internal static class UssCustomPropertyText
    {
        // The functions StyleValueImporter reads by a name in any case, besides filter(), rgb() and rgba(), which it
        // reads only as written, and resource(); it drops every other one its lexer does not take as a path with a
        // warning.
        private static readonly HashSet<string> s_importerFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "var", "env", "linear-gradient", "none", "tint", "opacity", "invert", "grayscale", "sepia", "blur",
            "contrast", "hue-rotate", "prop",
        };

        // The units StyleValueImporter's unit table maps; every other one is an error that leaves the sheet empty.
        private static readonly HashSet<string> s_importerUnits = new(StringComparer.Ordinal)
        {
            "px", "s", "ms", "deg", "grad", "rad", "turn",
        };

        // In NumberShape's letters: three integers separated alike, and for rgba() an alpha after them.
        private static readonly HashSet<string> s_heldChannels = new(StringComparer.Ordinal) { "i,i,i", "iii" };

        private static readonly HashSet<string> s_heldAlphaChannels = new(StringComparer.Ordinal)
        {
            "i,i,i,i", "i,i,i,n", "iii/i", "iii/n",
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
                if (SkipWhole(uss, ref i, output))
                {
                    // An escape begins the name that stands there, so a `--` after it is no custom property's.
                    atStart &= c != '\\';
                    continue;
                }
                if (c == '(' && StepsOverPath(uss, i))
                {
                    var end = PathEnd(uss, i);
                    output.Append(uss, i, end - i);
                    i = end;
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

        // A comment, a string or an escape, copied through whole.
        private static bool SkipWhole(string text, ref int i, StringBuilder? output)
        {
            var end = WholeEnd(text, i);
            if (end == i)
            {
                return false;
            }
            output?.Append(text, i, end - i);
            i = end;
            return true;
        }

        private static int WholeEnd(string text, int i)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                // MUTANT_SURVIVES(equivalent, boundary): the search starts at i + 2, so a match is never at 0.
                return close < 0 ? text.Length : close + 2;
            }
            if (c == '\\')
            {
                return Math.Min(EscapeLast(text, i + 1) + 1, text.Length);
            }
            if (c != '"' && c != '\'')
            {
                return i;
            }
            // A line break or a form feed no escape takes ends the string, as CSS ends a bad string there; it is taken
            // with the string so Flatten keeps it as written.
            for (var j = i + 1; j < text.Length; j++)
            {
                if (text[j] == '\\')
                {
                    j = EscapeLast(text, j + 1);
                }
                else if (text[j] == c)
                {
                    return j + 1;
                }
                else if (text[j] is '\n' or '\r' or '\f')
                {
                    return CharacterLast(text, j) + 1;
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
                else if (!SkipWhole(uss, ref colon, null))
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
            var written = uss.Substring(valueStart, valueEnd - valueStart);
            var value = Flatten(written, out var badUrl);
            var important = SplitImportant(ref value, out var importantAsWritten);
            value = value.Trim();
            var quoted = badUrl || NeedsQuoting(value, written);
            if (!quoted && importantAsWritten)
            {
                return;
            }
            var text = quoted ? Quote(value) : value;
            // The line breaks the value held and the text does not, so an importer message names the line the author
            // wrote; UssValueImportTests holds that the line of a property after it stays.
            var newlines = LineBreaks(written) - LineBreaks(text);
            output.Append(uss, i, valueStart - i).Append(' ').Append(text).Append(important).Append('\n', newlines);
            i = valueEnd;
        }

        // value as a string the importer's lexer reads back as value. The lexer fails the sheet on a raw line break or
        // form feed inside the quotes, and reads each escape written for one back as the character, its space ending it
        // before a hex digit can join it; UssValueImportTests holds both.
        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (var c in value)
            {
                if (c is '"' or '\\')
                {
                    text.Append('\\').Append(c);
                }
                else if (c is '\r' or '\n' or '\f')
                {
                    text.Append('\\').Append(c == '\r' ? 'd' : c == '\n' ? 'a' : 'c').Append(' ');
                }
                else
                {
                    text.Append(c);
                }
            }
            return text.Append('"').ToString();
        }

        // The lines the importer's lexer counts: a CR and the LF after it make one, and a CR alone makes one too.
        private static int LineBreaks(string text)
        {
            var count = 0;
            for (var k = 0; k < text.Length; k++)
            {
                count += text[k] == '\n' || (text[k] == '\r' && At(text, k + 1) != '\n') ? 1 : 0;
            }
            return count;
        }

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c is '-' or '_' || c > 127;

        // The `;` or `}` that ends the declaration whose value begins at start, outside any parenthesis.
        private static int ValueEnd(string text, int start)
        {
            var parens = 0;
            var i = start;
            while (i < text.Length)
            {
                if (SkipWhole(text, ref i, null))
                {
                    continue;
                }
                var c = text[i];
                if (c == '(')
                {
                    parens++;
                    if (StepsOverPath(text, i))
                    {
                        i = PathEnd(text, i);
                        continue;
                    }
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

        // A comment becomes a space, and so does a line break or a form feed, while a string, an escape and an unquoted
        // URL path are kept whole, so what reads as one inside them stays. badUrl says whether the importer's lexer
        // raises an error on a URL path, which fails the sheet.
        private static string Flatten(string value, out bool badUrl)
        {
            badUrl = false;
            var text = new StringBuilder(value.Length);
            var i = 0;
            while (i < value.Length)
            {
                var end = WholeEnd(value, i);
                if (value[i] == '(' && IsUrl(FunctionName(value, i)))
                {
                    badUrl |= UrlEnd(value, i) == -1;
                    // Kept as written after each function the importer's lexer reads a path in, so HasUnheldToken meets
                    // after a path it reads what the lexer meets there (`domain(a/*)*/b)`).
                    end = PathEnd(value, i);
                }
                if (end == i)
                {
                    var c = value[i];
                    text.Append(c is '\r' or '\n' or '\f' ? ' ' : c);
                    i++;
                    continue;
                }
                if (value[i] == '/')
                {
                    text.Append(' ');
                }
                else
                {
                    text.Append(value, i, end - i);
                }
                i = end;
            }
            return text.ToString();
        }

        // The `!important` that closes a declaration, which stays outside the quotes and is written in lower case, the
        // only spelling UI Toolkit's parser takes for it; asWritten says whether the declaration already spells it so.
        private static string SplitImportant(ref string value, out bool asWritten)
        {
            asWritten = true;
            var bang = value.LastIndexOf('!');
            var keyword = bang < 0 || IsEscaped(value, bang) ? string.Empty : value.Substring(bang + 1).Trim();
            if (!string.Equals(keyword, "important", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }
            asWritten = string.Equals(keyword, "important", StringComparison.Ordinal);
            value = value.Substring(0, bang);
            return " !important";
        }

        // Whether an odd run of backslashes comes before the character at at, escaping it.
        private static bool IsEscaped(string text, int at)
        {
            var slashes = 0;
            while (at - slashes > 0 && text[at - slashes - 1] == '\\')
            {
                slashes++;
            }
            return slashes % 2 == 1;
        }

        // Whether the importer would fail, drop or misread value: a token HasUnheldToken finds, or a comment
        // HasCommentInArguments finds in written, the value before its comments are taken out. A value holding a quote
        // or a backslash is left as written unless its URL is one UrlEnd refuses, so such a token in it still fails
        // the importer.
        internal static bool NeedsQuoting(string value, string written)
            => value.IndexOfAny(new[] { '"', '\'', '\\' }) < 0 && (HasUnheldToken(value) || HasCommentInArguments(written));

        // The importer's lexer hands a function a comment among its arguments as a token, which rgb() and rgba() pass
        // over, blur(), var() and linear-gradient() fail the sheet on, and resource() stores, left unread here as in
        // HasUnheldToken; it reads a URL function's `/*` as part of the path. UssValueImportTests holds each of those.
        private static bool HasCommentInArguments(string written)
        {
            var functions = new Stack<string>();
            var k = 0;
            while (k < written.Length)
            {
                var comment = written[k] == '/' ? WholeEnd(written, k) : k;
                if (comment > k)
                {
                    if (functions.TryPeek(out var function) && function is not ("rgb" or "rgba"))
                    {
                        return true;
                    }
                    k = comment;
                    continue;
                }
                if (written[k] == '(')
                {
                    var name = FunctionName(written, k);
                    if (IsUrl(name) || name == "resource")
                    {
                        k = CloseOf(written, k);
                    }
                    else
                    {
                        functions.Push(name);
                    }
                }
                else if (written[k] == ')')
                {
                    functions.TryPop(out _);
                }
                k++;
            }
            return false;
        }

        // Reads value token by token as UI Toolkit's lexer does, and answers whether one is a token the importer fails
        // the sheet on, drops or reads otherwise than CSS: a delimiter, a bracket no function name opens, a colon, a
        // function it does not know (a CSS math function, cubic-bezier(), steps()), one it fails the sheet on with
        // nothing in it or, for var(), with arguments ValidateVarFunction refuses, an rgb() or rgba() in another shape
        // than ReadsHeldChannels holds, a hex colour it cannot parse or CSS reads further, a range, a number it
        // misreads, or a full stop no digit follows, which it drops after an integer and fails the sheet on elsewhere.
        // A value of identifiers and delimiters alone, which the importer keeps as one string instead, is quoted all
        // the same. What a resource() with something in it encloses is left unread, as the importer takes it as a path
        // whatever it holds. UssValueImportTests holds each of those. What the URL functions enclose is left to
        // UrlEnd.
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
            // The lexer reads a lone `u` that `+` and a hex digit or `?` follow as a range, which VisitToken fails the
            // sheet on; where a letter or `?` follows, the `+` is no number, and ReadsHeldToken quotes it already.
            if (i - start == 1 && value[start] is 'u' or 'U' && At(value, i) == '+' && IsDigit(At(value, i + 1)))
            {
                return false;
            }
            if (i == value.Length || value[i] != '(')
            {
                return true;
            }
            var name = value.Substring(start, i - start);
            if (IsUrl(name))
            {
                i = CloseOf(value, i) + 1;
                return true;
            }
            if (i + 1 == value.Length || value[i + 1] == ')')
            {
                // The importer takes none() and filter() with nothing between their parentheses, and fails the sheet on
                // any other function with nothing there.
                i = CloseOf(value, i) + 1;
                return name is "none" or "filter";
            }
            if (name == "resource")
            {
                i = CloseOf(value, i) + 1;
                return true;
            }
            if (name is "rgb" or "rgba")
            {
                return ReadsHeldChannels(value, ref i, alpha: name == "rgba");
            }
            if (name == "var" && !HoldsVarArguments(value, i))
            {
                // MUTANT_SURVIVES(equivalent, literal): i is left at the parenthesis, which the caller then reads as one no function name opens.
                return false;
            }
            functions++;
            i++;
            return name == "filter" || s_importerFunctions.Contains(name);
        }

        // The `)` that closes the `(` at open, or the end of value where none does.
        private static int MatchOf(string value, int open)
        {
            var depth = 0;
            for (var k = open; k < value.Length; k++)
            {
                if (value[k] == '(')
                {
                    depth++;
                }
                else if (value[k] == ')' && --depth == 0)
                {
                    return k;
                }
            }
            return value.Length;
        }

        // Where the importer's lexer ends the token at k, a function's arguments included.
        private static int TokenEnd(string value, int k)
        {
            if (char.IsWhiteSpace(value[k]))
            {
                return SpaceEnd(value, k);
            }
            if (value[k] == '#')
            {
                return HexEnd(value, k + 1);
            }
            if (StartsIdentifier(value, k))
            {
                k = NameEnd(value, k + 1);
                return k < value.Length && value[k] == '(' ? MatchOf(value, k) + 1 : k;
            }
            if (!StartsNumber(value, k))
            {
                return k + 1;
            }
            if (value[k] is '+' or '-')
            {
                k++;
            }
            MisreadsNumber(value, ref k);
            return k;
        }

        // The arguments of rgb() or rgba() from the `(` at i, moving i past their `)`, held only in the shapes
        // s_heldChannels and s_heldAlphaChannels list. TryCreateColorFromFunctionToken counts number tokens and passes
        // over every other one, so a percentage, a function or a unit among them is lost or fails the sheet, and it
        // scales no channel when one of the first three holds a fraction; UssValueImportTests holds each of those.
        private static bool ReadsHeldChannels(string value, ref int i, bool alpha)
        {
            var close = MatchOf(value, i);
            var shape = new StringBuilder();
            for (var k = i + 1; k < close;)
            {
                var end = TokenEnd(value, k);
                if (!char.IsWhiteSpace(value[k]))
                {
                    shape.Append(value[k] is ',' or '/' ? value[k] : NumberShape(value, k, end));
                }
                k = end;
            }
            i = close + 1;
            return (alpha ? s_heldAlphaChannels : s_heldChannels).Contains(shape.ToString());
        }

        // `i` for the token from k to end that is a plain integer, `n` for one with a fraction, `x` for anything else.
        private static char NumberShape(string value, int k, int end)
        {
            if (value[k] is '+' or '-')
            {
                k++;
            }
            var digits = DigitsEnd(value, k);
            if (digits == end)
            {
                return digits > k ? 'i' : 'x';
            }
            return value[digits] == '.' && digits + 1 < end && DigitsEnd(value, digits + 1) == end ? 'n' : 'x';
        }

        // ValidateVarFunction, from the `(` at open: nothing but whitespace, or a name of `--` and at least one more
        // character, then nothing, or a comma and a fallback holding no second comma. It passes over the token after
        // the comma unread, and the fallback must leave one there.
        private static bool HoldsVarArguments(string value, int open)
        {
            var close = MatchOf(value, open);
            var k = SpaceEnd(value, open + 1);
            if (k == close)
            {
                return true;
            }
            var name = value.Substring(k, NameEnd(value, k) - k);
            if (name.Length < 3 || !name.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }
            var comma = false;
            for (k += name.Length; k < close;)
            {
                if (char.IsWhiteSpace(value[k]))
                {
                    k++;
                }
                else if (value[k] != ',')
                {
                    if (!comma)
                    {
                        return false;
                    }
                    k = TokenEnd(value, k);
                }
                else if (comma || value.Substring(k + 1, close - k - 1).Trim().Length == 0)
                {
                    return false;
                }
                else
                {
                    comma = true;
                    k = TokenEnd(value, k + 1);
                }
            }
            return true;
        }

        private static int SpaceEnd(string value, int k)
        {
            // MUTANT_SURVIVES(equivalent, boundary): value is trimmed and every argument list ends at a `)` or before the trim, so whitespace never runs to its end.
            while (k < value.Length && char.IsWhiteSpace(value[k]))
            {
                k++;
            }
            return k;
        }

        // The `)` after the `(` at open, or the end of value where none follows.
        private static int CloseOf(string value, int open)
        {
            var close = value.IndexOf(')', open);
            // MUTANT_SURVIVES(equivalent, boundary): the search starts at a parenthesis after a name, so a match is never at 0.
            return close < 0 ? value.Length : close;
        }

        // The importer's lexer takes url(), url-prefix() and domain() in any case as a path rather than as tokens, which
        // the importer stores where the lexer accepts it, empty included.
        private static bool IsUrl(string name)
            => name.Equals("url", StringComparison.OrdinalIgnoreCase) || name.Equals("url-prefix", StringComparison.OrdinalIgnoreCase)
               || name.Equals("domain", StringComparison.OrdinalIgnoreCase);

        // Whether a reading by token steps over the path after the `(` at open: after `url`, the one name CSS reads a path
        // after, and after the other URL functions where the importer's lexer reads the path, as the importer then holds
        // it whatever CSS reads there, and fails a sheet holding one it refuses; UssValueImportTests holds both.
        private static bool StepsOverPath(string text, int open)
        {
            var name = FunctionName(text, open);
            return name.Equals("url", StringComparison.OrdinalIgnoreCase) || (IsUrl(name) && UrlEnd(text, open) != -1);
        }

        // The name before the `(` at open as the importer's lexer reads it, an escape in it standing for its character, so
        // `\"url(` names `"url` and `\75rl(` names `url`.
        private static string FunctionName(string text, int open)
        {
            var name = new StringBuilder();
            for (var k = NameStart(text, open); k < open; k++)
            {
                if (text[k] != '\\')
                {
                    name.Append(text[k]);
                    continue;
                }
                name.Append(Escaped(text, k + 1));
                k = EscapeLast(text, k + 1);
            }
            return name.ToString();
        }

        // Where the name that ends at end begins, its escapes included: a name character, a backslash escaping the one
        // after it, a character other than a line break that a backslash escapes, and the whitespace that ends a hex
        // escape, a CR and the LF after it being one character to the lexer. A backslash right before end escapes the
        // `(` there, and the name it then ends in names no function.
        private static int NameStart(string text, int end)
        {
            var start = end;
            while (start > 0)
            {
                var c = text[start - 1];
                var escape = IsUrlSpace(c) ? HexEscapeStart(text, start - 1) : -1;
                if (IsIdentChar(c) || (c == '\\' && !IsEscaped(text, start - 1)))
                {
                    start--;
                }
                else if (c is not ('\n' or '\r') && IsEscaped(text, start - 1))
                {
                    start -= 2;
                }
                else if (escape >= 0)
                {
                    start = escape;
                }
                else
                {
                    return start;
                }
            }
            return 0;
        }

        // The backslash of the hex escape that the whitespace at space ends, or -1 where none does.
        private static int HexEscapeStart(string text, int space)
        {
            var digitsEnd = text[space] == '\n' && space > 0 && text[space - 1] == '\r' ? space - 1 : space;
            var digits = digitsEnd;
            while (digits > 0 && digitsEnd - digits < 6 && IsHex(text[digits - 1]))
            {
                digits--;
            }
            return digits < digitsEnd && IsEscaped(text, digits) ? digits - 1 : -1;
        }

        // The character the escape whose first character after the backslash is at k stands for, a hex escape past U+FFFD
        // reading as U+FFFD, which no name it is compared with holds.
        private static char Escaped(string text, int k)
        {
            var end = k;
            while (end - k < 6 && IsHex(At(text, end)))
            {
                end++;
            }
            if (end == k)
            {
                return At(text, k);
            }
            var code = Convert.ToInt32(text.Substring(k, end - k), 16);
            return (char)Math.Min(code, 0xFFFD);
        }

        // Where a reading by token resumes after the `(` at open of a URL function: past it where the path is quoted, its
        // quote opening a string, and otherwise at the first `)` no backslash escapes, or the end of text where none
        // does, which is where CSS ends a `url()`, bad or not. A path UrlEnd refuses ends there rather than where the
        // importer's lexer ends it, as the importer fails a sheet holding one.
        private static int PathEnd(string text, int open)
        {
            if (At(text, UrlSpaceEnd(text, open + 1)) is '"' or '\'')
            {
                return open + 1;
            }
            for (var k = open + 1; k < text.Length; k++)
            {
                if (text[k] == ')')
                {
                    return k;
                }
                if (text[k] == '\\')
                {
                    k++;
                }
            }
            return text.Length;
        }

        // The `)` that ends the URL function whose `(` is at open as the importer's lexer reads its path, the end of
        // text where none does, or -1 where the lexer raises an error on the path, which fails the sheet: nothing after
        // the `(`; in a quoted path, a line break no escape takes or a backslash the text ends after; anything but
        // whitespace between a quoted path and the `)`; and in an unquoted one, whitespace before anything but the `)`,
        // a quote, a `(`, a control character other than a vertical tab, or a backslash a line break or the end of text
        // follows. UssValueImportTests holds each of those.
        private static int UrlEnd(string text, int open)
        {
            var k = UrlSpaceEnd(text, open + 1);
            var first = At(text, k);
            if (first == Eof)
            {
                return -1;
            }
            return first is '"' or '\'' ? QuotedUrlEnd(text, k) : UnquotedUrlEnd(text, k);
        }

        // From the quote that opens the path at k.
        private static int QuotedUrlEnd(string text, int k)
        {
            var quote = text[k];
            while (true)
            {
                var c = At(text, ++k);
                if (c == Eof)
                {
                    return k;
                }
                if (c == quote)
                {
                    return ParenthesisAt(text, UrlSpaceEnd(text, k + 1));
                }
                if (c is '\n' or '\r')
                {
                    return -1;
                }
                if (c == '\\')
                {
                    if (At(text, k + 1) == Eof)
                    {
                        return -1;
                    }
                    k = EscapeLast(text, k + 1);
                }
            }
        }

        // From the first character of the path at k.
        private static int UnquotedUrlEnd(string text, int k)
        {
            for (; ; k++)
            {
                var c = At(text, k);
                if (c is ')' or Eof)
                {
                    return k;
                }
                if (IsUrlSpace(c))
                {
                    return ParenthesisAt(text, UrlSpaceEnd(text, k));
                }
                if (BreaksUnquotedUrl(text, k))
                {
                    return -1;
                }
                if (c == '\\')
                {
                    k = EscapeLast(text, k + 1);
                }
            }
        }

        private static bool BreaksUnquotedUrl(string text, int k)
        {
            var c = text[k];
            if (c is '"' or '\'' or '(')
            {
                return true;
            }
            if (c == '\\')
            {
                return At(text, k + 1) is '\n' or '\r' or Eof;
            }
            return char.IsControl(c) && c != '\v';
        }

        private static int ParenthesisAt(string text, int k) => At(text, k) == ')' ? k : -1;

        // The last character ConsumeEscape reads of the escape whose first character after the backslash is at k:
        // that character, or up to six hex digits and one whitespace character after them, a CR and the LF after it
        // being one character to the lexer.
        private static int EscapeLast(string text, int k)
        {
            var end = k;
            while (end - k < 6 && IsHex(At(text, end)))
            {
                end++;
            }
            if (end == k)
            {
                return CharacterLast(text, k);
            }
            return IsUrlSpace(At(text, end)) ? CharacterLast(text, end) : end - 1;
        }

        private static int CharacterLast(string text, int k) => At(text, k) == '\r' && At(text, k + 1) == '\n' ? k + 1 : k;

        private static int UrlSpaceEnd(string text, int k)
        {
            while (IsUrlSpace(At(text, k)))
            {
                k++;
            }
            return k;
        }

        // The whitespace the importer's lexer skips in a URL function; a vertical tab is not among it.
        private static bool IsUrlSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

        // The character the lexer reads at k, or Eof past the end of text, as the lexer's own end marker.
        private static char At(string text, int k) => k < text.Length ? text[k] : Eof;

        private const char Eof = '￿';

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
            while (i < value.Length && IsHex(value[i]))
            {
                i++;
            }
            return i;
        }

        private static bool IsHex(char c) => IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';

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
