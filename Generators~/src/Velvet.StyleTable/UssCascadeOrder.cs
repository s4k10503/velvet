using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Velvet.StyleTable
{
    /// <summary>
    /// Reads a stylesheet directory in the order the importer flattens it to: the partials an aggregator
    /// <c>@import</c>s, in that list's order, then the aggregator itself.
    /// </summary>
    /// <remarks>
    /// An imported sheet is spliced in AHEAD of the importing sheet's own rules, so the aggregator holds the
    /// last position and its rules outrank every partial's. Placing it first instead would invert that the day
    /// the aggregator stops being nothing but <c>@import</c> statements, and nothing about its current content
    /// would have made the inversion visible.
    ///
    /// Name order is not cascade order and is not close to it — <c>_animations.uss</c> sorts first and is
    /// imported last — so a derivation that records which of two rules wins cannot read the directory
    /// alphabetically. A file no aggregator imports lands after everything, where
    /// <see cref="StyleUtilityTableBuilder"/> reports it rather than letting it claim a position the importer
    /// never gives it.
    /// </remarks>
    internal static class UssCascadeOrder
    {
        private static readonly Regex ImportStatement = new Regex(@"@import\s+url\(""([^""]+)""\)");

        /// <summary>The names an aggregator imports, in declaration order.</summary>
        public static IEnumerable<string> ImportedNames(string aggregatorText) =>
            ImportStatement.Matches(aggregatorText).Cast<Match>().Select(match => match.Groups[1].Value);

        public static bool DeclaresImport(string sheetText) => ImportStatement.IsMatch(sheetText);

        public static IReadOnlyList<UssSourceText> SheetsIn(string directory)
        {
            var pending = Directory.EnumerateFiles(directory, "*.uss")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new UssSourceText(path, File.ReadAllText(path)))
                .ToList();

            var ordered = new List<UssSourceText>();
            foreach (var aggregator in pending.Where(sheet => DeclaresImport(sheet.Text)).ToList())
            {
                foreach (var imported in ImportedNames(aggregator.Text))
                {
                    Take(pending, Path.GetFileName(imported), ordered);
                }
                Take(pending, Path.GetFileName(aggregator.Path), ordered);
            }
            ordered.AddRange(pending);
            return ordered;
        }

        private static void Take(List<UssSourceText> pending, string fileName, List<UssSourceText> ordered)
        {
            var index = pending.FindIndex(
                sheet => string.Equals(Path.GetFileName(sheet.Path), fileName, StringComparison.Ordinal));
            if (index >= 0)
            {
                ordered.Add(pending[index]);
                pending.RemoveAt(index);
            }
        }
    }
}
