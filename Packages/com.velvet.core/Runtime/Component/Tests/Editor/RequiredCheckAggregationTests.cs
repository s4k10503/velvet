using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds every job in a required workflow against the aggregate job branch protection actually names.
    /// A job left out of that aggregate still runs and still goes red, and nothing is blocked by it — the
    /// same absence of a red required check as a repository with no such job at all. So a check added and
    /// left unwired reports for as long as anyone reads the run page and never once stops a merge.
    /// <para>
    /// Both halves are read out of the workflow rather than listed here, so a job added tomorrow is in
    /// scope for having been added. <c>WorkflowTriggerCoverageTests</c> owns which events start these
    /// workflows and which paths they filter on; this one owns what their results are wired into.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class RequiredCheckAggregationTests
    {
        // Which workflows these are is read out of the workflows, not listed: a workflow is required when
        // it carries a job whose display name is a required status check, and the repository-settings job
        // in generators.yml asserts against GitHub's own ruleset that the required contexts are exactly the
        // names matched here. Listing the pair instead would leave a third one outside every case below
        // for as long as nobody remembered the list — which is the failure this fixture is about, one
        // level up.
        private static readonly Regex RequiredContextPattern =
            new(@"^\s*name:\s*(?<context>Required checks \([^)]+\))\s*$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex JobKeyPattern =
            new(@"^  ([A-Za-z_][A-Za-z0-9_-]*):\s*$", RegexOptions.Compiled);

        private static readonly Regex NeedsListPattern =
            new(@"^\s*needs:\s*\[(?<jobs>[^\]]*)\]", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex ResultReferencePattern =
            new(@"needs\.(?<job>[A-Za-z_][A-Za-z0-9_-]*)\.result", RegexOptions.Compiled);

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? Directory.GetCurrentDirectory();
        }

        private static string ReadWorkflow(string relativePath) =>
            File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

        /// <summary>Each workflow holding a required-status-check aggregate, paired with that job's key.</summary>
        private static IReadOnlyList<(string Workflow, string Aggregate)> RequiredWorkflows()
        {
            var found = new List<(string, string)>();
            var directory = Path.Combine(RepositoryRoot(), ".github", "workflows");
            foreach (var path in Directory.EnumerateFiles(directory, "*.yml").OrderBy(path => path, StringComparer.Ordinal))
            {
                var workflow = File.ReadAllText(path);
                foreach (var job in JobNames(workflow))
                {
                    if (RequiredContextPattern.IsMatch(AggregateBlock(workflow, job)))
                    {
                        found.Add((".github/workflows/" + Path.GetFileName(path), job));
                    }
                }
            }

            return found;
        }

        private static IReadOnlyList<string> JobNames(string workflow)
        {
            var names = new List<string>();
            var inJobs = false;
            foreach (var line in workflow.Split('\n'))
            {
                if (line.StartsWith("jobs:", StringComparison.Ordinal))
                {
                    inJobs = true;
                    continue;
                }

                // A key back at column zero ends the jobs block; nothing else in the file is indented as a job.
                if (inJobs && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal))
                {
                    inJobs = false;
                }

                if (!inJobs)
                {
                    continue;
                }

                var match = JobKeyPattern.Match(line.TrimEnd('\r'));
                if (match.Success)
                {
                    names.Add(match.Groups[1].Value);
                }
            }

            return names;
        }

        private static string AggregateBlock(string workflow, string aggregate)
        {
            var lines = workflow.Split('\n');
            var start = Array.FindIndex(lines, line => line.TrimEnd('\r') == "  " + aggregate + ":");
            if (start < 0)
            {
                return string.Empty;
            }

            var end = Array.FindIndex(lines, start + 1, line => JobKeyPattern.IsMatch(line.TrimEnd('\r')));
            return string.Join("\n", lines.Skip(start).Take((end < 0 ? lines.Length : end) - start));
        }

        private static IEnumerable<string> Unwired(string relativePath, string aggregate, Func<string, IEnumerable<string>> read)
        {
            var workflow = ReadWorkflow(relativePath);
            var block = AggregateBlock(workflow, aggregate);
            var wired = read(block).ToHashSet(StringComparer.Ordinal);
            return JobNames(workflow)
                .Where(job => job != aggregate && !wired.Contains(job))
                .Select(job => relativePath + ":" + job);
        }

        private static readonly Regex WorkflowNamePattern =
            new(@"^name:\s*(?<name>.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex JobNamePattern =
            new(@"^    name:\s*(?<name>.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

        // The first backticked run of a table row's first cell. A row names either a workflow (`Docs`) or
        // one of its jobs (`Test ▸ unity-tests`), and the rest of the cell is prose about the matrix.
        private static readonly Regex TableRowPattern =
            new(@"^\|\s*`(?<cell>[^`]+)`", RegexOptions.Compiled);

        private static string Unquoted(string value) => value.Trim().Trim('"', '\'');

        private static string WorkflowDisplayName(string workflow)
        {
            var match = WorkflowNamePattern.Match(workflow);
            return match.Success ? Unquoted(match.Groups["name"].Value) : string.Empty;
        }

        private static string JobDisplayName(string workflow, string job)
        {
            var match = JobNamePattern.Match(AggregateBlock(workflow, job));
            return match.Success ? Unquoted(match.Groups["name"].Value) : string.Empty;
        }

        /// <summary>Each `Workflow ▸ job` / `Workflow` reference in CONTRIBUTING's continuous-integration table.</summary>
        private static IReadOnlyList<(string Workflow, string Job)> ContributingRows()
        {
            var rows = new List<(string, string)>();
            var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "CONTRIBUTING.md"));
            var start = Array.FindIndex(lines, line => line.TrimEnd('\r') == "## Continuous integration");
            for (var i = start + 1; start >= 0 && i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.StartsWith("##", StringComparison.Ordinal))
                {
                    break;
                }

                var match = TableRowPattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var cell = match.Groups["cell"].Value;
                var separator = cell.IndexOf('▸');
                rows.Add(separator < 0
                    ? (cell.Trim(), string.Empty)
                    : (cell.Substring(0, separator).Trim(), cell.Substring(separator + 1).Trim()));
            }

            return rows;
        }

        /// <summary>Every workflow file, paired with its display name.</summary>
        private static IReadOnlyList<(string Path, string Name, string Text)> Workflows()
        {
            var directory = Path.Combine(RepositoryRoot(), ".github", "workflows");
            return Directory.EnumerateFiles(directory, "*.yml")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    var text = File.ReadAllText(path);
                    return (Path.GetFileName(path), WorkflowDisplayName(text), text);
                })
                .ToList();
        }

        private static bool Names(string workflow, string reference, string job) =>
            reference == job || reference == JobDisplayName(workflow, job);

        [Test]
        public void Given_EveryRowOfContributingsCiTable_When_TheWorkflowsAreRead_Then_ItNamesOneThatExists()
        {
            // Arrange — a row naming no job is the direction that needs no curation: it is wrong outright.
            var workflows = Workflows();
            var rows = ContributingRows();

            // Act
            var dangling = new List<string>();
            foreach (var (name, job) in rows)
            {
                var workflow = workflows.FirstOrDefault(entry => entry.Name == name);
                if (workflow.Text == null)
                {
                    dangling.Add(name + " (no workflow of that name)");
                }
                else if (job.Length > 0 && !JobNames(workflow.Text).Any(key => Names(workflow.Text, job, key)))
                {
                    dangling.Add(name + " ▸ " + job + " (no such job in " + workflow.Path + ")");
                }
            }

            // Assert — the floors ride along for the reason the cases below give: a table nobody found, or
            // a workflow directory that read empty, reports nothing dangling and would pass having measured
            // nothing.
            Assert.That((rows.Count >= 2, workflows.Count >= 2, string.Join("\n", dangling)),
                Is.EqualTo((true, true, string.Empty)));
        }

        [Test]
        public void Given_EveryJobOfARequiredWorkflow_When_ContributingsCiTableIsRead_Then_ItHasARow()
        {
            // Arrange — which jobs a contributor is owed a row for is read off the workflows rather than
            // curated here: the ones in a workflow carrying a required aggregate are the ones that can
            // block a merge. The rest — the docs publishing chain, the upm split — are summarised by a row
            // per workflow, and an exemption list kept here would be the second place to edit that let the
            // table drift in the first place.
            var listed = ContributingRows();
            var required = RequiredWorkflows()
                .Select(entry => (entry.Workflow, Text: ReadWorkflow(entry.Workflow)))
                .ToList();

            // Act
            var unlisted = new List<string>();
            foreach (var (path, text) in required)
            {
                var name = WorkflowDisplayName(text);
                foreach (var job in JobNames(text))
                {
                    if (!listed.Any(row => row.Workflow == name && row.Job.Length > 0 && Names(text, row.Job, job)))
                    {
                        unlisted.Add(path + ":" + job);
                    }
                }
            }

            // Assert — same floors as the cases below, for the same reason.
            Assert.That((required.Count >= 2, listed.Count >= 2, string.Join("\n", unlisted)),
                Is.EqualTo((true, true, string.Empty)));
        }

        [Test]
        public void Given_EveryJobInARequiredWorkflow_When_TheAggregateIsRead_Then_ItDependsOnThatJob()
        {
            // Arrange
            var aggregates = RequiredWorkflows();

            // Act
            var unwired = aggregates
                .SelectMany(entry => Unwired(entry.Workflow, entry.Aggregate, block => NeedsListPattern
                    .Match(block).Groups["jobs"].Value
                    .Split(',')
                    .Select(job => job.Trim())
                    .Where(job => job.Length > 0)))
                .ToList();

            // Assert — the two counts ride along because no aggregate found, or an aggregate whose block
            // went unread, reports nothing missing and would pass for having measured nothing. They are
            // floors rather than exact numbers: an exact one is a hand-maintained mirror, and this fixture
            // exists because those go stale.
            var scanned = aggregates.Sum(entry => JobNames(ReadWorkflow(entry.Workflow)).Count);
            Assert.That((aggregates.Count >= 2, scanned > aggregates.Count, string.Join("\n", unwired)),
                Is.EqualTo((true, true, string.Empty)));
        }

        [Test]
        public void Given_EveryJobARequiredAggregateDependsOn_When_ItsStepIsRead_Then_ThatResultIsInspected()
        {
            // Arrange — depending on a job without reading its result makes the aggregate wait and pass anyway.
            var aggregates = RequiredWorkflows();

            // Act
            var uninspected = aggregates
                .SelectMany(entry => Unwired(entry.Workflow, entry.Aggregate, block => ResultReferencePattern
                    .Matches(block)
                    .Select(match => match.Groups["job"].Value)))
                .ToList();

            // Assert — same floors as above, for the same reason.
            var scanned = aggregates.Sum(entry => JobNames(ReadWorkflow(entry.Workflow)).Count);
            Assert.That((aggregates.Count >= 2, scanned > aggregates.Count, string.Join("\n", uninspected)),
                Is.EqualTo((true, true, string.Empty)));
        }
    }
}
