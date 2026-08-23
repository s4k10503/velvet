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
    /// Holds the Unity workflow's push path filter against two sets read out of the repository rather than
    /// listed here: the markdown <c>DocumentationDriftTests</c> scans, and the repo files the workflow
    /// itself names. A workflow that never starts leaves a push showing the same absence of a red check as
    /// one that ran and passed, so a guard left outside the path set keeps reporting nothing until some
    /// later, unrelated change happens to carry a file inside it — and then names that change as the
    /// culprit.
    /// <para>
    /// The trigger side is held by two rules, and the last two cases here are the whole of them: branch
    /// protection requires a check from each of these workflows, so each must subscribe to both events that
    /// ask for one, and neither of those subscriptions may carry a child key.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class WorkflowTriggerCoverageTests
    {
        // The markdown-corpus case still asks about one workflow — the one whose job runs this fixture —
        // because pairing a drift guard with the workflow that runs it would take a fixture-to-job mapping
        // nothing in this assembly can read.
        private const string WorkflowPath = ".github/workflows/test.yml";

        // The names-its-own-files case asks about every one instead. Naming a single workflow left the
        // other's filter unread, and it was excluding four scripts its own jobs run and the two
        // AdditionalFiles Roslyn reads at compile time — the exact failure this fixture describes, in the
        // workflow it did not look at. A workflow added tomorrow is asked the same question for existing.
        // Both extensions, because GitHub Actions runs both and a workflow spelled the other way would
        // otherwise be outside every question here.
        private static IEnumerable<string> Workflows() =>
            Directory.EnumerateFiles(Path.GetFullPath(".github/workflows"), "*.yml")
                .Concat(Directory.EnumerateFiles(Path.GetFullPath(".github/workflows"), "*.yaml"))
                .Select(RepoRelative)
                .OrderBy(path => path, StringComparer.Ordinal);

        private static readonly string[] RequiredCheckWorkflows =
        {
            ".github/workflows/test.yml",
            ".github/workflows/generators.yml",
        };

        private const string GeneratorSourceRoot = "Packages/com.velvet.core/Generators~/src";

        private static readonly Regex KeyPattern =
            new(@"^(\s*)[""']?([A-Za-z_][A-Za-z0-9_-]*)[""']?:", RegexOptions.Compiled);

        private static readonly Regex ListItemPattern = new(@"^(\s*)-\s*(.+?)\s*$", RegexOptions.Compiled);

        // A slash-bearing token is what a repo path looks like inside a workflow, whether it is a step
        // argument, an action input or prose in a comment. Whether the token IS a repo path is then settled
        // by the filesystem, which is what keeps an action reference (game-ci/unity-test-runner) and a URL
        // out without either having to be enumerated.
        private static readonly Regex PathTokenPattern =
            new(@"[A-Za-z0-9_.~-]+(?:/[A-Za-z0-9_.~-]+)+", RegexOptions.Compiled);

        // A step that runs a command, as opposed to a path named in a filter, a comment or an input. A
        // commented-out step is not one: the whole failure being caught is a file nothing executes, and a
        // plain mention of the path cannot tell an invocation from either.
        private static readonly Regex RunStepPattern =
            new(@"^(\s*)(?:-\s*)?run:\s*(\S?)", RegexOptions.Compiled);

        // A negation is a whole pattern, so a ! with anything before it is being used as something else.
        private static readonly Regex UnsupportedGlobPattern = new(@"[?+\[\]{}]|.!", RegexOptions.Compiled);

        [Test]
        public void Given_TheMarkdownThisSuiteScans_When_MatchedAgainstTheWorkflowPathFilter_Then_EveryFileStartsTheRun()
        {
            // Arrange
            var filters = ReadPathFilters(WorkflowPath);

            // Act
            var uncovered = ParseFailures(WorkflowPath, filters)
                .Concat(from filter in filters
                        from file in DocumentationCorpus.Files()
                        where !filter.Includes(file)
                        select $"{filter.Label} does not start for {file}")
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
            var workflows = Workflows().ToList();

            // Act
            var uncovered = new List<string>();
            foreach (var workflow in workflows)
            {
                var filters = ReadPathFilters(workflow);
                var named = NamedRepoFiles(workflow);
                if (named.Count == 0)
                {
                    uncovered.Add($"{workflow} names no file that exists in this repo");
                }
                uncovered.AddRange(from filter in filters
                                   from file in named
                                   where !filter.Includes(file)
                                   select $"{workflow}: {filter.Label} does not start for {file}");
            }

            // Assert — the workflow count rides along because an empty directory reports nothing uncovered.
            Assert.That((workflows.Count > 1, string.Join("\n", uncovered)), Is.EqualTo((true, string.Empty)),
                "a workflow runs these files and does not start when one of them changes");
        }

        [Test]
        public void Given_EveryHarnessUnitTest_When_TheWorkflowsAreScanned_Then_SomeJobRunsIt()
        {
            // Arrange — a harness under scripts/ carries its own unit tests because the Unity half of it
            // needs a licence and the decisions do not. One that no job invokes is a file that passes
            // locally and is never asked again, which is the same silence as a workflow that does not
            // start. Read from the whole tree rather than from scripts/test_quality, which left the
            // release and pull-request harnesses outside the scan: both happen to be run, by nothing
            // stronger than whoever wired them having remembered to.
            var harnessTests = Directory
                .EnumerateFiles(Path.GetFullPath("scripts"), "test_*.py", SearchOption.AllDirectories)
                .Select(RepoRelative)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var invoked = Workflows().SelectMany(RunCommandLines).ToList();

            // Act
            var unrun = harnessTests
                .Where(path => !invoked.Any(line => line.Contains(path, StringComparison.Ordinal)))
                .ToList();

            // Assert — a floor rather than the count, because a directory that yielded nothing leaves
            // nothing unrun. Raise it with the tree, or a deletion answers the same way an empty scan does.
            Assert.That((harnessTests.Count >= 6, string.Join("\n", unrun)), Is.EqualTo((true, string.Empty)),
                "a harness under scripts/ has unit tests that no workflow job runs");
        }

        /// <summary>Every line of every command a workflow's <c>run:</c> steps execute.</summary>
        /// <remarks>
        /// A block scalar's commands sit on the lines BELOW the key, so reading only the key's own line
        /// finds a one-line step and misses a multi-line one — a guard that reported a file unrun while a
        /// job ran it.
        /// </remarks>
        private static IEnumerable<string> RunCommandLines(string workflow)
        {
            var blockIndent = -1;
            foreach (var line in File.ReadAllLines(workflow))
            {
                var indent = line.Length - line.TrimStart().Length;
                if (blockIndent >= 0 && line.Trim().Length > 0)
                {
                    if (indent > blockIndent)
                    {
                        yield return line;
                        continue;
                    }
                    blockIndent = -1;
                }
                var match = RunStepPattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }
                // A block scalar opener carries only its indicator, so the command is what follows below.
                if (match.Groups[2].Value is "|" or ">" or "")
                {
                    blockIndent = match.Groups[1].Value.Length;
                    continue;
                }
                yield return line;
            }
        }

        [Test]
        public void Given_TheWorkflowPathFilter_When_ScannedForGlobSyntax_Then_NoPatternCarriesSyntaxReadLiterally()
        {
            // Arrange — every workflow, since a pattern GitHub reads literally does the same thing wherever
            // it sits: the filter silently matches nothing and the workflow stops starting.
            var filters = Workflows().SelectMany(ReadPathFilters).ToList();

            // Act
            var unsupported = (from filter in filters
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
            var filters = ReadPathFilters(WorkflowPath);
            var root = Path.GetFullPath(GeneratorSourceRoot);
            var sources = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();

            // Act
            var started = ParseFailures(WorkflowPath, filters)
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

        // GREEN_ON_BASE(characterization): both subscriptions are the base's own.
        [Test]
        public void Given_TheWorkflowsBranchProtectionRequires_When_TheirTriggersAreRead_Then_EachSubscribesToBoth()
        {
            // Arrange — a required check reports nothing for a pull request or a queue entry unless its
            // workflow subscribes to the event, and the thing waiting on it then waits forever. Both
            // subscribe through a key with no children, which is the shape an edit removes without leaving
            // a gap.
            var workflows = RequiredCheckWorkflows;

            // Act
            var missing = workflows
                .SelectMany(workflow => new[] { "pull_request", "merge_group" }
                    .Where(trigger => !Triggers(workflow).Contains(trigger))
                    .Select(trigger => $"{workflow}: {trigger}"))
                .ToList();

            // Assert — the list's size rides along, so a reader that found no workflows at all would report
            // nothing missing and pass having read nothing.
            Assert.That((workflows.Length, string.Join(", ", missing)), Is.EqualTo((2, string.Empty)));
        }

        [Test]
        public void Given_TheWorkflowsBranchProtectionRequires_When_TheirTriggersAreRead_Then_OnlyPushCarriesAChild()
        {
            // Arrange
            var filters = RequiredCheckWorkflows.SelectMany(TriggerFilters).ToList();

            // Act — both halves in one reading: a parser that found nothing satisfies "no filter on either
            // gated trigger" exactly, and that is the state a renamed key or a reformatted trigger block
            // produces.
            var onPush = filters.Count(entry => entry.Trigger == "push");
            var onGated = filters
                .Where(entry => entry.Trigger is "pull_request" or "merge_group")
                .Select(entry => $"{entry.Workflow}: {entry.Trigger}.{entry.Key}")
                .ToList();

            // Assert — a floor rather than the count, the way the harness and workflow cases above are:
            // this reader yields every push child, so a count moves on an ordinary push-filter edit
            // that touches neither gated trigger.
            Assert.That((onPush >= 2, string.Join(", ", onGated)), Is.EqualTo((true, string.Empty)),
                "The gated triggers carry no indented child at all. That is a blanket rule rather than a "
                + "judgement per key, because judging per key is what let a branch filter through while a "
                + "path filter was the one being watched for.");
        }

        // Read separately from the filters below because their absence and a trigger's absence are
        // different failures: a filter that should not be there fails the guard by appearing, and a trigger
        // that must be there fails it by not.
        private static IEnumerable<string> Triggers(string workflow)
        {
            var underOn = false;
            foreach (var line in File.ReadAllLines(Path.GetFullPath(workflow)))
            {
                var key = KeyPattern.Match(line);
                if (!key.Success)
                {
                    continue;
                }
                var indent = key.Groups[1].Value.Length;
                var name = key.Groups[2].Value;
                if (indent == 0)
                {
                    underOn = name == "on";
                }
                else if (underOn && indent == 2)
                {
                    yield return name;
                }
            }
        }

        // A child key whose colon follows its name is reported, rather than a list of the filters named
        // today: a list is silent about a key it has not got, where reporting one it should not have
        // reported fails and gets corrected. Spellings this reader does not reach are on #737.
        private static IEnumerable<(string Workflow, string Trigger, string Key)> TriggerFilters(string workflow)
        {
            var lines = File.ReadAllLines(Path.GetFullPath(workflow));
            var underOn = false;
            var trigger = string.Empty;
            foreach (var line in lines)
            {
                var key = KeyPattern.Match(line);
                if (!key.Success)
                {
                    continue;
                }
                var indent = key.Groups[1].Value.Length;
                var name = key.Groups[2].Value;
                if (indent == 0)
                {
                    underOn = name == "on";
                    continue;
                }
                if (!underOn)
                {
                    continue;
                }
                if (indent == 2)
                {
                    trigger = name;
                }
                else
                {
                    yield return (workflow, trigger, name);
                }
            }
        }

        // An unreadable workflow would leave every check above comparing against an empty rule set, where
        // "no file is uncovered" and "no pattern is unsupported" both hold for want of anything to compare.
        // Reported as a failure of the check rather than assumed away, so the fixture cannot pass by parsing
        // nothing.
        // Only where a filter is expected. A workflow carrying none starts for every path, so it cannot
        // fail to start for a file it names, and upm.yml deliberately carries none.
        private static IEnumerable<string> ParseFailures(string workflow, IReadOnlyCollection<PathFilter> filters) =>
            filters.Count == 0
                ? new[] { $"no paths: filter could be read out of {workflow}" }
                : Array.Empty<string>();

        // The one that matters here is scripts/test_quality/assert_no_inconclusive.py: no other file in the repository
        // invokes it, and its failure mode is passing a run it should have failed, which stays invisible
        // until a test starts skipping.
        private static List<string> NamedRepoFiles(string workflow)
        {
            var ignored = DocumentationCorpus.IgnoredRoots();
            return PathTokenPattern.Matches(File.ReadAllText(Path.GetFullPath(workflow)))
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .Where(token => File.Exists(Path.GetFullPath(token)))
                .Where(token => !ignored.Contains(token.Split('/')[0]))
                .OrderBy(token => token, StringComparer.Ordinal)
                .ToList();
        }

        private static string RepoRelative(string path) =>
            Path.GetRelativePath(Path.GetFullPath("."), path).Replace('\\', '/');

        // Every paths: block, labelled by the trigger holding it, rather than the push one by name: a block
        // appearing under a trigger that should carry none is a failure the case above reports, and reading
        // only the expected one would leave this half comparing against a filter that is no longer the
        // operative one.
        private static List<PathFilter> ReadPathFilters(string workflow)
        {
            var lines = File.ReadAllLines(Path.GetFullPath(workflow));
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
