using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Velvet.SourceGenerators.Tests
{
    /// <summary>
    /// Re-derives, from the bundled stylesheets, whether each utility family <c>setup.md</c> presents as
    /// independent of them really is — so the guide's claim is held by the sheets rather than by whoever
    /// last edited the sentence.
    /// </summary>
    /// <remarks>
    /// The same rule the property table follows: a fact the stylesheets own is derived from them, never
    /// restated. This one is "which families the sheets declare nothing for", and it had been written by
    /// hand and been wrong three times running.
    /// <para>
    /// WHAT THIS CANNOT DO, because a reader of the guide will assume otherwise. It checks only that a
    /// family named as sheet-independent has no declaration in any bundled sheet. It does NOT check that
    /// the guide's sample is COMPLETE: that would need the set of families Velvet realises in C#, which no
    /// single source holds — the filter registry, the gap and divide class parsers, and the paint layers
    /// each know their own, and nothing enumerates them together. Deriving that set by pattern-matching
    /// C# string literals was tried and rejected: separating a class prefix from every other literal needs
    /// a hand-maintained shape rule plus an allowlist, which relocates the hand-written list rather than
    /// removing it. So an omission from the sample stays invisible here, which is why the guide says in
    /// its own words that it is a sample.
    /// </para>
    /// <para>
    /// It also does not distinguish WHY a family has no rule — <c>gap-*</c> is written by a manipulator and
    /// <c>shadow-*</c> by a paint layer, and to this guard they are one fact — nor does it show that such a
    /// family works with no sheet attached. That last one is behaviour, measured for <c>gap-*</c> alone by
    /// <c>BundledStyleUtilitiesRuntimeTests</c> on a real panel.
    /// </para>
    /// </remarks>
    public sealed class UtilityFamilyDocumentationTests
    {
        // The count is a floor, not the exact 2138 the sheets currently declare: an exact figure turns
        // every added utility into a failure here, and the job of the number is only to prove the
        // extraction still matches something. The named classes below are what make a broken pattern fail
        // loudly — a regex that silently stopped matching would otherwise leave every claim trivially true.
        private const int MinimumPlausibleSelectorCount = 1500;

        private static readonly string[] KnownDeclaredClasses =
            { "flex-row", "bg-blue-500", "rounded-lg", "truncate" };

        private static readonly Regex ClassSelectorPattern =
            new(@"^\.([a-zA-Z0-9_-]+)", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex MarkedRegionPattern = new(
            @"<!--\s*sheet-independent:begin.*?-->(.*?)<!--\s*sheet-independent:end\s*-->",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // A class token, not an arbitrary value and not a path: brackets, slashes and dots are what the
        // surrounding prose uses for those, and none of them appear in a utility family name.
        private static readonly Regex ClassTokenPattern =
            new(@"`([a-z][a-z0-9-]*\*?)`", RegexOptions.Compiled);

        [Fact]
        public void Given_TheBundledSheets_When_SelectorsAreExtracted_Then_TheExtractionStillMatches()
        {
            // Arrange / Act
            var declared = DeclaredClasses();

            // Assert — the count and the known names travel together, because either alone is satisfiable
            // by a pattern that has quietly stopped working: a plausible count with the wrong names, or the
            // right names scraped out of a corpus that lost most of its rules.
            Assert.Equal(
                (true, string.Join(",", KnownDeclaredClasses)),
                (declared.Count >= MinimumPlausibleSelectorCount,
                 string.Join(",", KnownDeclaredClasses.Where(declared.Contains))));
        }

        [Fact]
        public void Given_TheFamiliesSetupMdCallsSheetIndependent_When_CheckedAgainstTheSheets_Then_NoneIsDeclared()
        {
            // Arrange
            var declared = DeclaredClasses();
            var claimed = ClaimedSheetIndependentFamilies();
            Assume.NotEmpty(claimed, "setup.md's marked region names at least one family");

            // Act — a family is contradicted when the sheets declare any class under its prefix.
            var contradicted = claimed
                .Where(family => declared.Any(d => Matches(family, d)))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            // Assert
            Assert.Equal("", string.Join(", ", contradicted));
        }

        private static bool Matches(string family, string declaredClass) =>
            family.EndsWith("*", StringComparison.Ordinal)
                ? declaredClass.StartsWith(family.TrimEnd('*'), StringComparison.Ordinal)
                : declaredClass == family;

        private static IReadOnlyCollection<string> ClaimedSheetIndependentFamilies()
        {
            var text = File.ReadAllText(Path.Combine(SolutionPaths.DocumentationRoot(), "setup.md"));
            var region = MarkedRegionPattern.Match(text);
            if (!region.Success)
            {
                throw new InvalidOperationException(
                    "setup.md no longer carries the sheet-independent:begin/end markers this guard reads. "
                    + "Restore them, or delete this guard deliberately — silently losing it is the failure "
                    + "mode it exists to prevent.");
            }

            return ClassTokenPattern.Matches(region.Groups[1].Value)
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static HashSet<string> DeclaredClasses()
        {
            var classes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sheet in Directory.EnumerateFiles(
                         Path.Combine(SolutionPaths.RuntimeRoot(), "Styles"), "*.uss"))
            {
                foreach (Match m in ClassSelectorPattern.Matches(File.ReadAllText(sheet)))
                {
                    classes.Add(m.Groups[1].Value);
                }
            }

            return classes;
        }
    }
}
