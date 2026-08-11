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

        // The trailing comment is allowed for: a job key carrying one is valid YAML and a valid job id, and
        // a pattern that misses it drops that job out of every case here — which is the drift these exist
        // to catch, happening inside them.
        private static readonly Regex JobKeyPattern =
            new(@"^  ([A-Za-z_][A-Za-z0-9_-]*):\s*(#.*)?$", RegexOptions.Compiled);

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
            foreach (var path in WorkflowFiles())
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
            var start = Array.FindIndex(lines, line =>
            {
                var match = JobKeyPattern.Match(line.TrimEnd('\r'));
                return match.Success && match.Groups[1].Value == aggregate;
            });
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

        // The first backticked run of a table row's first cell, which names either a workflow (`Docs`) or
        // one of its jobs (`Test ▸ unity-tests`).
        private static readonly Regex TableRowPattern =
            new(@"^\|\s*`(?<cell>[^`]+)`", RegexOptions.Compiled);

        // A pipe row of dashes and colons, and nothing else.
        private static readonly Regex TableSeparator = new(@"^\|[\s:|-]+\|\s*$", RegexOptions.Compiled);

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

        /// <summary>
        /// Each row of CONTRIBUTING's continuous-integration table: the `Workflow ▸ job` / `Workflow` its
        /// first cell names, and whether its last column says the check is required to merge.
        /// <para>
        /// Bounded by the table rather than by the section. Collection starts at the header's separator
        /// row and ends at the first line that is not a row, so a second table or a fenced example in the
        /// same section is not read as continuous-integration rows and misreported as a dangling one.
        /// </para>
        /// </summary>
        private static IReadOnlyList<(string Workflow, string Job, bool Required)> ContributingRows()
        {
            var rows = new List<(string, string, bool)>();
            var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "CONTRIBUTING.md"));
            var heading = Array.FindIndex(lines, line => line.TrimEnd('\r') == "## Continuous integration");
            if (heading < 0)
            {
                return rows;
            }

            var separator = Array.FindIndex(lines, heading + 1, line => TableSeparator.IsMatch(line.TrimEnd('\r')));
            for (var i = separator + 1; separator > heading && i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (!line.StartsWith("|", StringComparison.Ordinal))
                {
                    break;
                }

                var match = TableRowPattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var columns = line.Split('|');
                var required = columns.Length > 4
                    && string.Equals(columns[4].Replace("*", string.Empty).Trim(), "yes", StringComparison.OrdinalIgnoreCase);
                var cell = match.Groups["cell"].Value;
                var arrow = cell.IndexOf('▸');
                rows.Add(arrow < 0
                    ? (cell.Trim(), string.Empty, required)
                    : (cell.Substring(0, arrow).Trim(), cell.Substring(arrow + 1).Trim(), required));
            }

            return rows;
        }

        /// <summary>Every workflow file. Both extensions, because GitHub Actions runs both.</summary>
        private static IReadOnlyList<string> WorkflowFiles()
        {
            var directory = Path.Combine(RepositoryRoot(), ".github", "workflows");
            return Directory.EnumerateFiles(directory, "*.yml")
                .Concat(Directory.EnumerateFiles(directory, "*.yaml"))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Every workflow file, paired with its display name.</summary>
        private static IReadOnlyList<(string Path, string Name, string Text)> Workflows()
        {
            return WorkflowFiles()
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
            foreach (var (name, job, _) in rows)
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

            // Assert — the row floor rides along because a heading that was renamed leaves no rows to
            // dangle, which reads the same as a table that agrees. An empty workflow directory needs no
            // floor: it dangles every row rather than none.
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
            var asked = 0;
            foreach (var (path, text) in required)
            {
                var name = WorkflowDisplayName(text);
                foreach (var job in JobNames(text))
                {
                    asked++;
                    if (!listed.Any(row => row.Workflow == name && row.Job.Length > 0 && Names(text, row.Job, job)))
                    {
                        unlisted.Add(path + ":" + job);
                    }
                }
            }

            // Assert — the second floor counts the jobs actually asked about, as the cases below do, rather
            // than the rows offered to answer them. A workflow whose jobs went unenumerated reports nothing
            // unlisted, and a full table would have covered for it.
            Assert.That((required.Count >= 2, asked > required.Count, string.Join("\n", unlisted)),
                Is.EqualTo((true, true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): this column happened to be right on both sides. The other two
        // cases found the first column wrong, which is the reason to hold this one before it drifts too.
        [Test]
        public void Given_EveryRequiredCheckAggregate_When_ContributingsCiTableIsRead_Then_ItsRowIsTheOneMarkedRequired()
        {
            // Arrange — the column a contributor acts on is the last one, and it was as unpinned as the
            // first. Which rows may carry a yes is the same question the fixture already answers to find
            // the aggregates, so it is asked once rather than curated twice.
            var aggregates = RequiredWorkflows()
                .Select(entry => (Text: ReadWorkflow(entry.Workflow), entry.Aggregate))
                .Select(entry => (Workflow: WorkflowDisplayName(entry.Text), Display: JobDisplayName(entry.Text, entry.Aggregate)))
                .ToHashSet();

            // Act
            var wrong = ContributingRows()
                .Where(row => row.Job.Length > 0)
                .Where(row => aggregates.Contains((row.Workflow, row.Job)) != row.Required)
                .Select(row => row.Workflow + " ▸ " + row.Job + (row.Required ? " (marked required)" : " (not marked required)"))
                .ToList();

            // Assert — the floor rides along because an aggregate set that read empty matches no row, and
            // every row then correctly reads "not required", which is a pass having measured nothing.
            Assert.That((aggregates.Count >= 2, string.Join("\n", wrong)), Is.EqualTo((true, string.Empty)));
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
