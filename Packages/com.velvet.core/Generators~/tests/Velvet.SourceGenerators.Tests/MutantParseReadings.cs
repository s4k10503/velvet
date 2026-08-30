using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// The configurations a source is read under when a guard has to see every line of it.
    /// </summary>
    /// <remarks>
    /// A line inside an <c>#if</c> region no symbol lights is trivia: a guard parsing with
    /// <c>CSharpParseOptions.Default</c> sees no tokens there and has nothing to check. Measured on
    /// this package, 124 of 3255 line-removal mutants sit on such lines.
    /// <para>
    /// Which symbols the second and third light is derived from the source rather than listed. The
    /// regions here are not limited to <c>UNITY_EDITOR</c>, and a hand list is the mirror shape this
    /// repository pins against elsewhere.
    /// </para>
    /// </remarks>
    internal static class MutantParseReadings
    {
        private static readonly Regex ConditionalSymbol = new(
            @"^\s*#\s*(?:if|elif)\s+(.*)$", RegexOptions.Multiline);

        /// <summary>Nothing defined, everything the source tests, and everything it never negates.</summary>
        /// <remarks>
        /// The third because a region nested inside one the second lights can turn on the absence of
        /// a symbol that one defines, which leaves it dark under both of the others.
        /// </remarks>
        internal static IEnumerable<CSharpParseOptions> For(string source)
        {
            var conditions = ConditionalSymbol.Matches(source)
                .Select(match => match.Groups[1].Value)
                .ToList();
            var symbols = conditions
                .SelectMany(Names)
                .Distinct(System.StringComparer.Ordinal)
                .ToList();
            var negated = conditions
                .SelectMany(condition => Regex.Matches(condition, @"!\s*([A-Za-z_][A-Za-z0-9_]*)")
                    .Select(match => match.Groups[1].Value))
                .ToHashSet(System.StringComparer.Ordinal);
            var sets = new List<List<string>>
            {
                new(),
                symbols,
                symbols.Where(name => !negated.Contains(name)).ToList(),
            };
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var set in sets.Where(set => seen.Add(
                         string.Join(",", set.OrderBy(name => name, System.StringComparer.Ordinal)))))
            {
                yield return CSharpParseOptions.Default.WithPreprocessorSymbols(set);
            }
        }

        /// <summary>The one of them that leaves the least of a source unread.</summary>
        /// <remarks>
        /// A guard comparing one parse against another needs a single reading, not three: the same
        /// line has to be a designation in both. The widest is the one that lights the most, and a
        /// region dark under it is one no configuration derived from the source lights.
        /// </remarks>
        internal static CSharpParseOptions Widest(string source) =>
            For(source).OrderByDescending(options => options.PreprocessorSymbolNames.Count()).First();

        private static IEnumerable<string> Names(string condition) =>
            Regex.Matches(condition, @"[A-Za-z_][A-Za-z0-9_]*")
                .Select(match => match.Value)
                .Where(name => name != "true" && name != "false");
    }
}
