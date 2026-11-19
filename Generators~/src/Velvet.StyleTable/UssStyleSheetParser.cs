using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Reads the flat rule/declaration structure of a USS stylesheet straight off its text.
    /// </summary>
    /// <remarks>
    /// The compiled <c>StyleSheet</c> asset would be the other candidate source, but its rule, property and
    /// property-id types are assembly-internal to UI Toolkit with no <c>InternalsVisibleTo</c> reaching a
    /// package assembly, so reading it would mean reflection. Text is the authored form the importer itself
    /// consumes.
    ///
    /// This is deliberately not a CSS parser: it recognises comments, at-rule statements, selector blocks and
    /// <c>name: value</c> declarations, and reports anything else instead of guessing. Selector *shape* is
    /// classified separately by <see cref="UssSelector"/>; a shape this parser accepts is not thereby a shape
    /// the utility table can model.
    /// </remarks>
    internal static class UssStyleSheetParser
    {
        public static UssSheet Parse(string path, string text)
        {
            var rules = ImmutableArray.CreateBuilder<UssRule>();
            var atRules = ImmutableArray.CreateBuilder<UssAtRule>();
            var errors = ImmutableArray.CreateBuilder<UssParseError>();

            var index = 0;
            while (true)
            {
                index = SkipTrivia(text, index, errors);
                if (index >= text.Length)
                {
                    break;
                }

                var headerStart = index;
                var headerEnd = FindHeaderEnd(text, index, errors);
                if (headerEnd < 0)
                {
                    errors.Add(new UssParseError(
                        "expected '{' or ';' after '" + Excerpt(text, headerStart) + "'",
                        headerStart));
                    break;
                }

                var header = text.Substring(headerStart, headerEnd - headerStart).Trim();
                if (text[headerEnd] == ';')
                {
                    atRules.Add(new UssAtRule(header, headerStart));
                    index = headerEnd + 1;
                    continue;
                }

                var bodyEnd = FindBlockEnd(text, headerEnd + 1);
                if (bodyEnd < 0)
                {
                    errors.Add(new UssParseError("unterminated rule block for '" + header + "'", headerStart));
                    break;
                }

                rules.Add(new UssRule(
                    header,
                    headerStart,
                    ParseDeclarations(text, headerEnd + 1, bodyEnd, errors)));
                index = bodyEnd + 1;
            }

            return new UssSheet(path, text, rules.ToImmutable(), atRules.ToImmutable(), errors.ToImmutable());
        }

        private static ImmutableArray<UssDeclaration> ParseDeclarations(
            string text, int start, int end, ImmutableArray<UssParseError>.Builder errors)
        {
            var declarations = ImmutableArray.CreateBuilder<UssDeclaration>();
            var index = start;
            while (index < end)
            {
                index = SkipTrivia(text, index, errors);
                if (index >= end)
                {
                    break;
                }

                var declarationStart = index;
                var depth = 0;
                while (index < end && (depth > 0 || text[index] != ';'))
                {
                    var c = text[index];
                    if (c == '(')
                    {
                        depth++;
                    }
                    else if (c == ')')
                    {
                        depth--;
                    }
                    else if (c == '/' && index + 1 < end && text[index + 1] == '*')
                    {
                        index = SkipTrivia(text, index, errors);
                        continue;
                    }
                    index++;
                }

                // The scan above steps OVER a comment to find the terminating ';', so the text it cut still
                // contains one. Values are read, not just property names, and a comment inside a value would
                // otherwise reach the vocabulary as a token.
                var declaration = StripComments(text.Substring(declarationStart, index - declarationStart)).Trim();
                index++;
                if (declaration.Length == 0)
                {
                    continue;
                }

                var colon = declaration.IndexOf(':');
                if (colon <= 0)
                {
                    errors.Add(new UssParseError(
                        "declaration '" + declaration + "' is not a 'name: value' pair",
                        declarationStart));
                    continue;
                }

                declarations.Add(new UssDeclaration(
                    declaration.Substring(0, colon).Trim(),
                    declaration.Substring(colon + 1).Trim(),
                    declarationStart));
            }
            return declarations.ToImmutable();
        }

        private static string StripComments(string declaration)
        {
            var open = declaration.IndexOf("/*", StringComparison.Ordinal);
            if (open < 0)
            {
                return declaration;
            }
            var stripped = new StringBuilder(declaration.Length);
            var index = 0;
            while (open >= 0)
            {
                stripped.Append(declaration, index, open - index);
                var close = declaration.IndexOf("*/", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    return stripped.ToString();
                }
                index = close + 2;
                open = declaration.IndexOf("/*", index, StringComparison.Ordinal);
            }
            stripped.Append(declaration, index, declaration.Length - index);
            return stripped.ToString();
        }

        /// <summary>
        /// Advances past whitespace and block comments. A comment can sit anywhere a space can, so every
        /// scanning loop routes through here rather than testing for '/' at its own call sites.
        /// </summary>
        private static int SkipTrivia(string text, int index, ImmutableArray<UssParseError>.Builder errors)
        {
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }
                if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (close < 0)
                    {
                        errors.Add(new UssParseError("unterminated comment", index));
                        return text.Length;
                    }
                    index = close + 2;
                    continue;
                }
                return index;
            }
            return index;
        }

        private static int FindHeaderEnd(string text, int index, ImmutableArray<UssParseError>.Builder errors)
        {
            while (index < text.Length)
            {
                var c = text[index];
                if (c == '{' || c == ';')
                {
                    return index;
                }
                if (c == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    index = SkipTrivia(text, index, errors);
                    continue;
                }
                index++;
            }
            return -1;
        }

        private static int FindBlockEnd(string text, int index)
        {
            var depth = 1;
            while (index < text.Length)
            {
                var c = text[index];
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }
                }
                index++;
            }
            return -1;
        }

        private static string Excerpt(string text, int start) =>
            text.Substring(start, Math.Min(40, text.Length - start)).Trim();
    }

    /// <summary>One parsed stylesheet: its rules, its at-rule statements and anything unparseable in it.</summary>
    internal sealed class UssSheet
    {
        private readonly List<int> _lineStarts;

        public UssSheet(
            string path,
            string text,
            ImmutableArray<UssRule> rules,
            ImmutableArray<UssAtRule> atRules,
            ImmutableArray<UssParseError> errors)
        {
            Path = path;
            Rules = rules;
            AtRules = atRules;
            Errors = errors;
            _lineStarts = BuildLineStarts(text);
        }

        public string Path { get; }

        public ImmutableArray<UssRule> Rules { get; }

        public ImmutableArray<UssAtRule> AtRules { get; }

        public ImmutableArray<UssParseError> Errors { get; }

        /// <summary>
        /// Builds a problem located at <paramref name="offset"/>, so the message points at the offending rule
        /// rather than at the file as a whole.
        /// </summary>
        public UssProblem ProblemAt(string code, string message, int offset)
        {
            var line = 0;
            var high = _lineStarts.Count - 1;
            while (line < high)
            {
                var mid = (line + high + 1) / 2;
                if (_lineStarts[mid] <= offset)
                {
                    line = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return new UssProblem(code, message, Path, line + 1, offset - _lineStarts[line] + 1);
        }

        private static List<int> BuildLineStarts(string text)
        {
            var starts = new List<int> { 0 };
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    starts.Add(i + 1);
                }
            }
            return starts;
        }
    }

    internal readonly struct UssRule
    {
        public UssRule(string selector, int offset, ImmutableArray<UssDeclaration> declarations)
        {
            Selector = selector;
            Offset = offset;
            Declarations = declarations;
        }

        public string Selector { get; }

        public int Offset { get; }

        public ImmutableArray<UssDeclaration> Declarations { get; }

        /// <summary>
        /// A rule that declares custom properties and nothing else, which is what <c>:root</c> and the theme
        /// blocks beside it are. What it declares is read through <c>var()</c> by other rules, so the class
        /// or pseudo-class it is keyed on sets no property on the element that matches it.
        /// </summary>
        public bool IsTokenBlock
        {
            get
            {
                if (Declarations.IsEmpty)
                {
                    return false;
                }
                foreach (var declaration in Declarations)
                {
                    if (!declaration.IsCustomProperty)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

    internal readonly struct UssDeclaration
    {
        public UssDeclaration(string property, string value, int offset)
        {
            Property = property;
            Value = value;
            Offset = offset;
        }

        public string Property { get; }

        /// <summary>
        /// The text right of the colon, uninterpreted. Only <c>transition-property</c> needs it: its value is
        /// itself a list of property names, so which properties an element transitions cannot be answered from
        /// the declared property alone.
        /// </summary>
        public string Value { get; }

        public int Offset { get; }

        public bool IsCustomProperty => Property.StartsWith("--", StringComparison.Ordinal);
    }

    internal readonly struct UssAtRule
    {
        public UssAtRule(string text, int offset)
        {
            Text = text;
            Offset = offset;
        }

        public string Text { get; }

        public int Offset { get; }
    }

    internal readonly struct UssParseError
    {
        public UssParseError(string message, int offset)
        {
            Message = message;
            Offset = offset;
        }

        public string Message { get; }

        public int Offset { get; }
    }
}
