using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the Unity workflow's path filter against two sets read out of the repository rather than
    /// listed here: the markdown <c>DocumentationDriftTests</c> scans, and the repo files the workflow
    /// itself runs. A workflow that never starts leaves a pull request showing the same absence of a red
    /// check as one that ran and passed, so a guard left outside the path set keeps reporting nothing
    /// until some later, unrelated change happens to carry a file inside it — and then names that change
    /// as the culprit.
    /// </summary>
    [TestFixture]
    internal sealed class WorkflowTriggerCoverageTests
    {
        // One workflow: the one whose job runs this fixture. Pairing the drift guards in the Generators~
        // solution with the workflow that runs THEM would take a fixture-to-job mapping nothing in this
        // assembly can read, and writing that out by hand is the maintained-by-memory list this fixture
        // exists to replace.
        private const string WorkflowPath = ".github/workflows/test.yml";

        private const string GeneratorSourceRoot = "Packages/com.velvet.core/Generators~/src";

        private static readonly Regex KeyPattern =
            new(@"^(\s*)([A-Za-z_][A-Za-z0-9_-]*):", RegexOptions.Compiled);

        private static readonly Regex ListItemPattern = new(@"^(\s*)-\s*(.+?)\s*$", RegexOptions.Compiled);

        // A slash-bearing token is what a repo path looks like inside a workflow, whether it is a step
        // argument, an action input or prose in a comment. Whether the token IS a repo path is then settled
        // by the filesystem, which is what keeps an action reference (game-ci/unity-test-runner) and a URL
        // out without either having to be enumerated.
        private static readonly Regex PathTokenPattern =
            new(@"[A-Za-z0-9_.~-]+(?:/[A-Za-z0-9_.~-]+)+", RegexOptions.Compiled);

        // A negation is a whole pattern, so a ! with anything before it is being used as something else.
        private static readonly Regex UnsupportedGlobPattern = new(@"[?+\[\]{}]|.!", RegexOptions.Compiled);

        [Test]
        public void Given_TheMarkdownThisSuiteScans_When_MatchedAgainstTheWorkflowPathFilter_Then_EveryFileStartsTheRun()
        {
            // Arrange
            var filters = ReadPathFilters();

            // Act
            var uncovered = ParseFailures(filters)
                .Concat(from filter in filters
                        from file in DocumentationCorpus.Files()
                        let relative = RepoRelative(file.Path)
                        where !filter.Includes(relative)
                        select $"{filter.Label} does not start for {relative}")
                .ToList();

            // Assert
            Assert.That(uncovered, Is.Empty,
                $"{WorkflowPath} runs the fixture that scans these files, but a change to one starts nothing:\n"
                + string.Join("\n", uncovered));
        }

        [Test]
        public void Given_TheRepoFilesTheWorkflowNames_When_MatchedAgainstItsOwnPathFilter_Then_EveryOneStartsTheRun()
        {
            // Arrange
            var filters = ReadPathFilters();
            var named = NamedRepoFiles();

            // Act
            var uncovered = ParseFailures(filters)
                .Concat(named.Count == 0
                    ? new[] { $"{WorkflowPath} names no file that exists in this repo" }
                    : Array.Empty<string>())
                .Concat(from filter in filters
                        from file in named
                        where !filter.Includes(file)
                        select $"{filter.Label} does not start for {file}")
                .ToList();

            // Assert
            Assert.That(uncovered, Is.Empty,
                $"{WorkflowPath} runs these files but does not start when one of them changes:\n"
                + string.Join("\n", uncovered));
        }

        [Test]
        public void Given_TheWorkflowPathFilter_When_ScannedForGlobSyntax_Then_NoPatternCarriesSyntaxReadLiterally()
        {
            // Arrange
            var filters = ReadPathFilters();

            // Act
            var unsupported = ParseFailures(filters)
                .Concat(from filter in filters
                        from pattern in filter.Patterns
                        where UnsupportedGlobPattern.IsMatch(pattern)
                        select $"{filter.Label}: {pattern}")
                .ToList();

            // Assert
            Assert.That(unsupported, Is.Empty,
                "The translation below matches every character but * and ** literally, so these patterns would "
                + "be decided against the wrong set of files:\n" + string.Join("\n", unsupported));
        }

        [Test]
        public void Given_AGeneratorSolutionSource_When_MatchedAgainstTheWorkflowPathFilter_Then_ItStartsNoRun()
        {
            // Arrange — the exclusion pinned here is what keeps a generator-source edit off the Unity matrix,
            // and it is also the only case in this fixture whose answer is "no": a match that included every
            // path would satisfy every other test here.
            var filters = ReadPathFilters();
            var root = Path.GetFullPath(GeneratorSourceRoot);
            var sources = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();

            // Act
            var started = ParseFailures(filters)
                .Concat(sources.Length == 0
                    ? new[] { $"no .cs file under {GeneratorSourceRoot} for the exclusion to be tested against" }
                    : Array.Empty<string>())
                .Concat(from filter in filters
                        from source in sources
                        let relative = RepoRelative(source)
                        where filter.Includes(relative)
                        select $"{filter.Label} starts for {relative}")
                .ToList();

            // Assert
            Assert.That(started, Is.Empty,
                $"{WorkflowPath} excludes the generator solution, but starts for:\n" + string.Join("\n", started));
        }

        // An unreadable workflow would leave every check above comparing against an empty rule set, where
        // "no file is uncovered" and "no pattern is unsupported" both hold for want of anything to compare.
        // Reported as a failure of the check rather than assumed away, so the fixture cannot pass by parsing
        // nothing.
        private static IEnumerable<string> ParseFailures(IReadOnlyCollection<PathFilter> filters) =>
            filters.Count == 0
                ? new[] { $"no paths: filter could be read out of {WorkflowPath}" }
                : Array.Empty<string>();

        // The one that matters here is scripts/assert-no-inconclusive.py: no other file in the repository
        // invokes it, and its failure mode is passing a run it should have failed, which stays invisible
        // until a test starts skipping.
        private static List<string> NamedRepoFiles() =>
            PathTokenPattern.Matches(File.ReadAllText(Path.GetFullPath(WorkflowPath)))
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Where(token => File.Exists(Path.GetFullPath(token)))
                .OrderBy(token => token, StringComparer.Ordinal)
                .ToList();

        private static string RepoRelative(string path) =>
            Path.GetRelativePath(Path.GetFullPath("."), path).Replace('\\', '/');

        // Each paths: block, labelled by the trigger holding it, so a failure says whether it is the push
        // side or the pull_request side that stopped covering a file. Both are checked: they are written
        // twice in the file and can drift apart, and a filter that only covers pull_request lets a merge
        // to main go untested.
        private static List<PathFilter> ReadPathFilters()
        {
            var lines = File.ReadAllLines(Path.GetFullPath(WorkflowPath));
            var filters = new List<PathFilter>();
            for (var index = 0; index < lines.Length; index++)
            {
                var key = KeyPattern.Match(lines[index]);
                if (!key.Success || key.Groups[2].Value != "paths")
                {
                    continue;
                }
                var indent = key.Groups[1].Value.Length;
                var patterns = new List<string>();
                for (var item = index + 1; item < lines.Length; item++)
                {
                    var line = lines[item];
                    if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var listItem = ListItemPattern.Match(line);
                    if (!listItem.Success || listItem.Groups[1].Value.Length <= indent)
                    {
                        break;
                    }
                    patterns.Add(listItem.Groups[2].Value.Trim('\'', '"'));
                }
                filters.Add(new PathFilter(EnclosingKey(lines, index, indent) + " paths", patterns));
            }
            return filters;
        }

        private static string EnclosingKey(IReadOnlyList<string> lines, int index, int indent)
        {
            for (var above = index - 1; above >= 0; above--)
            {
                var key = KeyPattern.Match(lines[above]);
                if (key.Success && key.Groups[1].Value.Length < indent)
                {
                    return key.Groups[2].Value;
                }
            }
            return WorkflowPath;
        }

        // Only the two wildcards are translated; every other character is matched literally, which is what
        // the syntax test above refuses to let a pattern rely on.
        private static Regex TranslateGlob(string pattern)
        {
            var expression = new StringBuilder("^");
            for (var index = 0; index < pattern.Length; index++)
            {
                if (pattern[index] != '*')
                {
                    expression.Append(Regex.Escape(pattern[index].ToString()));
                    continue;
                }
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    expression.Append(".*");
                    index++;
                    continue;
                }
                expression.Append("[^/]*");
            }
            return new Regex(expression.Append('$').ToString(), RegexOptions.Compiled);
        }

        private sealed class PathFilter
        {
            private readonly List<(Regex Expression, bool Include)> rules;

            internal PathFilter(string label, IReadOnlyList<string> patterns)
            {
                Label = label;
                Patterns = patterns;
                rules = patterns
                    .Select(pattern => (
                        TranslateGlob(pattern.TrimStart('!')),
                        !pattern.StartsWith("!", StringComparison.Ordinal)))
                    .ToList();
            }

            internal string Label { get; }

            internal IReadOnlyList<string> Patterns { get; }

            // Every rule is evaluated and the last match decides: a re-include is written after the exclusion
            // it reverses, so stopping at the first hit would read a file's fate off the wrong line.
            internal bool Includes(string path)
            {
                var included = false;
                foreach (var (expression, include) in rules)
                {
                    if (expression.IsMatch(path))
                    {
                        included = include;
                    }
                }
                return included;
            }
        }
    }
}
