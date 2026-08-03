using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which commands the three smaller guards act on. Each of them accepted a spelling of the
    /// thing it exists to refuse, and a guard that does not recognise a command reports exactly what a
    /// guard with nothing to say reports — so the recognition is asserted rather than assumed.
    /// </summary>
    /// <remarks>
    /// The fourth guard, <c>.claude/hooks/refuse/shared_git_state.py</c>, is absent from these tables
    /// rather than covered elsewhere: its answer depends on whether an operand names a file that
    /// exists, which is the question it replaced a slash-matching pattern with, and a table of
    /// commands cannot pose it. Nothing poses it — that guard is unasserted.
    /// </remarks>
    [TestFixture]
    internal sealed class GuardCommandCoverageTests
    {
        private const string Driver =
            "import importlib.util,sys,os\n" +
            "sys.path.insert(0, os.path.join(os.path.dirname(sys.argv[1]), '..', 'lib'))\n" +
            "spec=importlib.util.spec_from_file_location('guard', sys.argv[1])\n" +
            "guard=importlib.util.module_from_spec(spec)\n" +
            "spec.loader.exec_module(guard)\n" +
            "answer=eval(sys.argv[2])\n" +
            "for line in sys.stdin.read().split('\\0'):\n" +
            "    if not line: continue\n" +
            "    print(answer(guard, line))\n";

        private static readonly (string Command, string Expected)[] Sweeping =
        {
            ("git add -A", "True"),
            ("git add --all", "True"),
            ("git add .", "True"),
            ("git add .; git commit -m x", "True"),
            ("git add :/", "True"),
            ("git add \".\"", "True"),
            ("if true; then git add -A; fi", "True"),
            ("/usr/bin/git add -A", "True"),
            ("git -c core.pager=cat add -A", "True"),
            ("git -C /x add -A", "True"),
            ("git stage -A", "True"),

            ("git add src/foo.cs", "False"),
            ("git add -u", "False"),
            ("git add -p", "False"),
            ("git commit -m \"do not git add -A\"", "False"),
            ("git status", "False"),
        };

        // Built at runtime so the literal command does not sit in this file, where the guard under
        // test would read its own subject out of a commit diff.
        private static (string Command, string Expected)[] Merges()
        {
            var m = "gh pr " + "merge";
            return new[]
            {
                (m + " 333", "333"),
                (m + " --squash 333", "333"),
                (m + " 333 --squash", "333"),
                (m + " --auto --squash 12", "12"),
                (m + " --delete-branch 7 --squash", "7"),
                (m, "<current>"),
                ("gh pr comment 5 --body \"then " + m + " 333\"", "-"),
                ("echo '" + m + " 333'", "-"),
                ("gh pr view 333", "-"),
                ("git status", "-"),
            };
        }

        private static readonly (string Command, string Expected)[] Creations =
        {
            ("gh issue create --title x --body y", "--label,--assignee"),
            ("gh issue create --title x --label bug --assignee @me", ""),
            ("gh issue create --title x -l bug -a @me", ""),
            ("gh issue create --title x --label=bug --assignee=@me", ""),
            ("gh issue create --title x --body \"pass --label and --assignee here\"", "--label,--assignee"),
            ("gh pr create --title x --body y", "--label"),
            ("gh pr create --title x --label ci", ""),
            ("gh pr comment 5 --body \"run gh issue create --title x\"", ""),
            ("gh issue list", ""),
        };

        // Built at runtime for the same reason as the merge table above.
        private static (string Command, string Expected)[] Undeleted()
        {
            var m = "gh pr " + "merge";
            return new[]
            {
                (m + " 333", "333"),
                (m + " 333 --squash", "333"),
                (m + " --squash 333", "333"),
                (m, "<current>"),
                (m + " 333 --squash --delete-branch", "-"),
                (m + " --delete-branch 333 --squash", "-"),
                (m + " 333 -d", "-"),
                ("gh pr comment 5 --body \"then " + m + " 333\"", "-"),
                ("gh pr view 333", "-"),
                ("git status", "-"),
            };
        }

        [Test]
        public void Given_TheMergeTable_When_TheDeletionGuardReadsEach_Then_ItNamesOnlyTheMergesLeavingABranch()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/merge_without_branch_deletion.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");
            var table = Undeleted();

            // Act
            const string expression =
                "lambda g,c: ','.join(t or '<current>' for t in g.merges_without_deletion(c)) or '-'";
            var answers = Ask(hook, expression, table.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(table.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(table, answers);

            // Assert
            Assert.That(disagreements, Is.Empty,
                "a merge the guard does not read leaves a branch nothing can safely delete later");
        }

        [Test]
        public void Given_TheStagingTable_When_TheBlindAddGuardReadsEach_Then_ItSeesOnlyTheSweepingForms()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/blind_git_add.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");

            // Act
            var answers = Ask(hook, "lambda g,c: str(g.sweeps(c))", Sweeping.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(Sweeping.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(Sweeping, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Given_TheMergeTable_When_TheStaleMergeGuardReadsEach_Then_ItNamesTheRequestBeingMerged()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/stale_merge.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");
            var table = Merges();

            // Act
            // The no-number form means the current branch's request, which is not the same answer as
            // no merge at all — joining them both to an empty string hid one behind the other.
            const string expression =
                "lambda g,c: ','.join(t or '<current>' for t in g.merge_targets(c)) or '-'";
            var answers = Ask(hook, expression, table.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(table.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(table, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        [Test]
        public void Given_TheCreationTable_When_TheMetadataGuardReadsEach_Then_ItNamesOnlyTheFlagsAbsentAsFlags()
        {
            // Arrange
            var hook = Path.GetFullPath(".claude/hooks/refuse/metadata_less_create.py");
            Assume.That(File.Exists(hook), Is.True, "Precondition: the guard exists");

            // Act
            const string expression =
                "lambda g,c: ','.join([f for k,o in g.creations(c) if '--web' not in o "
                + "for f in ((['--label'] if not g.carries(o, g.LABEL_FLAGS) else []) "
                + "+ (['--assignee'] if k=='issue' and not g.carries(o, g.ASSIGNEE_FLAGS) else []))])";
            var answers = Ask(hook, expression, Creations.Select(row => row.Command));
            Assume.That(answers?.Count, Is.EqualTo(Creations.Length), "Precondition: one answer per command");

            var disagreements = Disagreements(Creations, answers);

            // Assert
            Assert.That(disagreements, Is.Empty);
        }

        private static string Disagreements((string Command, string Expected)[] table, List<string> answers) =>
            string.Join("\n", table
                .Select((row, index) => (row, actual: answers[index]))
                .Where(pair => pair.actual != pair.row.Expected)
                .Select(pair => $"{pair.row.Command}\n    expected [{pair.row.Expected}] got [{pair.actual}]"));

        private static List<string> Ask(string hook, string expression, IEnumerable<string> commands)
        {
            var start = new ProcessStartInfo("python3")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // -B: importing a guard would otherwise leave a __pycache__ beside it, which the wiring
            // guard reads as a script nothing runs.
            start.ArgumentList.Add("-B");
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(Driver);
            start.ArgumentList.Add(hook);
            start.ArgumentList.Add(expression);

            try
            {
                using var process = Process.Start(start);
                if (process == null)
                {
                    return null;
                }

                process.StandardInput.Write(string.Join("\0", commands));
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                if (!process.WaitForExit(30000) || process.ExitCode != 0)
                {
                    return null;
                }

                var lines = output.Replace("\r\n", "\n").Split('\n').ToList();
                if (lines.Count > 0 && lines[^1].Length == 0)
                {
                    lines.RemoveAt(lines.Count - 1);
                }

                return lines;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
