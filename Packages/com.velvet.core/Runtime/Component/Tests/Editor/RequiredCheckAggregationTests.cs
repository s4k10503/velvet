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
        // The aggregate is found by the name branch protection requires rather than by position, because a
        // workflow may hold several jobs that depend on others and only this one stands for all of them.
        private static readonly (string Workflow, string Aggregate)[] RequiredWorkflows =
        {
            (".github/workflows/test.yml", "required-checks"),
            (".github/workflows/generators.yml", "required-checks"),
        };

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

        [Test]
        public void Given_EveryJobInARequiredWorkflow_When_TheAggregateIsRead_Then_ItDependsOnThatJob()
        {
            // Arrange
            var aggregates = RequiredWorkflows;

            // Act
            var unwired = aggregates
                .SelectMany(entry => Unwired(entry.Workflow, entry.Aggregate, block => NeedsListPattern
                    .Match(block).Groups["jobs"].Value
                    .Split(',')
                    .Select(job => job.Trim())
                    .Where(job => job.Length > 0)))
                .ToList();

            // Assume — an aggregate whose block or needs list went unread would report nothing missing.
            Assume.That(aggregates.Select(entry => JobNames(ReadWorkflow(entry.Workflow)).Count),
                Is.All.GreaterThan(1));

            // Assert
            Assert.That(string.Join("\n", unwired), Is.Empty);
        }

        [Test]
        public void Given_EveryJobARequiredAggregateDependsOn_When_ItsStepIsRead_Then_ThatResultIsInspected()
        {
            // Arrange — depending on a job without reading its result makes the aggregate wait and pass anyway.
            var aggregates = RequiredWorkflows;

            // Act
            var uninspected = aggregates
                .SelectMany(entry => Unwired(entry.Workflow, entry.Aggregate, block => ResultReferencePattern
                    .Matches(block)
                    .Select(match => match.Groups["job"].Value)))
                .ToList();

            // Assume
            Assume.That(aggregates.Select(entry => JobNames(ReadWorkflow(entry.Workflow)).Count),
                Is.All.GreaterThan(1));

            // Assert
            Assert.That(string.Join("\n", uninspected), Is.Empty);
        }
    }
}
