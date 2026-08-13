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
    /// scope for having been added. <c>WorkflowTriggerCoverageTests</c> owns the triggers a required
    /// workflow must carry and what its path filters decide about a file; this one owns what
    /// CONTRIBUTING's table claims about them, and what their results are wired into.
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

        // A `needs:` spelling this returns nothing for leaves
        // Given_EveryJobARequiredAggregateNames_When_TheWorkflowIsEnumerated_Then_ItIsFound asking nothing
        // and passing for it. The bare scalar was such a spelling, while docs.yml wrote three and test.yml
        // one.
        private static readonly Regex NeedsListPattern =
            new(@"^\s*needs:\s*\[(?<jobs>[^\]]*)\]", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex NeedsBlockPattern =
            new(@"^\s*needs:\s*(?:#[^\r\n]*)?\r?\n(?<jobs>(?:\s*-\s*[^\r\n]+\r?\n?)+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex NeedsScalarPattern =
            new(@"^[ \t]*needs:[ \t]*(?<job>[A-Za-z_][A-Za-z0-9_-]*)[ \t]*(?:#[^\r\n]*)?\r?$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>The jobs an aggregate's block declares it needs, in whichever spelling it uses.</summary>
        private static IEnumerable<string> NeedsOf(string block)
        {
            var flow = NeedsListPattern.Match(block);
            if (flow.Success)
            {
                return flow.Groups["jobs"].Value.Split(',').Select(job => job.Trim()).Where(job => job.Length > 0);
            }

            var sequence = NeedsBlockPattern.Match(block);
            if (sequence.Success)
            {
                return sequence.Groups["jobs"].Value.Split('\n')
                    .Select(line => line.Trim().TrimStart('-').Trim())
                    .Where(job => job.Length > 0);
            }

            var scalar = NeedsScalarPattern.Match(block);
            return scalar.Success ? new[] { scalar.Groups["job"].Value } : Enumerable.Empty<string>();
        }

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
        /// first cell names, its Trigger and Unity-license cells as written, and whether its last column
        /// says the check is required to merge.
        /// <para>
        /// Bounded by the table rather than by the section, and fenced blocks are skipped whole. Collection
        /// starts at the first separator row outside a fence and ends at the first line that is not a row,
        /// so a fenced example of a table, and any second table below this one, are not read as
        /// continuous-integration rows and misreported as naming no workflow. An unfenced one between the
        /// heading and it is read: that one becomes the rows. A fence is recognised by its opening run of
        /// backticks or tildes at the line's start, which is every fence in this file.
        /// </para>
        /// </summary>
        private static IReadOnlyList<(string Workflow, string Job, string Trigger, string License, bool Required)>
            ContributingRows()
        {
            var rows = new List<(string, string, string, string, bool)>();
            var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), "CONTRIBUTING.md"));
            var heading = Array.FindIndex(lines, line => line.TrimEnd('\r') == "## Continuous integration");
            if (heading < 0)
            {
                return rows;
            }

            var separator = -1;
            var fenced = false;
            for (var i = heading + 1; i < lines.Length && separator < 0; i++)
            {
                var scan = lines[i].TrimEnd('\r');
                if (scan.StartsWith("```", StringComparison.Ordinal) || scan.StartsWith("~~~", StringComparison.Ordinal))
                {
                    fenced = !fenced;
                }
                else if (!fenced && TableSeparator.IsMatch(scan))
                {
                    separator = i;
                }
            }

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
                var trigger = columns.Length > 2 ? columns[2] : string.Empty;
                var license = columns.Length > 3 ? columns[3] : string.Empty;
                var cell = match.Groups["cell"].Value;
                var arrow = cell.IndexOf('▸');
                rows.Add(arrow < 0
                    ? (cell.Trim(), string.Empty, trigger, license, required)
                    : (cell.Substring(0, arrow).Trim(), cell.Substring(arrow + 1).Trim(), trigger, license, required));
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
            foreach (var (name, job, _, _, _) in rows)
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

            // Assert — the floor is on the rows, because a heading that was renamed leaves none to dangle
            // and reads the same as a table that agrees. A workflow directory that read empty needs none:
            // it dangles every row rather than nothing.
            Assert.That((rows.Count >= 2, string.Join("\n", dangling)), Is.EqualTo((true, string.Empty)));
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

            // Assert — the second floor is per workflow rather than a total, so one required workflow
            // collapsing to its aggregate alone cannot be covered for by the other's jobs or by a full
            // table. It does not catch a workflow that lost *some* of its jobs to the parser;
            // Given_EveryJobARequiredAggregateNames_When_TheWorkflowIsEnumerated_Then_ItIsFound does.
            var enumerated = required.All(entry => JobNames(entry.Text).Count >= 2);
            Assert.That((required.Count >= 2, enumerated, string.Join("\n", unlisted)),
                Is.EqualTo((true, true, string.Empty)));
        }

        // The events a contributor's own work starts. `workflow_dispatch` and `release` sit outside by
        // decision rather than by omission: neither is something a pull request or a push to main causes,
        // and the table does not offer them for the required workflows.
        private static readonly string[] ContributorEvents = { "push", "pull_request", "merge_group" };

        // The words the Trigger column uses for those three, and it has two for a push: a path filter is
        // the difference between a push that starts the workflow and one that does not. A cell naming none
        // of them is reported rather than read as claiming nothing, so a row rewritten in a vocabulary
        // this does not know fails here instead of quietly leaving the column unheld.
        private static readonly (string Phrase, string Event)[] TriggerPhrases =
        {
            ("push (filtered)", "push (filtered)"),
            ("push to `main`", "push"),
            ("every PR", "pull_request"),
            ("merge group", "merge_group"),
        };

        private static readonly Regex OnKeyPattern = new(@"^  (?<event>[a-z_]+):", RegexOptions.Compiled);

        private static readonly Regex EventNameReferencePattern =
            new(@"github\.event_name", RegexOptions.Compiled);

        // An `if:` reaching for github.event_name any other way is refused rather than read past. Reading
        // past falls back to the workflow's whole `on:` set and prints that as what the job starts on, and
        // rewriting the cell to match the message is the repair the message asks for.
        private static readonly Regex EventEqualityPattern =
            new(@"github\.event_name\s*==\s*(?<quote>['""])(?<event>[a-z_]+)\k<quote>", RegexOptions.Compiled);

        /// <summary>The lines of a workflow's `on:` block.</summary>
        private static List<string> OnBlock(string workflow)
        {
            var lines = workflow.Split('\n');
            var start = Array.FindIndex(lines, line => line.TrimEnd('\r') == "on:");
            if (start < 0)
            {
                return new List<string>();
            }

            var end = Array.FindIndex(lines, start + 1, line =>
                line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("#", StringComparison.Ordinal));
            return lines.Skip(start + 1).Take((end < 0 ? lines.Length : end) - start - 1)
                .Select(line => line.TrimEnd('\r'))
                .ToList();
        }

        /// <summary>The events a workflow subscribes to, read out of its own `on:` block.</summary>
        private static IEnumerable<string> SubscribedEvents(string workflow) =>
            OnBlock(workflow)
                .Select(line => OnKeyPattern.Match(line))
                .Where(match => match.Success)
                .Select(match => match.Groups["event"].Value);

        /// <summary>How the Trigger column spells this workflow's push subscription.</summary>
        private static string PushPhrase(string workflow)
        {
            var block = OnBlock(workflow);
            var push = block.FindIndex(line =>
                OnKeyPattern.Match(line) is { Success: true } key && key.Groups["event"].Value == "push");
            if (push < 0)
            {
                return "push";
            }

            // paths-ignore counts, for the reason TriggerFilters in WorkflowTriggerCoverageTests gives.
            for (var i = push + 1; i < block.Count && !OnKeyPattern.IsMatch(block[i]); i++)
            {
                if (block[i].TrimStart().StartsWith("paths", StringComparison.Ordinal))
                {
                    return "push (filtered)";
                }
            }

            return "push";
        }

        /// <summary>A job's own `if:`, folded continuation lines joined in, or null where it carries none.</summary>
        private static string JobLevelIf(string block)
        {
            // Four spaces is a job-level key. A step's `if:` sits deeper and answers for that step alone —
            // upm.yml gates three steps on workflow_dispatch while the job itself runs on push too.
            const string key = "    if:";
            var lines = block.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            var start = lines.FindIndex(line => line.StartsWith(key, StringComparison.Ordinal));
            if (start < 0)
            {
                return null;
            }

            // A folded expression puts the whole condition below the key, where a reader of that line
            // alone finds no event named and narrows by nothing.
            var expression = lines[start].Substring(key.Length);
            for (var i = start + 1; i < lines.Count && lines[i].StartsWith("      ", StringComparison.Ordinal); i++)
            {
                expression += " " + lines[i].Trim();
            }

            return expression;
        }

        [Test]
        public void Given_EveryRowOfContributingsCiTable_When_ItsTriggerCellIsRead_Then_ItNamesWhatStartsThatJob()
        {
            // Arrange — the Trigger column was as unpinned as every other, and it was wrong: both aggregate
            // rows omitted push while their jobs carry no event gate at all.
            var workflows = Workflows();
            var rows = ContributingRows();

            // Act
            var wrong = new List<string>();
            foreach (var row in rows)
            {
                var workflow = workflows.FirstOrDefault(entry => entry.Name == row.Workflow);
                if (workflow.Text == null)
                {
                    continue;
                }

                var label = row.Workflow + (row.Job.Length > 0 ? " ▸ " + row.Job : string.Empty);
                var starts = SubscribedEvents(workflow.Text).Intersect(ContributorEvents, StringComparer.Ordinal)
                    .ToHashSet(StringComparer.Ordinal);
                var job = row.Job.Length == 0
                    ? null
                    : JobNames(workflow.Text).FirstOrDefault(key => Names(workflow.Text, row.Job, key));
                var gate = job == null ? string.Empty : JobLevelIf(AggregateBlock(workflow.Text, job)) ?? string.Empty;
                var named = EventNameReferencePattern.Matches(gate).Count;
                var equality = EventEqualityPattern.Match(gate);
                if (named > 1 || (named == 1 && !equality.Success))
                {
                    wrong.Add(label + " (its job-level if: reaches for github.event_name in a shape this "
                        + "does not read: " + gate.Trim() + ")");
                    continue;
                }

                if (named == 1)
                {
                    starts.IntersectWith(new[] { equality.Groups["event"].Value });
                }

                // Spelt as the column spells it only after any gate has narrowed the events: a gate names
                // the raw `push`, which intersects with nothing once the set holds `push (filtered)`.
                var spelt = starts.Select(name => name == "push" ? PushPhrase(workflow.Text) : name)
                    .ToHashSet(StringComparer.Ordinal);
                var claimed = TriggerPhrases
                    .Where(phrase => row.Trigger.Contains(phrase.Phrase, StringComparison.Ordinal))
                    .Select(phrase => phrase.Event)
                    .ToHashSet(StringComparer.Ordinal);
                if (claimed.Count == 0)
                {
                    wrong.Add(label + " (its trigger names no event this reads)");
                }
                else if (!claimed.SetEquals(spelt))
                {
                    wrong.Add(label + " (claims [" + string.Join("+", claimed.OrderBy(e => e, StringComparer.Ordinal))
                        + "], starts on [" + string.Join("+", spelt.OrderBy(e => e, StringComparer.Ordinal)) + "])");
                }
            }

            // Assert — the floor is on the rows for the reason
            // Given_EveryRowOfContributingsCiTable_When_TheWorkflowsAreRead_Then_ItNamesOneThatExists gives.
            Assert.That((rows.Count >= 2, string.Join("\n", wrong)), Is.EqualTo((true, string.Empty)));
        }

        // Read by the output the license check publishes rather than by that job's name, so renaming the
        // job leaves this matching and renaming the output reds every row whose license cell says required.
        private static readonly Regex LicenseGatePattern =
            new(@"needs\.[A-Za-z_][A-Za-z0-9_-]*\.outputs\.has_license", RegexOptions.Compiled);

        private static bool WaitsOnLicense(string workflow, string job) =>
            LicenseGatePattern.IsMatch(JobLevelIf(AggregateBlock(workflow, job)) ?? string.Empty);

        // GREEN_ON_BASE(characterization): the column agreed with the workflows on both sides. Nothing
        // derived it, which is what the branch changes — the first two columns were each found wrong the
        // moment they were derived, so this one is held before it drifts as well.
        [Test]
        public void Given_EveryRowOfContributingsCiTable_When_ItsUnityLicenseCellIsRead_Then_ItMatchesTheLicenseGate()
        {
            // Arrange — what marks a job as needing a license is its own `if:` waiting on the license
            // check's output, and a row naming a workflow rather than a job answers for every job in it.
            var workflows = Workflows();
            var rows = ContributingRows();

            // Act
            var wrong = new List<string>();
            var judged = 0;
            foreach (var row in rows)
            {
                var workflow = workflows.FirstOrDefault(entry => entry.Name == row.Workflow);
                if (workflow.Text == null)
                {
                    continue;
                }

                judged++;
                var label = row.Workflow + (row.Job.Length > 0 ? " ▸ " + row.Job : string.Empty);
                var waits = JobNames(workflow.Text)
                    .Where(key => row.Job.Length == 0 || Names(workflow.Text, row.Job, key))
                    .Any(key => WaitsOnLicense(workflow.Text, key));
                var cell = row.License.Replace("*", string.Empty).Split('(')[0].Trim();
                var claimed = string.Equals(cell, "required", StringComparison.OrdinalIgnoreCase);
                if (!claimed && !string.Equals(cell, "not required", StringComparison.OrdinalIgnoreCase))
                {
                    wrong.Add(label + " (its license cell says \"" + cell + "\", which this does not read)");
                }
                else if (claimed != waits)
                {
                    wrong.Add(label + (waits
                        ? " (waits on the license check and is not marked required)"
                        : " (marked required and nothing in it waits on the license check)"));
                }
            }

            // Assert — the floors are on what this judged rather than on what it was offered: a table read
            // whole but matched to no workflow reports nothing wrong, and so does a reader that found no
            // gate at all, which agrees exactly with a table rewritten to "not required" throughout.
            var gates = workflows.Sum(entry => JobNames(entry.Text).Count(key => WaitsOnLicense(entry.Text, key)));
            Assert.That((judged >= 2, gates >= 1, string.Join("\n", wrong)), Is.EqualTo((true, true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): the two namings already agree on both sides — every job key in
        // these workflows is plain, so the base's narrower pattern found them all, and both aggregates
        // spell `needs:` as a flow list, which the base's narrower reader took as well. What this holds is
        // the two patterns the branch widened, against being narrowed again by someone who has only the
        // passing cases to go on.
        [Test]
        public void Given_EveryJobARequiredAggregateNames_When_TheWorkflowIsEnumerated_Then_ItIsFound()
        {
            // Arrange — a job the enumerator loses is reported by the cases beside this one as a job that
            // does not exist, since the enumerator is their oracle for that. What none of them reds on is
            // the pair a real change makes: a job added in a spelling the enumerator misses, or one made
            // invisible and its row removed together. The aggregate's own `needs:` is a second naming of
            // the same set, written by hand in the workflow, and the two disagreeing is what says so.
            var required = RequiredWorkflows();

            // Act
            var missing = new List<string>();
            foreach (var (path, aggregate) in required)
            {
                var text = ReadWorkflow(path);
                var found = JobNames(text).ToHashSet(StringComparer.Ordinal);
                foreach (var job in NeedsOf(AggregateBlock(text, aggregate)))
                {
                    if (!found.Contains(job))
                    {
                        missing.Add(path + ":" + job);
                    }
                }
            }

            // Assert — the floor is on the workflows because that is what it can hold: an aggregate whose
            // needs list went unread names no job, nothing is missing from an enumeration that returned
            // nothing, and `RequiredWorkflows` never reads that list so its count does not move.
            Assert.That((required.Count >= 2, string.Join("\n", missing)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): this column happened to be right on both sides. Deriving the
        // first column found it wrong, which is the reason to hold this one before it drifts too.
        [Test]
        public void Given_EveryRequiredCheckAggregate_When_ContributingsCiTableIsRead_Then_ItsRowIsTheOneMarkedRequired()
        {
            // Arrange — the column a contributor acts on is the last one, and it was as unpinned as the
            // first. Which rows may carry a yes is the same question the fixture already answers to find
            // the aggregates, so it is asked once rather than curated twice. Matched through the same
            // Names as the cases above: the aggregate rows are the only ones spelt by display name today,
            // and spelling them by key instead is legal there, so a case that accepted only one spelling
            // would red on a table it had just been made consistent with.
            var aggregates = RequiredWorkflows()
                .Select(entry => (Text: ReadWorkflow(entry.Workflow), entry.Aggregate))
                .Select(entry => (Workflow: WorkflowDisplayName(entry.Text), entry.Text, entry.Aggregate))
                .ToList();

            // Act — every row, including one naming a workflow and no job: such a row can carry a yes too,
            // and nothing else would notice.
            var wrong = ContributingRows()
                .Where(row => aggregates.Any(entry =>
                    entry.Workflow == row.Workflow
                    && row.Job.Length > 0
                    && Names(entry.Text, row.Job, entry.Aggregate)) != row.Required)
                .Select(row => row.Workflow + (row.Job.Length > 0 ? " ▸ " + row.Job : string.Empty)
                    + (row.Required ? " (marked required)" : " (not marked required)"))
                .ToList();

            // Assert — the floor catches an aggregate set that read empty *and* a table with no yes left in
            // it, which agree with each other and describe a repository where nothing gates a merge. Either
            // alone reds through the message: the rows carrying a yes stop matching, or the aggregates stop
            // being matched.
            Assert.That((aggregates.Count >= 2, string.Join("\n", wrong)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): what this case asks did not change — only the reader it asks
        // through, which now takes a block sequence and a bare scalar as well as a flow list. Both
        // aggregates spell `needs:` as a flow list, so every reader answers the same here.
        [Test]
        public void Given_EveryJobInARequiredWorkflow_When_TheAggregateIsRead_Then_ItDependsOnThatJob()
        {
            // Arrange
            var aggregates = RequiredWorkflows();

            // Act
            var unwired = aggregates
                .SelectMany(entry => Unwired(entry.Workflow, entry.Aggregate, NeedsOf))
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
